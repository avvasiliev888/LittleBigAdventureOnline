using System;

namespace Simulation.Core
{
    /// <summary>
    /// Запись стартового набора актора в инвентаре (ADR-013): «страховочный» набор из конфига.
    /// </summary>
    public sealed class LoadoutItem
    {
        /// <summary>Создаёт запись стартового предмета.</summary>
        /// <param name="itemId">Идентификатор предмета; должен существовать в каталоге предметов.</param>
        /// <param name="quantity">Количество; положительное.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если идентификатор или количество не положительны.</exception>
        public LoadoutItem(uint itemId, int quantity)
        {
            if (itemId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Id предмета должен быть положительным.");
            }

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Количество стартового предмета должно быть положительным.");
            }

            ItemId = itemId;
            Quantity = quantity;
        }

        /// <summary>Идентификатор предмета из каталога.</summary>
        public uint ItemId { get; }

        /// <summary>Количество предметов.</summary>
        public int Quantity { get; }
    }
}
