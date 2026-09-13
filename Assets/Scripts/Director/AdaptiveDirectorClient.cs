using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AdaptiveWorld2D.Director
{
    [Serializable]
    public class TargetManifest
    {
        public string location_id;
        public string location_label;
        public float[] coordinates; // [x, y]
        public bool is_indoors;
        public string armor;
        public int health;
        public string weapon;
        public string defense_style;
        public string target_archetype;
    }

    [Serializable]
    public class GuardsManifest
    {
        public int count;
        public float vision_range;
        public float reaction_speed;
        public float hearing_range;
        public bool high_alert;
        public bool armored;
        public string patrol_pattern;
    }

    [Serializable]
    public class EnvironmentManifest
    {
        public string time_of_day;
        public string weather;
        public float visibility;
        public float rain_intensity;
        public float ambient_light;
        public string ambient_label;
    }

    [Serializable]
    public class RoutesManifest
    {
        public string primary_route;
        public string primary_label;
        public List<string> blocked_routes = new List<string>();
        public List<string> reinforced_barricades = new List<string>();
        public bool cars_available;
    }

    /// <summary>A single guard placement within a sector, offset from the sector center.
    /// Mirrors backend/target_solver.py's GuardEntityConfig exactly (field-for-field).</summary>
    [Serializable]
    public class GuardEntityConfig
    {
        public string sector_id;
        public float offset_x;
        public float offset_y;
        public string role;     // "patrol", "sentry", "bodyguard"
        public bool armored;
    }

    // TargetBossConfig was removed — it mirrored backend/target_solver.py's TargetBossConfig but
    // was never populated in the live JSON payload. Boss stats live in TargetManifest; spatial
    // binding is handled via RoomGraphManifest.target_sector. Re-add if the backend starts sending it.

    /// <summary>One cell of the 3x3 tactical sector grid (60x45 canvas, cells at
    /// X = -20/0/+20, Y = +15/0/-15). Mirrors backend/target_solver.py's SectorConfig.</summary>
    [Serializable]
    public class SectorConfig
    {
        public string id;       // "NW", "NC", "NE", "MW", "MC", "ME", "SW", "SC", "SE"
        public string module;   // "WAREHOUSE_INTERIOR", "FORTIFIED_PLAZA", "SLUM_ALLEYWAYS",
                                 // "URBAN_HIGHWAY", "INDUSTRIAL_DEPOT", "WATERFRONT_DOCKS",
                                 // "SECURITY_COMPOUND", "OPEN_STREET", "VACANT_LOT"
        public float center_x;
        public float center_y;
        public List<GuardEntityConfig> guards = new List<GuardEntityConfig>();
        public List<string> props = new List<string>();
    }

    /// <summary>A single edge between two adjacent sectors. Mirrors backend/target_solver.py's
    /// ConnectionConfig.</summary>
    [Serializable]
    public class ConnectionConfig
    {
        public string from_sector;
        public string to_sector;
        public string status;   // "open", "barricaded_wire_fence", "barricaded_concrete", "walled_off"
    }

    /// <summary>
    /// The full 9-sector tactical blueprint for Map 2.
    /// IMPORTANT: `sectors` and `connections` are JSON ARRAYS (List&lt;T&gt;), never dictionaries --
    /// Unity's JsonUtility cannot deserialize Dictionary&lt;string, T&gt;, so the backend always
    /// emits sector/connection data as lists with an explicit `id` / `from_sector`+`to_sector` key
    /// on each entry instead of using the sector id as a JSON object key.
    /// </summary>
    [Serializable]
    public class RoomGraphManifest
    {
        public List<SectorConfig> sectors = new List<SectorConfig>();
        public List<ConnectionConfig> connections = new List<ConnectionConfig>();
        public string target_sector;
        public string player_spawn_sector;
        public string extraction_sector;

        /// <summary>Linear lookup by sector id (only 9 entries -- no need for a dictionary).</summary>
        public SectorConfig GetSector(string sectorId)
        {
            if (sectors == null) return null;
            for (int i = 0; i < sectors.Count; i++)
            {
                if (sectors[i] != null && sectors[i].id == sectorId) return sectors[i];
            }
            return null;
        }
    }

    [Serializable]
    public class MissionManifest
    {
        public int mission_id;
        public string mission_title;
        public string player_archetype;
        public string mission_briefing;
        public string narrative_debriefing;
        public string psychological_deduction;
        public TargetManifest target;
        public GuardsManifest guards;
        public EnvironmentManifest environment;
        public RoutesManifest routes;
        public RoomGraphManifest room_graph;

        /// <summary>
        /// Post-deserialization null-guard (§2.3A). Unity's JsonUtility does not
        /// instantiate list fields that are missing or null in JSON, so any downstream
        /// foreach over guards/props/sectors/connections would NullReferenceException.
        /// Call this immediately after JsonUtility.FromJson&lt;MissionManifest&gt;().
        /// </summary>
        public void Sanitize()
        {
            if (room_graph == null) return;
            room_graph.sectors     ??= new System.Collections.Generic.List<SectorConfig>();
            room_graph.connections ??= new System.Collections.Generic.List<ConnectionConfig>();
            foreach (var s in room_graph.sectors)
            {
                if (s == null) continue;
                s.guards ??= new System.Collections.Generic.List<GuardEntityConfig>();
                s.props  ??= new System.Collections.Generic.List<string>();
            }
            if (routes != null)
            {
                routes.blocked_routes       ??= new System.Collections.Generic.List<string>();
                routes.reinforced_barricades ??= new System.Collections.Generic.List<string>();
            }
        }
    }

    [Serializable]
    public class PlayerProfileData
    {
        public string player_id;
        public float stealth_ratio;
        public float aggression;
        public float avg_speed;
        public float vehicle_preference;
        public float destruction_index;
        public float shortcut_ratio;
        public string archetype;
        public int total_kills;
        public int walls_destroyed;
        public int cars_burnt;
    }

    /// <summary>Matches the JSON shape returned by /api/telemetry/batch.</summary>
    [Serializable]
    internal class TelemetryResponse { public string current_archetype; }

    public class AdaptiveDirectorClient : MonoBehaviour
    {
        public static AdaptiveDirectorClient Instance { get; private set; }

        [Header("API Server Config")]
        public string apiBaseUrl = "http://127.0.0.1:8001";
        public float flushInterval = 2.0f;
        public bool autoFlush = true;

        [Header("Runtime State")]
        public string currentArchetype = "UNKNOWN";
        public static MissionManifest latestManifest;

        public event Action<string> OnArchetypeChanged;
        public event Action<MissionManifest> OnManifestReceived;

        // ── Disk Cache (§2.3B) ──────────────────────────────────────────────────
        // Persists the latest manifest JSON to a Temp file so it survives Unity
        // domain reloads (Editor script recompile). The cache is in Temp/ so it
        // is never committed to version control but persists across Play sessions
        // within a single Editor launch.
        private const string CACHE_PATH = "Temp/AdaptiveWorld_LatestManifest.json";

        /// <summary>Writes the latest manifest JSON to disk so it survives domain reloads.</summary>
        public static void CacheManifestToDisk(string json)
        {
            try
            {
                System.IO.File.WriteAllText(CACHE_PATH, json);
                Debug.Log($"[AdaptiveDirector] Manifest cached to {CACHE_PATH}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdaptiveDirector] Could not write manifest cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads and deserializes the manifest from the on-disk cache.
        /// Returns null if the cache file does not exist or is corrupt.
        /// </summary>
        public static MissionManifest LoadCachedManifest()
        {
            try
            {
                if (System.IO.File.Exists(CACHE_PATH))
                {
                    string json = System.IO.File.ReadAllText(CACHE_PATH);
                    var m = JsonUtility.FromJson<MissionManifest>(json);
                    m?.Sanitize();
                    if (m != null)
                        Debug.Log($"[AdaptiveDirector] Loaded manifest from disk cache (archetype={m.player_archetype}).");
                    return m;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdaptiveDirector] Failed to load disk cache: {ex.Message}");
            }
            return null;
        }
        // ────────────────────────────────────────────────────────────────────────

        private List<string> _rawEventsQueue = new List<string>();
        private float _flushTimer = 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this)
                Instance = null;
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                          UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            bool isMap2 = !string.IsNullOrEmpty(scene.name) &&
                          scene.name.IndexOf("Map2", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isMap2)
            {
#if UNITY_2023_1_OR_NEWER
                WorldManifestApplier applier = UnityEngine.Object.FindAnyObjectByType<WorldManifestApplier>();
#else
                WorldManifestApplier applier = UnityEngine.Object.FindObjectOfType<WorldManifestApplier>();
#endif
                if (applier != null)
                    applier.BootstrapForMap2(scene);
                else
                    Debug.LogWarning("[AdaptiveDirector] Map2 loaded but no WorldManifestApplier was found.");
                return;
            }

            latestManifest = null;
            try
            {
                if (System.IO.File.Exists(CACHE_PATH))
                    System.IO.File.Delete(CACHE_PATH);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdaptiveDirector] Could not clear manifest cache on fresh Map1 load: {ex.Message}");
            }

            Debug.Log("[AdaptiveDirector] Fresh non-Map2 scene loaded; cleared previous Map2 manifest.");
        }

        private void Update()
        {
            if (!autoFlush) return;

            _flushTimer += Time.deltaTime;
            if (_flushTimer >= flushInterval)
            {
                _flushTimer = 0f;
                if (_rawEventsQueue.Count > 0)
                {
                    FlushQueue();
                }
            }
        }

        public void QueueEvent(string eventType, string jsonData)
        {
            string ts = Time.time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            string item = "{\"event_type\":\"" + eventType + "\",\"timestamp_s\":" + ts + ",\"data\":" + jsonData + "}";
            _rawEventsQueue.Add(item);
        }

        public void FlushQueue()
        {
            if (_rawEventsQueue.Count == 0) return;

            List<string> toSend = new List<string>(_rawEventsQueue);
            _rawEventsQueue.Clear();

            string jsonPayload = "{\"events\":[" + string.Join(",", toSend) + "]}";
            StartCoroutine(PostTelemetryCoroutine(jsonPayload));
        }

        private IEnumerator PostTelemetryCoroutine(string jsonPayload)
        {
            string url = apiBaseUrl + "/api/telemetry/batch";
            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var resp = JsonUtility.FromJson<TelemetryResponse>(req.downloadHandler.text);
                    if (resp != null && !string.IsNullOrEmpty(resp.current_archetype) && resp.current_archetype != currentArchetype)
                    {
                        currentArchetype = resp.current_archetype;
                        OnArchetypeChanged?.Invoke(currentArchetype);
                        Debug.Log($"[AdaptiveDirector] Archetype updated: {currentArchetype}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[AdaptiveDirector] Telemetry flush failed: {req.error}");
                }
            }
        }

        public void RequestMission2Manifest(int seed = 42, Action<MissionManifest> callback = null)
        {
            StartCoroutine(SolveMissionCoroutine(seed, callback));
        }

        private IEnumerator SolveMissionCoroutine(int seed, Action<MissionManifest> callback)
        {
            string url = apiBaseUrl + "/api/mission/solve";
            string body = "{\"seed\":" + seed + "}";

            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string json = req.downloadHandler.text;
                    MissionManifest manifest = JsonUtility.FromJson<MissionManifest>(json);

                    // Sanitize null lists immediately after deserialization (§2.3A)
                    manifest?.Sanitize();

                    latestManifest   = manifest;
                    currentArchetype = manifest.player_archetype;

                    // Persist to disk so domain reloads don't lose the manifest (§2.3B)
                    CacheManifestToDisk(json);

                    Debug.Log($"[AdaptiveDirector] Received M2 Manifest! Target at {manifest.target.location_label}, Armor: {manifest.target.armor}");
                    LogRoomGraph(manifest.room_graph);

                    // Isolated multi-cast dispatch (§2.3C): a subscriber exception
                    // will not halt remaining subscribers.
                    DispatchManifest(manifest);
                    callback?.Invoke(manifest);
                }
                else
                {
                    Debug.LogError($"[AdaptiveDirector] Failed to solve mission: {req.error}");
                }
            }
        }

        /// <summary>
        /// Isolated multicast dispatch (§2.3C). Iterates each subscriber individually
        /// inside a try/catch so a faulty subscriber cannot prevent others from running.
        /// </summary>
        private void DispatchManifest(MissionManifest manifest)
        {
            if (OnManifestReceived == null) return;
            foreach (Action<MissionManifest> handler in OnManifestReceived.GetInvocationList())
            {
                try   { handler(manifest); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        /// <summary>Logs the deserialized room_graph in full -- sectors, target/spawn/extraction
        /// sectors, and every connection status -- so Phase 2 verification can confirm zero data
        /// loss straight from the Unity console.</summary>
        private static void LogRoomGraph(RoomGraphManifest rg)
        {
            if (rg == null || rg.sectors == null || rg.sectors.Count == 0)
            {
                Debug.LogWarning("[AdaptiveDirector] Manifest has no room_graph (older backend response or missing data) -- " +
                                  "Map 2 sector builder will need to fall back to flat target/location data.");
                return;
            }

            int connCount = rg.connections != null ? rg.connections.Count : 0;
            Debug.Log($"[AdaptiveDirector] RoomGraph received: {rg.sectors.Count}/9 sectors, {connCount}/12 connections | " +
                      $"spawn={rg.player_spawn_sector} -> target={rg.target_sector} -> extraction={rg.extraction_sector}");

            if (rg.sectors.Count != 9)
                Debug.LogWarning($"[AdaptiveDirector] room_graph has {rg.sectors.Count} sectors, expected 9 -- possible deserialization data loss!");
            if (connCount != 12)
                Debug.LogWarning($"[AdaptiveDirector] room_graph has {connCount} connections, expected 12 -- possible deserialization data loss!");

            foreach (var s in rg.sectors)
            {
                int guardCount = s.guards != null ? s.guards.Count : 0;
                string propsStr = s.props != null ? string.Join(",", s.props) : "";
                Debug.Log($"[AdaptiveDirector]   Sector {s.id}: module={s.module} center=({s.center_x}, {s.center_y}) guards={guardCount} props=[{propsStr}]");
            }

            if (rg.connections != null)
            {
                foreach (var c in rg.connections)
                {
                    Debug.Log($"[AdaptiveDirector]   Connection {c.from_sector} <-> {c.to_sector}: {c.status}");
                }
            }
        }

        public void ResetDirectorSession(Action onDone = null)
        {
            StartCoroutine(ResetCoroutine(onDone));
        }

        private IEnumerator ResetCoroutine(Action onDone)
        {
            string url = apiBaseUrl + "/api/reset";
            using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes("{}");
                req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");

                yield return req.SendWebRequest();
                onDone?.Invoke();
            }
        }
    }
}
