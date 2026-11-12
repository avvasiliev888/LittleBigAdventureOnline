using System;
using System.IO;
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
        private const string Version = "0.8.5";
        private const int AndroidVersionCode = 11;

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

        /// <summary>Сборка Android APK для телефона (меню).</summary>
        [MenuItem("Albion/Build/Android")]
        public static void BuildAndroid()
        {
            BuildAndroidInternal();
        }

        /// <summary>Точка входа для batchmode: Android APK.</summary>
        public static void BuildAndroidBatch()
        {
            BuildAndroidInternal();
            EditorApplication.Exit(0);
        }

        /// <summary>Точка входа для batchmode: desktop.</summary>
        public static void BuildDesktopBatch()
        {
            BuildDesktop();
            EditorApplication.Exit(0);
        }

        /// <summary>Точка входа для batchmode: mobile.</summary>
        public static void BuildMobileBatch()
        {
            BuildMobile();
            EditorApplication.Exit(0);
        }

        private static void Build(string profile, string defines)
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.bundleVersion = Version;
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

        private static void BuildAndroidInternal()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, MobileDefines);

            PlayerSettings.productName = "Albion Prototype";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.albion.prototype");
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = AndroidVersionCode;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            ConfigureAndroidSdk();

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                scenes = new[] { AlbionWorldBuilder.ScenePath };
            }

            var location = "Builds/android/AlbionPrototype.apk";
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = location,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, string.Empty);
            Debug.Log("Android build: result=" + summary.result
                + ", size=" + summary.totalSize
                + ", errors=" + summary.totalErrors
                + ", path=" + location);

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Android: сборка провалена (" + summary.result + ").");
            }
        }

        // Модуль Android из Hub кладёт SDK/NDK/JDK рядом с Editor; если пути ещё не заданы,
        // прописываем их, иначе batchmode-сборка не находит инструменты.
        private static void ConfigureAndroidSdk()
        {
            var androidRoot = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer");
            if (!Directory.Exists(androidRoot))
            {
                return;
            }

            var sdk = Path.Combine(androidRoot, "SDK");
            var ndk = Path.Combine(androidRoot, "NDK");
            var jdk = Path.Combine(androidRoot, "OpenJDK");
            if (Directory.Exists(sdk))
            {
                EditorPrefs.SetString("AndroidSdkRoot", sdk);
            }

            if (Directory.Exists(ndk))
            {
                EditorPrefs.SetString("AndroidNdkRoot", ndk);
            }

            if (Directory.Exists(jdk))
            {
                EditorPrefs.SetString("JdkPath", jdk);
            }
        }
    }
}
