using System;
using UnityEngine;
using AdaptiveWorld2D.Director;
using AdaptiveWorld2D.AI;

namespace AdaptiveWorld2D.Player
{
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Weapons Inventory")]
        public bool hasPistol = false;
        public int pistolAmmo = 12;
        public int maxPistolAmmo = 36;
        public Sprite armedSprite;

        [Header("Attack Stats")]
        public float meleeRange = 1.5f;
        public int meleeDamage = 40;
        public float gunRange = 15f;
        public int gunDamage = 35;
        public float fireRate = 0.25f;

        [Header("Effects & Layers")]
        public LayerMask enemyLayer;
        public GameObject bulletPrefab;
        public Transform firePoint;

        private float _lastFireTime = 0f;

        private void Start()
        {
            // Auto-display armed gun sprite if player has pistol
            if (hasPistol && armedSprite != null)
            {
                SpriteRenderer sr = GetComponent<SpriteRenderer>();
                if (sr != null) sr.sprite = armedSprite;
            }
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0)) // Left click to attack
            {
                if (hasPistol && pistolAmmo > 0)
                {
                    ShootPistol();
                }
                else
                {
                    PerformMeleeAttack();
                }
            }
        }

        public void UnlockPistol(int ammo = 24)
        {
            hasPistol = true;
            pistolAmmo = Mathf.Min(pistolAmmo + ammo, maxPistolAmmo);
            if (armedSprite != null)
            {
                SpriteRenderer sr = GetComponent<SpriteRenderer>();
                if (sr != null) sr.sprite = armedSprite;
            }
            Debug.Log("[PlayerCombat] Pistol acquired and loaded!");
        }

        private void ShootPistol()
        {
            if (Time.time - _lastFireTime < fireRate) return;
            _lastFireTime = Time.time;
            pistolAmmo--;

            Vector2 origin = firePoint != null ? (Vector2)firePoint.position : (Vector2)transform.position;
            Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 dir = (mouseWorldPos - origin).normalized;

            // Alert nearby guards through noise
            AlertNearbyEnemies(origin, 14.0f);

            // Raycast shot
            RaycastHit2D hit = Physics2D.Raycast(origin, dir, gunRange, enemyLayer);
            Vector2 endPoint = hit.collider != null ? hit.point : (origin + dir * gunRange);
            StartCoroutine(DrawBulletTracer(origin, endPoint));
            if (hit.collider != null)
            {
                var enemy = hit.collider.GetComponent<EnemyPatrol>();
                if (enemy != null)
                {
                    bool wasLethal = enemy.TakeDamage(gunDamage);
                    float dist = Vector2.Distance(origin, hit.point);

                    if (TelemetryReporter.Instance != null)
                    {
                        TelemetryReporter.Instance.ReportCombat(
                            "DIRECT_ASSAULT",
                            "pistol",
                            enemy.isTargetBoss ? "target" : "guard",
                            wasLethal,
                            dist
                        );
                    }
                }
            }

            Debug.Log($"[PlayerCombat] Pistol fired! Ammo left: {pistolAmmo}");
        }

        private void PerformMeleeAttack()
        {
            Vector2 origin = transform.position;
            Collider2D[] hits = Physics2D.OverlapCircleAll(origin, meleeRange, enemyLayer);

            foreach (var col in hits)
            {
                var enemy = col.GetComponent<EnemyPatrol>();
                if (enemy == null) continue;

                // Check if attack is from behind (stealth takedown)
                Vector2 toPlayer = (transform.position - enemy.transform.position).normalized;
                float dot = Vector2.Dot(enemy.transform.up, toPlayer);
                bool isStealth = dot < -0.2f && !enemy.isAlerted;

                int damage = isStealth ? 999 : meleeDamage;
                bool wasLethal = enemy.TakeDamage(damage);
                float dist = Vector2.Distance(origin, enemy.transform.position);

                string style = isStealth ? "STEALTH" : "DIRECT_ASSAULT";
                string weaponName = isStealth ? "knife" : "fist";

                if (TelemetryReporter.Instance != null)
                {
                    TelemetryReporter.Instance.ReportCombat(
                        style,
                        weaponName,
                        enemy.isTargetBoss ? "target" : "guard",
                        wasLethal,
                        dist
                    );
                }

                Debug.Log($"[PlayerCombat] Melee hit: {style} attack on enemy!");
                break; // Hit one target per swing
            }
        }

        private void AlertNearbyEnemies(Vector2 soundOrigin, float radius)
        {
            Collider2D[] guards = Physics2D.OverlapCircleAll(soundOrigin, radius, enemyLayer);
            foreach (var g in guards)
            {
                var detection = g.GetComponent<EnemyDetection>();
                if (detection != null)
                {
                    detection.AlertToNoise(soundOrigin);
                }
            }
        }

        private System.Collections.IEnumerator DrawBulletTracer(Vector2 start, Vector2 end)
        {
            GameObject tracerObj = new GameObject("BulletTracer");
            LineRenderer lr = tracerObj.AddComponent<LineRenderer>();
            lr.startWidth = 0.06f;
            lr.endWidth = 0.02f;
            lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(start.x, start.y, 0f));
            lr.SetPosition(1, new Vector3(end.x, end.y, 0f));
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = new Color(1f, 0.9f, 0.3f, 0.9f);
            lr.endColor = new Color(1f, 0.4f, 0.1f, 0.4f);
            lr.sortingOrder = 10;

            yield return new WaitForSeconds(0.06f);
            Destroy(tracerObj);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, meleeRange);
        }
    }
}
