#if UNITY_EDITOR
using System.Collections.Generic;
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
    // All 12 unique adjacent pairs (horizontal + vertical neighbors only, no diagonals).
    // Layer indices, cell dimensions, ROOT_NAME, SectorCoords, and ValidStatuses are
    // defined in SectorRuntimeBuilder and referenced directly via SectorRuntimeBuilder.*
    private static readonly (string a, string b)[] Adjacency =
    {
        ("NW","NC"), ("NC","NE"),
        ("MW","MC"), ("MC","ME"),
        ("SW","SC"), ("SC","SE"),
        ("NW","MW"), ("MW","SW"),
        ("NC","MC"), ("MC","SC"),
        ("NE","ME"), ("ME","SE"),
    };

    // ═════════════════════════════════════════════════════════════
    //  MENU ITEMS
    // ═════════════════════════════════════════════════════════════
    [MenuItem("AdaptiveWorld/Build Map 2 - Sector Graph/From Live or Cached Manifest")]
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

    [MenuItem("AdaptiveWorld/Build Map 2 - Sector Graph/Test - GHOST")]
    public static void BuildTestGhost() => Build(TestManifests.Ghost());

    [MenuItem("AdaptiveWorld/Build Map 2 - Sector Graph/Test - WRECKING_BALL")]
    public static void BuildTestWreckingBall() => Build(TestManifests.WreckingBall());

    [MenuItem("AdaptiveWorld/Build Map 2 - Sector Graph/Test - SPEEDRUNNER")]
    public static void BuildTestSpeedrunner() => Build(TestManifests.Speedrunner());

    [MenuItem("AdaptiveWorld/Build Map 2 - Sector Graph/Test - AGGRESSOR")]
    public static void BuildTestAggressor() => Build(TestManifests.Aggressor());

    [MenuItem("AdaptiveWorld/Build Map 2 - Sector Graph/Test - BALANCED")]
    public static void BuildTestBalanced() => Build(TestManifests.Balanced());

    [MenuItem("AdaptiveWorld/Build Map 2 - Sector Graph/Clear")]
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

        AdaptiveWorld2D.World.SectorThemeCatalog.CreateOrPopulateAsset();
        GameObject root = SectorRuntimeBuilder.Build(roomGraph);
        if (root == null) return;

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[ProceduralSectorBuilder] Sector grid build complete and saved to disk.");
        EditorUtility.DisplayDialog("Map 2 Sector Graph Built & Saved!",
            $"9 sectors + {roomGraph.connections.Count} gateways generated and saved to disk.\n\n" +
            $"Spawn: {roomGraph.player_spawn_sector}\nTarget: {roomGraph.target_sector}\nExtraction: {roomGraph.extraction_sector}\n\n" +
            "Dynamic entities (Viktor, guards, cars) and lighting are dynamically placed at runtime.", "Got it!");
    }

    private static void ClearPrevious()
    {
        SectorRuntimeBuilder.ClearPrevious();
    }

    // ═════════════════════════════════════════════════════════════
    //  BUNDLED TEST MANIFESTS
    //  Mirrors backend/target_solver.py's build_room_graph() output EXACTLY, archetype for
    //  archetype, sector for sector, connection for connection -- so Editor-time previews are
    //  guaranteed structurally identical to what the live backend would send.
    // ═════════════════════════════════════════════════════════════
    public static class TestManifests
    {
        private static SectorConfig S(string id, string module, List<GuardEntityConfig> guards = null, List<string> props = null)
        {
            Vector2 c = SectorRuntimeBuilder.SectorCoords[id];
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
