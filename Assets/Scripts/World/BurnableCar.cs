using System.Collections;
using UnityEngine;
using AdaptiveWorld2D.Director;
using AdaptiveWorld2D.AI;

namespace AdaptiveWorld2D.World
{
    [RequireComponent(typeof(Collider2D))]
    public class BurnableCar : MonoBehaviour
    {
        [Header("Car Identity")]
        public string carId = "car_01";
        public string carType = "SEDAN";

        [Header("Sprites")]
        public Sprite cleanCarSprite;
        public Sprite burningCarSprite;
        public Sprite wreckedCarSprite;

        [Header("Effects")]
        public ParticleSystem fireParticles;
        public ParticleSystem explosionParticles;
        public float burnDuration = 3.0f;
        public float explosionRadius = 4.0f;
        public int explosionDamage = 150;

        [Header("State")]
        public bool isOnFire = false;
        public bool isDestroyed = false;

        private SpriteRenderer _sr;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            if (cleanCarSprite != null && _sr != null) _sr.sprite = cleanCarSprite;
        }

        public void Ignite()
        {
            if (isOnFire || isDestroyed) return;
            isOnFire = true;

            if (burningCarSprite != null && _sr != null) _sr.sprite = burningCarSprite;
            if (fireParticles != null) fireParticles.Play();

            Debug.Log($"[BurnableCar] Car {carId} is on fire! Exploding soon...");
            StartCoroutine(BurnCountdown());
        }

        private IEnumerator BurnCountdown()
        {
            yield return new WaitForSeconds(burnDuration);
            Explode();
        }

        public void Explode()
        {
            if (isDestroyed) return;
            isDestroyed = true;
            isOnFire = false;

            if (fireParticles != null) fireParticles.Stop();
            if (explosionParticles != null) explosionParticles.Play();
            if (wreckedCarSprite != null && _sr != null) _sr.sprite = wreckedCarSprite;

            // Trigger dynamic camera impulse feedback (§4.2)
            if (CameraFollow2D.Instance != null)
            {
                CameraFollow2D.Instance.AddImpulse(0.8f);
            }

            // Spawn char/scorch decal on ground (§5.1)
            DecalPool.Instance?.SpawnScorch(transform.position, 2.4f);

            Debug.Log($"[BurnableCar] Car {carId} exploded into a fireball!");

            // Phase 4 fix: use targeted layer masks so AOE only hits enemies and
            // destructible interactables — never inadvertently damages the player.
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
                    Vector2 blastDir = (enemy.transform.position - transform.position).normalized;
                    enemy.TakeDamage(explosionDamage, blastDir);
                }
            }

            // Damage destructible walls/props in blast radius
            Collider2D[] propsInBlast = Physics2D.OverlapCircleAll(transform.position, explosionRadius, interactableLayerMask);
            foreach (var col in propsInBlast)
            {
                if (col.gameObject == gameObject) continue;
                var wall = col.GetComponent<DestructibleWall>();
                if (wall != null) wall.DamageWall(explosionDamage);
            }

            // Alert nearby enemies to the explosion noise (hearing check)
            Collider2D[] alertRadius = Physics2D.OverlapCircleAll(transform.position, explosionRadius * 2.5f, enemyLayerMask);
            foreach (var col in alertRadius)
            {
                var det = col.GetComponent<EnemyDetection>();
                if (det != null) det.AlertToNoise(transform.position);
            }

            // Report telemetry
            if (TelemetryReporter.Instance != null)
            {
                TelemetryReporter.Instance.ReportDestruction("CAR_BURN", carId);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
}
