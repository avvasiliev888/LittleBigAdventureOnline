using System.Collections.Generic;
using Simulation.Core;

namespace Tests
{
    /// <summary>
    /// Мир прогрессии (ADR-014): две ветки дерева навыков, оружие двух архетипов, доспех,
    /// ботинки на скорость, ресурсные жилы и Слава за убийство. Пороги Славы кратны 20,
    /// чтобы тесты проверяли ровно границу узла.
    /// </summary>
    internal static class ProgressionFixture
    {
        public const uint UnarmedAbilityId = 1;
        public const uint WarriorAbilityId = 2;
        public const uint ArcherAbilityId = 3;
        public const uint WarriorHeavyAbilityId = 4;

        public const uint SwordItemId = 10;
        public const uint ChestItemId = 11;
        public const uint BootsItemId = 13;
        public const uint BowItemId = 14;

        public const uint WoodNodeId = 1;
        public const uint FarNodeId = 2;

        public static IReadOnlyList<AbilityConfig> Abilities()
        {
            return new[]
            {
                new AbilityConfig(UnarmedAbilityId, 25, 3f, 2f, 10),
                new AbilityConfig(WarriorAbilityId, 50, 3f, 0.5f, 0),
                new AbilityConfig(ArcherAbilityId, 20, 8f, 0.5f, 0),
                new AbilityConfig(WarriorHeavyAbilityId, 60, 3f, 0.5f, 0),
            };
        }

        public static IReadOnlyList<ItemDefinition> Items()
        {
            return new[]
            {
                new ItemDefinition(1, "wood", ItemKind.Material, 1, 0),
                new ItemDefinition(2, "ore", ItemKind.Material, 1, 0),
                new ItemDefinition(SwordItemId, "sword", ItemKind.Equipment, 1, 100, EquipmentSlot.MainHand, "warrior", 4),
                new ItemDefinition(ChestItemId, "chest", ItemKind.Equipment, 1, 100, EquipmentSlot.Chest, maxHpBonus: 20),
                new ItemDefinition(BootsItemId, "boots", ItemKind.Equipment, 1, 60, EquipmentSlot.Feet, moveSpeedBonus: 0.05f),
                new ItemDefinition(BowItemId, "bow", ItemKind.Equipment, 1, 90, EquipmentSlot.MainHand, "archer", 3),
            };
        }

        /// <summary>
        /// Ветки: воин — 0 способность, 40 урон, 60 здоровье, 80 тяжёлая способность;
        /// лучник — 0 способность, 40 урон. Слава 60 открывает два узла воина.
        /// </summary>
        public static SkillTree Tree()
        {
            return new SkillTree(new[]
            {
                new SkillArchetype("warrior", "Воин", new[]
                {
                    new SkillNode(1, "Рассечение", 0, SkillNodeKind.Active, WarriorAbilityId),
                    new SkillNode(2, "Сила удара", 40, SkillNodeKind.Passive, damageBonus: 0.10f),
                    new SkillNode(3, "Крепкий доспех", 60, SkillNodeKind.Passive, maxHpBonus: 10),
                    new SkillNode(4, "Сокрушение", 80, SkillNodeKind.Active, WarriorHeavyAbilityId),
                }),
                new SkillArchetype("archer", "Лучник", new[]
                {
                    new SkillNode(5, "Выстрел", 0, SkillNodeKind.Active, ArcherAbilityId),
                    new SkillNode(6, "Острый глаз", 40, SkillNodeKind.Passive, damageBonus: 0.05f),
                }),
            });
        }

        /// <summary>Жила рядом со спавном и жила за пределами радиуса сбора.</summary>
        public static IReadOnlyList<ResourceNodeConfig> Nodes()
        {
            return new[]
            {
                new ResourceNodeConfig(WoodNodeId, "ash_tree", 1, 2f, 0f, 2, 0.5f, 5, 2f),
                new ResourceNodeConfig(FarNodeId, "far_vein", 2, 100f, 0f, 1, 0.5f, 5, 2f),
            };
        }

        public static SimulationConfig Config(
            bool withWeapon = true,
            bool withNodes = true,
            int fameOnKill = 30,
            IReadOnlyList<ResourceNodeConfig>? nodes = null)
        {
            var starterItems = withWeapon
                ? new[] { new LoadoutItem(BowItemId, 1), new LoadoutItem(BootsItemId, 1) }
                : null;
            var starterEquipment = withWeapon
                ? new[] { new LoadoutEquipment(SwordItemId), new LoadoutEquipment(ChestItemId) }
                : null;

            var actor = new ActorConfig(
                TestWorld.PlayerActorId,
                100,
                4f,
                100,
                0.2f,
                TestWorld.PlayerAbilityId,
                0f,
                ActorKind.Player,
                starterItems,
                starterEquipment,
                fameOnKill);

            return new SimulationConfig(
                new[] { actor },
                Abilities(),
                TestWorld.DefaultZones,
                Items(),
                Tree(),
                nodes ?? (withNodes ? Nodes() : null));
        }

        public static EntityState Player(SimulationConfig config, float x = 0f)
        {
            return TestWorld.Entity(1, config, x: x);
        }

        public static EntityState Target(SimulationConfig config, float x = 1f)
        {
            return TestWorld.Entity(2, config, x: x);
        }
    }
}
