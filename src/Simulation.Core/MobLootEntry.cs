using System;

namespace Simulation.Core
{
    /// <summary>
    /// Одна строка таблицы лута моба (ADR-019): предмет, диапазон количества и шанс выпадения.
    /// Один и тот же предмет может встречаться в таблице несколько раз — Simulation объединяет
    /// такие выпадения в одну стопку трупа.
    /// </summary>
    public sealed class MobLootEntry
    {
        /// <summary>Создаёт строку таблицы лута.</summary>
        /// <param name="itemId">Идентификатор предмета из каталога; положительный.</param>
        /// <param name="minQuantity">Минимальное количество; положительное.</param>
        /// <param name="maxQuantity">Максимальное количество; не меньше минимального.</param>
        /// <param name="chance">Шанс выпадения в диапазоне 0..1; 0 — никогда, 1 — всегда.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если числовое значение вне допустимого диапазона.</exception>
        public MobLootEntry(uint itemId, int minQuantity, int maxQuantity, float chance)
        {
            if (itemId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Id предмета лута должен быть положительным.");
            }

            if (minQuantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minQuantity), minQuantity, "Минимум лута должен быть положительным.");
            }

            if (maxQuantity < minQuantity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxQuantity),
                    maxQuantity,
                    "Максимум лута не может быть меньше минимума.");
            }

            if (!(chance >= 0f) || chance > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(chance), chance, "Шанс выпадения должен быть в диапазоне 0..1.");
            }

            ItemId = itemId;
            MinQuantity = minQuantity;
            MaxQuantity = maxQuantity;
            Chance = chance;
        }

        /// <summary>Идентификатор предмета.</summary>
        public uint ItemId { get; }

        /// <summary>Минимальное количество в выпадении.</summary>
        public int MinQuantity { get; }

        /// <summary>Максимальное количество в выпадении.</summary>
        public int MaxQuantity { get; }

        /// <summary>Шанс выпадения, 0..1.</summary>
        public float Chance { get; }
    }
}
