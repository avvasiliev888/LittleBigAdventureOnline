using System;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Команда движения: направление ввода на клиентском тике.
    /// Длина направления не нормализуется симуляцией — клиент обязан присылать единичный вектор,
    /// проверка величины ввода относится к валидатору сервера (M4, ADR-005).
    /// </summary>
    public sealed class MoveCommand : ICommand
    {
        /// <summary>Создаёт команду движения.</summary>
        /// <param name="direction">Направление в плоскости XZ: X — мировое X, Y — мировое Z.</param>
        /// <param name="clientTick">Клиентский тик, к которому относится команда.</param>
        /// <param name="sequence">Порядковый номер команды внутри тика.</param>
        /// <exception cref="ArgumentException">Если направление содержит NaN или бесконечность.</exception>
        public MoveCommand(Vector2 direction, uint clientTick, uint sequence)
        {
            if (!IsFinite(direction.X) || !IsFinite(direction.Y))
            {
                throw new ArgumentException("Направление содержит NaN или бесконечность.", nameof(direction));
            }

            Direction = direction;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Направление ввода: X — мировое X, Y — мировое Z.</summary>
        public Vector2 Direction { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
