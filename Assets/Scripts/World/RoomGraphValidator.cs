using System.Collections.Generic;
using System.Linq;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Pure room-graph structural validation extracted from
    /// <see cref="SectorRuntimeBuilder"/> (Architecture Improvement Plan, Phase 2).
    ///
    /// Leap 3 update: now dimension-aware.
    ///  - Legacy 3×3 manifests (NW..SE sector IDs) → exact 9-sector + 12-connection check.
    ///  - Dynamic grids (R{r}_C{c} IDs)            → count = cols × rows, valid statuses.
    /// Always validates non-empty spawn/target/extraction designations.
    /// </summary>
    public static class RoomGraphValidator
    {
        /// <summary>The 9 canonical legacy sector ids.</summary>
        public static readonly HashSet<string> LegacySectorIds = new HashSet<string>
        {
            "NW", "NC", "NE", "MW", "MC", "ME", "SW", "SC", "SE"
        };

        public static readonly HashSet<string> ValidConnectionStatuses = new HashSet<string>
        {
            "open", "barricaded_wire_fence", "barricaded_concrete", "walled_off"
        };

        /// <summary>
        /// Validates structural invariants of a room graph.
        /// Returns an empty list when valid.
        /// </summary>
        public static List<string> Validate(RoomGraphManifest rg)
        {
            var errors = new List<string>();

            if (rg == null)
            {
                errors.Add("room_graph is null.");
                return errors;
            }

            // Determine if this is a legacy 3×3 graph or a dynamic grid.
            bool isLegacy = IsLegacyGraph(rg);

            if (isLegacy)
            {
                ValidateLegacy3x3(rg, errors);
            }
            else
            {
                ValidateDynamic(rg, errors);
            }

            // Common: non-empty key sector designations
            if (string.IsNullOrEmpty(rg.target_sector))       errors.Add("target_sector is empty.");
            if (string.IsNullOrEmpty(rg.player_spawn_sector)) errors.Add("player_spawn_sector is empty.");
            if (string.IsNullOrEmpty(rg.extraction_sector))   errors.Add("extraction_sector is empty.");

            return errors;
        }

        // ─────────────────────────────────────────────────────────────────────

        private static bool IsLegacyGraph(RoomGraphManifest rg)
        {
            // A graph is legacy if it has no grid_dimensions metadata AND its
            // sector IDs are drawn from the classic NW..SE set.
            if (rg.grid_dimensions != null)
                return rg.grid_dimensions.columns == 3 && rg.grid_dimensions.rows == 3
                    && rg.sectors != null && rg.sectors.Count > 0
                    && LegacySectorIds.Contains(rg.sectors[0]?.id ?? "");

            if (rg.sectors == null || rg.sectors.Count == 0) return true; // assume legacy for null
            return LegacySectorIds.Contains(rg.sectors[0]?.id ?? "");
        }

        private static void ValidateLegacy3x3(RoomGraphManifest rg, List<string> errors)
        {
            if (rg.sectors == null || rg.sectors.Count != 9)
            {
                errors.Add($"Expected 9 sectors, got {(rg.sectors != null ? rg.sectors.Count : 0)}.");
            }
            else
            {
                var ids = new HashSet<string>(rg.sectors.Select(s => s?.id));
                if (!ids.SetEquals(LegacySectorIds))
                    errors.Add($"Sector id mismatch. Expected [{string.Join(",", LegacySectorIds)}], got [{string.Join(",", ids)}].");
            }

            if (rg.connections == null || rg.connections.Count != 12)
            {
                errors.Add($"Expected 12 connections, got {(rg.connections != null ? rg.connections.Count : 0)}.");
            }
            else
            {
                ValidateConnectionStatuses(rg.connections, errors);
            }
        }

        private static void ValidateDynamic(RoomGraphManifest rg, List<string> errors)
        {
            var dims = rg.GetGridDimensions();
            int expectedSectors = dims.columns * dims.rows;

            if (rg.sectors == null || rg.sectors.Count != expectedSectors)
            {
                errors.Add($"Expected {expectedSectors} sectors for {dims.columns}x{dims.rows} grid, " +
                           $"got {(rg.sectors != null ? rg.sectors.Count : 0)}.");
            }

            // Validate all sector IDs follow the R{r}_C{c} pattern
            if (rg.sectors != null)
            {
                foreach (var s in rg.sectors)
                {
                    if (s == null) { errors.Add("Null sector entry."); continue; }
                    if (!IsValidDynamicId(s.id, dims.rows, dims.columns))
                        errors.Add($"Invalid dynamic sector id '{s.id}' for {dims.columns}x{dims.rows} grid.");
                }
            }

            // Dynamic grid expected connections = horizontal + vertical adjacencies
            int expectedConns = (dims.columns - 1) * dims.rows + dims.columns * (dims.rows - 1);
            if (rg.connections == null || rg.connections.Count != expectedConns)
            {
                errors.Add($"Expected {expectedConns} connections for {dims.columns}x{dims.rows} grid, " +
                           $"got {(rg.connections != null ? rg.connections.Count : 0)}.");
            }
            else
            {
                ValidateConnectionStatuses(rg.connections, errors);
            }
        }

        private static bool IsValidDynamicId(string id, int rows, int cols)
        {
            if (string.IsNullOrEmpty(id)) return false;
            // Accept legacy IDs for 3x3 compatibility
            if (LegacySectorIds.Contains(id)) return true;
            // Pattern: R{r}_C{c}
            if (!id.StartsWith("R")) return false;
            int underscore = id.IndexOf('_');
            if (underscore < 2) return false;
            if (!int.TryParse(id.Substring(1, underscore - 1), out int r)) return false;
            if (!id.Substring(underscore + 1).StartsWith("C")) return false;
            if (!int.TryParse(id.Substring(underscore + 2), out int c)) return false;
            return r >= 0 && r < rows && c >= 0 && c < cols;
        }

        private static void ValidateConnectionStatuses(List<ConnectionConfig> connections, List<string> errors)
        {
            foreach (var conn in connections)
            {
                if (conn == null || !ValidConnectionStatuses.Contains(conn.status))
                    errors.Add($"Connection {conn?.from_sector}->{conn?.to_sector} has invalid status '{conn?.status}'.");
            }
        }
    }
}
