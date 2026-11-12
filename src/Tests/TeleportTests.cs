using System;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// ADR-023: debug-телепорт — явная команда, а не скрытое изменение позиции.
    /// Сервер остаётся авторитетным, а симуляция зажимает точку границами мира.
    /// </summary>
    public sealed class TeleportTests
    {
        [Fact]
        public void Teleport_MovesEntityToRequestedPoint()
        {
            var config = TestWorld.Config();
            var state = new SimulationState(0, new[] { TestWorld.Entity(1, config) });
            var simulation = new CoreSimulation(state, config);

            simulation.Tick(SimulationConstants.FixedDeltaTime, new[]
            {
                new CommandEnvelope(1, new TeleportCommand(new Vector2(10f, -12f), 0, 1)),
            });

            Assert.True(simulation.State.TryGetEntity(1, out var entity));
            Assert.Equal(10f, entity!.Position.X, 3);
            Assert.Equal(-12f, entity.Position.Z, 3);
        }

        [Fact]
        public void Teleport_OutOfBounds_IsClampedBySimulation()
        {
            var config = TestWorld.Config();
            var state = new SimulationState(0, new[] { TestWorld.Entity(1, config) });
            var simulation = new CoreSimulation(state, config);

            simulation.Tick(SimulationConstants.FixedDeltaTime, new[]
            {
                new CommandEnvelope(1, new TeleportCommand(new Vector2(5000f, 0f), 0, 1)),
            });

            Assert.True(simulation.State.TryGetEntity(1, out var entity));
            Assert.True(entity!.Position.X <= config.Zones.MaxX);
            Assert.True(entity.Position.X >= config.Zones.MinX);
        }

        [Fact]
        public void Teleport_DeadEntity_IsIgnored()
        {
            var config = TestWorld.Config();
            var entity = TestWorld.Entity(1, config, x: 3f, z: 4f);
            entity.Hp = 0;
            var state = new SimulationState(0, new[] { entity });
            var simulation = new CoreSimulation(state, config);

            simulation.Tick(SimulationConstants.FixedDeltaTime, new[]
            {
                new CommandEnvelope(1, new TeleportCommand(new Vector2(10f, 10f), 0, 1)),
            });

            Assert.Equal(3f, entity.Position.X, 3);
            Assert.Equal(4f, entity.Position.Z, 3);
        }
    }
}
