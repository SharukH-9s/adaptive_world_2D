using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
                Debug.Log("[WorldManifestApplier] Applying cached manifest on scene load.");
                ApplyManifest(AdaptiveDirectorClient.latestManifest);
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

            ApplyTarget(manifest.target);
            ApplyGuards(manifest.guards);
            ApplyEnvironment(manifest.environment);
            ApplyRoutes(manifest.routes);
        }

        private void ApplyTarget(TargetManifest t)
        {
            if (t == null) return;

            Vector3 spawnPos = Vector3.zero;
            bool foundAnchor = false;

            foreach (var anchor in locationAnchors)
            {
                if (anchor.locationId != null && anchor.locationId.Equals(t.location_id, StringComparison.OrdinalIgnoreCase) && anchor.spawnTransform != null)
                {
                    spawnPos = anchor.spawnTransform.position;
                    foundAnchor = true;
                    break;
                }
            }

            if (!foundAnchor && t.coordinates != null && t.coordinates.Length >= 2)
            {
                spawnPos = new Vector3(t.coordinates[0], t.coordinates[1], 0f);
                foundAnchor = true;
            }

            if (!foundAnchor)
            {
                spawnPos = new Vector3(8f, 2f, 0f); // default plaza location
            }

            if (targetPrefab == null)
            {
                targetPrefab = Resources.Load<GameObject>("ViktorPrefab");
            }

            if (currentTargetInstance == null)
            {
                if (targetPrefab != null)
                {
                    currentTargetInstance = Instantiate(targetPrefab, spawnPos, Quaternion.identity);
                }
                else
                {
                    currentTargetInstance = CreateProceduralBoss(spawnPos, t.health);
                }
            }
            else
            {
                currentTargetInstance.transform.position = spawnPos;
                currentTargetInstance.SetActive(true);
            }

            // Configure Target stats if target has a component
            if (currentTargetInstance != null)
            {
                var targetCombat = currentTargetInstance.GetComponent<AdaptiveWorld2D.AI.EnemyPatrol>();
                if (targetCombat != null)
                {
                    targetCombat.maxHealth = t.health;
                    targetCombat.currentHealth = t.health;
                    targetCombat.isTargetBoss = true;
                }
            }

            Debug.Log($"[WorldManifestApplier] Target Viktor ({t.location_label}) spawned at {spawnPos} (HP: {t.health}, Armor: {t.armor})");
        }

        private void ApplyGuards(GuardsManifest g)
        {
            if (g == null) return;

            // Clear previous guards
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

            int countToSpawn = Mathf.Min(g.count, 12);
            for (int i = 0; i < countToSpawn; i++)
            {
                // Use Inspector spawn points when available; else scatter in a circle
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

                var detection = guardObj.GetComponent<AdaptiveWorld2D.AI.EnemyDetection>();
                if (detection != null)
                {
                    detection.visionRange   = g.vision_range;
                    detection.reactionSpeed = g.reaction_speed;
                    detection.hearingRange  = g.hearing_range;
                }

                var patrol = guardObj.GetComponent<AdaptiveWorld2D.AI.EnemyPatrol>();
                if (patrol != null && g.armored)
                    patrol.damageResistance = 0.5f;
            }

            Debug.Log($"[WorldManifestApplier] Spawned {_activeGuards.Count}/{g.count} guards (armored={g.armored}, vision={g.vision_range}).");
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
            det.visionRange = 6f;

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
            det.visionRange = 7f;

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
                globalLight2D = FindObjectOfType<UnityEngine.Rendering.Universal.Light2D>();
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

            Debug.Log($"[WorldManifestApplier] Environment Applied: {env.time_of_day.ToUpper()} | Weather: {env.weather.ToUpper()} | Visibility: {env.visibility:F2}");
        }

        private void EnsureAtmosphereOverlay(EnvironmentManifest env)
        {
            var canvas = FindObjectOfType<Canvas>();
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
    }
}
