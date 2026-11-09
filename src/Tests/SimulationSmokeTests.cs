using System;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Базовые тесты симуляции: создание, продвижение тика, валидация аргументов.
    /// Гарантируют, что каркас M1/M2 не сломан последующими вехами.
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
        public void Tick_WithInvalidDeltaTime_Throws()
        {
            var simulation = new CoreSimulation();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => simulation.Tick(0f, Array.Empty<CommandEnvelope>()));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => simulation.Tick(float.NaN, Array.Empty<CommandEnvelope>()));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => simulation.Tick(float.PositiveInfinity, Array.Empty<CommandEnvelope>()));
        }

        [Fact]
        public void FixedDeltaTime_MatchesTickRate()
        {
            Assert.Equal(0.05f, SimulationConstants.FixedDeltaTime);
            Assert.Equal(20, SimulationConstants.TickRate);
        }

        [Fact]
        public void Simulation_WithStateOnly_KeepsEntities()
        {
            var config = TestWorld.Config();
            var state = new SimulationState(3, new[] { TestWorld.Entity(1, config) });

            var simulation = new CoreSimulation(state);

            Assert.Equal(3u, simulation.State.Tick);
            Assert.Single(simulation.State.Entities);
        }

        [Fact]
        public void Simulation_WithState_KeepsEntities()
        {
            var config = TestWorld.Config(maxHp: 100);
            var state = new SimulationState(
                7,
                new[]
                {
                    TestWorld.Entity(10, config, x: 1.5f, z: -2.5f),
                    TestWorld.Entity(20, config),
                });

            var simulation = new CoreSimulation(state, config);

            Assert.Equal(7u, simulation.State.Tick);
            Assert.Equal(2, simulation.State.Entities.Count);
            Assert.True(simulation.State.TryGetEntity(10, out var entity));
            Assert.Equal(100, entity!.MaxHp);
            Assert.False(simulation.State.TryGetEntity(999, out _));
        }
    }
}
