using UnityEngine;

namespace AdaptiveWorld2D.Director
{
    public class TelemetryReporter : MonoBehaviour
    {
        public static TelemetryReporter Instance { get; private set; }

        // ── Serializable payload structs — used with JsonUtility.ToJson ──────

        [System.Serializable]
        private struct NavigationPayload
        {
            public string movement_type;
            public string surface;
            public float  duration_s;
            public float  avg_speed;
            public float  distance_m;
        }

        [System.Serializable]
        private struct CombatPayload
        {
            public string style;
            public string weapon;
            public string target_type;
            public bool   lethal;
            public float  distance_m;
        }

        [System.Serializable]
        private struct DestructionPayload
        {
            public string destruction_type;
            public string object_id;
        }

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void ReportMovement(string movementType, string surface, float duration, float avgSpeed, float distance)
        {
            if (AdaptiveDirectorClient.Instance == null) return;

            string json = JsonUtility.ToJson(new NavigationPayload
            {
                movement_type = movementType,
                surface       = surface,
                duration_s    = duration,
                avg_speed     = avgSpeed,
                distance_m    = distance,
            });
            AdaptiveDirectorClient.Instance.QueueEvent("navigation", json);
        }

        public void ReportCombat(string style, string weapon, string targetType, bool lethal, float distance)
        {
            if (AdaptiveDirectorClient.Instance == null) return;

            string json = JsonUtility.ToJson(new CombatPayload
            {
                style       = style,
                weapon      = weapon,
                target_type = targetType,
                lethal      = lethal,
                distance_m  = distance,
            });
            AdaptiveDirectorClient.Instance.QueueEvent("combat", json);
        }

        public void ReportDestruction(string destructionType, string objectId)
        {
            if (AdaptiveDirectorClient.Instance == null) return;

            string json = JsonUtility.ToJson(new DestructionPayload
            {
                destruction_type = destructionType,
                object_id        = objectId,
            });
            AdaptiveDirectorClient.Instance.QueueEvent("destruction", json);
        }
    }
}
