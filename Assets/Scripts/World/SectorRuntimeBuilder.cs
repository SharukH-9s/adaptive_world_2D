using System.Collections.Generic;
using UnityEngine;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// SectorRuntimeBuilder (§3.3A — Runtime Decoupling / Dual Editor + Runtime Capability).
    ///
    /// Constructs the physical static architecture (ground surfaces, walls, buildings,
    /// and inter-sector gateways) of Map 2's 3x3 tactical sector grid (60x45 canvas)
    /// from a RoomGraphManifest at runtime without requiring the Unity Editor or AssetDatabase.
    ///
    /// Can be executed:
    ///   1. At runtime when WorldManifestApplier receives a live manifest from the director.
    ///   2. At runtime in standalone builds when transitioning from Map 1 to Map 2.
    ///   3. At edit time via ProceduralSectorBuilder menu items.
    /// </summary>
    public static class SectorRuntimeBuilder
    {
        public const int LAYER_INTERACTABLE = 6;
        public const int LAYER_ROAD         = 8;
        public const int LAYER_ALLEY        = 9;
        public const int LAYER_BUILDING     = 10;

        public const float CELL_WIDTH  = 20f;
        public const float CELL_HEIGHT = 15f;
        public const string ROOT_NAME  = "Map2_SectorGraph";

        public static readonly Dictionary<string, Vector2> SectorCoords = new Dictionary<string, Vector2>
        {
            { "NW", new Vector2(-20f, 15f) }, { "NC", new Vector2(0f, 15f) }, { "NE", new Vector2(20f, 15f) },
            { "MW", new Vector2(-20f,  0f) }, { "MC", new Vector2(0f,  0f) }, { "ME", new Vector2(20f,  0f) },
            { "SW", new Vector2(-20f,-15f) }, { "SC", new Vector2(0f,-15f) }, { "SE", new Vector2(20f,-15f) },
        };

        public static GameObject Build(RoomGraphManifest roomGraph)
        {
            if (roomGraph == null)
            {
                Debug.LogError("[SectorRuntimeBuilder] room_graph is null -- aborting build.");
                return null;
            }

            List<string> errors = Validate(roomGraph);
            if (errors.Count > 0)
            {
                Debug.LogError("[SectorRuntimeBuilder] room_graph failed validation, aborting build:\n - " + string.Join("\n - ", errors));
                return null;
            }

            GridDimensions dims = roomGraph.GetGridDimensions();

            Debug.Log($"[SectorRuntimeBuilder] Building {dims.columns}x{dims.rows} grid | " +
                      $"spawn={roomGraph.player_spawn_sector} -> target={roomGraph.target_sector} -> extraction={roomGraph.extraction_sector}");

            ClearPrevious();
            GameObject root = new GameObject(ROOT_NAME);

            foreach (var sector in roomGraph.sectors)
            {
                Vector3 center = new Vector3(sector.center_x, sector.center_y, 0f);
                GameObject sectorRoot = new GameObject($"Sector_{sector.id}_{sector.module}");
                sectorRoot.transform.SetParent(root.transform);
                sectorRoot.transform.position = center;

                BuildModule(sectorRoot.transform, sector.module, center);
                BuildProps(sectorRoot.transform, sector, center);

                if (!Application.isPlaying && sector.id == roomGraph.player_spawn_sector)
                    CreateRoleMarker(sectorRoot.transform, "SPAWN", center, new Color(0.2f, 0.9f, 1f, 0.9f));
                if (!Application.isPlaying && sector.id == roomGraph.target_sector)
                    CreateRoleMarker(sectorRoot.transform, "TARGET", center, new Color(1f, 0.2f, 0.2f, 0.9f));
                if (!Application.isPlaying && sector.id == roomGraph.extraction_sector)
                    CreateRoleMarker(sectorRoot.transform, "EXTRACTION", center, new Color(0.3f, 1f, 0.3f, 0.9f));
            }

            GameObject gatewaysRoot = new GameObject("Gateways");
            gatewaysRoot.transform.SetParent(root.transform);
            foreach (var conn in roomGraph.connections)
            {
                BuildGateway(gatewaysRoot.transform, roomGraph, conn);
            }

            BuildPerimeterBoundaries(root.transform, dims);

            Debug.Log("[SectorRuntimeBuilder] Sector grid build complete.");
            return root;
        }


        public static void ClearPrevious()
        {
            GameObject existing = GameObject.Find(ROOT_NAME);
            if (existing != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(existing);
                else
                    Object.DestroyImmediate(existing);
            }
        }

        /// <summary>
        /// Delegates to <see cref="RoomGraphValidator.Validate"/> (Architecture Improvement
        /// Plan, Phase 2). Kept as a private redirect so <see cref="Build"/>'s call site and
        /// logging behavior remain unchanged.
        /// </summary>
        private static List<string> Validate(RoomGraphManifest rg)
        {
            return RoomGraphValidator.Validate(rg);
        }

        private static void BuildModule(Transform parent, string module, Vector3 center)
        {
            switch (module)
            {
                case "WAREHOUSE_INTERIOR": BuildWarehouseInterior(parent, center); break;
                case "FORTIFIED_PLAZA":    BuildFortifiedPlaza(parent, center); break;
                case "SLUM_ALLEYWAYS":     BuildSlumAlleyways(parent, center); break;
                case "URBAN_HIGHWAY":      BuildUrbanHighway(parent, center); break;
                case "INDUSTRIAL_DEPOT":   BuildIndustrialDepot(parent, center); break;
                case "WATERFRONT_DOCKS":   BuildWaterfrontDocks(parent, center); break;
                case "SECURITY_COMPOUND":  BuildSecurityCompound(parent, center); break;
                case "OPEN_STREET":        BuildOpenStreet(parent, center); break;
                case "VACANT_LOT":         BuildVacantLot(parent, center); break;
                default:
                    Debug.LogWarning($"[SectorRuntimeBuilder] Unknown module '{module}' -- building generic OPEN_STREET fallback.");
                    BuildOpenStreet(parent, center);
                    break;
            }
        }

        private static void BuildWarehouseInterior(Transform parent, Vector3 center)
        {
            CreateGroundQuad(parent, "Ground_WarehouseFloor", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_BUILDING,
                new Color(0.30f, 0.28f, 0.25f), "WAREHOUSE_INTERIOR");

            float w = 14f, h = 10f, t = 0.6f;
            Color wallColor = new Color(0.45f, 0.42f, 0.38f);

            CreateSolidWall(parent, "WarehouseWall_N_L", center + new Vector3(-w / 4f - 1f, h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "WarehouseWall_N_R", center + new Vector3(w / 4f + 1f, h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "WarehouseWall_S_L", center + new Vector3(-w / 4f - 1f, -h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "WarehouseWall_S_R", center + new Vector3(w / 4f + 1f, -h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "WarehouseWall_W", center + new Vector3(-w / 2f, 0f, 0f), new Vector2(t, h), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "WarehouseWall_E", center + new Vector3(w / 2f, 0f, 0f), new Vector2(t, h), LAYER_BUILDING, wallColor);

            CreateSolidWall(parent, "WarehousePartition_Top", center + new Vector3(0f, h / 4f + 0.75f, 0f), new Vector2(t, h / 2f - 1.5f), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "WarehousePartition_Bottom", center + new Vector3(0f, -h / 4f - 0.75f, 0f), new Vector2(t, h / 2f - 1.5f), LAYER_BUILDING, wallColor);

            CreateCrate(parent, "WarehouseCrate_01", center + new Vector3(-3f, 2f, 0f));
            CreateCrate(parent, "WarehouseCrate_02", center + new Vector3(-3f, 1f, 0f));
            CreateCrate(parent, "WarehouseCrate_03", center + new Vector3(3f, -2f, 0f));
            CreateCrate(parent, "WarehouseCrate_04", center + new Vector3(3f, -3f, 0f));
        }

        private static void BuildFortifiedPlaza(Transform parent, Vector3 center)
        {
            CreateGroundQuad(parent, "Ground_Plaza", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ROAD,
                new Color(0.42f, 0.40f, 0.38f), "FORTIFIED_PLAZA");

            Vector3[] offsets = { new Vector3(-5f, 4f, 0f), new Vector3(5f, 4f, 0f), new Vector3(-5f, -4f, 0f), new Vector3(5f, -4f, 0f) };
            for (int i = 0; i < offsets.Length; i++)
                CreateDestructible(parent, $"PlazaBarrier_{i:D2}", center + offsets[i], new Vector2(2.1f, 0.62f), 100, new Color(0.55f, 0.53f, 0.5f));

            GameObject marker = new GameObject("PlazaMarker");
            marker.transform.SetParent(parent);
            marker.transform.position = center;
            SpriteRenderer sr = marker.AddComponent<SpriteRenderer>();
            sr.sprite = WorldManifestApplier.GetUniversalWhiteSprite();
            marker.transform.localScale = new Vector3(3f, 3f, 1f);
            sr.color = new Color(0.75f, 0.7f, 0.3f, 0.5f);
            sr.sortingOrder = -5;

            // Streetlights (§5.3A, Phase 5) — cool blue-white floodlights at the 4 corners of the plaza
            CreateStreetlight(parent, "PlazaLight_NW", center + new Vector3(-8f, 5.5f, 0f), "FORTIFIED_PLAZA");
            CreateStreetlight(parent, "PlazaLight_NE", center + new Vector3(8f, 5.5f, 0f), "FORTIFIED_PLAZA");
            CreateStreetlight(parent, "PlazaLight_SW", center + new Vector3(-8f, -5.5f, 0f), "FORTIFIED_PLAZA");
            CreateStreetlight(parent, "PlazaLight_SE", center + new Vector3(8f, -5.5f, 0f), "FORTIFIED_PLAZA");
        }

        private static void BuildSlumAlleyways(Transform parent, Vector3 center)
        {
            CreateGroundQuad(parent, "Ground_Alley", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ALLEY,
                new Color(0.22f, 0.22f, 0.24f), "SLUM_ALLEYWAYS");

            Color facadeColor = new Color(0.29f, 0.27f, 0.26f);
            CreateSolidWall(parent, "TenementFacade_NW", center + new Vector3(-6f, 4f, 0f), new Vector2(6f, 4f), LAYER_BUILDING, facadeColor);
            CreateSolidWall(parent, "TenementFacade_NE", center + new Vector3(6f, 4f, 0f), new Vector2(6f, 4f), LAYER_BUILDING, facadeColor);
            CreateSolidWall(parent, "TenementFacade_SW", center + new Vector3(-6f, -4f, 0f), new Vector2(6f, 4f), LAYER_BUILDING, facadeColor);
            CreateSolidWall(parent, "TenementFacade_SE", center + new Vector3(6f, -4f, 0f), new Vector2(6f, 4f), LAYER_BUILDING, facadeColor);

            CreateStreetlight(parent, "SlumLamp", center + new Vector3(0f, 0f, 0f), "SLUM_ALLEYWAYS");
        }

        private static void BuildUrbanHighway(Transform parent, Vector3 center)
        {
            CreateGroundQuad(parent, "Ground_Highway", center, new Vector2(CELL_WIDTH, CELL_HEIGHT - 3f), LAYER_ROAD,
                new Color(0.16f, 0.16f, 0.18f), "URBAN_HIGHWAY");

            Color sidewalkColor = new Color(0.5f, 0.5f, 0.48f);
            CreateGroundQuad(parent, "Sidewalk_N", center + new Vector3(0f, CELL_HEIGHT / 2f - 1f, 0f), new Vector2(CELL_WIDTH, 2f), LAYER_ROAD, sidewalkColor, "OPEN_STREET");
            CreateGroundQuad(parent, "Sidewalk_S", center + new Vector3(0f, -(CELL_HEIGHT / 2f - 1f), 0f), new Vector2(CELL_WIDTH, 2f), LAYER_ROAD, sidewalkColor, "OPEN_STREET");

            CreateStreetlight(parent, "Streetlight_W", center + new Vector3(-8f, CELL_HEIGHT / 2f - 1.6f, 0f), "URBAN_HIGHWAY");
            CreateStreetlight(parent, "Streetlight_C", center + new Vector3(0f, CELL_HEIGHT / 2f - 1.6f, 0f), "URBAN_HIGHWAY");
            CreateStreetlight(parent, "Streetlight_E", center + new Vector3(8f, CELL_HEIGHT / 2f - 1.6f, 0f), "URBAN_HIGHWAY");
        }

        private static void BuildIndustrialDepot(Transform parent, Vector3 center)
        {
            CreateGroundQuad(parent, "Ground_Depot", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ROAD,
                new Color(0.32f, 0.30f, 0.26f), "INDUSTRIAL_DEPOT");

            Vector3[] offsets = { new Vector3(-6f, 3f, 0f), new Vector3(-6f, -3f, 0f), new Vector3(6f, 3f, 0f), new Vector3(6f, -3f, 0f) };
            for (int i = 0; i < offsets.Length; i++)
                CreateSolidWall(parent, $"Container_{i:D2}", center + offsets[i], new Vector2(3.5f, 1.8f), LAYER_BUILDING, new Color(0.55f, 0.30f, 0.15f));
        }

        private static void BuildWaterfrontDocks(Transform parent, Vector3 center)
        {
            CreateGroundQuad(parent, "Ground_Pier", center, new Vector2(CELL_WIDTH, CELL_HEIGHT - 4f), LAYER_ROAD,
                new Color(0.40f, 0.36f, 0.28f), "WATERFRONT_DOCKS");

            CreateSolidWall(parent, "WaterBoundary", center + new Vector3(0f, -(CELL_HEIGHT / 2f - 1f), 0f), new Vector2(CELL_WIDTH, 2f), LAYER_BUILDING,
                new Color(0.10f, 0.25f, 0.45f));
        }

        private static void BuildSecurityCompound(Transform parent, Vector3 center)
        {
            CreateGroundQuad(parent, "Ground_Compound", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_BUILDING,
                new Color(0.24f, 0.24f, 0.26f), "SECURITY_COMPOUND");

            float w = 16f, h = 11f, t = 0.6f;
            Color wallColor = new Color(0.32f, 0.32f, 0.34f);

            CreateSolidWall(parent, "CompoundWall_N", center + new Vector3(0f, h / 2f, 0f), new Vector2(w, t), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "CompoundWall_W", center + new Vector3(-w / 2f, 0f, 0f), new Vector2(t, h), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "CompoundWall_E", center + new Vector3(w / 2f, 0f, 0f), new Vector2(t, h), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "CompoundWall_S_L", center + new Vector3(-w / 4f - 1f, -h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
            CreateSolidWall(parent, "CompoundWall_S_R", center + new Vector3(w / 4f + 1f, -h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);

            GameObject gate = new GameObject("CompoundGate_Marker");
            gate.transform.SetParent(parent);
            gate.transform.position = center + new Vector3(0f, -h / 2f, 0f);
            SpriteRenderer sr = gate.AddComponent<SpriteRenderer>();
            sr.sprite = WorldManifestApplier.GetUniversalWhiteSprite();
            gate.transform.localScale = new Vector3(2f, 0.3f, 1f);
            sr.color = new Color(0.9f, 0.75f, 0.15f, 0.8f);
            sr.sortingOrder = 1;
        }

        private static void BuildOpenStreet(Transform parent, Vector3 center)
        {
            CreateGroundQuad(parent, "Ground_OpenStreet", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ROAD,
                new Color(0.30f, 0.30f, 0.32f), "OPEN_STREET");
            CreateCrate(parent, "StreetCrate_01", center + new Vector3(-4f, 3f, 0f));
            CreateCrate(parent, "StreetCrate_02", center + new Vector3(4f, -3f, 0f));
        }

        private static void BuildVacantLot(Transform parent, Vector3 center)
        {
            CreateGroundQuad(parent, "Ground_VacantLot", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ROAD,
                new Color(0.34f, 0.33f, 0.28f), "VACANT_LOT");
        }

        private static void BuildGateway(Transform parent, RoomGraphManifest rg, ConnectionConfig conn)
        {
            if (conn.status == "open") return;

            SectorConfig from = rg.GetSector(conn.from_sector);
            SectorConfig to = rg.GetSector(conn.to_sector);
            if (from == null || to == null)
            {
                Debug.LogWarning($"[SectorRuntimeBuilder] Gateway {conn.from_sector}-{conn.to_sector} references an unknown sector -- skipped.");
                return;
            }

            Vector3 a = new Vector3(from.center_x, from.center_y, 0f);
            Vector3 b = new Vector3(to.center_x, to.center_y, 0f);
            Vector3 mid = (a + b) / 2f;

            bool sideBySide = Mathf.Abs(a.x - b.x) > Mathf.Abs(a.y - b.y);
            Vector2 size = sideBySide
                ? new Vector2(0.6f, CELL_HEIGHT * 0.6f)
                : new Vector2(CELL_WIDTH * 0.6f, 0.6f);

            string name = $"Gateway_{conn.from_sector}_{conn.to_sector}";

            switch (conn.status)
            {
                case "barricaded_wire_fence":
                    CreateDestructible(parent, name, mid, size, 40, new Color(0.75f, 0.75f, 0.75f, 0.85f));
                    break;

                case "barricaded_concrete":
                {
                    DestructibleWall dw = CreateDestructible(parent, name, mid, size, 100, new Color(0.5f, 0.48f, 0.45f));
                    Rigidbody2D rb = dw.gameObject.AddComponent<Rigidbody2D>();
                    rb.bodyType = RigidbodyType2D.Kinematic;
                    rb.gravityScale = 0f;
                    break;
                }

                case "walled_off":
                default:
                    CreateSolidWall(parent, name, mid, size, LAYER_BUILDING, new Color(0.2f, 0.2f, 0.22f));
                    break;
            }
        }

        private static GameObject CreateGroundQuad(Transform parent, string name, Vector3 center, Vector2 size, int layer, Color color, string module = null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = center;
            go.layer = layer;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            var catalog = SectorThemeCatalog.GetDefault();
            Sprite tile = null;
            if (catalog != null)
            {
                tile = (layer == LAYER_BUILDING) ? catalog.GetFloorTile(module) : catalog.GetRoadTile(module);
            }
            sr.sprite = (tile != null) ? tile : WorldManifestApplier.GetUniversalWhiteSprite();
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size;
            sr.color = Color.white;
            sr.sortingLayerName = "Background";
            sr.sortingOrder = -10;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            col.isTrigger = true;
            return go;
        }

        private static GameObject CreateSolidWall(Transform parent, string name, Vector3 pos, Vector2 size, int layer, Color color, bool reinforced = false)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.layer = layer;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            var catalog = SectorThemeCatalog.GetDefault();
            Sprite tile = catalog != null ? catalog.GetWallTile(reinforced) : WorldManifestApplier.GetUniversalWhiteSprite();
            sr.sprite = (tile != null) ? tile : WorldManifestApplier.GetUniversalWhiteSprite();
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size;
            sr.color = Color.white;
            sr.sortingLayerName = "Obstacles";
            sr.sortingOrder = 0;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = size;
            col.isTrigger = false;

            TryAttachShadowCaster(go);
            return go;
        }

        private static DestructibleWall CreateDestructible(Transform parent, string name, Vector3 pos, Vector2 size, int maxHealth, Color color)
        {
            GameObject go = CreateSolidWall(parent, name, pos, size, LAYER_INTERACTABLE, color);
            DestructibleWall dw = go.AddComponent<DestructibleWall>();
            dw.wallId = name.ToLower();
            dw.maxHealth = maxHealth;
            dw.intactSprite = go.GetComponent<SpriteRenderer>().sprite;
            return dw;
        }

        private static void CreateCrate(Transform parent, string name, Vector3 pos)
        {
            GameObject crate = new GameObject(name);
            crate.transform.SetParent(parent);
            crate.transform.position = pos;
            crate.layer = LAYER_INTERACTABLE;

            SpriteRenderer sr = crate.AddComponent<SpriteRenderer>();
            Sprite crateSprite = null;
#if UNITY_EDITOR
            string[] cratePaths =
            {
                "Assets/assets/kenney_topdown_shooter/PNG/Tiles/tile_131.png",
                "Assets/assets/kenney_topdown_shooter/PNG/Tiles/tile_132.png"
            };
            foreach (string path in cratePaths)
            {
                crateSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (crateSprite != null) break;
            }
#endif
            sr.sprite = crateSprite != null ? crateSprite : WorldManifestApplier.GetUniversalWhiteSprite();
            crate.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            sr.color = crateSprite != null ? Color.white : new Color(0.5f, 0.42f, 0.3f);
            sr.sortingOrder = 2;

            BoxCollider2D col = crate.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.7f, 0.7f);
        }

        private static void CreateStreetlight(Transform parent, string name, Vector3 pos, string module)
        {
            GameObject pole = new GameObject(name);
            pole.transform.SetParent(parent);
            pole.transform.position = pos;

            SpriteRenderer sr = pole.AddComponent<SpriteRenderer>();
            sr.sprite = WorldManifestApplier.GetUniversalWhiteSprite();
            pole.transform.localScale = new Vector3(0.15f, 1.2f, 1f);
            sr.sortingOrder = 3;

            // Dynamic Streetlight Controller (§5.3A)
            var ctrl = pole.AddComponent<StreetlightController>();
            ctrl.ConfigureForModule(module);
        }

        private static void CreateRoleMarker(Transform parent, string label, Vector3 center, Color color)
        {
            GameObject marker = new GameObject($"RoleMarker_{label}");
            marker.transform.SetParent(parent);
            marker.transform.position = center + new Vector3(0f, CELL_HEIGHT / 2f - 0.6f, 0f);

            SpriteRenderer sr = marker.AddComponent<SpriteRenderer>();
            sr.sprite = WorldManifestApplier.GetUniversalWhiteSprite();
            marker.transform.localScale = new Vector3(1.6f, 0.5f, 1f);
            sr.color = color;
            sr.sortingOrder = 20;
        }

        private static void TryAttachShadowCaster(GameObject go)
        {
#if UNITY_URP || UNITY_2021_2_OR_NEWER
            var casterType = System.Type.GetType("UnityEngine.Rendering.Universal.ShadowCaster2D, Unity.RenderPipelines.Universal.Runtime");
            if (casterType != null)
            {
                var caster = go.AddComponent(casterType);
                var selfShadowsProp = casterType.GetProperty("selfShadows");
                selfShadowsProp?.SetValue(caster, true);
            }
#endif
        }

        private static void BuildPerimeterBoundaries(Transform parent, GridDimensions dims = null)
        {
            if (dims == null) dims = new GridDimensions();

            GameObject perim = new GameObject("PerimeterBoundaries");
            perim.transform.SetParent(parent);

            float halfW = (dims.columns * dims.cell_width)  / 2f;
            float halfH = (dims.rows    * dims.cell_height) / 2f;
            float thickness = 1.5f;
            Color wallColor = new Color(0.18f, 0.17f, 0.16f);

            // North
            CreateSolidWall(perim.transform, "Perimeter_N", new Vector3(0f, halfH + thickness / 2f, 0f),
                new Vector2(halfW * 2f + thickness * 2f, thickness), LAYER_BUILDING, wallColor);
            // South
            CreateSolidWall(perim.transform, "Perimeter_S", new Vector3(0f, -(halfH + thickness / 2f), 0f),
                new Vector2(halfW * 2f + thickness * 2f, thickness), LAYER_BUILDING, wallColor);
            // West
            CreateSolidWall(perim.transform, "Perimeter_W", new Vector3(-(halfW + thickness / 2f), 0f, 0f),
                new Vector2(thickness, halfH * 2f + thickness * 2f), LAYER_BUILDING, wallColor);
            // East
            CreateSolidWall(perim.transform, "Perimeter_E", new Vector3(halfW + thickness / 2f, 0f, 0f),
                new Vector2(thickness, halfH * 2f + thickness * 2f), LAYER_BUILDING, wallColor);
        }

        // ─────────────────────────────────────────────────────────────
        //  PROP SYSTEM (Leap 3)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Instantiates every structured prop in a sector's props list.
        /// </summary>
        private static void BuildProps(Transform parent, SectorConfig sector, Vector3 center)
        {
            if (sector.props == null) return;
            int idx = 0;
            foreach (PropData p in sector.props)
            {
                if (p == null) continue;
                BuildProp(parent, p, center, sector.id, idx++);
            }
        }

        /// <summary>
        /// Dispatches a single <see cref="PropData"/> record to the appropriate factory method.
        /// Unknown prop types fall back to a generic crate_stack so levels never have holes.
        /// </summary>
        private static void BuildProp(Transform parent, PropData p, Vector3 center, string sectorId, int idx)
        {
            Vector3 pos = center + new Vector3(p.offset_x, p.offset_y, 0f);
            string uid = $"{sectorId}_{p.type}_{idx:D2}";

            switch (p.type)
            {
                case "crate_stack":
                case "crate":           // legacy alias
                    CreateCrateStack(parent, uid, pos, p.destructible);
                    break;

                case "power_breaker":
                    CreatePowerBreaker(parent, uid, pos, p.interact_action);
                    break;

                case "alarm_panel":
                    CreateAlarmPanel(parent, uid, pos, p.interact_action);
                    break;

                case "explosive_barrel":
                case "fuel_barrel":     // legacy alias
                    CreateExplosiveBarrel(parent, uid, pos, p.destructible);
                    break;

                case "vent_shaft":
                    CreateVentShaft(parent, uid, pos, p.connects_to);
                    break;

                case "surveillance_spotlight":
                    CreateSurveillanceSpotlight(parent, uid, pos);
                    break;

                case "car":
                    CreatePropCar(parent, uid, pos, p.destructible);
                    break;

                default:
                    Debug.LogWarning($"[SectorRuntimeBuilder] Unknown prop type '{p.type}' in {sectorId} -- falling back to crate_stack.");
                    CreateCrateStack(parent, uid, pos, false);
                    break;
            }
        }

        // ──── Prop factories ─────────────────────────────────────────

        private static void CreateCrateStack(Transform parent, string uid, Vector3 pos, bool destructible)
        {
            if (destructible)
            {
                // Re-use existing destructible wall at crate scale.
                CreateDestructible(parent, uid, pos, new Vector2(1.4f, 1.1f), 40, new Color(0.5f, 0.42f, 0.30f));
            }
            else
            {
                CreateCrate(parent, uid, pos);
            }
        }

        private static void CreatePowerBreaker(Transform parent, string uid, Vector3 pos, string interactAction)
        {
            GameObject go = new GameObject(uid);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.layer = LAYER_INTERACTABLE;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldManifestApplier.GetUniversalWhiteSprite();
            go.transform.localScale = new Vector3(0.5f, 0.8f, 1f);
            sr.color = new Color(0.15f, 0.55f, 1f);   // blue panel
            sr.sortingOrder = 3;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.9f, 0.9f);

            var pb = go.AddComponent<PowerBreaker>();
            pb.interactAction = interactAction;
        }

        private static void CreateAlarmPanel(Transform parent, string uid, Vector3 pos, string interactAction)
        {
            GameObject go = new GameObject(uid);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.layer = LAYER_INTERACTABLE;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldManifestApplier.GetUniversalWhiteSprite();
            go.transform.localScale = new Vector3(0.55f, 0.7f, 1f);
            sr.color = new Color(0.9f, 0.2f, 0.2f);   // red panel
            sr.sortingOrder = 3;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.9f, 0.9f);

            var ap = go.AddComponent<AlarmPanel>();
            ap.interactAction = interactAction;
        }

        private static void CreateExplosiveBarrel(Transform parent, string uid, Vector3 pos, bool destructible)
        {
            int hp = destructible ? 30 : 60;
            GameObject go = CreateSolidWall(parent, uid, pos, new Vector2(0.8f, 1.0f), LAYER_INTERACTABLE, new Color(0.75f, 0.25f, 0.15f));
            var hazard = go.AddComponent<ExplosiveHazard>();
            hazard.hazardId = uid.ToLower();
            hazard.maxHealth = hp;
            hazard.currentHealth = hp;
        }

        private static void CreateVentShaft(Transform parent, string uid, Vector3 pos, string connectsTo)
        {
            GameObject go = new GameObject(uid);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.layer = LAYER_INTERACTABLE;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldManifestApplier.GetUniversalWhiteSprite();
            go.transform.localScale = new Vector3(1.2f, 0.5f, 1f);
            sr.color = new Color(0.5f, 0.5f, 0.55f, 0.8f);  // grey vent grate
            sr.sortingOrder = 2;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1.0f, 0.8f);
            col.isTrigger = true;

            var vs = go.AddComponent<VentShaft>();
            vs.destinationSectorId = connectsTo;
        }

        private static void CreateSurveillanceSpotlight(Transform parent, string uid, Vector3 pos)
        {
            GameObject go = new GameObject(uid);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.layer = LAYER_INTERACTABLE;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldManifestApplier.GetUniversalWhiteSprite();
            go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
            sr.color = new Color(0.9f, 0.85f, 0.4f);  // yellow spotlight head
            sr.sortingOrder = 4;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(0.8f, 0.8f);
            col.isTrigger = true;

            go.AddComponent<SurveillanceSpotlight>();
        }

        private static void CreatePropCar(Transform parent, string uid, Vector3 pos, bool destructible)
        {
            // Re-use existing BurnableCar setup via a slim wrapper to keep runtime size small.
            GameObject go = new GameObject(uid);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            go.layer = LAYER_INTERACTABLE;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WorldManifestApplier.GetUniversalWhiteSprite();
            go.transform.localScale = new Vector3(2.4f, 1.2f, 1f);
            sr.color = new Color(0.4f, 0.4f, 0.45f);   // dark grey parked car
            sr.sortingOrder = 2;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(2.0f, 1.0f);

            if (destructible)
                go.AddComponent<BurnableCar>();
        }


        // ─────────────────────────────────────────────────────────────
        //  SPRITE UTILITIES
        // ─────────────────────────────────────────────────────────────

        private static Sprite _universalWhiteSprite;

        /// <summary>
        /// Runtime-compatible white 2×2 sprite generator, cached statically to avoid repeated
        /// texture allocations. Lives in the World namespace so that SectorRuntimeBuilder,
        /// PlayerCombat, and AdaptiveSceneBuilder do not import the Director namespace for this.
        /// </summary>
        public static Sprite GetUniversalWhiteSprite()
        {
            if (_universalWhiteSprite == null)
            {
                Texture2D tex = new Texture2D(2, 2);
                tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
                tex.Apply();
                _universalWhiteSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, 2, 2),
                    new Vector2(0.5f, 0.5f),
                    1f,
                    0,
                    SpriteMeshType.FullRect);
            }
            return _universalWhiteSprite;
        }
    }
}
