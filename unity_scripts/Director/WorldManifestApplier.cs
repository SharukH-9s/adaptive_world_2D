using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdaptiveWorld2D.World;
using AdaptiveWorld2D.Player;
using AdaptiveWorld2D.AI;
using AdaptiveWorld2D.Mission;

namespace AdaptiveWorld2D.Director
{
    public class WorldManifestApplier : MonoBehaviour
    {
        [Header("Target Boss Setup")]
        public GameObject targetPrefab;
        public GameObject currentTargetInstance;
        [Tooltip("Named anchor transforms for possible target locations: warehouse_interior, open_plaza, far_docks, fortified_building")]
        public List<LocationAnchor> locationAnchors = new List<LocationAnchor>();

        [Header("Guards Setup")]
        public GameObject guardPrefab;
        public List<Transform> guardSpawnPoints = new List<Transform>();
        private List<GameObject> _activeGuards = new List<GameObject>();
        private GameObject _extractionZone;     // Phase 4: extraction trigger

        [Header("Obstacles & Roadblocks")]
        public List<NamedBarricade> routeBarricades = new List<NamedBarricade>();
        public List<GameObject> drivableCars = new List<GameObject>();

        [Header("Environment & Weather")]
        public UnityEngine.Rendering.Universal.Light2D globalLight2D;
        public ParticleSystem rainParticles;
        public ParticleSystem fogParticles;

        [System.Serializable]
        public struct LocationAnchor
        {
            public string locationId;
            public Transform spawnTransform;
        }

        [System.Serializable]
        public struct NamedBarricade
        {
            public string routeId;
            public GameObject barricadeObject;
        }

        private void Start()
        {
            if (!IsMap2Scene())
            {
                // WorldManifestApplier is strictly for Map 2 (Mission 2).
                // Do not spawn guards or bosses in Map 1!
                return;
            }

            if (AdaptiveDirectorClient.Instance != null)
            {
                AdaptiveDirectorClient.Instance.OnManifestReceived += ApplyManifest;
            }

            // Delay by one frame so MissionManager.Start() finishes first
            StartCoroutine(ApplyManifestNextFrame());
        }

        private System.Collections.IEnumerator ApplyManifestNextFrame()
        {
            yield return null; // wait one frame

            if (AdaptiveDirectorClient.latestManifest != null)
            {
                Debug.Log("[WorldManifestApplier] Applying cached in-memory manifest on scene load.");
                ApplyManifest(AdaptiveDirectorClient.latestManifest);
            }
            else
            {
                // §2.3B: Try the on-disk cache before hitting the network
                // (survives domain reloads between Editor sessions)
                MissionManifest diskCached = AdaptiveDirectorClient.LoadCachedManifest();
                if (diskCached != null)
                {
                    Debug.Log("[WorldManifestApplier] Applying manifest loaded from disk cache.");
                    AdaptiveDirectorClient.latestManifest = diskCached;
                    ApplyManifest(diskCached);
                }
                else if (AdaptiveDirectorClient.Instance != null)
                {
                    Debug.Log("[WorldManifestApplier] No cached manifest — requesting fresh manifest for Map2 from Director...");
                    // OnManifestReceived event will automatically trigger ApplyManifest
                    AdaptiveDirectorClient.Instance.RequestMission2Manifest(42, null);
                }
                else
                {
                    Debug.LogWarning("[WorldManifestApplier] No Director Client available — applying default fallback target & guards.");
                    ApplyDefaultFallback();
                }
            }
        }

        private void ApplyDefaultFallback()
        {
            MissionManifest fallback = new MissionManifest
            {
                mission_id = 2,
                player_archetype = "GHOST",
                target = new TargetManifest
                {
                    location_id = "warehouse_interior",
                    location_label = "Warehouse Interior",
                    armor = "medium",
                    health = 100
                },
                guards = new GuardsManifest
                {
                    count = 6,
                    vision_range = 6.0f,
                    reaction_speed = 1.0f,
                    hearing_range = 8.0f,
                    armored = false
                },
                environment = new EnvironmentManifest
                {
                    ambient_light = 0.8f,
                    time_of_day = "day",
                    weather = "clear",
                    ambient_label = "Clear"
                },
                routes = new RoutesManifest
                {
                    cars_available = true,
                    blocked_routes = new List<string>()
                }
            };
            ApplyManifest(fallback);
        }

        private void OnDestroy()
        {
            if (AdaptiveDirectorClient.Instance != null)
            {
                AdaptiveDirectorClient.Instance.OnManifestReceived -= ApplyManifest;
            }
        }

        /// <summary>Returns true when the active scene is the Map 2 assassination mission.</summary>
        private static bool IsMap2Scene() =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                .IndexOf("Map2", StringComparison.OrdinalIgnoreCase) >= 0;

        public void ApplyManifest(MissionManifest manifest)
        {
            if (manifest == null) return;

            if (!IsMap2Scene()) return; // Safety guard: only apply manifest on Map 2

            Debug.Log($"[WorldManifestApplier] Applying Mission {manifest.mission_id} ({manifest.player_archetype}) to scene!");

            // Phase 4: sector-aware entity placement driven by room_graph
            RoomGraphManifest rg = manifest.room_graph;

            // Runtime Decoupling (§3.3A): Build the sector graph from the live room_graph manifest!
            if (rg != null)
            {
                SectorRuntimeBuilder.Build(rg);
            }

            try { ApplyTarget(manifest.target, rg); } catch (Exception ex) { Debug.LogException(ex); }
            try { ApplyGuards(manifest.guards, rg); } catch (Exception ex) { Debug.LogException(ex); }
            try { ApplyEnvironment(manifest.environment); } catch (Exception ex) { Debug.LogException(ex); }
            try { ApplyRoutes(manifest.routes); } catch (Exception ex) { Debug.LogException(ex); }
            try { ApplyTacticalProps(manifest.routes, rg); } catch (Exception ex) { Debug.LogException(ex); }
            try { ApplyPlayerSpawn(rg); } catch (Exception ex) { Debug.LogException(ex); }
            try { ApplyExtractionZone(rg); } catch (Exception ex) { Debug.LogException(ex); }

            // §5.3B: Scale guard vision for current weather after all guards are spawned
            try
            {
                if (manifest.environment != null)
                    ScaleGuardVisionForWeather(manifest.environment.visibility);
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        /// <summary>
        /// Phase 4: Spawns Viktor (boss target) at the target_sector center from room_graph.
        /// Falls back to TargetManifest coordinates/anchors if room_graph is absent.
        /// Then spawns 1-2 personal bodyguards flanking Viktor at ±1.5 units.
        /// </summary>
        private void ApplyTarget(TargetManifest t, RoomGraphManifest rg)
        {
            if (t == null) return;

            Vector3 spawnPos = Vector3.zero;
            bool foundPos    = false;

            // 1. Prefer room_graph target_sector center (most accurate for Phase 4+)
            if (rg != null && !string.IsNullOrEmpty(rg.target_sector))
            {
                var sector = rg.GetSector(rg.target_sector);
                if (sector != null)
                {
                    spawnPos = new Vector3(sector.center_x, sector.center_y, 0f);
                    foundPos = true;
                    Debug.Log($"[WorldManifestApplier] Target sector '{rg.target_sector}' resolved to center {spawnPos}.");
                }
            }

            // 2. Fallback: Inspector location anchors (legacy Map 2 fixed-location workflow)
            if (!foundPos)
            {
                foreach (var anchor in locationAnchors)
                {
                    if (anchor.locationId != null &&
                        anchor.locationId.Equals(t.location_id, StringComparison.OrdinalIgnoreCase) &&
                        anchor.spawnTransform != null)
                    {
                        spawnPos = anchor.spawnTransform.position;
                        foundPos = true;
                        break;
                    }
                }
            }

            // 3. Fallback: raw coordinates from TargetManifest (backend-supplied)
            if (!foundPos && t.coordinates != null && t.coordinates.Length >= 2)
            {
                spawnPos = new Vector3(t.coordinates[0], t.coordinates[1], 0f);
                foundPos = true;
            }

            // 4. Hard default
            if (!foundPos)
            {
                spawnPos = new Vector3(8f, 2f, 0f);
            }

            // Spawn Viktor
            if (targetPrefab == null)
                targetPrefab = Resources.Load<GameObject>("ViktorPrefab");

            if (currentTargetInstance == null)
            {
                currentTargetInstance = targetPrefab != null
                    ? Instantiate(targetPrefab, spawnPos, Quaternion.identity)
                    : CreateProceduralBoss(spawnPos, t.health);
            }
            else
            {
                currentTargetInstance.transform.position = spawnPos;
                currentTargetInstance.SetActive(true);
            }

            // Configure Viktor's combat stats
            if (currentTargetInstance != null)
            {
                var bossPatrol = currentTargetInstance.GetComponent<AdaptiveWorld2D.AI.EnemyPatrol>();
                if (bossPatrol != null)
                {
                    bossPatrol.maxHealth     = t.health;
                    bossPatrol.currentHealth = t.health;
                    bossPatrol.isTargetBoss  = true;
                    // Boss is slower and more deliberate — reduced patrol speed
                    bossPatrol.patrolSpeed = 1.5f;
                    bossPatrol.chaseSpeed  = 3.0f;
                }
                var bossDetect = currentTargetInstance.GetComponent<AdaptiveWorld2D.AI.EnemyDetection>();
                if (bossDetect != null)
                {
                    // Armor type affects armor color tint (visual only) and vision
                    bossDetect.visionRange = t.armor == "heavy" ? 8f : (t.armor == "medium" ? 7f : 6f);
                }
            }

            Debug.Log($"[WorldManifestApplier] Target Viktor ({t.location_label}) spawned at {spawnPos} " +
                      $"(HP: {t.health}, Armor: {t.armor})");

            // --- Phase 4: Spawn 1-2 personal bodyguards flanking Viktor ---
            // Bodyguards are drawn from the first entries of room_graph sector guard list
            // (if present), otherwise use GuardsManifest stats directly.
            SpawnViktorBodyguards(spawnPos, rg);
        }

        /// <summary>
        /// Spawns 1 or 2 bodyguards directly flanking Viktor at ±1.5 unit offsets.
        /// Their stats come from the target_sector's guard list in the room_graph (if
        /// present), otherwise defaults from the global guards manifest.
        /// </summary>
        private void SpawnViktorBodyguards(Vector3 viktorPos, RoomGraphManifest rg)
        {
            int bodyguardCount = 2; // Always spawn 2 bodyguards for Viktor
            float[] xOffsets   = { -1.8f, 1.8f };

            // Get guard stats from target sector's guard entities if available
            SectorConfig targetSectorCfg = null;
            if (rg != null && !string.IsNullOrEmpty(rg.target_sector))
                targetSectorCfg = rg.GetSector(rg.target_sector);

            for (int i = 0; i < bodyguardCount; i++)
            {
                Vector3 pos = viktorPos + new Vector3(xOffsets[i], 0f, 0f);

                // Check if there's a specific guard entity config for this bodyguard slot
                bool armored = false;
                if (targetSectorCfg != null && targetSectorCfg.guards != null &&
                    i < targetSectorCfg.guards.Count)
                {
                    var gc = targetSectorCfg.guards[i];
                    armored = gc.armored;
                    // Use the offset from the guard entity config if it defines one
                    if (gc.offset_x != 0f || gc.offset_y != 0f)
                        pos = new Vector3(targetSectorCfg.center_x + gc.offset_x,
                                          targetSectorCfg.center_y + gc.offset_y, 0f);
                }

                GameObject bodyguard = CreateProceduralGuard(pos, 900 + i);
                if (bodyguard == null) continue;

                bodyguard.name = $"Viktor_Bodyguard_{i:D2}";
                _activeGuards.Add(bodyguard);

                // Bodyguards are tougher and more alert than regular patrol guards
                var patrol = bodyguard.GetComponent<AdaptiveWorld2D.AI.EnemyPatrol>();
                if (patrol != null)
                {
                    patrol.maxHealth      = 80;
                    patrol.currentHealth  = 80;
                    patrol.patrolSpeed    = 1.5f;
                    patrol.chaseSpeed     = 4.5f;
                    patrol.damageResistance = armored ? 0.5f : 0.25f;
                    // Bodyguards patrol very close to Viktor — tight 1-unit radius
                    if (patrol.waypoints.Count >= 2)
                    {
                        patrol.waypoints[0].position = viktorPos + new Vector3(-1f, 0.5f, 0f);
                        patrol.waypoints[1].position = viktorPos + new Vector3( 1f, -0.5f, 0f);
                    }
                }

                var det = bodyguard.GetComponent<AdaptiveWorld2D.AI.EnemyDetection>();
                if (det != null)
                {
                    det.visionRange    = 8f;  // Bodyguards are hyper-vigilant
                    det.hearingRange   = 10f;
                    det.reactionSpeed  = 1.8f; // React faster than regular guards
                }

                // Tint bodyguards a darker crimson to visually distinguish from patrol guards
                var sr = bodyguard.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = new Color(0.7f, 0.1f, 0.1f);
            }

            Debug.Log($"[WorldManifestApplier] Spawned {bodyguardCount} personal bodyguards flanking Viktor.");
        }

        /// <summary>
        /// Phase 4: Distributes guards in pairs across non-target sectors.
        /// Each pair gets two waypoints within their assigned sector's bounds (±4 units
        /// from the sector center) so guards patrol the interior of their sector.
        /// Guard stats (vision, hearing, reaction, armor) come from GuardsManifest.
        /// Guards defined in room_graph SectorConfig.guards lists (per-sector entities)
        /// are also spawned with their exact offset positions.
        /// </summary>
        private void ApplyGuards(GuardsManifest g, RoomGraphManifest rg)
        {
            if (g == null) return;

            // Clear previous guards (bodyguards were already added — don't clear them yet;
            // they'll be added after Viktor spawns, so clear list first and re-add)
            foreach (var guard in _activeGuards)
            {
                if (guard != null) Destroy(guard);
            }
            _activeGuards.Clear();

            // Runtime fallback: try Resources if Inspector slot is empty
            if (guardPrefab == null)
            {
                guardPrefab = Resources.Load<GameObject>("GuardPrefab");
                if (guardPrefab == null)
                    Debug.LogWarning("[WorldManifestApplier] guardPrefab missing — using procedural fallback.");
            }

            // ─────────────────────────────────────────────────────────
            // Path A: room_graph present → sector-aware distribution
            // ─────────────────────────────────────────────────────────
            if (rg != null && rg.sectors != null && rg.sectors.Count == 9)
            {
                int spawned = 0;
                int maxGuards = Mathf.Min(g.count, 16);

                foreach (var sector in rg.sectors)
                {
                    // Skip the target sector — bodyguards (Viktor's escort) are spawned
                    // separately by SpawnViktorBodyguards() after ApplyTarget().
                    if (sector.id == rg.target_sector) continue;

                    if (spawned >= maxGuards) break;

                    // 1. Spawn any explicitly defined per-sector guard entities first
                    if (sector.guards != null && sector.guards.Count > 0)
                    {
                        foreach (var gc in sector.guards)
                        {
                            if (spawned >= maxGuards) break;
                            Vector3 pos = new Vector3(
                                sector.center_x + gc.offset_x,
                                sector.center_y + gc.offset_y, 0f);
                            SpawnSectorGuard(pos, sector, g, spawned, gc.armored, role: gc.role);
                            spawned++;
                        }
                    }
                    else
                    {
                        // 2. No explicit guards — auto-assign 1-2 guards per non-target sector
                        //    based on remaining guard budget, distributed evenly.
                        int nonTargetSectors = 8; // 9 total - 1 target
                        int guardsPerSector  = Mathf.Max(1, Mathf.CeilToInt((float)maxGuards / nonTargetSectors));
                        int toSpawnHere      = Mathf.Min(guardsPerSector, maxGuards - spawned);

                        for (int gi = 0; gi < toSpawnHere; gi++)
                        {
                            // Stagger within sector — left/right of center
                            float xOff = (gi == 0) ? -3.5f : 3.5f;
                            Vector3 pos = new Vector3(sector.center_x + xOff, sector.center_y, 0f);
                            SpawnSectorGuard(pos, sector, g, spawned, armored: g.armored, role: "patrol");
                            spawned++;
                        }
                    }
                }

                Debug.Log($"[WorldManifestApplier] Phase 4: Spawned {spawned} sector-distributed guards " +
                          $"(budget={maxGuards}, armored={g.armored}, vision={g.vision_range}).");
            }
            else
            {
                // ─────────────────────────────────────────────────────
                // Path B: No room_graph — legacy scatter fallback
                // ─────────────────────────────────────────────────────
                int countToSpawn = Mathf.Min(g.count, 12);
                for (int i = 0; i < countToSpawn; i++)
                {
                    Vector3 pos;
                    if (i < guardSpawnPoints.Count && guardSpawnPoints[i] != null)
                    {
                        pos = guardSpawnPoints[i].position;
                    }
                    else
                    {
                        float angle  = (360f / countToSpawn) * i;
                        float radius = 4f + (i % 3) * 2f;
                        pos = new Vector3(
                            Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                            Mathf.Sin(angle * Mathf.Deg2Rad) * radius, 0f);
                    }

                    GameObject guardObj = guardPrefab != null
                        ? Instantiate(guardPrefab, pos, Quaternion.identity)
                        : CreateProceduralGuard(pos, i);

                    if (guardObj == null) continue;
                    _activeGuards.Add(guardObj);

                    ApplyGuardStats(guardObj, g);
                }

                Debug.Log($"[WorldManifestApplier] Spawned {_activeGuards.Count}/{g.count} guards (legacy scatter).");
            }
        }

        /// <summary>Instantiates one guard at `pos`, configures waypoints within `sector` bounds,
        /// applies manifest stats, and registers the guard in _activeGuards.</summary>
        private void SpawnSectorGuard(Vector3 pos, SectorConfig sector, GuardsManifest g, int index,
                                      bool armored, string role)
        {
            GameObject guardObj = guardPrefab != null
                ? Instantiate(guardPrefab, pos, Quaternion.identity)
                : CreateProceduralGuard(pos, index);

            if (guardObj == null) return;
            _activeGuards.Add(guardObj);

            // Apply global guard manifest stats
            ApplyGuardStats(guardObj, g);

            // Stamp the sector id so AlertSystem can target this guard (§4.3B)
            var patrol = guardObj.GetComponent<AdaptiveWorld2D.AI.EnemyPatrol>();
            if (patrol != null)
            {
                patrol.sectorId = sector.id;

                if (armored)
                    patrol.damageResistance = 0.5f;

                // Re-assign waypoints to stay within this sector's 20×15 cell
                // Waypoints are set at ±4 units on the axis opposite to the sector row/col.
                if (patrol.waypoints.Count >= 2)
                {
                    float wpRange = role == "sentry" ? 1.5f : 4.0f;
                    patrol.waypoints[0].position = new Vector3(
                        sector.center_x - wpRange, sector.center_y + (index % 2 == 0 ? 1.5f : -1.5f), 0f);
                    patrol.waypoints[1].position = new Vector3(
                        sector.center_x + wpRange, sector.center_y - (index % 2 == 0 ? 1.5f : -1.5f), 0f);
                }
            }
        }

        /// <summary>Applies vision/hearing/reaction/armor stats from GuardsManifest to a guard GameObject.</summary>
        private static void ApplyGuardStats(GameObject guardObj, GuardsManifest g)
        {
            var detection = guardObj.GetComponent<AdaptiveWorld2D.AI.EnemyDetection>();
            if (detection != null)
            {
                // Use SetBaseVisionRange so weather scaling always starts from the
                // manifest-driven value rather than the Unity Inspector default.
                detection.SetBaseVisionRange(g.vision_range);
                detection.reactionSpeed = g.reaction_speed;
                detection.hearingRange  = g.hearing_range;
            }

            var patrol = guardObj.GetComponent<AdaptiveWorld2D.AI.EnemyPatrol>();
            if (patrol != null && g.armored)
                patrol.damageResistance = 0.5f;
        }

        /// <summary>Builds a fully functional guard at runtime — no prefab needed.</summary>
        private GameObject CreateProceduralGuard(Vector3 pos, int index)
        {
            GameObject go = new GameObject($"Guard_{index:D2}");
            go.transform.position = pos;
            go.tag   = "Enemy";
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            go.layer = enemyLayer >= 0 ? enemyLayer : 0;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = CreateCircleSprite(0.35f, new Color(0.85f, 0.25f, 0.25f));
            sr.sortingOrder = 5;

            Rigidbody2D rb    = go.AddComponent<Rigidbody2D>();
            rb.gravityScale   = 0f;
            rb.freezeRotation = true;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.35f;

            // Patrol waypoints
            GameObject wp0 = new GameObject("Waypoint_0");
            wp0.transform.SetParent(go.transform);
            wp0.transform.localPosition = new Vector3(-2f, 0f, 0f);
            GameObject wp1 = new GameObject("Waypoint_1");
            wp1.transform.SetParent(go.transform);
            wp1.transform.localPosition = new Vector3( 2f, 0f, 0f);

            var patrol = go.AddComponent<AdaptiveWorld2D.AI.EnemyPatrol>();
            patrol.patrolSpeed = 2f;
            patrol.chaseSpeed  = 4f;
            patrol.maxHealth   = 60;
            patrol.waypoints.Add(wp0.transform);
            patrol.waypoints.Add(wp1.transform);

            var det = go.AddComponent<AdaptiveWorld2D.AI.EnemyDetection>();
            det.visionRange   = 6f;
            // Phase 4 fix: include Interactable so cars/barriers block guard vision
            det.obstacleLayer = LayerMask.GetMask("Building", "Interactable");
            det.playerLayer   = LayerMask.GetMask("Default");

            return go;
        }

        /// <summary>Builds Viktor the target boss at runtime if ViktorPrefab is missing.</summary>
        private GameObject CreateProceduralBoss(Vector3 pos, int health)
        {
            GameObject go = new GameObject("Viktor_Boss_Target");
            go.transform.position = pos;
            go.tag   = "Enemy";
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            go.layer = enemyLayer >= 0 ? enemyLayer : 0;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = CreateCircleSprite(0.45f, new Color(1f, 0.2f, 0.2f));
            sr.sortingOrder = 6;

            Rigidbody2D rb    = go.AddComponent<Rigidbody2D>();
            rb.gravityScale   = 0f;
            rb.freezeRotation = true;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.45f;

            var patrol = go.AddComponent<AdaptiveWorld2D.AI.EnemyPatrol>();
            patrol.patrolSpeed  = 1.5f;
            patrol.chaseSpeed   = 3.5f;
            patrol.maxHealth    = health > 0 ? health : 100;
            patrol.currentHealth = patrol.maxHealth;
            patrol.isTargetBoss = true;

            var det = go.AddComponent<AdaptiveWorld2D.AI.EnemyDetection>();
            det.visionRange   = 7f;
            // Phase 4 fix: include Interactable so cars/barriers block boss vision
            det.obstacleLayer = LayerMask.GetMask("Building", "Interactable");
            det.playerLayer   = LayerMask.GetMask("Default");

            return go;
        }

        private static Sprite CreateCircleSprite(float radius, Color color)
        {
            int size   = 32;
            Texture2D tex = new Texture2D(size, size);
            float ctr  = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), new Vector2(ctr, ctr)) <= ctr ? color : Color.clear);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / (radius * 2f));
        }

        private void ApplyEnvironment(EnvironmentManifest env)
        {
            if (env == null) return;

            // 1. Camera Background Tint based on Time of Day
            if (Camera.main != null)
            {
                if (env.time_of_day.Contains("night"))
                    Camera.main.backgroundColor = new Color(0.04f, 0.06f, 0.12f); // Deep Midnight Blue
                else if (env.time_of_day.Contains("dusk"))
                    Camera.main.backgroundColor = new Color(0.24f, 0.12f, 0.08f); // Sunset Amber
                else
                    Camera.main.backgroundColor = new Color(0.12f, 0.13f, 0.15f); // Crisp Asphalt Gray
            }

            // 2. Global Light 2D adjustment (if project uses 2D URP lighting)
            if (globalLight2D == null)
            {
#if UNITY_2023_1_OR_NEWER
                globalLight2D = FindAnyObjectByType<UnityEngine.Rendering.Universal.Light2D>();
#else
                globalLight2D = FindObjectOfType<UnityEngine.Rendering.Universal.Light2D>();
#endif
            }
            if (globalLight2D != null)
            {
                globalLight2D.intensity = Mathf.Clamp(env.ambient_light, 0.15f, 1.2f);
                if (env.time_of_day.Contains("night"))
                    globalLight2D.color = new Color(0.2f, 0.25f, 0.45f);
                else if (env.time_of_day.Contains("dusk"))
                    globalLight2D.color = new Color(0.85f, 0.55f, 0.35f);
                else
                    globalLight2D.color = Color.white;
            }

            // 3. Screen-Space Atmospheric Color Filter (instant dramatic lighting)
            EnsureAtmosphereOverlay(env);

            // 4. Rain Particles
            if (env.weather.Contains("rain"))
            {
                EnsureRainSystem();
            }
            else if (rainParticles != null && rainParticles.isPlaying)
            {
                rainParticles.Stop();
            }

            // 5. Fog Particles
            if (env.weather.Contains("fog"))
            {
                EnsureFogSystem();
            }
            else if (fogParticles != null && fogParticles.isPlaying)
            {
                fogParticles.Stop();
            }

            // 6. Modulate streetlights based on ambient light & time of day (Phase 5 §5.3A)
#if UNITY_2023_1_OR_NEWER
            StreetlightController[] streetlights = FindObjectsByType<StreetlightController>();
#else
            StreetlightController[] streetlights = FindObjectsOfType<StreetlightController>();
#endif
            foreach (var sl in streetlights)
            {
                sl.SetEnvironmentLighting(env.ambient_light, env.time_of_day);
            }

            Debug.Log($"[WorldManifestApplier] Environment Applied: {env.time_of_day.ToUpper()} | Weather: {env.weather.ToUpper()} | Visibility: {env.visibility:F2}");
        }

        /// <summary>
        /// Scales the visionRange of every spawned guard/boss by the manifest environment
        /// visibility value (§5.3B). Called after all guards are spawned so the pool is complete.
        ///
        /// EffectiveVisionRange = baseVisionRange × visibility
        /// e.g. heavy fog (visibility=0.5) drops a 6.0-unit cone to 3.0 units.
        /// </summary>
        private void ScaleGuardVisionForWeather(float visibility)
        {
            if (Mathf.Approximately(visibility, 1.0f)) return; // No scaling needed in clear conditions

            int scaled = 0;
            foreach (var guardObj in _activeGuards)
            {
                if (guardObj == null) continue;
                var det = guardObj.GetComponent<AdaptiveWorld2D.AI.EnemyDetection>();
                if (det != null)
                {
                    det.SetWeatherVisibility(visibility);
                    scaled++;
                }
            }

            // Also scale the boss
            if (currentTargetInstance != null)
            {
                var det = currentTargetInstance.GetComponent<AdaptiveWorld2D.AI.EnemyDetection>();
                if (det != null)
                {
                    det.SetWeatherVisibility(visibility);
                    scaled++;
                }
            }

            Debug.Log($"[WorldManifestApplier] Weather vision scaling: {scaled} entity/entities scaled to {visibility:P0} visibility.");
        }

        private void EnsureAtmosphereOverlay(EnvironmentManifest env)
        {
#if UNITY_2023_1_OR_NEWER
            var canvas = FindAnyObjectByType<Canvas>();
#else
            var canvas = FindObjectOfType<Canvas>();
#endif
            if (canvas == null) return;

            Transform existing = canvas.transform.Find("AtmosphereOverlay");
            GameObject overlayGO = existing != null ? existing.gameObject : new GameObject("AtmosphereOverlay");
            if (existing == null)
            {
                overlayGO.transform.SetParent(canvas.transform, false);
                overlayGO.transform.SetAsFirstSibling(); // render behind HUD text
                var rect = overlayGO.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var img = overlayGO.GetComponent<Image>() ?? overlayGO.AddComponent<Image>();
            img.raycastTarget = false; // do not block clicks

            if (env.weather.Contains("fog"))
            {
                img.color = new Color(0.80f, 0.85f, 0.90f, 0.28f); // Soft misty fog veil
            }
            else if (env.weather.Contains("rain") || env.time_of_day.Contains("night"))
            {
                img.color = new Color(0.04f, 0.08f, 0.22f, 0.35f); // Midnight blue atmospheric shadow
            }
            else if (env.time_of_day.Contains("dusk"))
            {
                img.color = new Color(0.85f, 0.40f, 0.12f, 0.20f); // Warm twilight amber
            }
            else
            {
                img.color = Color.clear;
            }
        }

        private void EnsureRainSystem()
        {
            if (rainParticles == null)
            {
                var existing = GameObject.Find("Weather_Rain");
                if (existing != null)
                {
                    rainParticles = existing.GetComponent<ParticleSystem>();
                }
                else if (Camera.main != null)
                {
                    GameObject rainGO = new GameObject("Weather_Rain");
                    rainGO.transform.SetParent(Camera.main.transform, false);
                    rainGO.transform.localPosition = new Vector3(0, 7f, 10f);

                    rainParticles = rainGO.AddComponent<ParticleSystem>();
                    var main = rainParticles.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startSpeed = 16f;
                    main.startLifetime = 1.0f;
                    main.startSize = 0.08f;
                    main.startColor = new Color(0.7f, 0.85f, 1.0f, 0.7f);
                    main.maxParticles = 600;

                    var emission = rainParticles.emission;
                    emission.rateOverTime = 300f;

                    var shape = rainParticles.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(26f, 16f, 1f);

                    var rend = rainGO.GetComponent<ParticleSystemRenderer>();
                    rend.sortingOrder = 25;
                }
            }

            if (rainParticles != null && !rainParticles.isPlaying)
            {
                rainParticles.Play();
            }
        }

        private void EnsureFogSystem()
        {
            if (fogParticles == null)
            {
                var existing = GameObject.Find("Weather_Fog");
                if (existing != null)
                {
                    fogParticles = existing.GetComponent<ParticleSystem>();
                }
                else if (Camera.main != null)
                {
                    GameObject fogGO = new GameObject("Weather_Fog");
                    fogGO.transform.SetParent(Camera.main.transform, false);
                    fogGO.transform.localPosition = new Vector3(0, 0, 10f);

                    fogParticles = fogGO.AddComponent<ParticleSystem>();
                    var main = fogParticles.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startSpeed = 0.8f;
                    main.startLifetime = 4.0f;
                    main.startSize = 3.5f;
                    main.startColor = new Color(0.85f, 0.9f, 0.95f, 0.18f);
                    main.maxParticles = 80;

                    var emission = fogParticles.emission;
                    emission.rateOverTime = 20f;

                    var shape = fogParticles.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(25f, 18f, 1f);

                    var rend = fogGO.GetComponent<ParticleSystemRenderer>();
                    rend.sortingOrder = 24;
                }
            }

            if (fogParticles != null && !fogParticles.isPlaying)
            {
                fogParticles.Play();
            }
        }

        private void ApplyRoutes(RoutesManifest routes)
        {
            if (routes == null) return;

            // Activate roadblocks
            foreach (var item in routeBarricades)
            {
                if (item.barricadeObject == null) continue;
                bool isBlocked = routes.blocked_routes != null && routes.blocked_routes.Contains(item.routeId);
                item.barricadeObject.SetActive(isBlocked);
            }

            // Car availability
            if (!routes.cars_available)
            {
                foreach (var car in drivableCars)
                {
                    if (car != null) car.SetActive(false);
                }
            }
        }

        // ═════════════════════════════════════════════════════════
        //  PHASE 4: PLAYER SPAWN + EXTRACTION ZONE + TACTICAL PROPS
        // ═════════════════════════════════════════════════════════

        /// <summary>
        /// Repositions the Player GameObject to the center of the player_spawn_sector.
        /// Only moves the player when room_graph is available and has a valid spawn sector.
        /// </summary>
        private void ApplyPlayerSpawn(RoomGraphManifest rg)
        {
            if (rg == null || string.IsNullOrEmpty(rg.player_spawn_sector)) return;

            var sector = rg.GetSector(rg.player_spawn_sector);
            if (sector == null) return;

            Vector3 spawnCenter = new Vector3(sector.center_x, sector.center_y, 0f);
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                player = CreateProceduralPlayer(spawnCenter);
            }
            else
            {
                player.transform.position = spawnCenter;
                var rb = player.GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = Vector2.zero;
            }

            Debug.Log($"[WorldManifestApplier] Player repositioned to spawn sector '{rg.player_spawn_sector}' at {spawnCenter}.");
        }

        /// <summary>
        /// Creates and manages the extraction zone (MissionZoneTrigger.ZoneType.MissionEnd) at the
        /// center of the extraction_sector. The trigger GameObject starts DISABLED and is only
        /// enabled once MissionManager.missionCompleted == true (i.e., after Viktor is killed).
        /// A coroutine polls for completion and enables the zone.
        /// </summary>
        private void ApplyExtractionZone(RoomGraphManifest rg)
        {
            if (rg == null || string.IsNullOrEmpty(rg.extraction_sector)) return;

            var sector = rg.GetSector(rg.extraction_sector);
            if (sector == null) return;

            // Destroy any previously placed extraction zone from a prior manifest apply
            if (_extractionZone != null) Destroy(_extractionZone);

            Vector3 center = new Vector3(sector.center_x, sector.center_y, 0f);

            // Build the extraction zone GameObject
            _extractionZone = new GameObject("ExtractionZone_Map2");
            _extractionZone.transform.position = center;

            // Visual indicator — a green glowing circle on the ground
            var sr = _extractionZone.AddComponent<SpriteRenderer>();
            sr.sprite       = CreateCircleSprite(1.8f, new Color(0.0f, 1.0f, 0.4f, 0.55f));
            sr.sortingOrder = -1; // Render below player/enemies

            // Trigger collider (large enough to walk into)
            var col = _extractionZone.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius    = 2.0f;

            // MissionZoneTrigger component
            var trigger = _extractionZone.AddComponent<AdaptiveWorld2D.Mission.MissionZoneTrigger>();
            trigger.zoneType             = AdaptiveWorld2D.Mission.MissionZoneTrigger.ZoneType.MissionEnd;
            trigger.zoneName             = "Extraction Point Alpha";
            trigger.requireInteractButton = false;
            trigger.triggerOnce          = true;

            // Start LOCKED — only unlocked after missionCompleted
            _extractionZone.SetActive(false);

            // Start polling coroutine to unlock the zone when Viktor dies
            StartCoroutine(WaitAndEnableExtractionZone());

            Debug.Log($"[WorldManifestApplier] Extraction zone placed at sector '{rg.extraction_sector}' ({center}) — LOCKED until target eliminated.");
        }

        /// <summary>
        /// Polls MissionManager.missionCompleted every 0.5 seconds and enables the
        /// extraction zone GameObject when the mission becomes completed.
        /// Also spawns a HUD compass arrow pointing toward the extraction zone (§4.3E).
        /// </summary>
        private System.Collections.IEnumerator WaitAndEnableExtractionZone()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                yield return wait;

                if (_extractionZone == null) yield break;

                if (AdaptiveWorld2D.Mission.MissionManager.Instance != null &&
                    AdaptiveWorld2D.Mission.MissionManager.Instance.missionCompleted)
                {
                    _extractionZone.SetActive(true);

                    // Animate unlock: flash the extraction zone sprite bright green
                    var sr = _extractionZone.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        sr.color = new Color(0.0f, 1.0f, 0.4f, 0.9f);
                    }

                    Debug.Log("[WorldManifestApplier] Extraction zone UNLOCKED! Viktor has been eliminated.");

                    // Spawn the HUD extraction compass pointer (§4.3E)
                    SpawnExtractionCompass(_extractionZone.transform.position);

                    yield break;
                }
            }
        }

        /// <summary>
        /// Creates an on-screen UI arrow that continuously points from the player toward
        /// the extraction zone. The arrow is clamped to a circle near the screen edge
        /// and disappears once the player enters the extraction sector (§4.3E).
        /// </summary>
        private void SpawnExtractionCompass(Vector3 extractionWorldPos)
        {
#if UNITY_2023_1_OR_NEWER
            var canvas = FindAnyObjectByType<Canvas>();
#else
            var canvas = FindObjectOfType<Canvas>();
#endif
            if (canvas == null)
            {
                Debug.LogWarning("[WorldManifestApplier] No Canvas found — cannot spawn extraction compass.");
                return;
            }

            // Create the compass container
            GameObject compassGO = new GameObject("ExtractionCompass");
            compassGO.transform.SetParent(canvas.transform, false);

            // Arrow image (triangle pointing up, rotated toward target)
            var rt = compassGO.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(28f, 40f);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);

            var img = compassGO.AddComponent<Image>();
            img.color = new Color(0.0f, 1.0f, 0.35f, 0.9f);
            img.sprite = CreateArrowSprite();
            img.raycastTarget = false;

            // Label
            GameObject labelGO = new GameObject("CompassLabel");
            labelGO.transform.SetParent(compassGO.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.sizeDelta = new Vector2(90f, 18f);
            labelRT.anchoredPosition = new Vector2(0f, -22f);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = "EXTRACT";
            labelText.fontSize = 10;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = new Color(0.0f, 1.0f, 0.35f, 0.9f);
            labelText.alignment = TextAnchor.MiddleCenter;

            // Attach the tracker behaviour
            var tracker = compassGO.AddComponent<ExtractionCompassTracker>();
            tracker.extractionWorldPos = extractionWorldPos;
            tracker.extractionZone     = _extractionZone;
            tracker.compassRadius      = Mathf.Min(Screen.width, Screen.height) * 0.38f;

            Debug.Log("[WorldManifestApplier] Extraction compass spawned.");
        }

        /// <summary>Creates a simple upward-pointing triangle sprite for the compass arrow.</summary>
        private static Sprite CreateArrowSprite()
        {
            int w = 28, h = 40;
            Texture2D tex = new Texture2D(w, h);
            Color clear = Color.clear;
            Color white = Color.white;

            for (int y = 0; y < h; y++)
            {
                // Triangle: width narrows from base (y=0) to tip (y=h-1)
                float halfWidth = (w * 0.5f) * (1f - (float)y / (h - 1));
                int left  = Mathf.RoundToInt(w * 0.5f - halfWidth);
                int right = Mathf.RoundToInt(w * 0.5f + halfWidth);
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, (x >= left && x <= right) ? white : clear);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
        }

        /// <summary>
        /// Phase 4: Spawns tactical props (fuel barrels, ammo caches, and burnable cars)
        /// in their semantically correct sectors, driven by the routes manifest and room_graph.
        ///
        /// Fuel barrels and ammo caches are placed in INDUSTRIAL_DEPOT and WAREHOUSE_INTERIOR
        /// sectors so players never suffer weapon starvation (§4.3C).
        /// Burnable cars are placed in URBAN_HIGHWAY and FORTIFIED_PLAZA sectors if
        /// routes.cars_available is true.
        /// </summary>
        private void ApplyTacticalProps(RoutesManifest routes, RoomGraphManifest rg)
        {
            if (rg == null || rg.sectors == null) return;

            bool carsAllowed = routes != null && routes.cars_available;
            int barrelCount  = 0;
            int carCount     = 0;
            int ammoCount    = 0;

            foreach (var sector in rg.sectors)
            {
                Vector3 center = new Vector3(sector.center_x, sector.center_y, 0f);

                switch (sector.module)
                {
                    // ─ Fuel barrels + ammo caches in industrial/warehouse sectors ─────────
                    case "INDUSTRIAL_DEPOT":
                    case "WAREHOUSE_INTERIOR":
                        // Spawn 2 fuel barrels per qualifying sector, offset from center
                        Vector3[] barrelOffsets = { new Vector3(-3f, 2f, 0f), new Vector3(3f, -2f, 0f) };
                        foreach (var offset in barrelOffsets)
                        {
                            SpawnFuelBarrel(center + offset, $"FuelBarrel_{sector.id}_{barrelCount}");
                            barrelCount++;
                        }
                        // Spawn 1 ammo cache per qualifying sector (§4.3C) — prevents weapon starvation
                        SpawnAmmoCache(center + new Vector3(0f, -3f, 0f), $"AmmoCache_{sector.id}_{ammoCount}");
                        ammoCount++;
                        break;

                    // ─ Burnable cars in road/plaza sectors (if cars are available) ───────
                    case "URBAN_HIGHWAY":
                    case "FORTIFIED_PLAZA":
                        if (carsAllowed)
                        {
                            Vector3 carOffset = new Vector3((carCount % 2 == 0) ? -4f : 4f, 1.5f, 0f);
                            SpawnBurnableCar(center + carOffset, $"Car_{sector.id}_{carCount}");
                            carCount++;
                        }
                        break;
                }
            }

            if (barrelCount > 0 || carCount > 0 || ammoCount > 0)
            {
                Debug.Log($"[WorldManifestApplier] Tactical props: {barrelCount} fuel barrels, " +
                          $"{carCount} burnable cars, {ammoCount} ammo caches (cars_available={carsAllowed}).");
            }
        }

        /// <summary>Spawns a procedural fuel barrel (DestructibleWall, 20 HP) at `pos`.</summary>
        private static void SpawnFuelBarrel(Vector3 pos, string id)
        {
            GameObject go = new GameObject(id);
            go.transform.position = pos;
            int interactLayer = LayerMask.NameToLayer("Interactable");
            go.layer = interactLayer >= 0 ? interactLayer : 0;

            // Barrel visual — small orange cylinder silhouette (circle approximation)
            var sr    = go.AddComponent<SpriteRenderer>();
            Texture2D tex = new Texture2D(16, 16);
            float ctr = 8f;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), new Vector2(ctr, ctr)) <= ctr
                        ? new Color(0.85f, 0.45f, 0.1f) : Color.clear);
            tex.Apply();
            sr.sprite       = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
            sr.sortingOrder = 2;

            var col    = go.AddComponent<BoxCollider2D>();
            col.size   = new Vector2(0.5f, 0.5f);

            var wall       = go.AddComponent<AdaptiveWorld2D.World.DestructibleWall>();
            wall.wallId    = id;
            wall.maxHealth = 20;    // Barrels pop easily
            wall.currentHealth = 20;
        }

        /// <summary>Spawns a procedural burnable car (BurnableCar) at `pos`.</summary>
        private static void SpawnBurnableCar(Vector3 pos, string id)
        {
            GameObject go = new GameObject(id);
            go.transform.position = pos;
            go.tag = "Vehicle";
            int interactLayer = LayerMask.NameToLayer("Interactable");
            go.layer = interactLayer >= 0 ? interactLayer : 0;

            // Car visual — dark grey box silhouette
            var sr = go.AddComponent<SpriteRenderer>();
            Texture2D tex = new Texture2D(32, 16);
            Color carColor = new Color(0.25f, 0.25f, 0.3f);
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 32; x++)
                    tex.SetPixel(x, y, carColor);
            tex.Apply();
            sr.sprite       = Sprite.Create(tex, new Rect(0, 0, 32, 16), new Vector2(0.5f, 0.5f), 16f);
            sr.sortingOrder = 3;

            var col         = go.AddComponent<BoxCollider2D>();
            col.size        = new Vector2(2.0f, 1.0f);

            var rb          = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType     = RigidbodyType2D.Kinematic; // Cars don't drift

            var car         = go.AddComponent<AdaptiveWorld2D.World.BurnableCar>();
            car.carId       = id;
            car.carType     = "SEDAN";
        }

        /// <summary>
        /// Spawns a tactical ammo cache at `pos` (§4.3C).
        /// A small bright-green locker that gives +12 pistol ammo when the player walks into its trigger.
        /// </summary>
        private static void SpawnAmmoCache(Vector3 pos, string id)
        {
            GameObject go = new GameObject(id);
            go.transform.position = pos;
            SafeSetTag(go, "AmmoPickup");

            // Visual: small bright-green crate with "AMMO" silhouette
            var sr = go.AddComponent<SpriteRenderer>();
            Texture2D tex = new Texture2D(12, 12);
            Color ammoColor = new Color(0.1f, 0.85f, 0.25f);
            Color crossColor = new Color(0.05f, 0.45f, 0.1f);
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 12; x++)
                {
                    bool isCross = (x == 5 || x == 6) || (y == 5 || y == 6);
                    tex.SetPixel(x, y, isCross ? crossColor : ammoColor);
                }
            tex.Apply();
            sr.sprite       = Sprite.Create(tex, new Rect(0, 0, 12, 12), new Vector2(0.5f, 0.5f), 12f);
            sr.sortingOrder = 3;

            // Trigger collider
            var col       = go.AddComponent<BoxCollider2D>();
            col.size      = new Vector2(0.75f, 0.75f);
            col.isTrigger = true;

            // Pickup component — handles OnTriggerEnter2D and grants ammo
            go.AddComponent<AmmoCacheTrigger>();
        }

        /// <summary>
        /// Runtime-compatible white sprite generator (�3.3A). Works in both Editor and
        /// standalone builds. Cached statically to avoid repeated texture allocations.
        /// Shared with ProceduralSectorBuilder so wall/ground quads work at runtime.
        /// </summary>
        private static Sprite _universalWhiteSprite;
        public static Sprite GetUniversalWhiteSprite()
        {
            if (_universalWhiteSprite == null)
            {
                Texture2D tex = new Texture2D(2, 2);
                tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
                tex.Apply();
                _universalWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 1f);
            }
            return _universalWhiteSprite;
        }

        private static void SafeSetTag(GameObject go, string tag)
        {
            try { go.tag = tag; }
            catch { /* Tag not registered in TagManager, leave untagged without crashing */ }
        }

        // ═════════════════════════════════════════════════════════
        //  MAP 2 RUNTIME SELF-BOOTSTRAPPER
        // ═════════════════════════════════════════════════════════

        /// <summary>
        /// Procedurally creates a fully functional Player in Map 2 if not present in the scene.
        /// </summary>
        public static GameObject CreateProceduralPlayer(Vector3 pos)
        {
            GameObject player = new GameObject("Player");
            player.transform.position = pos;
            player.tag = "Player";

            SpriteRenderer sr = player.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite(0.35f, new Color(0.2f, 0.6f, 1f));
            sr.sortingOrder = 5;

            Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D col = player.AddComponent<CircleCollider2D>();
            col.radius = 0.35f;

            var ctrl = player.AddComponent<PlayerController>();
            ctrl.walkSpeed = 3.5f;
            ctrl.runSpeed = 6.0f;
            ctrl.driveSpeed = 12.0f;
            ctrl.roadLayer = LayerMask.GetMask("Road");
            ctrl.alleyLayer = LayerMask.GetMask("Alley");
            ctrl.buildingLayer = LayerMask.GetMask("Building");

            var combat = player.AddComponent<PlayerCombat>();
            combat.enemyLayer = LayerMask.GetMask("Enemy");
            combat.hasPistol = true;
            combat.pistolAmmo = 24;
            combat.meleeRange = 1.5f;
            combat.meleeDamage = 40;
            combat.gunRange = 15f;
            combat.gunDamage = 35;
            combat.fireRate = 0.25f;

            GameObject fp = new GameObject("FirePoint");
            fp.transform.SetParent(player.transform);
            fp.transform.localPosition = new Vector3(0.2f, 0.4f, 0f);
            combat.firePoint = fp.transform;

            var interaction = player.AddComponent<PlayerInteraction>();
            interaction.interactRadius = 2.0f;
            interaction.interactableLayer = LayerMask.GetMask("Interactable");

#if UNITY_2023_1_OR_NEWER
            var mm = FindAnyObjectByType<MissionManager>();
#else
            var mm = FindObjectOfType<MissionManager>();
#endif
            if (mm != null)
            {
                mm.playerCombat = combat;
                mm.playerController = ctrl;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                var camFollow = cam.GetComponent<CameraFollow2D>() ?? cam.gameObject.AddComponent<CameraFollow2D>();
                camFollow.target = player.transform;
            }

            Debug.Log($"[WorldManifestApplier] Procedural Player created and wired at {pos}.");
            return player;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterSceneLoadHook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoadedStatic;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoadedStatic;

            CheckMap2Bootstrapping(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        private static void OnSceneLoadedStatic(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            CheckMap2Bootstrapping(scene);
        }

        private static void CheckMap2Bootstrapping(UnityEngine.SceneManagement.Scene scene)
        {
            if (!Application.isPlaying) return;
            if (string.IsNullOrEmpty(scene.name) || scene.name.IndexOf("Map2", StringComparison.OrdinalIgnoreCase) < 0) return;

#if UNITY_2023_1_OR_NEWER
            var existingApplier = UnityEngine.Object.FindAnyObjectByType<WorldManifestApplier>();
#else
            var existingApplier = UnityEngine.Object.FindObjectOfType<WorldManifestApplier>();
#endif
            if (existingApplier != null) return; // Scene already has GameManager & WorldManifestApplier

            Debug.Log("[WorldManifestApplier] Map 2 loaded without GameManager/WorldManifestApplier — auto-bootstrapping at runtime!");

            // 1. Create GameManager
            GameObject gm = new GameObject("GameManager");
            var dirClient = gm.AddComponent<AdaptiveDirectorClient>();
            dirClient.apiBaseUrl = "http://127.0.0.1:8001";
            dirClient.flushInterval = 2.0f;
            dirClient.autoFlush = true;

            gm.AddComponent<TelemetryReporter>();
            var applier = gm.AddComponent<WorldManifestApplier>();
            var mm = gm.AddComponent<MissionManager>();

            // 2. Camera setup
            Camera cam = Camera.main;
            if (cam != null)
            {
                var camFollow = cam.GetComponent<CameraFollow2D>() ?? cam.gameObject.AddComponent<CameraFollow2D>();
                camFollow.smoothSpeed = 8f;
                camFollow.cameraZ = -10f;
            }

            // 3. UI Setup if missing
#if UNITY_2023_1_OR_NEWER
            Canvas existingCanvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
#else
            Canvas existingCanvas = UnityEngine.Object.FindObjectOfType<Canvas>();
#endif
            if (existingCanvas == null)
            {
                CreateRuntimeUI(gm, mm);
            }
        }

        private static Font GetDefaultFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 14);
            return f;
        }

        private static void CreateRuntimeUI(GameObject gm, MissionManager mm)
        {
            GameObject canvasGO = new GameObject("UICanvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            Font font = GetDefaultFont();

            // Objective text
            GameObject objGO = new GameObject("ObjectiveText");
            objGO.transform.SetParent(canvasGO.transform, false);
            Text objText = objGO.AddComponent<Text>();
            objText.text = "MISSION 2: Eliminate Viktor. Adapt to enemy defenses.";
            objText.font = font;
            objText.fontSize = 16;
            objText.color = Color.white;
            objText.alignment = TextAnchor.LowerCenter;
            RectTransform objRT = objGO.GetComponent<RectTransform>();
            objRT.anchorMin = new Vector2(0f, 0f);
            objRT.anchorMax = new Vector2(1f, 0.1f);
            objRT.offsetMin = new Vector2(10f, 10f);
            objRT.offsetMax = new Vector2(-10f, 0f);

            // Briefing panel
            GameObject panelGO = new GameObject("BriefingPanel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.85f);
            RectTransform panelRT = panelGO.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.1f, 0.25f);
            panelRT.anchorMax = new Vector2(0.9f, 0.85f);
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            GameObject debriefGO = new GameObject("DebriefingText");
            debriefGO.transform.SetParent(panelGO.transform, false);
            Text debriefText = debriefGO.AddComponent<Text>();
            debriefText.text = "";
            debriefText.font = font;
            debriefText.fontSize = 14;
            debriefText.color = Color.white;
            debriefText.alignment = TextAnchor.MiddleCenter;
            RectTransform debriefRT = debriefGO.GetComponent<RectTransform>();
            debriefRT.anchorMin = Vector2.zero;
            debriefRT.anchorMax = Vector2.one;
            debriefRT.offsetMin = new Vector2(20f, 20f);
            debriefRT.offsetMax = new Vector2(-20f, -20f);

            panelGO.SetActive(false);

            if (mm != null)
            {
                mm.objectiveText = objText;
                mm.debriefingText = debriefText;
                mm.briefingPanel = panelGO;
            }
        }
    }
}