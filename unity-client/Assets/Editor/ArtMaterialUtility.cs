using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Converts imported PBR materials to URP Lit while preserving Albedo, Normal,
    /// Metallic/Smoothness (or Mask), Occlusion and Emission maps. This is the only place
    /// that assigns character materials; no shader/colors are improvised at runtime.
    /// </summary>
    internal static class ArtMaterialUtility
    {
        private const string UrpLit = "Universal Render Pipeline/Lit";

        private static readonly string[] AlbedoProperties = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" };
        private static readonly string[] NormalProperties = { "_BumpMap", "_NormalMap", "_Normal" };
        private static readonly string[] MetallicProperties = { "_MetallicGlossMap", "_MaskMap", "_MetallicSmoothness" };
        private static readonly string[] OcclusionProperties = { "_OcclusionMap", "_AO", "_AmbientOcclusion" };
        private static readonly string[] EmissionProperties = { "_EmissionMap", "_EmissiveMap" };

        /// <summary>Replaces every renderer material with a persistent URP Lit asset.</summary>
        public static void ConvertRenderers(GameObject root, string materialFolder)
        {
            EnsureFolder(materialFolder);
            var cache = new Dictionary<Material, Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var source = renderer.sharedMaterials;
                var converted = new Material[source.Length];
                for (var i = 0; i < source.Length; i++)
                {
                    if (source[i] == null) continue;
                    if (!cache.TryGetValue(source[i], out var target))
                    {
                        target = CreateOrUpdate(source[i], materialFolder);
                        cache[source[i]] = target;
                    }
                    converted[i] = target;
                }
                renderer.sharedMaterials = converted;
            }
        }

        /// <summary>Creates a URP Lit material asset, preserving authored texture maps.</summary>
        public static Material CreateOrUpdate(Material source, string materialFolder)
        {
            EnsureFolder(materialFolder);
            var targetPath = materialFolder + "/M_" + Sanitize(source.name) + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
            if (existing != null)
            {
                var lit = Shader.Find(UrpLit);
                if (lit != null && existing.shader != lit) existing.shader = lit;
                Apply(existing, source);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var shader = Shader.Find(UrpLit);
            if (shader == null)
                throw new System.InvalidOperationException("URP Lit shader is missing from the project.");
            var material = new Material(shader) { name = "M_" + Sanitize(source.name) };
            Apply(material, source);
            AssetDatabase.CreateAsset(material, targetPath);
            return material;
        }

        /// <summary>Configures normal/mask/albedo import settings for an external texture.</summary>
        public static void ConfigureTexture(TextureImporter importer, string path)
        {
            var file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var isNormal = file.Contains("normal") || file.Contains("_nrm") || file.EndsWith("_n");
            var isData = file.Contains("mask") || file.Contains("metallic") || file.Contains("roughness")
                || file.Contains("smoothness") || file.Contains("_ao") || file.Contains("occlusion");
            if (isNormal)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.anisoLevel = 4;
            }
            else if (isData)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.anisoLevel = 2;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.anisoLevel = 4;
            }
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = file.Contains("albedo") || file.Contains("diffuse")
                || file.Contains("basecolor");
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        /// <summary>
        /// Some authored FBX packs reference textures by filename but ship them in a
        /// sibling folder. If the model importer produced a material without an albedo,
        /// resolve the authored texture by its filename from the Art tree (never generate
        /// or recolor one). This keeps weapon/staff materials textured instead of white.
        /// </summary>
        public static void AssignFallbackAlbedo(GameObject root, string artRoot)
        {
            if (root == null) return;
            var textures = new Dictionary<string, Texture2D>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { artRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var key = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (texture != null && !textures.ContainsKey(key)) textures[key] = texture;
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                if (material.GetTexture("_MainTex") != null || material.GetTexture("_BaseMap") != null) continue;
                var name = material.name.ToLowerInvariant();
                if (name.StartsWith("m_")) name = name.Substring(2);
                if (!textures.TryGetValue(name, out var texture)
                    && !textures.TryGetValue(name + "_texture", out texture))
                    continue;
                material.SetTexture("_MainTex", texture);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            }
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void Apply(Material target, Material source)
        {
            CopyTexture(source, target, AlbedoProperties, "_BaseMap", true);
            CopyColor(source, target, "_BaseColor", new Color(1f, 1f, 1f, 1f));
            CopyTexture(source, target, NormalProperties, "_BumpMap", false);
            CopyTexture(source, target, MetallicProperties, "_MetallicGlossMap", false);
            CopyTexture(source, target, OcclusionProperties, "_OcclusionMap", false);
            CopyTexture(source, target, EmissionProperties, "_EmissionMap", false);

            CopyFloat(source, target, "_Smoothness", 0.5f);
            CopyFloat(source, target, "_Metallic", 0f);
            CopyFloat(source, target, "_BumpScale", 1f);
            CopyFloat(source, target, "_OcclusionStrength", 1f);
            CopyColor(source, target, "_EmissionColor", Color.black);

            SetKeyword(target, "_NORMALMAP", target.GetTexture("_BumpMap") != null);
            SetKeyword(target, "_METALLICSPECGLOSSMAP", target.GetTexture("_MetallicGlossMap") != null);
            SetKeyword(target, "_OCCLUSIONMAP", target.GetTexture("_OcclusionMap") != null);
            SetKeyword(target, "_EMISSION", target.GetTexture("_EmissionMap") != null
                || target.GetColor("_EmissionColor").maxColorComponent > 0.001f);
            SetKeyword(target, "_ALPHATEST_ON", source.IsKeywordEnabled("_ALPHATEST_ON"));
            target.renderQueue = source.renderQueue;
            target.enableInstancing = true;
        }

        private static void CopyTexture(Material source, Material target, string[] candidates,
            string destination, bool copyScale)
        {
            var property = FirstTextureProperty(source, candidates);
            var texture = property != null ? source.GetTexture(property) : null;
            if (texture == null) return;
            target.SetTexture(destination, texture);
            if (destination != "_MainTex" && target.HasProperty("_MainTex")) target.SetTexture("_MainTex", texture);
            if (!copyScale) return;
            target.SetTextureScale(destination, source.GetTextureScale(property));
            target.SetTextureOffset(destination, source.GetTextureOffset(property));
        }

        private static string FirstTextureProperty(Material source, string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (source.HasProperty(candidate) && source.GetTexture(candidate) != null) return candidate;
            }
            return null;
        }

        private static void CopyColor(Material source, Material target, string property, Color fallback)
        {
            var value = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                : source.HasProperty("_Color") ? source.GetColor("_Color")
                : fallback;
            if (target.HasProperty(property)) target.SetColor(property, value);
        }

        private static void CopyFloat(Material source, Material target, string property, float fallback)
        {
            if (!target.HasProperty(property)) return;
            target.SetFloat(property, source.HasProperty(property) ? source.GetFloat(property) : fallback);
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        private static string Sanitize(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (var character in name)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-'
                    ? character : '_');
            }
            return builder.Length == 0 ? "Imported" : builder.ToString();
        }
    }
}
