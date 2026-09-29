#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.EditorTests
{
    /// <summary>
    /// In-Editor characterization test suite for Phase 1.
    /// Validates RoomGraphManifest deserialization, null-guard sanitization,
    /// and schema invariants directly within the Unity Editor environment.
    ///
    /// Run via Menu: AdaptiveWorld > Run Characterization Tests
    /// </summary>
    public static class RoomGraphValidationTests
    {
        [MenuItem("AdaptiveWorld/Run Characterization Tests")]
        public static void RunAllCharacterizationTests()
        {
            int passed = 0;
            int total = 0;

            void RunTest(string testName, Action testAction)
            {
                total++;
                try
                {
                    testAction();
                    passed++;
                    Debug.Log($"[PASS] {testName}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FAIL] {testName}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            Debug.Log("==================================================================");
            Debug.Log("  ADAPTIVE WORLD 2D - UNITY CHARACTERIZATION TEST SUITE (PHASE 1)");
            Debug.Log("==================================================================");

            RunTest("Sanitize Null Guard Instantiation", TestSanitizeNullGuards);
            RunTest("RoomGraph Sector Linear Lookup", TestGetSectorLookup);
            RunTest("JsonUtility Deserialization Contract", TestJsonUtilityDeserialization);
            RunTest("Guard Entity Config Parsing", TestGuardEntityConfig);
            RunTest("Routes Manifest Parsing", TestRoutesManifestParsing);
            RunTest("Leap 3 Dynamic Grid Deserialization", TestDynamicGridDeserialization);
            RunTest("Leap 3 Dynamic RoomGraph Validation", TestDynamicRoomGraphValidation);
            RunTest("Leap 3 Structured PropData Deserialization", TestStructuredPropDataDeserialization);

            Debug.Log("------------------------------------------------------------------");
            if (passed == total)
            {
                Debug.Log($"  ALL {passed}/{total} UNITY CHARACTERIZATION TESTS PASSED!");
            }
            else
            {
                Debug.LogError($"  CHARACTERIZATION SUITE FAILED: {passed}/{total} passed.");
            }
            Debug.Log("==================================================================");
        }

        private static void TestSanitizeNullGuards()
        {
            MissionManifest manifest = new MissionManifest
            {
                room_graph = new RoomGraphManifest
                {
                    sectors = new List<SectorConfig>
                    {
                        new SectorConfig { id = "MC", guards = null, props = null }
                    },
                    connections = null
                },
                routes = new RoutesManifest
                {
                    blocked_routes = null,
                    reinforced_barricades = null
                }
            };

            // Before Sanitize: fields are null
            if (manifest.room_graph.connections != null)
                throw new Exception("Precondition failed: connections should be null.");

            manifest.Sanitize();

            // After Sanitize: fields must be instantiated to avoid NullReferenceExceptions
            if (manifest.room_graph.connections == null)
                throw new Exception("Sanitize failed to instantiate connections list.");
            if (manifest.room_graph.sectors[0].guards == null)
                throw new Exception("Sanitize failed to instantiate guards list on sector MC.");
            if (manifest.room_graph.sectors[0].props == null)
                throw new Exception("Sanitize failed to instantiate props list on sector MC.");
            if (manifest.routes.blocked_routes == null)
                throw new Exception("Sanitize failed to instantiate blocked_routes list.");
            if (manifest.routes.reinforced_barricades == null)
                throw new Exception("Sanitize failed to instantiate reinforced_barricades list.");
        }

        private static void TestGetSectorLookup()
        {
            RoomGraphManifest rg = new RoomGraphManifest
            {
                sectors = new List<SectorConfig>
                {
                    new SectorConfig { id = "SW", module = "SLUM_ALLEYWAYS", center_x = -20f, center_y = -15f },
                    new SectorConfig { id = "MC", module = "SECURITY_COMPOUND", center_x = 0f, center_y = 0f },
                    new SectorConfig { id = "NE", module = "WATERFRONT_DOCKS", center_x = 20f, center_y = 15f }
                }
            };

            SectorConfig mc = rg.GetSector("MC");
            if (mc == null || mc.id != "MC" || mc.module != "SECURITY_COMPOUND")
                throw new Exception("Failed to retrieve sector 'MC' correctly.");

            SectorConfig sw = rg.GetSector("SW");
            if (sw == null || sw.id != "SW" || sw.center_x != -20f)
                throw new Exception("Failed to retrieve sector 'SW' correctly.");

            SectorConfig nonExistent = rg.GetSector("INVALID");
            if (nonExistent != null)
                throw new Exception("GetSector('INVALID') should return null.");
        }

        private static void TestJsonUtilityDeserialization()
        {
            string sampleJson = "{\"sectors\":[{\"id\":\"MC\",\"module\":\"FORTIFIED_PLAZA\",\"center_x\":0.0,\"center_y\":0.0}],\"connections\":[{\"from_sector\":\"SW\",\"to_sector\":\"SC\",\"status\":\"open\"}],\"target_sector\":\"MC\",\"player_spawn_sector\":\"SW\",\"extraction_sector\":\"NE\"}";
            
            RoomGraphManifest rg = JsonUtility.FromJson<RoomGraphManifest>(sampleJson);
            if (rg == null)
                throw new Exception("JsonUtility returned null for RoomGraphManifest.");

            if (rg.target_sector != "MC")
                throw new Exception($"Expected target_sector 'MC', got '{rg.target_sector}'");
            if (rg.player_spawn_sector != "SW")
                throw new Exception($"Expected player_spawn_sector 'SW', got '{rg.player_spawn_sector}'");
            if (rg.extraction_sector != "NE")
                throw new Exception($"Expected extraction_sector 'NE', got '{rg.extraction_sector}'");
            if (rg.sectors == null || rg.sectors.Count != 1)
                throw new Exception("Expected exactly 1 sector deserialized.");
            if (rg.connections == null || rg.connections.Count != 1)
                throw new Exception("Expected exactly 1 connection deserialized.");
            if (rg.connections[0].status != "open")
                throw new Exception($"Expected status 'open', got '{rg.connections[0].status}'");
        }

        private static void TestGuardEntityConfig()
        {
            GuardEntityConfig g = new GuardEntityConfig
            {
                sector_id = "MC",
                offset_x = 1.5f,
                offset_y = -2.0f,
                role = "bodyguard",
                armored = true
            };

            if (g.sector_id != "MC" || g.offset_x != 1.5f || g.offset_y != -2.0f || g.role != "bodyguard" || !g.armored)
                throw new Exception("GuardEntityConfig properties failed to preserve assigned values.");
        }

        private static void TestRoutesManifestParsing()
        {
            RoutesManifest routes = new RoutesManifest
            {
                primary_route = "main_road",
                primary_label = "Main Road",
                blocked_routes = new List<string> { "alley_south", "warehouse_front_entrance" },
                reinforced_barricades = new List<string> { "concrete_barrier_01" },
                cars_available = true
            };

            if (routes.primary_route != "main_road" || !routes.cars_available)
                throw new Exception("RoutesManifest primary fields mismatch.");
            if (routes.blocked_routes.Count != 2 || !routes.blocked_routes.Contains("alley_south"))
                throw new Exception("RoutesManifest blocked_routes mismatch.");
            if (routes.reinforced_barricades.Count != 1 || routes.reinforced_barricades[0] != "concrete_barrier_01")
                throw new Exception("RoutesManifest reinforced_barricades mismatch.");
        }

        private static void TestDynamicGridDeserialization()
        {
            string sampleJson = "{\"grid_dimensions\":{\"columns\":2,\"rows\":3,\"cell_width\":20.0,\"cell_height\":15.0},\"sectors\":[{\"id\":\"R0_C0\",\"module\":\"WAREHOUSE_INTERIOR\",\"center_x\":-10.0,\"center_y\":15.0,\"guards\":[],\"props\":[]}],\"connections\":[],\"target_sector\":\"R0_C0\",\"player_spawn_sector\":\"R0_C0\",\"extraction_sector\":\"R0_C0\"}";

            RoomGraphManifest rg = JsonUtility.FromJson<RoomGraphManifest>(sampleJson);
            if (rg == null) throw new Exception("Deserialization returned null.");
            if (rg.grid_dimensions == null) throw new Exception("grid_dimensions was not deserialized.");
            if (rg.grid_dimensions.columns != 2 || rg.grid_dimensions.rows != 3)
                throw new Exception($"Expected 2x3 grid dimensions, got {rg.grid_dimensions.columns}x{rg.grid_dimensions.rows}.");
            if (rg.GetGridDimensions().columns != 2)
                throw new Exception("GetGridDimensions() failed to return correct columns.");
        }

        private static void TestDynamicRoomGraphValidation()
        {
            // Valid 2x2 dynamic grid: 4 sectors, (2-1)*2 + 2*(2-1) = 4 connections
            RoomGraphManifest validRg = new RoomGraphManifest
            {
                grid_dimensions = new GridDimensions { columns = 2, rows = 2, cell_width = 20f, cell_height = 15f },
                sectors = new List<SectorConfig>
                {
                    new SectorConfig { id = "R0_C0", module = "WAREHOUSE_INTERIOR" },
                    new SectorConfig { id = "R0_C1", module = "FORTIFIED_PLAZA" },
                    new SectorConfig { id = "R1_C0", module = "OPEN_STREET" },
                    new SectorConfig { id = "R1_C1", module = "SECURITY_COMPOUND" }
                },
                connections = new List<ConnectionConfig>
                {
                    new ConnectionConfig { from_sector = "R0_C0", to_sector = "R0_C1", status = "open" },
                    new ConnectionConfig { from_sector = "R1_C0", to_sector = "R1_C1", status = "open" },
                    new ConnectionConfig { from_sector = "R0_C0", to_sector = "R1_C0", status = "open" },
                    new ConnectionConfig { from_sector = "R0_C1", to_sector = "R1_C1", status = "open" }
                },
                player_spawn_sector = "R0_C0",
                target_sector = "R1_C1",
                extraction_sector = "R0_C1"
            };

            var errors = AdaptiveWorld2D.World.RoomGraphValidator.Validate(validRg);
            if (errors.Count > 0)
                throw new Exception($"Valid dynamic grid failed validation: {string.Join("; ", errors)}");

            // Invalid connection status check
            validRg.connections[0].status = "invalid_status";
            var invalidErrors = AdaptiveWorld2D.World.RoomGraphValidator.Validate(validRg);
            if (invalidErrors.Count == 0)
                throw new Exception("Expected validation error for invalid connection status, but got none.");
        }

        private static void TestStructuredPropDataDeserialization()
        {
            string sampleJson = "{\"sectors\":[{\"id\":\"MC\",\"module\":\"SECURITY_COMPOUND\",\"center_x\":0.0,\"center_y\":0.0,\"props\":[{\"type\":\"power_breaker\",\"offset_x\":1.5,\"offset_y\":-2.0,\"destructible\":true,\"interact_action\":\"CUT_POWER\",\"connects_to\":\"\"},{\"type\":\"vent_shaft\",\"offset_x\":-3.0,\"offset_y\":1.0,\"destructible\":false,\"interact_action\":\"\",\"connects_to\":\"NE\"}]}]}";

            RoomGraphManifest rg = JsonUtility.FromJson<RoomGraphManifest>(sampleJson);
            if (rg == null || rg.sectors == null || rg.sectors.Count == 0)
                throw new Exception("Failed to deserialize sector.");

            var props = rg.sectors[0].props;
            if (props == null || props.Count != 2)
                throw new Exception($"Expected 2 props deserialized, got {props?.Count ?? 0}.");

            if (props[0].type != "power_breaker" || props[0].interact_action != "CUT_POWER" || !props[0].destructible)
                throw new Exception("power_breaker PropData fields failed to deserialize accurately.");

            if (props[1].type != "vent_shaft" || props[1].connects_to != "NE")
                throw new Exception("vent_shaft PropData fields failed to deserialize accurately.");
        }
    }
}
#endif
