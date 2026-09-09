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
        }
    }
}
