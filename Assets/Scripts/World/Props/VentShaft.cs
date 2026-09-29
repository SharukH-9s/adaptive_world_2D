using UnityEngine;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Leap 3 interactive prop: traversable vent shaft.
    ///
    /// Allows the player to teleport between two sectors without passing through
    /// a gateway (useful for stealth routes). The shaft is a trigger zone: when
    /// the player overlaps it and presses E, they are instantly repositioned at
    /// <see cref="destinationSectorId"/>'s vent exit (if one exists).
    ///
    /// Design rules (from leap3_implementation_plan.md §3):
    ///  - Trigger collider (no physics block).
    ///  - Destination is a sector id string; actual exit position is found by
    ///    scanning the scene for a VentShaft whose <see cref="destinationSectorId"/>
    ///    matches the *current* sector id (reverse lookup).
    ///  - Falls back gracefully when no exit vent is found: just logs a warning.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class VentShaft : MonoBehaviour
    {
        [Header("Config (set by SectorRuntimeBuilder)")]
        [Tooltip("Sector id of the vent exit. E.g. 'NE', 'R1_C2', etc.")]
        public string destinationSectorId;

        [Header("Tuning")]
        public float interactRange = 1.2f;

        [Header("Visuals")]
        public Color idleColor   = new Color(0.50f, 0.50f, 0.55f, 0.80f);
        public Color activeColor = new Color(0.30f, 0.90f, 0.55f, 0.90f);   // green glow when nearby

        private SpriteRenderer _sr;
        private Transform _player;
        private bool _playerNearby;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr) _sr.color = idleColor;

            var col = GetComponent<BoxCollider2D>();
            col.isTrigger = true;
        }

        private void Start()
        {
            GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo) _player = playerGo.transform;
        }

        private void Update()
        {
            if (_player == null) return;

            bool nearby = Vector2.Distance(transform.position, _player.position) <= interactRange;
            if (nearby != _playerNearby)
            {
                _playerNearby = nearby;
                if (_sr) _sr.color = nearby ? activeColor : idleColor;
            }

            if (nearby && Input.GetKeyDown(KeyCode.E))
                TryTraverse();
        }

        /// <summary>
        /// Teleports the player to the exit vent in <see cref="destinationSectorId"/>.
        /// </summary>
        public void TryTraverse()
        {
            if (string.IsNullOrEmpty(destinationSectorId))
            {
                Debug.LogWarning($"[VentShaft] {name}: destinationSectorId is not set.");
                return;
            }

            // Find the matching exit vent: a VentShaft in the scene whose
            // destinationSectorId is the sector this vent belongs to.
            string mySectorId = GetMySectorId();
            VentShaft[] allVents = FindObjectsOfType<VentShaft>();
            foreach (VentShaft v in allVents)
            {
                if (v == this) continue;
                if (v.destinationSectorId == mySectorId)
                {
                    Debug.Log($"[VentShaft] Traversing {name} -> {v.name}");
                    _player.position = v.transform.position + Vector3.up * 1.2f;
                    return;
                }
            }

            Debug.LogWarning($"[VentShaft] {name}: no exit vent found for sector '{mySectorId}' (destination='{destinationSectorId}').");
        }

        /// <summary>
        /// Infers this vent's own sector id by walking up the hierarchy to the
        /// Sector_ root object created by SectorRuntimeBuilder.
        /// </summary>
        private string GetMySectorId()
        {
            Transform t = transform;
            while (t != null)
            {
                if (t.name.StartsWith("Sector_"))
                {
                    // Name format: "Sector_{id}_{module}"
                    string[] parts = t.name.Split('_');
                    if (parts.Length >= 2) return parts[1];
                }
                t = t.parent;
            }
            return "";
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 0.55f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, interactRange);
            if (!string.IsNullOrEmpty(destinationSectorId))
            {
#if UNITY_EDITOR
                UnityEditor.Handles.Label(transform.position + Vector3.up * 0.8f, $"→ {destinationSectorId}");
#endif
            }
        }
    }
}
