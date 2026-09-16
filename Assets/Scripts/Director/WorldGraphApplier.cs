using System;
using UnityEngine;
using AdaptiveWorld2D.World;
using AdaptiveWorld2D.Player;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Applies sector graph construction, player spawn positioning, and tactical props
    /// (fuel barrels, ammo caches, burnable cars) from room graph manifests (Architecture Improvement Plan, Phase 6).
    /// </summary>
    public static class WorldGraphApplier
    {
        /// <summary>
        /// Builds the runtime sector layout from the room graph manifest.
        /// </summary>
        public static void BuildWorldGraph(RoomGraphManifest rg)
        {
            if (rg != null)
            {
                SectorRuntimeBuilder.Build(rg);
            }
        }

        /// <summary>
        /// Repositions the Player GameObject to the center of the player_spawn_sector.
        /// </summary>
        public static void ApplyPlayerSpawn(RoomGraphManifest rg)
        {
            if (rg == null || string.IsNullOrEmpty(rg.player_spawn_sector)) return;

            var sector = rg.GetSector(rg.player_spawn_sector);
            if (sector == null) return;

            Vector3 spawnCenter = new Vector3(sector.center_x, sector.center_y, 0f);
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                player = WorldManifestApplier.CreateProceduralPlayer(spawnCenter);
            }
            else
            {
                player.transform.position = spawnCenter;
                var rb = player.GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = Vector2.zero;
            }

            Debug.Log($"[WorldGraphApplier] Player repositioned to spawn sector '{rg.player_spawn_sector}' at {spawnCenter}.");
        }

        /// <summary>
        /// Spawns tactical props (fuel barrels, ammo caches, and burnable cars)
        /// in their semantically correct sectors, driven by the routes manifest and room_graph.
        /// </summary>
        public static void ApplyTacticalProps(RoutesManifest routes, RoomGraphManifest rg)
        {
            if (rg == null || rg.sectors == null) return;

            bool carsAllowed = routes != null && routes.cars_available;
            int barrelCount = 0;
            int carCount = 0;
            int ammoCount = 0;

            foreach (var sector in rg.sectors)
            {
                if (sector == null) continue;
                Vector3 center = new Vector3(sector.center_x, sector.center_y, 0f);

                switch (sector.module)
                {
                    case "INDUSTRIAL_DEPOT":
                    case "WAREHOUSE_INTERIOR":
                        Vector3[] barrelOffsets = { new Vector3(-3f, 2f, 0f), new Vector3(3f, -2f, 0f) };
                        foreach (var offset in barrelOffsets)
                        {
                            SpawnFuelBarrel(center + offset, $"FuelBarrel_{sector.id}_{barrelCount}");
                            barrelCount++;
                        }
                        SpawnAmmoCache(center + new Vector3(0f, -3f, 0f), $"AmmoCache_{sector.id}_{ammoCount}");
                        ammoCount++;
                        break;

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
                Debug.Log($"[WorldGraphApplier] Tactical props: {barrelCount} fuel barrels, " +
                          $"{carCount} burnable cars, {ammoCount} ammo caches (cars_available={carsAllowed}).");
            }
        }

        public static void SpawnFuelBarrel(Vector3 pos, string id)
        {
            GameObject go = new GameObject(id);
            go.transform.position = pos;
            int interactLayer = LayerMask.NameToLayer("Interactable");
            go.layer = interactLayer >= 0 ? interactLayer : 0;

            var sr = go.AddComponent<SpriteRenderer>();
            Sprite barrelSprite = null;
#if UNITY_EDITOR
            string[] barrelPaths =
            {
                "Assets/assets/kenney_topdown_shooter/PNG/Tiles/tile_197.png",
                "Assets/assets/kenney_topdown_shooter/PNG/Tiles/tile_206.png"
            };
            foreach (string path in barrelPaths)
            {
                barrelSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (barrelSprite != null) break;
            }
#endif
            if (barrelSprite != null)
            {
                sr.sprite = barrelSprite;
            }
            else
            {
                Texture2D tex = new Texture2D(16, 16);
                float ctr = 8f;
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                        tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), new Vector2(ctr, ctr)) <= ctr
                            ? new Color(0.85f, 0.45f, 0.1f) : Color.clear);
                tex.Apply();
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
            }
            sr.sortingOrder = 2;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.5f, 0.5f);

            var wall = go.AddComponent<DestructibleWall>();
            wall.wallId = id;
            wall.maxHealth = 20;
            wall.currentHealth = 20;
        }

        public static void SpawnBurnableCar(Vector3 pos, string id)
        {
            GameObject go = new GameObject(id);
            go.transform.position = pos;
            go.tag = "Vehicle";
            int interactLayer = LayerMask.NameToLayer("Interactable");
            go.layer = interactLayer >= 0 ? interactLayer : 0;

            var sr = go.AddComponent<SpriteRenderer>();
            Sprite carSprite = null;
#if UNITY_EDITOR
            string[] carPaths =
            {
                "Assets/assets/kenney_vehicles/PNG/Cars/car_black_1.png",
                "Assets/assets/kenney_vehicles/PNG/Cars/car_blue_1.png",
                "Assets/assets/kenney_vehicles/PNG/Cars/car_red_1.png"
            };
            string chosenPath = carPaths[Mathf.Abs(id.GetHashCode()) % carPaths.Length];
            carSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(chosenPath);
#endif
            if (carSprite != null)
            {
                sr.sprite = carSprite;
                go.transform.localScale = new Vector3(1f, 0.6f, 1f);
            }
            else
            {
                Texture2D tex = new Texture2D(32, 16);
                Color carColor = new Color(0.25f, 0.25f, 0.3f);
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 32; x++)
                        tex.SetPixel(x, y, carColor);
                tex.Apply();
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, 32, 16), new Vector2(0.5f, 0.5f), 16f);
            }
            sr.sortingOrder = 3;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(2.0f, 1.0f);

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;

            var car = go.AddComponent<BurnableCar>();
            car.carId = id;
            car.carType = "SEDAN";
            car.cleanCarSprite = carSprite;
        }

        public static void SpawnAmmoCache(Vector3 pos, string id)
        {
            GameObject go = new GameObject(id);
            go.transform.position = pos;
            SafeSetTag(go, "AmmoPickup");

            var sr = go.AddComponent<SpriteRenderer>();
            Sprite ammoSprite = null;
#if UNITY_EDITOR
            ammoSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/assets/kenney_topdown_shooter/PNG/Tiles/tile_183.png");
#endif
            if (ammoSprite != null)
            {
                sr.sprite = ammoSprite;
            }
            else
            {
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
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, 12, 12), new Vector2(0.5f, 0.5f), 12f);
            }
            sr.sortingOrder = 3;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.75f, 0.75f);
            col.isTrigger = true;

            go.AddComponent<AmmoCacheTrigger>();
        }

        private static void SafeSetTag(GameObject go, string tag)
        {
            try { go.tag = tag; }
            catch { /* Tag not registered in TagManager, leave untagged without crashing */ }
        }
    }
}
