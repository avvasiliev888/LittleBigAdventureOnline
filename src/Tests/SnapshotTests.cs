using System;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>Снапшоты: создание и восстановление состояния 1:1 (M2).</summary>
    public sealed class SnapshotTests
    {
        [Fact]
        public void CreateSnapshot_OfEmptyWorld_KeepsTick()
        {
            var simulation = new CoreSimulation(new SimulationConfig(1f));
            simulation.Tick(SimulationConstants.FixedDeltaTime, Array.Empty<CommandEnvelope>());

            var snapshot = simulation.CreateSnapshot();

            Assert.Equal(1u, snapshot.Tick);
            Assert.Empty(snapshot.Entities);
        }

        [Fact]
        public void RestoreFromSnapshot_RestoresEveryField()
        {
            var state = new SimulationState(
                42,
                new[]
                {
                    new EntityState(7, new Vector3(1.5f, 0f, -2.5f), 120),
                    new EntityState(9, new Vector3(-3f, 0f, 4f), 80),
                });
            state.Entities[0].Hp = 13;
            state.Entities[1].Hp = 0;

            var source = new CoreSimulation(state, new SimulationConfig(2f));
            var snapshot = source.CreateSnapshot();

            var restored = new CoreSimulation(new SimulationConfig(2f));
            restored.RestoreFromSnapshot(snapshot);

            Assert.Equal(42u, restored.State.Tick);
            Assert.Equal(2, restored.State.Entities.Count);

            var first = restored.State.Entities[0];
            Assert.Equal(7u, first.Id);
            Assert.Equal(new Vector3(1.5f, 0f, -2.5f), first.Position);
            Assert.Equal(13, first.Hp);
            Assert.Equal(120, first.MaxHp);
            Assert.False(first.IsDead);

            var second = restored.State.Entities[1];
            Assert.Equal(9u, second.Id);
            Assert.Equal(80, second.MaxHp);
            Assert.Equal(0, second.Hp);
            Assert.True(second.IsDead);
        }

        [Fact]
        public void RestoreFromSnapshot_WithNull_Throws()
        {
            var simulation = new CoreSimulation(new SimulationConfig(1f));

            Assert.Throws<ArgumentNullException>(() => simulation.RestoreFromSnapshot(null!));
        }

        [Fact]
        public void Snapshot_IsDetachedFromLiveState()
        {
            var state = new SimulationState(0, new[] { new EntityState(1, Vector3.Zero, 100) });
            var simulation = new CoreSimulation(state, new SimulationConfig(1f));

            var snapshot = simulation.CreateSnapshot();
            state.Entities[0].Position = new Vector3(9f, 0f, 9f);

            Assert.Equal(Vector3.Zero, snapshot.Entities[0].Position);
        }
    }
}
