using System;
using System.Collections;
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
        private ZoneAtmosphere _atmosphere;
        private UnityInputSource _input;
        private CameraFollow _cameraFollow;
        private ClientHud _hud;
        private MobileControls? _mobile;
        private string _status = "Загрузка конфигурации…";
        private string _startupError = string.Empty;
        private bool _awaitingConnect;
        private string _hostInput = "192.168.1.2";
        private string _portInput = "9050";
        private string _connectError = string.Empty;
        private TouchScreenKeyboard? _keyboard;
        private bool _mobileSkinConfigured;
        private uint _lastAbilityTarget;

        /// <summary>Клиентский конвейер после старта; <c>null</c> до загрузки конфига (для smoke-тестов).</summary>
        public ClientPipeline? Pipeline => _pipeline;

        /// <summary>Представление мира после старта; <c>null</c> до загрузки конфига (для smoke-тестов).</summary>
        public UnityView? View => _view;

        /// <summary>Input source used by the live pipeline; the playtest replaces its command provider.</summary>
        public UnityInputSource? Input => _input;

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

            StartCoroutine(LoadContentAndStart(host, port));
        }

        private IEnumerator LoadContentAndStart(string host, int port)
        {
            var configDirectory = Application.streamingAssetsPath.TrimEnd('/') + "/Config";
            yield return StreamingContentLoader.Load(configDirectory,
                content => _content = content,
                error =>
                {
                    _startupError = error;
                    _status = "Ошибка загрузки конфигурации";
                    Debug.LogError("ClientRuntime: " + error);
                });
            if (_content == null)
            {
                yield break;
            }

            Debug.Log("ClientRuntime: конфиг " + configDirectory + " загружен, hash=" + _content.Hash);
            _atmosphere = gameObject.AddComponent<ZoneAtmosphere>();
            _atmosphere.Initialize(_content);
            var worldRoot = new GameObject("Entities").transform;
            _view = new UnityView(worldRoot, _content);

            // На телефоне сервер не localhost: сначала спрашиваем IP компьютера, иначе клиент
            // подключался бы к самому устройству. На desktop поведение прежнее — автостарт.
            if (Application.isMobilePlatform)
            {
                Screen.orientation = ScreenOrientation.LandscapeLeft;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
                _awaitingConnect = true;
                _hostInput = PlayerPrefs.GetString("albion.serverHost", ReadMobileDefaultHost());
                _portInput = PlayerPrefs.GetString("albion.serverPort", "9050");
                _status = "укажите адрес сервера";
                yield break;
            }

            StartSession(host, port);
        }

        private static string ReadMobileDefaultHost()
        {
            // build-android.ps1 кладёт IP компьютера в Resources/MobileServerHost.txt, чтобы
            // на телефоне не вводить адрес вручную. Иначе остаётся понятный placeholder.
            var asset = Resources.Load<TextAsset>("MobileServerHost");
            if (asset != null && !string.IsNullOrWhiteSpace(asset.text))
            {
                return asset.text.Trim();
            }

            return "192.168.1.2";
        }

        private void StartSession(string host, int port)
        {
            if (_pipeline != null)
            {
                return;
            }

            var moveSpeed = _content.GetActor(1).MoveSpeed;
            _input = new UnityInputSource(
                ResolveAbilityTarget,
                moveSpeed > 1.5f ? 1.5f / moveSpeed : 1f,
                () => _view.IsPlayerAttackPlaying,
                () => _pipeline != null && _pipeline.Prediction != null && _pipeline.Prediction.IsAbilityReady,
                PlayerPosition,
                () => _cameraFollow != null ? _cameraFollow.GetComponent<Camera>() : Camera.main,
                touchMove: () => _mobile != null ? _mobile.Move : System.Numerics.Vector2.Zero,
                touchAttack: () => _mobile != null && _mobile.AttackHeld,
                groundClick: OnGroundClick,
                isPointerCapturedByUi: () => _hud != null && _hud.IsPointerOverUi,
                targetPosition: TargetPosition,
                abilityRange: AbilityRange,
                approachSlack: ApproachSlack());
            _transport = new LiteNetLibTransport(host, port);
            _pipeline = new ClientPipeline(_transport, _input, _view, content: _content);
            gameObject.AddComponent<CooldownHud>().Initialize(_pipeline);
            _hud = gameObject.AddComponent<ClientHud>();
            _hud.Initialize(_pipeline, _content, _view, _input);

            // Protocol v11: чужие атаки приходят событием и показываются замахом + снарядом.
            _pipeline.CombatEventReceived += _view.PlayCombatEvent;

            if (Application.isMobilePlatform)
            {
                _mobile = gameObject.AddComponent<MobileControls>();
                _mobile.Initialize(_hud);
            }

            _pipeline.SessionReady += entityId =>
            {
                _view.PlayerEntityId = entityId;
                _status = "подключено, сущность " + entityId;
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
                _status = "соединение потеряно: " + reason;
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
            _lastAbilityTarget = _pipeline.FindNearestEntity(_pipeline.EntityId);
            return _lastAbilityTarget;
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

        /// <summary>
        /// Where an ability target currently stands, as the renderer knows it. Null hides the
        /// target from the approach logic, so the strike still fires instead of a character
        /// running toward an entity that was never drawn.
        /// </summary>
        private Vector3? TargetPosition(uint entityId)
        {
            return _view != null ? _view.GetEntityPosition(entityId) : null;
        }

        /// <summary>
        /// Ability range from the same config the server validates with. Prediction holds it once
        /// the session is up; before that a zero is returned and the approach step stands down
        /// rather than auto-running on a guessed number.
        /// </summary>
        private float AbilityRange()
        {
            return _pipeline != null && _pipeline.Prediction != null ? _pipeline.Prediction.AbilityRange : 0f;
        }

        /// <summary>
        /// How far inside the ability radius the approach stops, so a drifting target cannot
        /// leave range between the last step and the strike. Authored in the character visual
        /// settings asset; the fallback only covers a missing asset.
        /// </summary>
        private float ApproachSlack()
        {
            var settings = Resources.Load<CharacterVisualSettings>(CharacterVisualSettings.ResourcePath);
            return settings != null ? settings.ApproachSlack : 0f;
        }

        private void OnGroundClick(Vector3 point)
        {
            _view?.ShowClickMarker(point);
            _hud?.OnGroundClick(point);
        }

        private void Update()
        {
            if (_pipeline == null)
            {
                return;
            }

            // Death/respawn edge: drop held movement input before the pipeline reads a command.
            // Without this the destination recorded before death stayed live, and the character
            // ran off on its own right after respawn until the player pressed something.
            if (_view != null && _view.ConsumePlayerRespawnEdge())
            {
                _input.ResetHeldInput();
            }

            _input.PollFrame();
            var cooldownBefore = _pipeline.Prediction?.CooldownRemainingTicks ?? 0;
            _pipeline.Update(Time.deltaTime);
            var cooldownAfter = _pipeline.Prediction?.CooldownRemainingTicks ?? 0;
            // The pipeline owns ability acceptance; readiness before the update plus a jump in
            // cooldown is the "attack actually started" signal. Requiring readiness avoids
            // restarting the swing on reconciliation jitter in the middle of a cooldown.
            if (cooldownBefore <= 1 && cooldownAfter > cooldownBefore + 1)
                _view.TriggerAttack(_lastAbilityTarget);
            UpdateFps();

            if (_cameraFollow != null && _cameraFollow.Target == null && _view.Player != null)
            {
                _cameraFollow.Target = _view.Player;
                _atmosphere?.Follow(_view.Player);
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
            DrawBuildVersion();
            if (Application.isMobilePlatform && !_mobileSkinConfigured)
            {
                var fontSize = Mathf.Max(18, Mathf.RoundToInt(Screen.height / 55f));
                GUI.skin.label.fontSize = fontSize;
                GUI.skin.button.fontSize = fontSize;
                GUI.skin.textField.fontSize = fontSize;
                GUI.skin.button.fixedHeight = 48f;
                GUI.skin.textField.fixedHeight = 40f;
                _mobileSkinConfigured = true;
            }

            if (_content == null)
            {
                DrawStartupPanel();
                return;
            }

            if (_awaitingConnect)
            {
                DrawConnectPanel();
                return;
            }

            GUI.Label(
                new Rect(12f, Screen.height - 24f, Screen.width - 24f, 20f),
                _status + "  |  конфиг: " + (_content == null ? "-" : _content.Hash.Substring(0, Math.Min(8, _content.Hash.Length)))
                + "  |  кадров/с: " + _fpsSmoothed.ToString("0"));
        }

        private static void DrawBuildVersion()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerRight,
                fontSize = Application.isMobilePlatform ? 16 : 12,
                normal = { textColor = new Color(0.88f, 0.92f, 0.84f) },
            };
            GUI.Label(new Rect(Screen.width - 330f, Screen.height - 48f, 318f, 22f),
                BuildInfo.Label, style);
        }

        private void DrawStartupPanel()
        {
            var width = Mathf.Min(Screen.width - 48f, 760f);
            var height = Mathf.Min(Screen.height - 48f, 340f);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f,
                width, height), GUI.skin.box);
            GUILayout.Label(_status);
            if (!string.IsNullOrEmpty(_startupError))
            {
                GUILayout.Label("Не удалось запустить клиент. Это ошибка загрузки данных приложения, а не сервера.");
                GUILayout.Label(_startupError, new GUIStyle(GUI.skin.label) { wordWrap = true });
            }
            GUILayout.EndArea();
        }

        private void DrawConnectPanel()
        {
            var width = Mathf.Min(Screen.width - 40f, 760f);
            var height = 420f;
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(rect, "Прототип Albion — подключение");

            GUILayout.BeginArea(new Rect(rect.x + 24f, rect.y + 52f, width - 48f, height - 76f));
            GUILayout.Label("IP компьютера, где запущен сервер:");
            _hostInput = GUILayout.TextField(_hostInput, 64);
            GUILayout.Label("Порт (по умолчанию 9050):");
            _portInput = GUILayout.TextField(_portInput, 8);

            if (GUILayout.Button("Показать клавиатуру для IP"))
            {
                _keyboard = TouchScreenKeyboard.Open(
                    _hostInput,
                    TouchScreenKeyboardType.NumbersAndPunctuation,
                    false,
                    false,
                    false,
                    false,
                    "192.168.1.2");
            }

            if (_keyboard != null)
            {
                if (_keyboard.status == TouchScreenKeyboard.Status.Done)
                {
                    _hostInput = _keyboard.text.Trim();
                    _keyboard = null;
                }
                else if (_keyboard.status == TouchScreenKeyboard.Status.Canceled)
                {
                    _keyboard = null;
                }
            }

            if (!string.IsNullOrEmpty(_connectError))
            {
                GUILayout.Label(_connectError);
            }

            if (GUILayout.Button("Подключиться"))
            {
                ConnectFromPanel();
            }

            GUILayout.EndArea();
        }

        private void ConnectFromPanel()
        {
            var host = _hostInput.Trim();
            if (string.IsNullOrEmpty(host))
            {
                _connectError = "Введите IP компьютера.";
                return;
            }

            if (!int.TryParse(_portInput.Trim(), out var port) || port <= 0 || port > 65535)
            {
                _connectError = "Порт должен быть числом 1..65535.";
                return;
            }

            _connectError = string.Empty;
            if (_keyboard != null)
            {
                _keyboard.active = false;
                _keyboard = null;
            }

            PlayerPrefs.SetString("albion.serverHost", host);
            PlayerPrefs.SetString("albion.serverPort", port.ToString());
            PlayerPrefs.Save();
            _awaitingConnect = false;
            _status = "подключение…";
            StartSession(host, port);
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
