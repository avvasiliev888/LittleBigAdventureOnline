using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Renders a mob prefab to PNG, one frame per animation state, so the body can be judged by
    /// eye before anyone launches the game.
    ///
    /// Why: the material validator only proves a texture is assigned. It cannot see a pink shader,
    /// a model sunk into the floor, a 2,45 m wizard who is actually 20 cm tall, or a robe that
    /// renders black. Each of those has already happened in this project once.
    ///
    /// Run:
    ///   Unity.exe -batchmode -quit -projectPath &lt;copy&gt; \
    ///     -executeMethod Albion.Editor.MobPortrait.Run -logFile &lt;log&gt;
    /// </summary>
    public static class MobPortrait
    {
        private const string OutRoot = "F:/dsh-tmp/mob-portraits";
        private const int Width = 512;
        private const int Height = 640;

        private static readonly (string Key, string Prefab)[] Targets =
        {
            ("red_warlord", "Assets/Art/Characters/Generated/WizardBody.prefab"),
        };

        /// <summary>Frames taken per mob. Idle first, then the three poses that reveal a bad clip.</summary>
        private static readonly string[] Poses = { "idle", "attack", "hit", "death" };

        [MenuItem("Albion/Mobs/Render Mob Portraits")]
        public static void Run()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("########## MOB PORTRAIT START ##########");
            Directory.CreateDirectory(OutRoot);

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            try
            {
                var lightObject = new GameObject("Sun");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.4f;
                light.color = new Color(1f, 0.96f, 0.9f);
                lightObject.transform.rotation = Quaternion.Euler(45f, 140f, 0f);

                var cameraObject = new GameObject("Camera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
                camera.fieldOfView = 32f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 200f;

                var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                target.Create();
                camera.targetTexture = target;

                foreach (var job in Targets)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(job.Prefab);
                    if (prefab == null)
                    {
                        report.AppendLine("MISSING prefab: " + job.Prefab);
                        continue;
                    }

                    var controller = LoadController(prefab);
                    if (controller == null)
                    {
                        report.AppendLine("no animator controller on: " + job.Key);
                        continue;
                    }

                    foreach (var pose in Poses)
                    {
                        // A FRESH instance per pose. Measured 21.11.2026: sampling several poses on
                        // one instance made only the first one stick — attack, hit and death all
                        // reported the identical bone rotation (pelvis 338,7/88,7/4,0) and produced
                        // byte-identical PNGs, because the graph kept re-applying its own state.
                        // A new instance has no previous state to fall back to.
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        instance.transform.position = Vector3.zero;

                        var animator = instance.GetComponentInChildren<Animator>();
                        if (animator != null)
                        {
                            // The graph must not run: it would overwrite the sampled pose during
                            // Render with whatever state the transition rules pick.
                            animator.enabled = false;
                        }

                        var clip = ResolveClip(controller, pose);
                        if (clip == null)
                        {
                            report.AppendLine("MISSING clip for pose: " + pose);
                            UnityEngine.Object.DestroyImmediate(instance);
                            continue;
                        }

                        UnityEditor.AnimationMode.StartAnimationMode();
                        try
                        {
                            UnityEditor.AnimationMode.BeginSampling();
                            UnityEditor.AnimationMode.SampleAnimationClip(instance, clip, clip.length * 0.35f);
                            UnityEditor.AnimationMode.EndSampling();
                        }
                        catch (Exception exception)
                        {
                            report.AppendLine("sample failed for " + pose + ": " + exception.Message);
                        }

                        var bounds = MeasureBounds(instance);
                        var pelvis = FindBone(instance, "pelvis");
                        report.AppendLine(job.Key + " pose=" + pose + " clip=" + clip.name
                            + " len=" + clip.length.ToString("0.00")
                            + " size=" + bounds.size.ToString("0.00")
                            + " pelvis=" + (pelvis != null
                                ? pelvis.localRotation.eulerAngles.ToString("0.0") : "-"));

                        Frame(camera, bounds);
                        Render(camera, target, job.Key, pose);

                        UnityEditor.AnimationMode.StopAnimationMode();
                        UnityEngine.Object.DestroyImmediate(instance);
                    }
                }

                target.Release();
            }
            catch (Exception exception)
            {
                report.AppendLine("FAILED: " + exception.GetType().Name + ": " + exception.Message);
                report.AppendLine(exception.StackTrace);
                report.AppendLine("########## MOB PORTRAIT END ##########");
                Debug.Log(report.ToString());
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            report.AppendLine("########## MOB PORTRAIT END ##########");
            Debug.Log(report.ToString());
        }

        private static RuntimeAnimatorController LoadController(GameObject prefab)
        {
            var animator = prefab.GetComponentInChildren<Animator>(true);
            return animator != null ? animator.runtimeAnimatorController : null;
        }

        /// <summary>Union of every renderer's world bounds — the only honest measure of body size.</summary>
        private static Bounds MeasureBounds(GameObject root)
        {
            var has = false;
            var result = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                if (!has)
                {
                    result = renderer.bounds;
                    has = true;
                }
                else
                {
                    result.Encapsulate(renderer.bounds);
                }
            }

            return result;
        }

        /// <summary>
        /// Three-quarter view with margin, re-framed per pose: a death animation lies the body down
        /// and a fixed frame cuts it in half. A straight front view hides a missing face texture,
        /// a straight side view hides a missing arm.
        /// </summary>
        private static void Frame(Camera camera, Bounds bounds)
        {
            if (bounds.size.y <= 0.01f && bounds.size.x <= 0.01f)
            {
                camera.transform.position = new Vector3(1.6f, 1.4f, -3.2f);
                camera.transform.LookAt(new Vector3(0f, 1f, 0f));
                return;
            }

            var extent = Mathf.Max(bounds.size.y, Mathf.Max(bounds.size.x, bounds.size.z));
            var distance = extent * 2.2f + 1f;
            var centre = bounds.center;

            // The Battle Wizard faces +Z; shooting from -Z gave four frames of his back, which
            // hides the face and any missing texture on it. Shoot from the front instead.
            camera.transform.position = centre + new Vector3(-distance * 0.55f, extent * 0.15f, distance);
            camera.transform.LookAt(centre);
        }

        private static Transform FindBone(GameObject root, string name)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(transform.name, name, StringComparison.OrdinalIgnoreCase)) return transform;
            }

            return null;
        }

        /// <summary>
        /// Maps a pose name to the clip behind the matching state. States are named after the
        /// authored clip, so matching is by role: a controller built from the magic set has Idle01,
        /// Attack01, GetHit and Die.
        /// </summary>
        private static AnimationClip ResolveClip(RuntimeAnimatorController controller, string pose)
        {
            var animatorController = controller as UnityEditor.Animations.AnimatorController;
            if (animatorController == null) return null;

            foreach (var layer in animatorController.layers)
            {
                foreach (var state in layer.stateMachine.states)
                {
                    if (MatchesPose(state.state.name, pose)) return state.state.motion as AnimationClip;
                }
            }

            return null;
        }

        private static bool MatchesPose(string stateName, string pose)
        {
            var name = stateName.ToLowerInvariant();
            return pose switch
            {
                "idle" => name.Contains("idle"),
                "attack" => name.Contains("attack") || name.Contains("spell") || name.Contains("shoot"),
                "hit" => name.Contains("hit") || name.Contains("pain") || name.Contains("damage"),
                "death" => name.Contains("death") || name.Contains("die"),
                _ => false,
            };
        }

        private static void Render(Camera camera, RenderTexture target, string key, string pose)
        {
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = null;

            var path = Path.Combine(OutRoot, key + "_" + pose + ".png").Replace('\\', '/');
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
