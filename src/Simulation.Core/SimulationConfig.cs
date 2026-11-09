using System;

namespace Simulation.Core
{
    /// <summary>
    /// Игровые параметры симуляции, которые обязаны жить в конфиге, а не в коде (ADR-005).
    /// В M3 объект заполняется загрузчиком JSON; в M2 создаётся вызывающим кодом и тестами.
    /// </summary>
    public sealed class SimulationConfig
    {
        /// <summary>Создаёт конфигурацию с указанной скоростью движения.</summary>
        /// <param name="moveSpeed">Скорость движения в единицах мира в секунду; должна быть положительной и конечной.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если скорость не положительна или не конечна.</exception>
        public SimulationConfig(float moveSpeed)
        {
            if (!(moveSpeed > 0f) || float.IsInfinity(moveSpeed))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(moveSpeed),
                    moveSpeed,
                    "MoveSpeed должен быть конечным положительным числом.");
            }

            MoveSpeed = moveSpeed;
        }

        /// <summary>Скорость движения сущности в единицах мира в секунду.</summary>
        public float MoveSpeed { get; }
    }
}
