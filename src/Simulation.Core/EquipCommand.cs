namespace Simulation.Core
{
    /// <summary>
    /// Команда смены снаряжения: надеть предмет из инвентаря. Вытесненная вещь возвращается в инвентарь.
    /// Смена оружия меняет архетип и активную способность, но не сбрасывает Славу (ADR-015).
    /// </summary>
    public sealed class EquipCommand : ICommand
    {
        /// <summary>Создаёт команду смены снаряжения.</summary>
        /// <param name="itemId">Идентификатор предмета из инвентаря; положительный.</param>
        /// <param name="clientTick">Клиентский тик, к которому относится команда.</param>
        /// <param name="sequence">Порядковый номер команды внутри тика.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">Если идентификатор предмета равен нулю.</exception>
        public EquipCommand(uint itemId, uint clientTick, uint sequence)
        {
            if (itemId == 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(itemId), itemId, "ItemId должен быть положительным.");
            }

            ItemId = itemId;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Идентификатор надеваемого предмета.</summary>
        public uint ItemId { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }
    }
}
