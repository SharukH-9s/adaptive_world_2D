#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.EditorTests
{
    /// <summary>
    /// In-Editor characterization test suite for Phase 5 (Architecture Improvement Plan).
    /// Validates the separated components: TelemetryEventQueue, DirectorResponseParser,
    /// ManifestCache, DirectorSessionCoordinator, and the AdaptiveDirectorClient facade.
    ///
    /// Run via Menu: AdaptiveWorld > Run Phase 5 Director Tests
    /// </summary>
    public static class Phase5DirectorTests
    {
        [MenuItem("AdaptiveWorld/Run Phase 5 Director Tests")]
        public static void RunAllPhase5Tests()
        {
            int passed = 0;
            int total  = 0;

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
            Debug.Log("  ADAPTIVE WORLD 2D - PHASE 5 DIRECTOR CLIENT SPLIT TEST SUITE");
            Debug.Log("==================================================================");

            // ── 1. TelemetryEventQueue Tests ─────────────────────────────────
            RunTest("TelemetryEventQueue: Enqueue and Count", TestQueueEnqueueAndCount);
            RunTest("TelemetryEventQueue: DequeueBatchPayload drained & formatted correctly", TestQueueDrainAndPayload);
            RunTest("TelemetryEventQueue: Dequeue on empty returns null", TestQueueEmptyDequeue);
            RunTest("TelemetryEventQueue: Clear resets count", TestQueueClear);
            RunTest("TelemetryEventQueue: Invariant timestamp formatting (dot separator)", TestQueueTimestampFormat);

            // ── 2. DirectorResponseParser Tests ──────────────────────────────
            RunTest("DirectorResponseParser: ParseTelemetryResponse valid JSON", TestParseTelemetryValid);
            RunTest("DirectorResponseParser: ParseTelemetryResponse malformed JSON returns null", TestParseTelemetryMalformed);
            RunTest("DirectorResponseParser: ParseMissionManifest valid JSON with Sanitize applied", TestParseManifestValid);
            RunTest("DirectorResponseParser: ParseMissionManifest malformed JSON returns null", TestParseManifestMalformed);

            // ── 3. ManifestCache Tests ───────────────────────────────────────
            RunTest("ManifestCache: Save, load text, load object, and clear", TestManifestCacheRoundtrip);
            RunTest("ManifestCache: Load from non-existent path returns null", TestManifestCacheNonExistent);

            // ── 4. Facade Compatibility & Invariants ─────────────────────────
            RunTest("AdaptiveDirectorClient: Exposes static latestManifest and CACHE_PATH", TestFacadeStaticFields);
            RunTest("AdaptiveDirectorClient: CacheManifestToDisk and LoadCachedManifest delegate correctly", TestFacadeCacheDelegation);

            Debug.Log("==================================================================");
            Debug.Log($"  PHASE 5 TEST RESULTS: {passed} / {total} PASSED");
            Debug.Log("==================================================================");
        }

        private static void TestQueueEnqueueAndCount()
        {
            var queue = new TelemetryEventQueue();
            AssertEquals(0, queue.Count, "Initial queue count");

            queue.Enqueue("navigation", "{\"speed\":3.5}", 1.25f);
            queue.Enqueue("combat", "{\"weapon\":\"knife\"}", 2.50f);

            AssertEquals(2, queue.Count, "Count after enqueuing 2 events");
        }

        private static void TestQueueDrainAndPayload()
        {
            var queue = new TelemetryEventQueue();
            queue.Enqueue("nav", "{\"x\":1}", 1.0f);
            queue.Enqueue("combat", "{\"dmg\":10}", 2.0f);

            string payload = queue.DequeueBatchPayload();
            AssertEquals(0, queue.Count, "Queue count after dequeue");
            AssertTrue(!string.IsNullOrEmpty(payload), "Payload is not empty");
            AssertTrue(payload.StartsWith("{\"events\":["), "Payload has events root array");
            AssertTrue(payload.Contains("\"event_type\":\"nav\""), "Contains nav event");
            AssertTrue(payload.Contains("\"event_type\":\"combat\""), "Contains combat event");
            AssertTrue(payload.EndsWith("]}"), "Payload properly closed");
        }

        private static void TestQueueEmptyDequeue()
        {
            var queue = new TelemetryEventQueue();
            string payload = queue.DequeueBatchPayload();
            AssertTrue(payload == null, "Empty queue dequeue should return null");
        }

        private static void TestQueueClear()
        {
            var queue = new TelemetryEventQueue();
            queue.Enqueue("test", "{}", 0f);
            queue.Clear();
            AssertEquals(0, queue.Count, "Queue count after clear");
            AssertTrue(queue.DequeueBatchPayload() == null, "Draining cleared queue returns null");
        }

        private static void TestQueueTimestampFormat()
        {
            var queue = new TelemetryEventQueue();
            queue.Enqueue("test", "{}", 12.3456f);
            string payload = queue.DequeueBatchPayload();
            AssertTrue(payload.Contains("\"timestamp_s\":12.35"), "Timestamp formatted with invariant culture and 2 decimals");
        }

        private static void TestParseTelemetryValid()
        {
            string json = "{\"current_archetype\":\"GHOST\"}";
            var resp = DirectorResponseParser.ParseTelemetryResponse(json);
            AssertTrue(resp != null, "Response parsed");
            AssertEquals("GHOST", resp.current_archetype, "Archetype parsed");
        }

        private static void TestParseTelemetryMalformed()
        {
            string badJson = "not a valid json {{{";
            var resp = DirectorResponseParser.ParseTelemetryResponse(badJson);
            // JsonUtility may return an empty object or null on parse error without throwing
            AssertTrue(resp == null || string.IsNullOrEmpty(resp.current_archetype), "Malformed telemetry handled safely");
        }

        private static void TestParseManifestValid()
        {
            string json = @"{
                ""mission_id"": 2,
                ""mission_title"": ""NODE 02"",
                ""player_archetype"": ""GHOST"",
                ""target"": { ""location_label"": ""Warehouse"", ""armor"": ""light"", ""health"": 100 },
                ""guards"": { ""count"": 4, ""vision_range"": 8.0, ""reaction_speed"": 1.5, ""hearing_range"": 10.0 },
                ""environment"": { ""time_of_day"": ""night"", ""weather"": ""fog"", ""visibility"": 0.5, ""ambient_light"": 0.3 },
                ""routes"": { ""primary_route"": ""North"", ""primary_label"": ""North Route"" },
                ""room_graph"": {
                    ""target_sector"": ""NW"",
                    ""player_spawn_sector"": ""SW"",
                    ""extraction_sector"": ""NE"",
                    ""sectors"": [
                        { ""id"": ""NW"", ""module"": ""WAREHOUSE_INTERIOR"", ""center_x"": -20.0, ""center_y"": 15.0 }
                    ],
                    ""connections"": []
                }
            }";

            var manifest = DirectorResponseParser.ParseMissionManifest(json);
            AssertTrue(manifest != null, "Manifest parsed successfully");
            AssertEquals(2, manifest.mission_id, "mission_id");
            AssertEquals("GHOST", manifest.player_archetype, "player_archetype");
            AssertTrue(manifest.room_graph != null, "room_graph parsed");
            AssertTrue(manifest.room_graph.sectors != null, "sectors list initialized");
            AssertEquals(1, manifest.room_graph.sectors.Count, "sectors count");
            AssertTrue(manifest.room_graph.sectors[0].guards != null, "guards list non-null (sanitized)");
            AssertTrue(manifest.room_graph.sectors[0].props != null, "props list non-null (sanitized)");
            AssertTrue(manifest.routes.blocked_routes != null, "blocked_routes sanitized");
            AssertTrue(manifest.routes.reinforced_barricades != null, "reinforced_barricades sanitized");
        }

        private static void TestParseManifestMalformed()
        {
            string badJson = "{ invalid json content ...";
            var manifest = DirectorResponseParser.ParseMissionManifest(badJson);
            AssertTrue(manifest == null || manifest.target == null, "Malformed manifest parsed safely");
        }

        private static void TestManifestCacheRoundtrip()
        {
            string testCachePath = "Temp/Test_Phase5_ManifestCache.json";
            string testJson = "{\"mission_id\":999,\"player_archetype\":\"WRECKING_BALL\"}";

            try
            {
                ManifestCache.SaveToDisk(testJson, testCachePath);
                AssertTrue(File.Exists(testCachePath), "Cache file exists after save");

                string loadedJson = ManifestCache.LoadJsonFromDisk(testCachePath);
                AssertEquals(testJson, loadedJson, "Loaded raw json matches saved");

                var loadedManifest = ManifestCache.LoadFromDisk(testCachePath);
                AssertTrue(loadedManifest != null, "Loaded manifest object non-null");
                AssertEquals(999, loadedManifest.mission_id, "mission_id preserved");
                AssertEquals("WRECKING_BALL", loadedManifest.player_archetype, "archetype preserved");

                ManifestCache.ClearDiskCache(testCachePath);
                AssertTrue(!File.Exists(testCachePath), "Cache file deleted after clear");
            }
            finally
            {
                if (File.Exists(testCachePath)) File.Delete(testCachePath);
            }
        }

        private static void TestManifestCacheNonExistent()
        {
            string nonExistent = "Temp/NonExistent_Test_12345.json";
            AssertTrue(ManifestCache.LoadJsonFromDisk(nonExistent) == null, "LoadJsonFromDisk on missing returns null");
            AssertTrue(ManifestCache.LoadFromDisk(nonExistent) == null, "LoadFromDisk on missing returns null");
        }

        private static void TestFacadeStaticFields()
        {
            AssertEquals(ManifestCache.DEFAULT_CACHE_PATH, AdaptiveDirectorClient.CACHE_PATH, "AdaptiveDirectorClient.CACHE_PATH matches ManifestCache.DEFAULT_CACHE_PATH");
            // latestManifest static property exists and is accessible
            AdaptiveDirectorClient.latestManifest = null;
            AssertTrue(AdaptiveDirectorClient.latestManifest == null, "latestManifest static field accessible");
        }

        private static void TestFacadeCacheDelegation()
        {
            string testJson = "{\"mission_id\":777,\"player_archetype\":\"SPEEDRUNNER\"}";
            AdaptiveDirectorClient.CacheManifestToDisk(testJson);
            var loaded = AdaptiveDirectorClient.LoadCachedManifest();
            AssertTrue(loaded != null, "Loaded via facade non-null");
            AssertEquals(777, loaded.mission_id, "mission_id loaded via facade");
            AssertEquals("SPEEDRUNNER", loaded.player_archetype, "archetype loaded via facade");
            ManifestCache.ClearDiskCache();
        }

        // ── Assertion Helpers ─────────────────────────────────────────────
        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception($"Assertion failed: {message}");
        }

        private static void AssertEquals<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"Assertion failed: {message}. Expected: '{expected}', Actual: '{actual}'");
        }
    }
}
#endif
