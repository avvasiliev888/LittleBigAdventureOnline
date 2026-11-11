#if UNITY_EDITOR
using System;
using System.Diagnostics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Albion.Editor
{
    /// <summary>
    /// GPU/static-batching acceptance checks for Stage 6. Runs inside the Play-mode smoke
    /// gate, renders a real off-screen frame and reports draw calls plus render throughput.
    /// </summary>
    public static class Stage6PerformanceChecks
    {
        private const int DesktopDrawCallBudget = 500;
        private const int MobileDrawCallBudget = 200;
        private const string DrawCallMarker = "Draw Calls Count";
        private static ProfilerRecorder _drawCalls;
        private static bool _combined;

        /// <summary>Start sampling early in Play mode so the counter has frames by acceptance time.</summary>
        public static void Begin()
        {
            if (_drawCalls.Valid) return;
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, DrawCallMarker);
        }

        /// <summary>Release the profiler handle without touching game objects.</summary>
        public static void End()
        {
            if (_drawCalls.Valid) _drawCalls.Dispose();
            _drawCalls = default;
        }

        public static void Verify(Camera camera)
        {
            if (camera == null) throw new InvalidOperationException("Stage6 perf: no main camera");
            Begin();

            var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            var lodGroups = UnityEngine.Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None);
            var staticRenderers = 0;
            foreach (var renderer in renderers)
            {
                if ((GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & StaticEditorFlags.BatchingStatic) != 0)
                {
                    staticRenderers++;
                }
            }

            if (renderers.Length < 500) throw new InvalidOperationException("Stage6 perf: too few staged props: " + renderers.Length);
            if (lodGroups.Length < 100) throw new InvalidOperationException("Stage6 perf: LOD groups missing: " + lodGroups.Length);
            if (staticRenderers < renderers.Length * 0.9f)
            {
                throw new InvalidOperationException("Stage6 perf: static batching flags below 90%: " + staticRenderers + "/" + renderers.Length);
            }

            camera.Render();
            // Mirror the player's build-time static batching; Editor Play mode does not apply it automatically.
            if (!_combined)
            {
                _combined = true;
                var ground = GameObject.Find("Ground");
                if (ground != null)
                {
                    try
                    {
                        StaticBatchingUtility.Combine(ground);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[agent-smoke] perf static combine: " + exception.Message);
                    }
                }
            }

            camera.Render();
            var drawCalls = _drawCalls.Valid && _drawCalls.Count > 0 ? _drawCalls.LastValue : -1;
            var fps = MeasureRenderThroughput(camera);
            var mobileBudget = Application.isMobilePlatform
                || string.Equals(Environment.GetEnvironmentVariable("ALBION_QUALITY"), "mobile", StringComparison.OrdinalIgnoreCase);
            Debug.Log("[agent-smoke] perf renderers=" + renderers.Length
                + " static=" + staticRenderers
                + " lodGroups=" + lodGroups.Length
                + " drawCalls=" + (drawCalls < 0 ? "n/a" : drawCalls.ToString())
                + " renderFps=" + fps.ToString("0.0")
                + " profile=" + (mobileBudget ? "mobile" : "desktop")
                + " budgets=" + DesktopDrawCallBudget + "/" + MobileDrawCallBudget);

            if (drawCalls > DesktopDrawCallBudget)
            {
                throw new InvalidOperationException("Stage6 perf: desktop draw calls " + drawCalls + " exceed " + DesktopDrawCallBudget);
            }

            if (mobileBudget && drawCalls > MobileDrawCallBudget)
            {
                throw new InvalidOperationException("Stage6 perf: mobile draw calls " + drawCalls + " exceed " + MobileDrawCallBudget);
            }

            if (fps < 30f)
            {
                throw new InvalidOperationException("Stage6 perf: render throughput below 30 FPS: " + fps.ToString("0.0"));
            }
        }

        private static float MeasureRenderThroughput(Camera camera)
        {
            var target = camera.targetTexture;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                camera.Render();
                var watch = Stopwatch.StartNew();
                const int frames = 20;
                for (var i = 0; i < frames; i++)
                {
                    camera.Render();
                }

                watch.Stop();
                return (float)(frames / Math.Max(0.0001, watch.Elapsed.TotalSeconds));
            }
            finally
            {
                camera.targetTexture = target;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }
    }
}
#endif
