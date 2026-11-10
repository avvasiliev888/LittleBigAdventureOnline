using System;
using System.Collections.Generic;
using System.IO;
using Albion.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Albion.Editor
{
    /// <summary>Explicit GPU acceptance probe; never runs on import or editor startup.</summary>
    public static class AlbionRenderProbe
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const int Epsilon = 3;
        private const int MinimumChangedPixels = Width * Height / 1000;

        [Serializable]
        private sealed class Comparison
        {
            public string name;
            public int changedPixels;
            public double meanAbsoluteRgbDifference;
            public int requiredChangedPixels;
            public int darkerGroundPixels;
            public bool passed;
        }

        [Serializable]
        private sealed class Report
        {
            public string unityVersion;
            public string graphicsDevice;
            public string graphicsApi;
            public int width = Width;
            public int height = Height;
            public int epsilon = Epsilon;
            public float fogDensity;
            public string fogMode;
            public string cameraView = "CameraFollow default offset (0,10,-8), target origin";
            public bool passed;
            public string error;
            public List<Comparison> comparisons = new List<Comparison>();
        }

        [MenuItem("Albion/Rendering/Capture and Test Arena")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Leave Play Mode before capturing.");
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                // Batch startup may contain Unity's unsaved default scene; no operator owns it.
                if (Application.isBatchMode && string.IsNullOrEmpty(scene.path)) continue;
                Require(!scene.isDirty && !string.IsNullOrEmpty(scene.path),
                    "Save every open scene before capturing; the probe will not save or discard user changes.");
            }

            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                "A real GPU is required. Do not use -nographics.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var originalPipeline = GraphicsSettings.defaultRenderPipeline;
            var originalQualityPipeline = QualitySettings.renderPipeline;
            var originalShadowDistance = QualitySettings.shadowDistance;
            var originalAa = QualitySettings.antiAliasing;
            var originalVsync = QualitySettings.vSyncCount;
            var originalTargetFps = Application.targetFrameRate;
            var originalActiveRt = RenderTexture.active;
            GameObject driver = null;
            RenderTexture target = null;
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/visual-check"));
            Directory.CreateDirectory(output);
            var report = new Report
            {
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            };

            try
            {
                var arena = EditorSceneManager.OpenScene(AlbionWorldBuilder.ScenePath, OpenSceneMode.Single);
                var camera = Camera.main;
                Require(camera != null && camera.gameObject.scene == arena, "Arena has no Main Camera.");
                ValidateScene(arena, camera, out var sun, out var volume);
                report.fogDensity = RenderSettings.fogDensity;
                report.fogMode = RenderSettings.fogMode.ToString();

                // Same settled view as the runtime CameraFollow, without entering Play Mode.
                camera.transform.position = new Vector3(0f, 10f, -8f);
                camera.transform.LookAt(Vector3.zero);
                driver = new GameObject("Arena Render Probe (transient)") { hideFlags = HideFlags.HideAndDontSave };
                AlbionCharacterSetup.Validate();
                var entities = new GameObject("Entities (snapshot-driven probe)");
                entities.transform.SetParent(driver.transform, false);
                var view = new UnityView(entities.transform) { PlayerEntityId = 1 };
                view.ApplySnapshot(new Simulation.Core.SimulationSnapshot(1, new[]
                {
                    ProbeEntity(1, 0f, 0f), ProbeEntity(2, 3f, 2f),
                }));
                view.Render(0f);
                var profile = driver.AddComponent<QualityProfile>();
                profile.ApplyFromEnvironment(volume);
                profile.Apply(QualityProfile.Mode.Desktop);
                var cameraData = camera.GetUniversalAdditionalCameraData();
                Require(cameraData.renderPostProcessing && volume.weight > 0f, "Desktop post-processing is disabled.");
                Require(GraphicsSettings.currentRenderPipeline == Resources.Load<UniversalRenderPipelineAsset>("Rendering/UrpDesktop"),
                    "Effective pipeline is not desktop; check per-quality pipeline overrides.");

                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "Arena GPU probe",
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                };
                Require(target.Create(), "Cannot create GPU render target.");
                var full = Capture(camera, target, output, "desktop-full");
                entities.SetActive(false);
                Compare(report, "desktop-character-visible", full,
                    Capture(camera, target, output, "desktop-no-characters"), MinimumChangedPixels);
                entities.SetActive(true);
                var repeat = Capture(camera, target, output, "desktop-repeat");
                var noise = CountChanged(full, repeat, out _);
                var minimum = Math.Max(MinimumChangedPixels, noise * 5);
                Require(noise < Width * Height / 100, "Repeated baseline is unstable; disable animated content before probing.");

                var fog = RenderSettings.fog;
                try
                {
                    RenderSettings.fog = false;
                    Compare(report, "desktop-fog", full, Capture(camera, target, output, "desktop-fog-off"), minimum);
                }
                finally { RenderSettings.fog = fog; }

                var shadows = sun.shadows;
                try
                {
                    sun.shadows = LightShadows.None;
                    var noShadows = Capture(camera, target, output, "desktop-shadows-off");
                    Compare(report, "desktop-shadows", full, noShadows, minimum);
                    var groundMask = BuildGroundMask(arena, camera);
                    var comparison = report.comparisons[report.comparisons.Count - 1];
                    for (var pixel = 0; pixel < full.Length; pixel++)
                    {
                        var litLuma = noShadows[pixel].r * 0.2126f + noShadows[pixel].g * 0.7152f + noShadows[pixel].b * 0.0722f;
                        var shadowLuma = full[pixel].r * 0.2126f + full[pixel].g * 0.7152f + full[pixel].b * 0.0722f;
                        if (groundMask[pixel] && litLuma - shadowLuma >= Epsilon) comparison.darkerGroundPixels++;
                    }
                    comparison.passed &= comparison.darkerGroundPixels >= 100;
                }
                finally { sun.shadows = shadows; }

                var post = cameraData.renderPostProcessing;
                try
                {
                    cameraData.renderPostProcessing = false;
                    Compare(report, "desktop-post", full, Capture(camera, target, output, "desktop-post-off"), minimum);
                }
                finally { cameraData.renderPostProcessing = post; }

                var rotation = sun.transform.rotation;
                try
                {
                    sun.transform.rotation = Quaternion.Euler(12f, 110f, 0f);
                    Compare(report, "desktop-light-direction", full, Capture(camera, target, output, "desktop-light-changed"), minimum);
                }
                finally { sun.transform.rotation = rotation; }

                profile.Apply(QualityProfile.Mode.Mobile);
                Require(GraphicsSettings.currentRenderPipeline == Resources.Load<UniversalRenderPipelineAsset>("Rendering/UrpMobile"),
                    "Effective pipeline is not mobile.");
                Require(!cameraData.renderPostProcessing && volume.weight == 0f, "Mobile post-processing is enabled.");
                var mobile = Capture(camera, target, output, "mobile-full");
                entities.SetActive(false);
                Compare(report, "mobile-character-visible", mobile,
                    Capture(camera, target, output, "mobile-no-characters"), MinimumChangedPixels);
                entities.SetActive(true);
                var mobileRepeat = Capture(camera, target, output, "mobile-repeat");
                var mobileNoise = CountChanged(mobile, mobileRepeat, out _);
                Require(mobileNoise < Width * Height / 100, "Mobile repeated baseline is unstable.");
                try
                {
                    RenderSettings.fog = false;
                    Compare(report, "mobile-fog", mobile, Capture(camera, target, output, "mobile-fog-off"),
                        Math.Max(MinimumChangedPixels, mobileNoise * 5));
                }
                finally { RenderSettings.fog = fog; }

                camera.transform.position = new Vector3(0f, 2.6f, -4f);
                camera.transform.LookAt(new Vector3(0f, 0.95f, 0f));
                Capture(camera, target, output, "warrior-mobile-close");
                CaptureAnimation(report, view.Player, camera, target, output, "mobile");
                profile.Apply(QualityProfile.Mode.Desktop);
                Capture(camera, target, output, "warrior-desktop-close");
                CaptureAnimation(report, view.Player, camera, target, output, "desktop");
                report.passed = report.comparisons.TrueForAll(comparison => comparison.passed);
                File.WriteAllText(Path.Combine(output, "metrics.json"), JsonUtility.ToJson(report, true));
                Require(report.passed, "Arena GPU differential checks failed. Inspect PNGs and metrics.json in " + output);
                Debug.Log("Arena GPU differential checks passed: " + output);
            }
            catch (Exception exception)
            {
                report.passed = false;
                report.error = exception.ToString();
                File.WriteAllText(Path.Combine(output, "metrics.json"), JsonUtility.ToJson(report, true));
                throw;
            }
            finally
            {
                RenderTexture.active = originalActiveRt;
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
                if (driver != null) UnityEngine.Object.DestroyImmediate(driver);
                GraphicsSettings.defaultRenderPipeline = originalPipeline;
                QualitySettings.renderPipeline = originalQualityPipeline;
                QualitySettings.shadowDistance = originalShadowDistance;
                QualitySettings.antiAliasing = originalAa;
                QualitySettings.vSyncCount = originalVsync;
                Application.targetFrameRate = originalTargetFps;
                // Force-close the transient copy even when Arena was originally open;
                // restoring an identical scene setup alone can reuse its modified objects.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // A batch startup default scene has no path and cannot be restored by this API.
                if (!Application.isBatchMode || (setup.Length > 0
                    && Array.TrueForAll(setup, item => !string.IsNullOrEmpty(item.path))
                    && Array.Exists(setup, item => item.isLoaded && item.isActive)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        private static void CaptureAnimation(Report report, Transform player, Camera camera, RenderTexture target,
            string output, string profile)
        {
            var motion = player.GetComponent<WarriorVisual>();
            var animator = motion.Animator;
            var origin = player.position;
            motion.SetMotion(Vector3.zero, false);
            animator.Play("Idle", 0, 0f);
            animator.Update(0f);
            var idle = Capture(camera, target, output, "warrior-" + profile + "-idle");
            motion.SetMotion(Vector3.forward * 4f, false);
            animator.Update(0.01f);
            animator.Update(0.2f);
            animator.Play("Locomotion", 0, 0f);
            animator.Update(0f);
            var first = Capture(camera, target, output, "warrior-" + profile + "-run-00");
            animator.Update(0.17f);
            var second = Capture(camera, target, output, "warrior-" + profile + "-run-01");
            Compare(report, profile + "-run-pose-advances", first, second, 100);
            animator.Update(0.17f);
            Capture(camera, target, output, "warrior-" + profile + "-run-02");
            // Full cycle and facing sequence for human review (actual GPU frames, no synthetic poses).
            motion.SetMotion(Vector3.back * 4f, false);
            animator.Play("Locomotion", 0, 0f);
            animator.Update(0f);
            for (var frame = 0; frame < 30; frame++)
            {
                animator.Update(1f / 30f);
                Capture(camera, target, output, "warrior-" + profile + "-cycle-" + frame.ToString("D2"));
            }
            motion.SetMotion(Vector3.back * 1.5f, false);
            animator.Update(0.1f);
            Capture(camera, target, output, "warrior-" + profile + "-walk");
            motion.SetMotion(Vector3.right * 4f, false);
            animator.Update(0.1f);
            Capture(camera, target, output, "warrior-" + profile + "-right");
            motion.SetMotion(Vector3.left * 4f, false);
            animator.Update(0.1f);
            Capture(camera, target, output, "warrior-" + profile + "-left");
            motion.SetMotion(Vector3.zero, false);
            animator.Update(0.01f);
            animator.Update(0.2f);
            Capture(camera, target, output, "warrior-" + profile + "-stopped");
            // Combat frames prove Attack/Death are wired to the same live Animator as locomotion.
            motion.PlayAttack();
            animator.Update(0.05f);
            var attackFirst = Capture(camera, target, output, "warrior-" + profile + "-attack-00");
            animator.Update(0.15f);
            var attackSecond = Capture(camera, target, output, "warrior-" + profile + "-attack-01");
            Compare(report, profile + "-attack-pose-advances", attackFirst, attackSecond, 100);
            motion.SetMotion(Vector3.zero, true);
            animator.Update(0.4f);
            var death = Capture(camera, target, output, "warrior-" + profile + "-death");
            Compare(report, profile + "-death-pose-changes", idle, death, 100);
            motion.SetMotion(Vector3.zero, false);
            animator.Update(0.2f);
            Require(player.position == origin && !animator.applyRootMotion,
                "Animation capture moved the network root or enabled root motion.");
            motion.Facing.localRotation = Quaternion.identity;
            animator.Play("Idle", 0, 0f);
            animator.Update(0f);
        }

        private static Simulation.Core.EntitySnapshot ProbeEntity(uint id, float x, float z) =>
            new Simulation.Core.EntitySnapshot(id, 1, new System.Numerics.Vector3(x, 0f, z),
                System.Numerics.Vector3.Zero, 100, 100, 100, 100, 0, 0, false);

        private static void ValidateScene(Scene scene, Camera camera, out Light sun, out Volume volume)
        {
            Require(RenderSettings.fog && RenderSettings.fogDensity > 0f, "Arena fog is disabled.");
            sun = null;
            volume = null;
            var materialCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var light in root.GetComponentsInChildren<Light>())
                    if (light.enabled && light.type == LightType.Directional && (sun == null || light.intensity > sun.intensity)) sun = light;
                foreach (var candidate in root.GetComponentsInChildren<Volume>())
                    if (candidate.enabled && candidate.isGlobal) volume = candidate;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    Require(renderer.shadowCastingMode != ShadowCastingMode.Off && renderer.receiveShadows,
                        "Arena renderer must cast and receive shadows: " + renderer.name);
                    foreach (var material in renderer.sharedMaterials)
                    {
                        Require(material != null && material.shader != null, "Missing Arena material.");
                        var expected = renderer.gameObject.name == "Ground" ? "Albion/Toon Ground" : "Albion/Toon Lit";
                        Require(material.shader.name == expected && !ShaderUtil.ShaderHasError(material.shader),
                            "Incorrect or broken toon shader on " + renderer.name);
                        Require(material.FindPass("ShadowCaster") >= 0 && material.FindPass("DepthOnly") >= 0,
                            "Missing shadow/depth pass on " + material.name);
                        materialCount++;
                    }
                }
            }

            Require(materialCount > 0, "Arena contains no mesh materials.");
            Require(sun != null && sun.intensity > 0f && sun.shadows != LightShadows.None && sun.shadowStrength > 0f,
                "Arena sun cannot cast visible shadows.");
            Require(volume != null && volume.weight > 0f
                && volume.sharedProfile == Resources.Load<VolumeProfile>("Rendering/AlbionPostProcess"),
                "Arena global volume/profile is missing.");
            Require(volume.sharedProfile.components.Count >= 4
                && !volume.sharedProfile.components.Exists(component => component == null), "Volume has missing components.");
            Require(camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.renderPostProcessing,
                "Generated Arena camera must explicitly enable URP post-processing.");
            Require((data.volumeLayerMask.value & (1 << volume.gameObject.layer)) != 0, "Camera cannot see the volume layer.");
        }

        private static Color32[] Capture(Camera camera, RenderTexture target, string output, string name)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "Active SRP does not support StandardRequest.");
            // Warm volume/renderer state after each isolated change, then capture the settled frame.
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);
            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = target;
                image = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                image.Apply(false, false);
                var pixels = image.GetPixels32();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
                var magenta = 0;
                var visible = 0;
                foreach (var pixel in pixels)
                {
                    if (pixel.r > 220 && pixel.b > 220 && pixel.g < 45) magenta++;
                    if (pixel.r > 8 || pixel.g > 8 || pixel.b > 8) visible++;
                }
                Require(magenta < 10, "Error-shader magenta in " + name);
                Require(visible > pixels.Length / 10, "Empty/black render in " + name);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static bool[] BuildGroundMask(Scene scene, Camera camera)
        {
            Bounds ground = default;
            var blockers = new List<Bounds>();
            var foundGround = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.gameObject.name == "Ground")
                    {
                        ground = renderer.bounds;
                        foundGround = true;
                    }
                    else
                    {
                        // Conservative bounds exclude geometry silhouettes, including outlines.
                        var bounds = renderer.bounds;
                        bounds.Expand(0.15f);
                        blockers.Add(bounds);
                    }
                }
            }

            Require(foundGround, "Cannot locate the actual ground receiver.");
            var mask = new bool[Width * Height];
            var plane = new Plane(Vector3.up, new Vector3(0f, ground.center.y, 0f));
            var previousAspect = camera.aspect;
            try
            {
                camera.aspect = (float)Width / Height;
                for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    var ray = camera.ViewportPointToRay(new Vector3((x + 0.5f) / Width, (y + 0.5f) / Height, 0f));
                    if (!plane.Raycast(ray, out var distance) || distance > camera.farClipPlane) continue;
                    var point = ray.GetPoint(distance);
                    if (point.x <= ground.min.x || point.x >= ground.max.x || point.z <= ground.min.z || point.z >= ground.max.z) continue;
                    var blocked = false;
                    foreach (var bounds in blockers)
                    {
                        if (bounds.IntersectRay(ray, out var hit) && hit < distance)
                        {
                            blocked = true;
                            break;
                        }
                    }
                    mask[y * Width + x] = !blocked;
                }
            }
            finally { camera.aspect = previousAspect; }
            return mask;
        }

        private static void Compare(Report report, string name, Color32[] baseline, Color32[] variant, int minimum)
        {
            var changed = CountChanged(baseline, variant, out var mean);
            report.comparisons.Add(new Comparison
            {
                name = name,
                changedPixels = changed,
                meanAbsoluteRgbDifference = mean,
                requiredChangedPixels = minimum,
                passed = changed >= minimum,
            });
        }

        private static int CountChanged(Color32[] baseline, Color32[] variant, out double mean)
        {
            var count = 0;
            long sum = 0;
            for (var index = 0; index < baseline.Length; index++)
            {
                var r = Math.Abs(baseline[index].r - variant[index].r);
                var g = Math.Abs(baseline[index].g - variant[index].g);
                var b = Math.Abs(baseline[index].b - variant[index].b);
                sum += r + g + b;
                if (Math.Max(r, Math.Max(g, b)) >= Epsilon) count++;
            }
            mean = sum / (baseline.Length * 3.0);
            return count;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Arena render probe: " + message);
        }
    }
}
