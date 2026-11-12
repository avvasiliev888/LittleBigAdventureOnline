using System;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Тестовая команда мгновенного перемещения в точку карты (ADR-023).
    /// Это не игровая способность: сервер принимает её только при включённом
    /// debug-телепорте и только внутри границ мира.
    /// </summary>
    public sealed class TeleportCommand : ICommand
    {
        /// <summary>Создаёт команду телепорта.</summary>
        /// <param name="destination">Точка в плоскости XZ: X — мировое X, Y — мировое Z.</param>
        /// <param name="clientTick">Клиентский тик, к которому относится команда.</param>
        /// <param name="sequence">Порядковый номер команды внутри тика.</param>
        /// <exception cref="ArgumentException">Если координаты содержат NaN или бесконечность.</exception>
        public TeleportCommand(Vector2 destination, uint clientTick, uint sequence)
        {
            if (!IsFinite(destination.X) || !IsFinite(destination.Y))
            {
                throw new ArgumentException("Точка телепорта содержит NaN или бесконечность.", nameof(destination));
            }

            Destination = destination;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Точка назначения: X — мировое X, Y — мировое Z.</summary>
        public Vector2 Destination { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
