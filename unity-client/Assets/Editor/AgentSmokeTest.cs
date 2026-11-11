#if UNITY_EDITOR
using System;
using System.IO;
using Albion.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Editor-only smoke-тест для автономной проверки клиента: открывает Arena, входит в Play,
    /// ждёт первый снапшот от сервера и проверяет, что HUD и визуальные сущности созданы.
    /// Запуск: Unity -batchmode -projectPath unity-client -executeMethod Albion.Editor.AgentSmokeTest.Run.
    /// Не входит в плеер (папка Editor), поэтому на билд не влияет.
    /// </summary>
    public static class AgentSmokeTest
    {
        private const string StateKey = "Albion.AgentSmoke.Running";
        private const string ResultFile = "agent-smoke-result.txt";
        private static double _started;
        private static double _exitAt;
        private static int _exitCode;
        private static bool _contentChecksStarted;
        private static bool _contentChecksPassed;

        /// <summary>Точка входа для <c>-executeMethod</c>.</summary>
        public static void Run()
        {
            _contentChecksStarted = false;
            _contentChecksPassed = false;
            SessionState.SetInt(StateKey, 1);
            _started = EditorApplication.timeSinceStartup;
            EditorSceneManager.OpenScene("Assets/Scenes/Arena.unity");
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void RestoreAfterDomainReload()
        {
            if (SessionState.GetInt(StateKey, 0) == 1)
            {
                _started = EditorApplication.timeSinceStartup;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
        }

        private static void Tick()
        {
            if (SessionState.GetInt(StateKey, 0) == 2)
            {
                // Скриншот пишется в конце кадра: даём рендеру кадр-другой и выходим.
                if (EditorApplication.isPaused)
                {
                    EditorApplication.isPaused = false;
                }

                if (EditorApplication.timeSinceStartup >= _exitAt)
                {
                    SessionState.SetInt(StateKey, 0);
                    EditorApplication.update -= Tick;
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = false;
                    }

                    EditorApplication.Exit(_exitCode);
                }

                return;
            }

            if (SessionState.GetInt(StateKey, 0) != 1)
            {
                EditorApplication.update -= Tick;
                return;
            }

            var elapsed = EditorApplication.timeSinceStartup - _started;
            if (!EditorApplication.isPlaying)
            {
                if (elapsed > 10.0)
                {
                    Fail("Play mode не включился за 10 секунд");
                }

                return;
            }

            // URI loading is asynchronous: unpause before waiting for the pipeline, not after it.
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;

            var runtime = UnityEngine.Object.FindFirstObjectByType<ClientRuntime>();
            if (runtime == null || runtime.Pipeline == null)
            {
                if (elapsed > 30.0)
                {
                    Fail("ClientRuntime/ClientPipeline не создан за 30 секунд");
                }

                return;
            }

            // Batchmode без окна может не продвигать Time.deltaTime; фиксируем шаг, чтобы
            // сетевой конвейер (Hello-ретраи и тики) работал как в обычном Play.
            if (Time.captureDeltaTime <= 0f)
            {
                Time.captureDeltaTime = 1f / 60f;
            }

            // Ошибка индексатора Unity Search в batchmode включает Error Pause и замораживает Play;
            // для smoke-теста снимаем паузу, иначе кадры не идут и клиент не получает снапшоты.
            if (EditorApplication.isPaused)
            {
                EditorApplication.isPaused = false;
            }

            if (!_contentChecksStarted)
            {
                _contentChecksStarted = true;
                runtime.StartCoroutine(StreamingContentSmokeChecks.Run(Fail, () => _contentChecksPassed = true));
            }

            if (!_contentChecksPassed)
            {
                if (elapsed > 40.0) Fail("content URI regression checks timed out");
                return;
            }

            var pipeline = runtime.Pipeline;
            if (!pipeline.HasSession || pipeline.SnapshotsApplied < 5)
            {
                if (elapsed > 40.0)
                {
                    Fail("нет сессии/снапшотов: session=" + pipeline.HasSession
                        + " snapshots=" + pipeline.SnapshotsApplied
                        + " tick=" + pipeline.ClientTick
                        + " dt=" + Time.deltaTime.ToString("0.0000")
                        + " frames=" + Time.frameCount);
                }

                return;
            }

            var view = runtime.View;
            if (view == null)
            {
                Fail("UnityView не создан");
                return;
            }

            if (view.VisualEntityCount < 7)
            {
                Fail("визуальных сущностей меньше 7: " + view.VisualEntityCount);
                return;
            }

            if (UnityEngine.Object.FindFirstObjectByType<ClientHud>() == null)
            {
                Fail("ClientHud не создан");
                return;
            }

            var snapshot = pipeline.LastSnapshot;
            var mobs = 0;
            if (snapshot != null)
            {
                foreach (var entity in snapshot.Entities)
                {
                    if (pipeline.Content != null && pipeline.Content.FindMobByActorId(entity.ActorId) != null)
                    {
                        mobs++;
                    }
                }
            }

            if (mobs < 6)
            {
                Fail("в снапшоте меньше 6 мобов: " + mobs);
                return;
            }

            Pass("snapshots=" + pipeline.SnapshotsApplied
                + " deltas=" + pipeline.DeltasApplied
                + " visuals=" + view.VisualEntityCount
                + " mobs=" + mobs
                + " corpses=" + view.CorpseVisualCount);
        }

        private static void Pass(string message)
        {
            Debug.Log("[agent-smoke] PASS " + message);
            Finish("PASS " + message, 0);
        }

        private static void Fail(string reason)
        {
            Debug.LogError("[agent-smoke] FAIL " + reason);
            Finish("FAIL " + reason, 1);
        }

        private static void Finish(string result, int exitCode)
        {
            try
            {
                File.WriteAllText(Path.GetFullPath(ResultFile), result + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[agent-smoke] не удалось записать результат: " + exception.Message);
            }

            var screenshot = Environment.GetEnvironmentVariable("ALBION_SMOKE_SCREENSHOT");
            if (!string.IsNullOrEmpty(screenshot))
            {
                try
                {
                    ScreenCapture.CaptureScreenshot(screenshot);
                    Debug.Log("[agent-smoke] скриншот: " + screenshot);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[agent-smoke] скриншот не удался: " + exception.Message);
                }

                // Даём Unity записать PNG в конце кадра, затем выходим из Play и batchmode.
                _exitCode = exitCode;
                _exitAt = EditorApplication.timeSinceStartup + 1.5;
                SessionState.SetInt(StateKey, 2);
                return;
            }

            SessionState.SetInt(StateKey, 0);
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }

            EditorApplication.Exit(exitCode);
        }
    }
}
#endif
