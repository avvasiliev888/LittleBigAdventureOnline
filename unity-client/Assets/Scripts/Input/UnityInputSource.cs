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
        private readonly float _walkScale;
        private readonly float _mouseMoveThreshold;
        private bool _abilityPressed;
        private uint _sequence;

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
        /// Клиентское переключение видимого оружия: 1 — меч, 2 — топор. Сервер не участвует,
        /// поэтому команда способности и протокол не меняются (M10.11).
        /// </param>
        public UnityInputSource(
            Func<uint>? abilityTarget = null,
            float walkScale = 1f,
            Func<bool>? isAttackPlaying = null,
            Func<bool>? isAbilityReady = null,
            Func<UnityVector3>? playerPosition = null,
            Func<UnityCamera?>? camera = null,
            float mouseMoveThreshold = 0.5f,
            Action<WeaponKind>? equipWeapon = null)
        {
            _abilityTarget = abilityTarget;
            _walkScale = float.IsFinite(walkScale) && walkScale > 0f ? Mathf.Min(walkScale, 1f) : 1f;
            _isAttackPlaying = isAttackPlaying;
            _isAbilityReady = isAbilityReady;
            _playerPosition = playerPosition;
            _camera = camera;
            _equipWeapon = equipWeapon;
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
        public void PollFrame()
        {
            if (Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0))
            {
                _abilityPressed = true;
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
        }

        /// <inheritdoc />
        public bool TryReadCommand(uint clientTick, out ICommand command)
        {
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
            if (!TryReadMouseDirection(out var mouse))
            {
                return keyboard;
            }

            // Мышь и клавиатура комбинируются: W + ПКМ дают диагональ, а противоположные
            // направления взаимно гасятся (персонаж останавливается).
            return keyboard + mouse;
        }

        private bool TryReadMouseDirection(out System.Numerics.Vector2 direction)
        {
            direction = System.Numerics.Vector2.Zero;
            if (!Input.GetMouseButton(1) || _camera == null || _playerPosition == null)
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

            var point = ray.GetPoint(enter);
            var player = _playerPosition();
            var dx = point.x - player.x;
            var dz = point.z - player.z;
            var distance = MathF.Sqrt((dx * dx) + (dz * dz));
            if (!float.IsFinite(distance) || distance <= _mouseMoveThreshold)
            {
                return false;
            }

            direction = new System.Numerics.Vector2(dx / distance, dz / distance);
            return true;
        }

        private bool IsAttackPlaying() => _isAttackPlaying != null && _isAttackPlaying();

        private bool IsAbilityReady() => _isAbilityReady == null || _isAbilityReady();
    }
}
