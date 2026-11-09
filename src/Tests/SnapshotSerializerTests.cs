using System;
using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>Бинарный формат снапшота (ADR-004/ADR-008): roundtrip, отказ на мусоре и NaN.</summary>
    public sealed class SnapshotSerializerTests
    {
        private readonly BinarySnapshotSerializer _serializer = new BinarySnapshotSerializer();

        [Fact]
        public void RoundTrip_PreservesEveryField()
        {
            var config = TestWorld.Config(maxHp: 120);
            var alive = TestWorld.Entity(7, config, x: 1.5f, z: -2.5f);
            alive.Hp = 13;
            alive.Resource = 40;
            alive.CooldownUntilTick = 77;

            var dead = TestWorld.Entity(9, config, x: -3f, z: 4f);
            dead.Hp = 0;
            dead.RespawnAtTick = 150;

            var snapshot = new SimulationSnapshot(42, new[]
            {
                ToSnapshot(alive),
                ToSnapshot(dead),
            });

            var bytes = _serializer.Serialize(snapshot);
            var restored = _serializer.Deserialize(bytes);

            SnapshotAssert.Equal(snapshot, restored);
        }

        [Fact]
        public void RoundTrip_EmptyWorld()
        {
            var snapshot = new SimulationSnapshot(0, Array.Empty<EntitySnapshot>());

            var bytes = _serializer.Serialize(snapshot);
            var restored = _serializer.Deserialize(bytes);

            Assert.Equal(0u, restored.Tick);
            Assert.Empty(restored.Entities);
        }

        [Fact]
        public void RoundTrip_MaxTick()
        {
            var snapshot = new SimulationSnapshot(uint.MaxValue, Array.Empty<EntitySnapshot>());

            var restored = _serializer.Deserialize(_serializer.Serialize(snapshot));

            Assert.Equal(uint.MaxValue, restored.Tick);
        }

        [Fact]
        public void Serialize_WithNanPosition_Throws()
        {
            var snapshot = new SimulationSnapshot(
                1,
                new[] { new EntitySnapshot(1, 1, new Vector3(float.NaN, 0f, 0f), Vector3.Zero, 100, 100, 0, 0, 0, 0, false) });

            Assert.Throws<ArgumentException>(() => _serializer.Serialize(snapshot));
        }

        [Fact]
        public void Serialize_WithNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _serializer.Serialize(null!));
        }

        [Fact]
        public void Deserialize_TruncatedHeader_Throws()
        {
            Assert.Throws<FormatException>(() => _serializer.Deserialize(new byte[3]));
        }

        [Fact]
        public void Deserialize_WrongLength_Throws()
        {
            var bytes = _serializer.Serialize(
                new SimulationSnapshot(1, new[] { ToSnapshot(TestWorld.Entity(1, TestWorld.Config())) }));

            Assert.Throws<FormatException>(() => _serializer.Deserialize(bytes.AsSpan(0, bytes.Length - 1)));
            Assert.Throws<FormatException>(() => _serializer.Deserialize(new byte[bytes.Length + 1]));
        }

        [Fact]
        public void Deserialize_NaNInPayload_ThrowsFormatException_OnReadPath()
        {
            // Пишем вручную битый пакет: одна сущность, координата NaN.
            var snapshot = new SimulationSnapshot(1, new[] { ToSnapshot(TestWorld.Entity(1, TestWorld.Config())) });
            var bytes = _serializer.Serialize(snapshot);

            // Позиция X начинается после tick(4)+count(2)+id(4)+actorId(4) = 14.
            BitConverter.GetBytes(float.NaN).CopyTo(bytes, 14);

            Assert.Throws<FormatException>(() => _serializer.Deserialize(bytes));
        }

        [Fact]
        public void EntityRecordSize_MatchesDocumentedFormat()
        {
            Assert.Equal(57, BinarySnapshotSerializer.EntityRecordSize);

            var bytes = _serializer.Serialize(
                new SimulationSnapshot(0, new[] { ToSnapshot(TestWorld.Entity(1, TestWorld.Config())) }));

            Assert.Equal(6 + BinarySnapshotSerializer.EntityRecordSize, bytes.Length);
        }

        private static EntitySnapshot ToSnapshot(EntityState entity)
        {
            return new EntitySnapshot(
                entity.Id,
                entity.ActorId,
                entity.Position,
                entity.SpawnPosition,
                entity.Hp,
                entity.MaxHp,
                entity.Resource,
                entity.MaxResource,
                entity.CooldownUntilTick,
                entity.RespawnAtTick,
                entity.IsDead);
        }
    }
}
