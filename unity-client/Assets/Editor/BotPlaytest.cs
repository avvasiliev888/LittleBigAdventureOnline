#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Albion.Client;
using Client.Network;
using Simulation.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Albion.Editor
{
    /// <summary>
    /// Real PlayMode acceptance bot. It runs a normal Arena session for at least 30 seconds,
    /// drives movement/attack/loot through the same <see cref="UnityInputSource"/> the player
    /// uses, checks Animator states and screenshots run/attack/corpse.
    ///
    /// Run from a terminal (server must be listening on ALBION_SERVER_PORT):
    ///   Unity -projectPath unity-client -executeMethod Albion.Editor.BotPlaytest.Run
    /// It writes artifacts/playtest/bot-playtest.log/json and 01-run/02-attack/03-corpse PNGs.
    /// </summary>
    public static class BotPlaytest
    {
        private const string StateKey = "Albion.BotPlaytest.Running";
        private const double ScenarioSeconds = 30.0;
        private const double MaxScenarioSeconds = 55.0;
        private const double CaptureTimeoutSeconds = 5.0;

        private static double _started;
        private static double _scenarioStarted = -1.0;
        private static double _runStarted = -1.0;
        private static double _attackStarted = -1.0;
        private static double _captureDeadline;
        private static string _capturePath;
        private static bool _capturePending;
        private static bool _prepared;
        private static bool _providerInstalled;
        private static bool _finished;
        private static int _finishCode;
        private static double _finishAt;
        private static string _failure = string.Empty;
        private static string _resultPath;
        private static string _artifactDir;
        private static ClientRuntime _runtime;
        private static ClientPipeline _pipeline;
        private static UnityView _view;
        private static CharacterVisuals _playerVisual;
        private static uint _targetId;
        private static uint _corpseId;
        private static uint _sequence;
        private static int _stage;
        private static bool _runChecked;
        private static bool _attackChecked;
        private static bool _corpseChecked;
        private static bool _lootSent;
        private static Quaternion _legBefore;
        private static bool _legSampleTaken;
        private static bool _runLegChecked;
        private static bool _legProbeLogged;
        private static bool _mobChecked;
        private static bool _mobAttackLogged;
        private static bool _mobAttackVerified;
        private static double _mobAttackAt;
        private static int _playerHp = -1;
        private static bool _lateLegChecked;
        private static bool _lateLegSampleTaken;
        private static bool _weaponSwitchChecked;
        private static string _lastSeenAttackState = string.Empty;
        private static float _runLegAngle;
        private static float _lateLegAngle;
        private static double _legSampleAt;
        private static double _lateLegSampleAt;
        private static Quaternion _lateLegBefore;
        private static double _lastLog;
        private static readonly StringBuilder LogBuilder = new StringBuilder();

        private const int StageRun = 0;
        private const int StageApproach = 1;
        private const int StageAttack = 2;
        private const int StageLootMove = 3;
        private const int StageLoot = 4;
        private const int StageDone = 5;

        private static string PlaytestDirectory()
        {
            // Keep artifacts at the repository root even though Unity's CWD is unity-client.
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "artifacts", "playtest"));
        }

        /// <summary>Entry point for <c>-executeMethod</c>.</summary>
        public static void Run()
        {
            _artifactDir = PlaytestDirectory();
            Directory.CreateDirectory(_artifactDir);
            SessionState.SetInt(StateKey, 1);
            _started = EditorApplication.timeSinceStartup;
            _finished = false;
            _failure = string.Empty;
            LogBuilder.Clear();
            Log("start; server=" + Environment.GetEnvironmentVariable("ALBION_SERVER_HOST")
                + ":" + Environment.GetEnvironmentVariable("ALBION_SERVER_PORT"));
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
            if (SessionState.GetInt(StateKey, 0) != 1) return;
            var now = EditorApplication.timeSinceStartup;
            if (!EditorApplication.isPlaying)
            {
                if (now - _started > 12.0) Fail("Play mode не включился за 12 секунд.");
                return;
            }

            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
            if (Time.captureDeltaTime <= 0f) Time.captureDeltaTime = 1f / 60f;

            var runtime = Object.FindFirstObjectByType<ClientRuntime>();
            if (runtime == null || runtime.Pipeline == null || runtime.View == null)
            {
                if (now - _started > 40.0) Fail("ClientRuntime/Pipeline/View не созданы.");
                return;
            }
            if (string.IsNullOrEmpty(_artifactDir))
            {
                _artifactDir = PlaytestDirectory();
                Directory.CreateDirectory(_artifactDir);
            }
            _runtime = runtime;
            _pipeline = runtime.Pipeline;
            _view = runtime.View;

            if (!_prepared)
            {
                _prepared = true;
                try { GameViewCapture.Prepare(); }
                catch (Exception exception) { Log("GameViewCapture.Prepare: " + exception.Message); }
            }

            if (!_providerInstalled && runtime.Input != null)
            {
                runtime.Input.SetScriptedCommandProvider(NextCommand);
                _providerInstalled = true;
                Log("scripted input provider installed");
            }

            if (!_pipeline.HasSession || _pipeline.SnapshotsApplied < 5)
            {
                if (now - _started > 40.0)
                    Fail("нет сессии/снапшотов: session=" + _pipeline.HasSession
                        + " snapshots=" + _pipeline.SnapshotsApplied);
                return;
            }

            if (_scenarioStarted < 0)
            {
                _scenarioStarted = now;
                _stage = StageRun;
                _runStarted = now;
                Log("scenario started");
            }

            if (_finished)
            {
                if (now >= _finishAt) Exit();
                return;
            }

            if (_capturePending)
            {
                if (TryCapture())
                {
                    _capturePending = false;
                    return;
                }
                if (now >= _captureDeadline)
                {
                    Fail("не удалось сохранить скриншот " + _capturePath
                        + " (GameView не отрисовал кадр)");
                }
                return;
            }

            if (_playerVisual == null && _view != null && _pipeline != null && _pipeline.HasSession)
                _playerVisual = _view.GetCharacterVisual(_pipeline.EntityId);

            var own = FindPlayerSnapshot();
            if (own.HasValue)
            {
                if (_playerHp < 0) _playerHp = own.Value.Hp;
                if (own.Value.Hp < _playerHp)
                {
                    _playerHp = own.Value.Hp;
                    _mobAttackAt = now;
                    _mobAttackLogged = true;
                    Log("MOB ATTACK playerHp=" + own.Value.Hp);
                }
            }

            if (_mobAttackLogged && !_mobAttackVerified && now - _mobAttackAt >= 0.5)
            {
                var attacker = _view != null ? _view.GetCharacterVisual(_targetId) : null;
                var state = attacker != null && attacker.Animator != null
                    ? CurrentClip(attacker.Animator) : "-";
                Log("MOB ATTACK state=" + state + " " + (_view != null ? _view.LastMobAttackDebug : ""));
                if (state.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) < 0)
                    Fail("моб ударил игрока, но анимация атаки не проигралась: state=" + state);
                _mobAttackVerified = true;
            }

            var elapsed = now - _scenarioStarted;

            if (!_mobChecked && elapsed >= 0.6)
            {
                var mob = _view != null && _pipeline != null && _pipeline.HasSession
                    ? _view.GetCharacterVisual(_targetId) : null;
                if (mob != null && mob.LastMotionSpeed > 0.1f) CheckMobRun();
                else if (elapsed >= 2.8) CheckMobRun();
            }

            // Sample the leg DURING the run stage, before the bot gets into attack range:
            // checking the leg after the swing proves nothing about locomotion.
            if (!_runLegChecked && elapsed >= 0.2)
            {
                if (!_legSampleTaken)
                {
                    var leg = _playerVisual != null && _playerVisual.Animator != null
                        ? _playerVisual.Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) : null;
                    if (leg != null)
                    {
                        _legBefore = leg.localRotation;
                        _legSampleTaken = true;
                        _legSampleAt = now;
                        Log("LEGS sampled at t=" + elapsed.ToString("F2")
                            + " state=" + _playerVisual.LastRequestedState);
                    }
                    else if (!_legProbeLogged)
                    {
                        _legProbeLogged = true;
                        Log("LEGS bone unavailable at t=" + elapsed.ToString("F2")
                            + " visual=" + (_playerVisual != null)
                            + " animator=" + (_playerVisual != null && _playerVisual.Animator != null));
                    }
                }
                else if (_legSampleTaken && now - _legSampleAt >= 0.4)
                {
                    _runLegChecked = true;
                    var leg = _playerVisual != null && _playerVisual.Animator != null
                        ? _playerVisual.Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) : null;
                    _runLegAngle = leg != null ? Quaternion.Angle(_legBefore, leg.localRotation) : 0f;
                    Log("LEGS during run: RightUpperLeg delta=" + _runLegAngle.ToString("F2") + "deg");
                    if (_runLegAngle < 0.2f)
                        Fail("ноги не двигаются во время бега: delta=" + _runLegAngle.ToString("F2") + "°");
                }
            }

            // A second sample ~1 s later catches a non-looping clip that plays its first
            // cycle and then freezes while the root keeps sliding.
            if (_runLegChecked && !_lateLegChecked && elapsed >= 1.6)
            {
                if (!_lateLegSampleTaken)
                {
                    var leg = _playerVisual != null && _playerVisual.Animator != null
                        ? _playerVisual.Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) : null;
                    if (leg != null)
                    {
                        _lateLegSampleTaken = true;
                        _lateLegBefore = leg.localRotation;
                        _lateLegSampleAt = now;
                        Log("LEGS late sample at t=" + elapsed.ToString("F2")
                            + " state=" + _playerVisual.LastRequestedState);
                    }
                }
                else if (now - _lateLegSampleAt >= 0.4)
                {
                    _lateLegChecked = true;
                    var leg = _playerVisual != null && _playerVisual.Animator != null
                        ? _playerVisual.Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) : null;
                    _lateLegAngle = leg != null ? Quaternion.Angle(_lateLegBefore, leg.localRotation) : 0f;
                    Log("LEGS late right-upper-leg delta=" + _lateLegAngle.ToString("F2") + "deg");
                    if (_lateLegAngle < 0.2f)
                        Fail("ноги замерли после первого цикла: delta=" + _lateLegAngle.ToString("F2") + "°");
                }
            }
            if (elapsed > MaxScenarioSeconds)
            {
                Fail("сценарий не завершился за " + MaxScenarioSeconds.ToString("0") + " секунд (stage=" + _stage + ")");
                return;
            }

            switch (_stage)
            {
                case StageRun:
                    if (elapsed >= 3.0)
                    {
                        CheckRun();
                        RequestCapture(Path.Combine(_artifactDir, "01-run.png"));
                        _stage = StageApproach;
                    }
                    break;

                case StageApproach:
                    if (TargetDead())
                    {
                        _stage = StageLootMove;
                        break;
                    }
                    if (TargetDistance() <= 2.35f)
                    {
                        _stage = StageAttack;
                        _attackStarted = now;
                        Log("in range; attacking target " + _targetId);
                    }
                    else if (elapsed > 22.0)
                    {
                        Fail("бот не дошёл до моба за 22 секунды; distance="
                            + TargetDistance().ToString("F2"));
                    }
                    break;

                case StageAttack:
                    var attack = _playerVisual != null ? _playerVisual.Animator : null;
                    if (attack != null && IsAttackState(attack))
                    {
                        var seen = CurrentClip(attack);
                        if (seen != _lastSeenAttackState)
                        {
                            _lastSeenAttackState = seen;
                            Log("ATTACK clip=" + seen);
                        }
                    }
                    if (attack != null && !_attackChecked && IsAttackState(attack))
                    {
                        CheckAttack(attack);
                        RequestCapture(Path.Combine(_artifactDir, "02-attack.png"));
                    }
                    if (TargetDead())
                    {
                        _corpseId = FindCorpse();
                        if (_corpseId == 0)
                        {
                            if (now - _attackStarted > 8.0) Fail("моб умер, но труп не появился.");
                            break;
                        }
                        _stage = StageLootMove;
                        Log("mob dead; corpse=" + _corpseId);
                    }
                    else if (now - _attackStarted > 20.0)
                    {
                        Fail("моб не умер за 20 секунд атаки; hp=" + TargetHp());
                    }
                    break;

                case StageLootMove:
                    if (_corpseId == 0) _corpseId = FindCorpse();
                    if (CorpseDistance() <= 2.0f)
                    {
                        _stage = StageLoot;
                        Log("in loot range; sending E/LootCommand");
                    }
                    else if (elapsed > 30.0)
                    {
                        Fail("не удалось подойти к трупу; distance=" + CorpseDistance().ToString("F2"));
                    }
                    break;

                case StageLoot:
                    // Screenshot the corpse BEFORE E: the server may remove the corpse as soon
                    // as the loot command succeeds, which would otherwise hide the evidence.
                    if (!_corpseChecked)
                    {
                        if (FindCorpseSnapshot() == null)
                        {
                            if (elapsed > 30.0) Fail("труп исчез до скриншота.");
                            break;
                        }
                        CheckCorpse();
                        RequestCapture(Path.Combine(_artifactDir, "03-corpse.png"));
                        break;
                    }
                    if (!_lootSent)
                    {
                        _lootSent = true;
                        _sequence++;
                        // E is the same LootCommand the HUD sends to ClientPipeline.
                        var sent = _pipeline.SendLoot(_corpseId);
                        Log("LootCommand sent for corpse " + _corpseId + " accepted=" + sent);
                    }
                    if (!_weaponSwitchChecked) CheckWeaponSwitching();
                    if (elapsed >= ScenarioSeconds)
                    {
                        _stage = StageDone;
                        Pass();
                    }
                    break;

                case StageDone:
                    if (elapsed >= ScenarioSeconds) Pass();
                    break;
            }

            if (!_finished && now - _lastLog >= 2.0)
            {
                _lastLog = now;
                Log("t=" + elapsed.ToString("F1") + " stage=" + StageName(_stage)
                    + " pos=" + PlayerPosition().ToString("F1")
                    + " target=" + _targetId + " dist=" + TargetDistance().ToString("F2")
                    + " hp=" + TargetHp()
                    + " anim=" + CurrentClip(_playerVisual != null ? _playerVisual.Animator : null));
            }
        }

        private static ICommand NextCommand(uint clientTick)
        {
            if (_pipeline == null || !_pipeline.HasSession) return null;
            if (_stage == StageRun || _stage == StageApproach)
            {
                var direction = DirectionToTarget();
                return direction.HasValue ? new MoveCommand(direction.Value, clientTick, ++_sequence) : null;
            }
            if (_stage == StageAttack)
            {
                if (TargetDistance() > 2.8f)
                {
                    var direction = DirectionToTarget();
                    return direction.HasValue ? new MoveCommand(direction.Value, clientTick, ++_sequence) : null;
                }
                return new AbilityCommand(_targetId, clientTick, ++_sequence);
            }
            if (_stage == StageLootMove)
            {
                var direction = DirectionToCorpse();
                return direction.HasValue ? new MoveCommand(direction.Value, clientTick, ++_sequence) : null;
            }
            return null;
        }

        private static System.Numerics.Vector2? DirectionToTarget()
        {
            var entity = FindTarget();
            if (!entity.HasValue) return null;
            var player = PlayerPosition();
            var dx = entity.Value.Position.X - player.x;
            var dz = entity.Value.Position.Z - player.z;
            var length = MathF.Sqrt((dx * dx) + (dz * dz));
            if (length < 0.001f) return null;
            return new System.Numerics.Vector2(dx / length, dz / length);
        }

        private static System.Numerics.Vector2? DirectionToCorpse()
        {
            var corpse = FindCorpseSnapshot();
            if (corpse == null) return null;
            var player = PlayerPosition();
            var dx = corpse.Position.X - player.x;
            var dz = corpse.Position.Z - player.z;
            var length = MathF.Sqrt((dx * dx) + (dz * dz));
            if (length < 0.001f) return null;
            return new System.Numerics.Vector2(dx / length, dz / length);
        }

        private static void CheckMobRun()
        {
            _mobChecked = true;
            var mob = _view.GetCharacterVisual(_targetId);
            if (mob == null || mob.Animator == null)
            {
                Fail("у моба " + _targetId + " нет CharacterVisuals/Animator.");
                return;
            }
            var state = mob.Animator.GetCurrentAnimatorStateInfo(0);
            var clip = CurrentClip(mob.Animator);
            Log("MOB id=" + _targetId + " state=" + state.shortNameHash
                + " clip=" + clip + " motion=" + mob.LastMotionSpeed.ToString("F2")
                + " weight=" + mob.Animator.GetLayerWeight(0).ToString("F2")
                + " length=" + state.length.ToString("F2")
                + " weapons=" + mob.ActiveWeaponSlotCount
                + " baked=" + mob.VisibleEmbeddedEquipmentCount);
            if (mob.Animator.GetLayerWeight(0) < 0.99f)
                Fail("слой Animator моба weight < 1.");
            if (mob.LastMotionSpeed <= 0.1f)
            {
                Fail("моб не двигается — нельзя проверить анимацию движения.");
                return;
            }
            if (state.IsName("Idle"))
                Fail("моб движется, но Animator остаётся в Idle (скольжение).");
            if (string.IsNullOrEmpty(clip) || clip == "-")
                Fail("у моба нет активного клипа.");
        }

        private static void CheckRun()
        {
            _playerVisual = _view != null ? _view.GetCharacterVisual(_pipeline.EntityId) : null;
            if (_playerVisual == null || _playerVisual.Animator == null)
            {
                Fail("у игрока нет CharacterVisuals/Animator.");
                return;
            }
            var animator = _playerVisual.Animator;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            var clip = CurrentClip(animator);
            var layerWeight = animator.GetLayerWeight(0);
            Log("RUN state=" + state.shortNameHash + " clip=" + clip + " weight=" + layerWeight.ToString("F2")
                + " speedParam=" + animator.GetFloat("Speed").ToString("F2")
                + " motion=" + _playerVisual.LastMotionSpeed.ToString("F2")
                + " stateSpeed=" + state.speed.ToString("F2")
                + " request=" + _playerVisual.LastRequestedState
                + " hasState=" + _playerVisual.LastRequestedStateExists
                + " enabled=" + animator.enabled + " active=" + animator.isActiveAndEnabled
                + " animSpeed=" + animator.speed.ToString("F2")
                + " controller=" + (animator.runtimeAnimatorController != null
                    ? animator.runtimeAnimatorController.name : "null")
                + " weapons=" + _playerVisual.ActiveWeaponSlotCount
                + " baked=" + _playerVisual.VisibleEmbeddedEquipmentCount);
            Log("AVATAR valid=" + (animator.avatar != null && animator.avatar.isValid)
                + " human=" + (animator.avatar != null && animator.avatar.isHuman)
                + " active=" + animator.isActiveAndEnabled);
            Log("STATES walk=" + _playerVisual.WalkStateName + " run=" + _playerVisual.RunStateName
                + " attack=" + _playerVisual.AttackStateName
                + " isWalking=" + state.IsName("Walking_A")
                + " isRunning=" + state.IsName("Running_A"));
            if (layerWeight < 0.99f) Fail("слой Animator weight < 1: " + layerWeight.ToString("F2"));
            if (!state.IsName("Walking_A") && !state.IsName("Running_A"))
                Fail("во время бега Animator не в Walking_A/Running_A: state=" + state.shortNameHash
                    + " clip=" + clip);
            if (string.IsNullOrEmpty(clip) || (!clip.Contains("Walk") && !clip.Contains("Run")))
                Fail("во время бега играет не Walk/Run: state=" + state.shortNameHash + " clip=" + clip);
            if (_playerVisual.ActiveWeaponSlotCount != 1)
                Fail("активных слотов оружия " + _playerVisual.ActiveWeaponSlotCount + ", ожидался 1.");
            if (_playerVisual.VisibleEmbeddedEquipmentCount != 0)
                Fail("видны встроенные меши оружия/щита: " + _playerVisual.VisibleEmbeddedEquipmentCount);
            if (!_runLegChecked || !_lateLegChecked)
                Fail("не удалось проверить движение ног во время бега.");
            Log("RUN legsDelta=" + _runLegAngle.ToString("F2")
                + " lateLegsDelta=" + _lateLegAngle.ToString("F2")
                + " length=" + state.length.ToString("F2")
                + " normalizedTime=" + state.normalizedTime.ToString("F2"));
            _runChecked = true;
        }

        private static void CheckWeaponSwitching()
        {
            _weaponSwitchChecked = true;
            var visuals = _playerVisual;
            if (visuals == null || visuals.Animator == null)
            {
                Fail("нельзя проверить смену оружия: CharacterVisuals/Animator отсутствует.");
                return;
            }

            _view.EquipWeapon(WeaponKind.Bow);
            var bowState = "1H_Ranged_Shoot";
            var hasBow = visuals.Animator.HasState(0, Animator.StringToHash(bowState));
            var bowSlot = visuals.EquippedTransform;
            Log("WEAPONS bow equipped=" + visuals.Equipped
                + " activeSlots=" + visuals.ActiveWeaponSlotCount
                + " transform=" + (bowSlot != null ? bowSlot.name : "-")
                + " state=" + bowState + " hasState=" + hasBow);

            _view.EquipWeapon(WeaponKind.Staff);
            var staffState = "Spellcast_Shoot";
            var hasStaff = visuals.Animator.HasState(0, Animator.StringToHash(staffState));
            var staffSlot = visuals.EquippedTransform;
            Log("WEAPONS staff equipped=" + visuals.Equipped
                + " activeSlots=" + visuals.ActiveWeaponSlotCount
                + " transform=" + (staffSlot != null ? staffSlot.name : "-")
                + " state=" + staffState + " hasState=" + hasStaff);

            if (visuals.Equipped != WeaponKind.Staff || staffSlot == null
                || visuals.ActiveWeaponSlotCount != 1 || !hasStaff)
                Fail("посох не экипируется/не имеет состояния атаки.");
            if (!hasBow || bowSlot == null)
                Fail("лук не экипируется/не имеет состояния атаки.");

            // Leave the hero with the sword for the remaining screenshots/state.
            _view.EquipWeapon(WeaponKind.Sword);
        }

        private static void CheckAttack(Animator animator)
        {
            var clip = CurrentClip(animator);
            Log("ATTACK state=" + animator.GetCurrentAnimatorStateInfo(0).shortNameHash
                + " clip=" + clip + " weapons=" + _playerVisual.ActiveWeaponSlotCount
                + " baked=" + _playerVisual.VisibleEmbeddedEquipmentCount);
            if (!IsAttackState(animator))
                Fail("во время удара Animator не в Attack: clip=" + clip);
            if (_playerVisual.ActiveWeaponSlotCount != 1)
                Fail("во время атаки активных оружий " + _playerVisual.ActiveWeaponSlotCount);
            if (_playerVisual.VisibleEmbeddedEquipmentCount != 0)
                Fail("во время атаки видны встроенные мечи/щит: " + _playerVisual.VisibleEmbeddedEquipmentCount);
            _attackChecked = true;
        }

        private static void CheckCorpse()
        {
            var corpse = FindCorpseSnapshot();
            if (corpse == null)
            {
                Fail("труп не найден для проверки.");
                return;
            }
            if (_view.CorpseVisualCount <= 0)
            {
                Fail("визуальный труп не создан.");
                return;
            }
            Log("CORPSE id=" + corpse.Id + " loot=" + corpse.Loot.Count
                + " silver=" + corpse.Silver + " corpseVisuals=" + _view.CorpseVisualCount);
            _corpseChecked = true;
        }

        private static bool IsAttackState(Animator animator)
        {
            if (animator == null) return false;
            var clip = CurrentClip(animator);
            var state = animator.GetCurrentAnimatorStateInfo(0);
            return state.IsName("Attack")
                || state.IsName(_playerVisual != null ? _playerVisual.AttackStateName : string.Empty)
                || (!string.IsNullOrEmpty(clip) && clip.Contains("Attack"));
        }

        private static string CurrentClip(Animator animator)
        {
            if (animator == null) return "-";
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            return clips.Length == 0 || clips[0].clip == null ? "-" : clips[0].clip.name;
        }

        private static bool TargetDead()
        {
            var target = FindTarget(true);
            return target.HasValue && (target.Value.IsDead || FindCorpseSnapshot() != null);
        }

        private static EntitySnapshot? FindPlayerSnapshot()
        {
            if (_pipeline == null || !_pipeline.HasSession) return null;
            var snapshot = _pipeline.LastSnapshot;
            if (snapshot == null) return null;
            for (var i = 0; i < snapshot.Entities.Count; i++)
            {
                if (snapshot.Entities[i].Id == _pipeline.EntityId) return snapshot.Entities[i];
            }
            return null;
        }

        private static int TargetHp()
        {
            var target = FindTarget(true);
            return target.HasValue ? target.Value.Hp : -1;
        }

        private static float TargetDistance()
        {
            var target = FindTarget();
            if (!target.HasValue) return 999f;
            var player = PlayerPosition();
            var dx = target.Value.Position.X - player.x;
            var dz = target.Value.Position.Z - player.z;
            return MathF.Sqrt((dx * dx) + (dz * dz));
        }

        private static float CorpseDistance()
        {
            var corpse = FindCorpseSnapshot();
            if (corpse == null) return 999f;
            var player = PlayerPosition();
            var dx = corpse.Position.X - player.x;
            var dz = corpse.Position.Z - player.z;
            return MathF.Sqrt((dx * dx) + (dz * dz));
        }

        private static Vector3 PlayerPosition()
        {
            if (_pipeline?.Prediction != null)
            {
                var p = _pipeline.Prediction.Position;
                return new Vector3(p.X, p.Y, p.Z);
            }
            return _view != null && _view.Player != null ? _view.Player.position : Vector3.zero;
        }

        private static EntitySnapshot? FindTarget(bool includeDead = false)
        {
            if (_pipeline?.LastSnapshot == null || _pipeline.Content == null) return null;
            EntitySnapshot? best = null;
            var bestDistance = float.MaxValue;
            var player = PlayerPosition();
            foreach (var entity in _pipeline.LastSnapshot.Entities)
            {
                if (entity.Id == _pipeline.EntityId) continue;
                if (!includeDead && entity.IsDead) continue;
                if (_pipeline.Content.FindMobByActorId(entity.ActorId) == null) continue;
                var dx = entity.Position.X - player.x;
                var dz = entity.Position.Z - player.z;
                var distance = dx * dx + dz * dz;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = entity;
                _targetId = entity.Id;
            }
            return best;
        }

        private static uint FindCorpse()
        {
            if (_pipeline?.LastSnapshot == null) return 0;
            foreach (var corpse in _pipeline.LastSnapshot.Corpses)
            {
                if (corpse.Id != 0) return corpse.Id;
            }
            return 0;
        }

        private static CorpseSnapshot FindCorpseSnapshot()
        {
            if (_pipeline?.LastSnapshot == null) return null;
            foreach (var corpse in _pipeline.LastSnapshot.Corpses)
            {
                if (corpse.Id == _corpseId || _corpseId == 0) return corpse;
            }
            return null;
        }

        private static int CorpseLootCount()
        {
            var corpse = FindCorpseSnapshot();
            return corpse == null ? -1 : corpse.Loot.Count;
        }

        private static void RequestCapture(string path)
        {
            _capturePath = path;
            _capturePending = true;
            _captureDeadline = EditorApplication.timeSinceStartup + CaptureTimeoutSeconds;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            Log("capture requested: " + path);
        }

        private static bool TryCapture()
        {
            if (string.IsNullOrEmpty(_capturePath)) return false;
            try
            {
                if (GameViewCapture.TryCapture(_capturePath, out var error))
                {
                    Log("captured " + _capturePath + " (" + new FileInfo(_capturePath).Length + " bytes)");
                    return true;
                }
                Log("capture retry: " + error);
            }
            catch (Exception exception)
            {
                Log("capture failed: " + exception.Message);
            }
            return false;
        }

        private static void Pass()
        {
            if (_finished) return;
            _finished = true;
            _finishCode = 0;
            _finishAt = EditorApplication.timeSinceStartup + 1.5;
            Log("PASS duration=" + (EditorApplication.timeSinceStartup - _scenarioStarted).ToString("F1")
                + "s run=" + _runChecked + " attack=" + _attackChecked + " corpse=" + _corpseChecked
                + " loot=" + _lootSent + " mobAttack=" + _mobAttackLogged
                + " weapons=" + _weaponSwitchChecked);
            WriteResult("PASS", string.Empty);
        }

        private static void Fail(string reason)
        {
            if (_finished) return;
            _finished = true;
            _failure = reason;
            _finishCode = 1;
            _finishAt = EditorApplication.timeSinceStartup + 1.5;
            Debug.LogError("[bot-playtest] FAIL " + reason);
            Log("FAIL " + reason);
            WriteResult("FAIL", reason);
        }

        private static void Exit()
        {
            SessionState.SetInt(StateKey, 0);
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.Exit(_finishCode);
        }

        private static void WriteResult(string status, string reason)
        {
            _artifactDir = PlaytestDirectory();
            Directory.CreateDirectory(_artifactDir);
            _resultPath = Path.Combine(_artifactDir, "bot-playtest.json");
            var json = "{\n"
                + "  \"status\": \"" + Escape(status) + "\",\n"
                + "  \"reason\": \"" + Escape(reason) + "\",\n"
                + "  \"durationSeconds\": " + (_scenarioStarted < 0 ? 0 : EditorApplication.timeSinceStartup - _scenarioStarted).ToString("F2") + ",\n"
                + "  \"runChecked\": " + _runChecked.ToString().ToLowerInvariant() + ",\n"
                + "  \"attackChecked\": " + _attackChecked.ToString().ToLowerInvariant() + ",\n"
                + "  \"corpseChecked\": " + _corpseChecked.ToString().ToLowerInvariant() + ",\n"
                + "  \"lootSent\": " + _lootSent.ToString().ToLowerInvariant() + ",\n"
                + "  \"weaponSwitchChecked\": " + _weaponSwitchChecked.ToString().ToLowerInvariant() + ",\n"
                + "  \"screenshots\": [\"01-run.png\", \"02-attack.png\", \"03-corpse.png\"],\n"
                + "  \"log\": \"" + Escape(LogBuilder.ToString()) + "\"\n"
                + "}\n";
            File.WriteAllText(_resultPath, json);
            File.WriteAllText(Path.Combine(_artifactDir, "bot-playtest.log"), LogBuilder.ToString());
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "").Replace("\n", "\\n");
        }

        private static void Log(string message)
        {
            var line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message;
            Debug.Log("[bot-playtest] " + line);
            LogBuilder.AppendLine(line);
        }

        private static string StageName(int stage)
        {
            switch (stage)
            {
                case StageRun: return "run";
                case StageApproach: return "approach";
                case StageAttack: return "attack";
                case StageLootMove: return "loot-move";
                case StageLoot: return "loot";
                default: return "done";
            }
        }
    }
}
#endif
