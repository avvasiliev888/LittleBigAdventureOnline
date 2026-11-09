using System;
using System.Collections.Generic;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Детерминизм (ADR-003, ADR-007): одинаковый ввод даёт одинаковое состояние,
    /// порядок прихода команд не влияет на результат после канонической сортировки.
    /// </summary>
    public sealed class DeterminismTests
    {
        private const int Ticks = 100;

        [Fact]
        public void SameInput_For100Ticks_ProducesIdenticalSnapshots()
        {
            var first = RunScenario(seed: 1234);
            var second = RunScenario(seed: 1234);

            SnapshotAssert.Equal(first.CreateSnapshot(), second.CreateSnapshot());
        }

        [Fact]
        public void ShuffledCommands_AfterCanonicalSort_ProduceSameState()
        {
            var commands = new List<CommandEnvelope>
            {
                TestWorld.Move(1, 0f, 1f, 0, 0),
                TestWorld.Move(2, 0f, 1f, 0, 0),
                TestWorld.Move(1, 1f, 0f, 0, 1),
                TestWorld.Move(2, 1f, 0f, 0, 1),
            };

            var direct = TestWorld.Simulation(TestWorld.Config(moveSpeed: 3f), Entities());
            TestWorld.Tick(direct, CommandOrdering.Sort(commands));

            var reversed = new List<CommandEnvelope>(commands);
            reversed.Reverse();

            var shuffled = TestWorld.Simulation(TestWorld.Config(moveSpeed: 3f), Entities());
            TestWorld.Tick(shuffled, CommandOrdering.Sort(reversed));

            SnapshotAssert.Equal(direct.CreateSnapshot(), shuffled.CreateSnapshot());
        }

        [Fact]
        public void Tick_IsMonotonic_AndIncreasesByOne()
        {
            var simulation = TestWorld.Simulation(TestWorld.Config(moveSpeed: 3f), Entities());

            for (var i = 0; i < Ticks; i++)
            {
                simulation.Tick(SimulationConstants.FixedDeltaTime, Array.Empty<CommandEnvelope>());
                Assert.Equal((uint)i + 1, simulation.State.Tick);
            }
        }

        private static CoreSimulation RunScenario(int seed)
        {
            var random = new Random(seed);
            var config = TestWorld.Config(moveSpeed: 3f);
            var simulation = TestWorld.Simulation(config, Entities());

            for (var tick = 0u; tick < Ticks; tick++)
            {
                var commands = new[]
                {
                    TestWorld.Move(1, NextAxis(random), NextAxis(random), tick, 0),
                    TestWorld.Move(2, NextAxis(random), NextAxis(random), tick, 0),
                };

                simulation.Tick(SimulationConstants.FixedDeltaTime, commands);
            }

            return simulation;
        }

        private static float NextAxis(Random random) => (float)((random.NextDouble() * 2d) - 1d);

        private static EntityState[] Entities()
        {
            var config = TestWorld.Config(moveSpeed: 3f);
            return new[] { TestWorld.Entity(1, config), TestWorld.Entity(2, config) };
        }
    }
}
