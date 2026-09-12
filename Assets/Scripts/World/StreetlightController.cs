using UnityEngine;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Dynamic Streetlight Controller (§5.3A — Dynamic Streetlight Behaviors).
    ///
    /// Attached to streetlight pole GameObjects in Map 2 sectors.
    /// Provides distinct lighting profiles depending on the sector theme:
    ///   - <see cref="LightProfile.SlumFlicker"/>: Intermittent flickering amber lamps
    ///     driven by Perlin noise and random micro-dips (for slum alleys).
    ///   - <see cref="LightProfile.HighwaySodium"/>: Warm amber sodium-vapor glow (for highways).
    ///   - <see cref="LightProfile.IndustrialFlood"/>: Steady, high-radius cool floodlights (for plazas, depots).
    ///
    /// Works with Universal Render Pipeline 2D Light2D if available, and also modulates
    /// its SpriteRenderer glow indicator for universal compatibility.
    /// </summary>
    public class StreetlightController : MonoBehaviour
    {
        public enum LightProfile
        {
            HighwaySodium,
            SlumFlicker,
            IndustrialFlood
        }

        [Header("Profile Configuration")]
        public LightProfile profile = LightProfile.HighwaySodium;
        public float baseIntensity = 1.0f;
        public float lightRadius = 6.0f;

        private float _noiseSeed;
        private float _currentIntensity;
        private Component _light2DComp;
        private System.Reflection.PropertyInfo _intensityProp;
        private SpriteRenderer _sr;
        private Color _baseColor;

        private void Awake()
        {
            _noiseSeed = Random.Range(0f, 1000f);
            _sr = GetComponent<SpriteRenderer>();

            ApplyProfileColors();
            TryFindLight2D();
        }

        private void ApplyProfileColors()
        {
            switch (profile)
            {
                case LightProfile.SlumFlicker:
                    _baseColor = new Color(1f, 0.65f, 0.2f, 0.9f); // Dim buzzing amber
                    baseIntensity = 0.85f;
                    lightRadius = 4.5f;
                    break;
                case LightProfile.HighwaySodium:
                    _baseColor = new Color(1.0f, 0.80f, 0.40f, 1f); // Warm amber (#FFCC66)
                    baseIntensity = 1.2f;
                    lightRadius = 6.0f;
                    break;
                case LightProfile.IndustrialFlood:
                    _baseColor = new Color(0.75f, 0.88f, 1.0f, 1f); // Cool blue-white for plazas & compounds
                    baseIntensity = 1.3f;
                    lightRadius = 6.5f;
                    break;
            }

            if (_sr != null)
                _sr.color = _baseColor;

            if (_light2DComp != null)
            {
                var colorProp = _light2DComp.GetType().GetProperty("color");
                colorProp?.SetValue(_light2DComp, _baseColor);
                var radiusProp = _light2DComp.GetType().GetProperty("pointLightOuterRadius");
                radiusProp?.SetValue(_light2DComp, lightRadius);
            }
        }

        private void TryFindLight2D()
        {
            var lightType = System.Type.GetType("UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (lightType != null)
            {
                _light2DComp = GetComponent(lightType);
                if (_light2DComp == null)
                {
                    _light2DComp = gameObject.AddComponent(lightType);
                    var colorProp = lightType.GetProperty("color");
                    var radiusProp = lightType.GetProperty("pointLightOuterRadius");

                    colorProp?.SetValue(_light2DComp, _baseColor);
                    radiusProp?.SetValue(_light2DComp, lightRadius);
                }
                _intensityProp = lightType.GetProperty("intensity");
            }
        }

        private void Update()
        {
            if (profile == LightProfile.SlumFlicker)
            {
                // Perlin noise flicker (fast frequency + subtle amplitude)
                float noise = Mathf.PerlinNoise(Time.time * 9f, _noiseSeed);
                _currentIntensity = baseIntensity * (0.78f + noise * 0.44f);

                // Occasional micro-dip (buzzing fluorescent / loose wiring)
                if (Random.value < 0.015f)
                {
                    _currentIntensity *= Random.Range(0.2f, 0.5f);
                }
            }
            else
            {
                // Steady with extremely subtle breathing
                _currentIntensity = baseIntensity * (0.98f + Mathf.Sin(Time.time * 1.5f + _noiseSeed) * 0.02f);
            }

            // Apply to Light2D if present
            _intensityProp?.SetValue(_light2DComp, _currentIntensity);

            // Modulate SpriteRenderer alpha slightly to reflect the light flicker
            if (_sr != null && baseIntensity > 0.001f)
            {
                float alpha = Mathf.Clamp01(_currentIntensity / baseIntensity);
                _sr.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);
            }
        }

        /// <summary>Configures the streetlight according to sector module type.</summary>
        public void ConfigureForModule(string module)
        {
            if (module == "SLUM_ALLEYWAYS")
                profile = LightProfile.SlumFlicker;
            else if (module == "URBAN_HIGHWAY" || module == "OPEN_STREET")
                profile = LightProfile.HighwaySodium;
            else
                profile = LightProfile.IndustrialFlood;

            ApplyProfileColors();
        }

        /// <summary>
        /// Adapts streetlight intensity and behavior based on the live manifest environment settings.
        /// (Phase 5: Atmospheric Lighting & Visual Polish).
        /// </summary>
        public void SetEnvironmentLighting(float ambientLight, string timeOfDay)
        {
            if (string.IsNullOrEmpty(timeOfDay)) return;

            if (timeOfDay.Contains("day"))
            {
                // In bright daylight, dim the lamps
                baseIntensity = Mathf.Lerp(0.15f, 0.4f, 1f - Mathf.Clamp01(ambientLight));
            }
            else if (timeOfDay.Contains("dusk"))
            {
                // In dusk, lamps glow warmly against sunset colors
                baseIntensity = Mathf.Clamp(1.4f - ambientLight * 0.4f, 0.9f, 1.6f);
            }
            else // night
            {
                // Deep night: bright, high-contrast illumination
                baseIntensity = Mathf.Clamp(1.8f - ambientLight * 0.5f, 1.1f, 2.0f);
            }

            _intensityProp?.SetValue(_light2DComp, baseIntensity);
            if (_sr != null && baseIntensity > 0.001f)
            {
                _sr.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, Mathf.Clamp01(baseIntensity / 1.5f));
            }
        }
    }
}
