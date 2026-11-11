using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Инвентарь сущности (ADR-013): стопки предметов в порядке добавления.
    /// Логика авторитетна и живёт в <see cref="Simulation"/> без Unity (ADR-001).
    /// </summary>
    public sealed class Inventory
    {
        private readonly List<ItemStack> _stacks = new List<ItemStack>();

        /// <summary>Стопки инвентаря в порядке добавления.</summary>
        public IReadOnlyList<ItemStack> Stacks => _stacks;

        /// <summary>Суммарное количество предметов во всех стопках.</summary>
        public int TotalQuantity
        {
            get
            {
                var total = 0;
                for (var i = 0; i < _stacks.Count; i++)
                {
                    total += _stacks[i].Quantity;
                }

                return total;
            }
        }

        /// <summary>Инвентарь пуст.</summary>
        public bool IsEmpty => _stacks.Count == 0;

        /// <summary>Добавляет предметы, объединяя их с существующей стопкой того же предмета.</summary>
        /// <param name="item">Определение предмета из конфига.</param>
        /// <param name="quantity">Количество; положительное.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="item"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если количество не положительно.</exception>
        public void Add(ItemDefinition item, int quantity)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Количество должно быть положительным.");
            }

            for (var i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i].ItemId == item.Id)
                {
                    _stacks[i].Quantity += quantity;
                    return;
                }
            }

            _stacks.Add(new ItemStack(item.Id, quantity));
        }

        /// <summary>Забирает предметы из инвентаря.</summary>
        /// <param name="itemId">Идентификатор предмета.</param>
        /// <param name="quantity">Сколько забрать; положительное.</param>
        /// <returns><c>true</c>, если предметов хватило и они списаны.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если количество не положительно.</exception>
        public bool Remove(uint itemId, int quantity)
        {
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Количество должно быть положительным.");
            }

            for (var i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i].ItemId != itemId)
                {
                    continue;
                }

                if (_stacks[i].Quantity < quantity)
                {
                    return false;
                }

                _stacks[i].Quantity -= quantity;
                if (_stacks[i].Quantity == 0)
                {
                    _stacks.RemoveAt(i);
                }

                return true;
            }

            return false;
        }

        /// <summary>Сколько таких предметов лежит в инвентаре.</summary>
        /// <param name="itemId">Идентификатор предмета.</param>
        /// <returns>Количество; 0, если предмета нет.</returns>
        public int CountOf(uint itemId)
        {
            for (var i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i].ItemId == itemId)
                {
                    return _stacks[i].Quantity;
                }
            }

            return 0;
        }

        /// <summary>Очищает инвентарь (полный лут при смерти в красной зоне).</summary>
        public void Clear()
        {
            _stacks.Clear();
        }
    }
}
