using UnityEngine;
using AdaptiveWorld2D.Director;
using AdaptiveWorld2D.AI;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Leap 3 interactive prop: explosive barrel hazard.
    ///
    /// Destructible object that explodes upon taking lethal damage or receiving
    /// a high-velocity physical impact. Creates an area-of-effect blast that
    /// damages enemies, destroys destructible walls/gates, and chain-detonates
    /// adjacent hazards.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class ExplosiveHazard : MonoBehaviour
    {
        [Header("Hazard Identity")]
        public string hazardId = "barrel_01";
        public int maxHealth = 60;
        public int currentHealth = 60;

        [Header("Explosion Tuning")]
        public float explosionRadius = 4.5f;
        public int enemyDamage = 180;
        public int wallDamage = 999;

        [Header("State")]
        public bool isExploded = false;

        [Header("Visuals & FX")]
        public Color normalColor = new Color(0.85f, 0.25f, 0.15f);
        public Color criticalColor = new Color(1.0f, 0.5f, 0.1f);
        public ParticleSystem explosionParticles;

        private SpriteRenderer _sr;
        private Collider2D _col;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _col = GetComponent<Collider2D>();
            currentHealth = maxHealth;
            if (_sr != null) _sr.color = normalColor;
        }

        /// <summary>
        /// Applies damage to the hazard. Can be called by weapons, explosions, or melee.
        /// </summary>
        public void Damage(int damage)
        {
            if (isExploded) return;

            currentHealth -= damage;
            if (currentHealth <= maxHealth / 2 && _sr != null)
            {
                _sr.color = criticalColor;
            }

            if (currentHealth <= 0)
            {
                Explode();
            }
        }

        /// <summary>
        /// Alias for Damage() to maintain compatibility with systems expecting DestructibleWall.
        /// </summary>
        public void DamageWall(int damage)
        {
            Damage(damage);
        }

        public void Explode()
        {
            if (isExploded) return;
            isExploded = true;

            if (_col != null) _col.enabled = false;
            if (_sr != null) _sr.enabled = false;

            if (explosionParticles != null)
            {
                explosionParticles.Play();
            }

            // Camera impulse feedback
            if (CameraFollow2D.Instance != null)
            {
                CameraFollow2D.Instance.AddImpulse(0.7f);
            }

            // Scorch decal on ground
            DecalPool.Instance?.SpawnScorch(transform.position, 2.0f);

            Debug.Log($"[ExplosiveHazard] {hazardId} detonated!");

            int enemyLayerMask = LayerMask.GetMask("Enemy");
            int interactableLayerMask = LayerMask.GetMask("Interactable", "Building");

            // Damage enemies in blast radius
            Collider2D[] enemiesInBlast = Physics2D.OverlapCircleAll(transform.position, explosionRadius, enemyLayerMask);
            foreach (var col in enemiesInBlast)
            {
                if (col.gameObject == gameObject) continue;
                var enemy = col.GetComponent<EnemyPatrol>();
                if (enemy != null)
                {
                    Vector2 blastDir = ((Vector2)enemy.transform.position - (Vector2)transform.position).normalized;
                    enemy.TakeDamage(enemyDamage, blastDir);
                }
            }

            // Damage destructible walls/gateways and chain-detonate other explosive hazards
            Collider2D[] propsInBlast = Physics2D.OverlapCircleAll(transform.position, explosionRadius, interactableLayerMask);
            foreach (var col in propsInBlast)
            {
                if (col.gameObject == gameObject) continue;

                var wall = col.GetComponent<DestructibleWall>();
                if (wall != null && !wall.isDestroyed)
                {
                    wall.DamageWall(wallDamage);
                }

                var otherHazard = col.GetComponent<ExplosiveHazard>();
                if (otherHazard != null && !otherHazard.isExploded)
                {
                    otherHazard.Damage(wallDamage);
                }

                var car = col.GetComponent<BurnableCar>();
                if (car != null && !car.isOnFire && !car.isDestroyed)
                {
                    car.Ignite();
                }
            }

            // Alert nearby guards through sound
            Collider2D[] alertRadius = Physics2D.OverlapCircleAll(transform.position, explosionRadius * 2.5f, enemyLayerMask);
            foreach (var col in alertRadius)
            {
                var det = col.GetComponent<EnemyDetection>();
                if (det != null) det.AlertToNoise(transform.position);
            }

            // Report telemetry
            TelemetryReporter.Instance?.ReportDestruction("BARREL_EXPLODE", hazardId);

            Destroy(gameObject, 1.5f);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.gameObject.CompareTag("Vehicle") || collision.relativeVelocity.magnitude > 4.0f)
            {
                Damage(100);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
}
