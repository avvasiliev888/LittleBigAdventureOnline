using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Albion.EditorTools
{
    /// <summary>
    /// Инициализация пустого Unity-проекта (M1): создаёт стартовую сцену с камерой и светом.
    /// Запускается из CLI: Unity -batchmode -quit -projectPath unity-client -executeMethod Albion.EditorTools.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string ScenePath = ScenesFolder + "/SampleScene.unity";

        /// <summary>Создаёт сцену, если её нет, и прописывает её в build settings.</summary>
        public static void Run()
        {
            Directory.CreateDirectory(ScenesFolder);

            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                var cameraObject = new GameObject("Main Camera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0f, 8f, -8f);
                cameraObject.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
                camera.clearFlags = CameraClearFlags.Skybox;

                var lightObject = new GameObject("Directional Light");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("ProjectSetup: сцена " + ScenePath + " готова, build settings обновлены.");
        }

        /// <summary>
        /// Собирает Windows-плеер в Builds/windows (каталог игнорируется git).
        /// Используется для проверки Gate M1 «Unity-проект собирается» из CLI:
        /// Unity -batchmode -quit -projectPath unity-client -executeMethod Albion.EditorTools.ProjectSetup.BuildWindowsPlayer
        /// </summary>
        public static void BuildWindowsPlayer()
        {
            Run();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/windows/AlbionPrototype.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log("BuildWindowsPlayer: result=" + summary.result
                      + ", errors=" + summary.totalErrors
                      + ", size=" + summary.totalSize + " bytes"
                      + ", output=" + summary.outputPath);

            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
