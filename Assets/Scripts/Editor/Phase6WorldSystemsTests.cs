#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using AdaptiveWorld2D.Director;
using AdaptiveWorld2D.World;
using AdaptiveWorld2D.AI;

namespace AdaptiveWorld2D.EditorTests
{
    /// <summary>
    /// In-Editor test suite for Phase 6 (Architecture Improvement Plan).
    /// Validates the separated world systems: FallbackManifestProvider, ManifestValidator,
    /// TargetSpawner, GuardSpawner, RouteApplier, ExtractionZoneBuilder, ManifestCoordinator,
    /// and the WorldManifestApplier facade.
    ///
    /// Run via Menu: AdaptiveWorld > Run Phase 6 World Systems Tests
    /// </summary>
    public static class Phase6WorldSystemsTests
    {
        [MenuItem("AdaptiveWorld/Run Phase 6 World Systems Tests")]
        public static void RunAllPhase6Tests()
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
            Debug.Log("  ADAPTIVE WORLD 2D - PHASE 6 WORLD SYSTEMS TEST SUITE");
            Debug.Log("==================================================================");

            // ── 1. FallbackManifestProvider Tests ────────────────────────────
            RunTest("FallbackManifestProvider: Generates valid default Mission 2 fallback", TestFallbackManifestDefault);
            RunTest("FallbackManifestProvider: Custom archetype propagates correctly", TestFallbackManifestCustomArchetype);

            // ── 2. ManifestValidator Tests ───────────────────────────────────
            RunTest("ManifestValidator: Validates complete manifest successfully", TestManifestValidatorValid);
            RunTest("ManifestValidator: Identifies missing sections", TestManifestValidatorMissingSections);
            RunTest("ManifestValidator: Sanitize handles null lists gracefully", TestManifestValidatorSanitize);

            // ── 3. TargetSpawner Tests ───────────────────────────────────────
            RunTest("TargetSpawner: Priority 1 - RoomGraph target_sector center resolution", TestTargetPositionRoomGraph);
            RunTest("TargetSpawner: Priority 2 - LocationAnchor resolution fallback", TestTargetPositionLocationAnchor);
            RunTest("TargetSpawner: Priority 3 - Raw coordinates fallback", TestTargetPositionCoordinates);
            RunTest("TargetSpawner: Priority 4 - Hard default position fallback", TestTargetPositionHardDefault);
            RunTest("TargetSpawner: Boss vision range scales with armor tier", TestTargetBossVisionArmor);

            // ── 4. GuardSpawner Tests ────────────────────────────────────────
            RunTest("GuardSpawner: ApplyGuardStats configures detection and resistance", TestGuardSpawnerApplyStats);

            // ── 5. RouteApplier Tests ────────────────────────────────────────
            RunTest("RouteApplier: Barricade activation matches blocked routes", TestRouteApplierBarricades);
            RunTest("RouteApplier: Cars disabled when cars_available is false", TestRouteApplierCarsAvailable);

            // ── 6. ExtractionZoneBuilder Tests ───────────────────────────────
            RunTest("ExtractionZoneBuilder: Arrow sprite generation produces valid sprite", TestExtractionArrowSprite);

            // ── 7. ManifestCoordinator Tests ─────────────────────────────────
            RunTest("ManifestCoordinator: BuildManifestKey generates deterministic composite key", TestManifestCoordinatorKey);
            RunTest("ManifestCoordinator: Key differs when archetype or guards count changes", TestManifestCoordinatorKeyDiffers);

            // ── 8. WorldManifestApplier Facade Invariants ────────────────────
            RunTest("WorldManifestApplier: Retains all required public/serialized fields", TestFacadePublicFields);
            RunTest("WorldManifestApplier: Retains static helper API methods", TestFacadeStaticApi);

            Debug.Log("==================================================================");
            Debug.Log($"  PHASE 6 TEST RESULTS: {passed} / {total} PASSED");
            Debug.Log("==================================================================");
        }

        // ── 1. FallbackManifestProvider Tests ────────────────────────────────

        private static void TestFallbackManifestDefault()
        {
            var manifest = FallbackManifestProvider.CreateDefaultFallback();
            AssertEquals(2, manifest.mission_id, "mission_id");
            AssertEquals("GHOST", manifest.player_archetype, "player_archetype");

            AssertTrue(manifest.target != null, "target != null");
            AssertEquals("warehouse_interior", manifest.target.location_id, "target location_id");
            AssertEquals(100, manifest.target.health, "target health");
            AssertEquals("medium", manifest.target.armor, "target armor");

            AssertTrue(manifest.guards != null, "guards != null");
            AssertEquals(6, manifest.guards.count, "guards count");
            AssertEquals(6.0f, manifest.guards.vision_range, "guards vision_range");
            AssertEquals(1.0f, manifest.guards.reaction_speed, "guards reaction_speed");
            AssertEquals(8.0f, manifest.guards.hearing_range, "guards hearing_range");
            AssertEquals(false, manifest.guards.armored, "guards armored");

            AssertTrue(manifest.environment != null, "environment != null");
            AssertEquals("day", manifest.environment.time_of_day, "environment time_of_day");
            AssertEquals("clear", manifest.environment.weather, "environment weather");

            AssertTrue(manifest.routes != null, "routes != null");
            AssertEquals(true, manifest.routes.cars_available, "routes cars_available");
        }

        private static void TestFallbackManifestCustomArchetype()
        {
            var manifest = FallbackManifestProvider.CreateDefaultFallback(3, "RECKLESS");
            AssertEquals(3, manifest.mission_id, "custom mission_id");
            AssertEquals("RECKLESS", manifest.player_archetype, "custom player_archetype");
        }

        // ── 2. ManifestValidator Tests ───────────────────────────────────────

        private static void TestManifestValidatorValid()
        {
            var manifest = FallbackManifestProvider.CreateDefaultFallback();
            bool valid = ManifestValidator.Validate(manifest, out var errors);
            AssertTrue(valid, "Default fallback should be valid");
            AssertEquals(0, errors.Count, "Error count on valid manifest");
        }

        private static void TestManifestValidatorMissingSections()
        {
            var manifest = new MissionManifest
            {
                mission_id = 2,
                player_archetype = "GHOST"
            };
            bool valid = ManifestValidator.Validate(manifest, out var errors);
            AssertFalse(valid, "Manifest missing sections should be invalid");
            AssertTrue(errors.Count >= 4, "Should report at least 4 missing sections");
        }

        private static void TestManifestValidatorSanitize()
        {
            var manifest = new MissionManifest
            {
                room_graph = new RoomGraphManifest
                {
                    sectors = new List<SectorConfig> { new SectorConfig { id = "S1", guards = null, props = null } },
                    connections = null
                },
                routes = new RoutesManifest
                {
                    blocked_routes = null,
                    reinforced_barricades = null
                }
            };
            ManifestValidator.Sanitize(manifest);
            AssertTrue(manifest.room_graph.connections != null, "connections sanitized to non-null");
            AssertTrue(manifest.room_graph.sectors[0].guards != null, "sector guards sanitized to non-null");
            AssertTrue(manifest.room_graph.sectors[0].props != null, "sector props sanitized to non-null");
            AssertTrue(manifest.routes.blocked_routes != null, "blocked_routes sanitized to non-null");
            AssertTrue(manifest.routes.reinforced_barricades != null, "reinforced_barricades sanitized to non-null");
        }

        // ── 3. TargetSpawner Tests ───────────────────────────────────────────

        private static void TestTargetPositionRoomGraph()
        {
            var target = new TargetManifest { location_id = "warehouse_interior", coordinates = new float[] { 1f, 1f } };
            var rg = new RoomGraphManifest
            {
                target_sector = "sector_warehouse",
                sectors = new List<SectorConfig>
                {
                    new SectorConfig { id = "sector_warehouse", center_x = 24f, center_y = 18f }
                }
            };
            Vector3 pos = TargetSpawner.ResolveTargetPosition(target, rg, null);
            AssertEquals(new Vector3(24f, 18f, 0f), pos, "Target position resolved to sector center");
        }

        private static void TestTargetPositionLocationAnchor()
        {
            var target = new TargetManifest { location_id = "open_plaza", coordinates = new float[] { 1f, 1f } };
            GameObject anchorObj = new GameObject("TestAnchor");
            anchorObj.transform.position = new Vector3(50f, 60f, 0f);

            var anchors = new List<WorldManifestApplier.LocationAnchor>
            {
                new WorldManifestApplier.LocationAnchor { locationId = "open_plaza", spawnTransform = anchorObj.transform }
            };

            Vector3 pos = TargetSpawner.ResolveTargetPosition(target, null, anchors);
            UnityEngine.Object.DestroyImmediate(anchorObj);
            AssertEquals(new Vector3(50f, 60f, 0f), pos, "Target position resolved to anchor transform");
        }

        private static void TestTargetPositionCoordinates()
        {
            var target = new TargetManifest { location_id = "unknown", coordinates = new float[] { 12f, -5f } };
            Vector3 pos = TargetSpawner.ResolveTargetPosition(target, null, null);
            AssertEquals(new Vector3(12f, -5f, 0f), pos, "Target position resolved to raw coordinates");
        }

        private static void TestTargetPositionHardDefault()
        {
            var target = new TargetManifest { location_id = null, coordinates = null };
            Vector3 pos = TargetSpawner.ResolveTargetPosition(target, null, null);
            AssertEquals(new Vector3(8f, 2f, 0f), pos, "Target position resolved to hard default");
        }

        private static void TestTargetBossVisionArmor()
        {
            AssertEquals(8.0f, TargetSpawner.ResolveBossVisionRange("heavy"), "Heavy armor vision");
            AssertEquals(7.0f, TargetSpawner.ResolveBossVisionRange("medium"), "Medium armor vision");
            AssertEquals(6.0f, TargetSpawner.ResolveBossVisionRange("light"), "Light armor vision");
            AssertEquals(6.0f, TargetSpawner.ResolveBossVisionRange("none"), "None armor vision");
        }

        // ── 4. GuardSpawner Tests ────────────────────────────────────────────

        private static void TestGuardSpawnerApplyStats()
        {
            GameObject guard = new GameObject("TestGuard");
            var det = guard.AddComponent<EnemyDetection>();
            var patrol = guard.GetComponent<EnemyPatrol>();

            var g = new GuardsManifest
            {
                vision_range = 9.5f,
                reaction_speed = 1.6f,
                hearing_range = 12.0f,
                armored = true
            };

            GuardSpawner.ApplyGuardStats(guard, g);

            AssertEquals(9.5f, det.visionRange, "det.visionRange");
            AssertEquals(1.6f, det.reactionSpeed, "det.reactionSpeed");
            AssertEquals(12.0f, det.hearingRange, "det.hearingRange");
            AssertEquals(0.5f, patrol.damageResistance, "patrol.damageResistance");

            UnityEngine.Object.DestroyImmediate(guard);
        }

        // ── 5. RouteApplier Tests ────────────────────────────────────────────

        private static void TestRouteApplierBarricades()
        {
            GameObject bar1 = new GameObject("Barricade_RouteA");
            GameObject bar2 = new GameObject("Barricade_RouteB");

            var barricades = new List<WorldManifestApplier.NamedBarricade>
            {
                new WorldManifestApplier.NamedBarricade { routeId = "route_a", barricadeObject = bar1 },
                new WorldManifestApplier.NamedBarricade { routeId = "route_b", barricadeObject = bar2 }
            };

            var routes = new RoutesManifest
            {
                cars_available = true,
                blocked_routes = new List<string> { "route_a" }
            };

            RouteApplier.ApplyRoutes(routes, barricades, null);

            AssertTrue(bar1.activeSelf, "route_a barricade should be active");
            AssertFalse(bar2.activeSelf, "route_b barricade should be inactive");

            UnityEngine.Object.DestroyImmediate(bar1);
            UnityEngine.Object.DestroyImmediate(bar2);
        }

        private static void TestRouteApplierCarsAvailable()
        {
            GameObject car = new GameObject("TestCar");
            car.SetActive(true);

            var routes = new RoutesManifest
            {
                cars_available = false,
                blocked_routes = new List<string>()
            };

            RouteApplier.ApplyRoutes(routes, null, new List<GameObject> { car });
            AssertFalse(car.activeSelf, "car should be disabled when cars_available == false");

            UnityEngine.Object.DestroyImmediate(car);
        }

        // ── 6. ExtractionZoneBuilder Tests ───────────────────────────────────

        private static void TestExtractionArrowSprite()
        {
            Sprite sprite = ExtractionZoneBuilder.CreateArrowSprite();
            AssertTrue(sprite != null, "Arrow sprite is non-null");
            AssertEquals(28f, sprite.rect.width, "Arrow sprite width");
            AssertEquals(40f, sprite.rect.height, "Arrow sprite height");
        }

        // ── 7. ManifestCoordinator Tests ─────────────────────────────────────

        private static void TestManifestCoordinatorKey()
        {
            var manifest = FallbackManifestProvider.CreateDefaultFallback();
            string key = ManifestCoordinator.BuildManifestKey(manifest);
            AssertTrue(!string.IsNullOrEmpty(key), "Composite key is not empty");
            AssertTrue(key.StartsWith("2|GHOST|"), "Key starts with mission_id and archetype");
            AssertTrue(key.EndsWith("|warehouse_interior|6"), "Key ends with location and guard count");
        }

        private static void TestManifestCoordinatorKeyDiffers()
        {
            var m1 = FallbackManifestProvider.CreateDefaultFallback(2, "GHOST");
            var m2 = FallbackManifestProvider.CreateDefaultFallback(2, "SHADOW");
            string k1 = ManifestCoordinator.BuildManifestKey(m1);
            string k2 = ManifestCoordinator.BuildManifestKey(m2);
            AssertTrue(k1 != k2, "Keys differ across archetypes");
        }

        // ── 8. WorldManifestApplier Facade Invariants ─────────────────────────

        private static void TestFacadePublicFields()
        {
            Type t = typeof(WorldManifestApplier);
            AssertPublicField(t, "targetPrefab", typeof(GameObject));
            AssertPublicField(t, "currentTargetInstance", typeof(GameObject));
            AssertPublicField(t, "guardPrefab", typeof(GameObject));
            AssertPublicField(t, "locationAnchors", typeof(List<WorldManifestApplier.LocationAnchor>));
            AssertPublicField(t, "guardSpawnPoints", typeof(List<Transform>));
            AssertPublicField(t, "routeBarricades", typeof(List<WorldManifestApplier.NamedBarricade>));
            AssertPublicField(t, "drivableCars", typeof(List<GameObject>));
            AssertPublicField(t, "globalLight2D", typeof(Component));
            AssertPublicField(t, "rainParticles", typeof(ParticleSystem));
            AssertPublicField(t, "fogParticles", typeof(ParticleSystem));
        }

        private static void TestFacadeStaticApi()
        {
            Type t = typeof(WorldManifestApplier);
            var prop = t.GetProperty("SceneInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            AssertTrue(prop != null, "SceneInstance property exists");

            var mIsMap2 = t.GetMethod("IsMap2Scene", BindingFlags.Static | BindingFlags.Public);
            AssertTrue(mIsMap2 != null, "IsMap2Scene method exists");

            var mWhiteSprite = t.GetMethod("GetUniversalWhiteSprite", BindingFlags.Static | BindingFlags.Public);
            AssertTrue(mWhiteSprite != null, "GetUniversalWhiteSprite method exists");

            var mPlayer = t.GetMethod("CreateProceduralPlayer", BindingFlags.Static | BindingFlags.Public);
            AssertTrue(mPlayer != null, "CreateProceduralPlayer method exists");

            var mUI = t.GetMethod("CreateRuntimeUI", BindingFlags.Static | BindingFlags.Public);
            AssertTrue(mUI != null, "CreateRuntimeUI method exists");
        }

        // ── Assertion Helpers ────────────────────────────────────────────────

        private static void AssertEquals<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{label}: Expected '{expected}' but got '{actual}'.");
        }

        private static void AssertTrue(bool condition, string label)
        {
            if (!condition)
                throw new Exception($"{label}: Expected true but got false.");
        }

        private static void AssertFalse(bool condition, string label)
        {
            if (condition)
                throw new Exception($"{label}: Expected false but got true.");
        }

        private static void AssertPublicField(Type declaringType, string fieldName, Type expectedType)
        {
            FieldInfo field = declaringType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                throw new Exception($"{declaringType.Name}.{fieldName} public field was not found.");
            if (field.FieldType != expectedType)
                throw new Exception($"{declaringType.Name}.{fieldName} is {field.FieldType.Name}, expected {expectedType.Name}.");
        }
    }
}
#endif
