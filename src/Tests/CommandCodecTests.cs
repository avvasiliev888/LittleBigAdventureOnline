using System;
using System.Numerics;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>Кодек команд (ADR-004): roundtrip и отказ на мусоре.</summary>
    public sealed class CommandCodecTests
    {
        [Fact]
        public void EncodeDecode_MoveCommand()
        {
            var command = new MoveCommand(new Vector2(0.5f, -1f), clientTick: 42, sequence: 7);

            var decoded = Decode(CommandCodec.Encode(command));

            var move = Assert.IsType<MoveCommand>(decoded);
            Assert.Equal(42u, move.ClientTick);
            Assert.Equal(7u, move.Sequence);
            Assert.Equal(0.5f, move.Direction.X);
            Assert.Equal(-1f, move.Direction.Y);
        }

        [Fact]
        public void EncodeDecode_AbilityCommand()
        {
            var command = new AbilityCommand(targetId: 9, clientTick: 3, sequence: 1);

            var decoded = Decode(CommandCodec.Encode(command));

            var ability = Assert.IsType<AbilityCommand>(decoded);
            Assert.Equal(9u, ability.TargetId);
            Assert.Equal(3u, ability.ClientTick);
            Assert.Equal(1u, ability.Sequence);
        }

        [Fact]
        public void Encode_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CommandCodec.Encode(null!));
        }

        [Fact]
        public void Decode_UnknownTag_ReturnsFalse()
        {
            Assert.False(CommandCodec.TryDecode(new byte[] { 99, 0, 0, 0, 0, 0, 0, 0, 0 }, out _));
        }

        [Fact]
        public void Decode_Truncated_ReturnsFalse()
        {
            Assert.False(CommandCodec.TryDecode(new byte[] { CommandCodec.MoveTag }, out _));
            Assert.False(CommandCodec.TryDecode(new byte[16], out _));
        }

        [Fact]
        public void Decode_ZeroTarget_ReturnsFalse()
        {
            var bytes = CommandCodec.Encode(new AbilityCommand(targetId: 1, clientTick: 0, sequence: 0));
            bytes[9] = 0;
            bytes[10] = 0;
            bytes[11] = 0;
            bytes[12] = 0;

            Assert.False(CommandCodec.TryDecode(bytes, out _));
        }

        private static ICommand Decode(byte[] payload)
        {
            Assert.True(CommandCodec.TryDecode(payload, out var command));
            Assert.NotNull(command);
            return command!;
        }
    }
}
