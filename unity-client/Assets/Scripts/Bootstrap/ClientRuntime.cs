using System;
using System.IO;
using Client.Network;
using Shared;
using Simulation.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Albion.Client
{
    /// <summary>
    /// Точка входа Unity-клиента (M6, M7): создаёт мир, транспорт, конвейер клиента и крутит кадры.
    /// С M7 клиент предсказывает движение игрока и реконсилирует его по ServerAck; в HUD видно
    /// Скрипт запускается сам после загрузки сцены, поэтому отдельная сцена/префаб не нужны.
    /// Настройки берутся из переменных окружения ALBION_SERVER_HOST / ALBION_SERVER_PORT
    /// или из аргументов командной строки -serverHost / -serverPort.
    /// </summary>
    public sealed class ClientRuntime : MonoBehaviour
    {
        private const string DefaultHost = "127.0.0.1";
        private const int DefaultPort = 9050;

        private LiteNetLibTransport _transport;
        private ClientContent _content;
        private ClientPipeline _pipeline;
        private QualityProfile _quality;
        private float _fpsSmoothed;
        private float _fpsLogTimer;
        private float _fpsLogSum;
        private int _fpsLogFrames;
        private UnityView _view;
        private UnityInputSource _input;
        private CameraFollow _cameraFollow;
        private string _status = "connecting...";

        /// <summary>Автостарт без правки сцены: клиент поднимается после загрузки любой сцены.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (FindFirstObjectByType<ClientRuntime>() != null)
            {
                return;
            }

            var clientObject = new GameObject("Albion Client");
            clientObject.AddComponent<ClientRuntime>();
        }

        private void Awake()
        {
            var host = ReadSetting("ALBION_SERVER_HOST", "serverHost", DefaultHost);
            var portText = ReadSetting("ALBION_SERVER_PORT", "serverPort", DefaultPort.ToString());
            var port = int.TryParse(portText, out var parsedPort) ? parsedPort : DefaultPort;

            EnsureCamera();
            EnsureLight();
            EnsureGround();
            EnsureAmbient();

            // M10.10: мышь — часть управления. В оконном/редакторном режиме курсор не должен
            // быть захвачен, а приложение продолжает получать ввод, пока окно активно.
            Application.runInBackground = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _quality = gameObject.AddComponent<QualityProfile>();
            _quality.ApplyFromEnvironment(EnsureVolume());

            var configDirectory = Path.Combine(Application.streamingAssetsPath, "Config");
            try
            {
                _content = ClientContent.LoadFromDirectory(configDirectory);
                Debug.Log("ClientRuntime: конфиг " + configDirectory + " загружен, hash=" + _content.Hash);
            }
            catch (Exception exception)
            {
                _status = "config error: " + exception.Message;
                Debug.LogError("ClientRuntime: конфиг не загружен (" + configDirectory + "): " + exception.Message
                    + ". Запустите tools/unity/build-plugins.ps1.");
                return;
            }

            var worldRoot = new GameObject("Entities").transform;
            _view = new UnityView(worldRoot);
            var moveSpeed = _content.GetActor(1).MoveSpeed;
            _input = new UnityInputSource(
                ResolveAbilityTarget,
                moveSpeed > 1.5f ? 1.5f / moveSpeed : 1f,
                () => _view.IsPlayerAttackPlaying,
                () => _pipeline != null && _pipeline.Prediction != null && _pipeline.Prediction.IsAbilityReady,
                PlayerPosition,
                () => _cameraFollow != null ? _cameraFollow.GetComponent<Camera>() : Camera.main,
                equipWeapon: _view.EquipWeapon);
            _transport = new LiteNetLibTransport(host, port);
            _pipeline = new ClientPipeline(_transport, _input, _view, content: _content);
            gameObject.AddComponent<CooldownHud>().Initialize(_pipeline);

            _pipeline.SessionReady += entityId =>
            {
                _view.PlayerEntityId = entityId;
                _status = "connected, entity " + entityId;
                Debug.Log("ClientRuntime: сессия готова, сущность " + entityId);

                if (_pipeline.Prediction != null)
                {
                    _pipeline.Prediction.Reconciled += reconciliation => Debug.Log(
                        "ClientRuntime: реконсиляция tick=" + reconciliation.Tick
                        + " error=" + reconciliation.Error.ToString("0.###")
                        + " predicted=(" + reconciliation.Predicted.X.ToString("0.###") + ", "
                        + reconciliation.Predicted.Z.ToString("0.###") + ")"
                        + " server=(" + reconciliation.Server.X.ToString("0.###") + ", "
                        + reconciliation.Server.Z.ToString("0.###") + ")");
                }
            };

            _pipeline.Disconnected += reason =>
            {
                _status = "disconnected: " + reason;
                Debug.LogWarning("ClientRuntime: соединение потеряно — " + reason);
            };

            _pipeline.Start();
            Debug.Log("ClientRuntime: подключаюсь к " + host + ":" + port);
        }

        private uint ResolveAbilityTarget()
        {
            if (_pipeline == null || !_pipeline.HasSession)
            {
                return 0;
            }

            // Живая цель имеет приоритет; если живых нет, берём ближайшую мёртвую. Удар по
            // мёртвой цели — легальный miss с кулдауном, а стабильный id переживает респавн.
            // target 0 остаётся только для пустого мира.
            return _pipeline.FindNearestEntity(_pipeline.EntityId);
        }

        private Vector3 PlayerPosition()
        {
            if (_pipeline != null && _pipeline.Prediction != null)
            {
                var predicted = _pipeline.Prediction.Position;
                return new Vector3(predicted.X, predicted.Y, predicted.Z);
            }

            return _view != null && _view.Player != null ? _view.Player.position : Vector3.zero;
        }

        private void Update()
        {
            if (_pipeline == null)
            {
                return;
            }

            _input.PollFrame();
            var cooldownBefore = _pipeline.Prediction?.CooldownRemainingTicks ?? 0;
            _pipeline.Update(Time.deltaTime);
            var cooldownAfter = _pipeline.Prediction?.CooldownRemainingTicks ?? 0;
            // The pipeline owns ability acceptance; readiness before the update plus a jump in
            // cooldown is the "attack actually started" signal. Requiring readiness avoids
            // restarting the swing on reconciliation jitter in the middle of a cooldown.
            if (cooldownBefore <= 1 && cooldownAfter > cooldownBefore + 1) _view.TriggerAttack();
            UpdateFps();

            if (_cameraFollow != null && _cameraFollow.Target == null && _view.Player != null)
            {
                _cameraFollow.Target = _view.Player;
            }
        }

        private void OnDestroy()
        {
            if (_pipeline != null)
            {
                _pipeline.Dispose();
                _pipeline = null;
            }
        }

        private void OnGUI()
        {
            var snapshots = _pipeline == null ? 0 : _pipeline.SnapshotsApplied;
            var deltas = _pipeline == null ? 0 : _pipeline.DeltasApplied;
            var fullRequests = _pipeline == null ? 0 : _pipeline.FullSnapshotRequests;
            var configHash = _content == null ? "-" : _content.Hash.Substring(0, Math.Min(8, _content.Hash.Length));

            GUI.Label(
                new Rect(12f, 12f, 1000f, 24f),
                "Albion: " + _status + " | snapshots: " + snapshots + " (delta " + deltas + ", fullReq " + fullRequests + ")"
                + " | cfg: " + configHash
                + " | profile: " + (_quality == null ? "-" : _quality.ModeName)
                + " | fps: " + _fpsSmoothed.ToString("0")
                + " | WASD/ПКМ — движение, Ctrl — шаг, Space/ЛКМ — способность, 1/2 — оружие");

            if (_pipeline?.Prediction != null)
            {
                var prediction = _pipeline.Prediction;
                GUI.Label(
                    new Rect(12f, 34f, 1000f, 24f),
                    "prediction: reconciles=" + prediction.Reconciliations
                    + " | lastError=" + prediction.LastError.ToString("0.000")
                    + " | pos=(" + prediction.Position.X.ToString("0.##") + ", " + prediction.Position.Z.ToString("0.##") + ")"
                    + " | unacked=" + prediction.Unacknowledged.Count
                    + " | hp: " + OwnHpText()
                    + " | res: " + OwnResourceText()
                    + " | cd: " + prediction.CooldownRemainingTicks + " тиков"
                    + " | range: " + prediction.AbilityRange.ToString("0.##"));
            }
        }

        private string OwnHpText()
        {
            var entity = OwnEntity();
            return entity.HasValue ? entity.Value.Hp + "/" + entity.Value.MaxHp : "-";
        }

        private string OwnResourceText()
        {
            var entity = OwnEntity();
            return entity.HasValue ? entity.Value.Resource + "/" + entity.Value.MaxResource : "-";
        }

        private EntitySnapshot? OwnEntity()
        {
            var snapshot = _pipeline?.LastSnapshot;
            if (snapshot == null)
            {
                return null;
            }

            foreach (var entity in snapshot.Entities)
            {
                if (entity.Id == _pipeline!.EntityId)
                {
                    return entity;
                }
            }

            return null;
        }

        private void EnsureCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.transform.position = new Vector3(0f, 10f, -8f);
            _cameraFollow = camera.GetComponent<CameraFollow>();
            if (_cameraFollow == null)
            {
                _cameraFollow = camera.gameObject.AddComponent<CameraFollow>();
            }
        }

        private static void EnsureLight()
        {
            if (FindFirstObjectByType<Light>() != null)
            {
                return;
            }

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            ArenaEnvironment.ConfigureSun(light);
        }

        private static void EnsureGround()
        {
            if (GameObject.Find("Ground") != null)
            {
                return;
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(8f, 1f, 8f);

            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Albion/Toon Ground");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                }

                var material = new Material(shader);
                if (material.HasProperty("_ColorA"))
                {
                    material.SetColor("_ColorA", new Color(0.27f, 0.40f, 0.32f));
                    material.SetColor("_ColorB", new Color(0.21f, 0.31f, 0.27f));
                    material.SetFloat("_TileSize", 2f);
                }
                else
                {
                    material.color = new Color(0.55f, 0.57f, 0.5f);
                }

                renderer.sharedMaterial = material;
            }
        }

        private static void EnsureAmbient()
        {
            ArenaEnvironment.ApplyAmbientAndFog();
        }

        private static Volume EnsureVolume()
        {
            var existing = FindFirstObjectByType<Volume>();
            if (existing != null)
            {
                return existing;
            }

            var volumeObject = new GameObject("Global Volume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            var profile = Resources.Load<VolumeProfile>("Rendering/AlbionPostProcess");
            if (profile != null)
            {
                volume.sharedProfile = profile;
            }

            return volume;
        }

        private void UpdateFps()
        {
            var frameTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            var fps = 1f / frameTime;
            _fpsSmoothed = _fpsSmoothed <= 0f ? fps : Mathf.Lerp(_fpsSmoothed, fps, 0.05f);

            _fpsLogTimer += frameTime;
            _fpsLogSum += fps;
            _fpsLogFrames++;
            if (_fpsLogTimer >= 10f)
            {
                Debug.Log("[fps] profile=" + (_quality == null ? "-" : _quality.ModeName)
                    + " avg=" + (_fpsLogSum / Mathf.Max(1, _fpsLogFrames)).ToString("0.0")
                    + " frames=" + _fpsLogFrames);
                _fpsLogTimer = 0f;
                _fpsLogSum = 0f;
                _fpsLogFrames = 0;
            }
        }

        private static string ReadSetting(string environmentName, string argumentName, string fallback)
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrEmpty(fromEnvironment))
            {
                return fromEnvironment;
            }

            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == "-" + argumentName)
                {
                    return arguments[i + 1];
                }
            }

            return fallback;
        }
    }
}
