using System;
using System.Collections.Generic;
using UnityEngine;
using AdaptiveWorld2D.AI;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Spawns and distributes patrol and sentry guards across sectors according to
    /// <see cref="GuardsManifest"/> and <see cref="RoomGraphManifest"/> (Architecture Improvement Plan, Phase 6).
    /// </summary>
    public static class GuardSpawner
    {
        /// <summary>
        /// Clears previously spawned patrol guards and spawns new ones.
        /// </summary>
        public static List<GameObject> SpawnGuards(
            GuardsManifest g,
            RoomGraphManifest rg,
            ref GameObject guardPrefab,
            IList<Transform> guardSpawnPoints,
            IList<GameObject> activeGuards)
        {
            var spawnedGuards = new List<GameObject>();
            if (g == null) return spawnedGuards;

            // Clear previous guards in active list
            if (activeGuards != null)
            {
                foreach (var guard in activeGuards)
                {
                    if (guard != null) UnityEngine.Object.Destroy(guard);
                }
                activeGuards.Clear();
            }

            // Resolve guard prefab
            if (guardPrefab == null)
            {
                guardPrefab = Resources.Load<GameObject>("GuardPrefab");
#if UNITY_EDITOR
                if (guardPrefab == null)
                    guardPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefabs/GuardPrefab.prefab");
#endif
                if (guardPrefab == null)
                    Debug.Log("[GuardSpawner] Guard prefab unavailable; using procedural runtime guard.");
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
                    if (sector == null) continue;
                    // Skip the target sector — bodyguards are spawned separately
                    if (sector.id == rg.target_sector) continue;

                    if (spawned >= maxGuards) break;

                    // 1. Explicit per-sector guards
                    if (sector.guards != null && sector.guards.Count > 0)
                    {
                        foreach (var gc in sector.guards)
                        {
                            if (spawned >= maxGuards) break;
                            Vector3 pos = new Vector3(
                                sector.center_x + gc.offset_x,
                                sector.center_y + gc.offset_y, 0f);
                            var guardObj = SpawnSectorGuard(pos, sector, g, spawned, gc.armored, gc.role, ref guardPrefab);
                            if (guardObj != null)
                            {
                                spawnedGuards.Add(guardObj);
                                if (activeGuards != null) activeGuards.Add(guardObj);
                                spawned++;
                            }
                        }
                    }
                    else
                    {
                        // 2. Auto-assign 1-2 guards per non-target sector
                        int nonTargetSectors = 8;
                        int guardsPerSector = Mathf.Max(1, Mathf.CeilToInt((float)maxGuards / nonTargetSectors));
                        int toSpawnHere = Mathf.Min(guardsPerSector, maxGuards - spawned);

                        for (int gi = 0; gi < toSpawnHere; gi++)
                        {
                            float xOff = (gi == 0) ? -3.5f : 3.5f;
                            Vector3 pos = new Vector3(sector.center_x + xOff, sector.center_y, 0f);
                            var guardObj = SpawnSectorGuard(pos, sector, g, spawned, g.armored, "patrol", ref guardPrefab);
                            if (guardObj != null)
                            {
                                spawnedGuards.Add(guardObj);
                                if (activeGuards != null) activeGuards.Add(guardObj);
                                spawned++;
                            }
                        }
                    }
                }

                Debug.Log($"[GuardSpawner] Phase 4/6: Spawned {spawned} sector-distributed guards " +
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
                    if (guardSpawnPoints != null && i < guardSpawnPoints.Count && guardSpawnPoints[i] != null)
                    {
                        pos = guardSpawnPoints[i].position;
                    }
                    else
                    {
                        float angle = (360f / countToSpawn) * i;
                        float radius = 4f + (i % 3) * 2f;
                        pos = new Vector3(
                            Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                            Mathf.Sin(angle * Mathf.Deg2Rad) * radius, 0f);
                    }

                    GameObject guardObj = guardPrefab != null
                        ? UnityEngine.Object.Instantiate(guardPrefab, pos, Quaternion.identity)
                        : TargetSpawner.CreateProceduralGuard(pos, i);

                    if (guardObj == null) continue;
                    TargetSpawner.ConfigureEnemyVisual(guardObj, 25);
                    spawnedGuards.Add(guardObj);
                    if (activeGuards != null) activeGuards.Add(guardObj);

                    ApplyGuardStats(guardObj, g);
                }

                Debug.Log($"[GuardSpawner] Spawned {spawnedGuards.Count}/{g.count} guards (legacy scatter).");
            }

            return spawnedGuards;
        }

        public static GameObject SpawnSectorGuard(
            Vector3 pos,
            SectorConfig sector,
            GuardsManifest g,
            int index,
            bool armored,
            string role,
            ref GameObject guardPrefab)
        {
            GameObject guardObj = guardPrefab != null
                ? UnityEngine.Object.Instantiate(guardPrefab, pos, Quaternion.identity)
                : TargetSpawner.CreateProceduralGuard(pos, index);

            if (guardObj == null) return null;
            guardObj.SetActive(true);
            TargetSpawner.ConfigureEnemyVisual(guardObj, 25);

            ApplyGuardStats(guardObj, g);

            var patrol = guardObj.GetComponent<EnemyPatrol>();
            if (patrol != null && sector != null)
            {
                patrol.sectorId = sector.id;

                if (armored)
                    patrol.damageResistance = 0.5f;

                if (patrol.waypoints != null && patrol.waypoints.Count >= 2)
                {
                    float wpRange = role == "sentry" ? 1.5f : 4.0f;
                    patrol.waypoints[0].position = new Vector3(
                        sector.center_x - wpRange, sector.center_y + (index % 2 == 0 ? 1.5f : -1.5f), 0f);
                    patrol.waypoints[1].position = new Vector3(
                        sector.center_x + wpRange, sector.center_y - (index % 2 == 0 ? 1.5f : -1.5f), 0f);
                }
            }

            return guardObj;
        }

        public static void ApplyGuardStats(GameObject guardObj, GuardsManifest g)
        {
            if (guardObj == null || g == null) return;

            var detection = guardObj.GetComponent<EnemyDetection>();
            if (detection != null)
            {
                detection.SetBaseVisionRange(g.vision_range);
                detection.reactionSpeed = g.reaction_speed;
                detection.hearingRange = g.hearing_range;
            }

            var patrol = guardObj.GetComponent<EnemyPatrol>();
            if (patrol != null && g.armored)
            {
                patrol.damageResistance = 0.5f;
            }
        }
    }
}
