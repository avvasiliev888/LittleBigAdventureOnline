using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Параметры типа актора из конфига (M3, ADR-005). Значения приходят из JSON и в коде не дублируются.
    /// </summary>
    public sealed class ActorConfig
    {
        /// <summary>Создаёт параметры актора.</summary>
        /// <param name="id">Идентификатор типа актора (уникален в конфиге).</param>
        /// <param name="maxHp">Максимальное здоровье; положительное.</param>
        /// <param name="moveSpeed">Скорость движения, ед/с; положительная и конечная.</param>
        /// <param name="maxResource">Максимальный запас ресурса; неотрицательный.</param>
        /// <param name="respawnSeconds">Время респавна после смерти, с; положительное и конечное.</param>
        /// <param name="abilityId">Идентификатор способности актора; должен существовать в конфиге способностей.</param>
        /// <param name="resourceRegenPerSecond">
        /// Восстановление ресурса, единиц в секунду; неотрицательное и конечное. 0 — без регенерации
        /// (легаси-конструктор тестов).
        /// </param>
        /// <param name="kind">Роль актора: игрок участвует в PvP и теряет предметы, моб — нет.</param>
        /// <param name="starterItems">Стартовые предметы в инвентаре; <c>null</c> — набор пуст.</param>
        /// <param name="starterEquipment">Стартовое снаряжение; <c>null</c> — надето ничего.</param>
        /// <param name="fameOnKill">Слава, которую получает убийца игрока-жертвы этого типа (ADR-015).</param>
        /// <param name="inventorySlots">Вместимость инвентаря в стопках; положительная (ADR-017).</param>
        /// <param name="startingSilver">Стартовое серебро; неотрицательное (ADR-017).</param>
        public ActorConfig(
            uint id,
            int maxHp,
            float moveSpeed,
            int maxResource,
            float respawnSeconds,
            uint abilityId,
            float resourceRegenPerSecond = 0f,
            ActorKind kind = ActorKind.Player,
            IReadOnlyList<LoadoutItem>? starterItems = null,
            IReadOnlyList<LoadoutEquipment>? starterEquipment = null,
            int fameOnKill = 0,
            int inventorySlots = Inventory.DefaultCapacity,
            int startingSilver = 0)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id актора должен быть положительным.");
            }

            if (maxHp <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "MaxHp должен быть положительным.");
            }

            if (!(moveSpeed > 0f) || float.IsInfinity(moveSpeed))
            {
                throw new ArgumentOutOfRangeException(nameof(moveSpeed), moveSpeed, "MoveSpeed должен быть конечным положительным числом.");
            }

            if (maxResource < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResource), maxResource, "MaxResource не может быть отрицательным.");
            }

            if (!(respawnSeconds > 0f) || float.IsInfinity(respawnSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(respawnSeconds), respawnSeconds, "RespawnSeconds должен быть конечным положительным числом.");
            }

            if (abilityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(abilityId), abilityId, "AbilityId должен быть положительным.");
            }

            if (float.IsNaN(resourceRegenPerSecond) || float.IsInfinity(resourceRegenPerSecond)
                || resourceRegenPerSecond < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resourceRegenPerSecond),
                    resourceRegenPerSecond,
                    "ResourceRegenPerSecond должен быть конечным неотрицательным числом.");
            }

            if (fameOnKill < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fameOnKill),
                    fameOnKill,
                    "Слава за убийство не может быть отрицательной.");
            }

            if (inventorySlots <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inventorySlots),
                    inventorySlots,
                    "Вместимость инвентаря должна быть положительной.");
            }

            if (startingSilver < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startingSilver),
                    startingSilver,
                    "Стартовое серебро не может быть отрицательным.");
            }

            Id = id;
            MaxHp = maxHp;
            MoveSpeed = moveSpeed;
            MaxResource = maxResource;
            RespawnSeconds = respawnSeconds;
            AbilityId = abilityId;
            ResourceRegenPerSecond = resourceRegenPerSecond;
            Kind = kind;
            StarterItems = CopyLoadout(starterItems, nameof(starterItems));
            StarterEquipment = CopyLoadout(starterEquipment, nameof(starterEquipment));
            FameOnKill = fameOnKill;
            InventorySlots = inventorySlots;
            StartingSilver = startingSilver;
        }

        /// <summary>Идентификатор типа актора.</summary>
        public uint Id { get; }

        /// <summary>Максимальное здоровье.</summary>
        public int MaxHp { get; }

        /// <summary>Скорость движения в единицах мира в секунду.</summary>
        public float MoveSpeed { get; }

        /// <summary>Максимальный запас ресурса.</summary>
        public int MaxResource { get; }

        /// <summary>Время до респавна после смерти в секундах.</summary>
        public float RespawnSeconds { get; }

        /// <summary>Идентификатор способности актора.</summary>
        public uint AbilityId { get; }

        /// <summary>Восстановление ресурса в секунду; 0 — без регенерации.</summary>
        public float ResourceRegenPerSecond { get; }

        /// <summary>Роль актора в правилах мира (PvP, потеря предметов, флаг враждебности).</summary>
        public ActorKind Kind { get; }

        /// <summary>Стартовые предметы в инвентаре (страховочный набор).</summary>
        public IReadOnlyList<LoadoutItem> StarterItems { get; }

        /// <summary>Стартовое снаряжение (страховочный набор).</summary>
        public IReadOnlyList<LoadoutEquipment> StarterEquipment { get; }

        /// <summary>Слава убийце за убийство актора этого типа.</summary>
        public int FameOnKill { get; }

        /// <summary>Вместимость инвентаря в стопках.</summary>
        public int InventorySlots { get; }

        /// <summary>Стартовое серебро персонажа.</summary>
        public int StartingSilver { get; }

        private static IReadOnlyList<T> CopyLoadout<T>(IReadOnlyList<T>? source, string parameterName)
            where T : class
        {
            if (source == null)
            {
                return Array.Empty<T>();
            }

            var copy = new List<T>(source.Count);
            for (var i = 0; i < source.Count; i++)
            {
                if (source[i] == null)
                {
                    throw new ArgumentException("Стартовый набор содержит null в позиции " + i + ".", parameterName);
                }

                copy.Add(source[i]);
            }

            return copy.AsReadOnly();
        }
    }
}
