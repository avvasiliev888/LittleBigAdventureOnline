using System.Collections.Generic;
using Simulation.Core;

namespace Tests
{
    /// <summary>
    /// Общий мир для тестов зон (ADR-013): три полосы — синяя, жёлтая и красная.
    /// Позиции <see cref="BlueX"/>, <see cref="YellowX"/>, <see cref="RedX"/> лежат строго внутри своих зон.
    /// </summary>
    internal static class ZoneFixture
    {
        public const float BlueX = -10f;
        public const float YellowX = 5f;
        public const float RedX = 15f;

        public const uint PlayerAbilityId = 1;

        /// <summary>Синяя [-30;0], жёлтая [0;10], красная [10;20] по X; мир [-30;30] по Z.</summary>
        public static IReadOnlyList<ZoneConfig> ThreeZones()
        {
            return new[]
            {
                new ZoneConfig(1, "blue", ZoneKind.Blue, -30f, -30f, 0f, 30f, false, false, 0.10f, 0f, 1, 2),
                new ZoneConfig(2, "yellow", ZoneKind.Yellow, 0f, -30f, 10f, 30f, true, false, 0.15f, 60f, 2, 3),
                new ZoneConfig(3, "red", ZoneKind.Red, 10f, -30f, 20f, 30f, true, true, 0f, 90f, 3, 4),
            };
        }

        public static IReadOnlyList<LoadoutItem> StarterItems()
        {
            return new[] { new LoadoutItem(1, 5) };
        }

        public static IReadOnlyList<LoadoutEquipment> StarterEquipment()
        {
            return new[] { new LoadoutEquipment(10) };
        }

        /// <summary>Два игрока в одном конфиге: способности, зоны и страховочный набор.</summary>
        public static SimulationConfig PlayerConfig(
            int damage = 25,
            float cooldownSeconds = 2f,
            bool withLoadout = false)
        {
            return TestWorld.Config(
                damage: damage,
                cooldownSeconds: cooldownSeconds,
                zones: ThreeZones(),
                starterItems: withLoadout ? StarterItems() : null,
                starterEquipment: withLoadout ? StarterEquipment() : null);
        }

        /// <summary>Игрок и моб: моб не подчиняется правилам PvP и лута игроков.</summary>
        public static SimulationConfig PlayerAndMobConfig(int damage = 25)
        {
            var ability = new AbilityConfig(PlayerAbilityId, damage, 3f, 2f, 10);
            var player = new ActorConfig(TestWorld.PlayerActorId, 100, 4f, 100, 5f, PlayerAbilityId);
            var mob = new ActorConfig(100, 100, 1f, 100, 5f, PlayerAbilityId, 0f, ActorKind.Mob);

            return new SimulationConfig(
                new[] { player, mob },
                new[] { ability },
                ThreeZones(),
                TestWorld.DefaultItems,
                TestWorld.DefaultSkillTree);
        }
    }
}
