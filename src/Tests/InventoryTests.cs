using System;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Вместимость инвентаря (ADR-017): инвентарь ограничен стопками, слияние старых стопок
    /// работает и при заполненном инвентаре, а вытесненная вещь может занять освободившийся слот.
    /// </summary>
    public sealed class InventoryTests
    {
        private static readonly ItemDefinition Wood = new ItemDefinition(1, "wood", ItemKind.Material, 1, 0);
        private static readonly ItemDefinition Ore = new ItemDefinition(2, "ore", ItemKind.Material, 1, 0);

        [Fact]
        public void NewInventory_HasConfiguredCapacity()
        {
            var inventory = new Inventory(3);

            Assert.Equal(3, inventory.Capacity);
            Assert.Equal(3, inventory.FreeSlots);
            Assert.True(inventory.IsEmpty);
            Assert.False(inventory.IsFull);
        }

        [Fact]
        public void Add_WhenFull_ReturnsFalseAndDoesNotMutate()
        {
            var inventory = new Inventory(1);

            Assert.True(inventory.Add(Wood, 2));
            Assert.True(inventory.IsFull);
            Assert.False(inventory.Add(Ore, 1));

            Assert.Equal(1, inventory.Stacks.Count);
            Assert.Equal(0, inventory.CountOf(Ore.Id));
        }

        [Fact]
        public void Add_ExistingStack_MergesEvenWhenFull()
        {
            var inventory = new Inventory(1);
            inventory.Add(Wood, 2);

            Assert.True(inventory.Add(Wood, 3));
            Assert.Equal(5, inventory.CountOf(Wood.Id));
            Assert.Equal(1, inventory.Stacks.Count);
        }

        [Fact]
        public void CanAdd_RespectsFreeSlotsAndExistingStacks()
        {
            var inventory = new Inventory(1);

            Assert.True(inventory.CanAdd(Wood.Id, 1));
            inventory.Add(Wood, 1);

            Assert.True(inventory.CanAdd(Wood.Id, 1));
            Assert.False(inventory.CanAdd(Ore.Id, 1));
            Assert.False(inventory.CanAdd(Ore.Id, 0));
        }

        [Fact]
        public void CanAddAfterRemoving_CountsFreedSlot()
        {
            var inventory = new Inventory(2);
            inventory.Add(Wood, 1);
            inventory.Add(Ore, 1);

            // Стопка дерева уходит целиком — слот освобождается.
            Assert.True(inventory.CanAddAfterRemoving(Wood.Id, 1, 11, 1));

            // Забираем часть стопки — слот не освобождается.
            Assert.False(inventory.CanAddAfterRemoving(Wood.Id, 0, 11, 1));
            Assert.False(inventory.CanAddAfterRemoving(Wood.Id, 1, 11, 0));

            inventory.Add(Ore, 1);
            Assert.True(inventory.CanAddAfterRemoving(Wood.Id, 1, Ore.Id, 1));
        }

        [Fact]
        public void Constructor_NonPositiveCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory(0));
        }
    }
}
