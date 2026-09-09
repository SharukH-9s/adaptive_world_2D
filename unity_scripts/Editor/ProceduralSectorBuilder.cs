#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using AdaptiveWorld2D.Director;
using AdaptiveWorld2D.World;

/// <summary>
/// ProceduralSectorBuilder -- Phase 3 of the Map 2 AI-Driven Sector Graph redesign.
///
/// Builds ONLY the physical static architecture (ground, walls, roads, buildings, and
/// inter-sector gateways) of Map 2's 3x3 tactical sector grid (60x45 canvas) from a
/// RoomGraphManifest. Dynamic entities (Viktor, guards, cars, extraction trigger) are
/// intentionally out of scope here -- that is Phase 4. Atmospheric lighting (streetlight
/// Light2D components, weather particles) is intentionally out of scope here -- that is
/// Phase 5. This script only places placeholder streetlight pole markers for Phase 5 to
/// later attach lights to.
///
/// Source of the RoomGraphManifest:
///   1. AdaptiveDirectorClient.latestManifest.room_graph, if the game has been played through
///      Mission 1 in this Editor session (static state may not survive domain reload).
///   2. One of five bundled offline test manifests (TestManifests below), which mirror
///      backend/target_solver.py's build_room_graph() output EXACTLY, field-for-field --
///      allowing full Editor-time iteration without Play mode or the Python backend running.
///
/// Menu Items (Unity menu bar):
///   AdaptiveWorld > Build Map 2 -- Sector Graph > From Live or Cached Manifest
///   AdaptiveWorld > Build Map 2 -- Sector Graph > Test - GHOST / WRECKING_BALL / SPEEDRUNNER /
///                                                  AGGRESSOR / BALANCED
///   AdaptiveWorld > Build Map 2 -- Sector Graph > Clear
///
/// Sector grid: columns X = -20/0/+20 (W/C/E), rows Y = +15/0/-15 (N/M/S), cell 20 wide x 15 tall.
/// </summary>
public static class ProceduralSectorBuilder
{
    // ─────────────────────────────────────────────────────────────
    //  Layer indices (must match Edit > Project Settings > Layers -- see AdaptiveSceneBuilder.cs)
    // ─────────────────────────────────────────────────────────────
    private const int LAYER_INTERACTABLE = 6;
    private const int LAYER_ROAD         = 8;
    private const int LAYER_ALLEY        = 9;
    private const int LAYER_BUILDING     = 10;

    private const float CELL_WIDTH  = 20f;
    private const float CELL_HEIGHT = 15f;
    private const string ROOT_NAME = "Map2_SectorGraph";

    private static readonly Dictionary<string, Vector2> SectorCoords = new Dictionary<string, Vector2>
    {
        { "NW", new Vector2(-20f, 15f) }, { "NC", new Vector2(0f, 15f) }, { "NE", new Vector2(20f, 15f) },
        { "MW", new Vector2(-20f,  0f) }, { "MC", new Vector2(0f,  0f) }, { "ME", new Vector2(20f,  0f) },
        { "SW", new Vector2(-20f,-15f) }, { "SC", new Vector2(0f,-15f) }, { "SE", new Vector2(20f,-15f) },
    };

    // All 12 unique adjacent pairs (horizontal + vertical neighbors only, no diagonals).
    private static readonly (string a, string b)[] Adjacency =
    {
        ("NW","NC"), ("NC","NE"),
        ("MW","MC"), ("MC","ME"),
        ("SW","SC"), ("SC","SE"),
        ("NW","MW"), ("MW","SW"),
        ("NC","MC"), ("MC","SC"),
        ("NE","ME"), ("ME","SE"),
    };

    private static readonly HashSet<string> ValidStatuses = new HashSet<string>
    {
        "open", "barricaded_wire_fence", "barricaded_concrete", "walled_off"
    };

    // ═════════════════════════════════════════════════════════════
    //  MENU ITEMS
    // ═════════════════════════════════════════════════════════════
    [MenuItem("AdaptiveWorld/Build Map 2 — Sector Graph/From Live or Cached Manifest")]
    public static void BuildFromLiveManifest()
    {
        RoomGraphManifest rg = AdaptiveDirectorClient.latestManifest != null
            ? AdaptiveDirectorClient.latestManifest.room_graph
            : null;

        if (rg == null || rg.sectors == null || rg.sectors.Count != 9)
        {
            Debug.LogWarning("[ProceduralSectorBuilder] No valid cached room_graph found (Play through Mission 1 first to " +
                              "populate AdaptiveDirectorClient.latestManifest, or use a 'Test - <ARCHETYPE>' menu item instead). " +
                              "Falling back to Test - BALANCED for preview.");
            rg = TestManifests.Balanced();
        }
        else
        {
            Debug.Log("[ProceduralSectorBuilder] Building from live/cached manifest " +
                      $"(archetype={AdaptiveDirectorClient.latestManifest.player_archetype}).");
        }
        Build(rg);
    }

    [MenuItem("AdaptiveWorld/Build Map 2 — Sector Graph/Test - GHOST")]
    public static void BuildTestGhost() => Build(TestManifests.Ghost());

    [MenuItem("AdaptiveWorld/Build Map 2 — Sector Graph/Test - WRECKING_BALL")]
    public static void BuildTestWreckingBall() => Build(TestManifests.WreckingBall());

    [MenuItem("AdaptiveWorld/Build Map 2 — Sector Graph/Test - SPEEDRUNNER")]
    public static void BuildTestSpeedrunner() => Build(TestManifests.Speedrunner());

    [MenuItem("AdaptiveWorld/Build Map 2 — Sector Graph/Test - AGGRESSOR")]
    public static void BuildTestAggressor() => Build(TestManifests.Aggressor());

    [MenuItem("AdaptiveWorld/Build Map 2 — Sector Graph/Test - BALANCED")]
    public static void BuildTestBalanced() => Build(TestManifests.Balanced());

    [MenuItem("AdaptiveWorld/Build Map 2 — Sector Graph/Clear")]
    public static void ClearMenuItem()
    {
        ClearPrevious();
        Debug.Log("[ProceduralSectorBuilder] Cleared sector graph.");
    }

    // ═════════════════════════════════════════════════════════════
    //  CORE BUILD PIPELINE
    // ═════════════════════════════════════════════════════════════
    public static void Build(RoomGraphManifest roomGraph)
    {
        if (roomGraph == null)
        {
            Debug.LogError("[ProceduralSectorBuilder] room_graph is null -- aborting build.");
            return;
        }

        List<string> errors = Validate(roomGraph);
        if (errors.Count > 0)
        {
            Debug.LogError("[ProceduralSectorBuilder] room_graph failed validation, aborting build:\n - " + string.Join("\n - ", errors));
            return;
        }

        Debug.Log($"[ProceduralSectorBuilder] Building 9-sector grid | spawn={roomGraph.player_spawn_sector} " +
                  $"-> target={roomGraph.target_sector} -> extraction={roomGraph.extraction_sector}");

        ClearPrevious();
        GameObject root = new GameObject(ROOT_NAME);

        foreach (var sector in roomGraph.sectors)
        {
            Vector3 center = new Vector3(sector.center_x, sector.center_y, 0f);
            GameObject sectorRoot = new GameObject($"Sector_{sector.id}_{sector.module}");
            sectorRoot.transform.SetParent(root.transform);
            sectorRoot.transform.position = center;

            BuildModule(sectorRoot.transform, sector.module, center);

            if (sector.id == roomGraph.player_spawn_sector) CreateRoleMarker(sectorRoot.transform, "SPAWN", center, new Color(0.2f, 0.9f, 1f, 0.9f));
            if (sector.id == roomGraph.target_sector) CreateRoleMarker(sectorRoot.transform, "TARGET", center, new Color(1f, 0.2f, 0.2f, 0.9f));
            if (sector.id == roomGraph.extraction_sector) CreateRoleMarker(sectorRoot.transform, "EXTRACTION", center, new Color(0.3f, 1f, 0.3f, 0.9f));
        }

        GameObject gatewaysRoot = new GameObject("Gateways");
        gatewaysRoot.transform.SetParent(root.transform);
        foreach (var conn in roomGraph.connections)
        {
            BuildGateway(gatewaysRoot.transform, roomGraph, conn);
        }

        Debug.Log("[ProceduralSectorBuilder] Sector grid build complete. Save the scene now (Ctrl+S).");
        EditorUtility.DisplayDialog("Map 2 Sector Graph Built!",
            $"9 sectors + {roomGraph.connections.Count} gateways generated.\n\n" +
            $"Spawn: {roomGraph.player_spawn_sector}\nTarget: {roomGraph.target_sector}\nExtraction: {roomGraph.extraction_sector}\n\n" +
            "Dynamic entities (Viktor, guards, cars) and lighting are added in later phases.\n\n" +
            "Save the scene now (Ctrl+S).", "Got it!");
    }

    private static void ClearPrevious()
    {
        GameObject existing = GameObject.Find(ROOT_NAME);
        if (existing != null) Object.DestroyImmediate(existing);
    }

    /// <summary>Structural validation mirroring backend/target_solver.py's RoomGraphManifest.validate()
    /// (schema shape only -- connectivity is already guaranteed server-side and re-checked in Phase 2's
    /// AdaptiveDirectorClient logging, so it is not re-verified here).</summary>
    private static List<string> Validate(RoomGraphManifest rg)
    {
        var errors = new List<string>();

        if (rg.sectors == null || rg.sectors.Count != 9)
        {
            errors.Add($"Expected 9 sectors, got {(rg.sectors != null ? rg.sectors.Count : 0)}.");
        }
        else
        {
            var ids = new HashSet<string>(rg.sectors.Select(s => s.id));
            var expected = new HashSet<string>(SectorCoords.Keys);
            if (!ids.SetEquals(expected))
                errors.Add($"Sector id mismatch. Expected [{string.Join(",", expected)}], got [{string.Join(",", ids)}].");
        }

        if (rg.connections == null || rg.connections.Count != 12)
        {
            errors.Add($"Expected 12 connections, got {(rg.connections != null ? rg.connections.Count : 0)}.");
        }
        else
        {
            foreach (var c in rg.connections)
                if (!ValidStatuses.Contains(c.status))
                    errors.Add($"Connection {c.from_sector}->{c.to_sector} has invalid status '{c.status}'.");
        }

        if (string.IsNullOrEmpty(rg.target_sector)) errors.Add("target_sector is empty.");
        if (string.IsNullOrEmpty(rg.player_spawn_sector)) errors.Add("player_spawn_sector is empty.");
        if (string.IsNullOrEmpty(rg.extraction_sector)) errors.Add("extraction_sector is empty.");

        return errors;
    }

    // ═════════════════════════════════════════════════════════════
    //  MODULE DISPATCH
    // ═════════════════════════════════════════════════════════════
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
                Debug.LogWarning($"[ProceduralSectorBuilder] Unknown module '{module}' -- building generic OPEN_STREET fallback.");
                BuildOpenStreet(parent, center);
                break;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  MODULE CONSTRUCTORS  (each fills a 20 x 15 cell centered at `center`)
    // ═════════════════════════════════════════════════════════════
    private static void BuildWarehouseInterior(Transform parent, Vector3 center)
    {
        CreateGroundQuad(parent, "Ground_WarehouseFloor", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_BUILDING,
            new Color(0.30f, 0.28f, 0.25f));

        // 14 x 10 perimeter walls with 2 door cutouts (north + south gap).
        float w = 14f, h = 10f, t = 0.6f;
        Color wallColor = new Color(0.45f, 0.42f, 0.38f);

        CreateSolidWall(parent, "WarehouseWall_N_L", center + new Vector3(-w / 4f - 1f, h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
        CreateSolidWall(parent, "WarehouseWall_N_R", center + new Vector3(w / 4f + 1f, h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
        CreateSolidWall(parent, "WarehouseWall_S_L", center + new Vector3(-w / 4f - 1f, -h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
        CreateSolidWall(parent, "WarehouseWall_S_R", center + new Vector3(w / 4f + 1f, -h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
        CreateSolidWall(parent, "WarehouseWall_W", center + new Vector3(-w / 2f, 0f, 0f), new Vector2(t, h), LAYER_BUILDING, wallColor);
        CreateSolidWall(parent, "WarehouseWall_E", center + new Vector3(w / 2f, 0f, 0f), new Vector2(t, h), LAYER_BUILDING, wallColor);

        // Interior partition (splits the floor, leaving a center gap to pass through).
        CreateSolidWall(parent, "WarehousePartition_Top", center + new Vector3(0f, h / 4f + 0.75f, 0f), new Vector2(t, h / 2f - 1.5f), LAYER_BUILDING, wallColor);
        CreateSolidWall(parent, "WarehousePartition_Bottom", center + new Vector3(0f, -h / 4f - 0.75f, 0f), new Vector2(t, h / 2f - 1.5f), LAYER_BUILDING, wallColor);

        // Crate cover clusters.
        CreateCrate(parent, "WarehouseCrate_01", center + new Vector3(-3f, 2f, 0f));
        CreateCrate(parent, "WarehouseCrate_02", center + new Vector3(-3f, 1f, 0f));
        CreateCrate(parent, "WarehouseCrate_03", center + new Vector3(3f, -2f, 0f));
        CreateCrate(parent, "WarehouseCrate_04", center + new Vector3(3f, -3f, 0f));
    }

    private static void BuildFortifiedPlaza(Transform parent, Vector3 center)
    {
        CreateGroundQuad(parent, "Ground_Plaza", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ROAD,
            new Color(0.42f, 0.40f, 0.38f));

        // 4 sandbag/barrier clusters at the corners of an inner square -- center stays open.
        Vector3[] offsets = { new Vector3(-5f, 4f, 0f), new Vector3(5f, 4f, 0f), new Vector3(-5f, -4f, 0f), new Vector3(5f, -4f, 0f) };
        for (int i = 0; i < offsets.Length; i++)
            CreateDestructible(parent, $"PlazaBarrier_{i:D2}", center + offsets[i], new Vector2(2.1f, 0.62f), 100, new Color(0.55f, 0.53f, 0.5f));

        // Central plaza marker (visual only, no collider).
        GameObject marker = new GameObject("PlazaMarker");
        marker.transform.SetParent(parent);
        marker.transform.position = center;
        SpriteRenderer sr = marker.AddComponent<SpriteRenderer>();
        sr.sprite = GetWhiteSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(3f, 3f);
        sr.color = new Color(0.75f, 0.7f, 0.3f, 0.5f);
        sr.sortingOrder = -5;
    }

    private static void BuildSlumAlleyways(Transform parent, Vector3 center)
    {
        CreateGroundQuad(parent, "Ground_Alley", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ALLEY,
            new Color(0.22f, 0.22f, 0.24f));

        // Tenement facades framing a cross-shaped alley path (2 narrow corridor gaps through the middle).
        Color facadeColor = new Color(0.29f, 0.27f, 0.26f);
        CreateSolidWall(parent, "TenementFacade_NW", center + new Vector3(-6f, 4f, 0f), new Vector2(6f, 4f), LAYER_BUILDING, facadeColor);
        CreateSolidWall(parent, "TenementFacade_NE", center + new Vector3(6f, 4f, 0f), new Vector2(6f, 4f), LAYER_BUILDING, facadeColor);
        CreateSolidWall(parent, "TenementFacade_SW", center + new Vector3(-6f, -4f, 0f), new Vector2(6f, 4f), LAYER_BUILDING, facadeColor);
        CreateSolidWall(parent, "TenementFacade_SE", center + new Vector3(6f, -4f, 0f), new Vector2(6f, 4f), LAYER_BUILDING, facadeColor);
    }

    private static void BuildUrbanHighway(Transform parent, Vector3 center)
    {
        CreateGroundQuad(parent, "Ground_Highway", center, new Vector2(CELL_WIDTH, CELL_HEIGHT - 3f), LAYER_ROAD,
            new Color(0.16f, 0.16f, 0.18f));

        Color sidewalkColor = new Color(0.5f, 0.5f, 0.48f);
        CreateGroundQuad(parent, "Sidewalk_N", center + new Vector3(0f, CELL_HEIGHT / 2f - 1f, 0f), new Vector2(CELL_WIDTH, 2f), LAYER_ROAD, sidewalkColor);
        CreateGroundQuad(parent, "Sidewalk_S", center + new Vector3(0f, -(CELL_HEIGHT / 2f - 1f), 0f), new Vector2(CELL_WIDTH, 2f), LAYER_ROAD, sidewalkColor);

        // Streetlight pole markers only -- Phase 5 attaches the actual Light2D components.
        CreateStreetlightMarker(parent, "Streetlight_W", center + new Vector3(-8f, CELL_HEIGHT / 2f - 1.6f, 0f));
        CreateStreetlightMarker(parent, "Streetlight_C", center + new Vector3(0f, CELL_HEIGHT / 2f - 1.6f, 0f));
        CreateStreetlightMarker(parent, "Streetlight_E", center + new Vector3(8f, CELL_HEIGHT / 2f - 1.6f, 0f));
    }

    private static void BuildIndustrialDepot(Transform parent, Vector3 center)
    {
        CreateGroundQuad(parent, "Ground_Depot", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ROAD,
            new Color(0.32f, 0.30f, 0.26f));

        // Container stack cover -- solid, non-destructible heavy steel (blocks movement and vision).
        Vector3[] offsets = { new Vector3(-6f, 3f, 0f), new Vector3(-6f, -3f, 0f), new Vector3(6f, 3f, 0f), new Vector3(6f, -3f, 0f) };
        for (int i = 0; i < offsets.Length; i++)
            CreateSolidWall(parent, $"Container_{i:D2}", center + offsets[i], new Vector2(3.5f, 1.8f), LAYER_BUILDING, new Color(0.55f, 0.30f, 0.15f));
    }

    private static void BuildWaterfrontDocks(Transform parent, Vector3 center)
    {
        CreateGroundQuad(parent, "Ground_Pier", center, new Vector2(CELL_WIDTH, CELL_HEIGHT - 4f), LAYER_ROAD,
            new Color(0.40f, 0.36f, 0.28f));

        // Water hazard boundary along the far edge -- non-traversable, not destructible (it's water).
        CreateSolidWall(parent, "WaterBoundary", center + new Vector3(0f, -(CELL_HEIGHT / 2f - 1f), 0f), new Vector2(CELL_WIDTH, 2f), LAYER_BUILDING,
            new Color(0.10f, 0.25f, 0.45f));
    }

    private static void BuildSecurityCompound(Transform parent, Vector3 center)
    {
        CreateGroundQuad(parent, "Ground_Compound", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_BUILDING,
            new Color(0.24f, 0.24f, 0.26f));

        float w = 16f, h = 11f, t = 0.6f;
        Color wallColor = new Color(0.32f, 0.32f, 0.34f);

        CreateSolidWall(parent, "CompoundWall_N", center + new Vector3(0f, h / 2f, 0f), new Vector2(w, t), LAYER_BUILDING, wallColor);
        CreateSolidWall(parent, "CompoundWall_W", center + new Vector3(-w / 2f, 0f, 0f), new Vector2(t, h), LAYER_BUILDING, wallColor);
        CreateSolidWall(parent, "CompoundWall_E", center + new Vector3(w / 2f, 0f, 0f), new Vector2(t, h), LAYER_BUILDING, wallColor);
        // Single guarded gate gap on the south wall.
        CreateSolidWall(parent, "CompoundWall_S_L", center + new Vector3(-w / 4f - 1f, -h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);
        CreateSolidWall(parent, "CompoundWall_S_R", center + new Vector3(w / 4f + 1f, -h / 2f, 0f), new Vector2(w / 2f - 2f, t), LAYER_BUILDING, wallColor);

        // Gate marker (visual only -- the actual gate sentry guard is spawned in Phase 4).
        GameObject gate = new GameObject("CompoundGate_Marker");
        gate.transform.SetParent(parent);
        gate.transform.position = center + new Vector3(0f, -h / 2f, 0f);
        SpriteRenderer sr = gate.AddComponent<SpriteRenderer>();
        sr.sprite = GetWhiteSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(2f, 0.3f);
        sr.color = new Color(0.9f, 0.75f, 0.15f, 0.8f);
        sr.sortingOrder = 1;
    }

    private static void BuildOpenStreet(Transform parent, Vector3 center)
    {
        CreateGroundQuad(parent, "Ground_OpenStreet", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ROAD,
            new Color(0.30f, 0.30f, 0.32f));
        CreateCrate(parent, "StreetCrate_01", center + new Vector3(-4f, 3f, 0f));
        CreateCrate(parent, "StreetCrate_02", center + new Vector3(4f, -3f, 0f));
    }

    private static void BuildVacantLot(Transform parent, Vector3 center)
    {
        // Generic sparse open connector -- minimal cover, mostly empty ground.
        CreateGroundQuad(parent, "Ground_VacantLot", center, new Vector2(CELL_WIDTH, CELL_HEIGHT), LAYER_ROAD,
            new Color(0.34f, 0.33f, 0.28f));
    }

    // ═════════════════════════════════════════════════════════════
    //  INTER-SECTOR GATEWAYS
    // ═════════════════════════════════════════════════════════════
    private static void BuildGateway(Transform parent, RoomGraphManifest rg, ConnectionConfig conn)
    {
        if (conn.status == "open") return; // no object placed -- sectors flow freely into each other

        SectorConfig from = rg.GetSector(conn.from_sector);
        SectorConfig to = rg.GetSector(conn.to_sector);
        if (from == null || to == null)
        {
            Debug.LogWarning($"[ProceduralSectorBuilder] Gateway {conn.from_sector}-{conn.to_sector} references an unknown sector -- skipped.");
            return;
        }

        Vector3 a = new Vector3(from.center_x, from.center_y, 0f);
        Vector3 b = new Vector3(to.center_x, to.center_y, 0f);
        Vector3 mid = (a + b) / 2f;

        // Side-by-side (E/W) neighbors share a vertical boundary; stacked (N/S) neighbors share a horizontal one.
        bool sideBySide = Mathf.Abs(a.x - b.x) > Mathf.Abs(a.y - b.y);
        Vector2 size = sideBySide
            ? new Vector2(0.6f, CELL_HEIGHT * 0.6f)   // vertical gate
            : new Vector2(CELL_WIDTH * 0.6f, 0.6f);   // horizontal gate

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
                CreateSolidWall(parent, name, mid, size, LAYER_BUILDING, new Color(0.2f, 0.2f, 0.22f));
                break;

            default:
                Debug.LogWarning($"[ProceduralSectorBuilder] Unknown connection status '{conn.status}' on {name} -- treated as walled_off.");
                CreateSolidWall(parent, name, mid, size, LAYER_BUILDING, new Color(0.2f, 0.2f, 0.22f));
                break;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  LOW-LEVEL GEOMETRY HELPERS
    // ═════════════════════════════════════════════════════════════
    private static Sprite GetWhiteSprite() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    private static GameObject CreateGroundQuad(Transform parent, string name, Vector3 center, Vector2 size, int layer, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = center;
        go.layer = layer;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetWhiteSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = color;
        sr.sortingOrder = -10;

        // Trigger-only: this is a walkable surface-detection zone (PlayerController.DetectSurface),
        // never a physical obstacle.
        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.size = size;
        col.isTrigger = true;
        return go;
    }

    private static GameObject CreateSolidWall(Transform parent, string name, Vector3 pos, Vector2 size, int layer, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.layer = layer;

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetWhiteSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = color;
        sr.sortingOrder = 0;

        BoxCollider2D col = go.AddComponent<BoxCollider2D>();
        col.size = size;
        col.isTrigger = false; // solid -- blocks movement and enemy vision
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
        sr.sprite = GetWhiteSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(0.8f, 0.8f);
        sr.color = new Color(0.5f, 0.42f, 0.3f);
        sr.sortingOrder = 2;

        BoxCollider2D col = crate.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.7f, 0.7f);
    }

    private static void CreateStreetlightMarker(Transform parent, string name, Vector3 pos)
    {
        GameObject pole = new GameObject(name);
        pole.transform.SetParent(parent);
        pole.transform.position = pos;

        SpriteRenderer sr = pole.AddComponent<SpriteRenderer>();
        sr.sprite = GetWhiteSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(0.15f, 1.2f);
        sr.color = new Color(0.2f, 0.2f, 0.22f);
        sr.sortingOrder = 3;
        // Phase 5 attaches a UnityEngine.Rendering.Universal.Light2D here, colored/dimmed by
        // manifest.environment.ambient_light.
    }

    /// <summary>Small non-gameplay visual tag (no collider, no components) marking the
    /// spawn/target/extraction sector so the layout is readable at a glance in the Editor.
    /// Actual gameplay entities (player, Viktor, extraction trigger) are placed in Phase 4.</summary>
    private static void CreateRoleMarker(Transform parent, string label, Vector3 center, Color color)
    {
        GameObject marker = new GameObject($"RoleMarker_{label}");
        marker.transform.SetParent(parent);
        marker.transform.position = center + new Vector3(0f, CELL_HEIGHT / 2f - 0.6f, 0f);

        SpriteRenderer sr = marker.AddComponent<SpriteRenderer>();
        sr.sprite = GetWhiteSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(1.6f, 0.5f);
        sr.color = color;
        sr.sortingOrder = 20;
    }

    // ═════════════════════════════════════════════════════════════
    //  BUNDLED TEST MANIFESTS
    //  Mirrors backend/target_solver.py's build_room_graph() output EXACTLY, archetype for
    //  archetype, sector for sector, connection for connection -- so Editor-time previews are
    //  guaranteed structurally identical to what the live backend would send.
    // ═════════════════════════════════════════════════════════════
    private static class TestManifests
    {
        private static SectorConfig S(string id, string module, List<GuardEntityConfig> guards = null, List<string> props = null)
        {
            Vector2 c = SectorCoords[id];
            return new SectorConfig
            {
                id = id,
                module = module,
                center_x = c.x,
                center_y = c.y,
                guards = guards ?? new List<GuardEntityConfig>(),
                props = props ?? new List<string>(),
            };
        }

        private static GuardEntityConfig G(string sectorId, float ox, float oy, string role, bool armored = false) =>
            new GuardEntityConfig { sector_id = sectorId, offset_x = ox, offset_y = oy, role = role, armored = armored };

        private static List<ConnectionConfig> Edges(Dictionary<(string, string), string> statusMap)
        {
            var result = new List<ConnectionConfig>();
            foreach (var (a, b) in Adjacency)
            {
                string status = "open";
                if (statusMap.TryGetValue((a, b), out var s1)) status = s1;
                else if (statusMap.TryGetValue((b, a), out var s2)) status = s2;
                result.Add(new ConnectionConfig { from_sector = a, to_sector = b, status = status });
            }
            return result;
        }

        public static RoomGraphManifest Ghost() => new RoomGraphManifest
        {
            sectors = new List<SectorConfig>
            {
                S("SW", "SLUM_ALLEYWAYS", guards: new List<GuardEntityConfig> { G("SW", -1, 0, "patrol") }),
                S("SC", "SLUM_ALLEYWAYS", guards: new List<GuardEntityConfig> { G("SC", 0, 0, "sentry") }),
                S("SE", "VACANT_LOT"),
                S("MW", "SLUM_ALLEYWAYS", guards: new List<GuardEntityConfig> { G("MW", 1, 0, "patrol") }),
                S("MC", "OPEN_STREET"),
                S("ME", "URBAN_HIGHWAY", guards: new List<GuardEntityConfig> { G("ME", 0, 1, "patrol") }),
                S("NW", "WAREHOUSE_INTERIOR",
                    props: new List<string> { "crate", "crate" },
                    guards: new List<GuardEntityConfig> { G("NW", -2, 0, "bodyguard", true), G("NW", 2, 0, "bodyguard", true) }),
                S("NC", "FORTIFIED_PLAZA", guards: new List<GuardEntityConfig> { G("NC", 0, 0, "sentry") }),
                S("NE", "WATERFRONT_DOCKS"),
            },
            connections = Edges(new Dictionary<(string, string), string>
            {
                { ("NW", "NC"), "open" }, { ("NC", "NE"), "barricaded_wire_fence" },
                { ("MW", "MC"), "barricaded_wire_fence" }, { ("MC", "ME"), "walled_off" },
                { ("SW", "SC"), "open" }, { ("SC", "SE"), "barricaded_concrete" },
                { ("NW", "MW"), "barricaded_wire_fence" }, { ("MW", "SW"), "open" },
                { ("NC", "MC"), "barricaded_wire_fence" }, { ("MC", "SC"), "walled_off" },
                { ("NE", "ME"), "barricaded_wire_fence" }, { ("ME", "SE"), "open" },
            }),
            target_sector = "NW",
            player_spawn_sector = "SW",
            extraction_sector = "NE",
        };

        public static RoomGraphManifest WreckingBall() => new RoomGraphManifest
        {
            sectors = new List<SectorConfig>
            {
                S("SW", "INDUSTRIAL_DEPOT", props: new List<string> { "fuel_barrel", "fuel_barrel" }),
                S("SC", "INDUSTRIAL_DEPOT", props: new List<string> { "fuel_barrel" }),
                S("SE", "VACANT_LOT"),
                S("MW", "URBAN_HIGHWAY", props: new List<string> { "car" }),
                S("MC", "FORTIFIED_PLAZA",
                    guards: new List<GuardEntityConfig> { G("MC", -2, 0, "bodyguard", true), G("MC", 2, 0, "bodyguard", true) }),
                S("ME", "URBAN_HIGHWAY", props: new List<string> { "car" }),
                S("NW", "WAREHOUSE_INTERIOR"),
                S("NC", "OPEN_STREET"),
                S("NE", "WATERFRONT_DOCKS"),
            },
            connections = Edges(new Dictionary<(string, string), string>
            {
                { ("NW", "NC"), "walled_off" }, { ("NC", "NE"), "barricaded_concrete" },
                { ("MW", "MC"), "barricaded_concrete" }, { ("MC", "ME"), "barricaded_concrete" },
                { ("SW", "SC"), "open" }, { ("SC", "SE"), "walled_off" },
                { ("NW", "MW"), "barricaded_concrete" }, { ("MW", "SW"), "open" },
                { ("NC", "MC"), "barricaded_concrete" }, { ("MC", "SC"), "walled_off" },
                { ("NE", "ME"), "barricaded_concrete" }, { ("ME", "SE"), "walled_off" },
            }),
            target_sector = "MC",
            player_spawn_sector = "SW",
            extraction_sector = "NE",
        };

        public static RoomGraphManifest Speedrunner() => new RoomGraphManifest
        {
            sectors = new List<SectorConfig>
            {
                S("SW", "URBAN_HIGHWAY", props: new List<string> { "car" }),
                S("SC", "URBAN_HIGHWAY"),
                S("SE", "WATERFRONT_DOCKS"),
                S("MW", "OPEN_STREET"),
                S("MC", "URBAN_HIGHWAY", props: new List<string> { "car" }),
                S("ME", "URBAN_HIGHWAY"),
                S("NW", "VACANT_LOT"),
                S("NC", "FORTIFIED_PLAZA"),
                S("NE", "WATERFRONT_DOCKS",
                    guards: new List<GuardEntityConfig> { G("NE", 0, 1, "sentry"), G("NE", 0, -1, "patrol") }),
            },
            connections = Edges(new Dictionary<(string, string), string>
            {
                { ("NW", "NC"), "walled_off" }, { ("NC", "NE"), "barricaded_concrete" },
                { ("MW", "MC"), "open" }, { ("MC", "ME"), "open" },
                { ("SW", "SC"), "open" }, { ("SC", "SE"), "barricaded_concrete" },
                { ("NW", "MW"), "walled_off" }, { ("MW", "SW"), "open" },
                { ("NC", "MC"), "barricaded_concrete" }, { ("MC", "SC"), "walled_off" },
                { ("NE", "ME"), "barricaded_concrete" }, { ("ME", "SE"), "open" },
            }),
            target_sector = "NE",
            player_spawn_sector = "SW",
            extraction_sector = "SE",
        };

        public static RoomGraphManifest Aggressor() => new RoomGraphManifest
        {
            sectors = new List<SectorConfig>
            {
                S("MW", "URBAN_HIGHWAY"),
                S("SW", "VACANT_LOT"),
                S("SC", "OPEN_STREET"),
                S("NE", "WATERFRONT_DOCKS"),
                S("MC", "URBAN_HIGHWAY", props: new List<string> { "car" }),
                S("ME", "FORTIFIED_PLAZA",
                    guards: new List<GuardEntityConfig> { G("ME", -1, 1, "sentry", true), G("ME", 1, -1, "sentry", true) }),
                S("NW", "WAREHOUSE_INTERIOR"),
                S("NC", "INDUSTRIAL_DEPOT", props: new List<string> { "fuel_barrel" }),
                S("SE", "SECURITY_COMPOUND",
                    guards: new List<GuardEntityConfig>
                    {
                        G("SE", -2, 0, "bodyguard", true), G("SE", 2, 0, "bodyguard", true), G("SE", 0, 2, "sentry", true),
                    }),
            },
            connections = Edges(new Dictionary<(string, string), string>
            {
                { ("NW", "NC"), "barricaded_concrete" }, { ("NC", "NE"), "walled_off" },
                { ("MW", "MC"), "open" }, { ("MC", "ME"), "barricaded_concrete" },
                { ("SW", "SC"), "open" }, { ("SC", "SE"), "barricaded_concrete" },
                { ("NW", "MW"), "barricaded_concrete" }, { ("MW", "SW"), "walled_off" },
                { ("NC", "MC"), "barricaded_concrete" }, { ("MC", "SC"), "walled_off" },
                { ("NE", "ME"), "barricaded_concrete" }, { ("ME", "SE"), "barricaded_concrete" },
            }),
            target_sector = "SE",
            player_spawn_sector = "MW",
            extraction_sector = "NE",
        };

        public static RoomGraphManifest Balanced() => new RoomGraphManifest
        {
            sectors = new List<SectorConfig>
            {
                S("SW", "SLUM_ALLEYWAYS", guards: new List<GuardEntityConfig> { G("SW", 0, 0, "patrol") }),
                S("SC", "OPEN_STREET"),
                S("SE", "VACANT_LOT"),
                S("MW", "URBAN_HIGHWAY", props: new List<string> { "car" }),
                S("MC", "INDUSTRIAL_DEPOT", props: new List<string> { "fuel_barrel" }),
                S("ME", "OPEN_STREET"),
                S("NW", "WAREHOUSE_INTERIOR"),
                S("NC", "FORTIFIED_PLAZA",
                    guards: new List<GuardEntityConfig> { G("NC", -1, 0, "bodyguard"), G("NC", 1, 0, "bodyguard") }),
                S("NE", "WATERFRONT_DOCKS"),
            },
            connections = Edges(new Dictionary<(string, string), string>
            {
                { ("NW", "NC"), "open" }, { ("NC", "NE"), "barricaded_wire_fence" },
                { ("MW", "MC"), "open" }, { ("MC", "ME"), "barricaded_wire_fence" },
                { ("SW", "SC"), "barricaded_wire_fence" }, { ("SC", "SE"), "walled_off" },
                { ("NW", "MW"), "barricaded_concrete" }, { ("MW", "SW"), "open" },
                { ("NC", "MC"), "open" }, { ("MC", "SC"), "walled_off" },
                { ("NE", "ME"), "barricaded_wire_fence" }, { ("ME", "SE"), "open" },
            }),
            target_sector = "NC",
            player_spawn_sector = "SW",
            extraction_sector = "NE",
        };
    }
}
#endif
