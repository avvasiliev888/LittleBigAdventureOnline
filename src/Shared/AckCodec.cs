using System;

namespace Shared
{
    /// <summary>
    /// Кодек сообщения <see cref="MessageType.ServerAck"/> (M7, ADR-009):
    /// идентификатор сущности, последняя обработанная команда и тик сервера, к которому она относится.
    /// </summary>
    /// <remarks>
    /// Формат (12 байт, little-endian): entityId u32, acknowledgedSequence u32, tick u32.
    /// Sequence — порядковый номер команды клиента (<see cref="Simulation.Core.ICommand.Sequence"/>).
    /// Подтверждаются и отклонённые команды: иначе клиент повторял бы их бесконечно.
    /// </remarks>
    public static class AckCodec
    {
        /// <summary>Размер полезной нагрузки ServerAck в байтах.</summary>
        public const int PayloadSize = 12;

        /// <summary>Кодирует подтверждение.</summary>
        /// <param name="entityId">Идентификатор сущности клиента.</param>
        /// <param name="acknowledgedSequence">Последняя обработанная команда.</param>
        /// <param name="tick">Тик сервера, на котором команда обработана.</param>
        /// <returns>Массив из <see cref="PayloadSize"/> байт.</returns>
        public static byte[] Encode(uint entityId, uint acknowledgedSequence, uint tick)
        {
            var payload = new byte[PayloadSize];
            WriteUInt32(payload, 0, entityId);
            WriteUInt32(payload, 4, acknowledgedSequence);
            WriteUInt32(payload, 8, tick);
            return payload;
        }

        /// <summary>Декодирует подтверждение.</summary>
        /// <param name="payload">Полезная нагрузка.</param>
        /// <param name="ack">Разобранное подтверждение.</param>
        /// <returns><c>true</c>, если размер корректен.</returns>
        public static bool TryDecode(ReadOnlySpan<byte> payload, out AckMessage ack)
        {
            if (payload.Length != PayloadSize)
            {
                ack = default;
                return false;
            }

            ack = new AckMessage(ReadUInt32(payload, 0), ReadUInt32(payload, 4), ReadUInt32(payload, 8));
            return true;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }
    }

    /// <summary>Подтверждение сервера (M7): тик, сущность и последняя обработанная команда.</summary>
    public readonly struct AckMessage
    {
        /// <summary>Создаёт подтверждение.</summary>
        /// <param name="entityId">Идентификатор сущности.</param>
        /// <param name="acknowledgedSequence">Последняя обработанная команда.</param>
        /// <param name="tick">Тик сервера.</param>
        public AckMessage(uint entityId, uint acknowledgedSequence, uint tick)
        {
            EntityId = entityId;
            AcknowledgedSequence = acknowledgedSequence;
            Tick = tick;
        }

        /// <summary>Идентификатор сущности клиента.</summary>
        public uint EntityId { get; }

        /// <summary>Последняя обработанная команда (включая отклонённые).</summary>
        public uint AcknowledgedSequence { get; }

        /// <summary>Тик сервера, к которому относится подтверждение.</summary>
        public uint Tick { get; }
    }
}
