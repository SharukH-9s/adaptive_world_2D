using UnityEngine;
using UnityEngine.Events;

namespace AdaptiveWorld2D.Mission
{
    [RequireComponent(typeof(Collider2D))]
    public class MissionZoneTrigger : MonoBehaviour
    {
        public enum ZoneType
        {
            GunShop,
            MissionEnd,
            TargetArea,
            GenericCheckpoint
        }

        [Header("Zone Configuration")]
        public ZoneType zoneType = ZoneType.GenericCheckpoint;
        public string zoneName = "Gun Shop";
        public bool requireInteractButton = false;
        public bool triggerOnce = true;

        [Header("Events")]
        public UnityEvent onZoneEntered;

        private bool _hasTriggered = false;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.CompareTag("Player") && !requireInteractButton)
            {
                TriggerZone(other.gameObject);
            }
        }

        public void TriggerZone(GameObject player)
        {
            if (_hasTriggered && triggerOnce) return;
            _hasTriggered = true;

            if (MissionManager.Instance != null)
            {
                MissionManager.Instance.OnZoneReached(this);
            }
        }

        public void ResetTrigger()
        {
            _hasTriggered = false;
        }
    }
}
