#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using AdaptiveWorld2D.Player;
using AdaptiveWorld2D.AI;
using AdaptiveWorld2D.World;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.EditorTests
{
    /// <summary>
    /// In-Editor characterization test suite for Phase 2 (Architecture Improvement Plan).
    /// Validates the pure calculation classes extracted from PlayerController, EnemyPatrol,
    /// SectorRuntimeBuilder, and MissionManifest against the behavior they replaced.
    ///
    /// Run via Menu: AdaptiveWorld > Run Phase 2 Extraction Tests
    /// </summary>
    public static class Phase2ExtractionTests
    {
        [MenuItem("AdaptiveWorld/Run Phase 2 Extraction Tests")]
        public static void RunAllPhase2Tests()
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
            Debug.Log("  ADAPTIVE WORLD 2D - PHASE 2 EXTRACTION TEST SUITE");
            Debug.Log("==================================================================");

            // PlayerMovementModel
            RunTest("PlayerMovementModel: Speed selection (drive > sprint > walk)", TestSelectSpeed);
            RunTest("PlayerMovementModel: Velocity from normalized input", TestComputeVelocity);
            RunTest("PlayerMovementModel: Kinematic displacement", TestComputeKinematicDisplacement);
            RunTest("PlayerMovementModel: Facing angle incl. -90f offset", TestComputeFacingAngle);
            RunTest("PlayerMovementModel: Rotation threshold (0.05f)", TestShouldRotate);
            RunTest("PlayerMovementModel: Movement type DRIVE/RUN/WALK", TestDetermineMovementType);
            RunTest("PlayerMovementModel: Average speed", TestComputeAverageSpeed);

            // EnemyDecisionModel
            RunTest("EnemyDecisionModel: Damage at zero resistance", TestDamageZeroResistance);
            RunTest("EnemyDecisionModel: Damage with partial resistance", TestDamagePartialResistance);
            RunTest("EnemyDecisionModel: Damage with full resistance (min-1 rule)", TestDamageFullResistance);
            RunTest("EnemyDecisionModel: Damage with over-resistance (min-1 rule)", TestDamageOverResistance);
            RunTest("EnemyDecisionModel: Lethal detection and non-clamped health", TestDamageLethal);
            RunTest("EnemyDecisionModel: Boss retreat exactly at 40% (not retreating)", TestBossRetreatAtExactly40Percent);
            RunTest("EnemyDecisionModel: Boss retreat below 40% (retreating)", TestBossRetreatBelow40Percent);
            RunTest("EnemyDecisionModel: Wounded boss chase speed multiplier", TestWoundedBossChaseSpeed);
            RunTest("EnemyDecisionModel: Waypoint index wraps via modulo", TestAdvanceWaypointIndex);
            RunTest("EnemyDecisionModel: Look angle incl. -90f offset", TestComputeLookAngle);

            // RoomGraphValidator
            RunTest("RoomGraphValidator: Valid 9-sector/12-connection graph passes", TestRoomGraphValidatorValid);
            RunTest("RoomGraphValidator: Wrong sector count fails", TestRoomGraphValidatorWrongSectorCount);
            RunTest("RoomGraphValidator: Wrong connection count fails", TestRoomGraphValidatorWrongConnectionCount);
            RunTest("RoomGraphValidator: Invalid connection status fails", TestRoomGraphValidatorInvalidStatus);
            RunTest("RoomGraphValidator: Empty designations fail", TestRoomGraphValidatorEmptyDesignations);
            RunTest("RoomGraphValidator: Null root/list/entries do not throw", TestRoomGraphValidatorNullSafety);

            // ManifestValidator
            RunTest("ManifestValidator: Sanitize preserves null room_graph", TestManifestValidatorSanitizeNullRoomGraph);
            RunTest("ManifestValidator: Sanitize instantiates nested lists", TestManifestValidatorSanitizeNestedLists);
            RunTest("ManifestValidator: Validate flags missing sections", TestManifestValidatorValidateMissingSections);
            RunTest("ManifestValidator: Validate passes complete manifest", TestManifestValidatorValidateComplete);

            Debug.Log("------------------------------------------------------------------");
            if (passed == total)
            {
                Debug.Log($"  ALL {passed}/{total} PHASE 2 EXTRACTION TESTS PASSED!");
            }
            else
            {
                Debug.LogError($"  PHASE 2 EXTRACTION SUITE FAILED: {passed}/{total} passed.");
            }
            Debug.Log("==================================================================");
        }

        // ── PlayerMovementModel ──────────────────────────────────────────────

        private static void TestSelectSpeed()
        {
            float speed = PlayerMovementModel.SelectSpeed(isDriving: true, isSprinting: true, walkSpeed: 3.5f, runSpeed: 6.0f, driveSpeed: 12.0f);
            if (speed != 12.0f) throw new Exception($"Expected driveSpeed to take precedence, got {speed}.");

            speed = PlayerMovementModel.SelectSpeed(isDriving: false, isSprinting: true, walkSpeed: 3.5f, runSpeed: 6.0f, driveSpeed: 12.0f);
            if (speed != 6.0f) throw new Exception($"Expected runSpeed while sprinting, got {speed}.");

            speed = PlayerMovementModel.SelectSpeed(isDriving: false, isSprinting: false, walkSpeed: 3.5f, runSpeed: 6.0f, driveSpeed: 12.0f);
            if (speed != 3.5f) throw new Exception($"Expected walkSpeed by default, got {speed}.");
        }

        private static void TestComputeVelocity()
        {
            Vector2 input = new Vector2(1f, 0f);
            Vector2 velocity = PlayerMovementModel.ComputeVelocity(input, 5f);
            if (velocity != new Vector2(5f, 0f)) throw new Exception($"Expected (5,0), got {velocity}.");

            Vector2 diagInput = new Vector2(0.7071f, 0.7071f); // pre-normalized diagonal
            Vector2 diagVelocity = PlayerMovementModel.ComputeVelocity(diagInput, 2f);
            if (Math.Abs(diagVelocity.x - 1.4142f) > 0.001f || Math.Abs(diagVelocity.y - 1.4142f) > 0.001f)
                throw new Exception($"Diagonal velocity mismatch: {diagVelocity}.");

            Vector2 zero = PlayerMovementModel.ComputeVelocity(Vector2.zero, 5f);
            if (zero != Vector2.zero) throw new Exception($"Expected zero velocity for zero input, got {zero}.");
        }

        private static void TestComputeKinematicDisplacement()
        {
            Vector2 pos = new Vector2(10f, 10f);
            Vector2 input = new Vector2(1f, 0f);
            Vector2 result = PlayerMovementModel.ComputeKinematicDisplacement(pos, input, 5f, 0.02f);
            Vector2 expected = pos + input * 5f * 0.02f;
            if (result != expected) throw new Exception($"Expected {expected}, got {result}.");
        }

        private static void TestComputeFacingAngle()
        {
            float angle = PlayerMovementModel.ComputeFacingAngleDegrees(new Vector2(1f, 0f));
            float expected = Mathf.Atan2(0f, 1f) * Mathf.Rad2Deg - 90f;
            if (Math.Abs(angle - expected) > 0.001f) throw new Exception($"Expected {expected}, got {angle}.");
        }

        private static void TestShouldRotate()
        {
            if (PlayerMovementModel.ShouldRotate(new Vector2(0.05f, 0f)))
                throw new Exception("Magnitude exactly at threshold (0.05f) must not trigger rotation.");
            if (!PlayerMovementModel.ShouldRotate(new Vector2(0.06f, 0f)))
                throw new Exception("Magnitude above threshold must trigger rotation.");
            if (PlayerMovementModel.ShouldRotate(Vector2.zero))
                throw new Exception("Zero input must not trigger rotation.");
        }

        private static void TestDetermineMovementType()
        {
            if (PlayerMovementModel.DetermineMovementType(true, true) != "DRIVE")
                throw new Exception("Driving must take precedence and report DRIVE.");
            if (PlayerMovementModel.DetermineMovementType(false, true) != "RUN")
                throw new Exception("Sprinting without driving must report RUN.");
            if (PlayerMovementModel.DetermineMovementType(false, false) != "WALK")
                throw new Exception("Default state must report WALK.");
        }

        private static void TestComputeAverageSpeed()
        {
            float avg = PlayerMovementModel.ComputeAverageSpeed(15f, 3f);
            if (Math.Abs(avg - 5f) > 0.0001f) throw new Exception($"Expected average speed 5, got {avg}.");
        }

        // ── EnemyDecisionModel ───────────────────────────────────────────────

        private static void TestDamageZeroResistance()
        {
            var result = EnemyDecisionModel.ComputeDamage(rawDamage: 10, damageResistance: 0f, currentHealth: 60);
            if (result.MitigatedDamage != 10) throw new Exception($"Expected mitigated 10, got {result.MitigatedDamage}.");
            if (result.AppliedDamage != 10) throw new Exception($"Expected applied 10, got {result.AppliedDamage}.");
            if (result.NewHealth != 50) throw new Exception($"Expected health 50, got {result.NewHealth}.");
        }

        private static void TestDamagePartialResistance()
        {
            var result = EnemyDecisionModel.ComputeDamage(rawDamage: 10, damageResistance: 0.5f, currentHealth: 60);
            if (result.MitigatedDamage != 5) throw new Exception($"Expected mitigated 5, got {result.MitigatedDamage}.");
            if (result.NewHealth != 55) throw new Exception($"Expected health 55, got {result.NewHealth}.");
        }

        private static void TestDamageFullResistance()
        {
            var result = EnemyDecisionModel.ComputeDamage(rawDamage: 10, damageResistance: 1.0f, currentHealth: 60);
            if (result.MitigatedDamage != 0) throw new Exception($"Expected mitigated 0, got {result.MitigatedDamage}.");
            if (result.AppliedDamage != 1) throw new Exception($"Minimum-one-damage rule violated: applied={result.AppliedDamage}.");
            if (result.NewHealth != 59) throw new Exception($"Expected health 59 (60 - min1), got {result.NewHealth}.");
        }

        private static void TestDamageOverResistance()
        {
            // Resistance > 1.0 is not a currently supported gameplay value, but the formula
            // must not throw and must still apply the minimum-one-damage rule.
            var result = EnemyDecisionModel.ComputeDamage(rawDamage: 10, damageResistance: 1.5f, currentHealth: 60);
            if (result.AppliedDamage != 1) throw new Exception($"Minimum-one-damage rule violated: applied={result.AppliedDamage}.");
        }

        private static void TestDamageLethal()
        {
            var result = EnemyDecisionModel.ComputeDamage(rawDamage: 100, damageResistance: 0f, currentHealth: 10);
            if (!result.IsLethal) throw new Exception("Expected lethal result.");
            if (result.NewHealth != -90) throw new Exception($"Health must not be clamped to zero, expected -90, got {result.NewHealth}.");
        }

        private static void TestBossRetreatAtExactly40Percent()
        {
            // maxHealth=100 -> 40% = 40. Health exactly at 40 must NOT retreat (strict <).
            bool retreating = EnemyDecisionModel.IsBossRetreating(isTargetBoss: true, currentHealth: 40, maxHealth: 100);
            if (retreating) throw new Exception("Boss at exactly 40% health must not be retreating (threshold is strict <40%).");
        }

        private static void TestBossRetreatBelow40Percent()
        {
            bool retreating = EnemyDecisionModel.IsBossRetreating(isTargetBoss: true, currentHealth: 39, maxHealth: 100);
            if (!retreating) throw new Exception("Boss below 40% health must be retreating.");

            bool nonBossRetreating = EnemyDecisionModel.IsBossRetreating(isTargetBoss: false, currentHealth: 1, maxHealth: 100);
            if (nonBossRetreating) throw new Exception("Non-boss enemies must never trigger the retreat behavior.");
        }

        private static void TestWoundedBossChaseSpeed()
        {
            float speed = EnemyDecisionModel.ComputeChaseSpeed(chaseSpeed: 4.0f, isBossRetreating: true);
            if (Math.Abs(speed - 3.4f) > 0.0001f) throw new Exception($"Expected 4.0 * 0.85 = 3.4, got {speed}.");

            float normalSpeed = EnemyDecisionModel.ComputeChaseSpeed(chaseSpeed: 4.0f, isBossRetreating: false);
            if (normalSpeed != 4.0f) throw new Exception($"Expected unmodified chase speed, got {normalSpeed}.");
        }

        private static void TestAdvanceWaypointIndex()
        {
            int next = EnemyDecisionModel.AdvanceWaypointIndex(currentIndex: 0, waypointCount: 3);
            if (next != 1) throw new Exception($"Expected 1, got {next}.");

            int wrapped = EnemyDecisionModel.AdvanceWaypointIndex(currentIndex: 2, waypointCount: 3);
            if (wrapped != 0) throw new Exception($"Expected wrap to 0, got {wrapped}.");
        }

        private static void TestComputeLookAngle()
        {
            float angle = EnemyDecisionModel.ComputeLookAngleDegrees(new Vector2(0f, 1f));
            float expected = Mathf.Atan2(1f, 0f) * Mathf.Rad2Deg - 90f;
            if (Math.Abs(angle - expected) > 0.001f) throw new Exception($"Expected {expected}, got {angle}.");
        }

        // ── RoomGraphValidator ───────────────────────────────────────────────

        private static RoomGraphManifest BuildValidRoomGraph()
        {
            var rg = new RoomGraphManifest
            {
                sectors = new List<SectorConfig>(),
                connections = new List<ConnectionConfig>(),
                target_sector = "MC",
                player_spawn_sector = "SW",
                extraction_sector = "NE"
            };
            foreach (var id in RoomGraphValidator.ExpectedSectorIds)
                rg.sectors.Add(new SectorConfig { id = id, module = "OPEN_STREET" });

            for (int i = 0; i < 12; i++)
                rg.connections.Add(new ConnectionConfig { from_sector = "NW", to_sector = "NC", status = "open" });

            return rg;
        }

        private static void TestRoomGraphValidatorValid()
        {
            var rg = BuildValidRoomGraph();
            var errors = RoomGraphValidator.Validate(rg);
            if (errors.Count != 0) throw new Exception($"Expected no errors, got: {string.Join("; ", errors)}.");
        }

        private static void TestRoomGraphValidatorWrongSectorCount()
        {
            var rg = BuildValidRoomGraph();
            rg.sectors.RemoveAt(0);
            var errors = RoomGraphValidator.Validate(rg);
            if (errors.Count == 0) throw new Exception("Expected an error for 8 sectors.");
        }

        private static void TestRoomGraphValidatorWrongConnectionCount()
        {
            var rg = BuildValidRoomGraph();
            rg.connections.RemoveAt(0);
            var errors = RoomGraphValidator.Validate(rg);
            if (errors.Count == 0) throw new Exception("Expected an error for 11 connections.");
        }

        private static void TestRoomGraphValidatorInvalidStatus()
        {
            var rg = BuildValidRoomGraph();
            rg.connections[0].status = "bogus_status";
            var errors = RoomGraphValidator.Validate(rg);
            if (errors.Count == 0) throw new Exception("Expected an error for invalid connection status.");
        }

        private static void TestRoomGraphValidatorEmptyDesignations()
        {
            var rg = BuildValidRoomGraph();
            rg.target_sector = "";
            rg.player_spawn_sector = null;
            var errors = RoomGraphValidator.Validate(rg);
            if (errors.Count < 2) throw new Exception($"Expected at least 2 errors for empty designations, got {errors.Count}.");
        }

        private static void TestRoomGraphValidatorNullSafety()
        {
            var errorsForNullGraph = RoomGraphValidator.Validate(null);
            if (errorsForNullGraph.Count == 0) throw new Exception("Expected an error for a null room graph, not an exception.");

            var rg = BuildValidRoomGraph();
            rg.sectors[0] = null;
            var errorsForNullSector = RoomGraphValidator.Validate(rg);
            if (errorsForNullSector.Count == 0) throw new Exception("Expected an error for a null sector entry, not an exception.");

            var rg2 = BuildValidRoomGraph();
            rg2.connections[0] = null;
            var errorsForNullConnection = RoomGraphValidator.Validate(rg2);
            if (errorsForNullConnection.Count == 0) throw new Exception("Expected an error for a null connection entry, not an exception.");
        }

        // ── ManifestValidator ────────────────────────────────────────────────

        private static void TestManifestValidatorSanitizeNullRoomGraph()
        {
            // Original Sanitize() returns early when room_graph is null, so routes must
            // also remain untouched — matching the pre-extraction early-return semantics.
            var manifest = new MissionManifest
            {
                room_graph = null,
                routes = new RoutesManifest { blocked_routes = null, reinforced_barricades = null }
            };
            ManifestValidator.Sanitize(manifest);

            if (manifest.room_graph != null)
                throw new Exception("A missing room_graph must remain null after Sanitize.");
            if (manifest.routes.blocked_routes != null)
                throw new Exception("routes.blocked_routes must not be sanitized when room_graph is null (early-return semantics).");
            if (manifest.routes.reinforced_barricades != null)
                throw new Exception("routes.reinforced_barricades must not be sanitized when room_graph is null (early-return semantics).");
        }

        private static void TestManifestValidatorSanitizeNestedLists()
        {
            var manifest = new MissionManifest
            {
                room_graph = new RoomGraphManifest
                {
                    sectors = new List<SectorConfig> { new SectorConfig { id = "MC", guards = null, props = null } },
                    connections = null
                },
                routes = new RoutesManifest { blocked_routes = null, reinforced_barricades = null },
                target = null,
                guards = null,
                environment = null
            };

            ManifestValidator.Sanitize(manifest);

            if (manifest.room_graph.connections == null) throw new Exception("connections must be instantiated.");
            if (manifest.room_graph.sectors[0].guards == null) throw new Exception("sector guards must be instantiated.");
            if (manifest.room_graph.sectors[0].props == null) throw new Exception("sector props must be instantiated.");
            if (manifest.routes.blocked_routes == null) throw new Exception("blocked_routes must be instantiated.");
            if (manifest.routes.reinforced_barricades == null) throw new Exception("reinforced_barricades must be instantiated.");

            // Missing target/guards/environment must not be invented.
            if (manifest.target != null) throw new Exception("A missing target must not be invented by Sanitize.");
            if (manifest.guards != null) throw new Exception("A missing guards object must not be invented by Sanitize.");
            if (manifest.environment != null) throw new Exception("A missing environment must not be invented by Sanitize.");
        }

        private static void TestManifestValidatorValidateMissingSections()
        {
            var manifest = new MissionManifest();
            bool valid = ManifestValidator.Validate(manifest, out var errors);
            if (valid) throw new Exception("An empty manifest must not validate as complete.");
            if (errors.Count != 4) throw new Exception($"Expected 4 errors (target/guards/environment/routes), got {errors.Count}.");
        }

        private static void TestManifestValidatorValidateComplete()
        {
            var manifest = new MissionManifest
            {
                target = new TargetManifest(),
                guards = new GuardsManifest(),
                environment = new EnvironmentManifest(),
                routes = new RoutesManifest(),
                room_graph = null // room_graph is intentionally optional -- see Sanitize null semantics.
            };
            bool valid = ManifestValidator.Validate(manifest, out var errors);
            if (!valid) throw new Exception($"Expected a complete manifest to validate, got errors: {string.Join("; ", errors)}.");
        }
    }
}
#endif
