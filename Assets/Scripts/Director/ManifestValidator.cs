using System.Collections.Generic;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Pure manifest sanitization and structural validation extracted from
    /// <see cref="MissionManifest"/> (Architecture Improvement Plan, Phase 2).
    /// </summary>
    public static class ManifestValidator
    {
        /// <summary>
        /// Post-deserialization null-guard. Unity's JsonUtility does not instantiate list
        /// fields that are missing or null in JSON, so any downstream foreach over
        /// guards/props/sectors/connections would NullReferenceException.
        ///
        /// Preserves the original <c>MissionManifest.Sanitize()</c> null semantics exactly:
        /// - a missing room_graph causes an early return — nothing else is mutated;
        /// - an existing room_graph gets non-null sectors and connections lists;
        /// - existing sectors get non-null guards and props lists;
        /// - routes sanitization (blocked_routes / reinforced_barricades) only runs when
        ///   room_graph is also present, matching the original early-return behavior;
        /// - missing target, guards, environment, or routes objects are not invented.
        /// </summary>
        public static void Sanitize(MissionManifest manifest)
        {
            if (manifest == null) return;
            if (manifest.room_graph == null) return; // matches original early-return: routes are not touched

            manifest.room_graph.sectors ??= new List<SectorConfig>();
            manifest.room_graph.connections ??= new List<ConnectionConfig>();
            foreach (var s in manifest.room_graph.sectors)
            {
                if (s == null) continue;
                s.guards ??= new List<GuardEntityConfig>();
                s.props ??= new List<string>();
            }

            if (manifest.routes != null)
            {
                manifest.routes.blocked_routes ??= new List<string>();
                manifest.routes.reinforced_barricades ??= new List<string>();
            }
        }

        /// <summary>
        /// Verifies structural completeness of a manifest. This does NOT change the current
        /// application path: nothing in production code currently rejects a manifest, builds
        /// a fallback, or partially mutates the scene based on this result. An explicit
        /// integration point and fallback policy must be defined and tested before this is
        /// wired into any manifest-application flow.
        ///
        /// Preserves the distinction between:
        /// - no room_graph        -> existing coordinate/fallback behavior;
        /// - empty room_graph     -> existing SectorRuntimeBuilder/RoomGraphValidator behavior;
        /// - valid room_graph     -> existing sector build behavior.
        /// This method intentionally does not treat a missing room_graph as an error.
        /// </summary>
        public static bool Validate(MissionManifest manifest, out List<string> errors)
        {
            errors = new List<string>();

            if (manifest == null)
            {
                errors.Add("manifest is null.");
                return false;
            }

            if (manifest.target == null) errors.Add("target is missing.");
            if (manifest.guards == null) errors.Add("guards is missing.");
            if (manifest.environment == null) errors.Add("environment is missing.");
            if (manifest.routes == null) errors.Add("routes is missing.");

            return errors.Count == 0;
        }
    }
}
