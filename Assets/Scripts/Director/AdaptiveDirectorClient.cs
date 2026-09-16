using System;
using System.Collections.Generic;
using UnityEngine;

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
        ///
        /// Delegates to <see cref="ManifestValidator.Sanitize"/> (Architecture Improvement
        /// Plan, Phase 2) so this remains the stable compatibility API.
        /// </summary>
        public void Sanitize()
        {
            ManifestValidator.Sanitize(this);
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
    public class TelemetryResponse { public string current_archetype; }

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

        // ── Phase 5 Extracted Services ──────────────────────────────────────────
        private readonly TelemetryEventQueue _eventQueue = new TelemetryEventQueue();
        private DirectorTransport _transport;
        private float _flushTimer = 0f;

        public TelemetryEventQueue EventQueue => _eventQueue;
        public DirectorTransport Transport => _transport ??= new DirectorTransport(apiBaseUrl);

        // ── Disk Cache (§2.3B / Phase 5 ManifestCache Delegation) ────────────────
        public const string CACHE_PATH = ManifestCache.DEFAULT_CACHE_PATH;

        /// <summary>Writes the latest manifest JSON to disk so it survives domain reloads.</summary>
        public static void CacheManifestToDisk(string json)
        {
            ManifestCache.SaveToDisk(json, CACHE_PATH);
        }

        /// <summary>
        /// Loads and deserializes the manifest from the on-disk cache.
        /// Returns null if the cache file does not exist or is corrupt.
        /// </summary>
        public static MissionManifest LoadCachedManifest()
        {
            return ManifestCache.LoadFromDisk(CACHE_PATH);
        }
        // ────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _transport = new DirectorTransport(apiBaseUrl);
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
            DirectorSessionCoordinator.HandleSceneLoaded(scene, mode, () => latestManifest = null);
        }

        private void Update()
        {
            if (!autoFlush) return;

            _flushTimer += Time.deltaTime;
            if (_flushTimer >= flushInterval)
            {
                _flushTimer = 0f;
                if (_eventQueue.Count > 0)
                {
                    FlushQueue();
                }
            }
        }

        public void QueueEvent(string eventType, string jsonData)
        {
            _eventQueue.Enqueue(eventType, jsonData);
        }

        public void FlushQueue()
        {
            string jsonPayload = _eventQueue.DequeueBatchPayload();
            if (string.IsNullOrEmpty(jsonPayload)) return;

            EnsureTransport();
            StartCoroutine(_transport.PostTelemetryBatch(jsonPayload, OnTelemetrySuccess, OnTelemetryError));
        }

        private void OnTelemetrySuccess(string responseText)
        {
            var resp = DirectorResponseParser.ParseTelemetryResponse(responseText);
            if (resp != null && !string.IsNullOrEmpty(resp.current_archetype) && resp.current_archetype != currentArchetype)
            {
                currentArchetype = resp.current_archetype;
                OnArchetypeChanged?.Invoke(currentArchetype);
                Debug.Log($"[AdaptiveDirector] Archetype updated: {currentArchetype}");
            }
        }

        private void OnTelemetryError(string error)
        {
            Debug.LogWarning($"[AdaptiveDirector] Telemetry flush failed: {error}");
        }

        public void RequestMission2Manifest(int seed = 42, Action<MissionManifest> callback = null)
        {
            EnsureTransport();
            StartCoroutine(_transport.PostMissionSolve(seed,
                json => HandleMissionSolveSuccess(json, callback),
                error => Debug.LogError($"[AdaptiveDirector] Failed to solve mission: {error}")));
        }

        private void HandleMissionSolveSuccess(string json, Action<MissionManifest> callback)
        {
            MissionManifest manifest = DirectorResponseParser.ParseMissionManifest(json);
            if (manifest == null) return;

            latestManifest   = manifest;
            currentArchetype = manifest.player_archetype;

            // Persist to disk so domain reloads don't lose the manifest (§2.3B)
            ManifestCache.SaveToDisk(json, CACHE_PATH);

            Debug.Log($"[AdaptiveDirector] Received M2 Manifest! Target at {manifest.target?.location_label}, Armor: {manifest.target?.armor}");
            LogRoomGraph(manifest.room_graph);

            // Isolated multi-cast dispatch (§2.3C): a subscriber exception
            // will not halt remaining subscribers.
            DispatchManifest(manifest);
            callback?.Invoke(manifest);
        }

        private void EnsureTransport()
        {
            if (_transport == null)
            {
                _transport = new DirectorTransport(apiBaseUrl);
            }
            else if (_transport.ApiBaseUrl != apiBaseUrl)
            {
                _transport.ApiBaseUrl = apiBaseUrl;
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
            EnsureTransport();
            StartCoroutine(_transport.PostReset(
                () => onDone?.Invoke(),
                error => Debug.LogWarning($"[AdaptiveDirector] Reset failed: {error}")));
        }
    }
}
