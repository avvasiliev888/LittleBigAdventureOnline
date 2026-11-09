using System;
using System.Collections.Generic;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Smoke-тесты M1: симуляция создаётся, тик не падает, снапшот восстанавливается.
    /// Это минимум, который обязана проходить любая сборка (Auto-Gate M1).
    /// </summary>
    public sealed class SimulationSmokeTests
    {
        [Fact]
        public void NewSimulation_StartsAtTickZero_WithoutEntities()
        {
            var simulation = new CoreSimulation();

            Assert.Equal(0u, simulation.State.Tick);
            Assert.Empty(simulation.State.Entities);
        }

        [Fact]
        public void Tick_WithoutCommands_IncrementsTick_AndDoesNotThrow()
        {
            var simulation = new CoreSimulation();

            simulation.Tick(SimulationConstants.FixedDeltaTime, Array.Empty<CommandEnvelope>());
            simulation.Tick(SimulationConstants.FixedDeltaTime, Array.Empty<CommandEnvelope>());

            Assert.Equal(2u, simulation.State.Tick);
        }

        [Fact]
        public void Tick_WithCommand_IsAcceptedWithoutGameplayLogic()
        {
            var simulation = new CoreSimulation();
            var commands = new List<CommandEnvelope>
            {
                new CommandEnvelope(1, new TestCommand(clientTick: 0, sequence: 0)),
            };

            simulation.Tick(SimulationConstants.FixedDeltaTime, commands);

            Assert.Equal(1u, simulation.State.Tick);
        }

        [Fact]
        public void Tick_WithInvalidDeltaTime_Throws()
        {
            var simulation = new CoreSimulation();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => simulation.Tick(0f, Array.Empty<CommandEnvelope>()));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => simulation.Tick(float.NaN, Array.Empty<CommandEnvelope>()));
        }

        [Fact]
        public void FixedDeltaTime_MatchesTickRate()
        {
            Assert.Equal(0.05f, SimulationConstants.FixedDeltaTime);
            Assert.Equal(20, SimulationConstants.TickRate);
        }

        [Fact]
        public void CreateSnapshot_ThenRestore_KeepsTickAndEntities()
        {
            var state = new SimulationState(
                7,
                new[]
                {
                    new EntityState(10, new Vector3(1.5f, 0f, -2.5f), 100),
                    new EntityState(20, new Vector3(0f, 0f, 0f), 80),
                });
            var simulation = new CoreSimulation(state);
            simulation.State.Entities[0].Hp = 42;

            var snapshot = simulation.CreateSnapshot();

            Assert.Equal(7u, snapshot.Tick);
            Assert.Equal(2, snapshot.Entities.Count);
            Assert.Equal(42, snapshot.Entities[0].Hp);

            var restored = new CoreSimulation();
            restored.RestoreFromSnapshot(snapshot);

            Assert.Equal(7u, restored.State.Tick);
            Assert.Equal(2, restored.State.Entities.Count);
            Assert.True(restored.State.TryGetEntity(10, out var entity));
            Assert.Equal(42, entity!.Hp);
            Assert.Equal(new Vector3(1.5f, 0f, -2.5f), entity.Position);
        }

        private sealed class TestCommand : ICommand
        {
            public TestCommand(uint clientTick, uint sequence)
            {
                ClientTick = clientTick;
                Sequence = sequence;
            }

            public uint ClientTick { get; }

            public uint Sequence { get; }
        }
    }
}
