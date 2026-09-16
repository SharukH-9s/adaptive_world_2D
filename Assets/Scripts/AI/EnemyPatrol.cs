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

        // ── Phase 3: Explicit AI State ────────────────────────────────────────
        private EnemyState _state = EnemyState.Patrolling;

        /// <summary>
        /// The current explicit AI state, updated each <c>FixedUpdate</c> tick via
        /// <see cref="EnemyStateAdapter"/>. Mirrors <c>isAlerted</c>, <c>isDead</c>,
        /// the boss-retreat condition, and partial awareness; no behavior is changed.
        /// </summary>
        public EnemyState CurrentState => _state;
        // ─────────────────────────────────────────────────────────────────────

        [Header("Dependencies (Phase 4 — assign in Inspector to skip runtime lookup)")]
        [Tooltip("Assign the Player transform here. When null at Start, falls back to FindGameObjectWithTag(\"Player\").")]
        [SerializeField] private Transform _targetPlayer;
        [Tooltip("Assign the MissionManager here. When null, falls back to MissionManager.Instance.")]
        [SerializeField] private MissionManager _missionManager;

        private Rigidbody2D _rb;
        private int _currentWaypointIndex = 0;
        private EnemyDetection _detection;
        private bool _hasSummonedReinforcements = false;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _detection = GetComponent<EnemyDetection>();
            currentHealth = maxHealth;
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;

            var sr = GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sortingLayerName = "Characters";
                sr.sortingOrder = 25;
            }

            // Default avoidance layer — walls and interactables
            if (obstacleAvoidanceLayer == 0)
                obstacleAvoidanceLayer = LayerMask.GetMask("Building", "Interactable");
        }

        private void Start()
        {
            if (_targetPlayer == null)
            {
                var playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null) _targetPlayer = playerObj.transform;
            }
        }

        [ContextMenu("Eliminate / Kill (Debug)")]
        public void KillForDebug()
        {
            currentHealth = 0;
            if (!isDead) Die();
        }

        private void FixedUpdate()
        {
            if (!isDead && currentHealth <= 0)
            {
                Die();
                return;
            }

            // ── Phase 3: resolve explicit state from existing booleans ────────
            bool isBossRetreatingNow = EnemyDecisionModel.IsBossRetreating(isTargetBoss, currentHealth, maxHealth);
            _state = EnemyStateAdapter.Resolve(
                isDead,
                isAlerted,
                isBossRetreatingNow,
                _detection != null && _detection.IsPartiallyAware,
                _targetPlayer != null);
            // ─────────────────────────────────────────────────────────────────

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
                float angle = EnemyDecisionModel.ComputeLookAngleDegrees(steerDir);
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (Vector2.Distance(transform.position, targetWp.position) <= waypointTolerance)
            {
                _currentWaypointIndex = EnemyDecisionModel.AdvanceWaypointIndex(_currentWaypointIndex, waypoints.Count);
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
            bool isBossRetreating = EnemyDecisionModel.IsBossRetreating(isTargetBoss, currentHealth, maxHealth);
            if (isBossRetreating)
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
            float speed = EnemyDecisionModel.ComputeChaseSpeed(chaseSpeed, isBossRetreating);
            _rb.linearVelocity = steerDir * speed;

            if (steerDir.magnitude > 0.1f)
            {
                float angle = EnemyDecisionModel.ComputeLookAngleDegrees(steerDir);
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        public bool TakeDamage(int rawDamage, Vector2 hitDirection = default)
        {
            if (isDead) return true;

            var damageResult = EnemyDecisionModel.ComputeDamage(rawDamage, damageResistance, currentHealth);
            currentHealth = damageResult.NewHealth;

            // Turn around and become alerted if attacked
            isAlerted = true;

            Debug.Log($"[EnemyPatrol] Enemy took {damageResult.MitigatedDamage} damage. HP remaining: {currentHealth}");

            if (damageResult.IsLethal)
            {
                Die();
                return true; // lethal
            }
            else
            {
                // Non-lethal hit spawns directional impact blood splatter (§5.1)
                Vector2 dir = hitDirection != default ? hitDirection : (Vector2)(-transform.up);
                AdaptiveWorld2D.World.DecalPool.Instance?.SpawnBlood(transform.position, dir);
                return false;
            }
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
            if (sr != null)
            {
                sr.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
                sr.sortingLayerName = "Decals";
                sr.sortingOrder = -1;
            }

            AdaptiveWorld2D.World.DecalPool.Instance?.SpawnBlood(transform.position, Vector2.down);

            Debug.Log($"[EnemyPatrol] {(isTargetBoss ? "TARGET BOSS" : "Guard")} eliminated!");

            // ── Phase 4 fix: wire boss death → mission completion ──────────────
            if (isTargetBoss)
            {
                Debug.Log("[EnemyPatrol] Target boss eliminated — notifying MissionManager!");
                var mm = _missionManager != null ? _missionManager : MissionManager.Instance;
                mm?.OnTargetEliminated();
            }
        }
    }
}
