using System.Collections.Generic;
using UnityEngine;

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

        private Rigidbody2D _rb;
        private int _currentWaypointIndex = 0;
        private Transform _targetPlayer;
        private EnemyDetection _detection;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _detection = GetComponent<EnemyDetection>();
            currentHealth = maxHealth;
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
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

            Vector2 dir = ((Vector2)targetWp.position - (Vector2)transform.position).normalized;
            _rb.linearVelocity = dir * patrolSpeed;

            // Rotate towards direction
            if (dir.magnitude > 0.1f)
            {
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            if (Vector2.Distance(transform.position, targetWp.position) <= waypointTolerance)
            {
                _currentWaypointIndex = (_currentWaypointIndex + 1) % waypoints.Count;
            }
        }

        private void ChasePlayer()
        {
            Vector2 dir = ((Vector2)_targetPlayer.position - (Vector2)transform.position).normalized;
            _rb.linearVelocity = dir * chaseSpeed;

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
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
        }
    }
}
