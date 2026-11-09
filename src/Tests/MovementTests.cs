using System;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>Движение: применение <see cref="MoveCommand"/> с шагом и скоростью из конфига.</summary>
    public sealed class MovementTests
    {
        [Fact]
        public void MoveCommand_MovesEntityBySpeedTimesDeltaTime()
        {
            var state = new SimulationState(0, new[] { new EntityState(1, Vector3.Zero, 100) });
            var simulation = new CoreSimulation(state, new SimulationConfig(moveSpeed: 4f));

            simulation.Tick(SimulationConstants.FixedDeltaTime, Pack(new MoveCommand(new Vector2(1f, 0f), 0, 1)));

            // 4 ед/с * 0.05 с = 0.2 по X.
            SnapshotAssert.Equal(new Vector3(0.2f, 0f, 0f), simulation.State.Entities[0].Position);
        }

        [Fact]
        public void MoveCommand_DiagonalDirection_UsesBothAxes()
        {
            var state = new SimulationState(0, new[] { new EntityState(1, Vector3.Zero, 100) });
            var simulation = new CoreSimulation(state, new SimulationConfig(moveSpeed: 2f));

            simulation.Tick(SimulationConstants.FixedDeltaTime, Pack(new MoveCommand(new Vector2(0f, 1f), 0, 1)));

            SnapshotAssert.Equal(new Vector3(0f, 0f, 0.1f), simulation.State.Entities[0].Position);
        }

        [Fact]
        public void MoveCommand_ForUnknownEntity_IsIgnored()
        {
            var simulation = new CoreSimulation(new SimulationConfig(moveSpeed: 3f));

            simulation.Tick(SimulationConstants.FixedDeltaTime, Pack(new MoveCommand(Vector2.One, 0, 1)));

            Assert.Equal(1u, simulation.State.Tick);
            Assert.Empty(simulation.State.Entities);
        }

        [Fact]
        public void Tick_WithCommandsAndNoConfig_Throws()
        {
            var simulation = new CoreSimulation();

            Assert.Throws<InvalidOperationException>(
                () => simulation.Tick(SimulationConstants.FixedDeltaTime, Pack(new MoveCommand(Vector2.One, 0, 1))));
        }

        [Fact]
        public void Tick_WithUnsupportedCommand_Throws()
        {
            var state = new SimulationState(0, new[] { new EntityState(1, Vector3.Zero, 100) });
            var simulation = new CoreSimulation(state, new SimulationConfig(moveSpeed: 1f));

            Assert.Throws<NotSupportedException>(
                () => simulation.Tick(
                    SimulationConstants.FixedDeltaTime,
                    new[] { new CommandEnvelope(1, new UnknownCommand()) }));
        }

        [Fact]
        public void Tick_WithNullCommands_Throws()
        {
            var simulation = new CoreSimulation();

            Assert.Throws<ArgumentNullException>(
                () => simulation.Tick(SimulationConstants.FixedDeltaTime, null!));
        }

        [Fact]
        public void Config_WithNonPositiveSpeed_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationConfig(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationConfig(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationConfig(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationConfig(float.PositiveInfinity));
        }

        [Fact]
        public void MoveCommand_WithNonFiniteDirection_Throws()
        {
            Assert.Throws<ArgumentException>(() => new MoveCommand(new Vector2(float.NaN, 0f), 0, 1));
            Assert.Throws<ArgumentException>(() => new MoveCommand(new Vector2(0f, float.PositiveInfinity), 0, 1));
        }

        [Fact]
        public void Simulation_WithNullState_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new CoreSimulation((SimulationState)null!, new SimulationConfig(1f)));
        }

        private static CommandEnvelope[] Pack(params ICommand[] commands)
        {
            var packed = new CommandEnvelope[commands.Length];
            for (var i = 0; i < commands.Length; i++)
            {
                packed[i] = new CommandEnvelope(1, commands[i]);
            }

            return packed;
        }

        private sealed class UnknownCommand : ICommand
        {
            public uint ClientTick => 0;

            public uint Sequence => 0;
        }
    }
}
