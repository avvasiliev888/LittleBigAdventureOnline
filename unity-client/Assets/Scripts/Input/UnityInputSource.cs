using System;
using System.Numerics;
using Client.Network;
using Simulation.Core;
using UnityEngine;
using UnityCamera = UnityEngine.Camera;
using UnityPlane = UnityEngine.Plane;
using UnityVector3 = UnityEngine.Vector3;

namespace Albion.Client
{
    /// <summary>
    /// What the client knows about a locked target right now.
    ///
    /// Three different situations used to collapse into "not found", and they need three different
    /// reactions: a corpse ends the fight, a target that vanished from a delta tick must be ridden
    /// out, and a missing dependency must change nothing at all.
    /// </summary>
    public enum AbilityTargetStatus
    {
        /// <summary>No information (a dependency is missing). Treated as alive: a gap must never end a fight.</summary>
        Unknown = 0,
        /// <summary>Present in the last snapshot with Hp left.</summary>
        Alive = 1,
        /// <summary>Present and out of Hp. The body may still be drawn; the fight is over.</summary>
        Dead = 2,
        /// <summary>Absent from the snapshot and from the world — respawned elsewhere or never drawn.</summary>
        Gone = 3,
    }

    /// <summary>
    /// Ввод: WASD + ПКМ (движение к курсору) → <see cref="MoveCommand"/>; Space/ЛКМ →
    /// <see cref="AbilityCommand"/> (M9/M10.10). Атака работает как авто-атака: пока клавиша/кнопка
    /// удерживается, на каждом кадре выставляется флаг, а клиентский тик отправляет способность,
    /// как только кулдаун равен 0 и текущий удар уже доиграл. Отпускание прекращает новые команды;
    /// короткий тап не теряется между тиками. Движение и атака не блокируют друг друга.
    /// </summary>
    public sealed class UnityInputSource : IInputSource
    {
        private readonly Func<uint>? _abilityTarget;
        private readonly Func<bool>? _isAttackPlaying;
        private readonly Func<bool>? _isAbilityReady;
        private readonly Func<UnityVector3>? _playerPosition;
        private readonly Func<UnityCamera?>? _camera;
        private readonly Func<System.Numerics.Vector2>? _touchMove;
        private readonly Func<bool>? _touchAttack;
        private readonly Action<UnityVector3>? _groundClick;
        private readonly Func<bool>? _isPointerCapturedByUi;
        private readonly Func<uint, UnityVector3?>? _targetPosition;
        private readonly Func<float>? _abilityRange;
        private readonly Func<bool>? _isStopped;
        /// <summary>
        /// Answers "what is the state of this target", which the engagement loop needs in one
        /// call: a corpse ends the fight, a target missing from a delta tick must be ridden out,
        /// and no dependency at all must change nothing.
        /// </summary>
        private readonly Func<uint, AbilityTargetStatus>? _targetStatus;
        private readonly float _approachSlack;
        private readonly float _walkScale;
        private readonly float _mouseMoveThreshold;
        /// <summary>
        /// How many ticks the body may refuse to come to rest before the strike is sent anyway.
        /// A safety bound only: without it a prediction that never reports zero speed would keep
        /// the attack latched forever, and the player would read that as a dead Space key.
        /// </summary>
        private const int StoppedTicksLimit = 10;

        /// <summary>
        /// How many client ticks a locked target may be absent from the snapshot before the
        /// engagement ends. Delta snapshots can drop an entity for a tick or two; treating the
        /// first miss as death would cancel a fight that is still going on.
        /// </summary>
        private const int MissingTicksLimit = 20;

        private bool _attackHeld;
        private bool _moveKeyHeld;
        /// <summary>
        /// The target the player engaged. 0 means no fight. It survives the key release, a
        /// movement command and the cooldown, because the contract is "attack until the target
        /// dies or I give another order", not "attack while the key is down".
        /// </summary>
        private uint _engagedTarget;
        /// <summary>
        /// Chase is the ONLY part of an engagement that a movement command cancels. The target
        /// stays locked, so the character keeps striking while it remains inside weapon range.
        /// </summary>
        private bool _chaseActive;
        private int _missingTicks;
        /// <summary>
        /// One legal swing into empty space, queued when the player pressed attack with no entity
        /// under the cursor. It is deliberately NOT an engagement: there is nothing to fight, so
        /// it must not repeat.
        /// </summary>
        private bool _voidSwing;
        private bool _destinationActive;
        private int _stoppedTicks;
        private UnityVector3 _cursorPoint;
        private bool _cursorPointValid;
        private UnityVector3 _destination;
        private uint _sequence;
        private Func<uint, ICommand?>? _scriptedCommand;

        /// <summary>Создаёт источник ввода.</summary>
        /// <param name="abilityTarget">
        /// Поставщик цели способности: ближайшая живая сущность, иначе ближайшая мёртвая,
        /// иначе 0 (в мире нет других сущностей). Удар по мёртвой цели — легальный miss
        /// с кулдауном; 0 так же активирует способность (M10.8.1).
        /// Без него Space/ЛКМ не отправляют команду.
        /// </param>
        /// <param name="walkScale">
        /// Множитель направления для шага (Ctrl): 1.5 / moveSpeed. Сервер уже принимает
        /// аналоговую длину направления, поэтому шаг остаётся в том же протоколе.
        /// </param>
        /// <param name="isAttackPlaying">
        /// Необязательный предикат «локальный Attack ещё проигрывается». Пока он true, команда
        /// способности не отправляется (даже если кулдаун уже 0), а флаг удержания сохраняется:
        /// так зажатая атака не перезапускает клип с первого кадра и сработает после его конца.
        /// </param>
        /// <param name="isAbilityReady">
        /// Необязательный предикат готовности кулдауна. Если кулдаун ещё идёт, источник отдаёт
        /// MoveCommand (движение не блокируется атакой/удержанием кнопки), а флаг способности
        /// остаётся до следующего готового тика.
        /// </param>
        /// <param name="playerPosition">
        /// Текущая позиция игрока (из prediction/вида) для движения к курсору. Без неё ПКМ
        /// не двигает персонажа.
        /// </param>
        /// <param name="camera">
        /// Камера для ScreenPointToRay. Без неё ПКМ не двигает персонажа.
        /// </param>
        /// <param name="mouseMoveThreshold">
        /// Минимальное расстояние до точки под курсором, ниже которого движение не отправляется
        /// (0.5 м по умолчанию).
        /// </param>
        /// <param name="touchMove">
        /// Направление экранного стика для мобильной сборки; <c>null</c> на desktop.
        /// </param>
        /// <param name="touchAttack">
        /// Удержание экранной кнопки удара для мобильной сборки; <c>null</c> на desktop.
        /// </param>
        /// <param name="groundClick">
        /// Точка земли под кликом ЛКМ/ПКМ: HUD использует её для лута, вид — для кружка-метки.
        /// </param>
        /// <param name="isPointerCapturedByUi">
        /// Предикат «курсор сейчас над видимым окном HUD». Пока он true, клики не создают
        /// точку движения и не отдают команду на карту: иначе кнопка «Скрафтить» одновременно
        /// запускала бег персонажа к курсору.
        /// </param>
        public UnityInputSource(
            Func<uint>? abilityTarget = null,
            float walkScale = 1f,
            Func<bool>? isAttackPlaying = null,
            Func<bool>? isAbilityReady = null,
            Func<UnityVector3>? playerPosition = null,
            Func<UnityCamera?>? camera = null,
            float mouseMoveThreshold = 0.5f,
            Func<System.Numerics.Vector2>? touchMove = null,
            Func<bool>? touchAttack = null,
            Action<UnityVector3>? groundClick = null,
            Func<bool>? isPointerCapturedByUi = null,
            Func<uint, UnityVector3?>? targetPosition = null,
            Func<float>? abilityRange = null,
            float approachSlack = 0.6f,
            Func<bool>? isStopped = null,
            Func<uint, AbilityTargetStatus>? targetStatus = null)
        {
            _abilityTarget = abilityTarget;
            _targetPosition = targetPosition;
            _abilityRange = abilityRange;
            _isStopped = isStopped;
            _targetStatus = targetStatus;
            _approachSlack = float.IsFinite(approachSlack) && approachSlack >= 0f ? approachSlack : 0.6f;
            _walkScale = float.IsFinite(walkScale) && walkScale > 0f ? Mathf.Min(walkScale, 1f) : 1f;
            _isAttackPlaying = isAttackPlaying;
            _isAbilityReady = isAbilityReady;
            _playerPosition = playerPosition;
            _camera = camera;
            _touchMove = touchMove;
            _touchAttack = touchAttack;
            _groundClick = groundClick;
            _isPointerCapturedByUi = isPointerCapturedByUi;
            _mouseMoveThreshold = float.IsFinite(mouseMoveThreshold) && mouseMoveThreshold > 0f
                ? mouseMoveThreshold
                : 0.5f;
        }

        /// <summary>
        /// Вызывается каждый кадр из <c>ClientRuntime.Update</c> до <c>ClientPipeline.Update</c>:
        /// фиксирует удержание Space/ЛКМ, чтобы авто-атака не зависела от того, попал ли кадр
        /// нажатия в клиентский тик (20 Гц). Отпускание прекращает новые команды; уже
        /// зафиксированное нажатие доедет до ближайшего тика.
        /// </summary>
        /// <summary>
        /// Test/playtest seam: when set, supplies commands before the keyboard path. The live game
        /// leaves it null, so this cannot affect a normal player.
        /// </summary>
        public void SetScriptedCommandProvider(Func<uint, ICommand?>? provider)
        {
            _scriptedCommand = provider;
        }

        /// <summary>
        /// Cancels a destination left over from a world click. Called when a HUD window opens
        /// so a click that opened or used the window cannot keep the character running.
        /// </summary>
        public void CancelPointerDestination()
        {
            _destinationActive = false;
        }

        /// <summary>
        /// Drops every held movement input: the click-to-move destination AND the held ability key.
        ///
        /// Death and respawn must both call this. A player who dies (or respawns) while still
        /// holding the right mouse button keeps a live destination, so the very next tick after
        /// respawn sends a MoveCommand and the character runs off on its own — for as long as the
        /// button is down, which the player reads as "runs right forever until I press something".
        /// The destination was recorded against the OLD position, so its direction is arbitrary and
        /// typically points away from the spawn point.
        ///
        /// Held keys are intentionally not consulted here: this is called once on the death/respawn
        /// edge, and the player must re-press to move again. Guessing "they probably still hold it"
        /// is exactly what produced the runaway.
        /// </summary>
        public void ResetHeldInput()
        {
            ReleaseEngagement(clearDestination: true);
            // The attack key dies with the character too: after respawn a still-held key would
            // otherwise re-engage whatever the cursor happens to point at.
            _attackHeld = false;
            _moveKeyHeld = false;
        }

        public void PollFrame()
        {
            // Pointer state first: the attack press below locks the target by the cursor point,
            // so the point has to be the one from THIS frame, not the previous one.
            var worldClick = false;
            if (!Application.isMobilePlatform)
            {
                if (_isPointerCapturedByUi != null && _isPointerCapturedByUi())
                {
                    // A click on a HUD window is UI input, never a world move command.
                    _destinationActive = false;
                    _cursorPointValid = false;
                }
                else
                {
                    // The cursor point is tracked every frame, not only while a button is held:
                    // the attack key must know what the player is pointing at.
                    _cursorPointValid = TryGroundPoint(out _cursorPoint);
                    worldClick = PollMouseDestination();
                }
            }

            // A fresh world click is a NEW MOVEMENT ORDER. It cancels the chase — the player asked
            // to go somewhere else — but it does NOT end the fight: the target stays locked, and
            // strikes continue on every ready cooldown for as long as that target remains inside
            // weapon range. That split is the heart of the model: movement and combat are two
            // independent channels, and only the chase belongs to both.
            if (worldClick)
            {
                CancelChase();
            }

            // Same for a fresh movement-key press. Held keys are not re-checked — that would
            // cancel the chase while the player simply keeps walking toward the target.
            var moveHeld = KeyboardDirection() != System.Numerics.Vector2.Zero;
            if (moveHeld && !_moveKeyHeld)
            {
                CancelChase();
            }

            _moveKeyHeld = moveHeld;

            var touchAttack = _touchAttack != null && _touchAttack();
            // На Android касание приходит и как mouse button 0; чтобы тапы по HUD/стику не
            // запускали удар, мышь учитываем только на desktop. Атака на телефоне — кнопка.
            // ЛКМ больше не атакует: это движение к курсору/точке и клик по луту.
            //
            // One PRESS starts one engagement, and ONLY a press can do it. The old code re-armed
            // a "pending strike" on every frame the key stayed down, so a held (or slightly
            // sticky) Space fired a fresh strike the moment a cooldown expired, with no action
            // from the player. Engagement now lives across the cooldown by design; the press edge
            // is the only thing that may create or replace it.
            var attackHeld = Input.GetKey(KeyCode.Space) || touchAttack;
            if (attackHeld && !_attackHeld)
            {
                _stoppedTicks = 0;
                // The target is chosen ONCE, here. Re-aiming while the key is merely down let a
                // character already running to one enemy switch to whatever the cursor brushed
                // over, so the strike landed somewhere the player never picked.
                var target = _abilityTarget != null ? _abilityTarget() : 0u;
                if (target == 0)
                {
                    // No entity to fight. A swing into the void is a legal miss, but it must not
                    // become an engagement, or the character would keep striking empty air until
                    // the player pressed something else.
                    ReleaseEngagement(clearDestination: false);
                    _voidSwing = true;
                }
                else
                {
                    ReleaseEngagement(clearDestination: false);
                    _engagedTarget = target;
                    // A press means "go and hit it": the chase starts armed, and a later movement
                    // command may take the chase back without dropping the target.
                    _chaseActive = true;
                    _voidSwing = false;
                }
            }

            _attackHeld = attackHeld;
        }

        /// <inheritdoc />
        public bool TryReadCommand(uint clientTick, out ICommand command)
        {
            if (_scriptedCommand != null)
            {
                var scripted = _scriptedCommand(clientTick);
                if (scripted != null)
                {
                    command = scripted;
                    return true;
                }
            }

            // ---- COMBAT CHANNEL -------------------------------------------------------------
            // Combat is independent of movement. An engagement survives the key release, the
            // cooldown, and a movement order; only death, absence from the world, or leaving the
            // weapon radius ends it. This is the "attack until the target dies or I give another
            // order" contract, and it is why a pending-strike flag could not express it.
            if (_engagedTarget != 0)
            {
                var status = TargetStatus(_engagedTarget);
                if (status == AbilityTargetStatus.Dead)
                {
                    // A corpse in the snapshot. The fight is over and the player expects the
                    // character to STOP where it stands, not to pick the next enemy by itself.
                    ReleaseEngagement(clearDestination: true);
                    command = null;
                    return false;
                }

                if (status == AbilityTargetStatus.Gone)
                {
                    // Absent from the snapshot. A delta can drop an entity for a tick or two, so
                    // this is not evidence of death yet; only a sustained absence is. Returning
                    // here is what makes the grace period mean anything — falling through would
                    // hit the range check below, find no position, and end the fight immediately.
                    if (++_missingTicks >= MissingTicksLimit)
                    {
                        ReleaseEngagement(clearDestination: true);
                        command = null;
                        return false;
                    }

                    return ReadPlainMovement(clientTick, out command);
                }

                _missingTicks = 0;

                if (!IsAttackPlaying())
                {
                    // Chase only while it is still armed. A movement command disarms it and
                    // leaves the target locked: the player takes back "run to him" without
                    // taking back "hit him".
                    if (_chaseActive && TryApproachStep(clientTick, out var chase))
                    {
                        _sequence++;
                        command = chase;
                        return true;
                    }

                    if (IsWithinReach(_engagedTarget))
                    {
                        if (IsAbilityReady())
                        {
                            // Arrived, but the body may still be sliding: the server needs a stop
                            // tick before the strike, otherwise the swing starts mid-stride and
                            // the facing is still owned by the velocity vector — "turns his back
                            // and hits the air, yet the damage lands". Bounded, so a body that
                            // never reports rest cannot latch the strike.
                            if (!IsStopped() && _stoppedTicks < StoppedTicksLimit)
                            {
                                ++_stoppedTicks;
                                _sequence++;
                                command = new MoveCommand(System.Numerics.Vector2.Zero, clientTick, _sequence);
                                return true;
                            }

                            _stoppedTicks = 0;
                            // Repeating by cooldown is the whole point of an engagement: the
                            // strike comes back on its own while the target stays in range, with
                            // no further input from the player.
                            _sequence++;
                            command = new AbilityCommand(_engagedTarget, clientTick, _sequence);
                            return true;
                        }

                        // Cooldown still running: fall through to movement, so WASD and clicks
                        // still work. The engagement is untouched and the next ready tick strikes
                        // again — pursuit during the cooldown is the default, not a special case.
                    }
                    else if (!_chaseActive)
                    {
                        // The chase was cancelled and the player has walked out of reach: the
                        // strike can no longer land, so the engagement ends. The destination is
                        // kept — the player is running somewhere and must not be stopped.
                        ReleaseEngagement(clearDestination: false);
                    }
                }
            }

            // A queued swing into empty space. It waits for readiness instead of being dropped,
            // so a press during the cooldown is not silently swallowed.
            if (_voidSwing && !IsAttackPlaying() && IsAbilityReady())
            {
                _voidSwing = false;
                if (!IsStopped() && _stoppedTicks < StoppedTicksLimit)
                {
                    ++_stoppedTicks;
                    _sequence++;
                    command = new MoveCommand(System.Numerics.Vector2.Zero, clientTick, _sequence);
                    return true;
                }

                _stoppedTicks = 0;
                _sequence++;
                command = new AbilityCommand(0u, clientTick, _sequence);
                return true;
            }

            return ReadPlainMovement(clientTick, out command);
        }

        /// <summary>
        /// The movement channel on its own: the attack clip owns the body while it plays, then
        /// WASD and the click-to-move destination decide. Split out because the engagement loop
        /// needs it both as its tail and as the answer for a target that is briefly missing.
        /// </summary>
        private bool ReadPlainMovement(uint clientTick, out ICommand command)
        {

            // A one-shot attack or shot owns the character: the legs are planted in the clip, so
            // sending a move command here made the body slide across the ground with its feet in
            // a shooting pose — the "floats without running" symptom. Standing still for the
            // length of the clip, then continuing, is what the animation is authored for.
            if (IsAttackPlaying())
            {
                command = null;
                return false;
            }

            var direction = ReadMovementDirection();
            if (direction == System.Numerics.Vector2.Zero)
            {
                command = null;
                return false;
            }

            direction = System.Numerics.Vector2.Normalize(direction);
            // Ctrl is real walking, not a fake animation: the smaller command magnitude makes
            // server and prediction both move at 1.5 m/s, which selects the Walk blend child.
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                direction *= _walkScale;
            }

            _sequence++;
            command = new MoveCommand(direction, clientTick, _sequence);
            return true;
        }

        /// <summary>
        /// WASD only, without the mouse destination and without the touch stick. Split out because
        /// the new-command check in PollFrame must see the key press itself, not the combined
        /// direction: a live mouse destination would otherwise mask a fresh key press.
        /// </summary>
        private System.Numerics.Vector2 KeyboardDirection()
        {
            var x = 0f;
            var z = 0f;

            if (Input.GetKey(KeyCode.A))
            {
                x -= 1f;
            }

            if (Input.GetKey(KeyCode.D))
            {
                x += 1f;
            }

            if (Input.GetKey(KeyCode.S))
            {
                z -= 1f;
            }

            if (Input.GetKey(KeyCode.W))
            {
                z += 1f;
            }

            return new System.Numerics.Vector2(x, z);
        }

        /// <summary>
        /// Takes back the CHASE only, leaving the engagement intact.
        ///
        /// This is the single place where the two channels meet. The player's rule: a new movement
        /// order cancels "run to that enemy", but it does not cancel "keep hitting him" — the fight
        /// goes on while the target stays inside weapon range, and ends only when the new orders
        /// carry the character out of it.
        /// </summary>
        private void CancelChase()
        {
            _chaseActive = false;
        }

        /// <summary>
        /// Ends the engagement. <paramref name="clearDestination"/> decides whether the body also
        /// stops: a dead target means "stand still", while walking out of reach means "keep going
        /// where I told you".
        /// </summary>
        private void ReleaseEngagement(bool clearDestination)
        {
            _engagedTarget = 0;
            _chaseActive = false;
            _voidSwing = false;
            _stoppedTicks = 0;
            _missingTicks = 0;
            if (clearDestination)
            {
                _destinationActive = false;
                _destination = UnityVector3.zero;
            }
        }

        /// <summary>
        /// What the client knows about a locked target. Without an accessor the answer is
        /// <see cref="AbilityTargetStatus.Unknown"/>, which the engagement treats as alive, so a
        /// missing dependency can never make the character stand still for no visible reason.
        /// </summary>
        private AbilityTargetStatus TargetStatus(uint targetId)
        {
            return _targetStatus != null ? _targetStatus(targetId) : AbilityTargetStatus.Unknown;
        }

        /// <summary>
        /// Inside the strike radius, measured horizontally against the same config range the
        /// server validates with, minus the approach slack. A bow simply has a bigger radius, so
        /// the same check stops the chase at 7.4 m instead of 2.4 m — no separate bow branch.
        /// </summary>
        private bool IsWithinReach(uint targetId)
        {
            if (_targetPosition == null || _playerPosition == null) return true;

            var target = _targetPosition(targetId);
            if (!target.HasValue) return false;

            var range = _abilityRange != null ? _abilityRange() : 0f;
            if (!float.IsFinite(range) || range <= 0f) return true;

            var player = _playerPosition();
            var dx = target.Value.x - player.x;
            var dz = target.Value.z - player.z;
            var distance = MathF.Sqrt((dx * dx) + (dz * dz));
            if (!float.IsFinite(distance)) return true;

            return distance <= MathF.Max(0.1f, range - _approachSlack);
        }

        private System.Numerics.Vector2 ReadMovementDirection()
        {
            var keyboard = KeyboardDirection();
            var touch = _touchMove != null ? _touchMove() : System.Numerics.Vector2.Zero;
            // Новая клавиатурная команда отменяет точку, к которой персонаж бежал.
            if (keyboard.LengthSquared() > 0.0001f)
            {
                _destinationActive = false;
            }

            if (Application.isMobilePlatform || !TryReadMouseDirection(out var mouse))
            {
                return keyboard + touch;
            }

            // Мышь и клавиатура комбинируются: W + ЛКМ/ПКМ дают диагональ, а противоположные
            // направления взаимно гасятся (персонаж останавливается).
            return keyboard + mouse + touch;
        }

        /// <summary>
        /// One approach step toward the ability target, or false when already inside range.
        ///
        /// Distance is measured horizontally and compared against the server's own ability range
        /// minus a slack, so a target that drifts cannot step out of range between the last move
        /// command and the strike. The radius source is the same config the server validates
        /// against; when it is not known yet the step assumes "already in range" rather than
        /// stranding the player in an auto-run that never ends.
        /// </summary>
        private bool TryApproachStep(uint clientTick, out ICommand command)
        {
            command = null;
            if (_targetPosition == null || _playerPosition == null || _engagedTarget == 0)
            {
                return false;
            }

            // The engaged target, not a fresh lookup: the chase must follow the enemy the player
            // aimed at even after the cursor has moved away.
            var target = _targetPosition(_engagedTarget);
            if (!target.HasValue) return false;

            // Already inside the radius: the caller sends the strike on the same tick, so there is
            // no one-frame window where the character stands still before swinging.
            if (IsWithinReach(_engagedTarget)) return false;

            var player = _playerPosition();
            var dx = target.Value.x - player.x;
            var dz = target.Value.z - player.z;
            var distance = MathF.Sqrt((dx * dx) + (dz * dz));
            if (!float.IsFinite(distance) || distance <= 0.0001f) return false;

            _sequence++;
            command = new MoveCommand(System.Numerics.Vector2.Normalize(
                new System.Numerics.Vector2(dx / distance, dz / distance)), clientTick, _sequence);
            return true;
        }

        /// <summary>
        /// True once the body has actually come to rest. The server strike must not be sent while
        /// the character still slides: the swing clip plays over a run and the visible facing is
        /// still driven by the velocity vector.
        /// </summary>
        private bool IsStopped()
        {
            return _isStopped == null || _isStopped();
        }

        /// <summary>
        /// The target the player is currently fighting, or 0 when there is no fight.
        ///
        /// The view uses this to face the strike: aiming is decided once, on the attack press, and
        /// every repeated strike must be shown against the SAME enemy. Re-resolving the target on
        /// each swing (what <c>_lastAbilityTarget</c> does) re-aims the character mid-fight at
        /// whatever the cursor brushed over.
        /// </summary>
        public uint EngagedTarget => _engagedTarget;

        /// <summary>
        /// World point under the mouse cursor, refreshed every frame. The ability target is chosen
        /// by this point, not by the character's own position: the player aims with the cursor.
        /// </summary>
        public bool TryGetCursorPoint(out UnityVector3 point)
        {
            point = _cursorPoint;
            return _cursorPointValid;
        }

        /// <summary>
        /// Tracks the click-to-move destination. Returns true only on a fresh button press, which
        /// the caller uses as "the player issued a different command" — a click supersedes a
        /// pending attack. A held button is not a new command.
        ///
        /// Releasing the button does NOT cancel the destination. The player's contract is "I click
        /// and he runs there until he arrives or I give another command", so the destination must
        /// outlive the click itself; it dies on arrival (<see cref="TryReadMouseDirection"/>) or on
        /// a newer command (WASD, another click, HUD, death).
        ///
        /// This is the exact line that broke running: the reset below used to sit AFTER the early
        /// return for "no button", so it was unreachable, and moving it above the guard made a
        /// quick click last only the one or two frames the button stayed down.
        /// </summary>
        private bool PollMouseDestination()
        {
            var left = Input.GetMouseButton(0);
            var right = Input.GetMouseButton(1);
            if (!left && !right)
            {
                return false;
            }

            if (!TryGroundPoint(out var point))
            {
                return false;
            }

            var clicked = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            _destination = point;
            _destinationActive = true;
            if (clicked)
            {
                _groundClick?.Invoke(point);
            }

            return clicked;
        }

        private bool TryReadMouseDirection(out System.Numerics.Vector2 direction)
        {
            direction = System.Numerics.Vector2.Zero;
            if (!_destinationActive || _playerPosition == null)
            {
                return false;
            }

            var player = _playerPosition();
            var dx = _destination.x - player.x;
            var dz = _destination.z - player.z;
            var distance = MathF.Sqrt((dx * dx) + (dz * dz));
            if (!float.IsFinite(distance) || distance <= _mouseMoveThreshold)
            {
                _destinationActive = false;
                return false;
            }

            direction = new System.Numerics.Vector2(dx / distance, dz / distance);
            return true;
        }

        private bool TryGroundPoint(out UnityVector3 point)
        {
            point = UnityVector3.zero;
            if (_camera == null)
            {
                return false;
            }

            var camera = _camera();
            if (camera == null)
            {
                return false;
            }

            // Плоскость земли Y=0: луч из камеры через курсор, без зависимости от коллайдеров.
            var ground = new UnityPlane(UnityVector3.up, UnityVector3.zero);
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!ground.Raycast(ray, out var enter) || enter < 0f)
            {
                return false;
            }

            point = ray.GetPoint(enter);
            return true;
        }

        private bool IsAttackPlaying() => _isAttackPlaying != null && _isAttackPlaying();

        private bool IsAbilityReady() => _isAbilityReady == null || _isAbilityReady();
    }
}
