using UnityEngine;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Leap 3 interactive prop: alarm panel.
    ///
    /// When the player interacts with this object it raises the alarm (dispatches
    /// a "SABOTAGE" / <see cref="interactAction"/> world event). Guards in the scene
    /// can subscribe to <see cref="OnAlarmTriggered"/> to enter alert mode.
    ///
    /// If the player is stealthy they can also *disarm* the panel before it fires
    /// (<see cref="DisarmPanel"/>), preventing the alert entirely.
    ///
    /// Design rules (from leap3_implementation_plan.md §3):
    ///  - Proximity interaction (E key) within <see cref="interactRange"/>.
    ///  - Two states: ARMED (red) → DISARMED (grey) / TRIGGERED (orange).
    ///  - Can only be triggered or disarmed once.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class AlarmPanel : MonoBehaviour
    {
        [Header("Config (set by SectorRuntimeBuilder)")]
        public string interactAction = "SABOTAGE";   // mirrors PropData.interact_action

        [Header("Tuning")]
        public float interactRange = 1.5f;

        [Header("Visuals")]
        public Color armedColor     = new Color(0.90f, 0.20f, 0.20f);  // red
        public Color disarmedColor  = new Color(0.30f, 0.30f, 0.32f);  // grey
        public Color triggeredColor = new Color(1.00f, 0.55f, 0.10f);  // orange

        /// <summary>Raised when the alarm fires. Other systems subscribe to respond.</summary>
        public static event System.Action<AlarmPanel> OnAlarmTriggered;
        /// <summary>Raised when the panel is disarmed before the alarm fires.</summary>
        public static event System.Action<AlarmPanel> OnAlarmDisarmed;

        private enum State { Armed, Disarmed, Triggered }
        private State _state = State.Armed;

        private SpriteRenderer _sr;
        private Transform _player;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr) _sr.color = armedColor;

            GetComponent<BoxCollider2D>().isTrigger = false;
        }

        private void Start()
        {
            GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo) _player = playerGo.transform;
        }

        private void Update()
        {
            if (_state != State.Armed || _player == null) return;

            float dist = Vector2.Distance(transform.position, _player.position);
            if (dist > interactRange) return;

            if (Input.GetKeyDown(KeyCode.E))       TriggerAlarm();
            if (Input.GetKeyDown(KeyCode.F))       DisarmPanel();  // alternate key: disarm
        }

        /// <summary>
        /// Player (or script) deliberately triggers the alarm — alerts guards.
        /// </summary>
        public void TriggerAlarm()
        {
            if (_state != State.Armed) return;
            _state = State.Triggered;

            if (_sr) _sr.color = triggeredColor;
            Debug.Log($"[AlarmPanel] {name} TRIGGERED — action={interactAction}");
            OnAlarmTriggered?.Invoke(this);
        }

        /// <summary>
        /// Player disarms the panel before it fires, preventing the alert.
        /// </summary>
        public void DisarmPanel()
        {
            if (_state != State.Armed) return;
            _state = State.Disarmed;

            if (_sr) _sr.color = disarmedColor;
            Debug.Log($"[AlarmPanel] {name} DISARMED.");
            OnAlarmDisarmed?.Invoke(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.2f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, interactRange);
        }
    }
}
