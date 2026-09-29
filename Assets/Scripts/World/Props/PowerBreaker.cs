using UnityEngine;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Leap 3 interactive prop: electrical power breaker panel.
    ///
    /// When the player interacts with this object, it cuts power (raises a
    /// "CUT_POWER" world event) or performs whatever action <see cref="interactAction"/>
    /// specifies. Paired lights / cameras in the scene can subscribe to
    /// <see cref="OnPowerCut"/> to react (e.g., turn off surveillance spotlights).
    ///
    /// Design rules (from leap3_implementation_plan.md §3):
    ///  - Player must be within <see cref="interactRange"/> units.
    ///  - Single-use: once activated it cannot be reset.
    ///  - Raises <see cref="OnPowerCut"/> static event for other systems to consume.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class PowerBreaker : MonoBehaviour
    {
        [Header("Config (set by SectorRuntimeBuilder)")]
        public string interactAction = "CUT_POWER";   // mirrors PropData.interact_action

        [Header("Tuning")]
        [Tooltip("Distance within which the player can interact.")]
        public float interactRange = 1.5f;

        [Header("Visuals")]
        public Color activeColor   = new Color(0.15f, 0.55f, 1.00f);   // blue  — powered
        public Color disabledColor = new Color(0.15f, 0.15f, 0.18f);   // dark  — cut

        /// <summary>Raised once, the moment the breaker is tripped.</summary>
        public static event System.Action<PowerBreaker> OnPowerCut;

        private bool _isTripped;
        private SpriteRenderer _sr;
        private Transform _player;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr) _sr.color = activeColor;

            GetComponent<BoxCollider2D>().isTrigger = false;
        }

        private void Start()
        {
            // Find the player lazily so that we don't need a hard reference.
            GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo) _player = playerGo.transform;
        }

        private void Update()
        {
            if (_isTripped || _player == null) return;

            float dist = Vector2.Distance(transform.position, _player.position);
            if (dist <= interactRange && Input.GetKeyDown(KeyCode.E))
            {
                TripBreaker();
            }
        }

        /// <summary>Programmatic trigger (e.g. from an explosion or another system).</summary>
        public void TripBreaker()
        {
            if (_isTripped) return;
            _isTripped = true;

            if (_sr) _sr.color = disabledColor;

            Debug.Log($"[PowerBreaker] {name} tripped — action={interactAction}");
            OnPowerCut?.Invoke(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.15f, 0.55f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, interactRange);
        }
    }
}
