using System;
using System.Numerics;
using Server;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Валидатор входящих команд (M4): отклоняет читы и мусор, но не «съедает» легитимные команды.
    /// Отказ означает, что команда не применяется (M4 AI-Failure Mode: «мягкий» валидатор).
    /// </summary>
    public sealed class InputValidatorTests
    {
        [Fact]
        public void ValidMove_IsAccepted()
        {
            var (validator, state) = Create();

            var result = validator.Validate(state, 0, Envelope(1, Move(1f, 0f)), commandsFromClientThisTick: 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void MoveWithOversizedDirection_IsRejected()
        {
            var (validator, state) = Create();

            var result = validator.Validate(state, 0, Envelope(1, Move(2f, 0f)), commandsFromClientThisTick: 0);

            Assert.False(result.IsValid);
            Assert.Equal(CommandRejectReason.DirectionTooLarge, result.Reason);
        }

        [Fact]
        public void CommandOverRateLimit_IsRejected()
        {
            var (validator, state) = Create(options: new ServerOptions { MaxCommandsPerTick = 5 });

            var result = validator.Validate(state, 0, Envelope(1, Move(1f, 0f)), commandsFromClientThisTick: 5);

            Assert.False(result.IsValid);
            Assert.Equal(CommandRejectReason.RateLimit, result.Reason);
        }

        [Fact]
        public void CommandForUnknownEntity_IsRejected()
        {
            var (validator, state) = Create();

            var result = validator.Validate(state, 0, Envelope(99, Move(1f, 0f)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.UnknownEntity, result.Reason);
        }

        [Fact]
        public void CommandFromDeadEntity_IsRejected()
        {
            var (validator, state) = Create();
            state.Entities[0].Hp = 0;

            var result = validator.Validate(state, 0, Envelope(1, Move(1f, 0f)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.DeadCaster, result.Reason);
        }

        [Fact]
        public void UnknownCommandType_IsRejected()
        {
            var (validator, state) = Create();

            var result = validator.Validate(state, 0, Envelope(1, new UnknownCommand()), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.UnsupportedCommand, result.Reason);
        }

        [Fact]
        public void ValidAbility_IsAccepted()
        {
            var (validator, state) = Create();

            var result = validator.Validate(state, 0, Envelope(1, new AbilityCommand(2, 0, 0)), commandsFromClientThisTick: 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void AbilityOnCooldown_IsRejected()
        {
            var (validator, state) = Create();
            state.Entities[0].CooldownUntilTick = 10;

            var result = validator.Validate(state, 5, Envelope(1, new AbilityCommand(2, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.OnCooldown, result.Reason);
        }

        [Fact]
        public void AbilityWithoutResource_IsRejected()
        {
            var (validator, state) = Create();
            state.Entities[0].Resource = 5;

            var result = validator.Validate(state, 0, Envelope(1, new AbilityCommand(2, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.NotEnoughResource, result.Reason);
        }

        [Fact]
        public void AbilityOutOfRange_IsAcceptedAsMiss()
        {
            var (validator, state) = Create();
            state.Entities[1].Position = new Vector3(10f, 0f, 0f);

            var result = validator.Validate(state, 0, Envelope(1, new AbilityCommand(2, 0, 0)), commandsFromClientThisTick: 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void AbilityAtDeadTarget_IsAcceptedAsMiss()
        {
            var (validator, state) = Create();
            state.Entities[1].Hp = 0;

            var result = validator.Validate(state, 0, Envelope(1, new AbilityCommand(2, 0, 0)), commandsFromClientThisTick: 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void AbilityTargetingSelf_IsRejected()
        {
            var (validator, state) = Create();

            var result = validator.Validate(state, 0, Envelope(1, new AbilityCommand(1, 0, 0)), commandsFromClientThisTick: 0);

            Assert.Equal(CommandRejectReason.SelfTarget, result.Reason);
        }

        [Fact]
        public void AbilityForUnknownTarget_IsAcceptedAsMiss()
        {
            var (validator, state) = Create();

            var result = validator.Validate(state, 0, Envelope(1, new AbilityCommand(404, 0, 0)), commandsFromClientThisTick: 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void AbilityWithNoTarget_IsAcceptedAsMiss()
        {
            var (validator, state) = Create();

            var result = validator.Validate(state, 0, Envelope(1, new AbilityCommand(0, 0, 0)), commandsFromClientThisTick: 0);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_WithNullState_Throws()
        {
            var (validator, _) = Create();

            Assert.Throws<ArgumentNullException>(
                () => validator.Validate(null!, 0, Envelope(1, Move(1f, 0f)), 0));
        }

        private static (InputValidator Validator, SimulationState State) Create(ServerOptions? options = null)
        {
            var config = TestWorld.Config();
            var state = new SimulationState(0, new[]
            {
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f),
            });

            return (new InputValidator(config, options ?? new ServerOptions()), state);
        }

        private static CommandEnvelope Envelope(uint entityId, ICommand command) => new CommandEnvelope(entityId, command);

        private static MoveCommand Move(float x, float z) => new MoveCommand(new Vector2(x, z), 0, 0);

        private sealed class UnknownCommand : ICommand
        {
            public uint ClientTick => 0;

            public uint Sequence => 0;
        }
    }
}
