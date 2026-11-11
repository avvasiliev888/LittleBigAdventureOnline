using Server;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Валидатор экономических команд (ADR-017): отказ приходит до применения команды,
    /// поэтому состояние мира при отказе не меняется.
    /// </summary>
    public sealed class InputValidatorEconomyTests
    {
        private const uint TraderId = 1;
        private const uint RecipeId = 1;

        [Fact]
        public void ValidCraft_IsAccepted()
        {
            var (validator, state) = Create();

            var result = validator.Validate(
                state, 0, Envelope(1, new CraftCommand(RecipeId, 0, 0)), commandsFromClientThisTick: 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void CraftUnknownRecipe_IsRejected()
        {
            var (validator, state) = Create();

            var result = validator.Validate(
                state, 0, Envelope(1, new CraftCommand(99, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.UnknownRecipe, result.Reason);
        }

        [Fact]
        public void CraftWithoutMaterials_IsRejected()
        {
            var (validator, state) = Create(starterItems: new[] { new LoadoutItem(1, 1) });

            var result = validator.Validate(
                state, 0, Envelope(1, new CraftCommand(RecipeId, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.MissingMaterials, result.Reason);
        }

        [Fact]
        public void CraftWithoutInventorySpace_IsRejected()
        {
            var (validator, state) = Create(
                inventorySlots: 2,
                starterItems: new[] { new LoadoutItem(1, 5), new LoadoutItem(2, 5) });

            var result = validator.Validate(
                state, 0, Envelope(1, new CraftCommand(RecipeId, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.InventoryFull, result.Reason);
        }

        [Fact]
        public void ValidBuy_IsAccepted()
        {
            var (validator, state) = Create();

            var result = validator.Validate(
                state, 0, Envelope(1, new BuyCommand(TraderId, 1, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void BuyFromUnknownTrader_IsRejected()
        {
            var (validator, state) = Create();

            var result = validator.Validate(
                state, 0, Envelope(1, new BuyCommand(99, 1, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.UnknownTrader, result.Reason);
        }

        [Fact]
        public void BuyOutOfRange_IsRejected()
        {
            var (validator, state) = Create(playerX: 20f);

            var result = validator.Validate(
                state, 0, Envelope(1, new BuyCommand(TraderId, 1, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.TraderOutOfRange, result.Reason);
        }

        [Fact]
        public void BuyWithoutSilver_IsRejected()
        {
            var (validator, state) = Create(startingSilver: 5);

            var result = validator.Validate(
                state, 0, Envelope(1, new BuyCommand(TraderId, 10, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.NotEnoughSilver, result.Reason);
        }

        [Fact]
        public void BuyNotSoldItem_IsRejected()
        {
            var (validator, state) = Create();

            var result = validator.Validate(
                state, 0, Envelope(1, new BuyCommand(TraderId, 12, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.TraderDoesNotSell, result.Reason);
        }

        [Fact]
        public void BuyWithoutInventorySpace_IsRejected()
        {
            var (validator, state) = Create(
                inventorySlots: 1,
                starterItems: new[] { new LoadoutItem(1, 5) });

            var result = validator.Validate(
                state, 0, Envelope(1, new BuyCommand(TraderId, 11, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.InventoryFull, result.Reason);
        }

        [Fact]
        public void ValidSell_IsAccepted()
        {
            var (validator, state) = Create();

            var result = validator.Validate(
                state, 0, Envelope(1, new SellCommand(TraderId, 1, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void SellMissingItem_IsRejected()
        {
            var (validator, state) = Create();

            var result = validator.Validate(
                state, 0, Envelope(1, new SellCommand(TraderId, 10, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.ItemNotInInventory, result.Reason);
        }

        [Fact]
        public void SellNotBoughtItem_IsRejected()
        {
            var (validator, state) = Create();

            var result = validator.Validate(
                state, 0, Envelope(1, new SellCommand(TraderId, 12, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.TraderDoesNotBuy, result.Reason);
        }

        [Fact]
        public void SellOutOfRange_IsRejected()
        {
            var (validator, state) = Create(playerX: 20f);

            var result = validator.Validate(
                state, 0, Envelope(1, new SellCommand(TraderId, 1, 1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.TraderOutOfRange, result.Reason);
        }

        private static (InputValidator Validator, SimulationState State) Create(
            int inventorySlots = Inventory.DefaultCapacity,
            int startingSilver = 100,
            System.Collections.Generic.IReadOnlyList<LoadoutItem>? starterItems = null,
            float playerX = 0f)
        {
            var trader = TestWorld.Trader(
                TraderId,
                "test",
                0f,
                0f,
                3f,
                TestWorld.Price(1, 10, 6),
                TestWorld.Price(10, 100, 40),
                TestWorld.Price(11, 50, 20));
            var recipe = TestWorld.Recipe(
                RecipeId,
                10,
                5,
                TestWorld.Material(2, 1),
                TestWorld.Material(1, 1));

            var config = TestWorld.Config(
                recipes: new[] { recipe },
                traders: new[] { trader },
                starterItems: starterItems ?? new[] { new LoadoutItem(1, 5), new LoadoutItem(2, 5) },
                inventorySlots: inventorySlots,
                startingSilver: startingSilver);

            var state = new SimulationState(0, new[] { TestWorld.Entity(1, config, x: playerX) });
            return (new InputValidator(config, new ServerOptions()), state);
        }

        private static CommandEnvelope Envelope(uint entityId, ICommand command) => new CommandEnvelope(entityId, command);
    }
}
