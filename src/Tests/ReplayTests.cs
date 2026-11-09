using System;
using System.Collections.Generic;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Replay-тест (M2, ADR-007): 1000 команд с фиксированным seed, два независимых прогона
    /// дают идентичное финальное состояние. Это главный тест против недетерминированной симуляции.
    /// </summary>
    public sealed class ReplayTests
    {
        private const int CommandCount = 1000;
        private const int Seed = 20261108;

        [Fact]
        public void Replay_1000SeededCommands_ProducesIdenticalFinalState()
        {
            var first = Replay(Seed);
            var second = Replay(Seed);

            SnapshotAssert.Equal(first.CreateSnapshot(), second.CreateSnapshot());
            Assert.Equal((uint)CommandCount, first.State.Tick);
        }

        [Fact]
        public void Replay_DifferentSeed_ProducesDifferentState()
        {
            var first = Replay(Seed);
            var second = Replay(Seed + 1);

            Assert.NotEqual(first.State.Entities[0].Position, second.State.Entities[0].Position);
        }

        private static CoreSimulation Replay(int seed)
        {
            var commands = BuildCommands(seed);
            var config = TestWorld.Config(moveSpeed: 5f);
            var simulation = TestWorld.Simulation(config, Entities(config));

            foreach (var tickCommands in commands)
            {
                simulation.Tick(SimulationConstants.FixedDeltaTime, tickCommands);
            }

            return simulation;
        }

        /// <summary>Строит по одной команде на тик для трёх сущностей, детерминированно от seed.</summary>
        private static List<CommandEnvelope[]> BuildCommands(int seed)
        {
            var random = new Random(seed);
            var result = new List<CommandEnvelope[]>(CommandCount);

            for (var tick = 0u; tick < CommandCount; tick++)
            {
                var entityId = (uint)(tick % 3) + 1;
                var direction = new Vector2(
                    (float)((random.NextDouble() * 2d) - 1d),
                    (float)((random.NextDouble() * 2d) - 1d));

                result.Add(new[] { new CommandEnvelope(entityId, new MoveCommand(direction, tick, 0)) });
            }

            return result;
        }

        private static EntityState[] Entities(SimulationConfig config)
        {
            return new[]
            {
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config),
                TestWorld.Entity(3, config),
            };
        }
    }
}
