using System.Collections.Generic;
using System.Linq;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Pure room-graph structural validation extracted from
    /// <see cref="SectorRuntimeBuilder"/> (Architecture Improvement Plan, Phase 2).
    ///
    /// This reproduces the current Unity validator's contract exactly: it does not add
    /// adjacency, duplicate-edge, endpoint, or reachability checks. Those exist in the
    /// backend validator but are a separate behavior change requiring their own
    /// compatibility decision and tests.
    ///
    /// Unlike the original inline implementation, this validator returns errors rather
    /// than throwing for null lists or null entries.
    /// </summary>
    public static class RoomGraphValidator
    {
        /// <summary>The 9 canonical sector ids. Must stay in sync with
        /// <see cref="SectorRuntimeBuilder.SectorCoords"/>'s keys.</summary>
        public static readonly HashSet<string> ExpectedSectorIds = new HashSet<string>
        {
            "NW", "NC", "NE", "MW", "MC", "ME", "SW", "SC", "SE"
        };

        public static readonly HashSet<string> ValidConnectionStatuses = new HashSet<string>
        {
            "open", "barricaded_wire_fence", "barricaded_concrete", "walled_off"
        };

        /// <summary>
        /// Validates structural invariants of a room graph: exactly 9 sectors with the
        /// expected ids, exactly 12 connections with allowed statuses, and non-empty
        /// target/player-spawn/extraction designations. Returns an empty list when valid.
        /// </summary>
        public static List<string> Validate(RoomGraphManifest rg)
        {
            var errors = new List<string>();

            if (rg == null)
            {
                errors.Add("room_graph is null.");
                return errors;
            }

            if (rg.sectors == null || rg.sectors.Count != 9)
            {
                errors.Add($"Expected 9 sectors, got {(rg.sectors != null ? rg.sectors.Count : 0)}.");
            }
            else
            {
                var ids = new HashSet<string>(rg.sectors.Select(s => s?.id));
                if (!ids.SetEquals(ExpectedSectorIds))
                    errors.Add($"Sector id mismatch. Expected [{string.Join(",", ExpectedSectorIds)}], got [{string.Join(",", ids)}].");
            }

            if (rg.connections == null || rg.connections.Count != 12)
            {
                errors.Add($"Expected 12 connections, got {(rg.connections != null ? rg.connections.Count : 0)}.");
            }
            else
            {
                foreach (var c in rg.connections)
                {
                    if (c == null || !ValidConnectionStatuses.Contains(c.status))
                        errors.Add($"Connection {c?.from_sector}->{c?.to_sector} has invalid status '{c?.status}'.");
                }
            }

            if (string.IsNullOrEmpty(rg.target_sector)) errors.Add("target_sector is empty.");
            if (string.IsNullOrEmpty(rg.player_spawn_sector)) errors.Add("player_spawn_sector is empty.");
            if (string.IsNullOrEmpty(rg.extraction_sector)) errors.Add("extraction_sector is empty.");

            return errors;
        }
    }
}
