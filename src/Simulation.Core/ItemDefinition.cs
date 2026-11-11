using System;

namespace Simulation.Core
{
    /// <summary>
    /// Определение предмета из <c>config/items.json</c> (ADR-005): идентификатор, вид, тир и прочность.
    /// В коде ни одно из этих значений не дублируется.
    /// </summary>
    public sealed class ItemDefinition
    {
        /// <summary>Создаёт определение предмета.</summary>
        /// <param name="id">Идентификатор предмета, уникальный в конфиге; положительный.</param>
        /// <param name="key">Читаемый ключ предмета (например, <c>wood</c>).</param>
        /// <param name="kind">Вид предмета: сырьё или снаряжение.</param>
        /// <param name="tier">Тир предмета; положительный.</param>
        /// <param name="maxDurability">Максимальная прочность; у сырья 0, у снаряжения положительна.</param>
        /// <param name="slot">Слот снаряжения; обязателен для снаряжения и запрещён для сырья.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если числовое значение вне допустимого диапазона.</exception>
        /// <exception cref="ArgumentException">Если ключ пуст или вид предмета противоречит слоту/прочности.</exception>
        public ItemDefinition(
            uint id,
            string key,
            ItemKind kind,
            int tier,
            int maxDurability,
            EquipmentSlot? slot = null)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id предмета должен быть положительным.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Ключ предмета не может быть пустым.", nameof(key));
            }

            if (tier < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "Тир предмета должен быть положительным.");
            }

            if (maxDurability < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDurability),
                    maxDurability,
                    "Максимальная прочность не может быть отрицательной.");
            }

            if (kind == ItemKind.Equipment)
            {
                if (!slot.HasValue)
                {
                    throw new ArgumentException(
                        "Снаряжение '" + key + "' обязано указывать слот.",
                        nameof(slot));
                }

                if (maxDurability <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maxDurability),
                        maxDurability,
                        "Снаряжение '" + key + "' должно иметь положительную прочность.");
                }
            }
            else
            {
                if (slot.HasValue)
                {
                    throw new ArgumentException(
                        "Сырьё '" + key + "' не может занимать слот снаряжения.",
                        nameof(slot));
                }

                if (maxDurability != 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maxDurability),
                        maxDurability,
                        "Сырьё '" + key + "' не имеет прочности: ожидается 0.");
                }
            }

            Id = id;
            Key = key;
            ItemKind = kind;
            Tier = tier;
            MaxDurability = maxDurability;
            Slot = slot;
        }

        /// <summary>Идентификатор предмета, уникальный в конфиге.</summary>
        public uint Id { get; }

        /// <summary>Читаемый ключ предмета.</summary>
        public string Key { get; }

        /// <summary>Вид предмета: сырьё или снаряжение.</summary>
        public ItemKind ItemKind { get; }

        /// <summary>Тир предмета.</summary>
        public int Tier { get; }

        /// <summary>Максимальная прочность предмета; у сырья 0.</summary>
        public int MaxDurability { get; }

        /// <summary>Слот снаряжения; <c>null</c> для сырья.</summary>
        public EquipmentSlot? Slot { get; }
    }
}
