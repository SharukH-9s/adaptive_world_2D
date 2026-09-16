using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Coordinates scene-load and mission lifecycle transitions for the Adaptive Director.
    /// Extracted from <see cref="AdaptiveDirectorClient"/> (Architecture Improvement Plan, Phase 5).
    /// </summary>
    public static class DirectorSessionCoordinator
    {
        /// <summary>
        /// Handles scene loading events.
        /// When Map2 is loaded: bootstraps <see cref="WorldManifestApplier"/>.
        /// When a fresh non-Map2 scene (such as Map1) is loaded: clears the active manifest and deletes the disk cache.
        /// </summary>
        public static void HandleSceneLoaded(Scene scene, LoadSceneMode mode, Action clearActiveManifestAction)
        {
            bool isMap2 = !string.IsNullOrEmpty(scene.name) &&
                          scene.name.IndexOf("Map2", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isMap2)
            {
                WorldManifestApplier applier = WorldManifestApplier.SceneInstance;
                if (applier == null)
                {
#if UNITY_2023_1_OR_NEWER
                    applier = UnityEngine.Object.FindAnyObjectByType<WorldManifestApplier>();
#else
                    applier = UnityEngine.Object.FindObjectOfType<WorldManifestApplier>();
#endif
                }

                if (applier != null)
                {
                    applier.BootstrapForMap2(scene);
                }
                else
                {
                    Debug.LogWarning("[AdaptiveDirector] Map2 loaded but no WorldManifestApplier was found.");
                }
                return;
            }

            clearActiveManifestAction?.Invoke();
            ManifestCache.ClearDiskCache();
            Debug.Log("[AdaptiveDirector] Fresh non-Map2 scene loaded; cleared previous Map2 manifest.");
        }
    }
}
