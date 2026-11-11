namespace Simulation.Core
{
    /// <summary>
    /// Слот снаряжения (ADR-013). Слот определяется предметом из <c>config/items.json</c>,
    /// а не тем, кто его надевает.
    /// </summary>
    public enum EquipmentSlot : byte
    {
        /// <summary>Оружие в основной руке.</summary>
        MainHand = 1,

        /// <summary>Нагрудник.</summary>
        Chest = 2,

        /// <summary>Поножи.</summary>
        Legs = 3,

        /// <summary>Обувь.</summary>
        Feet = 4,
    }
}
