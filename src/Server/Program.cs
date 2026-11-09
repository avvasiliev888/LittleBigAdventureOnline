using System;
using Simulation.Core;

namespace Server
{
    /// <summary>
    /// Точка входа серверного процесса. M1: каркас, который доказывает, что симуляция
    /// создаётся и тикает. Серверный цикл, транспорт и валидация — M4–M5.
    /// </summary>
    public static class Program
    {
        /// <summary>Запускает серверный процесс.</summary>
        /// <param name="args">Аргументы командной строки (пока не используются).</param>
        /// <returns>Код возврата процесса.</returns>
        public static int Main(string[] args)
        {
            var simulation = new Simulation.Core.Simulation();

            simulation.Tick(SimulationConstants.FixedDeltaTime, Array.Empty<CommandEnvelope>());

            Console.WriteLine(
                $"server skeleton: tick={simulation.State.Tick}, entities={simulation.State.Entities.Count}, deltaTime={SimulationConstants.FixedDeltaTime}");

            return 0;
        }
    }
}
