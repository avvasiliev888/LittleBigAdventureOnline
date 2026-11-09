using System;
using System.Collections.Generic;
using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// M8: дельта-снапшоты (ADR-010). Дельта обязана восстанавливать ровно то же состояние, что и
    /// полный снапшот, и быть заметно меньше него на обычном движении (AI-Failure Mode: «дельта на
    /// всякий случай полная» ловится тестом на размер).
    /// </summary>
    public sealed class DeltaSnapshotTests
    {
        private readonly BinarySnapshotSerializer _serializer = new BinarySnapshotSerializer();

        [Fact]
        public void Delta_RoundTrip_EqualsFullSnapshot()
        {
            var baseline = new SimulationSnapshot(10, new[]
            {
                Entity(1, 1.0f, 1.5f, hp: 100),
                Entity(2, 0f, 0f, hp: 80),
            });

            var current = new SimulationSnapshot(11, new[]
            {
                Entity(1, 1.2f, 1.5f, hp: 100),   // сдвинулся
                Entity(3, -4f, 2f, hp: 50),       // новая сущность
            });                                   // сущность 2 удалена

            var delta = _serializer.SerializeDelta(baseline, current);
            var restored = _serializer.ApplyDelta(baseline, delta);

            SnapshotAssert.Equal(current, restored);
        }

        [Fact]
        public void Delta_IsSmallerThanFullSnapshot()
        {
            var baseline = new SimulationSnapshot(100, new[]
            {
                Entity(1, 2f, 0f, hp: 100),
                Entity(2, -3f, 1f, hp: 70),
            });
            var current = new SimulationSnapshot(101, new[]
            {
                Entity(1, 2.2f, 0f, hp: 100),
                Entity(2, -2.8f, 1f, hp: 70),
            });

            var full = _serializer.Serialize(current);
            var delta = _serializer.SerializeDelta(baseline, current);

            Assert.True(delta.Length < full.Length,
                "дельта не меньше полного снапшота: delta=" + delta.Length + ", full=" + full.Length);
            Assert.True(delta.Length < full.Length / 2,
                "дельта подозрительно большая: delta=" + delta.Length + ", full=" + full.Length);
        }

        [Fact]
        public void Delta_WithNoChanges_IsHeaderOnly()
        {
            var snapshot = new SimulationSnapshot(5, new[] { Entity(1, 0f, 0f) });

            var delta = _serializer.SerializeDelta(snapshot, new SimulationSnapshot(6, snapshot.Entities));

            Assert.Equal(BinarySnapshotSerializer.DeltaHeaderSize, delta.Length);
        }

        [Fact]
        public void Delta_AllFieldsChanged_StillRoundTrips()
        {
            var baseline = new SimulationSnapshot(1, new[] { Entity(1, 0f, 0f, hp: 100) });
            var current = new SimulationSnapshot(2, new[]
            {
                new EntitySnapshot(1, 2u, new Vector3(5f, 0f, -5f), new Vector3(5f, 0f, -5f), 3, 200, 11, 200, 42, 0, false),
            });

            var restored = _serializer.ApplyDelta(baseline, _serializer.SerializeDelta(baseline, current));

            SnapshotAssert.Equal(current, restored);
        }

        [Fact]
        public void ApplyDelta_WrongBaseline_Throws()
        {
            var baseline = new SimulationSnapshot(10, new[] { Entity(1, 0f, 0f) });
            var current = new SimulationSnapshot(11, new[] { Entity(1, 1f, 0f) });
            var delta = _serializer.SerializeDelta(baseline, current);

            var otherBaseline = new SimulationSnapshot(9, new[] { Entity(1, 0f, 0f) });

            Assert.Throws<FormatException>(() => _serializer.ApplyDelta(otherBaseline, delta));
        }

        [Fact]
        public void SerializeDelta_RequiresNewerTick()
        {
            var snapshot = new SimulationSnapshot(5, new[] { Entity(1, 0f, 0f) });

            Assert.Throws<ArgumentException>(() => _serializer.SerializeDelta(snapshot, snapshot));
        }

        [Fact]
        public void ReadBaselineTick_ReturnsBaselineTick()
        {
            var baseline = new SimulationSnapshot(1234, new[] { Entity(1, 0f, 0f) });
            var delta = _serializer.SerializeDelta(baseline, new SimulationSnapshot(1235, baseline.Entities));

            Assert.Equal(1234u, _serializer.ReadBaselineTick(delta));
        }

        [Fact]
        public void ApplyDelta_TruncatedPayload_Throws()
        {
            var baseline = new SimulationSnapshot(1, new[] { Entity(1, 0f, 0f) });
            var delta = _serializer.SerializeDelta(baseline, new SimulationSnapshot(2, new[] { Entity(1, 1f, 0f) }));

            Assert.Throws<FormatException>(() => _serializer.ApplyDelta(baseline, delta.AsSpan(0, 10)));
        }

        [Fact]
        public void DeltaChain_MatchesSequenceOfFullSnapshots()
        {
            // «дельта + полный снапшот = результат полных снапшотов»: цепочка дельт от одного
            // полного снапшота должна совпасть с исходной последовательностью состояний.
            var entities = new List<EntitySnapshot> { Entity(1, 0f, 0f) };
            var baseline = new SimulationSnapshot(50, entities.ToArray());

            for (var tick = 51u; tick <= 60u; tick++)
            {
                entities = new List<EntitySnapshot> { Entity(1, 0.2f * (tick - 50u), 0f) };
                var full = new SimulationSnapshot(tick, entities.ToArray());
                var restored = _serializer.ApplyDelta(baseline, _serializer.SerializeDelta(baseline, full));

                SnapshotAssert.Equal(full, restored);
                baseline = restored;
            }
        }

        private static EntitySnapshot Entity(uint id, float x, float z, int hp = 100)
        {
            return new EntitySnapshot(
                id,
                1u,
                new Vector3(x, 0f, z),
                new Vector3(2f, 0f, 2f),
                hp,
                100,
                50,
                50,
                0,
                0,
                false);
        }
    }
}
