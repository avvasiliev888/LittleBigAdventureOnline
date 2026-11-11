using System.Collections.Generic;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Торговля с NPC (ADR-017): покупка списывает серебро и выдаёт предмет, продажа — наоборот.
    /// Все проверки (радиус, цена, серебро, место) выполняются до изменения состояния.
    /// </summary>
    public sealed class TradingTests
    {
        private const uint TraderId = 1;
        private const uint WoodId = 1;
        private const uint SwordId = 10;
        private const uint ChestId = 11;
        private const uint LegsId = 12;

        [Fact]
        public void Buy_Success_ChargesSilverAndAddsItem()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Buy(1, TraderId, WoodId, 2));

            Assert.Equal(7, player.Inventory.CountOf(WoodId));
            Assert.Equal(80, player.Silver);
        }

        [Fact]
        public void Buy_MergesWithExistingStack_WhenInventoryFull()
        {
            var config = Config(inventorySlots: 1, starterItems: new[] { new LoadoutItem(WoodId, 5) });
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Buy(1, TraderId, WoodId, 2));

            Assert.Equal(7, player.Inventory.CountOf(WoodId));
            Assert.Equal(1, player.Inventory.Stacks.Count);
            Assert.Equal(80, player.Silver);
        }

        [Fact]
        public void Buy_NotEnoughSilver_DoesNothing()
        {
            var config = Config(startingSilver: 10);
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Buy(1, TraderId, SwordId, 1));

            Assert.Equal(0, player.Inventory.CountOf(SwordId));
            Assert.Equal(10, player.Silver);
        }

        [Fact]
        public void Buy_InventoryFull_DoesNotChargeSilver()
        {
            var config = Config(inventorySlots: 1, startingSilver: 500, starterItems: new[] { new LoadoutItem(WoodId, 5) });
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Buy(1, TraderId, ChestId, 1));

            Assert.Equal(0, player.Inventory.CountOf(ChestId));
            Assert.Equal(500, player.Silver);
        }

        [Fact]
        public void Buy_OutOfRange_DoesNothing()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config, x: 20f);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Buy(1, TraderId, WoodId, 1));

            Assert.Equal(5, player.Inventory.CountOf(WoodId));
            Assert.Equal(100, player.Silver);
        }

        [Fact]
        public void Buy_UnknownTrader_DoesNothing()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Buy(1, 99, WoodId, 1));

            Assert.Equal(100, player.Silver);
        }

        [Fact]
        public void Buy_ItemNotSold_DoesNothing()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Buy(1, TraderId, LegsId, 1));

            Assert.Equal(100, player.Silver);
            Assert.Equal(0, player.Inventory.CountOf(LegsId));
        }

        [Fact]
        public void Buy_CostOverflow_DoesNothing()
        {
            var trader = TestWorld.Trader(
                TraderId,
                "test",
                0f,
                0f,
                3f,
                TestWorld.Price(WoodId, int.MaxValue, 1));
            var config = Config(traders: new[] { trader });
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Buy(1, TraderId, WoodId, 2));

            Assert.Equal(5, player.Inventory.CountOf(WoodId));
            Assert.Equal(100, player.Silver);
        }

        [Fact]
        public void Sell_Success_RemovesItemAndAddsSilver()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Sell(1, TraderId, WoodId, 2));

            Assert.Equal(3, player.Inventory.CountOf(WoodId));
            Assert.Equal(112, player.Silver);
        }

        [Fact]
        public void Sell_ItemNotInInventory_DoesNothing()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Sell(1, TraderId, SwordId, 1));

            Assert.Equal(100, player.Silver);
            Assert.Equal(0, player.Inventory.CountOf(SwordId));
        }

        [Fact]
        public void Sell_ItemNotBoughtByTrader_DoesNothing()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Sell(1, TraderId, LegsId, 1));

            Assert.Equal(100, player.Silver);
        }

        [Fact]
        public void Sell_RevenueOverflow_DoesNothing()
        {
            var trader = TestWorld.Trader(
                TraderId,
                "test",
                0f,
                0f,
                3f,
                TestWorld.Price(WoodId, int.MaxValue, 2_000_000_000));
            var config = Config(traders: new[] { trader }, startingSilver: int.MaxValue);
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Sell(1, TraderId, WoodId, 2));

            Assert.Equal(5, player.Inventory.CountOf(WoodId));
            Assert.Equal(int.MaxValue, player.Silver);
        }

        [Fact]
        public void Sell_OutOfRange_DoesNothing()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config, x: 20f);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Sell(1, TraderId, WoodId, 1));

            Assert.Equal(5, player.Inventory.CountOf(WoodId));
            Assert.Equal(100, player.Silver);
        }

        [Fact]
        public void DeadEntity_CannotTrade()
        {
            var config = Config();
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);
            player.Hp = 0;

            TestWorld.Tick(
                simulation,
                TestWorld.Buy(1, TraderId, WoodId, 1),
                TestWorld.Sell(1, TraderId, WoodId, 1));

            Assert.Equal(5, player.Inventory.CountOf(WoodId));
            Assert.Equal(100, player.Silver);
        }

        private static SimulationConfig Config(
            IReadOnlyList<NpcTrader>? traders = null,
            IReadOnlyList<LoadoutItem>? starterItems = null,
            int inventorySlots = Inventory.DefaultCapacity,
            int startingSilver = 100)
        {
            var defaultTrader = TestWorld.Trader(
                TraderId,
                "test",
                0f,
                0f,
                3f,
                TestWorld.Price(WoodId, 10, 6),
                TestWorld.Price(SwordId, 100, 40),
                TestWorld.Price(ChestId, 50, 20));

            return TestWorld.Config(
                traders: traders ?? new[] { defaultTrader },
                starterItems: starterItems ?? new[] { new LoadoutItem(WoodId, 5) },
                inventorySlots: inventorySlots,
                startingSilver: startingSilver);
        }
    }
}
