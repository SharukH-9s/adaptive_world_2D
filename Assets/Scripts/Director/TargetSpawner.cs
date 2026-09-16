using System;
using System.Collections.Generic;
using UnityEngine;
using AdaptiveWorld2D.AI;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Spawns Viktor (boss target) and his personal bodyguards according to
    /// <see cref="TargetManifest"/> and <see cref="RoomGraphManifest"/> (Architecture Improvement Plan, Phase 6).
    /// </summary>
    public static class TargetSpawner
    {
        /// <summary>
        /// Spawns or repositions Viktor, configures his combat/detection stats,
        /// and spawns his flanking personal bodyguards.
        /// </summary>
        public static GameObject SpawnTarget(
            TargetManifest t,
            RoomGraphManifest rg,
            ref GameObject targetPrefab,
            ref GameObject currentTargetInstance,
            IList<WorldManifestApplier.LocationAnchor> locationAnchors,
            ref GameObject guardPrefab,
            IList<GameObject> activeGuards)
        {
            if (t == null) return currentTargetInstance;

            Vector3 spawnPos = ResolveTargetPosition(t, rg, locationAnchors);

            // Spawn or resolve Viktor
            if (targetPrefab == null)
                targetPrefab = Resources.Load<GameObject>("ViktorPrefab");
#if UNITY_EDITOR
            if (targetPrefab == null)
                targetPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Prefabs/ViktorPrefab.prefab");
#endif

            if (currentTargetInstance == null)
            {
                currentTargetInstance = targetPrefab != null
                    ? UnityEngine.Object.Instantiate(targetPrefab, spawnPos, Quaternion.identity)
                    : CreateProceduralBoss(spawnPos, t.health);
            }
            else
            {
                currentTargetInstance.transform.position = spawnPos;
            }

            // Configure Viktor's combat stats
            if (currentTargetInstance != null)
            {
                currentTargetInstance.SetActive(true);
                ConfigureEnemyVisual(currentTargetInstance, 30);
                var bossPatrol = currentTargetInstance.GetComponent<EnemyPatrol>();
                if (bossPatrol != null)
                {
                    bossPatrol.maxHealth = t.health;
                    bossPatrol.currentHealth = t.health;
                    bossPatrol.isTargetBoss = true;
                    bossPatrol.patrolSpeed = 1.5f;
                    bossPatrol.chaseSpeed = 3.0f;
                }
                var bossDetect = currentTargetInstance.GetComponent<EnemyDetection>();
                if (bossDetect != null)
                {
                    bossDetect.visionRange = ResolveBossVisionRange(t.armor);
                }
            }

            Debug.Log($"[TargetSpawner] Target Viktor ({t.location_label}) spawned at {spawnPos} " +
                      $"(HP: {t.health}, Armor: {t.armor})");

            // Spawn 1-2 personal bodyguards flanking Viktor
            SpawnViktorBodyguards(spawnPos, rg, ref guardPrefab, activeGuards);

            return currentTargetInstance;
        }

        public static Vector3 ResolveTargetPosition(
            TargetManifest t,
            RoomGraphManifest rg,
            IList<WorldManifestApplier.LocationAnchor> locationAnchors)
        {
            // 1. Prefer room_graph target_sector center
            if (rg != null && !string.IsNullOrEmpty(rg.target_sector))
            {
                var sector = rg.GetSector(rg.target_sector);
                if (sector != null)
                {
                    return new Vector3(sector.center_x, sector.center_y, 0f);
                }
            }

            // 2. Fallback: Inspector location anchors
            if (locationAnchors != null && t != null && !string.IsNullOrEmpty(t.location_id))
            {
                foreach (var anchor in locationAnchors)
                {
                    if (anchor.locationId != null &&
                        anchor.locationId.Equals(t.location_id, StringComparison.OrdinalIgnoreCase) &&
                        anchor.spawnTransform != null)
                    {
                        return anchor.spawnTransform.position;
                    }
                }
            }

            // 3. Fallback: raw coordinates
            if (t != null && t.coordinates != null && t.coordinates.Length >= 2)
            {
                return new Vector3(t.coordinates[0], t.coordinates[1], 0f);
            }

            // 4. Hard default
            return new Vector3(8f, 2f, 0f);
        }

        public static float ResolveBossVisionRange(string armor)
        {
            if (string.Equals(armor, "heavy", StringComparison.OrdinalIgnoreCase))
                return 8f;
            if (string.Equals(armor, "medium", StringComparison.OrdinalIgnoreCase))
                return 7f;
            return 6f;
        }

        public static void SpawnViktorBodyguards(
            Vector3 viktorPos,
            RoomGraphManifest rg,
            ref GameObject guardPrefab,
            IList<GameObject> activeGuards)
        {
            int bodyguardCount = 2;
            float[] xOffsets = { -1.8f, 1.8f };

            SectorConfig targetSectorCfg = null;
            if (rg != null && !string.IsNullOrEmpty(rg.target_sector))
                targetSectorCfg = rg.GetSector(rg.target_sector);

            for (int i = 0; i < bodyguardCount; i++)
            {
                Vector3 pos = viktorPos + new Vector3(xOffsets[i], 0f, 0f);

                bool armored = false;
                if (targetSectorCfg != null && targetSectorCfg.guards != null &&
                    i < targetSectorCfg.guards.Count)
                {
                    var gc = targetSectorCfg.guards[i];
                    armored = gc.armored;
                    if (gc.offset_x != 0f || gc.offset_y != 0f)
                        pos = new Vector3(targetSectorCfg.center_x + gc.offset_x,
                                          targetSectorCfg.center_y + gc.offset_y, 0f);
                }

                if (guardPrefab == null)
                    guardPrefab = Resources.Load<GameObject>("GuardPrefab");
#if UNITY_EDITOR
                if (guardPrefab == null)
                    guardPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefabs/GuardPrefab.prefab");
#endif
                GameObject bodyguard = guardPrefab != null
                    ? UnityEngine.Object.Instantiate(guardPrefab, pos, Quaternion.identity)
                    : CreateProceduralGuard(pos, 900 + i);
                if (bodyguard == null) continue;

                bodyguard.name = $"Viktor_Bodyguard_{i:D2}";
                bodyguard.SetActive(true);
                ConfigureEnemyVisual(bodyguard, 26);
                if (activeGuards != null)
                    activeGuards.Add(bodyguard);

                var patrol = bodyguard.GetComponent<EnemyPatrol>();
                if (patrol != null)
                {
                    patrol.maxHealth = 80;
                    patrol.currentHealth = 80;
                    patrol.patrolSpeed = 1.5f;
                    patrol.chaseSpeed = 4.5f;
                    patrol.damageResistance = armored ? 0.5f : 0.25f;
                    if (patrol.waypoints != null && patrol.waypoints.Count >= 2)
                    {
                        patrol.waypoints[0].position = viktorPos + new Vector3(-1f, 0.5f, 0f);
                        patrol.waypoints[1].position = viktorPos + new Vector3(1f, -0.5f, 0f);
                    }
                }

                var det = bodyguard.GetComponent<EnemyDetection>();
                if (det != null)
                {
                    det.visionRange = 8f;
                    det.hearingRange = 10f;
                    det.reactionSpeed = 1.8f;
                }

                var sr = bodyguard.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = new Color(0.85f, 0.85f, 0.85f);
            }

            Debug.Log($"[TargetSpawner] Spawned {bodyguardCount} personal bodyguards flanking Viktor.");
        }

        public static void ConfigureEnemyVisual(GameObject enemy, int sortingOrder)
        {
            if (enemy == null) return;

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0)
                enemy.layer = enemyLayer;

            SpriteRenderer renderer = enemy.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
                renderer.sortingLayerName = "Characters";
                renderer.sortingOrder = sortingOrder;
                renderer.color = new Color(renderer.color.r, renderer.color.g, renderer.color.b, 1f);
            }
        }

        public static GameObject CreateProceduralBoss(Vector3 pos, int health)
        {
            GameObject go = new GameObject("Viktor_Boss_Target");
            go.transform.position = pos;
            go.tag = "Enemy";
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            go.layer = enemyLayer >= 0 ? enemyLayer : 0;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite(0.45f, new Color(1f, 0.2f, 0.2f));
            sr.sortingOrder = 6;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.45f;

            var patrol = go.AddComponent<EnemyPatrol>();
            patrol.patrolSpeed = 1.5f;
            patrol.chaseSpeed = 3.5f;
            patrol.maxHealth = health > 0 ? health : 100;
            patrol.currentHealth = patrol.maxHealth;
            patrol.isTargetBoss = true;

            var det = go.AddComponent<EnemyDetection>();
            det.visionRange = 7f;
            det.obstacleLayer = LayerMask.GetMask("Building", "Interactable");
            det.playerLayer = LayerMask.GetMask("Default");

            return go;
        }

        public static GameObject CreateProceduralGuard(Vector3 pos, int index)
        {
            GameObject go = new GameObject($"Guard_{index:D2}");
            go.transform.position = pos;
            go.tag = "Enemy";
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            go.layer = enemyLayer >= 0 ? enemyLayer : 0;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateCircleSprite(0.35f, new Color(0.85f, 0.25f, 0.25f));
            sr.sortingOrder = 5;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            CircleCollider2D col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.35f;

            GameObject wp0 = new GameObject("Waypoint_0");
            wp0.transform.SetParent(go.transform);
            wp0.transform.localPosition = new Vector3(-2f, 0f, 0f);
            GameObject wp1 = new GameObject("Waypoint_1");
            wp1.transform.SetParent(go.transform);
            wp1.transform.localPosition = new Vector3(2f, 0f, 0f);

            var patrol = go.AddComponent<EnemyPatrol>();
            patrol.patrolSpeed = 2f;
            patrol.chaseSpeed = 4f;
            patrol.maxHealth = 60;
            patrol.waypoints.Add(wp0.transform);
            patrol.waypoints.Add(wp1.transform);

            var det = go.AddComponent<EnemyDetection>();
            det.visionRange = 6f;
            det.obstacleLayer = LayerMask.GetMask("Building", "Interactable");
            det.playerLayer = LayerMask.GetMask("Default");

            return go;
        }

        public static Sprite CreateCircleSprite(float radius, Color color)
        {
            int size = 32;
            Texture2D tex = new Texture2D(size, size);
            float ctr = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), new Vector2(ctr, ctr)) <= ctr ? color : Color.clear);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / (radius * 2f));
        }
    }
}
