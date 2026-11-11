using System;

namespace Simulation.Core
{
    /// <summary>
    /// Стопка одинаковых предметов в инвентаре (ADR-013). Один предмет — одна стопка с количеством.
    /// </summary>
    public sealed class ItemStack
    {
        /// <summary>Создаёт стопку предметов.</summary>
        /// <param name="itemId">Идентификатор предмета; положительный.</param>
        /// <param name="quantity">Количество; положительное.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если идентификатор или количество не положительны.</exception>
        public ItemStack(uint itemId, int quantity)
        {
            if (itemId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Id предмета должен быть положительным.");
            }

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Количество в стопке должно быть положительным.");
            }

            ItemId = itemId;
            Quantity = quantity;
        }

        /// <summary>Идентификатор предмета.</summary>
        public uint ItemId { get; }

        /// <summary>Количество предметов в стопке; положительное.</summary>
        public int Quantity { get; internal set; }
    }
}
