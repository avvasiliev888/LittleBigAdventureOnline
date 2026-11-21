using System.Text;
using UnityEditor;
using Albion.Client;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Compares three ways of measuring a body's height, on a known-good mob and on the wizard.
    ///
    /// Why: the game divides Height by the measured native height to get a scale. On the wizard it
    /// measured 0,0197 m for a 2,25 m body and applied scale 124 — the model filled the screen with
    /// stretched polygons and the body itself became invisible. The knight measures correctly, so
    /// the formula is only wrong for a hierarchy that carries a non-unit scale.
    /// </summary>
    public static class MobMeasureDiag
    {
        private static readonly (string Label, string Path)[] Targets =
        {
            ("Knight", "Assets/Art/Characters/Generated/Knight.prefab"),
            ("WizardBody", "Assets/Art/Characters/Generated/WizardBody.prefab"),
        };

        [MenuItem("Albion/Mobs/Diagnose Body Measure")]
        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("########## MEASURE DIAG START ##########");

            foreach (var target in Targets)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target.Path);
                if (prefab == null)
                {
                    report.AppendLine(target.Label + ": PREFAB NOT FOUND");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                try
                {
                    report.AppendLine("=== " + target.Label + " root scale=" + instance.transform.localScale.ToString("0.000"));

                    report.AppendLine("  A bake(useScale=false)+TransformPoint = " + MeasureA(instance).ToString("0.0000"));
                    report.AppendLine("  B bake(useScale=true)+TransformPoint  = " + MeasureB(instance).ToString("0.0000"));
                    report.AppendLine("  C bake(useScale=true) raw to root     = " + MeasureC(instance).ToString("0.0000"));
                    report.AppendLine("  D renderer.bounds union               = " + MeasureRendererBounds(instance).ToString("0.0000"));

                    var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (skin != null)
                    {
                        report.AppendLine("  skin lossyScale=" + skin.transform.lossyScale.ToString("0.0000")
                            + " sharedMesh.bounds.size=" + skin.sharedMesh.bounds.size.ToString("0.00"));
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            // End-to-end: run the real game path (CharacterVisualSettings.Create) and measure what
            // it actually builds. A formula that looks right in isolation can still throw or
            // produce a wild scale once the settings are applied.
            var mobVisuals = AssetDatabase.LoadAssetAtPath<MobVisualSettings>(
                "Assets/Resources/Characters/MobVisuals.asset");
            if (mobVisuals != null && mobVisuals.Entries != null)
            {
                var host = new GameObject("MeasureHost").transform;
                try
                {
                    foreach (var entry in mobVisuals.Entries)
                    {
                        if (entry == null || entry.Prefab == null) continue;
                        try
                        {
                            var root = mobVisuals.Create(host, 1u, entry.MobKey);
                            if (root == null)
                            {
                                report.AppendLine("CREATE " + entry.MobKey + " -> null");
                                continue;
                            }

                            var has = false;
                            var bounds = new Bounds();
                            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                            {
                                if (!has) { bounds = renderer.bounds; has = true; }
                                else bounds.Encapsulate(renderer.bounds);
                            }

                            var calibration = root.Find("Character Facing/Character Calibration");
                            report.AppendLine("CREATE " + entry.MobKey
                                + " wanted=" + entry.Height.ToString("0.00")
                                + " worldHeight=" + (has ? bounds.size.y.ToString("0.000") : "-")
                                + " scale=" + (calibration != null
                                    ? calibration.localScale.x.ToString("0.0000") : "-"));

                            Object.DestroyImmediate(root.gameObject);
                        }
                        catch (System.Exception exception)
                        {
                            report.AppendLine("CREATE " + entry.MobKey + " THREW "
                                + exception.GetType().Name + ": " + exception.Message);
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(host.gameObject);
                }
            }

            report.AppendLine("########## MEASURE DIAG END ##########");
            Debug.Log(report.ToString());
        }

        /// <summary>The formula in CharacterVisualSettings.Measure: bake, then TransformPoint.</summary>
        private static float MeasureA(GameObject visual)
        {
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            var mesh = new Mesh();
            try
            {
                foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) continue;
                    skin.BakeMesh(mesh, false);
                    foreach (var vertex in mesh.vertices)
                    {
                        var point = visual.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        min = Vector3.Min(min, point);
                        max = Vector3.Max(max, point);
                    }

                    mesh.Clear();
                }
            }
            finally { Object.DestroyImmediate(mesh); }

            return max.y - min.y;
        }

        /// <summary>Bake WITH scale, then TransformPoint.</summary>
        private static float MeasureB(GameObject visual) => MeasureVariant(visual, true, true);

        /// <summary>Bake WITH scale, vertices treated as world-space already.</summary>
        private static float MeasureC(GameObject visual) => MeasureVariant(visual, true, false);

        private static float MeasureVariant(GameObject visual, bool useScale, bool transformPoint)
        {
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            var mesh = new Mesh();
            try
            {
                foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) continue;
                    skin.BakeMesh(mesh, useScale);
                    foreach (var vertex in mesh.vertices)
                    {
                        var point = transformPoint
                            ? visual.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex))
                            : visual.transform.InverseTransformPoint(vertex);
                        min = Vector3.Min(min, point);
                        max = Vector3.Max(max, point);
                    }
                    mesh.Clear();
                }
            }
            finally { Object.DestroyImmediate(mesh); }
            return max.y - min.y;
        }

        /// <summary>Bake, then treat the vertex as already in world space (no TransformPoint).</summary>
        private static float MeasureWorldBaked(GameObject visual)
        {
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            var mesh = new Mesh();
            try
            {
                foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) continue;
                    skin.BakeMesh(mesh, false);
                    foreach (var vertex in mesh.vertices)
                    {
                        var point = visual.transform.InverseTransformPoint(vertex);
                        min = Mathf.Min(min, point.y);
                        max = Mathf.Max(max, point.y);
                    }

                    mesh.Clear();
                }
            }
            finally { Object.DestroyImmediate(mesh); }

            return max - min;
        }

        /// <summary>Union of renderer world bounds, converted into the root's space.</summary>
        private static float MeasureRendererBounds(GameObject visual)
        {
            var has = false;
            var bounds = new Bounds();
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!has) { bounds = renderer.bounds; has = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!has) return 0f;
            var min = visual.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            var max = visual.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
            return max.y - min.y;
        }
    }
}
