using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// M10: две сборки Windows-плеера — desktop и mobile. Профиль выбирается в рантайме
    /// (<c>QualityProfile</c>), а дефолт сборки задаётся символом <c>ALBION_MOBILE_DEFAULT</c>,
    /// поэтому «оба профиля собираются» проверяется двумя реальными билдами.
    /// </summary>
    public static class AlbionBuild
    {
        private const string DesktopDefines = "";
        private const string MobileDefines = "ALBION_MOBILE_DEFAULT";

        /// <summary>Сборка desktop-профиля (меню).</summary>
        [MenuItem("Albion/Build/Desktop")]
        public static void BuildDesktop()
        {
            Build("desktop", DesktopDefines);
        }

        /// <summary>Сборка mobile-профиля (меню).</summary>
        [MenuItem("Albion/Build/Mobile")]
        public static void BuildMobile()
        {
            Build("mobile", MobileDefines);
        }

        /// <summary>Точка входа для batchmode: desktop.</summary>
        public static void BuildDesktopBatch()
        {
            BuildDesktop();
        }

        /// <summary>Точка входа для batchmode: mobile.</summary>
        public static void BuildMobileBatch()
        {
            BuildMobile();
        }

        private static void Build(string profile, string defines)
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, defines);

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                scenes = new[] { AlbionWorldBuilder.ScenePath };
            }

            var location = "Builds/" + profile + "/AlbionPrototype.exe";
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = location,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            // Символ — только для этой сборки: в проекте снова пустые defines, иначе редактор
            // и следующий билд по умолчанию уезжают в mobile-профиль.
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, string.Empty);
            Debug.Log("M10 build " + profile + ": result=" + summary.result
                + ", size=" + summary.totalSize
                + ", errors=" + summary.totalErrors
                + ", path=" + location);

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("M10: сборка " + profile + " провалена (" + summary.result + ").");
            }
        }
    }
}
