using UnityEngine;

namespace AdaptiveWorld2D.AI
{
    [RequireComponent(typeof(EnemyPatrol))]
    public class EnemyDetection : MonoBehaviour
    {
        [Header("Vision Cone (Driven by Director Manifest)")]
        public float visionRange = 6.0f;
        public float visionAngle = 90.0f;
        public float reactionSpeed = 1.0f; // Multiplier: >1 is faster alert, <1 is slower
        public float hearingRange = 7.0f;

        [Header("Obstacle Layer (Walls block vision)")]
        public LayerMask obstacleLayer;
        public LayerMask playerLayer;

        [Header("Alert UI")]
        public GameObject alertIconObject;

        /// <summary>
        /// The base (unscaled) vision range set by the manifest.
        /// Stored so weather scaling can always compute from the original value
        /// rather than compounding repeated multiplications.
        /// </summary>
        private float _baseVisionRange = -1f;

        private EnemyPatrol _patrol;
        private Transform _playerTransform;
        private float _awarenessTimer = 0f;
        private const float BaseAlertThreshold = 0.6f;

        private void Awake()
        {
            _patrol = GetComponent<EnemyPatrol>();
        }

        private void Start()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) _playerTransform = p.transform;
            if (alertIconObject != null) alertIconObject.SetActive(false);

            // Capture the initial inspector-assigned range as the base
            if (_baseVisionRange < 0f)
                _baseVisionRange = visionRange;
        }

        private void Update()
        {
            if (_patrol.isDead) return;

            if (!_patrol.isAlerted && _playerTransform != null)
            {
                CheckVision();
            }
        }

        private void CheckVision()
        {
            Vector2 toPlayer = (Vector2)_playerTransform.position - (Vector2)transform.position;
            float dist = toPlayer.magnitude;

            if (dist <= visionRange)
            {
                // Check if within FOV cone (transform.up is facing direction)
                float angleToPlayer = Vector2.Angle(transform.up, toPlayer);
                if (angleToPlayer <= visionAngle * 0.5f)
                {
                    // Check line of sight (no walls in between)
                    RaycastHit2D hit = Physics2D.Raycast(transform.position, toPlayer.normalized, dist, obstacleLayer);
                    if (hit.collider == null)
                    {
                        // In line of sight! Accumulate awareness
                        _awarenessTimer += Time.deltaTime * reactionSpeed;
                        if (_awarenessTimer >= BaseAlertThreshold)
                        {
                            TriggerAlert();
                        }
                        return;
                    }
                }
            }

            // Slowly decay awareness if lost sight
            _awarenessTimer = Mathf.Max(0f, _awarenessTimer - Time.deltaTime * 0.5f);
        }

        public void AlertToNoise(Vector2 soundOrigin)
        {
            if (_patrol.isDead) return;

            float dist = Vector2.Distance(transform.position, soundOrigin);
            if (dist <= hearingRange)
            {
                Debug.Log("[EnemyDetection] Heard gunshots or explosion! Investigating...");
                TriggerAlert();
            }
        }

        /// <summary>
        /// Scales the effective vision range by the manifest environment visibility value.
        /// Formula: effectiveVisionRange = baseVisionRange × visibility
        /// (§5.3B — Weather-Gameplay Feedback Loop)
        ///
        /// Example: heavy fog with visibility=0.5 drops a 6.0-unit cone to 3.0 units,
        /// actively rewarding stealth players in bad weather.
        /// </summary>
        /// <param name="visibility">0.0 (blind) to 1.0 (full visibility). Values above 1 allowed for buff conditions.</param>
        public void SetWeatherVisibility(float visibility)
        {
            // Capture base range on first call if Start() hasn't run yet
            if (_baseVisionRange < 0f)
                _baseVisionRange = visionRange;

            visionRange = _baseVisionRange * Mathf.Clamp(visibility, 0.05f, 2.0f);
            Debug.Log($"[EnemyDetection] Vision range scaled to {visionRange:F2} (base={_baseVisionRange:F2} × visibility={visibility:F2})");
        }

        /// <summary>
        /// Directly sets the base vision range AND recalculates the effective range
        /// using the last applied visibility multiplier. Called by WorldManifestApplier
        /// when applying guard stats, so subsequent weather scaling starts from the
        /// correct manifest-driven base.
        /// </summary>
        public void SetBaseVisionRange(float range)
        {
            _baseVisionRange = range;
            visionRange = range; // Reset to full before visibility is applied
        }

        /// <summary>Force-alerts the guard (used by AlertSystem sector broadcast and EnemyPatrol.Alert()).</summary>
        public void OnForceAlert()
        {
            TriggerAlert();
        }

        private void TriggerAlert()
        {
            _patrol.isAlerted = true;
            if (alertIconObject != null) alertIconObject.SetActive(true);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, visionRange);

            // Draw FOV cone
            Vector3 leftRay = Quaternion.Euler(0, 0, visionAngle * 0.5f) * transform.up * visionRange;
            Vector3 rightRay = Quaternion.Euler(0, 0, -visionAngle * 0.5f) * transform.up * visionRange;
            Gizmos.DrawRay(transform.position, leftRay);
            Gizmos.DrawRay(transform.position, rightRay);

            // Draw base range in a different color if weather has scaled it
            if (_baseVisionRange > 0f && !Mathf.Approximately(_baseVisionRange, visionRange))
            {
                Gizmos.color = new Color(1f, 0.8f, 0f, 0.4f);
                Gizmos.DrawWireSphere(transform.position, _baseVisionRange);
            }
        }
    }
}
