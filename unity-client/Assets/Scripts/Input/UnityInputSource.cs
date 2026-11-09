using System;
using System.Numerics;
using Client.Network;
using Simulation.Core;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Ввод с клавиатуры (WASD) → <see cref="MoveCommand"/>. Виртуальный стик в M6 не нужен,
    /// интерфейс <see cref="IInputSource"/> его допускает без изменений.
    /// </summary>
    public sealed class UnityInputSource : IInputSource
    {
        private uint _sequence;

        /// <inheritdoc />
        public bool TryReadCommand(uint clientTick, out ICommand command)
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
