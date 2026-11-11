namespace Simulation.Core
{
    /// <summary>Вид узла дерева навыков (ADR-015): пассивный бонус или активная способность.</summary>
    public enum SkillNodeKind : byte
    {
        /// <summary>Пассивный бонус к характеристикам оружия, под которым открыт узел.</summary>
        Passive = 1,

        /// <summary>Активная способность, заменяющая базовую при экипировке этого архетипа.</summary>
        Active = 2,
    }
}
