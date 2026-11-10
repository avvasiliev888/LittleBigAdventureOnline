using UnityEngine;
using UnityEngine.Rendering;

namespace Albion.Client
{
    /// <summary>Shared arena lighting/fog defaults for generated scenes and fallback runtime scenes.</summary>
    public static class ArenaEnvironment
    {
        /// <summary>Cool atmospheric depth against warm direct sunlight; fog is URP shader fog, not a Volume override.</summary>
        public static void ApplyAmbientAndFog()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.38f, 0.46f, 0.60f);
            RenderSettings.ambientEquatorColor = new Color(0.23f, 0.29f, 0.34f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.14f, 0.17f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.46f, 0.57f, 0.64f);
            RenderSettings.fogDensity = 0.032f;
        }

        /// <summary>Readable diagonal cast shadows, including small procedural character meshes.</summary>
        public static void ConfigureSun(Light light)
        {
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.91f, 0.76f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.9f;
            light.shadowBias = 0.03f;
            light.shadowNormalBias = 0.15f;
            light.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            RenderSettings.sun = light;
        }
    }
}
