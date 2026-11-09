using System;
using System.Numerics;
using Client.Network;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>M6: буфер снапшотов и интерполяция между двумя последними.</summary>
    public sealed class SnapshotBufferTests
    {
        [Fact]
        public void EmptyBuffer_ReturnsNoEntities()
        {
            var buffer = new SnapshotBuffer();

            Assert.Empty(buffer.Sample(3.5f));
            Assert.Equal(0u, buffer.LatestTick);
        }

        [Fact]
        public void SingleSnapshot_IsProjectedWithoutInterpolation()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snapshot(10, Entity(1, 5f)));

            var sampled = buffer.Sample(10.7f);

            var entity = Assert.Single(sampled);
            Assert.Equal(5f, entity.Position.X);
        }

        [Fact]
        public void Midpoint_IsInterpolated()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snapshot(10, Entity(1, 0f)));
            buffer.Add(Snapshot(12, Entity(1, 4f)));

            var atEleven = Assert.Single(buffer.Sample(11f));
            Assert.Equal(2f, atEleven.Position.X, 3);

            var atTenAndHalf = Assert.Single(buffer.Sample(10.5f));
            Assert.Equal(1f, atTenAndHalf.Position.X, 3);
        }

        [Fact]
        public void BeforeFirstAndAfterLatest_AreClamped()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snapshot(10, Entity(1, 1f)));
            buffer.Add(Snapshot(12, Entity(1, 3f)));

            Assert.Equal(1f, Assert.Single(buffer.Sample(0f)).Position.X);
            Assert.Equal(3f, Assert.Single(buffer.Sample(999f)).Position.X);
        }

        [Fact]
        public void StaleAndDuplicateSnapshots_AreIgnored()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snapshot(10, Entity(1, 1f)));
            buffer.Add(Snapshot(10, Entity(1, 99f)));
            buffer.Add(Snapshot(9, Entity(1, 99f)));

            Assert.Equal(1, buffer.Count);
            Assert.Equal(10u, buffer.LatestTick);
        }

        [Fact]
        public void Capacity_DropsOldestSnapshots()
        {
            var buffer = new SnapshotBuffer(capacity: 3);
            for (var tick = 1; tick <= 5; tick++)
            {
                buffer.Add(Snapshot((uint)tick, Entity(1, tick)));
            }

            Assert.Equal(3, buffer.Count);
            Assert.Equal(3u, buffer.EarliestTick);
            Assert.Equal(5u, buffer.LatestTick);
        }

        [Fact]
        public void NewEntity_AppearsInSecondHalfOfInterval()
        {
            var buffer = new SnapshotBuffer();
            buffer.Add(Snapshot(10, Entity(1, 0f)));
            buffer.Add(Snapshot(11, Entity(1, 1f), Entity(2, 3f)));

            Assert.Single(buffer.Sample(10.2f));
            Assert.Equal(2, buffer.Sample(10.8f).Count);
        }

        [Fact]
        public void Constructor_RejectsTooSmallCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotBuffer(capacity: 1));
        }

        [Fact]
        public void Add_Null_Throws()
        {
            var buffer = new SnapshotBuffer();

            Assert.Throws<ArgumentNullException>(() => buffer.Add(null!));
        }

        private static SimulationSnapshot Snapshot(uint tick, params EntitySnapshot[] entities)
        {
            return new SimulationSnapshot(tick, entities);
        }

        private static EntitySnapshot Entity(uint id, float x)
        {
            return new EntitySnapshot(
                id,
                1,
                new Vector3(x, 0f, 0f),
                Vector3.Zero,
                100,
                100,
                100,
                100,
                0,
                0,
                false);
        }
    }
}
