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
                Envelope(entity: 1, tick: 0, sequence: 0),
                Envelope(entity: 2, tick: 0, sequence: 0),
                Envelope(entity: 1, tick: 0, sequence: 1),
                Envelope(entity: 2, tick: 0, sequence: 1),
            };

            var direct = new CoreSimulation(World(), Config());
            direct.Tick(SimulationConstants.FixedDeltaTime, CommandOrdering.Sort(commands));

            var reversed = new List<CommandEnvelope>(commands);
            reversed.Reverse();

            var shuffled = new CoreSimulation(World(), Config());
            shuffled.Tick(SimulationConstants.FixedDeltaTime, CommandOrdering.Sort(reversed));

            SnapshotAssert.Equal(direct.CreateSnapshot(), shuffled.CreateSnapshot());
        }

        [Fact]
        public void Tick_IsMonotonic_AndIncreasesByOne()
        {
            var simulation = new CoreSimulation(World(), Config());

            for (var i = 0; i < Ticks; i++)
            {
                simulation.Tick(SimulationConstants.FixedDeltaTime, Array.Empty<CommandEnvelope>());
                Assert.Equal((uint)i + 1, simulation.State.Tick);
            }
        }

        private static CoreSimulation RunScenario(int seed)
        {
            var random = new Random(seed);
            var simulation = new CoreSimulation(World(), Config());

            for (var tick = 0u; tick < Ticks; tick++)
            {
                var commands = new[]
                {
                    new CommandEnvelope(
                        1,
                        new MoveCommand(
                            new System.Numerics.Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f),
                            tick,
                            0)),
                    new CommandEnvelope(
                        2,
                        new MoveCommand(
                            new System.Numerics.Vector2((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f),
                            tick,
                            0)),
                };

                simulation.Tick(SimulationConstants.FixedDeltaTime, commands);
            }

            return simulation;
        }

        private static SimulationState World()
        {
            return new SimulationState(
                0,
                new[]
                {
                    new EntityState(1, System.Numerics.Vector3.Zero, 100),
                    new EntityState(2, System.Numerics.Vector3.Zero, 100),
                });
        }

        private static SimulationConfig Config() => new SimulationConfig(moveSpeed: 3f);

        private static CommandEnvelope Envelope(uint entity, uint tick, uint sequence)
        {
            return new CommandEnvelope(entity, new MoveCommand(new System.Numerics.Vector2(0f, 1f), tick, sequence));
        }
    }
}
