using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Надетое снаряжение сущности (ADR-013): не больше одной вещи на слот.
    /// Смерть в зоне с полным лутом очищает его, смерть в остальных зонах только тратит прочность.
    /// </summary>
    public sealed class Equipment
    {
        private readonly EquippedItem?[] _slots = new EquippedItem?[4];

        /// <summary>Надетые вещи в порядке слотов; сломанные вещи остаются на месте.</summary>
        public IEnumerable<EquippedItem> Items
        {
            get
            {
                for (var i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i] != null)
                    {
                        yield return _slots[i]!;
                    }
                }
            }
        }

        /// <summary>Ничего не надето.</summary>
        public bool IsEmpty
        {
            get
            {
                for (var i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i] != null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Суммарный бонус к максимальному здоровью от надетых вещей.
        /// Сломанные вещи бонусов не дают: прочность 0 — вещь больше не помогает.
        /// </summary>
        public int MaxHpBonus
        {
            get
            {
                var total = 0;
                for (var i = 0; i < _slots.Length; i++)
                {
                    var item = _slots[i];
                    if (item != null && !item.IsBroken)
                    {
                        total += item.Definition.MaxHpBonus;
                    }
                }

                return total;
            }
        }

        /// <summary>Суммарный плоский бонус к урону способности от надетых вещей.</summary>
        public int DamageBonus
        {
            get
            {
                var total = 0;
                for (var i = 0; i < _slots.Length; i++)
                {
                    var item = _slots[i];
                    if (item != null && !item.IsBroken)
                    {
                        total += item.Definition.DamageBonus;
                    }
                }

                return total;
            }
        }

        /// <summary>Суммарная доля бонуса к скорости движения от надетых вещей.</summary>
        public float MoveSpeedBonus
        {
            get
            {
                var total = 0f;
                for (var i = 0; i < _slots.Length; i++)
                {
                    var item = _slots[i];
                    if (item != null && !item.IsBroken)
                    {
                        total += item.Definition.MoveSpeedBonus;
                    }
                }

                return total;
            }
        }

        /// <summary>Оружие в руке; <c>null</c>, если рука пуста.</summary>
        public EquippedItem? Weapon => _slots[ToIndex(EquipmentSlot.MainHand)];

        /// <summary>Возвращает вещь в слоте.</summary>
        /// <param name="slot">Слот снаряжения.</param>
        /// <returns>Надетая вещь или <c>null</c>, если слот пуст.</returns>
        public EquippedItem? Get(EquipmentSlot slot)
        {
            return _slots[ToIndex(slot)];
        }

        /// <summary>Надевает предмет, заменяя вещь в его слоте. Прочность — полная.</summary>
        /// <param name="item">Определение снаряжения из конфига.</param>
        /// <returns>Надетая вещь.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="item"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если предмет не является снаряжением.</exception>
        public EquippedItem Equip(ItemDefinition item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var equipped = new EquippedItem(item);
            _slots[ToIndex(equipped.Slot)] = equipped;
            return equipped;
        }

        /// <summary>
        /// Тратит прочность всех надетых вещей при смерти. Потеря считается от максимума вещи,
        /// а не от остатка, поэтому одинаковые смерти стоят одинаково.
        /// </summary>
        /// <param name="fraction">Доля максимальной прочности, 0..1.</param>
        /// <returns>Суммарно потерянная прочность.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если доля вне диапазона 0..1.</exception>
        public int ApplyDurabilityLoss(float fraction)
        {
            if (!(fraction >= 0f) || fraction > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Доля потери прочности должна быть в диапазоне 0..1.");
            }

            var totalLost = 0;
            for (var i = 0; i < _slots.Length; i++)
            {
                var item = _slots[i];
                if (item == null || item.Durability <= 0)
                {
                    continue;
                }

                var loss = (int)Math.Round(item.MaxDurability * (double)fraction, MidpointRounding.AwayFromZero);
                if (loss <= 0)
                {
                    continue;
                }

                var applied = Math.Min(loss, item.Durability);
                item.Durability -= applied;
                totalLost += applied;
            }

            return totalLost;
        }

        /// <summary>Снимает всё снаряжение (полный лут при смерти в красной зоне).</summary>
        public void Clear()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                _slots[i] = null;
            }
        }

        private static int ToIndex(EquipmentSlot slot)
        {
            var index = (int)slot - 1;
            if (index < 0 || index >= 4)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Неизвестный слот снаряжения.");
            }

            return index;
        }
    }
}
