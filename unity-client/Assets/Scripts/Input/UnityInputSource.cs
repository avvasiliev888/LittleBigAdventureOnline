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
        private readonly Action<WeaponKind>? _equipWeapon;
        private readonly Func<System.Numerics.Vector2>? _touchMove;
        private readonly Func<bool>? _touchAttack;
        private readonly Action<UnityVector3>? _groundClick;
        private readonly float _walkScale;
        private readonly float _mouseMoveThreshold;
        private bool _abilityPressed;
        private bool _destinationActive;
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
        /// <param name="equipWeapon">
        /// Клиентское переключение видимого оружия: 1 — меч, 2 — топор, 3 — лук, 4 — посох. Сервер не участвует,
        /// поэтому команда способности и протокол не меняются (M10.11).
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
        public UnityInputSource(
            Func<uint>? abilityTarget = null,
            float walkScale = 1f,
            Func<bool>? isAttackPlaying = null,
            Func<bool>? isAbilityReady = null,
            Func<UnityVector3>? playerPosition = null,
            Func<UnityCamera?>? camera = null,
            float mouseMoveThreshold = 0.5f,
            Action<WeaponKind>? equipWeapon = null,
            Func<System.Numerics.Vector2>? touchMove = null,
            Func<bool>? touchAttack = null,
            Action<UnityVector3>? groundClick = null)
        {
            _abilityTarget = abilityTarget;
            _walkScale = float.IsFinite(walkScale) && walkScale > 0f ? Mathf.Min(walkScale, 1f) : 1f;
            _isAttackPlaying = isAttackPlaying;
            _isAbilityReady = isAbilityReady;
            _playerPosition = playerPosition;
            _camera = camera;
            _equipWeapon = equipWeapon;
            _touchMove = touchMove;
            _touchAttack = touchAttack;
            _groundClick = groundClick;
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

        public void PollFrame()
        {
            var touchAttack = _touchAttack != null && _touchAttack();
            // На Android касание приходит и как mouse button 0; чтобы тапы по HUD/стику не
            // запускали удар, мышь учитываем только на desktop. Атака на телефоне — кнопка.
            // ЛКМ больше не атакует: это движение к курсору/точке и клик по луту.
            if (Input.GetKey(KeyCode.Space) || touchAttack)
            {
                _abilityPressed = true;
            }

            if (!Application.isMobilePlatform)
            {
                PollMouseDestination();
            }

            // M10.11: client-side weapon visuals only. The server ability is unchanged.
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                _equipWeapon?.Invoke(WeaponKind.Sword);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                _equipWeapon?.Invoke(WeaponKind.Axe);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                _equipWeapon?.Invoke(WeaponKind.Bow);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            {
                _equipWeapon?.Invoke(WeaponKind.Staff);
            }
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

            // Способность не зависит от движения: проверяется отдельно и в любом состоянии.
            // Пока Attack доигрывается ИЛИ кулдаун ещё идёт, не отправляем новую способность
            // и НЕ сбрасываем флаг. В это же время ниже вернётся MoveCommand, поэтому зажатые
            // WASD/ПКМ продолжают двигать персонажа во время атаки и кулдауна.
            if (_abilityPressed && _abilityTarget != null && !IsAttackPlaying() && IsAbilityReady())
            {
                // A missing/dead/out-of-range target is a legal miss. target 0 means "swing into
                // the void"; the server still charges cooldown/resource but deals no damage.
                _abilityPressed = false;
                _sequence++;
                command = new AbilityCommand(_abilityTarget(), clientTick, _sequence);
                return true;
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

        private System.Numerics.Vector2 ReadMovementDirection()
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

            var keyboard = new System.Numerics.Vector2(x, z);
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

        private void PollMouseDestination()
        {
            var left = Input.GetMouseButton(0);
            var right = Input.GetMouseButton(1);
            if (!left && !right)
            {
                return;
            }

            if (!TryGroundPoint(out var point))
            {
                return;
            }

            var clicked = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            _destination = point;
            _destinationActive = true;
            if (clicked)
            {
                _groundClick?.Invoke(point);
            }
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
