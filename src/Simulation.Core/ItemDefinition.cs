using System;

namespace Simulation.Core
{
    /// <summary>
    /// Определение предмета из <c>config/items.json</c> (ADR-005): идентификатор, вид, тир, прочность
    /// и бонусы к характеристикам. В коде ни одно из этих значений не дублируется.
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
        /// <param name="archetype">
        /// Архетип оружия (<c>warrior</c>, <c>archer</c>, <c>mage</c>): ветка дерева навыков.
        /// Обязателен для оружия в слоте <see cref="EquipmentSlot.MainHand"/> и запрещён для остальных вещей.
        /// </param>
        /// <param name="damageBonus">Плоский бонус к урону способности; только у оружия.</param>
        /// <param name="maxHpBonus">Плоский бонус к максимальному здоровью; только неотрицательный.</param>
        /// <param name="moveSpeedBonus">Доля бонуса к скорости (0.05 = +5%); неотрицательная.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если числовое значение вне допустимого диапазона.</exception>
        /// <exception cref="ArgumentException">Если ключ пуст или вид предмета противоречит слоту/прочности/бонусам.</exception>
        public ItemDefinition(
            uint id,
            string key,
            ItemKind kind,
            int tier,
            int maxDurability,
            EquipmentSlot? slot = null,
            string? archetype = null,
            int damageBonus = 0,
            int maxHpBonus = 0,
            float moveSpeedBonus = 0f)
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

            if (damageBonus < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(damageBonus),
                    damageBonus,
                    "Бонус к урону не может быть отрицательным.");
            }

            if (maxHpBonus < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxHpBonus),
                    maxHpBonus,
                    "Бонус к здоровью не может быть отрицательным.");
            }

            if (!(moveSpeedBonus >= 0f) || moveSpeedBonus > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(moveSpeedBonus),
                    moveSpeedBonus,
                    "Бонус к скорости должен быть конечным числом в диапазоне 0..1.");
            }

            if (archetype != null && string.IsNullOrWhiteSpace(archetype))
            {
                throw new ArgumentException("Архетип оружия не может быть пустым.", nameof(archetype));
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

                if (slot.Value == EquipmentSlot.MainHand)
                {
                    if (archetype == null)
                    {
                        throw new ArgumentException(
                            "Оружие '" + key + "' обязано указывать архетип ветки дерева навыков.",
                            nameof(archetype));
                    }
                }
                else
                {
                    if (archetype != null)
                    {
                        throw new ArgumentException(
                            "Архетип указывает только оружие: '" + key + "' занимает " + slot.Value + ".",
                            nameof(archetype));
                    }

                    if (damageBonus != 0)
                    {
                        throw new ArgumentException(
                            "Бонус к урону даёт только оружие: '" + key + "' занимает " + slot.Value + ".",
                            nameof(damageBonus));
                    }
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

                if (archetype != null || damageBonus != 0 || maxHpBonus != 0 || moveSpeedBonus > 0f)
                {
                    throw new ArgumentException(
                        "Сырьё '" + key + "' не даёт бонусов к характеристикам.",
                        nameof(damageBonus));
                }
            }

            Id = id;
            Key = key;
            ItemKind = kind;
            Tier = tier;
            MaxDurability = maxDurability;
            Slot = slot;
            Archetype = archetype;
            DamageBonus = damageBonus;
            MaxHpBonus = maxHpBonus;
            MoveSpeedBonus = moveSpeedBonus;
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

        /// <summary>Архетип оружия (ветка дерева навыков); <c>null</c> для не-оружия.</summary>
        public string? Archetype { get; }

        /// <summary>Плоский бонус к урону способности.</summary>
        public int DamageBonus { get; }

        /// <summary>Плоский бонус к максимальному здоровью.</summary>
        public int MaxHpBonus { get; }

        /// <summary>Доля бонуса к скорости движения.</summary>
        public float MoveSpeedBonus { get; }
    }
}
