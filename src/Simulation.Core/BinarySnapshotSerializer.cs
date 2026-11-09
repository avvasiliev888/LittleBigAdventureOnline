using System;
using System.IO;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Бинарный формат тела снапшота (ADR-004, ADR-008): little-endian, ручная запись без рефлексии.
    /// Заголовок сообщения (magic/version/type/length) добавляет транспортная обёртка в <c>Shared</c>.
    /// </summary>
    public sealed class BinarySnapshotSerializer : ISnapshotSerializer
    {
        /// <summary>Размер записи одной сущности в байтах.</summary>
        public const int EntityRecordSize = 57;

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Если <paramref name="snapshot"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если сущностей больше <see cref="ushort.MaxValue"/> или координаты NaN/Infinity.</exception>
        public byte[] Serialize(SimulationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.Entities.Count > ushort.MaxValue)
            {
                throw new ArgumentException("Слишком много сущностей для снапшота: " + snapshot.Entities.Count + ".", nameof(snapshot));
            }

            using var stream = new MemoryStream(6 + (EntityRecordSize * snapshot.Entities.Count));
            using var writer = new BinaryWriter(stream);

            writer.Write(snapshot.Tick);
            writer.Write((ushort)snapshot.Entities.Count);

            for (var i = 0; i < snapshot.Entities.Count; i++)
            {
                WriteEntity(writer, snapshot.Entities[i]);
            }

            writer.Flush();
            return stream.ToArray();
        }

        /// <inheritdoc />
        /// <exception cref="FormatException">Если данные повреждены, обрезаны или содержат NaN/Infinity.</exception>
        public SimulationSnapshot Deserialize(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 6)
            {
                throw new FormatException("Снапшот короче заголовка: " + payload.Length + " байт.");
            }

            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var tick = reader.ReadUInt32();
            var count = reader.ReadUInt16();

            var expected = 6 + (EntityRecordSize * count);
            if (payload.Length != expected)
            {
                throw new FormatException(
                    "Длина снапшота " + payload.Length + " не соответствует " + count + " сущностям (ожидалось " + expected + ").");
            }

            var entities = new EntitySnapshot[count];
            for (var i = 0; i < count; i++)
            {
                try
                {
                    entities[i] = ReadEntity(reader);
                }
                catch (ArgumentException exception)
                {
                    throw new FormatException("Повреждённый снапшот: " + exception.Message, exception);
                }
            }

            return new SimulationSnapshot(tick, entities);
        }

        private static void WriteEntity(BinaryWriter writer, EntitySnapshot entity)
        {
            RequireFinite(entity.Position, "position");
            RequireFinite(entity.SpawnPosition, "spawnPosition");

            writer.Write(entity.Id);
            writer.Write(entity.ActorId);
            writer.Write(entity.Position.X);
            writer.Write(entity.Position.Y);
            writer.Write(entity.Position.Z);
            writer.Write(entity.SpawnPosition.X);
            writer.Write(entity.SpawnPosition.Y);
            writer.Write(entity.SpawnPosition.Z);
            writer.Write(entity.Hp);
            writer.Write(entity.MaxHp);
            writer.Write(entity.Resource);
            writer.Write(entity.MaxResource);
            writer.Write(entity.CooldownUntilTick);
            writer.Write(entity.RespawnAtTick);
            writer.Write((byte)(entity.IsDead ? 1 : 0));
        }

        private static EntitySnapshot ReadEntity(BinaryReader reader)
        {
            var id = reader.ReadUInt32();
            var actorId = reader.ReadUInt32();
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var spawnPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var hp = reader.ReadInt32();
            var maxHp = reader.ReadInt32();
            var resource = reader.ReadInt32();
            var maxResource = reader.ReadInt32();
            var cooldownUntilTick = reader.ReadUInt32();
            var respawnAtTick = reader.ReadUInt32();
            var flags = reader.ReadByte();

            RequireFinite(position, "position");
            RequireFinite(spawnPosition, "spawnPosition");

            return new EntitySnapshot(
                id,
                actorId,
                position,
                spawnPosition,
                hp,
                maxHp,
                resource,
                maxResource,
                cooldownUntilTick,
                respawnAtTick,
                (flags & 1) != 0);
        }

        private static void RequireFinite(Vector3 value, string field)
        {
            if (float.IsNaN(value.X) || float.IsInfinity(value.X)
                || float.IsNaN(value.Y) || float.IsInfinity(value.Y)
                || float.IsNaN(value.Z) || float.IsInfinity(value.Z))
            {
                throw new ArgumentException("Координата " + field + " содержит NaN/Infinity (ADR-004).", field);
            }
        }
    }
}
