using System;

namespace Simulation.Core
{
    /// <summary>
    /// Запись стартового снаряжения актора (ADR-013). Слот берётся из определения предмета,
    /// поэтому в конфиге указывается только идентификатор.
    /// </summary>
    public sealed class LoadoutEquipment
    {
        /// <summary>Создаёт запись стартовой вещи.</summary>
        /// <param name="itemId">Идентификатор снаряжения; должен существовать в каталоге предметов.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если идентификатор не положителен.</exception>
        public LoadoutEquipment(uint itemId)
        {
            if (itemId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Id предмета должен быть положительным.");
            }

            ItemId = itemId;
        }

        /// <summary>Идентификатор снаряжения из каталога.</summary>
        public uint ItemId { get; }
    }
}
