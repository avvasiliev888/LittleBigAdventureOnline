using System;
using System.IO;
using Simulation.Core;

namespace Shared
{
    /// <summary>
    /// Бинарный кодек команд (ADR-004). Тег типа + поля команды; ничего лишнего:
    /// параметры способности живут в конфиге, а не в пакете.
    /// </summary>
    public static class CommandCodec
    {
        /// <summary>Тег команды движения.</summary>
        public const byte MoveTag = 1;

        /// <summary>Тег команды способности.</summary>
        public const byte AbilityTag = 2;

        /// <summary>Тег команды смены снаряжения.</summary>
        public const byte EquipTag = 3;

        /// <summary>Тег команды сбора ресурса.</summary>
        public const byte GatherTag = 4;

        /// <summary>Тег команды крафта.</summary>
        public const byte CraftTag = 5;

        /// <summary>Тег команды покупки у NPC.</summary>
        public const byte BuyTag = 6;

        /// <summary>Тег команды продажи NPC.</summary>
        public const byte SellTag = 7;

        /// <summary>Кодирует команду в тело сообщения.</summary>
        /// <param name="command">Команда для кодирования.</param>
        /// <returns>Байты тела.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="command"/> равен <c>null</c>.</exception>
        /// <exception cref="NotSupportedException">Если тип команды неизвестен.</exception>
        public static byte[] Encode(ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            using var stream = new MemoryStream(13);
            using var writer = new BinaryWriter(stream);

            switch (command)
            {
                case MoveCommand move:
                    writer.Write(MoveTag);
                    writer.Write(move.ClientTick);
                    writer.Write(move.Sequence);
                    writer.Write(move.Direction.X);
                    writer.Write(move.Direction.Y);
                    break;

                case AbilityCommand ability:
                    writer.Write(AbilityTag);
                    writer.Write(ability.ClientTick);
                    writer.Write(ability.Sequence);
                    writer.Write(ability.TargetId);
                    break;

                case EquipCommand equip:
                    writer.Write(EquipTag);
                    writer.Write(equip.ClientTick);
                    writer.Write(equip.Sequence);
                    writer.Write(equip.ItemId);
                    break;

                case GatherCommand gather:
                    writer.Write(GatherTag);
                    writer.Write(gather.ClientTick);
                    writer.Write(gather.Sequence);
                    writer.Write(gather.NodeId);
                    break;

                case CraftCommand craft:
                    writer.Write(CraftTag);
                    writer.Write(craft.ClientTick);
                    writer.Write(craft.Sequence);
                    writer.Write(craft.RecipeId);
                    break;

                case BuyCommand buy:
                    writer.Write(BuyTag);
                    writer.Write(buy.ClientTick);
                    writer.Write(buy.Sequence);
                    writer.Write(buy.TraderId);
                    writer.Write(buy.ItemId);
                    writer.Write((uint)buy.Quantity);
                    break;

                case SellCommand sell:
                    writer.Write(SellTag);
                    writer.Write(sell.ClientTick);
                    writer.Write(sell.Sequence);
                    writer.Write(sell.TraderId);
                    writer.Write(sell.ItemId);
                    writer.Write((uint)sell.Quantity);
                    break;

                default:
                    throw new NotSupportedException("Неизвестный тип команды: " + command.GetType().FullName + ".");
            }

            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>Декодирует команду из тела сообщения.</summary>
        /// <param name="payload">Байты тела.</param>
        /// <param name="command">Декодированная команда.</param>
        /// <returns><c>true</c>, если тело корректно.</returns>
        public static bool TryDecode(ReadOnlySpan<byte> payload, out ICommand? command)
        {
            command = null;

            if (payload.Length < 9)
            {
                return false;
            }

            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var tag = reader.ReadByte();
            var clientTick = reader.ReadUInt32();
            var sequence = reader.ReadUInt32();

            switch (tag)
            {
                case MoveTag:
                    if (payload.Length != 17)
                    {
                        return false;
                    }

                    command = new MoveCommand(
                        new System.Numerics.Vector2(reader.ReadSingle(), reader.ReadSingle()),
                        clientTick,
                        sequence);
                    return true;

                case AbilityTag:
                    if (payload.Length != 13)
                    {
                        return false;
                    }

                    var targetId = reader.ReadUInt32();
                    command = new AbilityCommand(targetId, clientTick, sequence);
                    return true;

                case EquipTag:
                    if (payload.Length != 13)
                    {
                        return false;
                    }

                    var itemId = reader.ReadUInt32();
                    command = new EquipCommand(itemId, clientTick, sequence);
                    return true;

                case GatherTag:
                    if (payload.Length != 13)
                    {
                        return false;
                    }

                    var nodeId = reader.ReadUInt32();
                    command = new GatherCommand(nodeId, clientTick, sequence);
                    return true;

                case CraftTag:
                    if (payload.Length != 13)
                    {
                        return false;
                    }

                    var recipeId = reader.ReadUInt32();
                    command = new CraftCommand(recipeId, clientTick, sequence);
                    return true;

                case BuyTag:
                    return TryDecodeTrade(reader, payload, clientTick, sequence, buy: true, out command);

                case SellTag:
                    return TryDecodeTrade(reader, payload, clientTick, sequence, buy: false, out command);

                default:
                    return false;
            }
        }

        private static bool TryDecodeTrade(
            BinaryReader reader,
            ReadOnlySpan<byte> payload,
            uint clientTick,
            uint sequence,
            bool buy,
            out ICommand? command)
        {
            command = null;
            if (payload.Length != 21)
            {
                return false;
            }

            var traderId = reader.ReadUInt32();
            var itemId = reader.ReadUInt32();
            var rawQuantity = reader.ReadUInt32();
            if (rawQuantity == 0 || rawQuantity > int.MaxValue)
            {
                return false;
            }

            var quantity = (int)rawQuantity;
            command = buy
                ? new BuyCommand(traderId, itemId, quantity, clientTick, sequence)
                : (ICommand)new SellCommand(traderId, itemId, quantity, clientTick, sequence);
            return true;
        }
    }
}
