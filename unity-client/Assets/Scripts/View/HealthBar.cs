using UnityEngine;
using UnityEngine.Rendering;

namespace Albion.Client
{
    /// <summary>
    /// Presentation-only world-space bars: a dark backing and a red HP fill on top, plus a thinner
    /// blue resource (mana) fill directly underneath with the same width. Both always face the camera.
    /// HP and resource are authoritative and come from snapshots; at 0 the dark backing remains
    /// visible so the empty proportion is readable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealthBar : MonoBehaviour
    {
        private const float Width = 0.9f;
        private const float Height = 0.11f;
        private const float ResourceHeight = 0.045f;
        private const float ResourceGap = 0.015f;
        private const float FillDepth = -0.01f;
        private static readonly float ResourceY = -(Height * 0.5f + ResourceGap + ResourceHeight * 0.5f);
        private static readonly Color BackColor = new Color(0.055f, 0.055f, 0.065f, 1f);
        private static readonly Color FillColor = new Color(0.82f, 0.045f, 0.045f, 1f);
        private static readonly Color ResourceColor = new Color(0.10f, 0.36f, 0.95f, 1f);

        private Transform _fill;
        private Transform _resourceFill;
        private Camera _camera;

        /// <summary>Creates HP + resource bars above the entity root at <paramref name="height"/> metres.</summary>
        public static HealthBar Attach(Transform parent, float height)
        {
            var holder = new GameObject("Health Bar");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = new Vector3(0f, height + 0.24f, 0f);
            var bar = holder.AddComponent<HealthBar>();
            bar.Build();
            return bar;
        }

        /// <summary>Updates the red HP fill; the backing bar is always visible.</summary>
        public void SetHp(int hp, int maxHp)
        {
            if (_fill == null)
            {
                return;
            }

            var ratio = Ratio(hp, maxHp);
            _fill.localScale = new Vector3(Width * ratio, Height, 1f);
            // The quad pivot is centred, so shifting it left by half the missing width pins the
            // red fill to the left edge while it shrinks.
            _fill.localPosition = new Vector3(-Width * (1f - ratio) * 0.5f, 0f, FillDepth);
            _fill.gameObject.SetActive(ratio > 0f);
        }

        /// <summary>Updates the blue resource/mana fill under the HP bar.</summary>
        public void SetResource(int resource, int maxResource)
        {
            if (_resourceFill == null)
            {
                return;
            }

            var ratio = Ratio(resource, maxResource);
            _resourceFill.localScale = new Vector3(Width * ratio, ResourceHeight, 1f);
            _resourceFill.localPosition = new Vector3(
                -Width * (1f - ratio) * 0.5f,
                ResourceY,
                FillDepth);
            _resourceFill.gameObject.SetActive(ratio > 0f);
        }

        private static float Ratio(int value, int max)
        {
            return max > 0 ? Mathf.Clamp01((float)value / max) : 0f;
        }

        private void Build()
        {
            CreateQuad("Back", transform, BackColor, 0f);
            _fill = CreateQuad("Hp Fill", transform, FillColor, FillDepth).transform;
            CreateQuad("Mana Back", transform, BackColor, 0f, ResourceHeight, ResourceY);
            _resourceFill = CreateQuad(
                "Mana Fill",
                transform,
                ResourceColor,
                FillDepth,
                ResourceHeight,
                ResourceY).transform;
            SetHp(100, 100);
            SetResource(100, 100);
            FaceCamera();
        }

        private void LateUpdate()
        {
            FaceCamera();
        }

        private void FaceCamera()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera != null)
            {
                transform.rotation = _camera.transform.rotation;
            }
        }

        private static GameObject CreateQuad(
            string name,
            Transform parent,
            Color color,
            float z,
            float height = Height,
            float y = 0f)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = new Vector3(0f, y, z);
            quad.transform.localScale = new Vector3(Width, height, 1f);
            var collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateMaterial(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return quad;
        }

        private static Material CreateMaterial(Color color)
        {
            // URP Lit is already referenced by every Warrior material, so it is guaranteed to be
            // present in the player build. Unlit/Sprites can be stripped when no authored asset
            // references them; a stripped shader would render the bar magenta or not at all.
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            var material = new Material(shader)
            {
                name = "Health Bar (" + color.r.ToString("0.00") + ", " + color.g.ToString("0.00") + ")",
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0f);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                // Keep the bar readable even when the character stands in shadow; the scene's
                // directional light is not guaranteed to hit a camera-facing quad.
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 1f));
            }

            return material;
        }
    }
}
