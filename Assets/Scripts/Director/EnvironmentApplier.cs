using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdaptiveWorld2D.World;
using AdaptiveWorld2D.AI;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Applies environmental effects (lighting, camera tint, atmospheric overlays,
    /// particle systems, streetlight modulation, and guard vision scaling) from an
    /// <see cref="EnvironmentManifest"/> (Architecture Improvement Plan, Phase 6).
    /// </summary>
    public static class EnvironmentApplier
    {
        public static void ApplyEnvironment(
            EnvironmentManifest env,
            ref Component globalLight2D,
            ref ParticleSystem rainParticles,
            ref ParticleSystem fogParticles)
        {
            if (env == null) return;

            // 1. Camera Background Tint based on Time of Day
            if (Camera.main != null && !string.IsNullOrEmpty(env.time_of_day))
            {
                if (env.time_of_day.IndexOf("night", StringComparison.OrdinalIgnoreCase) >= 0)
                    Camera.main.backgroundColor = new Color(0.04f, 0.06f, 0.12f); // Deep Midnight Blue
                else if (env.time_of_day.IndexOf("dusk", StringComparison.OrdinalIgnoreCase) >= 0)
                    Camera.main.backgroundColor = new Color(0.24f, 0.12f, 0.08f); // Sunset Amber
                else
                    Camera.main.backgroundColor = new Color(0.12f, 0.13f, 0.15f); // Crisp Asphalt Gray
            }

            // 2. Global Light 2D adjustment (via reflection — works with or without URP installed)
            if (globalLight2D == null)
            {
                var light2DType = System.Type.GetType("UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
                if (light2DType != null)
                {
#if UNITY_2023_1_OR_NEWER
                    globalLight2D = (Component)UnityEngine.Object.FindAnyObjectByType(light2DType);
#else
                    globalLight2D = (Component)UnityEngine.Object.FindObjectOfType(light2DType);
#endif
                }
            }
            if (globalLight2D != null)
            {
                var light2DType = globalLight2D.GetType();
                float intensity = Mathf.Clamp(env.ambient_light, 0.15f, 1.2f);
                Color col = (env.time_of_day != null && env.time_of_day.IndexOf("night", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? new Color(0.2f, 0.25f, 0.45f)
                    : (env.time_of_day != null && env.time_of_day.IndexOf("dusk", StringComparison.OrdinalIgnoreCase) >= 0)
                        ? new Color(0.85f, 0.55f, 0.35f)
                        : Color.white;
                try { light2DType.GetProperty("intensity")?.SetValue(globalLight2D, intensity); } catch { }
                try { light2DType.GetProperty("color")?.SetValue(globalLight2D, col); } catch { }
            }

            // 3. Screen-Space Atmospheric Color Filter (instant dramatic lighting)
            EnsureAtmosphereOverlay(env);

            // 4. Rain Particles
            if (env.weather != null && env.weather.IndexOf("rain", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                EnsureRainSystem(ref rainParticles);
            }
            else if (rainParticles != null && rainParticles.isPlaying)
            {
                rainParticles.Stop();
            }

            // 5. Fog Particles
            if (env.weather != null && env.weather.IndexOf("fog", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                EnsureFogSystem(ref fogParticles);
            }
            else if (fogParticles != null && fogParticles.isPlaying)
            {
                fogParticles.Stop();
            }

            // 6. Modulate streetlights based on ambient light & time of day
#if UNITY_2023_1_OR_NEWER
            StreetlightController[] streetlights = UnityEngine.Object.FindObjectsByType<StreetlightController>();
#else
            StreetlightController[] streetlights = UnityEngine.Object.FindObjectsOfType<StreetlightController>();
#endif
            if (streetlights != null)
            {
                foreach (var sl in streetlights)
                {
                    if (sl != null)
                        sl.SetEnvironmentLighting(env.ambient_light, env.time_of_day);
                }
            }

            Debug.Log($"[EnvironmentApplier] Environment Applied: {env.time_of_day?.ToUpper()} | Weather: {env.weather?.ToUpper()} | Visibility: {env.visibility:F2}");
        }

        public static void ScaleGuardVisionForWeather(float visibility, IEnumerable<GameObject> activeGuards, GameObject currentTargetInstance = null)
        {
            if (Mathf.Approximately(visibility, 1.0f)) return; // No scaling needed in clear conditions

            int scaled = 0;
            if (activeGuards != null)
            {
                foreach (var guardObj in activeGuards)
                {
                    if (guardObj == null) continue;
                    var det = guardObj.GetComponent<EnemyDetection>();
                    if (det != null)
                    {
                        det.SetWeatherVisibility(visibility);
                        scaled++;
                    }
                }
            }

            // Also scale the boss
            if (currentTargetInstance != null)
            {
                var det = currentTargetInstance.GetComponent<EnemyDetection>();
                if (det != null)
                {
                    det.SetWeatherVisibility(visibility);
                    scaled++;
                }
            }

            Debug.Log($"[EnvironmentApplier] Weather vision scaling: {scaled} entity/entities scaled to {visibility:P0} visibility.");
        }

        private static void EnsureAtmosphereOverlay(EnvironmentManifest env)
        {
#if UNITY_2023_1_OR_NEWER
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
#else
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
#endif
            if (canvas == null) return;

            Transform existing = canvas.transform.Find("AtmosphereOverlay");
            GameObject overlayGO = existing != null ? existing.gameObject : new GameObject("AtmosphereOverlay");
            if (existing == null)
            {
                overlayGO.transform.SetParent(canvas.transform, false);
                overlayGO.transform.SetAsFirstSibling(); // render behind HUD text
                var rect = overlayGO.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var img = overlayGO.GetComponent<Image>() ?? overlayGO.AddComponent<Image>();
            img.raycastTarget = false; // do not block clicks

            if (env.weather != null && env.weather.IndexOf("fog", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                img.color = new Color(0.80f, 0.85f, 0.90f, 0.28f); // Soft misty fog veil
            }
            else if ((env.weather != null && env.weather.IndexOf("rain", StringComparison.OrdinalIgnoreCase) >= 0) ||
                     (env.time_of_day != null && env.time_of_day.IndexOf("night", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                img.color = new Color(0.04f, 0.08f, 0.22f, 0.35f); // Midnight blue atmospheric shadow
            }
            else if (env.time_of_day != null && env.time_of_day.IndexOf("dusk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                img.color = new Color(0.85f, 0.40f, 0.12f, 0.20f); // Warm twilight amber
            }
            else
            {
                img.color = Color.clear;
            }
        }

        private static void EnsureRainSystem(ref ParticleSystem rainParticles)
        {
            if (rainParticles == null)
            {
                var existing = GameObject.Find("Weather_Rain");
                if (existing != null)
                {
                    rainParticles = existing.GetComponent<ParticleSystem>();
                }
                else if (Camera.main != null)
                {
                    GameObject rainGO = new GameObject("Weather_Rain");
                    rainGO.transform.SetParent(Camera.main.transform, false);
                    rainGO.transform.localPosition = new Vector3(0, 7f, 10f);

                    rainParticles = rainGO.AddComponent<ParticleSystem>();
                    var main = rainParticles.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startSpeed = 16f;
                    main.startLifetime = 1.0f;
                    main.startSize = 0.08f;
                    main.startColor = new Color(0.7f, 0.85f, 1.0f, 0.7f);
                    main.maxParticles = 600;

                    var emission = rainParticles.emission;
                    emission.rateOverTime = 300f;

                    var shape = rainParticles.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(26f, 16f, 1f);

                    var rend = rainGO.GetComponent<ParticleSystemRenderer>();
                    rend.renderMode = ParticleSystemRenderMode.Stretch;
                    rend.lengthScale = 3.5f;
                    rend.velocityScale = 0.02f;
                    rend.sortingLayerName = "Atmosphere";
                    rend.sortingOrder = 60;
                }
            }

            if (rainParticles != null && !rainParticles.isPlaying)
            {
                rainParticles.Play();
            }
        }

        private static void EnsureFogSystem(ref ParticleSystem fogParticles)
        {
            if (fogParticles == null)
            {
                var existing = GameObject.Find("Weather_Fog");
                if (existing != null)
                {
                    fogParticles = existing.GetComponent<ParticleSystem>();
                }
                else if (Camera.main != null)
                {
                    GameObject fogGO = new GameObject("Weather_Fog");
                    fogGO.transform.SetParent(Camera.main.transform, false);
                    fogGO.transform.localPosition = new Vector3(0, 0, 10f);

                    fogParticles = fogGO.AddComponent<ParticleSystem>();
                    var main = fogParticles.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startSpeed = 0.8f;
                    main.startLifetime = 4.0f;
                    main.startSize = 3.5f;
                    main.startColor = new Color(0.85f, 0.9f, 0.95f, 0.18f);
                    main.maxParticles = 80;

                    var emission = fogParticles.emission;
                    emission.rateOverTime = 20f;

                    var shape = fogParticles.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(25f, 18f, 1f);

                    var rend = fogGO.GetComponent<ParticleSystemRenderer>();
                    rend.sortingLayerName = "Atmosphere";
                    rend.sortingOrder = 55;
                }
            }

            if (fogParticles != null && !fogParticles.isPlaying)
            {
                fogParticles.Play();
            }
        }
    }
}
