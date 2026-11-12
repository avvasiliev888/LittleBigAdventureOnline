using System;
using System.Collections.Generic;
using System.IO;
using Simulation.Core;

namespace Shared
{
    /// <summary>Стопка авторитетного инвентаря в сообщении Protocol v10.</summary>
    public readonly struct InventoryStack
    {
        /// <summary>Создаёт стопку.</summary>
        /// <param name="itemId">Идентификатор предмета; положительный.</param>
        /// <param name="quantity">Количество; положительное.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если значения не положительны.</exception>
        public InventoryStack(uint itemId, int quantity)
        {
            if (itemId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Id предмета должен быть положительным.");
            }

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Количество должно быть положительным.");
            }

            ItemId = itemId;
            Quantity = quantity;
        }

        /// <summary>Идентификатор предмета.</summary>
        public uint ItemId { get; }

        /// <summary>Количество.</summary>
        public int Quantity { get; }
    }

    /// <summary>
    /// Полное содержимое инвентаря игрока (Protocol v10). Сервер отправляет его при изменении,
    /// чтобы клиент показывал покупки, крафт, лут и добычу, а не только локальный журнал.
    /// </summary>
    public sealed class InventoryMessage
    {
        /// <summary>Создаёт сообщение инвентаря.</summary>
        /// <param name="entityId">Идентификатор сущности; положительный.</param>
        /// <param name="stacks">Стопки в порядке инвентаря.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="stacks"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="entityId"/> не положителен.</exception>
        public InventoryMessage(uint entityId, IReadOnlyList<InventoryStack> stacks)
        {
            if (entityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId), entityId, "Id сущности должен быть положительным.");
            }

            if (stacks == null)
            {
                throw new ArgumentNullException(nameof(stacks));
            }

            EntityId = entityId;
            var copy = new InventoryStack[stacks.Count];
            for (var i = 0; i < stacks.Count; i++)
            {
                copy[i] = stacks[i];
            }

            Stacks = copy;
        }

        /// <summary>Идентификатор сущности.</summary>
        public uint EntityId { get; }

        /// <summary>Стопки инвентаря.</summary>
        public IReadOnlyList<InventoryStack> Stacks { get; }
    }

    /// <summary>
    /// Кодек сообщения <see cref="MessageType.Inventory"/> (Protocol v10): entityId, число стопок
    /// и пары «itemId + quantity». Формат фиксирован; список передаётся целиком.
    /// </summary>
    public static class InventoryCodec
    {
        /// <summary>Размер заголовка: entityId и число стопок.</summary>
        public const int HeaderSize = 6;

        /// <summary>Размер одной записи стопки.</summary>
        public const int StackSize = 8;

        /// <summary>Защитный предел числа стопок в одном сообщении.</summary>
        public const int MaxStacks = 256;

        /// <summary>Кодирует инвентарь сущности.</summary>
        /// <param name="entityId">Идентификатор сущности.</param>
        /// <param name="stacks">Стопки инвентаря.</param>
        /// <returns>Тело сообщения.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="stacks"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если стопок больше <see cref="MaxStacks"/>.</exception>
        public static byte[] Encode(uint entityId, IReadOnlyList<ItemStack> stacks)
        {
            if (stacks == null)
            {
                throw new ArgumentNullException(nameof(stacks));
            }

            if (stacks.Count > MaxStacks)
            {
                throw new ArgumentException("Слишком много стопок для сообщения инвентаря: " + stacks.Count + ".", nameof(stacks));
            }

            using var stream = new MemoryStream(HeaderSize + (StackSize * stacks.Count));
            using var writer = new BinaryWriter(stream);
            writer.Write(entityId);
            writer.Write((ushort)stacks.Count);
            for (var i = 0; i < stacks.Count; i++)
            {
                writer.Write(stacks[i].ItemId);
                writer.Write(stacks[i].Quantity);
            }

            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>Декодирует инвентарь сущности.</summary>
        /// <param name="payload">Тело сообщения.</param>
        /// <param name="message">Сообщение при успехе.</param>
        /// <returns><c>true</c>, если формат корректен.</returns>
        public static bool TryDecode(ReadOnlySpan<byte> payload, out InventoryMessage? message)
        {
            message = null;
            if (payload.Length < HeaderSize)
            {
                return false;
            }

            try
            {
                using var stream = new MemoryStream(payload.ToArray(), writable: false);
                using var reader = new BinaryReader(stream);
                var entityId = reader.ReadUInt32();
                var count = reader.ReadUInt16();
                if (entityId == 0 || count > MaxStacks || payload.Length != HeaderSize + (StackSize * count))
                {
                    return false;
                }

                var stacks = new InventoryStack[count];
                for (var i = 0; i < count; i++)
                {
                    var itemId = reader.ReadUInt32();
                    var quantity = reader.ReadInt32();
                    if (itemId == 0 || quantity <= 0)
                    {
                        return false;
                    }

                    stacks[i] = new InventoryStack(itemId, quantity);
                }

                message = new InventoryMessage(entityId, stacks);
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
