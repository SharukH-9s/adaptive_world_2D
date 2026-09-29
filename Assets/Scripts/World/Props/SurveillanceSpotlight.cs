using UnityEngine;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Leap 3 interactive prop: rotating surveillance spotlight.
    ///
    /// Sweeps a cone of light back and forth. If the player enters the cone they
    /// are "spotted" (raises <see cref="OnPlayerSpotted"/>). Tripping a
    /// <see cref="PowerBreaker"/> in the same sector (or the same level) silences
    /// the light permanently.
    ///
    /// Design rules (from leap3_implementation_plan.md §3):
    ///  - Spotlight rotates between <see cref="minAngle"/> and <see cref="maxAngle"/>
    ///    at <see cref="sweepSpeed"/> degrees/second.
    ///  - Detection cone half-angle = <see cref="coneHalfAngle"/>.
    ///  - Range = <see cref="detectionRange"/>.
    ///  - Subscribes to <see cref="PowerBreaker.OnPowerCut"/> to disable itself.
    /// </summary>
    public class SurveillanceSpotlight : MonoBehaviour
    {
        [Header("Sweep")]
        public float minAngle     = -45f;
        public float maxAngle     =  45f;
        public float sweepSpeed   = 30f;   // degrees per second

        [Header("Detection")]
        public float detectionRange  = 6f;
        public float coneHalfAngle   = 20f;  // half-angle of vision cone in degrees

        [Header("Visuals")]
        public Color idleColor    = new Color(0.90f, 0.85f, 0.40f, 0.85f);   // yellow
        public Color alertColor   = new Color(1.00f, 0.30f, 0.20f, 1.00f);   // red when triggered
        public Color deadColor    = new Color(0.20f, 0.20f, 0.22f, 0.70f);   // grey when powered off

        /// <summary>Raised when the spotlight detects the player.</summary>
        public static event System.Action<SurveillanceSpotlight> OnPlayerSpotted;

        private bool _isDisabled;
        private bool _hasSpotted;
        private float _currentAngle;
        private float _sweepDir = 1f;

        private SpriteRenderer _sr;
        private Transform _player;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (_sr) _sr.color = idleColor;
            _currentAngle = minAngle;
        }

        private void OnEnable()
        {
            PowerBreaker.OnPowerCut += HandlePowerCut;
        }

        private void OnDisable()
        {
            PowerBreaker.OnPowerCut -= HandlePowerCut;
        }

        private void Start()
        {
            GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo) _player = playerGo.transform;
        }

        private void Update()
        {
            if (_isDisabled) return;

            // --- Rotate the spotlight ---
            _currentAngle += _sweepDir * sweepSpeed * Time.deltaTime;
            if (_currentAngle >= maxAngle)       { _currentAngle = maxAngle;  _sweepDir = -1f; }
            else if (_currentAngle <= minAngle)  { _currentAngle = minAngle;  _sweepDir =  1f; }

            transform.rotation = Quaternion.Euler(0f, 0f, _currentAngle);

            // --- Detection ---
            if (_hasSpotted || _player == null) return;

            Vector2 toPlayer = (Vector2)_player.position - (Vector2)transform.position;
            float dist = toPlayer.magnitude;
            if (dist > detectionRange) return;

            // Compare angle between spotlight forward and direction-to-player
            Vector2 forward = transform.up;
            float angle = Vector2.Angle(forward, toPlayer);
            if (angle <= coneHalfAngle)
            {
                // Raycast to check for walls between spotlight and player
                int wallMask = (1 << SectorRuntimeBuilder.LAYER_BUILDING);
                RaycastHit2D hit = Physics2D.Raycast(transform.position, toPlayer.normalized, dist, wallMask);
                if (!hit.collider)
                {
                    SpotPlayer();
                }
            }
        }

        private void SpotPlayer()
        {
            if (_hasSpotted) return;
            _hasSpotted = true;

            if (_sr) _sr.color = alertColor;
            Debug.Log($"[SurveillanceSpotlight] {name}: player spotted!");
            OnPlayerSpotted?.Invoke(this);
        }

        private void HandlePowerCut(PowerBreaker breaker)
        {
            if (_isDisabled) return;
            _isDisabled = true;
            if (_sr) _sr.color = deadColor;
            transform.rotation = Quaternion.identity;
            Debug.Log($"[SurveillanceSpotlight] {name}: powered off by {breaker.name}.");
        }

        private void OnDrawGizmosSelected()
        {
            // Draw detection cone in scene view.
            Gizmos.color = new Color(0.9f, 0.85f, 0.4f, 0.25f);
            Vector2 fwd = transform.up;
            Gizmos.DrawLine(transform.position, (Vector2)transform.position + fwd * detectionRange);

            float left  = _currentAngle - coneHalfAngle;
            float right = _currentAngle + coneHalfAngle;
            Vector3 leftDir  = new Vector3(Mathf.Sin(left  * Mathf.Deg2Rad), Mathf.Cos(left  * Mathf.Deg2Rad), 0f);
            Vector3 rightDir = new Vector3(Mathf.Sin(right * Mathf.Deg2Rad), Mathf.Cos(right * Mathf.Deg2Rad), 0f);
            Gizmos.DrawLine(transform.position, transform.position + leftDir  * detectionRange);
            Gizmos.DrawLine(transform.position, transform.position + rightDir * detectionRange);
        }
    }
}
