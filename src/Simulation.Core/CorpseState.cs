using System;
using System.Collections.Generic;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Труп моба на сервере (ADR-019): позиция, серебро и стопки лута, которые игрок забирает
    /// командой <see cref="LootCommand"/> в радиусе <see cref="LootRange"/>. В снапшот трупы
    /// не входят: клиентский UI сбора — этап 5.
    /// </summary>
    public sealed class CorpseState
    {
        private readonly List<ItemStack> _loot;

        /// <summary>Создаёт труп.</summary>
        /// <param name="id">Идентификатор трупа; положительный и уникальный в мире.</param>
        /// <param name="position">Позиция трупа в мире.</param>
        /// <param name="lootRange">Радиус сбора лута; положительный.</param>
        /// <param name="silver">Серебро в трупе; неотрицательное.</param>
        /// <param name="loot">Стопки лута; <c>null</c> — труп без предметов.</param>
        /// <param name="expireAtTick">Тик, на котором труп исчезает; положительный.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если идентификатор или числа вне допустимого диапазона.</exception>
        /// <exception cref="ArgumentException">Если позиция не конечна или лут содержит null.</exception>
        public CorpseState(
            uint id,
            Vector3 position,
            float lootRange,
            int silver,
            IEnumerable<ItemStack>? loot,
            uint expireAtTick)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id трупа должен быть положительным.");
            }

            if (!IsFinite(position.X) || !IsFinite(position.Y) || !IsFinite(position.Z))
            {
                throw new ArgumentException("Позиция трупа должна быть конечной.", nameof(position));
            }

            if (!IsFinite(lootRange) || lootRange <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(lootRange), lootRange, "Радиус сбора лута должен быть конечным положительным числом.");
            }

            if (silver < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(silver), silver, "Серебро в трупе не может быть отрицательным.");
            }

            if (expireAtTick == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expireAtTick), expireAtTick, "Тик исчезновения трупа должен быть положительным.");
            }

            _loot = new List<ItemStack>();
            if (loot != null)
            {
                foreach (var stack in loot)
                {
                    if (stack == null)
                    {
                        throw new ArgumentException("Лут трупа содержит null.", nameof(loot));
                    }

                    _loot.Add(stack);
                }
            }

            Id = id;
            Position = position;
            LootRange = lootRange;
            Silver = silver;
            ExpireAtTick = expireAtTick;
        }

        /// <summary>Идентификатор трупа.</summary>
        public uint Id { get; }

        /// <summary>Позиция трупа.</summary>
        public Vector3 Position { get; }

        /// <summary>Радиус сбора лута.</summary>
        public float LootRange { get; }

        /// <summary>Серебро в трупе; переносится игроку при сборе.</summary>
        public int Silver { get; internal set; }

        /// <summary>Стопки лута в трупе.</summary>
        public IReadOnlyList<ItemStack> Loot => _loot;

        /// <summary>Тик, на котором труп исчезает.</summary>
        public uint ExpireAtTick { get; }

        /// <summary>В трупе не осталось ни предметов, ни серебра.</summary>
        public bool IsEmpty => _loot.Count == 0 && Silver == 0;

        /// <summary>Изменяемый список лута: нужен только симуляции при сборе и истечении трупа.</summary>
        internal List<ItemStack> MutableLoot => _loot;

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
