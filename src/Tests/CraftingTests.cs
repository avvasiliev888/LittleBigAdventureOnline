using System.Collections.Generic;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Крафт из сырья (ADR-017): рецепт списывает материалы, создаёт снаряжение и начисляет
    /// Славу в ветку оружия в руке; при отказе состояние не меняется.
    /// </summary>
    public sealed class CraftingTests
    {
        private const uint RecipeId = 1;
        private const uint SwordId = 10;

        [Fact]
        public void Craft_Success_ConsumesMaterialsAndCreatesItem()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Craft(1, RecipeId));

            Assert.Equal(4, player.Inventory.CountOf(1));
            Assert.Equal(4, player.Inventory.CountOf(2));
            Assert.Equal(1, player.Inventory.CountOf(SwordId));
            Assert.Equal(5, player.Fame.Get("warrior"));
            Assert.Equal(SwordId, player.Equipment.Weapon?.Definition.Id);
        }

        [Fact]
        public void Craft_PartialStack_ConsumesOnlyRecipeAmount()
        {
            var recipe = TestWorld.Recipe(1, SwordId, 5, TestWorld.Material(2, 3));
            var config = Config(
                recipes: new[] { recipe },
                starterItems: new[] { new LoadoutItem(2, 5) },
                starterEquipment: new[] { new LoadoutEquipment(SwordId) });
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Craft(1, RecipeId));

            Assert.Equal(2, player.Inventory.CountOf(2));
            Assert.Equal(1, player.Inventory.CountOf(SwordId));
        }

        [Fact]
        public void Craft_InsufficientMaterials_DoesNothing()
        {
            var config = Config(starterItems: new[] { new LoadoutItem(1, 1) });
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Craft(1, RecipeId));

            Assert.Equal(1, player.Inventory.CountOf(1));
            Assert.Equal(0, player.Inventory.CountOf(2));
            Assert.Equal(0, player.Inventory.CountOf(SwordId));
            Assert.Equal(0, player.Fame.Total);
        }

        [Fact]
        public void Craft_UnknownRecipe_DoesNothing()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Craft(1, 99));

            Assert.Equal(5, player.Inventory.CountOf(1));
            Assert.Equal(5, player.Inventory.CountOf(2));
            Assert.Equal(0, player.Inventory.CountOf(SwordId));
        }

        [Fact]
        public void Craft_InventoryFull_KeepsMaterials()
        {
            // Две стопки занимают весь инвентарь; ни одна не расходуется целиком — место под меч не появится.
            var config = Config(
                starterItems: new[] { new LoadoutItem(1, 5), new LoadoutItem(2, 5) },
                inventorySlots: 2);
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Craft(1, RecipeId));

            Assert.Equal(5, player.Inventory.CountOf(1));
            Assert.Equal(5, player.Inventory.CountOf(2));
            Assert.Equal(0, player.Inventory.CountOf(SwordId));
            Assert.Equal(0, player.Fame.Total);
        }

        [Fact]
        public void Craft_WithoutWeapon_DoesNotAwardFame()
        {
            var config = Config(starterEquipment: System.Array.Empty<LoadoutEquipment>());
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Craft(1, RecipeId));

            Assert.Equal(1, player.Inventory.CountOf(SwordId));
            Assert.Equal(0, player.Fame.Total);
        }

        [Fact]
        public void DeadEntity_CannotCraft()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);
            player.Hp = 0;

            TestWorld.Tick(simulation, TestWorld.Craft(1, RecipeId));

            Assert.Equal(5, player.Inventory.CountOf(1));
            Assert.Equal(0, player.Inventory.CountOf(SwordId));
        }

        [Fact]
        public void Craft_WhenOutputStackExists_AddsToStack()
        {
            var config = Config(starterItems: new[]
            {
                new LoadoutItem(1, 5),
                new LoadoutItem(2, 5),
                new LoadoutItem(SwordId, 1),
            });
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Craft(1, RecipeId));

            Assert.Equal(2, player.Inventory.CountOf(SwordId));
        }

        [Fact]
        public void Craft_WhenMaterialStackIsFreed_MakesRoomForOutput()
        {
            // Инвентарь заполнен, но дерево расходуется целиком — его слот освобождается под меч.
            var config = Config(
                starterItems: new[] { new LoadoutItem(1, 1), new LoadoutItem(2, 5) },
                inventorySlots: 2);
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Craft(1, RecipeId));

            Assert.Equal(0, player.Inventory.CountOf(1));
            Assert.Equal(4, player.Inventory.CountOf(2));
            Assert.Equal(1, player.Inventory.CountOf(SwordId));
            Assert.Equal(5, player.Fame.Get("warrior"));
        }

        private static SimulationConfig Config(
            IReadOnlyList<Recipe>? recipes = null,
            IReadOnlyList<LoadoutItem>? starterItems = null,
            IReadOnlyList<LoadoutEquipment>? starterEquipment = null,
            int inventorySlots = Inventory.DefaultCapacity)
        {
            var defaultRecipe = TestWorld.Recipe(
                RecipeId,
                SwordId,
                5,
                TestWorld.Material(2, 1),
                TestWorld.Material(1, 1));

            return TestWorld.Config(
                recipes: recipes ?? new[] { defaultRecipe },
                starterItems: starterItems ?? new[]
                {
                    new LoadoutItem(1, 5),
                    new LoadoutItem(2, 5),
                },
                starterEquipment: starterEquipment ?? new[] { new LoadoutEquipment(SwordId) },
                inventorySlots: inventorySlots);
        }
    }
}
