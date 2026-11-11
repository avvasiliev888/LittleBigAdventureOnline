using System;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>Снапшоты: создание и восстановление состояния 1:1 (M2, M3).</summary>
    public sealed class SnapshotTests
    {
        [Fact]
        public void CreateSnapshot_OfEmptyWorld_KeepsTick()
        {
            var config = TestWorld.Config();
            var simulation = new CoreSimulation(config);
            simulation.Tick(SimulationConstants.FixedDeltaTime, Array.Empty<CommandEnvelope>());

            var snapshot = simulation.CreateSnapshot();

            Assert.Equal(1u, snapshot.Tick);
            Assert.Empty(snapshot.Entities);
        }

        [Fact]
        public void CreateSnapshot_WithoutConfig_ReportsZeroZone()
        {
            var config = TestWorld.Config();
            var simulation = new CoreSimulation();
            simulation.State.AddEntity(TestWorld.Entity(1, config, x: 5f, z: -5f));

            var snapshot = simulation.CreateSnapshot();

            Assert.Equal(0u, Assert.Single(snapshot.Entities).ZoneId);
        }

        [Fact]
        public void CreateSnapshot_OutsideZones_ReportsZeroZone()
        {
            var config = TestWorld.Config();
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config, x: 5000f, z: 5000f));

            var snapshot = simulation.CreateSnapshot();

            Assert.Equal(0u, Assert.Single(snapshot.Entities).ZoneId);
        }

        [Fact]
        public void CreateSnapshot_CopiesCorpses()
        {
            var config = TestWorld.Config();
            var corpse = new CorpseState(3, new Vector3(4f, 0f, 5f), 2.5f, 12,
                new[] { new ItemStack(1, 2) }, 500);
            var state = new SimulationState(9, new[] { TestWorld.Entity(1, config) }, new[] { corpse });
            var simulation = new CoreSimulation(state, config);

            var snapshot = simulation.CreateSnapshot();
            var copy = Assert.Single(snapshot.Corpses);

            Assert.Equal(3u, copy.Id);
            Assert.Equal(12, copy.Silver);
            Assert.Equal(500u, copy.ExpireAtTick);
            Assert.Equal(2, Assert.Single(copy.Loot).Quantity);
        }

        [Fact]
        public void RestoreFromSnapshot_RestoresEveryField()
        {
            var config = TestWorld.Config(maxHp: 120);
            var first = TestWorld.Entity(7, config, x: 1.5f, z: -2.5f);
            first.Hp = 13;
            first.Resource = 40;
            first.CooldownUntilTick = 77;

            var second = TestWorld.Entity(9, config, x: -3f, z: 4f);
            second.Hp = 0;
            second.RespawnAtTick = 150;

            var simulation = TestWorld.Simulation(config, first, second);
            simulation = new CoreSimulation(new SimulationState(42, new[] { first, second }), config);

            var restored = new CoreSimulation(config);
            restored.RestoreFromSnapshot(simulation.CreateSnapshot());

            Assert.Equal(42u, restored.State.Tick);
            Assert.Equal(2, restored.State.Entities.Count);

            var left = restored.State.Entities[0];
            Assert.Equal(7u, left.Id);
            Assert.Equal(1.5f, left.Position.X);
            Assert.Equal(-2.5f, left.Position.Z);
            Assert.Equal(13, left.Hp);
            Assert.Equal(120, left.MaxHp);
            Assert.Equal(40, left.Resource);
            Assert.Equal(77u, left.CooldownUntilTick);
            Assert.False(left.IsDead);

            var right = restored.State.Entities[1];
            Assert.Equal(9u, right.Id);
            Assert.Equal(0, right.Hp);
            Assert.Equal(150u, right.RespawnAtTick);
            Assert.True(right.IsDead);
        }

        [Fact]
        public void RestoreFromSnapshot_WithNull_Throws()
        {
            var simulation = new CoreSimulation(TestWorld.Config());

            Assert.Throws<ArgumentNullException>(() => simulation.RestoreFromSnapshot(null!));
        }

        [Fact]
        public void RestoreFromSnapshot_WithoutConfig_Throws()
        {
            var simulation = new CoreSimulation();
            var snapshot = new SimulationSnapshot(0, Array.Empty<EntitySnapshot>());

            Assert.Throws<InvalidOperationException>(() => simulation.RestoreFromSnapshot(snapshot));
        }

        [Fact]
        public void Snapshot_IsDetachedFromLiveState()
        {
            var config = TestWorld.Config();
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config));

            var snapshot = simulation.CreateSnapshot();
            simulation.State.Entities[0].Position = new Vector3(9f, 0f, 9f);

            Assert.Equal(Vector3.Zero, snapshot.Entities[0].Position);
        }
    }
}
