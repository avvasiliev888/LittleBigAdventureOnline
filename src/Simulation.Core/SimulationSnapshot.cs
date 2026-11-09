using System;
using System.Collections.Generic;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Неизменяемая копия состояния одной сущности на тике снапшота.
    /// </summary>
    public readonly struct EntitySnapshot
    {
        /// <summary>Создаёт снимок сущности.</summary>
        /// <param name="id">Идентификатор сущности.</param>
        /// <param name="actorId">Идентификатор типа актора.</param>
        /// <param name="position">Позиция на момент снапшота.</param>
        /// <param name="spawnPosition">Точка появления (нужна для респавна).</param>
        /// <param name="hp">Текущее здоровье.</param>
        /// <param name="maxHp">Максимальное здоровье.</param>
        /// <param name="resource">Текущий ресурс.</param>
        /// <param name="maxResource">Максимальный ресурс.</param>
        /// <param name="cooldownUntilTick">Тик окончания кулдауна.</param>
        /// <param name="respawnAtTick">Тик респавна (0 — жив).</param>
        /// <param name="isDead">Признак смерти на момент снапшота.</param>
        public EntitySnapshot(
            uint id,
            uint actorId,
            Vector3 position,
            Vector3 spawnPosition,
            int hp,
            int maxHp,
            int resource,
            int maxResource,
            uint cooldownUntilTick,
            uint respawnAtTick,
            bool isDead)
        {
            Id = id;
            ActorId = actorId;
            Position = position;
            SpawnPosition = spawnPosition;
            Hp = hp;
            MaxHp = maxHp;
            Resource = resource;
            MaxResource = maxResource;
            CooldownUntilTick = cooldownUntilTick;
            RespawnAtTick = respawnAtTick;
            IsDead = isDead;
        }

        /// <summary>Идентификатор сущности.</summary>
        public uint Id { get; }

        /// <summary>Идентификатор типа актора.</summary>
        public uint ActorId { get; }

        /// <summary>Позиция сущности.</summary>
        public Vector3 Position { get; }

        /// <summary>Точка появления и респавна.</summary>
        public Vector3 SpawnPosition { get; }

        /// <summary>Текущее здоровье.</summary>
        public int Hp { get; }

        /// <summary>Максимальное здоровье.</summary>
        public int MaxHp { get; }

        /// <summary>Текущий ресурс.</summary>
        public int Resource { get; }

        /// <summary>Максимальный ресурс.</summary>
        public int MaxResource { get; }

        /// <summary>Тик окончания кулдауна способности.</summary>
        public uint CooldownUntilTick { get; }

        /// <summary>Тик респавна (0 — сущность жива).</summary>
        public uint RespawnAtTick { get; }

        /// <summary>Признак смерти.</summary>
        public bool IsDead { get; }
    }

    /// <summary>
    /// Полный снапшот мира на тике: используется для сети (ADR-004), replay-тестов и реконсиляции (ADR-006).
    /// </summary>
    public sealed class SimulationSnapshot
    {
        /// <summary>Создаёт снапшот мира.</summary>
        /// <param name="tick">Тик, к которому относится состояние.</param>
        /// <param name="entities">Снимки сущностей в детерминированном порядке.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="entities"/> равен <c>null</c>.</exception>
        public SimulationSnapshot(uint tick, IReadOnlyList<EntitySnapshot> entities)
        {
            Tick = tick;
            Entities = entities ?? throw new ArgumentNullException(nameof(entities));
        }

        /// <summary>Тик, к которому относится снапшот.</summary>
        public uint Tick { get; }

        /// <summary>Снимки сущностей в детерминированном порядке.</summary>
        public IReadOnlyList<EntitySnapshot> Entities { get; }
    }
}
