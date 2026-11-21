using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Albion.Editor
{
    /// <summary>
    /// Renders every humanoid body in the project to a PNG, so bodies can be chosen by eye.
    ///
    /// Why this exists: choosing a body from a file name is a guess — "Base Man" says nothing
    /// about silhouette, and "Modular Fantasy knight" is a pile of armour parts rather than a
    /// character. Rendering every candidate once turns the choice into a comparison of pictures,
    /// which is the only form in which it can actually be made.
    ///
    /// A body is only rendered if it has a valid Humanoid avatar, because a body that cannot
    /// retarget the shared animation sets is useless for the mob pipeline regardless of looks.
    /// The skipped ones are reported by reason instead of silently disappearing.
    ///
    /// Output: one PNG per body into <see cref="OutRoot"/>, plus a JSON manifest.
    ///
    /// Run:
    ///   Unity.exe -batchmode -quit -projectPath &lt;copy&gt; \
    ///     -executeMethod Albion.Editor.BodyGallery.Run -logFile &lt;log&gt;
    /// </summary>
    public static class BodyGallery
    {
        private const string OutRoot = "F:/dsh-tmp/bodies";
        /// <summary>Set by the last <see cref="Capture"/> call when most of the body is missing-shader pink.</summary>
        private static bool LastRenderMissingMaterial;

        private const int Width = 260;
        private const int Height = 340;

        [MenuItem("Albion/Bodies/Render Body Gallery")]
        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("########## BODY GALLERY START ##########");

            try
            {
                Directory.CreateDirectory(OutRoot);

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = BuildCamera();
                var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                target.Create();

                var rendered = new List<Row>();
                var skipped = new List<Row>();
                var parts = new List<Row>();

                // A body sold in three render pipelines (Default/HDRP/URP) is ONE body. Without
                // this guard the gallery shows "RFA_Model" three times and the customer has to
                // guess whether the difference is real — measured 21.11.2026, three identical frames.
                var seenShape = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var models = CollectModels();
                report.AppendLine("candidate models: " + models.Count);

                foreach (var path in models)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null)
                    {
                        skipped.Add(new Row(path, "load failed"));
                        continue;
                    }

                    var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                    if (avatar == null || !avatar.isValid)
                    {
                        // Not a rigged body. It may still be a body PART (head, torso, armour) that
                        // the publisher ships separated for customisation. Hiding those would tell
                        // the customer "there is nothing here" when in fact there is a usable mesh,
                        // so they are rendered into a separate "parts" set with the reason stated.
                        if (LooksLikeBodyPart(model))
                        {
                            var partBytes = RenderBody(model, camera, target);
                            if (partBytes != null)
                            {
                                var partName = Path.GetFileNameWithoutExtension(path);
                                var partFile = "part_" + UniqueName(parts, partName);
                                File.WriteAllBytes(Path.Combine(OutRoot, partFile + ".png"), partBytes);
                                parts.Add(new Row(path, partFile));
                                continue;
                            }
                        }

                        skipped.Add(new Row(path, avatar == null ? "no avatar" : "invalid avatar"));
                        continue;
                    }

                    // Clip-carrier FBX files hold a copy of the body mesh next to the animation, so
                    // they pass a skinned-mesh test and flooded the gallery with 33 duplicates of
                    // the same wizard — measured 21.11.2026, 33 of the first 45 frames were clips,
                    // not bodies. Publishers put clips under an "Animations" folder by convention,
                    // while bodies sit next to their textures, so that path is the reliable signal.
                    if (path.IndexOf("/Animations/", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        skipped.Add(new Row(path, "clip carrier"));
                        continue;
                    }

                    // Skinned meshes are the signal that this is a character, not a prop. An FBX
                    // full of rigid armour pieces has no skeleton to pose and cannot be a body.
                    var skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                    if (skinned == 0)
                    {
                        skipped.Add(new Row(path, "no skinned mesh"));
                        continue;
                    }

                    // Generic rigs are rendered too. They cannot retarget the shared humanoid sets,
                    // but the customer asked to see every body, and hiding a usable-looking model
                    // because of its rig would remove it from the choice without explanation. The
                    // rig is written into the filename so the difference stays visible.
                    var rig = avatar.isHuman ? "humanoid" : "generic";

                    // Same shape under another pipeline folder is a duplicate, not a second body.
                    var shapeKey = rig + ":" + SkinnedVertexCount(model);
                    if (!seenShape.Add(shapeKey))
                    {
                        skipped.Add(new Row(path, "duplicate of an earlier body"));
                        continue;
                    }

                    var bytes = RenderBody(model, camera, target);
                    if (bytes == null)
                    {
                        skipped.Add(new Row(path, "empty render"));
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(path);
                    var file = UniqueName(rendered, name);
                    File.WriteAllBytes(Path.Combine(OutRoot, file + ".png"), bytes);
                    rendered.Add(new Row(path, file)
                    {
                        Rig = rig,
                        Note = LastRenderMissingMaterial ? "нет материала" : string.Empty,
                    });
                }

                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                EditorSceneManager.CloseScene(scene, true);

                WriteManifest(rendered, skipped, parts);

                report.AppendLine("=== RENDERED ===");
                foreach (var row in rendered)
                {
                    report.AppendLine(row.Key + " <- " + row.Value);
                }

                report.AppendLine("=== SKIPPED ===");
                foreach (var row in skipped)
                {
                    report.AppendLine(row.Value + " : " + row.Key);
                }

                report.AppendLine("=== PARTS (unrigged meshes, shown for completeness) ===");
                foreach (var row in parts)
                {
                    report.AppendLine(row.Value + " <- " + row.Key);
                }

                report.AppendLine("rendered: " + rendered.Count);
                report.AppendLine("parts: " + parts.Count);
                report.AppendLine("skipped: " + skipped.Count);
            }
            catch (Exception exception)
            {
                report.AppendLine("FAILED: " + exception.GetType().Name + ": " + exception.Message);
                report.AppendLine(exception.StackTrace);
                report.AppendLine("########## BODY GALLERY END ##########");
                Debug.Log(report.ToString());
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                return;
            }

            report.AppendLine("########## BODY GALLERY END ##########");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// True when any renderer has no material, or Unity's error shader. That shader is what
        /// draws the pink silhouette, so an empty or broken material is exactly the condition
        /// worth warning about.
        /// </summary>
        private static bool HasMissingMaterial(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    return true;
                }

                foreach (var material in materials)
                {
                    if (material == null || material.shader == null)
                    {
                        return true;
                    }

                    if (material.shader.name.IndexOf("InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    // A material authored for the Built-in pipeline renders as Unity's pink error
                    // silhouette under URP, which this project uses. That is the real cause of the
                    // pink "Fantasy_character", not a missing material: its .mat is valid and points
                    // at the Standard shader (guid 0000000000000000f000000000000000) — measured
                    // 21.11.2026. Without this check the warning stayed silent while the picture
                    // was visibly broken.
                    if (material.shader.name.StartsWith("Standard", StringComparison.OrdinalIgnoreCase)
                        || material.shader.name.IndexOf("Standard (", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// A rigged body split into parts still ships a skinned or textured mesh, whereas a
        /// collision helper or a single blade has neither. The test is deliberately loose: it is
        /// used only to decide whether an unrigged model deserves a picture, not to approve it.
        /// </summary>
        private static bool LooksLikeBodyPart(GameObject model)
        {
            var meshes = model.GetComponentsInChildren<MeshRenderer>(true);
            var skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            return meshes.Length + skinned.Length > 0;
        }

        /// <summary>
        /// Total vertex count identifies a body independently of its folder. Three copies of the
        /// same character under Default/HDRP/URP folders produce identical totals, while genuinely
        /// different models do not — which is exactly the duplicate test needed here.
        /// </summary>
        private static int SkinnedVertexCount(GameObject model)
        {
            var total = 0;
            foreach (var meshFilter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh != null)
                {
                    total += meshFilter.sharedMesh.vertexCount;
                }
            }

            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null)
                {
                    total += skinned.sharedMesh.vertexCount;
                }
            }

            return total;
        }

        /// <summary>
        /// Every model FBX in the project. No folder whitelist on purpose: imported packages use
        /// unpredictable folder names ("Serah Fei Bikini", "Fantasy knight", "Base_Man"), and a
        /// hardcoded list silently misses whole packages — measured 21.11.2026, a whitelist built
        /// from package titles found 5 of 11 folders. The rig and skinned-mesh filters below are
        /// what actually separate a body from a prop, so scanning everything is both simpler and
        /// more correct.
        /// </summary>
        private static List<string> CollectModels()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var guid in AssetDatabase.FindAssets("t:Model"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || seen.Contains(path))
                {
                    continue;
                }

                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".FBX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                seen.Add(path);
                result.Add(path);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static string UniqueName(List<Row> taken, string name)
        {
            if (!taken.Any(r => string.Equals(r.Value, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }

            for (var i = 2; i < 100; i++)
            {
                var candidate = name + "_" + i;
                if (!taken.Any(r => string.Equals(r.Value, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    return candidate;
                }
            }

            return name + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static Camera BuildCamera()
        {
            var cameraObject = new GameObject("BodyCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.20f, 0.22f, 0.26f, 1f);

            var key = new GameObject("BodyKey");
            var keyLight = key.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.4f;
            keyLight.color = new Color(1f, 0.97f, 0.92f, 1f);
            key.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

            var fill = new GameObject("BodyFill");
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.6f;
            fillLight.color = new Color(0.74f, 0.80f, 0.95f, 1f);
            fill.transform.rotation = Quaternion.Euler(10f, -55f, 0f);

            var rim = new GameObject("BodyRim");
            var rimLight = rim.AddComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.intensity = 0.5f;
            rimLight.color = new Color(0.9f, 0.9f, 1f, 1f);
            rim.transform.rotation = Quaternion.Euler(-20f, 40f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.40f, 0.43f, 0.50f, 1f);

            return camera;
        }

        private static byte[] RenderBody(GameObject model, Camera camera, RenderTexture target)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (instance == null)
            {
                return null;
            }

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                // The real test for a drawable body. A pink silhouette is what a missing shader
                // looks like, and it must be reported rather than sold as a usable model:
                // "Fantasy_character" rendered solid pink because its materials did not come with
                // the package — measured 21.11.2026. Counting pink pixels failed as a detector
                // because the grey background made every pixel count as visible.
                LastRenderMissingMaterial = HasMissingMaterial(instance);

                // A T-pose from the file is what a body looks like unposed; sampling nothing keeps
                // the comparison fair, since no body gets an animation advantage.
                Frame(camera, instance);

                var pixels = Capture(camera, target);
                return pixels == null ? null : Encode(pixels);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void Frame(Camera camera, GameObject instance)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.one * 0.5f);
            var any = false;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds.min);
                    bounds.Encapsulate(renderer.bounds.max);
                }
            }

            if (!any)
            {
                camera.transform.position = new Vector3(0f, 1f, -3f);
                camera.transform.LookAt(new Vector3(0f, 1f, 0f));
                return;
            }

            var height = Mathf.Max(0.2f, bounds.size.y);
            var center = bounds.center;
            var distance = height * 2.7f;

            // Three-quarter front view: a straight-on view hides depth, a profile hides the face.
            camera.transform.position = center + new Vector3(distance * 0.42f, height * 0.10f, distance);
            camera.transform.LookAt(center);
        }

        private static Color32[] Capture(Camera camera, RenderTexture target)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
            {
                var previous = RenderTexture.active;
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;
                RenderTexture.active = previous;
            }

            var previousActive = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = target;
                image = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);
                var pixels = image.GetPixels32();

                var visible = 0;
                foreach (var pixel in pixels)
                {
                    if (pixel.r > 12 || pixel.g > 12 || pixel.b > 12)
                    {
                        visible++;
                    }
                }

                // A body that renders as background means the mesh or camera failed; reporting it
                // as "empty render" is honest, shipping a blank square is not.
                return visible < pixels.Length / 60 ? null : pixels;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (image != null)
                {
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
        }

        private static byte[] Encode(Color32[] pixels)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void WriteManifest(List<Row> rendered, List<Row> skipped, List<Row> parts)
        {
            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"bodies\": [");
            for (var i = 0; i < rendered.Count; i++)
            {
                json.Append("    {\"file\": \"").Append(rendered[i].Value.Replace("\\", "/"))
                    .Append("\", \"path\": \"").Append(rendered[i].Key.Replace("\\", "/"))
                    .Append("\", \"rig\": \"").Append(rendered[i].Rig)
                    .Append("\", \"note\": \"").Append(rendered[i].Note).Append("\"}");
                json.AppendLine(i < rendered.Count - 1 ? "," : string.Empty);
            }

            json.AppendLine("  ],");
            json.AppendLine("  \"parts\": [");
            for (var i = 0; i < parts.Count; i++)
            {
                json.Append("    {\"file\": \"").Append(parts[i].Value.Replace("\\", "/"))
                    .Append("\", \"path\": \"").Append(parts[i].Key.Replace("\\", "/")).Append("\"}");
                json.AppendLine(i < parts.Count - 1 ? "," : string.Empty);
            }

            json.AppendLine("  ],");
            json.AppendLine("  \"skipped\": [");
            for (var i = 0; i < skipped.Count; i++)
            {
                json.Append("    {\"path\": \"").Append(skipped[i].Key.Replace("\\", "/"))
                    .Append("\", \"reason\": \"").Append(skipped[i].Value).Append("\"}");
                json.AppendLine(i < skipped.Count - 1 ? "," : string.Empty);
            }

            json.AppendLine("  ]");
            json.AppendLine("}");
            File.WriteAllText(Path.Combine(OutRoot, "bodies.json"), json.ToString(), new UTF8Encoding(false));
        }

        private sealed class Row
        {
            public Row(string key, string value)
            {
                Key = key;
                Value = value;
            }

            public string Key { get; }

            public string Value { get; }

            /// <summary>
            /// Kept out of <see cref="Value"/> on purpose: the value is the PNG file name, and
            /// appending "[humanoid]" to it made every gallery link point at a file that does not
            /// exist — measured 21.11.2026, all 14 body images were broken.
            /// </summary>
            public string Rig { get; set; } = string.Empty;

            /// <summary>Human-readable warning shown on the card, e.g. a missing material.</summary>
            public string Note { get; set; } = string.Empty;
        }
    }
}
