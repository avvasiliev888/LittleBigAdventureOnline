using System;

namespace Simulation.Core
{
    /// <summary>
    /// Вид предмета (ADR-005, ADR-013): сырьё идёт в рецепты крафта, снаряжение надевается в слот.
    /// </summary>
    public enum ItemKind : byte
    {
        /// <summary>Сырьё (дерево, руда, кожа, ткань): не надевается, прочности не имеет.</summary>
        Material = 1,

        /// <summary>Снаряжение: занимает слот и имеет прочность.</summary>
        Equipment = 2,
    }
}
