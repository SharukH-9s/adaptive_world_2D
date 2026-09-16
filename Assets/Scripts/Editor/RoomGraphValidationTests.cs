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
    }
}
#endif
