using System.Collections.Generic;
using UnityEngine;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Applies route modifications (roadblocks, barricades, and vehicle availability)
    /// from a <see cref="RoutesManifest"/> to the scene (Architecture Improvement Plan, Phase 6).
    /// </summary>
    public static class RouteApplier
    {
        /// <summary>
        /// Activates/deactivates barricades and toggles drivable cars based on the routes manifest.
        /// </summary>
        public static void ApplyRoutes(RoutesManifest routes,
            IList<WorldManifestApplier.NamedBarricade> routeBarricades,
            IList<GameObject> drivableCars)
        {
            if (routes == null) return;

            // Activate / deactivate roadblocks
            if (routeBarricades != null)
            {
                foreach (var item in routeBarricades)
                {
                    if (item.barricadeObject == null) continue;
                    bool isBlocked = routes.blocked_routes != null && routes.blocked_routes.Contains(item.routeId);
                    item.barricadeObject.SetActive(isBlocked);
                }
            }

            // Car availability: disable cars if cars_available is false
            if (drivableCars != null && !routes.cars_available)
            {
                foreach (var car in drivableCars)
                {
                    if (car != null) car.SetActive(false);
                }
            }
        }
    }
}
