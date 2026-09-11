using UnityEngine;
using AdaptiveWorld2D.Player;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Tactical ammo cache pickup trigger (§4.3C — Tactical Pickups & Weapon Caches).
    ///
    /// Placed by WorldManifestApplier.SpawnAmmoCache() in WAREHOUSE_INTERIOR and
    /// INDUSTRIAL_DEPOT sectors to prevent total weapon starvation on Map 2.
    ///
    /// When the Player walks into the trigger collider:
    ///   - Grants +12 pistol ammo (clamped to maxPistolAmmo).
    ///   - If the player doesn't have a pistol yet, also unlocks it.
    ///   - Plays a visual flash effect, then destroys the pickup.
    /// </summary>
    public class AmmoCacheTrigger : MonoBehaviour
    {
        [Header("Ammo Grant")]
        [Tooltip("Amount of pistol ammo granted on pickup.")]
        public int ammoGrant = 12;

        private bool _consumed = false;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_consumed) return;
            if (!other.CompareTag("Player")) return;

            var combat = other.GetComponent<PlayerCombat>();
            if (combat == null) return;

            _consumed = true;

            int before = combat.pistolAmmo;

            if (!combat.hasPistol)
            {
                // Grant pistol + ammo simultaneously
                combat.UnlockPistol(ammoGrant);
                Debug.Log($"[AmmoCacheTrigger] Player found a weapon cache! Pistol unlocked with {ammoGrant} rounds.");
            }
            else
            {
                // Just top up existing ammo
                combat.pistolAmmo = Mathf.Min(combat.pistolAmmo + ammoGrant, combat.maxPistolAmmo);
                int granted = combat.pistolAmmo - before;
                Debug.Log($"[AmmoCacheTrigger] Player picked up ammo cache. +{granted} pistol rounds " +
                          $"({combat.pistolAmmo}/{combat.maxPistolAmmo}).");
            }

            // Flash white and destroy
            StartCoroutine(FlashAndDestroy());
        }

        private System.Collections.IEnumerator FlashAndDestroy()
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                Color original = sr.color;
                sr.color = Color.white;
                yield return new WaitForSeconds(0.08f);
                sr.color = original;
                yield return new WaitForSeconds(0.08f);
                sr.color = Color.white;
                yield return new WaitForSeconds(0.08f);
            }
            Destroy(gameObject);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.1f, 0.9f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(transform.position, new Vector3(0.75f, 0.75f, 0f));
        }
    }
}
