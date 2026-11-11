using System;
using System.Collections.Generic;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Предметы, инвентарь и снаряжение (ADR-013): стопки, слоты, прочность и страховочный набор.
    /// </summary>
    public sealed class ItemRulesTests
    {
        private static readonly ItemDefinition Wood = new ItemDefinition(1, "wood", ItemKind.Material, 1, 0);
        private static readonly ItemDefinition Sword = new ItemDefinition(
            10, "sword", ItemKind.Equipment, 1, 100, EquipmentSlot.MainHand, "warrior", 4);
        private static readonly ItemDefinition Chest = new ItemDefinition(
            11, "chest", ItemKind.Equipment, 1, 80, EquipmentSlot.Chest, maxHpBonus: 20);

        [Fact]
        public void Inventory_MergesStacksAndRemovesPartially()
        {
            var inventory = new Inventory();

            inventory.Add(Wood, 5);
            inventory.Add(Wood, 3);

            Assert.Single(inventory.Stacks);
            Assert.Equal(8, inventory.CountOf(1));
            Assert.Equal(8, inventory.TotalQuantity);

            Assert.True(inventory.Remove(1, 6));
            Assert.Equal(2, inventory.CountOf(1));

            Assert.False(inventory.Remove(1, 3));
            Assert.Equal(2, inventory.CountOf(1));

            Assert.True(inventory.Remove(1, 2));
            Assert.True(inventory.IsEmpty);
            Assert.Equal(0, inventory.TotalQuantity);
        }

        [Fact]
        public void Inventory_RejectsNonPositiveQuantities()
        {
            var inventory = new Inventory();

            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Add(Wood, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Remove(1, 0));
        }

        [Fact]
        public void Equipment_ReplacesSlotAndKeepsFullDurability()
        {
            var equipment = new Equipment();

            equipment.Equip(Sword);
            var replaced = equipment.Equip(Chest);

            Assert.Equal(EquipmentSlot.Chest, replaced.Slot);
            Assert.Equal(100, equipment.Get(EquipmentSlot.MainHand)!.Durability);
            Assert.Equal(80, equipment.Get(EquipmentSlot.Chest)!.MaxDurability);
            Assert.Equal(2, CountItems(equipment));

            equipment.Clear();
            Assert.True(equipment.IsEmpty);
        }

        [Fact]
        public void DurabilityLoss_IsFractionOfMaximum_RoundedAwayFromZero()
        {
            var equipment = new Equipment();
            equipment.Equip(Sword);
            equipment.Equip(Chest);

            var lost = equipment.ApplyDurabilityLoss(0.15f);

            // 100 * 0.15 = 15, 80 * 0.15 = 12: потеря считается от максимума, а не от остатка.
            Assert.Equal(27, lost);
            Assert.Equal(85, equipment.Get(EquipmentSlot.MainHand)!.Durability);
            Assert.Equal(68, equipment.Get(EquipmentSlot.Chest)!.Durability);
        }

        [Fact]
        public void DurabilityLoss_StopsAtZero_AndBrokenItemStaysEquipped()
        {
            var equipment = new Equipment();
            equipment.Equip(Sword);

            equipment.ApplyDurabilityLoss(1f);
            var item = equipment.Get(EquipmentSlot.MainHand)!;

            Assert.Equal(0, item.Durability);
            Assert.True(item.IsBroken);
            Assert.Equal(0, equipment.ApplyDurabilityLoss(1f));
            Assert.False(equipment.IsEmpty);
        }

        [Fact]
        public void ItemDefinition_ValidatesKindAgainstSlotAndDurability()
        {
            Assert.Throws<ArgumentException>(
                () => new ItemDefinition(2, "bad", ItemKind.Equipment, 1, 100));
            Assert.Throws<ArgumentException>(
                () => new ItemDefinition(2, "bad", ItemKind.Material, 1, 0, EquipmentSlot.Chest));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ItemDefinition(2, "bad", ItemKind.Equipment, 1, 0, EquipmentSlot.Chest));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ItemDefinition(2, "bad", ItemKind.Material, 0, 0));
        }

        [Fact]
        public void GrantStarterLoadout_FillsInventoryAndEquipment()
        {
            var config = ZoneFixture.PlayerConfig(withLoadout: true);
            var entity = TestWorld.Entity(1, config);

            Assert.Equal(5, entity.Inventory.CountOf(1));
            Assert.NotNull(entity.Equipment.Get(EquipmentSlot.MainHand));
            Assert.Equal(100, entity.Equipment.Get(EquipmentSlot.MainHand)!.Durability);
        }

        [Fact]
        public void SimulationConfig_RejectsUnknownStarterItem()
        {
            var actor = new ActorConfig(1, 100, 4f, 100, 5f, 1, 0f, ActorKind.Player,
                starterItems: new[] { new LoadoutItem(999, 1) });

            var exception = Assert.Throws<ArgumentException>(
                () => new SimulationConfig(
                    new[] { actor },
                    new[] { new AbilityConfig(1, 25, 3f, 2f, 10) },
                    TestWorld.DefaultZones,
                    TestWorld.DefaultItems,
                    TestWorld.DefaultSkillTree));

            Assert.Contains("999", exception.Message);
        }

        [Fact]
        public void SimulationConfig_RejectsDuplicateEquipmentSlotInLoadout()
        {
            var secondSword = new ItemDefinition(19, "second_sword", ItemKind.Equipment, 1, 50, EquipmentSlot.MainHand, "warrior");
            var actor = new ActorConfig(1, 100, 4f, 100, 5f, 1, 0f, ActorKind.Player,
                starterEquipment: new[] { new LoadoutEquipment(10), new LoadoutEquipment(19) });
            var items = new List<ItemDefinition>(TestWorld.DefaultItems) { secondSword };

            var exception = Assert.Throws<ArgumentException>(
                () => new SimulationConfig(
                    new[] { actor },
                    new[] { new AbilityConfig(1, 25, 3f, 2f, 10) },
                    TestWorld.DefaultZones,
                    items,
                    TestWorld.DefaultSkillTree));

            Assert.Contains("занят дважды", exception.Message);
        }

        private static int CountItems(Equipment equipment)
        {
            var count = 0;
            foreach (var _ in equipment.Items)
            {
                count++;
            }

            return count;
        }
    }
}
