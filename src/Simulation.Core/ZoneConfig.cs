using System;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Одна зона мира: прямоугольник на плоскости X/Z и правила PvP и смерти внутри него (ADR-013).
    /// Все числа приходят из <c>config/zones.json</c>; в коде их нет (ADR-005).
    /// </summary>
    public sealed class ZoneConfig
    {
        /// <summary>Создаёт описание зоны.</summary>
        /// <param name="id">Идентификатор зоны, уникальный в конфиге; положительный.</param>
        /// <param name="key">Читаемый ключ зоны для логов и UI (например, <c>blue</c>).</param>
        /// <param name="kind">Тип зоны для UI и палитры.</param>
        /// <param name="minX">Минимальная координата X (включительно).</param>
        /// <param name="minZ">Минимальная координата Z (включительно).</param>
        /// <param name="maxX">Максимальная координата X (включительно).</param>
        /// <param name="maxZ">Максимальная координата Z (включительно).</param>
        /// <param name="pvpEnabled">Разрешён ли урон между игроками в этой зоне.</param>
        /// <param name="fullLootOnDeath">Теряет ли игрок при смерти снаряжение и инвентарь.</param>
        /// <param name="durabilityLossFraction">Доля максимальной прочности, теряемая при смерти, 0..1.</param>
        /// <param name="hostileFlagSeconds">Длительность флага враждебности после атаки на игрока, с.</param>
        /// <param name="resourceTierMin">Минимальный тир ресурсов зоны.</param>
        /// <param name="resourceTierMax">Максимальный тир ресурсов зоны.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если числовое значение вне допустимого диапазона.</exception>
        /// <exception cref="ArgumentException">Если ключ пуст, границы некорректны или правила противоречат друг другу.</exception>
        public ZoneConfig(
            uint id,
            string key,
            ZoneKind kind,
            float minX,
            float minZ,
            float maxX,
            float maxZ,
            bool pvpEnabled,
            bool fullLootOnDeath,
            float durabilityLossFraction,
            float hostileFlagSeconds,
            int resourceTierMin,
            int resourceTierMax)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id зоны должен быть положительным.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Ключ зоны не может быть пустым.", nameof(key));
            }

            if (!IsFinite(minX) || !IsFinite(minZ) || !IsFinite(maxX) || !IsFinite(maxZ))
            {
                throw new ArgumentException("Границы зоны должны быть конечными числами.");
            }

            if (!(minX < maxX) || !(minZ < maxZ))
            {
                throw new ArgumentException(
                    "Границы зоны " + key + " некорректны: minX должен быть меньше maxX, minZ — меньше maxZ.",
                    nameof(minX));
            }

            if (fullLootOnDeath && !pvpEnabled)
            {
                throw new ArgumentException(
                    "Зона " + key + ": полный лут при смерти возможен только в зоне с включённым PvP.",
                    nameof(fullLootOnDeath));
            }

            if (!(durabilityLossFraction >= 0f) || durabilityLossFraction > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durabilityLossFraction),
                    durabilityLossFraction,
                    "Доля потери прочности должна быть в диапазоне 0..1.");
            }

            if (!(hostileFlagSeconds >= 0f) || float.IsInfinity(hostileFlagSeconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hostileFlagSeconds),
                    hostileFlagSeconds,
                    "Длительность флага должна быть конечным неотрицательным числом секунд.");
            }

            if (pvpEnabled && !(hostileFlagSeconds > 0f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hostileFlagSeconds),
                    hostileFlagSeconds,
                    "Зона " + key + " с включённым PvP требует положительной длительности флага.");
            }

            if (resourceTierMin < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resourceTierMin),
                    resourceTierMin,
                    "Минимальный тир ресурсов зоны должен быть положительным.");
            }

            if (resourceTierMax < resourceTierMin)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resourceTierMax),
                    resourceTierMax,
                    "Максимальный тир ресурсов не может быть меньше минимального.");
            }

            Id = id;
            Key = key;
            Kind = kind;
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
            PvpEnabled = pvpEnabled;
            FullLootOnDeath = fullLootOnDeath;
            DurabilityLossFraction = durabilityLossFraction;
            HostileFlagSeconds = hostileFlagSeconds;
            ResourceTierMin = resourceTierMin;
            ResourceTierMax = resourceTierMax;
        }

        /// <summary>Идентификатор зоны, уникальный в конфиге.</summary>
        public uint Id { get; }

        /// <summary>Читаемый ключ зоны для логов и UI.</summary>
        public string Key { get; }

        /// <summary>Тип зоны для UI и палитры.</summary>
        public ZoneKind Kind { get; }

        /// <summary>Минимальная координата X (включительно).</summary>
        public float MinX { get; }

        /// <summary>Минимальная координата Z (включительно).</summary>
        public float MinZ { get; }

        /// <summary>Максимальная координата X (включительно).</summary>
        public float MaxX { get; }

        /// <summary>Максимальная координата Z (включительно).</summary>
        public float MaxZ { get; }

        /// <summary>Разрешён ли урон между игроками в этой зоне.</summary>
        public bool PvpEnabled { get; }

        /// <summary>Теряет ли игрок при смерти снаряжение и инвентарь.</summary>
        public bool FullLootOnDeath { get; }

        /// <summary>Доля максимальной прочности, теряемая каждой надетой вещью при смерти, 0..1.</summary>
        public float DurabilityLossFraction { get; }

        /// <summary>Длительность флага враждебности после атаки на игрока, с.</summary>
        public float HostileFlagSeconds { get; }

        /// <summary>Минимальный тир ресурсов зоны.</summary>
        public int ResourceTierMin { get; }

        /// <summary>Максимальный тир ресурсов зоны.</summary>
        public int ResourceTierMax { get; }

        /// <summary>Ширина зоны по X в единицах мира.</summary>
        public float Width => MaxX - MinX;

        /// <summary>Глубина зоны по Z в единицах мира.</summary>
        public float Depth => MaxZ - MinZ;

        /// <summary>Площадь зоны в квадратных единицах мира.</summary>
        public double Area => (double)Width * Depth;

        /// <summary>Проверяет, лежит ли точка внутри зоны (границы включительно).</summary>
        /// <param name="position">Позиция в мире; высота Y игнорируется.</param>
        /// <returns><c>true</c>, если точка внутри зоны.</returns>
        public bool Contains(Vector3 position)
        {
            return position.X >= MinX && position.X <= MaxX
                && position.Z >= MinZ && position.Z <= MaxZ;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
