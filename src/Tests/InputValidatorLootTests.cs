using System.Numerics;
using Server;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>Валидатор команды сбора лута (ADR-018, ADR-019): труп существует и в радиусе.</summary>
    public sealed class InputValidatorLootTests
    {
        [Fact]
        public void Loot_InRange_IsAccepted()
        {
            var (config, state, player) = World(playerX: 1f, corpseX: 0f);
            var validator = new InputValidator(config, new ServerOptions());

            var result = validator.Validate(state, 0, TestWorld.Loot(player.Id, 100u), 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Loot_UnknownCorpse_IsRejected()
        {
            var (config, state, player) = World(playerX: 1f, corpseX: 0f);
            var validator = new InputValidator(config, new ServerOptions());

            var result = validator.Validate(state, 0, TestWorld.Loot(player.Id, 999u), 0);

            Assert.False(result.IsValid);
            Assert.Equal(CommandRejectReason.UnknownCorpse, result.Reason);
        }

        [Fact]
        public void Loot_OutOfRange_IsRejected()
        {
            var (config, state, player) = World(playerX: 10f, corpseX: 0f);
            var validator = new InputValidator(config, new ServerOptions());

            var result = validator.Validate(state, 0, TestWorld.Loot(player.Id, 100u), 0);

            Assert.False(result.IsValid);
            Assert.Equal(CommandRejectReason.CorpseOutOfRange, result.Reason);
        }

        [Fact]
        public void Loot_DeadCaster_IsRejected()
        {
            var (config, state, player) = World(playerX: 1f, corpseX: 0f);
            player.Hp = 0;
            var validator = new InputValidator(config, new ServerOptions());

            var result = validator.Validate(state, 0, TestWorld.Loot(player.Id, 100u), 0);

            Assert.False(result.IsValid);
            Assert.Equal(CommandRejectReason.DeadCaster, result.Reason);
        }

        private static (SimulationConfig Config, SimulationState State, EntityState Player) World(
            float playerX,
            float corpseX)
        {
            var config = TestWorld.Config();
            var player = TestWorld.Entity(10, config, playerX, 0f);
            var state = new SimulationState(
                0,
                new[] { player },
                new[] { new CorpseState(100, new Vector3(corpseX, 0f, 0f), 2f, 5, null, 100) });
            return (config, state, player);
        }
    }
}
