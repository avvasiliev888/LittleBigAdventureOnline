using System;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Проход между двумя соседними зонами (ADR-013): единственное место, где разрешено пересечь
    /// их общую границу. Все числа приходят из <c>config/zones.json</c> (ADR-005).
    /// </summary>
    /// <remarks>
    /// Проход задаётся центром на общей границе и половиной ширины вдоль неё. Для вертикальной
    /// границы (<c>minX</c>/<c>maxX</c>) ширина откладывается по Z, для горизонтальной — по X.
    /// Соседство и попадание центра на границу проверяет <see cref="ZoneMap"/>: один проход
    /// описывает одну точку перехода, объявлять его с обеих сторон не нужно.
    /// </remarks>
    public sealed class ZoneGate
    {
        /// <summary>Создаёт описание прохода в соседнюю зону.</summary>
        /// <param name="toZoneId">Идентификатор соседней зоны; положительный и не равен своей зоне.</param>
        /// <param name="x">Мировая координата X центра прохода; конечная.</param>
        /// <param name="z">Мировая координата Z центра прохода; конечная.</param>
        /// <param name="halfWidth">Половина ширины прохода вдоль границы; положительная.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если числовое значение вне допустимого диапазона.</exception>
        public ZoneGate(uint toZoneId, float x, float z, float halfWidth)
        {
            if (toZoneId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(toZoneId), toZoneId, "Id соседней зоны должен быть положительным.");
            }

            if (!IsFinite(x) || !IsFinite(z))
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, "Координаты прохода должны быть конечными числами.");
            }

            if (!(halfWidth > 0f) || float.IsInfinity(halfWidth))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(halfWidth),
                    halfWidth,
                    "Половина ширины прохода должна быть конечным положительным числом.");
            }

            ToZoneId = toZoneId;
            X = x;
            Z = z;
            HalfWidth = halfWidth;
        }

        /// <summary>Идентификатор соседней зоны, в которую ведёт проход.</summary>
        public uint ToZoneId { get; }

        /// <summary>Мировая координата X центра прохода.</summary>
        public float X { get; }

        /// <summary>Мировая координата Z центра прохода.</summary>
        public float Z { get; }

        /// <summary>Половина ширины прохода вдоль общей границы, единицы мира.</summary>
        public float HalfWidth { get; }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
