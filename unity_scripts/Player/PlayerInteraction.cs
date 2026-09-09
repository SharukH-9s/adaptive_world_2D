using System;
using UnityEngine;
using AdaptiveWorld2D.World;
using AdaptiveWorld2D.Mission;

namespace AdaptiveWorld2D.Player
{
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteraction : MonoBehaviour
    {
        [Header("Interaction Settings")]
        public float interactRadius = 2.0f;
        public LayerMask interactableLayer;

        private PlayerController _controller;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            // E key: Enter/Exit vehicle or Buy Gun / Trigger interactable
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (_controller.isDriving)
                {
                    _controller.ExitVehicle();
                }
                else
                {
                    TryInteract();
                }
            }

            // F key: Ignite/Burn car or breach wall
            if (Input.GetKeyDown(KeyCode.F))
            {
                TryDestructiveAction();
            }
        }

        private void TryInteract()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, interactRadius, interactableLayer);
            foreach (var col in hits)
            {
                // Check for Drivable / Burnable Car
                var car = col.GetComponent<BurnableCar>();
                if (car != null && !car.isOnFire && !car.isDestroyed)
                {
                    _controller.EnterVehicle(car.gameObject);
                    return;
                }

                // Check for Mission Zone / Gun Store
                var zone = col.GetComponent<MissionZoneTrigger>();
                if (zone != null)
                {
                    zone.TriggerZone(gameObject);
                    return;
                }
            }
        }

        private void TryDestructiveAction()
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, interactRadius, interactableLayer);
            foreach (var col in hits)
            {
                // Burn car with F key
                var car = col.GetComponent<BurnableCar>();
                if (car != null && !car.isOnFire && !car.isDestroyed)
                {
                    car.Ignite();
                    Debug.Log("[PlayerInteraction] Ignited vehicle!");
                    return;
                }

                // Break wall with F key
                var wall = col.GetComponent<DestructibleWall>();
                if (wall != null && !wall.isDestroyed)
                {
                    wall.DamageWall(50);
                    Debug.Log("[PlayerInteraction] Damaged destructible wall!");
                    return;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
