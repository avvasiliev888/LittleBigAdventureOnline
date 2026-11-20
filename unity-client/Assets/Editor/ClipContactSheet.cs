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
    /// Shows what an animation package actually contains, without a human opening the Editor.
    ///
    /// Why this exists: choosing an animation donor for retargeting needs the MOTION to be seen,
    /// not a list of file names. A name like "Attack02Maintain" says nothing about whether the
    /// swing is a chop, a cast, or a shove. One batch run renders every clip into stills, so the
    /// decision is made on evidence instead of on file names — and the run is deterministic, so
    /// it costs no tokens to repeat.
    ///
    /// Output: one PNG per frame for the required clips (assembled into GIFs outside Unity) plus
    /// one mid-pose PNG per clip for the full catalogue sheet.
    ///
    /// Run:
    ///   Unity.exe -batchmode -quit -projectPath &lt;copy&gt; \
    ///     -executeMethod Albion.Editor.ClipContactSheet.Run -logFile &lt;log&gt;
    /// </summary>
    public static class ClipContactSheet
    {
        private const string PackageName = "Battle Wizard PBR.unitypackage";
        private const string ImportedRoot = "Assets/WizardPBR";
        private const string BodyPath = "Assets/WizardPBR/Mesh/WizardBodyMesh.fbx";
        private const string AnimationsRoot = "Assets/WizardPBR/Animations";

        /// <summary>Where the stills land. Outside the project so nothing is imported by accident.</summary>
        private const string OutRoot = "F:/dsh-tmp/wizclips/out";

        private const int Width = 240;
        private const int Height = 300;

        /// <summary>Frames per required clip. Twelve is enough for a loop to read as motion.</summary>
        private const int Frames = 12;

        /// <summary>
        /// The six clips the game actually needs. Chosen by name fragment, because the package
        /// uses "BattleRunForward" and "WalkForward" rather than plain "run"/"walk".
        /// </summary>
        private static readonly (string Label, string Fragment)[] Required =
        {
            ("idle", "Idle01"),
            ("walk", "WalkForward"),
            ("run", "BattleRunForward"),
            ("attack", "Attack01"),
            ("death", "Die"),
            ("hit", "GetHit"),
        };

        [MenuItem("Albion/Clips/Contact Sheet Battle Wizard")]
        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("########## CLIP SHEET START ##########");

            try
            {
                if (!File.Exists(BodyPath))
                {
                    // Not imported by this method on purpose. AssetDatabase.ImportPackage is
                    // ASYNCHRONOUS in batchmode: the very next statement sees an empty project and
                    // every path check fails (measured 20.11.2026 — "import done, body exists=False").
                    // The package is unpacked by tools/unity/extract-package.py BEFORE Unity starts,
                    // which is deterministic and needs no Editor at all.
                    throw new InvalidOperationException(
                        "Package not unpacked. Run: python tools/unity/extract-package.py <package> <project>. Missing: "
                        + BodyPath);
                }

                var clipPaths = AssetDatabase
                    .FindAssets("t:AnimationClip", new[] { AnimationsRoot })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                report.AppendLine("clip files: " + clipPaths.Count);
                if (clipPaths.Count == 0)
                {
                    throw new InvalidOperationException("No animation FBX found under " + AnimationsRoot);
                }

                var bodyModel = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
                if (bodyModel == null)
                {
                    throw new InvalidOperationException("Body model did not load: " + BodyPath);
                }

                var avatar = AssetDatabase
                    .LoadAllAssetsAtPath(BodyPath)
                    .OfType<Avatar>()
                    .FirstOrDefault();

                report.AppendLine("avatar: " + (avatar != null ? avatar.name + " valid=" + avatar.isValid : "NONE"));

                Directory.CreateDirectory(OutRoot);
                var frameDir = Path.Combine(OutRoot, "frames");
                var stillDir = Path.Combine(OutRoot, "stills");
                Directory.CreateDirectory(frameDir);
                Directory.CreateDirectory(stillDir);

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = BuildCamera();
                var renderTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                };
                renderTexture.Create();

                report.AppendLine("=== CLIPS ===");

                foreach (var clipPath in clipPaths)
                {
                    var clip = AssetDatabase
                        .LoadAllAssetsAtPath(clipPath)
                        .OfType<AnimationClip>()
                        .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));

                    if (clip == null)
                    {
                        report.AppendLine("SKIP no clip: " + clipPath);
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(clipPath);
                    var length = clip.length;
                    report.AppendLine($"{name} len={length:0.###} loop={clip.isLooping}");

                    // The whole catalogue gets one mid-pose still, so every clip is visible even
                    // if it is not one of the six the game needs today.
                    var still = RenderAt(bodyModel, avatar, clip, length * 0.5f, camera, renderTexture);
                    if (still != null)
                    {
                        File.WriteAllBytes(Path.Combine(stillDir, name + ".png"), still);
                    }

                    var label = MatchRequired(name);
                    if (label == null)
                    {
                        continue;
                    }

                    // A loop is sampled over one period; a one-shot over its whole length. Sampling
                    // a one-shot on a loop wrap shows the same first pose twice.
                    var step = clip.isLooping ? length / Frames : length / (Frames - 1);
                    for (var frame = 0; frame < Frames; frame++)
                    {
                        var bytes = RenderAt(bodyModel, avatar, clip, step * frame, camera, renderTexture);
                        if (bytes == null)
                        {
                            continue;
                        }

                        var file = Path.Combine(frameDir, $"{label}_{frame:00}.png");
                        File.WriteAllBytes(file, bytes);
                    }
                }

                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                EditorSceneManager.CloseScene(scene, true);

                report.AppendLine("frames: " + Directory.GetFiles(frameDir, "*.png").Length);
                report.AppendLine("stills: " + Directory.GetFiles(stillDir, "*.png").Length);
            }
            catch (Exception exception)
            {
                report.AppendLine("FAILED: " + exception.GetType().Name + ": " + exception.Message);
                report.AppendLine(exception.StackTrace);
                report.AppendLine("########## CLIP SHEET END ##########");
                Debug.Log(report.ToString());
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                return;
            }

            report.AppendLine("########## CLIP SHEET END ##########");
            Debug.Log(report.ToString());
        }

        private static string MatchRequired(string clipName)
        {
            // EXACT name only, deliberately. A substring rule let "DieRecovery" match the "Die"
            // fragment and overwrite the death_XX.png frames that "Die" had just written, so the
            // death filmstrip showed the character STANDING UP — measured 20.11.2026, it read as a
            // resurrection and made a perfectly good clip look broken. All six required clips have
            // exact file names, so substring matching buys nothing and only creates collisions.
            foreach (var (label, fragment) in Required)
            {
                if (string.Equals(clipName, fragment, StringComparison.OrdinalIgnoreCase))
                {
                    return label;
                }
            }

            return null;
        }

        private static Camera BuildCamera()
        {
            var cameraObject = new GameObject("SheetCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 32f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Mid grey: a black background hides a dark silhouette, white hides a pale one.
            camera.backgroundColor = new Color(0.22f, 0.24f, 0.28f, 1f);

            var lightObject = new GameObject("SheetLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = new Color(1f, 0.97f, 0.92f, 1f);
            lightObject.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

            var fillObject = new GameObject("SheetFill");
            var fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.55f;
            fill.color = new Color(0.72f, 0.78f, 0.92f, 1f);
            fillObject.transform.rotation = Quaternion.Euler(12f, -60f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.5f, 1f);

            return camera;
        }

        /// <summary>
        /// Poses the body on one clip at one time and returns a PNG. Returns null when the render
        /// is empty, so a silently black frame cannot be mistaken for a successful capture.
        /// </summary>
        private static byte[] RenderAt(
            GameObject bodyModel,
            Avatar avatar,
            AnimationClip clip,
            float time,
            Camera camera,
            RenderTexture target)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(bodyModel);
            if (instance == null)
            {
                return null;
            }

            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                var animator = instance.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = instance.AddComponent<Animator>();
                }

                if (avatar != null)
                {
                    animator.avatar = avatar;
                }

                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(instance, clip, Mathf.Max(0f, time));
                AnimationMode.EndSampling();

                Frame(camera, instance);
                var pixels = Capture(camera, target);
                AnimationMode.StopAnimationMode();

                return pixels == null ? null : Encode(pixels);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Frames the body from a three-quarter front view, scaled to its own height.</summary>
        private static void Frame(Camera camera, GameObject instance)
        {
            var bounds = new Bounds(instance.transform.position, Vector3.one * 0.5f);
            var any = false;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                bounds = any ? Encapsulate(bounds, renderer.bounds) : renderer.bounds;
                any = true;
            }

            var size = Mathf.Max(0.2f, bounds.size.y);
            var center = bounds.center;
            var distance = size * 2.6f;

            // Model forward is +Z in Unity, so a camera at +X/+Z sees the face from the front-right.
            camera.transform.position = center + new Vector3(distance * 0.45f, size * 0.12f, distance);
            camera.transform.LookAt(center);
        }

        private static Bounds Encapsulate(Bounds accumulator, Bounds next)
        {
            accumulator.Encapsulate(next.min);
            accumulator.Encapsulate(next.max);
            return accumulator;
        }

        private static Color32[] Capture(Camera camera, RenderTexture target)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
            {
                // Not on an SRP, or an SRP without StandardRequest support: the built-in path.
                var previous = RenderTexture.active;
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;
                RenderTexture.active = previous;
            }
            else
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderPipeline.SubmitRenderRequest(camera, request);
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

                // An empty frame means the pose never applied or the camera missed the body.
                // Returning null keeps that failure visible in the frame count instead of
                // shipping a black square the customer has to decode.
                return visible < pixels.Length / 50 ? null : pixels;
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
    }
}
