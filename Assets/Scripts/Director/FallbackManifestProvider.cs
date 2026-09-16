using System.Collections.Generic;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Provides fallback <see cref="MissionManifest"/> instances when the AI Director
    /// backend is unavailable or does not respond before timeout (Architecture Improvement Plan, Phase 6).
    /// Pure data generation with zero scene dependencies.
    /// </summary>
    public static class FallbackManifestProvider
    {
        /// <summary>
        /// Creates the canonical default Map 2 fallback manifest.
        /// Preserves the exact values previously embedded in <c>WorldManifestApplier.ApplyDefaultFallback()</c>.
        /// </summary>
        public static MissionManifest CreateDefaultFallback(int missionId = 2, string playerArchetype = "GHOST")
        {
            return new MissionManifest
            {
                mission_id = missionId,
                player_archetype = playerArchetype ?? "GHOST",
                target = new TargetManifest
                {
                    location_id = "warehouse_interior",
                    location_label = "Warehouse Interior",
                    armor = "medium",
                    health = 100,
                    coordinates = new float[] { 8f, 2f }
                },
                guards = new GuardsManifest
                {
                    count = 6,
                    vision_range = 6.0f,
                    reaction_speed = 1.0f,
                    hearing_range = 8.0f,
                    armored = false
                },
                environment = new EnvironmentManifest
                {
                    ambient_light = 0.8f,
                    time_of_day = "day",
                    weather = "clear",
                    ambient_label = "Clear",
                    visibility = 1.0f
                },
                routes = new RoutesManifest
                {
                    cars_available = true,
                    blocked_routes = new List<string>(),
                    reinforced_barricades = new List<string>()
                }
            };
        }
    }
}
