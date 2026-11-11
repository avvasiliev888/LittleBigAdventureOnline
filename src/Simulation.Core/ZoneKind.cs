namespace Simulation.Core
{
    /// <summary>
    /// Тип зоны по правилам PvP и смерти (product-vision §6.1, ADR-013).
    /// Сам по себе тип — только метка для UI и палитры; конкретные правила берутся из <see cref="ZoneConfig"/>.
    /// </summary>
    public enum ZoneKind : byte
    {
        /// <summary>Безопасная зона: PvP выключен, смерть стоит только прочности снаряжения.</summary>
        Blue = 1,

        /// <summary>Опасная зона: PvP включён, смерть стоит только прочности, предметы остаются.</summary>
        Yellow = 2,

        /// <summary>Зона полного лута: PvP включён, смерть забирает снаряжение и инвентарь.</summary>
        Red = 3,
    }
}
