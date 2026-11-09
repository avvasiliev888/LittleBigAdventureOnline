using System;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Состояние одной сущности мира. Мутируется исключительно симуляцией;
    /// снаружи объект читается для отображения и тестов.
    /// </summary>
    public sealed class EntityState
    {
        /// <summary>Создаёт сущность с полным запасом здоровья.</summary>
        /// <param name="id">Уникальный идентификатор сущности в рамках мира.</param>
        /// <param name="position">Начальная позиция в мировых координатах.</param>
        /// <param name="maxHp">Максимальное и текущее здоровье; должно быть положительным.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="maxHp"/> не положителен.</exception>
        public EntityState(uint id, Vector3 position, int maxHp)
        {
            if (maxHp <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "MaxHp должен быть положительным.");
            }

            Id = id;
            Position = position;
            MaxHp = maxHp;
            Hp = maxHp;
        }

        /// <summary>Уникальный идентификатор сущности.</summary>
        public uint Id { get; }

        /// <summary>Позиция в мировых координатах.</summary>
        public Vector3 Position { get; set; }

        /// <summary>Текущее здоровье; при значении ≤ 0 сущность мертва.</summary>
        public int Hp { get; set; }

        /// <summary>Максимальное здоровье сущности (из конфига акторов).</summary>
        public int MaxHp { get; }

        /// <summary>Признак смерти: <see cref="Hp"/> ≤ 0.</summary>
        public bool IsDead => Hp <= 0;
    }
}
