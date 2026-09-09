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

            Debug.Log($"[BurnableCar] Car {carId} exploded into a fireball!");

            // AOE Damage to nearby entities and walls
            Collider2D[] inBlast = Physics2D.OverlapCircleAll(transform.position, explosionRadius);
            foreach (var col in inBlast)
            {
                if (col.gameObject == gameObject) continue;

                var wall = col.GetComponent<DestructibleWall>();
                if (wall != null) wall.DamageWall(explosionDamage);

                var enemy = col.GetComponent<EnemyPatrol>();
                if (enemy != null) enemy.TakeDamage(explosionDamage);
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
