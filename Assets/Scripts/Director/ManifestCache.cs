using System;
using System.IO;
using UnityEngine;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Handles persisting and loading manifest JSON to/from disk cache.
    /// Extracted from <see cref="AdaptiveDirectorClient"/> (Architecture Improvement Plan, Phase 5).
    /// </summary>
    public static class ManifestCache
    {
        public const string DEFAULT_CACHE_PATH = "Temp/AdaptiveWorld_LatestManifest.json";

        /// <summary>
        /// Writes the manifest JSON to disk so it survives domain reloads.
        /// </summary>
        public static void SaveToDisk(string json, string cachePath = DEFAULT_CACHE_PATH)
        {
            try
            {
                string dir = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(cachePath, json);
                Debug.Log($"[AdaptiveDirector] Manifest cached to {cachePath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdaptiveDirector] Could not write manifest cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the raw JSON string from disk cache if present. Returns null if not found or on error.
        /// </summary>
        public static string LoadJsonFromDisk(string cachePath = DEFAULT_CACHE_PATH)
        {
            try
            {
                if (File.Exists(cachePath))
                {
                    return File.ReadAllText(cachePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdaptiveDirector] Failed to read disk cache text: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Loads and deserializes the manifest from disk cache.
        /// Sanitizes null lists immediately. Returns null if not found or corrupt.
        /// </summary>
        public static MissionManifest LoadFromDisk(string cachePath = DEFAULT_CACHE_PATH)
        {
            try
            {
                string json = LoadJsonFromDisk(cachePath);
                if (!string.IsNullOrEmpty(json))
                {
                    MissionManifest manifest = DirectorResponseParser.ParseMissionManifest(json);
                    if (manifest != null)
                    {
                        Debug.Log($"[AdaptiveDirector] Loaded manifest from disk cache (archetype={manifest.player_archetype}).");
                    }
                    return manifest;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdaptiveDirector] Failed to load disk cache: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Deletes the cached manifest file from disk.
        /// </summary>
        public static void ClearDiskCache(string cachePath = DEFAULT_CACHE_PATH)
        {
            try
            {
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                    Debug.Log($"[AdaptiveDirector] Manifest cache cleared at {cachePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AdaptiveDirector] Could not clear manifest cache: {ex.Message}");
            }
        }
    }
}
