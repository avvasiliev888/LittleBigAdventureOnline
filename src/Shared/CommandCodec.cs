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

                default:
                    return false;
            }
        }
    }
}
