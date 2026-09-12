using UnityEngine;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.World
{
    [RequireComponent(typeof(Collider2D))]
    public class DestructibleWall : MonoBehaviour
    {
        [Header("Wall Identification")]
        public string wallId = "wall_north_01";
        public int maxHealth = 100;
        public int currentHealth = 100;

        [Header("Destruction Visuals")]
        public Sprite intactSprite;
        public Sprite damagedSprite;
        public Sprite rubbleSprite;
        public ParticleSystem debrisParticles;

        [Header("State")]
        public bool isDestroyed = false;

        private SpriteRenderer _sr;
        private Collider2D _col;

        private void Awake()
        {
            _sr = GetComponent<SpriteRenderer>();
            _col = GetComponent<Collider2D>();
            currentHealth = maxHealth;
            if (intactSprite != null && _sr != null) _sr.sprite = intactSprite;
        }

        public void DamageWall(int damage)
        {
            if (isDestroyed) return;

            currentHealth -= damage;
            if (currentHealth <= maxHealth / 2 && damagedSprite != null && _sr != null)
            {
                _sr.sprite = damagedSprite;
            }

            if (currentHealth <= 0)
            {
                DestroyWall();
            }
        }

        public void DestroyWall()
        {
            if (isDestroyed) return;
            isDestroyed = true;

            // Turn off blocking collider so player/car can walk through
            if (_col != null) _col.enabled = false;

            // Rubble sprite
            if (rubbleSprite != null && _sr != null)
            {
                _sr.sprite = rubbleSprite;
                _sr.sortingLayerName = "Decals";
                _sr.sortingOrder = -1; // Flatten behind player
                DecalPool.Instance?.RegisterDecal(gameObject);
            }
            else if (_sr != null)
            {
                _sr.enabled = false;
            }

            if (debrisParticles != null)
            {
                debrisParticles.Play();
            }

            // Trigger dynamic camera impulse feedback (§4.2)
            if (CameraFollow2D.Instance != null)
            {
                CameraFollow2D.Instance.AddImpulse(0.35f);
            }

            Debug.Log($"[DestructibleWall] Wall {wallId} destroyed!");

            // Report telemetry
            if (TelemetryReporter.Instance != null)
            {
                TelemetryReporter.Instance.ReportDestruction("WALL_BREAK", wallId);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            // If hit by high-speed vehicle, break immediately
            if (collision.gameObject.CompareTag("Vehicle") || collision.relativeVelocity.magnitude > 6.0f)
            {
                DamageWall(100);
            }
        }
    }
}
