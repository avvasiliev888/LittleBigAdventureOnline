using System;

namespace Simulation.Core
{
    /// <summary>
    /// Константы темпа симуляции. Зафиксированы ADR-003: тик 20 Гц, шаг 0.05 с.
    /// </summary>
    public static class SimulationConstants
    {
        /// <summary>Частота тика симуляции в герцах. Изменение — только через новый ADR.</summary>
        public const int TickRate = 20;

        /// <summary>Фиксированный шаг симуляции в секундах: <c>1 / TickRate</c>.</summary>
        public const float FixedDeltaTime = 1f / TickRate;
    }
}
