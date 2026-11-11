using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Бинарный формат тела снапшота (ADR-004, ADR-008, ADR-010, ADR-020): little-endian, ручная
    /// запись без рефлексии. С Protocol v8 запись сущности содержит зону, Славу, серебро,
    /// снаряжение и флаг враждебности, а после сущностей идёт список трупов.
    /// </summary>
    public sealed class BinarySnapshotSerializer : ISnapshotSerializer
    {
        /// <summary>Размер записи одной сущности в байтах.</summary>
        public const int EntityRecordSize = 101;

        /// <summary>Размер записи трупа без стопок лута в байтах.</summary>
        public const int CorpseRecordSize = 30;

        /// <summary>Размер заголовка дельты: базовый тик, тик, число удалённых, число записей, число трупов.</summary>
        public const int DeltaHeaderSize = 14;

        /// <summary>Маска «все поля» — для новых сущностей в дельте.</summary>
        public const uint AllFieldMask = 0x1FFFFF;

        /// <summary>Число бит маски полей (10 старых + 11 полей Protocol v8).</summary>
        public const int FieldMaskBits = 21;

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Если <paramref name="snapshot"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если сущностей или трупов больше <see cref="ushort.MaxValue"/> или координаты NaN/Infinity.</exception>
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

            if (snapshot.Corpses.Count > ushort.MaxValue)
            {
                throw new ArgumentException("Слишком много трупов для снапшота: " + snapshot.Corpses.Count + ".", nameof(snapshot));
            }

            using var stream = new MemoryStream(
                8 + (EntityRecordSize * snapshot.Entities.Count) + (CorpseRecordSize * snapshot.Corpses.Count));
            using var writer = new BinaryWriter(stream);

            writer.Write(snapshot.Tick);
            writer.Write((ushort)snapshot.Entities.Count);
            for (var i = 0; i < snapshot.Entities.Count; i++)
            {
                WriteEntity(writer, snapshot.Entities[i]);
            }

            WriteCorpses(writer, snapshot.Corpses);

            writer.Flush();
            return stream.ToArray();
        }

        /// <inheritdoc />
        /// <exception cref="FormatException">Если данные повреждены, обрезаны или содержат NaN/Infinity.</exception>
        public SimulationSnapshot Deserialize(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 8)
            {
                throw new FormatException("Снапшот короче заголовка: " + payload.Length + " байт.");
            }

            try
            {
                return DeserializeCore(payload);
            }
            catch (EndOfStreamException exception)
            {
                throw new FormatException("Снапшот обрезан: " + exception.Message, exception);
            }
        }

        private SimulationSnapshot DeserializeCore(ReadOnlySpan<byte> payload)
        {
            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var tick = reader.ReadUInt32();
            var count = reader.ReadUInt16();

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

            List<CorpseSnapshot> corpses;
            try
            {
                corpses = ReadCorpses(reader);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Повреждённый снапшот: " + exception.Message, exception);
            }

            if (stream.Position != payload.Length)
            {
                throw new FormatException(
                    "Длина снапшота " + payload.Length + " не соответствует содержимому (прочитано "
                    + stream.Position + ").");
            }

            return new SimulationSnapshot(tick, entities, corpses);
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

            if (current.Corpses.Count > ushort.MaxValue)
            {
                throw new ArgumentException("Слишком много трупов для дельты.");
            }

            var baselineById = new Dictionary<uint, EntitySnapshot>(baseline.Entities.Count);
            foreach (var entity in baseline.Entities)
            {
                baselineById[entity.Id] = entity;
            }

            var currentIds = new HashSet<uint>();
            var records = new List<(EntitySnapshot Entity, uint Mask)>();
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

            using var stream = new MemoryStream(
                DeltaHeaderSize + (removed.Count * 4) + (records.Count * 24)
                + (CorpseRecordSize * current.Corpses.Count));
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

            // Трупы живут секунды и меняются редко: в дельте передаём их полный список, а не маску.
            WriteCorpses(writer, current.Corpses);

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

            try
            {
                return ApplyDeltaCore(baseline, payload);
            }
            catch (EndOfStreamException exception)
            {
                throw new FormatException("Дельта обрезана: " + exception.Message, exception);
            }
        }

        private SimulationSnapshot ApplyDeltaCore(SimulationSnapshot baseline, ReadOnlySpan<byte> payload)
        {
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
                var mask = reader.ReadUInt32();
                var baseEntity = FindById(baseline, id);
                updated[id] = ReadFields(reader, id, mask, baseEntity);
                if (baseEntity == null)
                {
                    added.Add(id);
                }
            }

            List<CorpseSnapshot> corpses;
            try
            {
                corpses = ReadCorpses(reader);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Повреждённая дельта: " + exception.Message, exception);
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

            return new SimulationSnapshot(tick, entities, corpses);
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

        private static uint ComputeMask(EntitySnapshot baseline, EntitySnapshot current)
        {
            uint mask = 0;
            if (baseline.ActorId != current.ActorId)
            {
                mask |= 1u;
            }

            if (!SameVector(baseline.Position, current.Position))
            {
                mask |= 2u;
            }

            if (!SameVector(baseline.SpawnPosition, current.SpawnPosition))
            {
                mask |= 4u;
            }

            if (baseline.Hp != current.Hp)
            {
                mask |= 8u;
            }

            if (baseline.MaxHp != current.MaxHp)
            {
                mask |= 16u;
            }

            if (baseline.Resource != current.Resource)
            {
                mask |= 32u;
            }

            if (baseline.MaxResource != current.MaxResource)
            {
                mask |= 64u;
            }

            if (baseline.CooldownUntilTick != current.CooldownUntilTick)
            {
                mask |= 128u;
            }

            if (baseline.RespawnAtTick != current.RespawnAtTick)
            {
                mask |= 256u;
            }

            if (baseline.IsDead != current.IsDead)
            {
                mask |= 512u;
            }

            if (baseline.ZoneId != current.ZoneId)
            {
                mask |= 1024u;
            }

            if (baseline.AbilityId != current.AbilityId)
            {
                mask |= 2048u;
            }

            if (!SameFloat(baseline.MoveSpeed, current.MoveSpeed))
            {
                mask |= 4096u;
            }

            if (baseline.Silver != current.Silver)
            {
                mask |= 8192u;
            }

            if (baseline.HostileUntilTick != current.HostileUntilTick)
            {
                mask |= 16384u;
            }

            if (baseline.WeaponItemId != current.WeaponItemId)
            {
                mask |= 32768u;
            }

            if (baseline.ChestItemId != current.ChestItemId)
            {
                mask |= 65536u;
            }

            if (baseline.LegsItemId != current.LegsItemId)
            {
                mask |= 131072u;
            }

            if (baseline.FeetItemId != current.FeetItemId)
            {
                mask |= 262144u;
            }

            if (baseline.FameTotal != current.FameTotal)
            {
                mask |= 524288u;
            }

            if (baseline.BranchFame != current.BranchFame)
            {
                mask |= 1048576u;
            }

            return mask;
        }

        private static bool SameVector(Vector3 a, Vector3 b)
        {
            return SameFloat(a.X, b.X) && SameFloat(a.Y, b.Y) && SameFloat(a.Z, b.Z);
        }

        private static bool SameFloat(float a, float b)
        {
            return BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        }

        private static void WriteFields(BinaryWriter writer, EntitySnapshot entity, uint mask)
        {
            if ((mask & 1u) != 0)
            {
                writer.Write(entity.ActorId);
            }

            if ((mask & 2u) != 0)
            {
                RequireFinite(entity.Position, "position");
                writer.Write(entity.Position.X);
                writer.Write(entity.Position.Y);
                writer.Write(entity.Position.Z);
            }

            if ((mask & 4u) != 0)
            {
                RequireFinite(entity.SpawnPosition, "spawnPosition");
                writer.Write(entity.SpawnPosition.X);
                writer.Write(entity.SpawnPosition.Y);
                writer.Write(entity.SpawnPosition.Z);
            }

            if ((mask & 8u) != 0)
            {
                writer.Write(entity.Hp);
            }

            if ((mask & 16u) != 0)
            {
                writer.Write(entity.MaxHp);
            }

            if ((mask & 32u) != 0)
            {
                writer.Write(entity.Resource);
            }

            if ((mask & 64u) != 0)
            {
                writer.Write(entity.MaxResource);
            }

            if ((mask & 128u) != 0)
            {
                writer.Write(entity.CooldownUntilTick);
            }

            if ((mask & 256u) != 0)
            {
                writer.Write(entity.RespawnAtTick);
            }

            if ((mask & 512u) != 0)
            {
                writer.Write((byte)(entity.IsDead ? 1 : 0));
            }

            if ((mask & 1024u) != 0)
            {
                writer.Write(entity.ZoneId);
            }

            if ((mask & 2048u) != 0)
            {
                writer.Write(entity.AbilityId);
            }

            if ((mask & 4096u) != 0)
            {
                writer.Write(entity.MoveSpeed);
            }

            if ((mask & 8192u) != 0)
            {
                writer.Write(entity.Silver);
            }

            if ((mask & 16384u) != 0)
            {
                writer.Write(entity.HostileUntilTick);
            }

            if ((mask & 32768u) != 0)
            {
                writer.Write(entity.WeaponItemId);
            }

            if ((mask & 65536u) != 0)
            {
                writer.Write(entity.ChestItemId);
            }

            if ((mask & 131072u) != 0)
            {
                writer.Write(entity.LegsItemId);
            }

            if ((mask & 262144u) != 0)
            {
                writer.Write(entity.FeetItemId);
            }

            if ((mask & 524288u) != 0)
            {
                writer.Write(entity.FameTotal);
            }

            if ((mask & 1048576u) != 0)
            {
                writer.Write(entity.BranchFame);
            }
        }

        private static EntitySnapshot ReadFields(BinaryReader reader, uint id, uint mask, EntitySnapshot? baseline)
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
            var zoneId = baseline?.ZoneId ?? 0u;
            var abilityId = baseline?.AbilityId ?? 0u;
            var moveSpeed = baseline?.MoveSpeed ?? 0f;
            var silver = baseline?.Silver ?? 0;
            var hostileUntilTick = baseline?.HostileUntilTick ?? 0u;
            var weaponItemId = baseline?.WeaponItemId ?? 0u;
            var chestItemId = baseline?.ChestItemId ?? 0u;
            var legsItemId = baseline?.LegsItemId ?? 0u;
            var feetItemId = baseline?.FeetItemId ?? 0u;
            var fameTotal = baseline?.FameTotal ?? 0;
            var branchFame = baseline?.BranchFame ?? 0;

            if ((mask & 1u) != 0)
            {
                actorId = reader.ReadUInt32();
            }

            if ((mask & 2u) != 0)
            {
                position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                RequireFinite(position, "position");
            }

            if ((mask & 4u) != 0)
            {
                spawnPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                RequireFinite(spawnPosition, "spawnPosition");
            }

            if ((mask & 8u) != 0)
            {
                hp = reader.ReadInt32();
            }

            if ((mask & 16u) != 0)
            {
                maxHp = reader.ReadInt32();
            }

            if ((mask & 32u) != 0)
            {
                resource = reader.ReadInt32();
            }

            if ((mask & 64u) != 0)
            {
                maxResource = reader.ReadInt32();
            }

            if ((mask & 128u) != 0)
            {
                cooldownUntilTick = reader.ReadUInt32();
            }

            if ((mask & 256u) != 0)
            {
                respawnAtTick = reader.ReadUInt32();
            }

            if ((mask & 512u) != 0)
            {
                isDead = (reader.ReadByte() & 1) != 0;
            }

            if ((mask & 1024u) != 0)
            {
                zoneId = reader.ReadUInt32();
            }

            if ((mask & 2048u) != 0)
            {
                abilityId = reader.ReadUInt32();
            }

            if ((mask & 4096u) != 0)
            {
                moveSpeed = reader.ReadSingle();
            }

            if ((mask & 8192u) != 0)
            {
                silver = reader.ReadInt32();
            }

            if ((mask & 16384u) != 0)
            {
                hostileUntilTick = reader.ReadUInt32();
            }

            if ((mask & 32768u) != 0)
            {
                weaponItemId = reader.ReadUInt32();
            }

            if ((mask & 65536u) != 0)
            {
                chestItemId = reader.ReadUInt32();
            }

            if ((mask & 131072u) != 0)
            {
                legsItemId = reader.ReadUInt32();
            }

            if ((mask & 262144u) != 0)
            {
                feetItemId = reader.ReadUInt32();
            }

            if ((mask & 524288u) != 0)
            {
                fameTotal = reader.ReadInt32();
            }

            if ((mask & 1048576u) != 0)
            {
                branchFame = reader.ReadInt32();
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
                isDead,
                zoneId,
                abilityId,
                moveSpeed,
                silver,
                hostileUntilTick,
                weaponItemId,
                chestItemId,
                legsItemId,
                feetItemId,
                fameTotal,
                branchFame);
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
            writer.Write(entity.ZoneId);
            writer.Write(entity.AbilityId);
            writer.Write(entity.MoveSpeed);
            writer.Write(entity.Silver);
            writer.Write(entity.HostileUntilTick);
            writer.Write(entity.WeaponItemId);
            writer.Write(entity.ChestItemId);
            writer.Write(entity.LegsItemId);
            writer.Write(entity.FeetItemId);
            writer.Write(entity.FameTotal);
            writer.Write(entity.BranchFame);
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
            var zoneId = reader.ReadUInt32();
            var abilityId = reader.ReadUInt32();
            var moveSpeed = reader.ReadSingle();
            var silver = reader.ReadInt32();
            var hostileUntilTick = reader.ReadUInt32();
            var weaponItemId = reader.ReadUInt32();
            var chestItemId = reader.ReadUInt32();
            var legsItemId = reader.ReadUInt32();
            var feetItemId = reader.ReadUInt32();
            var fameTotal = reader.ReadInt32();
            var branchFame = reader.ReadInt32();

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
                (flags & 1) != 0,
                zoneId,
                abilityId,
                moveSpeed,
                silver,
                hostileUntilTick,
                weaponItemId,
                chestItemId,
                legsItemId,
                feetItemId,
                fameTotal,
                branchFame);
        }

        private static void WriteCorpses(BinaryWriter writer, IReadOnlyList<CorpseSnapshot> corpses)
        {
            writer.Write((ushort)corpses.Count);
            for (var i = 0; i < corpses.Count; i++)
            {
                var corpse = corpses[i];
                RequireFinite(corpse.Position, "corpse.position");
                writer.Write(corpse.Id);
                writer.Write(corpse.Position.X);
                writer.Write(corpse.Position.Y);
                writer.Write(corpse.Position.Z);
                writer.Write(corpse.LootRange);
                writer.Write(corpse.Silver);
                writer.Write(corpse.ExpireAtTick);
                writer.Write((ushort)corpse.Loot.Count);
                for (var j = 0; j < corpse.Loot.Count; j++)
                {
                    writer.Write(corpse.Loot[j].ItemId);
                    writer.Write(corpse.Loot[j].Quantity);
                }
            }
        }

        private static List<CorpseSnapshot> ReadCorpses(BinaryReader reader)
        {
            var count = reader.ReadUInt16();
            var corpses = new List<CorpseSnapshot>(count);
            for (var i = 0; i < count; i++)
            {
                var id = reader.ReadUInt32();
                var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                RequireFinite(position, "corpse.position");
                var lootRange = reader.ReadSingle();
                var silver = reader.ReadInt32();
                var expireAtTick = reader.ReadUInt32();
                var stackCount = reader.ReadUInt16();
                var loot = new List<ItemStack>(stackCount);
                for (var j = 0; j < stackCount; j++)
                {
                    loot.Add(new ItemStack(reader.ReadUInt32(), reader.ReadInt32()));
                }

                corpses.Add(new CorpseSnapshot(id, position, lootRange, silver, loot, expireAtTick));
            }

            return corpses;
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
