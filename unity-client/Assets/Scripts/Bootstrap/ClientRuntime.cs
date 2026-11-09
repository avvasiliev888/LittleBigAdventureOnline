using System;
using Client.Network;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Точка входа Unity-клиента (M6): создаёт мир, транспорт, конвейер клиента и крутит кадры.
    /// Скрипт запускается сам после загрузки сцены, поэтому отдельная сцена/префаб не нужны.
    /// Настройки берутся из переменных окружения ALBION_SERVER_HOST / ALBION_SERVER_PORT
    /// или из аргументов командной строки -serverHost / -serverPort.
    /// </summary>
    public sealed class ClientRuntime : MonoBehaviour
    {
        private const string DefaultHost = "127.0.0.1";
        private const int DefaultPort = 9050;

        private LiteNetLibTransport _transport;
        private ClientPipeline _pipeline;
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

            var worldRoot = new GameObject("Entities").transform;
            _view = new UnityView(worldRoot);
            _input = new UnityInputSource();
            _transport = new LiteNetLibTransport(host, port);
            _pipeline = new ClientPipeline(_transport, _input, _view);

            _pipeline.SessionReady += entityId =>
            {
                _view.PlayerEntityId = entityId;
                _status = "connected, entity " + entityId;
                Debug.Log("ClientRuntime: сессия готова, сущность " + entityId);
            };

            _pipeline.Disconnected += reason =>
            {
                _status = "disconnected: " + reason;
                Debug.LogWarning("ClientRuntime: соединение потеряно — " + reason);
            };

            _pipeline.Start();
            Debug.Log("ClientRuntime: подключаюсь к " + host + ":" + port);
        }

        private void Update()
        {
            if (_pipeline == null)
            {
                return;
            }

            _pipeline.Update(Time.deltaTime);

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
            GUI.Label(new Rect(12f, 12f, 900f, 24f), "Albion: " + _status + " | snapshots: " + snapshots + " | WASD — движение");
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
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void EnsureGround()
        {
            if (GameObject.Find("Ground") != null)
            {
                return;
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f);

            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                var material = new Material(shader);
                material.color = new Color(0.55f, 0.57f, 0.5f);
                renderer.sharedMaterial = material;
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
