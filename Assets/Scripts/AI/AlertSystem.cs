using System.Collections.Generic;
using UnityEngine;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.AI
{
    /// <summary>
    /// Gateway Alert Cascade System (§4.3B).
    ///
    /// When an alarm or loud explosion occurs in a sector, this system propagates
    /// the alert through all <c>"open"</c> gateway connections to neighboring sectors,
    /// causing guards in those sectors to become alerted too.
    ///
    /// Only <c>"open"</c> connections transmit alerts — barricaded and walled connections
    /// dampen the sound, preventing alerts from bleeding through physical barriers.
    ///
    /// Usage (call from PlayerCombat.FireWeapon, BurnableCar.Explode, etc.):
    /// <code>
    ///   AlertSystem.BroadcastAlert(transform.position, sectorId, manifest.room_graph);
    /// </code>
    /// </summary>
    public static class AlertSystem
    {
        /// <summary>
        /// Broadcasts an alert originating from <paramref name="originSectorId"/>.
        /// Finds all neighboring sectors connected via open gateways and alerts
        /// every <see cref="EnemyPatrol"/> guard whose <c>sectorId</c> matches.
        ///
        /// Also performs a world-space hearing check using <paramref name="soundOrigin"/>
        /// so guards on the fringe of a sector boundary still react if they are close enough.
        /// </summary>
        /// <param name="soundOrigin">World position of the sound/explosion source.</param>
        /// <param name="originSectorId">The sector id (e.g. "NW") where the event occurred.</param>
        /// <param name="roomGraph">The live room graph from the manifest.</param>
        /// <param name="alertNeighbors">If true, propagates alert to open neighboring sectors (default true).</param>
        public static void BroadcastAlert(Vector2 soundOrigin, string originSectorId,
                                          RoomGraphManifest roomGraph, bool alertNeighbors = true)
        {
            if (string.IsNullOrEmpty(originSectorId)) return;

            // Alert guards in the origin sector first
            AlertGuardsInSector(originSectorId, soundOrigin, hearingCheck: false);

            if (!alertNeighbors || roomGraph == null || roomGraph.connections == null) return;

            // Collect neighboring sectors reachable through "open" connections
            HashSet<string> neighborIds = GetOpenNeighbors(originSectorId, roomGraph);

            foreach (string neighborId in neighborIds)
            {
                Debug.Log($"[AlertSystem] Alert cascade: {originSectorId} → {neighborId} (open gateway)");
                AlertGuardsInSector(neighborId, soundOrigin, hearingCheck: true);
            }
        }

        /// <summary>
        /// Overload that resolves the origin sector automatically from a world position
        /// by comparing against sector centers in the room graph.
        /// </summary>
        public static void BroadcastAlert(Vector2 soundOrigin, RoomGraphManifest roomGraph,
                                          float sectorResolutionRadius = 12f)
        {
            if (roomGraph?.sectors == null) return;

            string nearest = null;
            float bestDist = float.MaxValue;
            foreach (var sector in roomGraph.sectors)
            {
                float d = Vector2.Distance(soundOrigin, new Vector2(sector.center_x, sector.center_y));
                if (d < bestDist) { bestDist = d; nearest = sector.id; }
            }

            if (nearest != null && bestDist <= sectorResolutionRadius)
                BroadcastAlert(soundOrigin, nearest, roomGraph);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Private Helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Returns all sector ids reachable from <paramref name="sectorId"/> via open connections.</summary>
        private static HashSet<string> GetOpenNeighbors(string sectorId, RoomGraphManifest roomGraph)
        {
            var neighbors = new HashSet<string>();
            foreach (var conn in roomGraph.connections)
            {
                if (conn.status != "open") continue;

                if (conn.from_sector == sectorId)
                    neighbors.Add(conn.to_sector);
                else if (conn.to_sector == sectorId)
                    neighbors.Add(conn.from_sector);
            }
            return neighbors;
        }

        /// <summary>
        /// Finds all <see cref="EnemyPatrol"/> components tagged "Enemy" whose
        /// <c>sectorId</c> matches <paramref name="targetSectorId"/> and calls
        /// <see cref="EnemyPatrol.Alert()"/> on them.
        ///
        /// If <paramref name="hearingCheck"/> is true, only guards within their
        /// own <see cref="EnemyDetection.hearingRange"/> of <paramref name="soundOrigin"/>
        /// are alerted (simulates sound attenuation through a gateway).
        /// </summary>
        private static void AlertGuardsInSector(string targetSectorId, Vector2 soundOrigin, bool hearingCheck)
        {
#if UNITY_2023_1_OR_NEWER
            EnemyPatrol[] allGuards = Object.FindObjectsByType<EnemyPatrol>();
#else
            EnemyPatrol[] allGuards = Object.FindObjectsOfType<EnemyPatrol>();
#endif
            int alerted = 0;

            foreach (var guard in allGuards)
            {
                if (guard.sectorId != targetSectorId) continue;
                if (guard.isDead || guard.isAlerted) continue;

                if (hearingCheck)
                {
                    var det = guard.GetComponent<EnemyDetection>();
                    if (det != null)
                    {
                        float dist = Vector2.Distance(soundOrigin, (Vector2)guard.transform.position);
                        // Allow a generous 1.5× hearing range for cross-sector propagation
                        if (dist > det.hearingRange * 1.5f) continue;
                    }
                }

                guard.Alert();
                alerted++;
            }

            if (alerted > 0)
                Debug.Log($"[AlertSystem] Alerted {alerted} guard(s) in sector '{targetSectorId}'.");
        }
    }
}
