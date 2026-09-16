using System;
using UnityEngine;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Deserializes and sanitizes responses received from the Director API.
    /// Extracted from <see cref="AdaptiveDirectorClient"/> (Architecture Improvement Plan, Phase 5).
    /// </summary>
    public static class DirectorResponseParser
    {
        /// <summary>
        /// Parses a telemetry batch response JSON into a <see cref="TelemetryResponse"/>.
        /// Returns null on error or empty input.
        /// </summary>
        public static TelemetryResponse ParseTelemetryResponse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonUtility.FromJson<TelemetryResponse>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectorResponseParser] Failed to parse TelemetryResponse: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses a mission manifest JSON, sanitizes null lists using <see cref="ManifestValidator.Sanitize"/>,
        /// and returns the structured <see cref="MissionManifest"/>.
        /// Returns null on error or empty input.
        /// </summary>
        public static MissionManifest ParseMissionManifest(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                MissionManifest manifest = JsonUtility.FromJson<MissionManifest>(json);
                manifest?.Sanitize();
                return manifest;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DirectorResponseParser] Failed to parse MissionManifest: {ex.Message}");
                return null;
            }
        }
    }
}
