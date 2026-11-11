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
        public void Decode_ZeroTarget_IsValidMiss()
        {
            var bytes = CommandCodec.Encode(new AbilityCommand(targetId: 0, clientTick: 4, sequence: 2));

            var ability = Assert.IsType<AbilityCommand>(Decode(bytes));

            Assert.Equal(0u, ability.TargetId);
            Assert.Equal(4u, ability.ClientTick);
            Assert.Equal(2u, ability.Sequence);
        }

        [Fact]
        public void EncodeDecode_EquipCommand()
        {
            var command = new EquipCommand(itemId: 14, clientTick: 8, sequence: 3);

            var decoded = Decode(CommandCodec.Encode(command));

            var equip = Assert.IsType<EquipCommand>(decoded);
            Assert.Equal(14u, equip.ItemId);
            Assert.Equal(8u, equip.ClientTick);
            Assert.Equal(3u, equip.Sequence);
        }

        [Fact]
        public void EncodeDecode_GatherCommand()
        {
            var command = new GatherCommand(nodeId: 7, clientTick: 9, sequence: 4);

            var decoded = Decode(CommandCodec.Encode(command));

            var gather = Assert.IsType<GatherCommand>(decoded);
            Assert.Equal(7u, gather.NodeId);
            Assert.Equal(9u, gather.ClientTick);
            Assert.Equal(4u, gather.Sequence);
        }

        [Fact]
        public void Decode_TruncatedEquipAndGather_ReturnsFalse()
        {
            Assert.False(CommandCodec.TryDecode(new byte[] { CommandCodec.EquipTag, 0, 0 }, out _));
            Assert.False(CommandCodec.TryDecode(new byte[] { CommandCodec.GatherTag, 0, 0 }, out _));
            Assert.False(CommandCodec.TryDecode(new byte[13], out _));
        }

        [Fact]
        public void EncodeDecode_CraftCommand()
        {
            var command = new CraftCommand(recipeId: 11, clientTick: 5, sequence: 2);

            var decoded = Decode(CommandCodec.Encode(command));

            var craft = Assert.IsType<CraftCommand>(decoded);
            Assert.Equal(11u, craft.RecipeId);
            Assert.Equal(5u, craft.ClientTick);
            Assert.Equal(2u, craft.Sequence);
        }

        [Fact]
        public void EncodeDecode_BuyCommand()
        {
            var command = new BuyCommand(traderId: 2, itemId: 20, quantity: 3, clientTick: 6, sequence: 1);

            var decoded = Decode(CommandCodec.Encode(command));

            var buy = Assert.IsType<BuyCommand>(decoded);
            Assert.Equal(2u, buy.TraderId);
            Assert.Equal(20u, buy.ItemId);
            Assert.Equal(3, buy.Quantity);
            Assert.Equal(6u, buy.ClientTick);
            Assert.Equal(1u, buy.Sequence);
        }

        [Fact]
        public void EncodeDecode_SellCommand()
        {
            var command = new SellCommand(traderId: 3, itemId: 25, quantity: 7, clientTick: 4, sequence: 9);

            var decoded = Decode(CommandCodec.Encode(command));

            var sell = Assert.IsType<SellCommand>(decoded);
            Assert.Equal(3u, sell.TraderId);
            Assert.Equal(25u, sell.ItemId);
            Assert.Equal(7, sell.Quantity);
            Assert.Equal(4u, sell.ClientTick);
            Assert.Equal(9u, sell.Sequence);
        }

        [Fact]
        public void EncodeDecode_LootCommand()
        {
            var command = new LootCommand(corpseId: 42, clientTick: 7, sequence: 3);

            var decoded = Decode(CommandCodec.Encode(command));

            var loot = Assert.IsType<LootCommand>(decoded);
            Assert.Equal(42u, loot.CorpseId);
            Assert.Equal(7u, loot.ClientTick);
            Assert.Equal(3u, loot.Sequence);
        }

        [Fact]
        public void Decode_TruncatedLoot_ReturnsFalse()
        {
            Assert.False(CommandCodec.TryDecode(new byte[] { CommandCodec.LootTag, 0, 0 }, out _));
        }

        [Fact]
        public void Decode_TruncatedCraftTrade_ReturnsFalse()
        {
            Assert.False(CommandCodec.TryDecode(new byte[] { CommandCodec.CraftTag, 0, 0 }, out _));
            Assert.False(CommandCodec.TryDecode(new byte[] { CommandCodec.BuyTag, 0, 0 }, out _));
            Assert.False(CommandCodec.TryDecode(new byte[] { CommandCodec.SellTag, 0, 0 }, out _));
        }

        [Fact]
        public void Decode_ZeroTradeQuantity_ReturnsFalse()
        {
            var bytes = CommandCodec.Encode(new BuyCommand(1, 1, 1, 0, 0));
            bytes[bytes.Length - 1] = 0;
            bytes[bytes.Length - 2] = 0;
            bytes[bytes.Length - 3] = 0;
            bytes[bytes.Length - 4] = 0;

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
