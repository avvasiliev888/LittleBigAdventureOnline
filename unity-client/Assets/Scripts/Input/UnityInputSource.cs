using System;
using System.Numerics;
using Client.Network;
using Simulation.Core;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Ввод с клавиатуры (WASD) → <see cref="MoveCommand"/>; Space → <see cref="AbilityCommand"/> (M9).
    /// Нажатие Space запоминается в <see cref="PollFrame"/> (вызывается каждый кадр) и живёт до
    /// ближайшего клиентского тика: иначе GetKeyDown терялся на кадрах, где тик не выполнялся,
    /// и способность «через раз» не срабатывала (M9, баг Human-Gate).
    /// </summary>
    public sealed class UnityInputSource : IInputSource
    {
        private readonly Func<uint>? _abilityTarget;
        private bool _abilityPressed;
        private uint _sequence;

        /// <summary>Создаёт источник ввода.</summary>
        /// <param name="abilityTarget">
        /// Поставщик цели способности: ближайшая сущность или 0, если цели ещё нет (M9).
        /// Без него Space не отправляет команду.
        /// </param>
        public UnityInputSource(Func<uint>? abilityTarget = null)
        {
            _abilityTarget = abilityTarget;
        }

        /// <summary>
        /// Вызывается каждый кадр из <c>ClientRuntime.Update</c> до <c>ClientPipeline.Update</c>:
        /// фиксирует фронт нажатия Space, чтобы он не потерялся между клиентскими тиками (20 Гц).
        /// </summary>
        public void PollFrame()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _abilityPressed = true;
            }
        }

        /// <inheritdoc />
        public bool TryReadCommand(uint clientTick, out ICommand command)
        {
            // Способность не зависит от движения: проверяется отдельно и в любом состоянии.
            if (_abilityPressed && _abilityTarget != null)
            {
                var target = _abilityTarget();
                if (target != 0)
                {
                    _abilityPressed = false;
                    _sequence++;
                    command = new AbilityCommand(target, clientTick, _sequence);
                    return true;
                }
            }

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

            if (x == 0f && z == 0f)
            {
                command = null;
                return false;
            }

            var direction = new System.Numerics.Vector2(x, z);
            direction = System.Numerics.Vector2.Normalize(direction);

            _sequence++;
            command = new MoveCommand(direction, clientTick, _sequence);
            return true;
        }
    }
}
