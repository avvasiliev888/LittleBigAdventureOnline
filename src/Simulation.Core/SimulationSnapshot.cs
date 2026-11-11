using System;
using System.Collections.Generic;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Неизменяемая копия состояния одной сущности на тике снапшота.
    /// </summary>
    /// <remarks>
    /// Поля зоны, Славы, серебра, снаряжения и флага враждебности добавлены в Protocol v8 (ADR-020):
    /// клиент рисует HUD и экипировку без доступа к серверному состоянию. Необязательные параметры
    /// конструктора сохраняют совместимость с тестами и мирами без прогрессии.
    /// </remarks>
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
        /// <param name="zoneId">Идентификатор зоны, в которой стоит сущность; 0 — конфиг без зон.</param>
        /// <param name="abilityId">Активная способность с учётом оружия и Славы; 0 — способности нет.</param>
        /// <param name="moveSpeed">Текущая скорость с учётом бонусов снаряжения, ед/с.</param>
        /// <param name="silver">Серебро сущности (у мобов 0).</param>
        /// <param name="hostileUntilTick">Тик окончания флага враждебности; 0 — флага нет.</param>
        /// <param name="weaponItemId">Предмет в руке; 0 — пусто.</param>
        /// <param name="chestItemId">Нагрудник; 0 — пусто.</param>
        /// <param name="legsItemId">Штаны; 0 — пусто.</param>
        /// <param name="feetItemId">Сапоги; 0 — пусто.</param>
        /// <param name="fameTotal">Суммарная Слава по всем веткам.</param>
        /// <param name="branchFame">Слава в ветке текущего оружия; 0 без оружия.</param>
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
            bool isDead,
            uint zoneId = 0,
            uint abilityId = 0,
            float moveSpeed = 0f,
            int silver = 0,
            uint hostileUntilTick = 0,
            uint weaponItemId = 0,
            uint chestItemId = 0,
            uint legsItemId = 0,
            uint feetItemId = 0,
            int fameTotal = 0,
            int branchFame = 0)
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
            ZoneId = zoneId;
            AbilityId = abilityId;
            MoveSpeed = moveSpeed;
            Silver = silver;
            HostileUntilTick = hostileUntilTick;
            WeaponItemId = weaponItemId;
            ChestItemId = chestItemId;
            LegsItemId = legsItemId;
            FeetItemId = feetItemId;
            FameTotal = fameTotal;
            BranchFame = branchFame;
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

        /// <summary>Идентификатор зоны (Protocol v8); 0, если зоны не сконфигурированы.</summary>
        public uint ZoneId { get; }

        /// <summary>Активная способность с учётом оружия и Славы (Protocol v8).</summary>
        public uint AbilityId { get; }

        /// <summary>Скорость движения с учётом бонусов снаряжения (Protocol v8).</summary>
        public float MoveSpeed { get; }

        /// <summary>Серебро сущности (Protocol v8).</summary>
        public int Silver { get; }

        /// <summary>Тик окончания флага враждебности; 0 — флага нет (Protocol v8).</summary>
        public uint HostileUntilTick { get; }

        /// <summary>Предмет в руке; 0 — пусто (Protocol v8).</summary>
        public uint WeaponItemId { get; }

        /// <summary>Нагрудник; 0 — пусто (Protocol v8).</summary>
        public uint ChestItemId { get; }

        /// <summary>Штаны; 0 — пусто (Protocol v8).</summary>
        public uint LegsItemId { get; }

        /// <summary>Сапоги; 0 — пусто (Protocol v8).</summary>
        public uint FeetItemId { get; }

        /// <summary>Суммарная Слава по всем веткам (Protocol v8).</summary>
        public int FameTotal { get; }

        /// <summary>Слава в ветке текущего оружия; 0 без оружия (Protocol v8).</summary>
        public int BranchFame { get; }
    }

    /// <summary>
    /// Снимок трупа моба (ADR-019, Protocol v8). Труп существует только на сервере и в снапшоте:
    /// клиент рисует его и отправляет <see cref="LootCommand"/> по идентификатору.
    /// </summary>
    public sealed class CorpseSnapshot
    {
        /// <summary>Создаёт снимок трупа.</summary>
        /// <param name="id">Идентификатор трупа.</param>
        /// <param name="position">Позиция трупа.</param>
        /// <param name="lootRange">Радиус сбора лута.</param>
        /// <param name="silver">Серебро в трупе.</param>
        /// <param name="loot">Стопки лута; <c>null</c> — пусто.</param>
        /// <param name="expireAtTick">Тик исчезновения трупа.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="loot"/> содержит <c>null</c>.</exception>
        public CorpseSnapshot(
            uint id,
            Vector3 position,
            float lootRange,
            int silver,
            IReadOnlyList<ItemStack>? loot,
            uint expireAtTick)
        {
            Id = id;
            Position = position;
            LootRange = lootRange;
            Silver = silver;
            ExpireAtTick = expireAtTick;

            var stacks = loot == null ? Array.Empty<ItemStack>() : new ItemStack[loot.Count];
            if (loot != null)
            {
                for (var i = 0; i < stacks.Length; i++)
                {
                    stacks[i] = loot[i] ?? throw new ArgumentNullException(nameof(loot), "Лут трупа содержит null.");
                }
            }

            Loot = stacks;
        }

        /// <summary>Идентификатор трупа.</summary>
        public uint Id { get; }

        /// <summary>Позиция трупа.</summary>
        public Vector3 Position { get; }

        /// <summary>Радиус сбора лута.</summary>
        public float LootRange { get; }

        /// <summary>Серебро в трупе.</summary>
        public int Silver { get; }

        /// <summary>Стопки лута.</summary>
        public IReadOnlyList<ItemStack> Loot { get; }

        /// <summary>Тик исчезновения трупа.</summary>
        public uint ExpireAtTick { get; }
    }

    /// <summary>
    /// Полный снапшот мира на тике: используется для сети (ADR-004, ADR-020), replay-тестов
    /// и реконсиляции (ADR-006).
    /// </summary>
    public sealed class SimulationSnapshot
    {
        /// <summary>Создаёт снапшот мира.</summary>
        /// <param name="tick">Тик, к которому относится состояние.</param>
        /// <param name="entities">Снимки сущностей в детерминированном порядке.</param>
        /// <param name="corpses">Снимки трупов; <c>null</c> — трупов нет.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="entities"/> равен <c>null</c>.</exception>
        public SimulationSnapshot(
            uint tick,
            IReadOnlyList<EntitySnapshot> entities,
            IReadOnlyList<CorpseSnapshot>? corpses = null)
        {
            Tick = tick;
            Entities = entities ?? throw new ArgumentNullException(nameof(entities));

            if (corpses == null)
            {
                Corpses = Array.Empty<CorpseSnapshot>();
            }
            else
            {
                var copy = new CorpseSnapshot[corpses.Count];
                for (var i = 0; i < copy.Length; i++)
                {
                    copy[i] = corpses[i] ?? throw new ArgumentException("Список трупов содержит null.", nameof(corpses));
                }

                Corpses = copy;
            }
        }

        /// <summary>Тик, к которому относится снапшот.</summary>
        public uint Tick { get; }

        /// <summary>Снимки сущностей в детерминированном порядке.</summary>
        public IReadOnlyList<EntitySnapshot> Entities { get; }

        /// <summary>Снимки трупов в детерминированном порядке (Protocol v8).</summary>
        public IReadOnlyList<CorpseSnapshot> Corpses { get; }
    }
}
