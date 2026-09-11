using System.Collections.Generic;
using UnityEngine;
using AdaptiveWorld2D.Mission;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.AI
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class EnemyPatrol : MonoBehaviour
    {
        [Header("Enemy Identity")]
        public bool isTargetBoss = false;
        public int maxHealth = 60;
        public int currentHealth;
        public float damageResistance = 0.0f; // e.g., 0.5f = 50% damage reduction for armor

        [Header("Movement")]
        public float patrolSpeed = 2.0f;
        public float chaseSpeed = 4.0f;
        public List<Transform> waypoints = new List<Transform>();
        public float waypointTolerance = 0.4f;

        [Header("State")]
        public bool isAlerted = false;
        public bool isDead = false;

        [Header("Obstacle Avoidance")]
        [Tooltip("Distance of the forward feeler raycast used to steer around walls.")]
        public float feelerDistance = 1.2f;
        [Tooltip("Layer mask for objects that block patrol movement (walls, interactables).")]
        public LayerMask obstacleAvoidanceLayer;

        /// <summary>The sector id (e.g. "NW", "MC") this guard belongs to.
        /// Set by WorldManifestApplier so AlertSystem can target the right sector.</summary>
        [HideInInspector] public string sectorId = "";

        private Rigidbody2D _rb;
        private int _currentWaypointIndex = 0;
        private Transform _targetPlayer;
        private EnemyDetection _detection;
        private bool _hasSummonedReinforcements = false;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _detection = GetComponent<EnemyDetection>();
            currentHealth = maxHealth;
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;

            // Default avoidance layer — walls and interactables
            if (obstacleAvoidanceLayer == 0)
                obstacleAvoidanceLayer = LayerMask.GetMask("Building", "Interactable");
        }

        private void Start()
        {
            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) _targetPlayer = playerObj.transform;
        }

        private void FixedUpdate()
        {
            if (isDead) return;

            if (isAlerted && _targetPlayer != null)
            {
                ChasePlayer();
            }
            else
            {
                Patrol();
            }
        }

        private void Patrol()
        {
            if (waypoints.Count == 0) return;

            Transform targetWp = waypoints[_currentWaypointIndex];
            if (targetWp == null) return;

            Vector2 rawDir = ((Vector2)targetWp.position - (Vector2)transform.position).normalized;

            // ── Obstacle Avoidance (§4.3A) ────────────────────────────────────
            // Three-feeler system: forward, forward-left 30°, forward-right 30°.
            // If the forward feeler hits a wall, steer using the perpendicular to
            // the hit normal. Side feelers prevent corner-catching.
            Vector2 steerDir = ApplyObstacleAvoidance(rawDir);
            _rb.linearVelocity = steerDir * patrolSpeed;
            // ─────────────────────────────────────────────────────────────────

            // Rotate towards direction
            if (steerDir.magnitude > 0.1f)
            {
                float angle = Mathf.Atan2(steerDir.y, steerDir.x) * Mathf.Rad2Deg - 90f;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (Vector2.Distance(transform.position, targetWp.position) <= waypointTolerance)
            {
                _currentWaypointIndex = (_currentWaypointIndex + 1) % waypoints.Count;
            }
        }

        /// <summary>
        /// Applies a 3-feeler obstacle avoidance system to the desired patrol direction.
        /// Casts forward, forward-left (30°) and forward-right (30°) feelers. If the
        /// primary feeler hits an obstacle, steers perpendicular to the surface normal.
        /// Side feelers provide early corner-catching detection.
        /// </summary>
        private Vector2 ApplyObstacleAvoidance(Vector2 desiredDir)
        {
            // Forward feeler
            RaycastHit2D hit = Physics2D.Raycast(transform.position, desiredDir, feelerDistance, obstacleAvoidanceLayer);
            if (hit.collider != null)
            {
                // Steer perpendicular to the obstacle surface normal
                Vector2 normal    = hit.normal;
                Vector2 avoidDir  = Vector2.Perpendicular(normal);

                // Choose the perpendicular direction that preserves most of the original intent
                if (Vector2.Dot(avoidDir, desiredDir) < 0f)
                    avoidDir = -avoidDir;

                return Vector2.Lerp(desiredDir, avoidDir, 0.7f).normalized;
            }

            // Side feelers (30° offset) — catches corners before the forward feeler does
            Vector2 leftFeeler  = Quaternion.Euler(0, 0,  30f) * desiredDir;
            Vector2 rightFeeler = Quaternion.Euler(0, 0, -30f) * desiredDir;

            RaycastHit2D leftHit  = Physics2D.Raycast(transform.position, leftFeeler,  feelerDistance * 0.8f, obstacleAvoidanceLayer);
            RaycastHit2D rightHit = Physics2D.Raycast(transform.position, rightFeeler, feelerDistance * 0.8f, obstacleAvoidanceLayer);

            if (leftHit.collider != null && rightHit.collider == null)
                return Vector2.Lerp(desiredDir, rightFeeler, 0.4f).normalized;
            if (rightHit.collider != null && leftHit.collider == null)
                return Vector2.Lerp(desiredDir, leftFeeler, 0.4f).normalized;

            return desiredDir;
        }

        private void ChasePlayer()
        {
            Vector2 rawDir = ((Vector2)_targetPlayer.position - (Vector2)transform.position).normalized;

            // ── Tactical Boss Behaviors (§4.3D) ───────────────────────────────
            // If Viktor's health drops below 40%, he executes a tactical retreat:
            // backs away from the player to create distance and broadcasts a sector
            // alert to summon remaining sector guards as reinforcements.
            if (isTargetBoss && currentHealth < maxHealth * 0.4f)
            {
                rawDir = -rawDir; // Back away toward cover

                if (!_hasSummonedReinforcements)
                {
                    _hasSummonedReinforcements = true;
                    Debug.Log($"[EnemyPatrol] Viktor wounded (<40% HP)! Calling sector '{sectorId}' reinforcements!");
                    if (AdaptiveDirectorClient.latestManifest != null && AdaptiveDirectorClient.latestManifest.room_graph != null)
                    {
                        AlertSystem.BroadcastAlert(transform.position, sectorId, AdaptiveDirectorClient.latestManifest.room_graph);
                    }
                }
            }

            // Steer around obstacles even while chasing to prevent wall-catching (§4.3A)
            Vector2 steerDir = ApplyObstacleAvoidance(rawDir);
            float speed = (isTargetBoss && currentHealth < maxHealth * 0.4f) ? chaseSpeed * 0.85f : chaseSpeed;
            _rb.linearVelocity = steerDir * speed;

            if (steerDir.magnitude > 0.1f)
            {
                float angle = Mathf.Atan2(steerDir.y, steerDir.x) * Mathf.Rad2Deg - 90f;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        public bool TakeDamage(int rawDamage)
        {
            if (isDead) return true;

            int netDamage = Mathf.RoundToInt(rawDamage * (1.0f - damageResistance));
            currentHealth -= Mathf.Max(1, netDamage);

            // Turn around and become alerted if attacked
            isAlerted = true;

            Debug.Log($"[EnemyPatrol] Enemy took {netDamage} damage. HP remaining: {currentHealth}");

            if (currentHealth <= 0)
            {
                Die();
                return true; // lethal
            }
            return false;
        }

        /// <summary>External alert entry point used by AlertSystem.BroadcastSectorAlert().</summary>
        public void Alert()
        {
            if (isDead || isAlerted) return;
            isAlerted = true;
            _detection?.OnForceAlert();
            Debug.Log($"[EnemyPatrol] Guard in sector '{sectorId}' alerted via sector broadcast!");
        }

        private void Die()
        {
            isDead = true;
            _rb.linearVelocity = Vector2.zero;
            GetComponent<Collider2D>().enabled = false;

            // Rotate onto side or play death animation
            transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);

            Debug.Log($"[EnemyPatrol] {(isTargetBoss ? "TARGET BOSS" : "Guard")} eliminated!");

            // ── Phase 4 fix: wire boss death → mission completion ──────────────
            if (isTargetBoss)
            {
                Debug.Log("[EnemyPatrol] Target boss eliminated — notifying MissionManager!");
                MissionManager.Instance?.OnTargetEliminated();
            }
        }
    }
}
