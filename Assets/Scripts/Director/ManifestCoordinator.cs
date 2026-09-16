using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Coordinates the multi-step manifest application pipeline across focused world systems
    /// (Architecture Improvement Plan, Phase 6).
    /// </summary>
    public class ManifestCoordinator
    {
        private readonly MonoBehaviour _coroutineHost;
        private string _appliedManifestKey;

        public List<GameObject> ActiveGuards { get; } = new List<GameObject>();
        public GameObject CurrentTargetInstance { get; set; }
        public GameObject ExtractionZone { get; set; }

        public ManifestCoordinator(MonoBehaviour coroutineHost)
        {
            _coroutineHost = coroutineHost;
        }

        /// <summary>
        /// Generates a hash key to identify duplicate manifest applications.
        /// </summary>
        public static string BuildManifestKey(MissionManifest manifest)
        {
            if (manifest == null) return string.Empty;
            RoomGraphManifest rg = manifest.room_graph;
            return string.Join("|",
                manifest.mission_id,
                manifest.player_archetype ?? string.Empty,
                rg != null ? rg.player_spawn_sector : string.Empty,
                rg != null ? rg.target_sector : string.Empty,
                rg != null ? rg.extraction_sector : string.Empty,
                manifest.target != null ? manifest.target.location_id : string.Empty,
                manifest.guards != null ? manifest.guards.count.ToString() : "0");
        }

        /// <summary>
        /// Executes the full sequential manifest application pipeline:
        /// 1. Sanitize and validate manifest
        /// 2. Apply environment and weather
        /// 3. Build world graph, tactical props, and player spawn
        /// 4. Spawn Viktor and personal bodyguards
        /// 5. Spawn sector patrol guards
        /// 6. Apply routes and roadblocks
        /// 7. Build and monitor extraction zone
        /// 8. Scale guard/boss vision for weather
        /// </summary>
        public bool ApplyManifest(
            MissionManifest manifest,
            ref GameObject targetPrefab,
            ref GameObject guardPrefab,
            ref Component globalLight2D,
            ref ParticleSystem rainParticles,
            ref ParticleSystem fogParticles,
            IList<WorldManifestApplier.LocationAnchor> locationAnchors,
            IList<WorldManifestApplier.NamedBarricade> routeBarricades,
            IList<GameObject> drivableCars,
            IList<Transform> guardSpawnPoints = null)
        {
            if (manifest == null) return false;

            // 1. Sanitize manifest null-guards
            ManifestValidator.Sanitize(manifest);

            string manifestKey = BuildManifestKey(manifest);
            if (!string.IsNullOrEmpty(_appliedManifestKey) && _appliedManifestKey == manifestKey)
            {
                Debug.Log($"[ManifestCoordinator] Ignoring duplicate manifest application ({manifestKey}).");
                return false;
            }
            _appliedManifestKey = manifestKey;

            Debug.Log($"[ManifestCoordinator] Applying Mission {manifest.mission_id} ({manifest.player_archetype}) to scene!");

            RoomGraphManifest rg = manifest.room_graph;

            // 2. Apply Environment & Lighting
            try
            {
                EnvironmentApplier.ApplyEnvironment(manifest.environment, ref globalLight2D, ref rainParticles, ref fogParticles);
            }
            catch (Exception ex) { Debug.LogException(ex); }

            // 3. Build World Graph & Runtime Sectors
            try
            {
                WorldGraphApplier.BuildWorldGraph(rg);
                WorldGraphApplier.ApplyPlayerSpawn(rg);
                WorldGraphApplier.ApplyTacticalProps(manifest.routes, rg);
            }
            catch (Exception ex) { Debug.LogException(ex); }

            // 4. Spawn Target Viktor & Bodyguards
            try
            {
                GameObject targetInst = CurrentTargetInstance;
                CurrentTargetInstance = TargetSpawner.SpawnTarget(
                    manifest.target,
                    rg,
                    ref targetPrefab,
                    ref targetInst,
                    locationAnchors,
                    ref guardPrefab,
                    ActiveGuards);
            }
            catch (Exception ex) { Debug.LogException(ex); }

            // 5. Spawn Guards (patrol & sentry across sectors)
            try
            {
                GuardSpawner.SpawnGuards(
                    manifest.guards,
                    rg,
                    ref guardPrefab,
                    guardSpawnPoints,
                    ActiveGuards);
            }
            catch (Exception ex) { Debug.LogException(ex); }

            // 6. Apply Routes & Barricades
            try
            {
                RouteApplier.ApplyRoutes(manifest.routes, routeBarricades, drivableCars);
            }
            catch (Exception ex) { Debug.LogException(ex); }

            // 7. Create Extraction Zone
            try
            {
                GameObject currentZone = ExtractionZone;
                ExtractionZone = ExtractionZoneBuilder.BuildExtractionZone(rg, _coroutineHost, ref currentZone);
            }
            catch (Exception ex) { Debug.LogException(ex); }

            // 8. Scale Guard & Boss Vision for Weather
            try
            {
                if (manifest.environment != null)
                {
                    EnvironmentApplier.ScaleGuardVisionForWeather(
                        manifest.environment.visibility,
                        ActiveGuards,
                        CurrentTargetInstance);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); }

            return true;
        }
    }
}
