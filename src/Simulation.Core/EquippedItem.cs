using System;

namespace Simulation.Core
{
    /// <summary>
    /// Надетая вещь: предмет, слот и текущая прочность (ADR-013). Прочность копится от предмета
    /// конфига и уменьшается при смерти в зонах без полного лута.
    /// </summary>
    public sealed class EquippedItem
    {
        internal EquippedItem(ItemDefinition item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (item.ItemKind != ItemKind.Equipment || !item.Slot.HasValue)
            {
                throw new ArgumentException(
                    "Надеть можно только снаряжение со слотом: предмет '" + item.Key + "' не является снаряжением.",
                    nameof(item));
            }

            ItemId = item.Id;
            Slot = item.Slot.Value;
            MaxDurability = item.MaxDurability;
            Durability = item.MaxDurability;
            Definition = item;
        }

        /// <summary>Определение предмета из конфига (для бонусов к характеристикам).</summary>
        public ItemDefinition Definition { get; }

        /// <summary>Идентификатор предмета из конфига.</summary>
        public uint ItemId { get; }

        /// <summary>Слот, который занимает вещь.</summary>
        public EquipmentSlot Slot { get; }

        /// <summary>Текущая прочность; 0 — вещь сломана.</summary>
        public int Durability { get; internal set; }

        /// <summary>Максимальная прочность из конфига.</summary>
        public int MaxDurability { get; }

        /// <summary>Вещь сломана (прочность 0).</summary>
        public bool IsBroken => Durability <= 0;
    }
}
