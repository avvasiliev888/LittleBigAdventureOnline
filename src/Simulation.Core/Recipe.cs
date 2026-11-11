using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Строка рецепта: сколько какого сырья нужно. Отдельный тип, чтобы <see cref="Recipe"/>
    /// можно было собрать из JSON-загрузчика (ADR-005, ADR-017).
    /// </summary>
    public sealed class RecipeMaterial
    {
        /// <summary>Создаёт требование к сырью.</summary>
        /// <param name="itemId">Идентификатор предмета-сырья; положительный.</param>
        /// <param name="quantity">Требуемое количество; положительное.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если идентификатор или количество не положительны.</exception>
        public RecipeMaterial(uint itemId, int quantity)
        {
            if (itemId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Id сырья должен быть положительным.");
            }

            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Количество сырья должно быть положительным.");
            }

            ItemId = itemId;
            Quantity = quantity;
        }

        /// <summary>Идентификатор предмета-сырья.</summary>
        public uint ItemId { get; }

        /// <summary>Требуемое количество.</summary>
        public int Quantity { get; }
    }

    /// <summary>
    /// Рецепт крафта из <c>config/recipes.json</c> (ADR-017): сырьё напрямую, без этапа переработки.
    /// Крафт создаёт предмет в инвентаре и начисляет Славу в ветку оружия в руке.
    /// </summary>
    public sealed class Recipe
    {
        private readonly IReadOnlyList<RecipeMaterial> _materials;

        /// <summary>Создаёт рецепт.</summary>
        /// <param name="id">Идентификатор рецепта, уникальный в конфиге; положительный.</param>
        /// <param name="key">Читаемый ключ (например, <c>sword_t1</c>).</param>
        /// <param name="outputItemId">Предмет-результат; должен быть снаряжением в каталоге.</param>
        /// <param name="outputQuantity">Сколько предметов создаёт крафт; положительное.</param>
        /// <param name="materials">Сырьё рецепта; непустой список без повторов предметов.</param>
        /// <param name="famePerCraft">Слава за крафт; неотрицательная.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="materials"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если ключ пуст, сырья нет или предметы в нём повторяются.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если число вне допустимого диапазона.</exception>
        public Recipe(
            uint id,
            string key,
            uint outputItemId,
            int outputQuantity,
            IReadOnlyList<RecipeMaterial> materials,
            int famePerCraft = 0)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id рецепта должен быть положительным.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Ключ рецепта не может быть пустым.", nameof(key));
            }

            if (outputItemId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(outputItemId), outputItemId, "Предмет-результат должен быть положительным.");
            }

            if (outputQuantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(outputQuantity), outputQuantity, "Количество результата должно быть положительным.");
            }

            if (materials == null)
            {
                throw new ArgumentNullException(nameof(materials));
            }

            if (materials.Count == 0)
            {
                throw new ArgumentException("Рецепт '" + key + "' должен содержать хотя бы одно сырьё.", nameof(materials));
            }

            if (famePerCraft < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(famePerCraft), famePerCraft, "Слава за крафт не может быть отрицательной.");
            }

            var seen = new HashSet<uint>();
            var copy = new List<RecipeMaterial>(materials.Count);
            for (var i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null)
                {
                    throw new ArgumentException("Рецепт '" + key + "' содержит null в позиции " + i + ".", nameof(materials));
                }

                if (!seen.Add(material.ItemId))
                {
                    throw new ArgumentException(
                        "Рецепт '" + key + "' повторяет предмет " + material.ItemId + ".",
                        nameof(materials));
                }

                copy.Add(material);
            }

            Id = id;
            Key = key;
            OutputItemId = outputItemId;
            OutputQuantity = outputQuantity;
            _materials = copy.AsReadOnly();
            FamePerCraft = famePerCraft;
        }

        /// <summary>Идентификатор рецепта.</summary>
        public uint Id { get; }

        /// <summary>Читаемый ключ рецепта.</summary>
        public string Key { get; }

        /// <summary>Идентификатор предмета-результата.</summary>
        public uint OutputItemId { get; }

        /// <summary>Сколько предметов создаёт крафт.</summary>
        public int OutputQuantity { get; }

        /// <summary>Требуемое сырьё.</summary>
        public IReadOnlyList<RecipeMaterial> Materials => _materials;

        /// <summary>Слава за крафт.</summary>
        public int FamePerCraft { get; }
    }
}
