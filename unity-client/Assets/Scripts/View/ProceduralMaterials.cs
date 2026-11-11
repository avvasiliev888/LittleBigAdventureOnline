using System;
using System.Collections.Generic;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>Shared ToonLit materials for the generated characters and weapons.</summary>
    public static class ProceduralMaterials
    {
        private static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Material> GlowCache = new Dictionary<int, Material>();
        private static Material _outline;

        /// <summary>Returns a shared opaque ToonLit material for the given colour.</summary>
        public static Material Lit(Color color)
        {
            var key = ColorKey(color);
            if (Cache.TryGetValue(key, out var material) && material != null)
            {
                return material;
            }

            material = new Material(Required("Albion/Toon Lit"))
            {
                name = "Procedural Toon " + ColorUtility.ToHtmlStringRGB(color),
                enableInstancing = true,
            };
            material.SetColor("_BaseColor", color);
            material.SetColor("_ShadeColor", Shade(color));
            material.SetColor("_HighlightColor", Color.Lerp(Color.white, color, 0.2f));
            material.SetFloat("_OutlineWidth", 0.011f);
            Cache[key] = material;
            return material;
        }

        /// <summary>Bright emissive ToonLit material for weapon glow and slash flashes.</summary>
        public static Material Glow(Color color)
        {
            var key = ColorKey(color);
            if (GlowCache.TryGetValue(key, out var material) && material != null)
            {
                return material;
            }

            material = new Material(Required("Albion/Toon Lit"))
            {
                name = "Procedural Glow " + ColorUtility.ToHtmlStringRGB(color),
                enableInstancing = true,
            };
            material.SetColor("_BaseColor", color);
            material.SetColor("_ShadeColor", Shade(color));
            material.SetColor("_EmissionColor", color * 2.4f);
            material.SetFloat("_RimStrength", 0.5f);
            GlowCache[key] = material;
            return material;
        }

        /// <summary>Shared inverted-hull material used by the outline child renderers.</summary>
        public static Material Outline
        {
            get
            {
                if (_outline != null)
                {
                    return _outline;
                }

                _outline = new Material(Required("Albion/Toon Outline"))
                {
                    name = "Procedural Character Outline",
                    enableInstancing = true,
                };
                _outline.SetColor("_OutlineColor", new Color(0.035f, 0.045f, 0.055f, 1f));
                _outline.SetFloat("_OutlineWidth", 0.012f);
                return _outline;
            }
        }

        private static Shader Required(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null)
            {
                throw new InvalidOperationException("Procedural character shader missing: " + name);
            }
            return shader;
        }

        private static Color Shade(Color color)
        {
            return new Color(color.r * 0.48f, color.g * 0.52f, color.b * 0.62f, color.a);
        }

        private static int ColorKey(Color color)
        {
            var r = Mathf.RoundToInt(color.r * 255f);
            var g = Mathf.RoundToInt(color.g * 255f);
            var b = Mathf.RoundToInt(color.b * 255f);
            var a = Mathf.RoundToInt(color.a * 255f);
            return (r << 24) | (g << 16) | (b << 8) | a;
        }
    }
}
