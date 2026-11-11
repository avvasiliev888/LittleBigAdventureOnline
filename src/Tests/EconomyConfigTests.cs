using System;
using System.IO;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Конфигурация экономики (ADR-017): рецепты и NPC-торговцы валидируются на старте,
    /// репозиторные файлы читаются загрузчиком и хешируются вместе с остальными.
    /// </summary>
    public sealed class EconomyConfigTests
    {
        [Fact]
        public void RepoConfig_HasTieredRecipesAndTradersPerZone()
        {
            var config = ConfigLoader.LoadFromDirectory(RepoConfigDirectory());

            Assert.Equal(24, config.Recipes.Count);
            Assert.Equal(3, config.Traders.Count);
            Assert.Equal(32, config.Items.Count);

            var zones = new System.Collections.Generic.HashSet<string>();
            for (var i = 0; i < config.Traders.Count; i++)
            {
                zones.Add(config.Traders[i].ZoneKey);
            }

            Assert.Equal(3, zones.Count);

            // У каждого торговца цена покупки строго больше цены продажи: арбитраж невозможен.
            for (var i = 0; i < config.Traders.Count; i++)
            {
                var trader = config.Traders[i];
                for (var p = 0; p < trader.Prices.Count; p++)
                {
                    Assert.True(trader.Prices[p].BuyPrice > trader.Prices[p].SellPrice);
                }
            }
        }

        [Fact]
        public void RepoConfig_EveryRecipeProducesEquipmentFromRawMaterials()
        {
            var config = ConfigLoader.LoadFromDirectory(RepoConfigDirectory());

            foreach (var pair in config.Recipes)
            {
                var recipe = pair.Value;
                Assert.Equal(ItemKind.Equipment, config.GetItem(recipe.OutputItemId).ItemKind);
                for (var i = 0; i < recipe.Materials.Count; i++)
                {
                    Assert.Equal(ItemKind.Material, config.GetItem(recipe.Materials[i].ItemId).ItemKind);
                }
            }
        }

        [Fact]
        public void Recipe_DuplicateMaterial_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => new Recipe(
                    1,
                    "dup",
                    10,
                    1,
                    new[] { new RecipeMaterial(1, 1), new RecipeMaterial(1, 2) }));
        }

        [Fact]
        public void Recipe_EmptyMaterials_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => new Recipe(1, "empty", 10, 1, Array.Empty<RecipeMaterial>()));
        }

        [Fact]
        public void Config_RecipeWithUnknownOutput_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => TestWorld.Config(recipes: new[] { TestWorld.Recipe(1, 99, 0, TestWorld.Material(1, 1)) }));

            Assert.Contains("отсутствующий предмет", exception.Message);
        }

        [Fact]
        public void Config_RecipeOutputIsMaterial_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => TestWorld.Config(recipes: new[] { TestWorld.Recipe(1, 2, 0, TestWorld.Material(1, 1)) }));

            Assert.Contains("не является снаряжением", exception.Message);
        }

        [Fact]
        public void Config_RecipeMaterialIsEquipment_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => TestWorld.Config(recipes: new[] { TestWorld.Recipe(1, 10, 0, TestWorld.Material(10, 1)) }));

            Assert.Contains("не является сырьём", exception.Message);
        }

        [Fact]
        public void Config_DuplicateRecipeId_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => TestWorld.Config(recipes: new[]
                {
                    TestWorld.Recipe(1, 10, 0, TestWorld.Material(1, 1)),
                    TestWorld.Recipe(1, 11, 0, TestWorld.Material(2, 1)),
                }));

            Assert.Contains("Дубликат Id рецепта", exception.Message);
        }

        [Fact]
        public void TraderPrice_SellNotLessThanBuy_Throws()
        {
            Assert.Throws<ArgumentException>(() => new TraderPrice(1, 10, 10));
            Assert.Throws<ArgumentException>(() => new TraderPrice(1, 10, 11));
        }

        [Fact]
        public void Config_TraderInUnknownZone_Throws()
        {
            var trader = TestWorld.Trader(1, "missing", 0f, 0f, 3f, TestWorld.Price(1, 10, 5));

            var exception = Assert.Throws<ArgumentException>(() => TestWorld.Config(traders: new[] { trader }));

            Assert.Contains("отсутствующую зону", exception.Message);
        }

        [Fact]
        public void Config_TraderOutsideItsZone_Throws()
        {
            var trader = TestWorld.Trader(1, "test", 5000f, 0f, 3f, TestWorld.Price(1, 10, 5));

            var exception = Assert.Throws<ArgumentException>(() => TestWorld.Config(traders: new[] { trader }));

            Assert.Contains("вне своей зоны", exception.Message);
        }

        [Fact]
        public void Config_DuplicateTraderId_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => TestWorld.Config(traders: new[]
                {
                    TestWorld.Trader(1, "test", 0f, 0f, 3f, TestWorld.Price(1, 10, 5)),
                    TestWorld.Trader(1, "test", 5f, 0f, 3f, TestWorld.Price(2, 10, 5)),
                }));

            Assert.Contains("Дубликат Id торговца", exception.Message);
        }

        [Fact]
        public void Config_StarterItemsExceedInventorySlots_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(
                () => TestWorld.Config(
                    starterItems: new[] { new LoadoutItem(1, 1), new LoadoutItem(2, 1) },
                    inventorySlots: 1));

            Assert.Contains("больше вместимости инвентаря", exception.Message);
        }

        [Fact]
        public void LoadFromJson_RecipesAndTraders_AreParsed()
        {
            var config = TestWorld.LoadFromJson(
                recipesJson: @"[
  { ""id"": 1, ""key"": ""chest_recipe"", ""outputItemId"": 10, ""outputQuantity"": 1, ""famePerCraft"": 4,
    ""materials"": [ { ""itemId"": 1, ""quantity"": 2 }, { ""itemId"": 2, ""quantity"": 1 } ] }
]",
                tradersJson: @"[
  { ""id"": 1, ""key"": ""merchant"", ""zoneKey"": ""test"", ""x"": 0.0, ""z"": 0.0, ""range"": 3.0,
    ""prices"": [ { ""itemId"": 1, ""buyPrice"": 10, ""sellPrice"": 6 } ] }
]",
                itemsJson: @"[
  { ""id"": 1, ""key"": ""wood"", ""kind"": ""material"", ""tier"": 1, ""maxDurability"": 0 },
  { ""id"": 2, ""key"": ""ore"", ""kind"": ""material"", ""tier"": 1, ""maxDurability"": 0 },
  { ""id"": 10, ""key"": ""chest"", ""kind"": ""equipment"", ""tier"": 1, ""maxDurability"": 100, ""slot"": ""chest"", ""maxHpBonus"": 20 }
]");

            Assert.Equal(1, config.Recipes.Count);
            Assert.Equal(2, config.GetRecipe(1).Materials.Count);
            Assert.Equal(4, config.GetRecipe(1).FamePerCraft);
            Assert.Equal(1, config.Traders.Count);
            Assert.Equal(6, config.Traders[0].FindPrice(1)!.SellPrice);
        }

        [Fact]
        public void LoadFromJson_RecipeWithoutMaterials_Throws()
        {
            var exception = Assert.Throws<ConfigValidationException>(
                () => TestWorld.LoadFromJson(
                    recipesJson: @"[
  { ""id"": 1, ""key"": ""broken"", ""outputItemId"": 10, ""outputQuantity"": 1 }
]"));

            Assert.Contains("materials", exception.Message);
        }

        [Fact]
        public void LoadFromJson_TraderWithoutPrices_Throws()
        {
            var exception = Assert.Throws<ConfigValidationException>(
                () => TestWorld.LoadFromJson(
                    tradersJson: @"[
  { ""id"": 1, ""key"": ""broken"", ""zoneKey"": ""test"", ""x"": 0.0, ""z"": 0.0, ""range"": 3.0 }
]"));

            Assert.Contains("prices", exception.Message);
        }

        private static string RepoConfigDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Prototype.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory!.FullName, "config");
        }
    }
}
