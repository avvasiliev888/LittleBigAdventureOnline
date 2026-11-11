using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Инвентарь сущности (ADR-013, ADR-017): стопки предметов в порядке добавления и ограничение
    /// числа стопок (<see cref="Capacity"/>). Логика авторитетна и живёт в <see cref="Simulation"/>
    /// без Unity (ADR-001).
    /// </summary>
    public sealed class Inventory
    {
        /// <summary>Вместимость инвентаря по умолчанию, стопок.</summary>
        public const int DefaultCapacity = 24;

        private readonly List<ItemStack> _stacks = new List<ItemStack>();

        /// <summary>Создаёт инвентарь с ограничением числа стопок.</summary>
        /// <param name="capacity">Максимальное число стопок; положительное.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если вместимость не положительна.</exception>
        public Inventory(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Вместимость инвентаря должна быть положительной.");
            }

            Capacity = capacity;
        }

        /// <summary>Максимальное число стопок в инвентаре.</summary>
        public int Capacity { get; }

        /// <summary>Стопки инвентаря в порядке добавления.</summary>
        public IReadOnlyList<ItemStack> Stacks => _stacks;

        /// <summary>Свободные слоты под новые стопки.</summary>
        public int FreeSlots => Capacity - _stacks.Count;

        /// <summary>Инвентарь заполнен: новую стопку положить некуда.</summary>
        public bool IsFull => _stacks.Count >= Capacity;

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

        /// <summary>
        /// Добавляет предметы, объединяя их с существующей стопкой того же предмета.
        /// Новая стопка создаётся, только если есть свободный слот.
        /// </summary>
        /// <param name="item">Определение предмета из конфига.</param>
        /// <param name="quantity">Количество; положительное.</param>
        /// <returns><c>true</c>, если предметы поместились; <c>false</c>, если инвентарь заполнен.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="item"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если количество не положительно.</exception>
        public bool Add(ItemDefinition item, int quantity)
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
                    return true;
                }
            }

            if (IsFull)
            {
                return false;
            }

            _stacks.Add(new ItemStack(item.Id, quantity));
            return true;
        }

        /// <summary>Поместится ли предмет в инвентарь, если ничего не забирать.</summary>
        /// <param name="itemId">Идентификатор предмета.</param>
        /// <param name="quantity">Количество; положительное.</param>
        /// <returns><c>true</c>, если предмет поместится.</returns>
        public bool CanAdd(uint itemId, int quantity)
        {
            if (quantity <= 0)
            {
                return false;
            }

            return CountOf(itemId) > 0 || !IsFull;
        }

        /// <summary>
        /// Поместится ли предмет после того, как из инвентаря заберут другую стопку.
        /// Нужно смене снаряжения: вытесненная вещь возвращается в инвентарь.
        /// </summary>
        /// <param name="removeItemId">Что забираем.</param>
        /// <param name="removeQuantity">Сколько забираем; положительное.</param>
        /// <param name="addItemId">Что кладём.</param>
        /// <param name="addQuantity">Сколько кладём; положительное.</param>
        /// <returns><c>true</c>, если после изъятия предмет поместится.</returns>
        public bool CanAddAfterRemoving(uint removeItemId, int removeQuantity, uint addItemId, int addQuantity)
        {
            if (removeQuantity <= 0 || addQuantity <= 0)
            {
                return false;
            }

            if (CountOf(addItemId) > 0)
            {
                return true;
            }

            var slots = _stacks.Count;
            for (var i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i].ItemId == removeItemId && _stacks[i].Quantity == removeQuantity)
                {
                    slots--;
                    break;
                }
            }

            return slots < Capacity;
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
