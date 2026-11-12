using System;
using System.IO;
using System.Text;

namespace Shared
{
    /// <summary>
    /// Результат обработки UI-команды сервером (Protocol v10): подтверждение или причина отказа.
    /// Клиент показывает его вместо оптимистичного «отправлено», чтобы игрок видел, что
    /// команда действительно принята.
    /// </summary>
    public readonly struct CommandResult
    {
        /// <summary>Создаёт результат команды.</summary>
        /// <param name="sequence">Sequence команды, к которой относится результат.</param>
        /// <param name="accepted">Команда принята сервером.</param>
        /// <param name="detail">Причина отказа или пустая строка при успехе.</param>
        public CommandResult(uint sequence, bool accepted, string? detail)
        {
            Sequence = sequence;
            Accepted = accepted;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Sequence команды.</summary>
        public uint Sequence { get; }

        /// <summary>Команда принята.</summary>
        public bool Accepted { get; }

        /// <summary>Пояснение для игрока; пусто при успехе.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// Кодек сообщения <see cref="MessageType.CommandResult"/>: sequence, флаг принятия,
    /// длина UTF-8 пояснения и само пояснение. Формат фиксирован Protocol v10.
    /// </summary>
    public static class CommandResultCodec
    {
        /// <summary>Размер заголовка: sequence, accepted, длина строки.</summary>
        public const int HeaderSize = 7;

        /// <summary>Максимальная длина пояснения в UTF-8, байт.</summary>
        public const int MaxDetailBytes = 512;

        /// <summary>Кодирует результат команды.</summary>
        /// <param name="sequence">Sequence команды.</param>
        /// <param name="accepted">Команда принята.</param>
        /// <param name="detail">Пояснение; <c>null</c> трактуется как пустая строка.</param>
        /// <returns>Тело сообщения.</returns>
        /// <exception cref="ArgumentException">Если пояснение длиннее <see cref="MaxDetailBytes"/>.</exception>
        public static byte[] Encode(uint sequence, bool accepted, string? detail)
        {
            var text = detail ?? string.Empty;
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > MaxDetailBytes)
            {
                throw new ArgumentException("Пояснение результата длиннее " + MaxDetailBytes + " байт.", nameof(detail));
            }

            using var stream = new MemoryStream(HeaderSize + bytes.Length);
            using var writer = new BinaryWriter(stream);
            writer.Write(sequence);
            writer.Write(accepted ? (byte)1 : (byte)0);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>Декодирует результат команды.</summary>
        /// <param name="payload">Тело сообщения.</param>
        /// <param name="result">Результат при успехе.</param>
        /// <returns><c>true</c>, если формат корректен.</returns>
        public static bool TryDecode(ReadOnlySpan<byte> payload, out CommandResult result)
        {
            result = default;
            if (payload.Length < HeaderSize)
            {
                return false;
            }

            try
            {
                using var stream = new MemoryStream(payload.ToArray(), writable: false);
                using var reader = new BinaryReader(stream);
                var sequence = reader.ReadUInt32();
                var accepted = reader.ReadByte();
                var length = reader.ReadUInt16();
                if (accepted > 1 || length > MaxDetailBytes || payload.Length != HeaderSize + length)
                {
                    return false;
                }

                var detail = length == 0 ? string.Empty : Encoding.UTF8.GetString(reader.ReadBytes(length));
                result = new CommandResult(sequence, accepted == 1, detail);
                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
