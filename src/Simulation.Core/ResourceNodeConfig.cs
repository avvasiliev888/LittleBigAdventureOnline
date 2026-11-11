using System;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Ресурсная жила из <c>config/resources.json</c> (ADR-015): источник сырья и Славы за сбор.
    /// Жила статична: в этом этапе она не истощается, ограничение — кулдаун сбора у игрока.
    /// </summary>
    public sealed class ResourceNodeConfig
    {
        /// <summary>Создаёт ресурсную жилу.</summary>
        /// <param name="id">Идентификатор жилы, уникальный в конфиге; положительный.</param>
        /// <param name="key">Читаемый ключ (например, <c>ash_tree</c>).</param>
        /// <param name="itemId">Предмет-сырьё, который даёт сбор; должен быть сырьём в каталоге.</param>
        /// <param name="x">Мировая координата X; конечная.</param>
        /// <param name="z">Мировая координата Z; конечная.</param>
        /// <param name="amount">Сколько предметов даёт один сбор; положительное.</param>
        /// <param name="gatherSeconds">Кулдаун сбора для игрока, с; положительный.</param>
        /// <param name="famePerGather">Слава за один сбор; неотрицательная.</param>
        /// <param name="range">Радиус взаимодействия с жилой; положительный.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если числовое значение вне допустимого диапазона.</exception>
        /// <exception cref="ArgumentException">Если ключ пуст.</exception>
        public ResourceNodeConfig(
            uint id,
            string key,
            uint itemId,
            float x,
            float z,
            int amount,
            float gatherSeconds,
            int famePerGather,
            float range)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id жилы должен быть положительным.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Ключ жилы не может быть пустым.", nameof(key));
            }

            if (itemId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemId), itemId, "Предмет жилы должен быть положительным.");
            }

            if (!IsFinite(x) || !IsFinite(z))
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, "Координаты жилы должны быть конечными числами.");
            }

            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Добыча одного сбора должна быть положительной.");
            }

            if (!(gatherSeconds > 0f) || float.IsInfinity(gatherSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gatherSeconds),
                    gatherSeconds,
                    "Кулдаун сбора должен быть конечным положительным числом.");
            }

            if (famePerGather < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(famePerGather),
                    famePerGather,
                    "Слава за сбор не может быть отрицательной.");
            }

            if (!(range > 0f) || float.IsInfinity(range))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(range),
                    range,
                    "Радиус взаимодействия должен быть конечным положительным числом.");
            }

            Id = id;
            Key = key;
            ItemId = itemId;
            Position = new Vector3(x, 0f, z);
            Amount = amount;
            GatherSeconds = gatherSeconds;
            FamePerGather = famePerGather;
            Range = range;
        }

        /// <summary>Идентификатор жилы.</summary>
        public uint Id { get; }

        /// <summary>Читаемый ключ жилы.</summary>
        public string Key { get; }

        /// <summary>Идентификатор предмета-сырья.</summary>
        public uint ItemId { get; }

        /// <summary>Позиция жилы в мире (Y = 0).</summary>
        public Vector3 Position { get; }

        /// <summary>Добыча одного сбора.</summary>
        public int Amount { get; }

        /// <summary>Кулдаун сбора для игрока в секундах.</summary>
        public float GatherSeconds { get; }

        /// <summary>Слава за один сбор.</summary>
        public int FamePerGather { get; }

        /// <summary>Радиус взаимодействия.</summary>
        public float Range { get; }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
