using System;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>Движение: скорость берётся из конфига актора, а не из команды.</summary>
    public sealed class MovementTests
    {
        [Fact]
        public void MoveCommand_MovesEntityBySpeedTimesDeltaTime()
        {
            var config = TestWorld.Config(moveSpeed: 4f);
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config));

            TestWorld.Tick(simulation, TestWorld.Move(1, 1f, 0f));

            // 4 ед/с * 0.05 с = 0.2 по X.
            SnapshotAssert.Equal(new Vector3(0.2f, 0f, 0f), simulation.State.Entities[0].Position);
        }

        [Fact]
        public void MoveCommand_UsesDirectionAsWorldXZ()
        {
            var config = TestWorld.Config(moveSpeed: 2f);
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config));

            TestWorld.Tick(simulation, TestWorld.Move(1, 0f, 1f));

            SnapshotAssert.Equal(new Vector3(0f, 0f, 0.1f), simulation.State.Entities[0].Position);
        }

        [Fact]
        public void MoveCommand_ForDeadEntity_IsIgnored()
        {
            var config = TestWorld.Config();
            var entity = TestWorld.Entity(1, config);
            entity.Hp = 0;
            var simulation = TestWorld.Simulation(config, entity);

            TestWorld.Tick(simulation, TestWorld.Move(1, 1f, 0f));

            Assert.Equal(Vector3.Zero, simulation.State.Entities[0].Position);
        }

        [Fact]
        public void MoveCommand_ForUnknownEntity_IsIgnored()
        {
            var config = TestWorld.Config();
            var simulation = TestWorld.Simulation(config);

            TestWorld.Tick(simulation, TestWorld.Move(42, 1f, 0f));

            Assert.Equal(1u, simulation.State.Tick);
            Assert.Empty(simulation.State.Entities);
        }

        [Fact]
        public void Tick_WithCommandsAndNoConfig_Throws()
        {
            var simulation = new CoreSimulation();

            Assert.Throws<InvalidOperationException>(() => TestWorld.Tick(simulation, TestWorld.Move(1, 1f, 0f)));
        }

        [Fact]
        public void Tick_WithUnsupportedCommand_Throws()
        {
            var config = TestWorld.Config();
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config));

            Assert.Throws<NotSupportedException>(
                () => TestWorld.Tick(
                    simulation,
                    new CommandEnvelope(1, new UnknownCommand())));
        }

        [Fact]
        public void Tick_WithNullCommands_Throws()
        {
            var simulation = new CoreSimulation();

            Assert.Throws<ArgumentNullException>(
                () => simulation.Tick(SimulationConstants.FixedDeltaTime, null!));
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
                () => new CoreSimulation((SimulationState)null!, TestWorld.Config()));
        }

        private sealed class UnknownCommand : ICommand
        {
            public uint ClientTick => 0;

            public uint Sequence => 0;
        }
    }
}
