using Client.Network;
using UnityEngine;
using UnityEngine.Rendering;

namespace Albion.Client
{
    /// <summary>Cosmetic zone palette; bounds and ownership always come from the server configuration.</summary>
    public sealed class ZoneAtmosphere : MonoBehaviour
    {
        private ClientContent _content;
        private Transform _target;
        private Material _sky;
        private string _zone;
        private Color _horizon, _zenith, _sun, _ambient;
        private Quaternion _rotation;

        /// <summary>Bind once configuration is available. This component never changes gameplay state.</summary>
        public void Initialize(ClientContent content)
        {
            _content = content;
            _sky = new Material(Shader.Find("Albion/Gradient Sky")) { name = "Zone sky (runtime)" };
            RenderSettings.skybox = _sky;
            SetZone("blue");
            Apply(1f);
        }

        /// <summary>Follow the local rendered player; selection uses the configured zone rectangles.</summary>
        public void Follow(Transform target) => _target = target;

        /// <summary>Evaluate the same cosmetic palette immediately for an Editor GPU acceptance capture.</summary>
        public void PreviewAt(Vector3 position)
        {
            if (_content != null && _content.Config.Zones.TryGetZone(
                new System.Numerics.Vector3(position.x, 0f, position.z), out var zone))
                SetZone(zone.Key);
            Apply(1f);
        }

        private void LateUpdate()
        {
            if (_content == null) return;
            if (_target != null && _content.Config.Zones.TryGetZone(
                new System.Numerics.Vector3(_target.position.x, 0f, _target.position.z), out var zone))
                SetZone(zone.Key);
            Apply(1f - Mathf.Exp(-Time.deltaTime * 2f));
        }

        private void SetZone(string key)
        {
            if (_zone == key) return;
            _zone = key;
            switch (key)
            {
                case "yellow":
                    _horizon = Hex("F5D9A0"); _zenith = Hex("719BA7"); _sun = Hex("FFE2A3"); _ambient = Hex("858763");
                    _rotation = Quaternion.Euler(36f, -40f, 0f); break;
                case "red":
                    _horizon = Hex("C08479"); _zenith = Hex("3E3358"); _sun = Hex("FFB08A"); _ambient = Hex("9A7F8C");
                    _rotation = Quaternion.Euler(34f, -55f, 0f); break;
                default:
                    _horizon = Hex("BDDCD6"); _zenith = Hex("477E9C"); _sun = Hex("D9F0E5"); _ambient = Hex("648F98");
                    _rotation = Quaternion.Euler(43f, -35f, 0f); break;
            }
        }

        private void Apply(float blend)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(RenderSettings.ambientSkyColor, _ambient, blend);
            RenderSettings.ambientEquatorColor = Color.Lerp(RenderSettings.ambientEquatorColor, _ambient * 0.65f, blend);
            RenderSettings.ambientGroundColor = _ambient * 0.3f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0045f;
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, _horizon, blend);
            _sky.SetColor("_HorizonColor", RenderSettings.fogColor);
            _sky.SetColor("_ZenithColor", Color.Lerp(_sky.GetColor("_ZenithColor"), _zenith, blend));
            _sky.SetColor("_GroundColor", _ambient * 0.6f);
            _sky.SetColor("_SunColor", _sun);
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.color = Color.Lerp(sun.color, _sun, blend);
                sun.transform.rotation = Quaternion.Slerp(sun.transform.rotation, _rotation, blend);
                _sky.SetVector("_SunDirection", -sun.transform.forward);
            }
            if (Camera.main != null) Camera.main.clearFlags = CameraClearFlags.Skybox;
        }

        /// <summary>Read an authored art-palette color (not a gameplay setting).</summary>
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var color);
            return color;
        }

        private void OnDestroy()
        {
            if (_sky != null) Destroy(_sky);
        }
    }
}
