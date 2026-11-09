using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Бинарный формат тела снапшота (ADR-004, ADR-008, ADR-010): little-endian, ручная запись без рефлексии.
    /// Заголовок сообщения (magic/version/type/length) добавляет транспортная обёртка в <c>Shared</c>.
    /// </summary>
    public sealed class BinarySnapshotSerializer : ISnapshotSerializer
    {
        /// <summary>Размер записи одной сущности в байтах.</summary>
        public const int EntityRecordSize = 57;

        /// <summary>Размер заголовка дельты: базовый тик, тик, число удалённых, число записей.</summary>
        public const int DeltaHeaderSize = 12;

        /// <summary>Маска «все поля» — для новых сущностей в дельте.</summary>
        public const ushort AllFieldMask = 0x3FF;

        /// <summary>Число бит маски полей (actor, position, spawn, hp, maxHp, resource, maxResource, cooldown, respawn, isDead).</summary>
        public const int FieldMaskBits = 10;

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

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Если <paramref name="baseline"/> или <paramref name="current"/> равны <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если тик текущего снапшота не больше базового.</exception>
        public byte[] SerializeDelta(SimulationSnapshot baseline, SimulationSnapshot current)
        {
            if (baseline == null)
            {
                throw new ArgumentNullException(nameof(baseline));
            }

            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (current.Tick <= baseline.Tick)
            {
                throw new ArgumentException(
                    "Дельта требует текущий тик больше базового: " + current.Tick + " <= " + baseline.Tick + ".", nameof(current));
            }

            if (current.Entities.Count > ushort.MaxValue || baseline.Entities.Count > ushort.MaxValue)
            {
                throw new ArgumentException("Слишком много сущностей для дельты.");
            }

            var baselineById = new Dictionary<uint, EntitySnapshot>(baseline.Entities.Count);
            foreach (var entity in baseline.Entities)
            {
                baselineById[entity.Id] = entity;
            }

            var currentIds = new HashSet<uint>();
            var records = new List<(EntitySnapshot Entity, ushort Mask)>();
            foreach (var entity in current.Entities)
            {
                currentIds.Add(entity.Id);
                if (baselineById.TryGetValue(entity.Id, out var previous))
                {
                    var mask = ComputeMask(previous, entity);
                    if (mask != 0)
                    {
                        records.Add((entity, mask));
                    }
                }
                else
                {
                    records.Add((entity, AllFieldMask));
                }
            }

            var removed = new List<uint>();
            foreach (var entity in baseline.Entities)
            {
                if (!currentIds.Contains(entity.Id))
                {
                    removed.Add(entity.Id);
                }
            }

            using var stream = new MemoryStream(DeltaHeaderSize + (removed.Count * 4) + (records.Count * 16));
            using var writer = new BinaryWriter(stream);

            writer.Write(baseline.Tick);
            writer.Write(current.Tick);
            writer.Write((ushort)removed.Count);
            foreach (var id in removed)
            {
                writer.Write(id);
            }

            writer.Write((ushort)records.Count);
            foreach (var record in records)
            {
                writer.Write(record.Entity.Id);
                writer.Write(record.Mask);
                WriteFields(writer, record.Entity, record.Mask);
            }

            writer.Flush();
            return stream.ToArray();
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Если <paramref name="baseline"/> равен <c>null</c>.</exception>
        /// <exception cref="FormatException">Если данные повреждены или база не совпадает.</exception>
        public SimulationSnapshot ApplyDelta(SimulationSnapshot baseline, ReadOnlySpan<byte> payload)
        {
            if (baseline == null)
            {
                throw new ArgumentNullException(nameof(baseline));
            }

            if (payload.Length < DeltaHeaderSize)
            {
                throw new FormatException("Дельта короче заголовка: " + payload.Length + " байт.");
            }

            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var baselineTick = reader.ReadUInt32();
            if (baselineTick != baseline.Tick)
            {
                throw new FormatException(
                    "База дельты не совпадает: в дельте " + baselineTick + ", у снапшота " + baseline.Tick + ".");
            }

            var tick = reader.ReadUInt32();
            if (tick <= baselineTick)
            {
                throw new FormatException("Тик дельты " + tick + " не больше базового " + baselineTick + ".");
            }

            var removedCount = reader.ReadUInt16();
            var removed = new HashSet<uint>();
            for (var i = 0; i < removedCount; i++)
            {
                removed.Add(reader.ReadUInt32());
            }

            var recordCount = reader.ReadUInt16();
            var updated = new Dictionary<uint, EntitySnapshot>(recordCount);
            var added = new List<uint>(recordCount);

            for (var i = 0; i < recordCount; i++)
            {
                var id = reader.ReadUInt32();
                var mask = reader.ReadUInt16();
                var baseEntity = FindById(baseline, id);
                updated[id] = ReadFields(reader, id, mask, baseEntity);
                if (baseEntity == null)
                {
                    added.Add(id);
                }
            }

            if (stream.Position != payload.Length)
            {
                throw new FormatException(
                    "Длина дельты " + payload.Length + " не соответствует содержимому (прочитано " + stream.Position + ").");
            }

            var entities = new List<EntitySnapshot>(baseline.Entities.Count + added.Count);
            foreach (var entity in baseline.Entities)
            {
                if (removed.Contains(entity.Id))
                {
                    continue;
                }

                entities.Add(updated.TryGetValue(entity.Id, out var changed) ? changed : entity);
            }

            foreach (var id in added)
            {
                entities.Add(updated[id]);
            }

            return new SimulationSnapshot(tick, entities);
        }

        /// <inheritdoc />
        /// <exception cref="FormatException">Если данных меньше заголовка дельты.</exception>
        public uint ReadBaselineTick(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 4)
            {
                throw new FormatException("Дельта короче тика базы: " + payload.Length + " байт.");
            }

            return (uint)(payload[0] | (payload[1] << 8) | (payload[2] << 16) | (payload[3] << 24));
        }

        private static ushort ComputeMask(EntitySnapshot baseline, EntitySnapshot current)
        {
            ushort mask = 0;
            if (baseline.ActorId != current.ActorId)
            {
                mask |= 1;
            }

            if (!SameVector(baseline.Position, current.Position))
            {
                mask |= 2;
            }

            if (!SameVector(baseline.SpawnPosition, current.SpawnPosition))
            {
                mask |= 4;
            }

            if (baseline.Hp != current.Hp)
            {
                mask |= 8;
            }

            if (baseline.MaxHp != current.MaxHp)
            {
                mask |= 16;
            }

            if (baseline.Resource != current.Resource)
            {
                mask |= 32;
            }

            if (baseline.MaxResource != current.MaxResource)
            {
                mask |= 64;
            }

            if (baseline.CooldownUntilTick != current.CooldownUntilTick)
            {
                mask |= 128;
            }

            if (baseline.RespawnAtTick != current.RespawnAtTick)
            {
                mask |= 256;
            }

            if (baseline.IsDead != current.IsDead)
            {
                mask |= 512;
            }

            return mask;
        }

        private static bool SameVector(Vector3 a, Vector3 b)
        {
            return BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X)
                && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y)
                && BitConverter.SingleToInt32Bits(a.Z) == BitConverter.SingleToInt32Bits(b.Z);
        }

        private static void WriteFields(BinaryWriter writer, EntitySnapshot entity, ushort mask)
        {
            if ((mask & 1) != 0)
            {
                writer.Write(entity.ActorId);
            }

            if ((mask & 2) != 0)
            {
                RequireFinite(entity.Position, "position");
                writer.Write(entity.Position.X);
                writer.Write(entity.Position.Y);
                writer.Write(entity.Position.Z);
            }

            if ((mask & 4) != 0)
            {
                RequireFinite(entity.SpawnPosition, "spawnPosition");
                writer.Write(entity.SpawnPosition.X);
                writer.Write(entity.SpawnPosition.Y);
                writer.Write(entity.SpawnPosition.Z);
            }

            if ((mask & 8) != 0)
            {
                writer.Write(entity.Hp);
            }

            if ((mask & 16) != 0)
            {
                writer.Write(entity.MaxHp);
            }

            if ((mask & 32) != 0)
            {
                writer.Write(entity.Resource);
            }

            if ((mask & 64) != 0)
            {
                writer.Write(entity.MaxResource);
            }

            if ((mask & 128) != 0)
            {
                writer.Write(entity.CooldownUntilTick);
            }

            if ((mask & 256) != 0)
            {
                writer.Write(entity.RespawnAtTick);
            }

            if ((mask & 512) != 0)
            {
                writer.Write((byte)(entity.IsDead ? 1 : 0));
            }
        }

        private static EntitySnapshot ReadFields(BinaryReader reader, uint id, ushort mask, EntitySnapshot? baseline)
        {
            var actorId = baseline?.ActorId ?? 0u;
            var position = baseline?.Position ?? Vector3.Zero;
            var spawnPosition = baseline?.SpawnPosition ?? Vector3.Zero;
            var hp = baseline?.Hp ?? 0;
            var maxHp = baseline?.MaxHp ?? 0;
            var resource = baseline?.Resource ?? 0;
            var maxResource = baseline?.MaxResource ?? 0;
            var cooldownUntilTick = baseline?.CooldownUntilTick ?? 0u;
            var respawnAtTick = baseline?.RespawnAtTick ?? 0u;
            var isDead = baseline?.IsDead ?? false;

            if ((mask & 1) != 0)
            {
                actorId = reader.ReadUInt32();
            }

            if ((mask & 2) != 0)
            {
                position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                RequireFinite(position, "position");
            }

            if ((mask & 4) != 0)
            {
                spawnPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                RequireFinite(spawnPosition, "spawnPosition");
            }

            if ((mask & 8) != 0)
            {
                hp = reader.ReadInt32();
            }

            if ((mask & 16) != 0)
            {
                maxHp = reader.ReadInt32();
            }

            if ((mask & 32) != 0)
            {
                resource = reader.ReadInt32();
            }

            if ((mask & 64) != 0)
            {
                maxResource = reader.ReadInt32();
            }

            if ((mask & 128) != 0)
            {
                cooldownUntilTick = reader.ReadUInt32();
            }

            if ((mask & 256) != 0)
            {
                respawnAtTick = reader.ReadUInt32();
            }

            if ((mask & 512) != 0)
            {
                isDead = (reader.ReadByte() & 1) != 0;
            }

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
                isDead);
        }

        private static EntitySnapshot? FindById(SimulationSnapshot snapshot, uint id)
        {
            for (var i = 0; i < snapshot.Entities.Count; i++)
            {
                if (snapshot.Entities[i].Id == id)
                {
                    return snapshot.Entities[i];
                }
            }

            return null;
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
