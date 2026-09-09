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

        private List<string> _rawEventsQueue = new List<string>();
        private float _flushTimer = 0f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
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
                    latestManifest = manifest;
                    currentArchetype = manifest.player_archetype;

                    Debug.Log($"[AdaptiveDirector] Received M2 Manifest! Target at {manifest.target.location_label}, Armor: {manifest.target.armor}");
                    OnManifestReceived?.Invoke(manifest);
                    callback?.Invoke(manifest);
                }
                else
                {
                    Debug.LogError($"[AdaptiveDirector] Failed to solve mission: {req.error}");
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
