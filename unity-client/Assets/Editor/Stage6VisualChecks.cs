using System;
using System.IO;
using Albion.Client;
using Client.Network;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Albion.Editor
{
    /// <summary>GPU gallery of real generated regions; never mutates simulation or network entities.</summary>
    public static class Stage6VisualChecks
    {
        public static void VerifyAndCapture(ClientContent content)
        {
            foreach (var name in new[]
            {
                "Albion/Toon Lit", "Albion/Toon Ground", "Albion/Toon Outline",
                "Albion/Foliage", "Albion/Stylized Water", "Albion/Gradient Sky",
            })
            {
                var shader = Shader.Find(name);
                if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                {
                    throw new InvalidOperationException("Stage6 shader missing, unsupported or has errors: " + name);
                }
            }

            var mobVisuals = MobVisualSettings.Load();
            if (mobVisuals == null)
            {
                throw new InvalidOperationException("Stage6 mob visual settings are missing.");
            }

            mobVisuals.ValidateMaterials();

            if (UnityEngine.Object.FindObjectsByType<InstancedMeadow>(FindObjectsSortMode.None).Length
                != content.Config.Zones.Zones.Count)
            {
                throw new InvalidOperationException("Stage6 region meadow count differs from config");
            }

            foreach (var zone in content.Config.Zones.Zones)
            {
                if (zone.Width < 300f || zone.Depth < 300f)
                {
                    throw new InvalidOperationException("Stage6 zone is smaller than the agreed 320-metre scale: " + zone.Key);
                }

                var region = GameObject.Find("Region " + zone.Key);
                var renderers = region == null ? 0 : region.GetComponentsInChildren<Renderer>().Length;
                if (renderers < 40)
                {
                    throw new InvalidOperationException("Stage6 region has too few props: " + zone.Key + "=" + renderers);
                }

                Debug.Log("[agent-smoke] region " + zone.Key + " renderers=" + renderers);
            }

            // Shared production rule: outside the gate the border blocks, inside it opens.
            var zones = content.Config.Zones;
            var blocked = zones.ResolveMovement(
                new System.Numerics.Vector3(157f, 0f, 60f), new System.Numerics.Vector3(163f, 0f, 60f));
            if (blocked.X > 160f)
            {
                throw new InvalidOperationException("Stage6 gate rule failed: border crossed outside the gateway");
            }

            var allowed = zones.ResolveMovement(
                new System.Numerics.Vector3(157f, 0f, 0f), new System.Numerics.Vector3(163f, 0f, 0f));
            if (allowed.X <= 160f)
            {
                throw new InvalidOperationException("Stage6 gate rule failed: gateway blocked a legal crossing");
            }

            var folder = Environment.GetEnvironmentVariable("ALBION_STAGE6_GALLERY");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);
            var camera = Camera.main;
            var atmosphere = UnityEngine.Object.FindFirstObjectByType<ZoneAtmosphere>();
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var target = camera.targetTexture;
            var far = camera.farClipPlane;
            var fog = RenderSettings.fog;
            var oldActive = RenderTexture.active;
            var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                camera.farClipPlane = 900f;
                foreach (var zone in content.Config.Zones.Zones)
                {
                    // Each region has a guaranteed landmark grove at its centre (Z=40), so frame that.
                    var focus = new Vector3((zone.MinX + zone.MaxX) * 0.5f, 7f, 40f);
                    atmosphere.PreviewAt(focus);
                    camera.transform.position = focus + new Vector3(0f, 32f, -58f);
                    camera.transform.LookAt(focus);
                    Capture(camera, rt, texture, Path.Combine(folder, zone.Key + ".png"));
                }

                // A real gate close-up proves the pass is open while the rest of the border is a wall.
                var border = content.Config.Zones.Zones[1].MinX;
                atmosphere.PreviewAt(new Vector3(border - 20f, 0f, 0f));
                camera.transform.position = new Vector3(border - 20f, 15f, -24f);
                camera.transform.LookAt(new Vector3(border, 4f, 0f));
                Capture(camera, rt, texture, Path.Combine(folder, "gate.png"));

                // Close ground-level frame: proves individual props exist in the crimson region.
                atmosphere.PreviewAt(new Vector3(560f, 0f, -10f));
                camera.transform.position = new Vector3(560f, 6f, -30f);
                camera.transform.LookAt(new Vector3(560f, 2f, -10f));
                Capture(camera, rt, texture, Path.Combine(folder, "red-close.png"));

                // Eye-level sky frame: the crimson gradient must be visible above the ash ground.
                camera.transform.position = new Vector3(560f, 6f, -8f);
                camera.transform.LookAt(new Vector3(560f, 34f, 70f));
                Capture(camera, rt, texture, Path.Combine(folder, "red-sky.png"));

                // Natural border close-up and a ground-level tree frame for volume checks.
                atmosphere.PreviewAt(new Vector3(140f, 0f, 40f));
                camera.transform.position = new Vector3(140f, 8f, 40f);
                camera.transform.LookAt(new Vector3(170f, 9f, 40f));
                Capture(camera, rt, texture, Path.Combine(folder, "border.png"));

                atmosphere.PreviewAt(new Vector3(0f, 0f, 40f));
                camera.transform.position = new Vector3(0f, 3.2f, 31f);
                camera.transform.LookAt(new Vector3(0f, 4.5f, 40f));
                Capture(camera, rt, texture, Path.Combine(folder, "flora.png"));

                // Overview uses no fog so the three 320x320 regions and their walled gateways stay visible.
                atmosphere.PreviewAt(new Vector3(320f, 0f, 0f));
                RenderSettings.fog = false;
                camera.transform.position = new Vector3(320f, 360f, -360f);
                camera.transform.LookAt(new Vector3(320f, 0f, 60f));
                Capture(camera, rt, texture, Path.Combine(folder, "overview.png"));
                Debug.Log("[agent-smoke] Stage6 shaders/regions/gate/overview GPU gallery PASS: " + folder);
            }
            finally
            {
                RenderSettings.fog = fog;
                atmosphere.PreviewAt(position);
                camera.farClipPlane = far;
                camera.targetTexture = target;
                camera.transform.SetPositionAndRotation(position, rotation);
                RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(texture);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static void Capture(Camera camera, RenderTexture rt, Texture2D texture, string path)
        {
            foreach (var meadow in UnityEngine.Object.FindObjectsByType<InstancedMeadow>(FindObjectsSortMode.None))
            {
                meadow.DrawFor(camera);
            }

            // Warm a complete URP frame before reading back explicit off-screen camera state.
            camera.Render();
            camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
    }
}
