#if UNITY_EDITOR
using System;
using System.IO;
using Albion.Client;
using Client.Network;
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
        private static bool _viewPrepared;
        private static bool _captureDone;
        private static string _screenshotPath = string.Empty;
        private static double _captureDeadline;
        private static string _captureError = string.Empty;
        private static bool _qaTeleportDone;
        private static double _qaTeleportWaitUntil;
        private static int _qaShopState;
        private static double _qaShopWaitUntil;
        private static int _qaEquipState;
        private static double _qaEquipWaitUntil;
        private static double _qaEquipBuyDeadline;
        private static string _qaEquipResult = string.Empty;

        /// <summary>Точка входа для <c>-executeMethod</c>.</summary>
        public static void Run()
        {
            _contentChecksStarted = false;
            _contentChecksPassed = false;
            _qaTeleportDone = false;
            _qaTeleportWaitUntil = 0.0;
            _qaShopState = 0;
            _qaShopWaitUntil = 0.0;
            _qaEquipState = 0;
            _qaEquipWaitUntil = 0.0;
            _qaEquipBuyDeadline = 0.0;
            _qaEquipResult = string.Empty;
            SessionState.SetInt(StateKey, 1);
            _started = EditorApplication.timeSinceStartup;
            if (Environment.GetEnvironmentVariable("ALBION_REBUILD_WORLD") == "1")
                AlbionWorldBuilder.BuildArenaScene();
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
                // Композитный GameView-кадр (включая IMGUI HUD) считывается только после
                // настоящего Repaint окна, поэтому пробуем несколько editor-тиков.
                if (EditorApplication.isPaused)
                {
                    EditorApplication.isPaused = false;
                }

                var now = EditorApplication.timeSinceStartup;
                if (!_captureDone && !string.IsNullOrEmpty(_screenshotPath))
                {
                    if (GameViewCapture.TryCapture(_screenshotPath, out var error))
                    {
                        _captureDone = true;
                        _exitAt = now + 0.5;
                        Debug.Log("[agent-smoke] скриншот с HUD: " + _screenshotPath);
                    }
                    else
                    {
                        _captureError = error;
                    }

                    if (!_captureDone && now >= _captureDeadline)
                    {
                        // Явный fallback без HUD; в отчёте такой кадр не выдаётся за UI.
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_screenshotPath)));
                        ScreenCapture.CaptureScreenshot(_screenshotPath);
                        _captureDone = true;
                        _exitAt = now + 1.0;
                        Debug.LogWarning("[agent-smoke] GameView недоступен, снят только мир: " + _captureError);
                    }
                }

                if ((_captureDone || string.IsNullOrEmpty(_screenshotPath)) && now >= _exitAt)
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

            if (!_viewPrepared && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALBION_SMOKE_SCREENSHOT")))
            {
                _viewPrepared = true;
                try
                {
                    GameViewCapture.Prepare();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[agent-smoke] GameView Prepare: " + exception.Message);
                }
            }

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
            Stage6PerformanceChecks.Begin();
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

            if (UnityEngine.Object.FindFirstObjectByType<DamageNumbers>() == null)
            {
                Fail("DamageNumbers не создан: урон не будет виден");
                return;
            }

            var healthBars = UnityEngine.Object.FindObjectsByType<HealthBar>(
                UnityEngine.FindObjectsSortMode.None);
            if (healthBars.Length < 7)
            {
                Fail("полосок здоровья меньше 7: " + healthBars.Length);
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

            // QA-only: move the local player next to a mob of the selected zone so the
            // screenshot proves the albedo is no longer washed out by white emission.
            if (QaTeleportPending(pipeline))
            {
                return;
            }

            // QA-only: buy the blue-zone staff and craft the starter bow before the screenshot,
            // so the inventory panel is captured with real server-authoritative contents.
            if (QaShopPending(pipeline))
            {
                return;
            }

            // QA-only: equip a weapon through the server command path and verify the mount.
            if (QaEquipPending(pipeline))
            {
                return;
            }

            try
            {
                HudSmokeChecks.Verify();
                // QA seam: capture the craft window for the release screenshot. It is read
                // only in the editor smoke test, never by a normal player.
                if (Environment.GetEnvironmentVariable("ALBION_CRAFT_CAPTURE") == "1")
                {
                    var hud = UnityEngine.Object.FindFirstObjectByType<ClientHud>();
                    if (hud != null)
                    {
                        hud.ToggleCraft();
                        Debug.Log("[agent-smoke] craft window opened for capture");
                    }
                }

                var stage6 = Environment.GetEnvironmentVariable("ALBION_REBUILD_WORLD") == "1"
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALBION_STAGE6_GALLERY"));
                if (stage6)
                {
                    Stage6VisualChecks.VerifyAndCapture(pipeline.Content);
                    Stage6PerformanceChecks.Verify(Camera.main);
                }
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
                return;
            }

            Pass("snapshots=" + pipeline.SnapshotsApplied
                + " deltas=" + pipeline.DeltasApplied
                + " visuals=" + view.VisualEntityCount
                + " mobs=" + mobs
                + " corpses=" + view.CorpseVisualCount);
        }

        private static bool QaShopPending(ClientPipeline pipeline)
        {
            if (Environment.GetEnvironmentVariable("ALBION_QA_SHOP") != "1" || pipeline.Content == null)
            {
                return false;
            }

            var now = EditorApplication.timeSinceStartup;
            if (_qaShopState == 0)
            {
                Simulation.Core.NpcTrader? trader = null;
                foreach (var candidate in pipeline.Content.Config.Traders)
                {
                    if (candidate.Key == "blue_merchant")
                    {
                        trader = candidate;
                        break;
                    }
                }

                if (trader == null || !pipeline.SendTeleport(trader.Position.X + 2f, trader.Position.Z))
                {
                    _qaShopState = 5;
                    return false;
                }

                _qaShopState = 1;
                _qaShopWaitUntil = now + 1.2;
                return true;
            }

            if (now < _qaShopWaitUntil)
            {
                return true;
            }

            switch (_qaShopState)
            {
                case 1:
                    _qaShopState = 2;
                    _qaShopWaitUntil = now + 0.7;
                    pipeline.SendBuy(1, 15, 1);
                    return true;

                case 2:
                    _qaShopState = 3;
                    _qaShopWaitUntil = now + 0.7;
                    pipeline.SendCraft(5);
                    return true;

                case 3:
                    _qaShopState = 4;
                    _qaShopWaitUntil = now + 0.5;
                    var hud = UnityEngine.Object.FindFirstObjectByType<ClientHud>();
                    if (hud != null)
                    {
                        hud.ToggleInventory();
                        var contents = string.Empty;
                        foreach (var stack in pipeline.Inventory)
                        {
                            if (contents.Length > 0)
                            {
                                contents += ", ";
                            }

                            contents += "#" + stack.ItemId + "x" + stack.Quantity;
                        }

                        Debug.Log("[agent-smoke] QA shop inventory: " + contents);
                    }

                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// QA seam для главной жалобы на качество боя: удар мечом обязан проигрывать
        /// состояние с клипом клинка. Проверяем именно это, а не факт "что-то проигралось".
        /// </summary>
        private static bool QaSwordPending(ClientPipeline pipeline, double now)
        {
            if (_qaEquipState == 0)
            {
                _qaEquipState = 1;
                _qaEquipWaitUntil = now + 1.0;
                return true;
            }

            if (now < _qaEquipWaitUntil)
            {
                return true;
            }

            if (_qaEquipState == 1)
            {
                _qaEquipState = 2;
                var view = UnityEngine.Object.FindFirstObjectByType<ClientRuntime>()?.View;
                var target = pipeline.FindNearestEntity(pipeline.EntityId);
                if (target == 0)
                {
                    Fail("QA sword: рядом нет цели для удара");
                    return false;
                }

                view?.TriggerAttack(target);
                var character = view?.GetCharacterVisual(pipeline.EntityId);
                if (character == null)
                {
                    Fail("QA sword: у игрока нет CharacterVisuals");
                    return false;
                }

                var state = character.LastRequestedState;
                if (!character.LastRequestedStateExists)
                {
                    Fail("QA sword: состояния '" + state + "' нет в контроллере — удар не проиграется");
                    return false;
                }

                if (state.IndexOf("Melee_Attack", StringComparison.Ordinal) < 0)
                {
                    Fail("QA sword: удар выбрал '" + state + "', а не клип атаки клинком");
                    return false;
                }

                if (view.IsPlayerAttackPlaying != true)
                {
                    Fail("QA sword: замах не отмечен как активный");
                    return false;
                }

                Debug.Log("[agent-smoke] QA sword: удар проигрывает '" + state + "'");
                return true;
            }

            return false;
        }

        /// <summary>Есть ли предмет в авторитетном инвентаре клиента.</summary>
        private static bool InventoryHas(ClientPipeline pipeline, uint itemId)
        {
            foreach (var stack in pipeline.Inventory)
            {
                if (stack.ItemId == itemId && stack.Quantity > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// QA seam: buys and equips the bow/staff through the real server command path, then
        /// checks that the weapon is mounted on the correct hand and that a shot spawns a
        /// projectile. This is the automated regression for "bought but cannot equip".
        /// </summary>
        private static bool QaEquipPending(ClientPipeline pipeline)
        {
            var request = Environment.GetEnvironmentVariable("ALBION_QA_EQUIP");
            if (string.IsNullOrEmpty(request) || pipeline.Content == null)
            {
                return false;
            }

            var now = EditorApplication.timeSinceStartup;
            if (request == "sword")
            {
                return QaSwordPending(pipeline, now);
            }

            if (_qaEquipState == 0)
            {
                Simulation.Core.NpcTrader? trader = null;
                foreach (var candidate in pipeline.Content.Config.Traders)
                {
                    if (candidate.Key == "blue_merchant")
                    {
                        trader = candidate;
                        break;
                    }
                }

                if (trader == null || !pipeline.SendTeleport(trader.Position.X + 2f, trader.Position.Z))
                {
                    _qaEquipState = 9;
                    Fail("QA equip: не удалось подойти к торговцу");
                    return false;
                }

                _qaEquipState = 1;
                _qaEquipWaitUntil = now + 1.2;
                _qaEquipBuyDeadline = now + 12.0;
                return true;
            }

            if (now < _qaEquipWaitUntil)
            {
                return true;
            }

            switch (_qaEquipState)
            {
                case 1:
                    _qaEquipState = 2;
                    _qaEquipWaitUntil = now + 0.6;
                    pipeline.SendBuy(1, 15, 1);
                    return true;

                case 2:
                    _qaEquipState = 3;
                    _qaEquipWaitUntil = now + 0.6;
                    pipeline.SendBuy(1, 14, 1);
                    return true;

                case 3:
                    // Ждём, пока авторитетный инвентарь действительно получит купленное
                    // оружие: экипировка раньше покупки была бы отклонена сервером и
                    // замаскировала бы регрессию "купил, но надеть нельзя".
                    if (!InventoryHas(pipeline, 15) || !InventoryHas(pipeline, 14))
                    {
                        if (now > _qaEquipBuyDeadline)
                        {
                            Fail("QA equip: покупки не дошли до инвентаря за 6 с");
                            _qaEquipState = 9;
                            return false;
                        }

                        return true;
                    }

                    _qaEquipState = 4;
                    _qaEquipWaitUntil = now + 0.4;
                    var itemToEquip = request == "staff" ? 15u : 14u;
                    if (!pipeline.SendEquip(itemToEquip))
                    {
                        Fail("QA equip: SendEquip вернул false");
                    }

                    return true;

                case 4:
                    _qaEquipState = 6;
                    _qaEquipWaitUntil = now + 0.3;
                    var hud = UnityEngine.Object.FindFirstObjectByType<ClientHud>();
                    hud?.ToggleInventory();
                    var snapshot = pipeline.LastSnapshot;
                    if (snapshot == null)
                    {
                        Fail("QA equip: нет снапшота");
                        return false;
                    }

                    var expected = request == "staff" ? 15u : 14u;
                    var equipped = 0u;
                    foreach (var entity in snapshot.Entities)
                    {
                        if (entity.Id == pipeline.EntityId) equipped = entity.WeaponItemId;
                    }

                    if (equipped != expected)
                    {
                        Fail("QA equip: в снапшоте оружие #" + equipped + ", ожидалось #" + expected
                            + "; результат: " + pipeline.LastCommandResult?.Detail);
                        return false;
                    }

                    var view = UnityEngine.Object.FindFirstObjectByType<ClientRuntime>()?.View;
                    var character = view?.GetCharacterVisual(pipeline.EntityId);
                    if (character == null)
                    {
                        Fail("QA equip: у игрока нет CharacterVisuals");
                        return false;
                    }

                    var weapon = character.EquippedTransform;
                    if (weapon == null)
                    {
                        Fail("QA equip: оружие не смонтировано в руку");
                        return false;
                    }

                    var socket = expected == 14u ? character.LeftHandSocket : character.RightHandSocket;
                    if (socket == null)
                    {
                        Fail("QA equip: сокет руки не найден");
                        return false;
                    }

                    var handDistance = UnityEngine.Vector3.Distance(weapon.position, socket.position);
                    if (handDistance > 0.45f)
                    {
                        Fail("QA equip: оружие далеко от руки (" + handDistance.ToString("0.00") + " м)");
                        return false;
                    }

                    _qaEquipResult = "weapon=" + weapon.name + " equipped=" + equipped
                        + " hand=" + socket.name + " dist=" + handDistance.ToString("0.00")
                        + " active=" + character.ActiveWeaponSlotCount;
                    Debug.Log("[agent-smoke] QA equip: " + _qaEquipResult);

                    // Лук и посох обязаны стрелять снарядом. Проверяем в том же кадре:
                    // стрела живёт доли секунды и к следующему шагу уже уничтожится.
                    var rangedView = UnityEngine.Object.FindFirstObjectByType<ClientRuntime>()?.View;
                    rangedView?.TriggerAttack(pipeline.FindNearestEntity(pipeline.EntityId));
                    var spawned = UnityEngine.Object.FindObjectsByType<ProjectileVfx>(
                        UnityEngine.FindObjectsSortMode.None);
                    if (spawned.Length == 0)
                    {
                        Fail("QA equip: атака #" + expected + " не создала снаряд");
                        return false;
                    }

                    Debug.Log("[agent-smoke] QA equip: снаряд создан (" + spawned.Length + ")");

                    // Клип должен существовать в контроллере: иначе аниматор остаётся в Idle,
                    // и удар выглядит как рывок корпусом, а не как замах оружием.
                    if (!character.LastRequestedStateExists)
                    {
                        Fail("QA equip: состояния '" + character.LastRequestedState + "' нет в контроллере");
                        return false;
                    }

                    Debug.Log("[agent-smoke] QA equip: состояние '" + character.LastRequestedState + "' найдено");
                    _qaEquipState = 6;
                    return true;

                default:
                    return false;
            }
        }

        private static bool QaTeleportPending(ClientPipeline pipeline)
        {
            var request = Environment.GetEnvironmentVariable("ALBION_QA_MOB_ZONE");
            if (string.IsNullOrEmpty(request) || pipeline.Content == null)
            {
                return false;
            }

            if (_qaTeleportDone)
            {
                return EditorApplication.timeSinceStartup < _qaTeleportWaitUntil;
            }

            _qaTeleportDone = true;
            var snapshot = pipeline.LastSnapshot;
            if (snapshot != null)
            {
                foreach (var entity in snapshot.Entities)
                {
                    var mob = pipeline.Content.FindMobByActorId(entity.ActorId);
                    if (mob == null || mob.ZoneKey != request)
                    {
                        continue;
                    }

                    // Seven metres south of the mob keeps it in front of the follow camera
                    // while the mob's own albedo fills a readable part of the frame.
                    var targetX = entity.Position.X;
                    var targetZ = entity.Position.Z - 7f;
                    if (pipeline.SendTeleport(targetX, targetZ))
                    {
                        Debug.Log("[agent-smoke] QA teleport to " + request + " mob: "
                            + targetX.ToString("0.#") + "," + targetZ.ToString("0.#"));
                        _qaTeleportWaitUntil = EditorApplication.timeSinceStartup + 2.2;
                        return true;
                    }
                }
            }

            Debug.LogWarning("[agent-smoke] QA mob zone not found: " + request);
            return false;
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
            Stage6PerformanceChecks.End();
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
                _exitCode = exitCode;
                _screenshotPath = screenshot;
                _captureDone = false;
                _captureError = string.Empty;
                _captureDeadline = EditorApplication.timeSinceStartup + 5.0;
                _exitAt = _captureDeadline;
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
