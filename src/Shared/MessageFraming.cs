using System;

namespace Shared
{
    /// <summary>Причина, по которой пакет не удалось разобрать (ADR-004).</summary>
    public enum FramingError : byte
    {
        /// <summary>Пакет разобран успешно.</summary>
        None = 0,

        /// <summary>Пакет короче заголовка.</summary>
        Truncated = 1,

        /// <summary>Не совпал маркер пакета — это не наш протокол.</summary>
        BadMagic = 2,

        /// <summary>Версия протокола не совпадает с локальной.</summary>
        BadVersion = 3,

        /// <summary>Длина тела не совпадает с заявленной в заголовке.</summary>
        BadLength = 4,
    }

    /// <summary>Разобранный заголовок сообщения.</summary>
    public readonly struct MessageFrame
    {
        /// <summary>Создаёт разобранный кадр.</summary>
        /// <param name="type">Тип сообщения.</param>
        /// <param name="payload">Тело сообщения.</param>
        public MessageFrame(MessageType type, byte[] payload)
        {
            Type = type;
            Payload = payload;
        }

        /// <summary>Тип сообщения.</summary>
        public MessageType Type { get; }

        /// <summary>Тело сообщения без заголовка.</summary>
        public byte[] Payload { get; }
    }

    /// <summary>
    /// Заголовок сообщения (ADR-004): magic (u16), protocolVersion (u16), messageType (u8), payloadLength (u16).
    /// Все числа little-endian, руками, без рефлексии.
    /// </summary>
    public static class MessageFraming
    {
        /// <summary>Размер заголовка в байтах.</summary>
        public const int HeaderSize = 7;

        /// <summary>Оборачивает тело сообщения в пакет с заголовком.</summary>
        /// <param name="type">Тип сообщения.</param>
        /// <param name="payload">Тело сообщения.</param>
        /// <returns>Пакет целиком.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="payload"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если тело больше <see cref="ushort.MaxValue"/> байт.</exception>
        public static byte[] Wrap(MessageType type, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > ushort.MaxValue)
            {
                throw new ArgumentException("Тело сообщения больше " + ushort.MaxValue + " байт.", nameof(payload));
            }

            var packet = new byte[HeaderSize + payload.Length];
            WriteUInt16(packet, 0, Protocol.Magic);
            WriteUInt16(packet, 2, Protocol.Version);
            packet[4] = (byte)type;
            WriteUInt16(packet, 5, (ushort)payload.Length);
            payload.CopyTo(packet.AsSpan(HeaderSize));

            return packet;
        }

        /// <summary>Разбирает пакет: проверяет magic, версию и длину.</summary>
        /// <param name="packet">Пакет целиком.</param>
        /// <param name="frame">Разобранный кадр при успехе.</param>
        /// <returns><see cref="FramingError.None"/> при успехе, иначе причину отказа.</returns>
        public static FramingError TryUnwrap(ReadOnlySpan<byte> packet, out MessageFrame frame)
        {
            frame = default;

            if (packet.Length < HeaderSize)
            {
                return FramingError.Truncated;
            }

            if (ReadUInt16(packet, 0) != Protocol.Magic)
            {
                return FramingError.BadMagic;
            }

            if (ReadUInt16(packet, 2) != Protocol.Version)
            {
                return FramingError.BadVersion;
            }

            var declared = ReadUInt16(packet, 5);
            if (packet.Length != HeaderSize + declared)
            {
                return FramingError.BadLength;
            }

            frame = new MessageFrame((MessageType)packet[4], packet.Slice(HeaderSize).ToArray());
            return FramingError.None;
        }

        /// <summary>Читает версию протокола из пакета без полной проверки (для отказа в handshake).</summary>
        /// <param name="packet">Пакет целиком.</param>
        /// <param name="version">Версия из заголовка.</param>
        /// <returns><c>true</c>, если заголовок достаточно длинный.</returns>
        public static bool TryReadVersion(ReadOnlySpan<byte> packet, out ushort version)
        {
            if (packet.Length < 4)
            {
                version = 0;
                return false;
            }

            version = ReadUInt16(packet, 2);
            return true;
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }
    }
}
