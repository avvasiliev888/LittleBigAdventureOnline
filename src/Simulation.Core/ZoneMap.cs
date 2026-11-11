using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Карта мира: набор зон, который покрывает прямоугольный мир целиком и не имеет пересечений (ADR-013).
    /// Отвечает на вопрос «в какой зоне точка» и удерживает сущности внутри мира.
    /// </summary>
    /// <remarks>
    /// Покрытие проверяется по площадям: зоны не пересекаются и все лежат в общей рамке, поэтому
    /// равенство суммы площадей площади рамки означает, что мир покрыт без «дыр». Это O(n²)
    /// и не требует растеризации мира на старте.
    /// </remarks>
    public sealed class ZoneMap
    {
        private const double RelativeAreaTolerance = 1e-5;
        private const double AbsoluteAreaTolerance = 1e-4;

        private readonly ZoneConfig[] _zones;

        /// <summary>Создаёт карту зон и проверяет её целостность.</summary>
        /// <param name="zones">Зоны мира; должно быть не меньше одной.</param>
        /// <exception cref="ArgumentNullException">Если коллекция равна <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если зоны пересекаются, не покрывают мир или дублируются.</exception>
        public ZoneMap(IEnumerable<ZoneConfig> zones)
        {
            if (zones == null)
            {
                throw new ArgumentNullException(nameof(zones));
            }

            var list = new List<ZoneConfig>();
            foreach (var zone in zones)
            {
                if (zone == null)
                {
                    throw new ArgumentException("Список зон содержит null.", nameof(zones));
                }

                list.Add(zone);
            }

            if (list.Count == 0)
            {
                throw new ArgumentException("Мир должен содержать хотя бы одну зону.", nameof(zones));
            }

            _zones = list.ToArray();
            ValidateUniqueZones();
            ValidateNoOverlaps();
            ComputeBounds();
            ValidateCoverage();
        }

        /// <summary>Зоны мира в порядке объявления в конфиге.</summary>
        public IReadOnlyList<ZoneConfig> Zones => _zones;

        /// <summary>Минимальная координата мира по X.</summary>
        public float MinX { get; private set; }

        /// <summary>Минимальная координата мира по Z.</summary>
        public float MinZ { get; private set; }

        /// <summary>Максимальная координата мира по X.</summary>
        public float MaxX { get; private set; }

        /// <summary>Максимальная координата мира по Z.</summary>
        public float MaxZ { get; private set; }

        /// <summary>Возвращает зону, в которой находится точка.</summary>
        /// <param name="position">Позиция в мире; высота Y игнорируется.</param>
        /// <returns>Зона, содержащая точку.</returns>
        /// <exception cref="InvalidOperationException">Если точка вне мира — это ошибка симуляции, а не игровое событие.</exception>
        public ZoneConfig GetZone(Vector3 position)
        {
            if (TryGetZone(position, out var zone))
            {
                return zone!;
            }

            throw new InvalidOperationException(
                "Точка (" + position.X.ToString("0.###", CultureInfo.InvariantCulture) + "; "
                + position.Z.ToString("0.###", CultureInfo.InvariantCulture) + ") вне зон мира.");
        }

        /// <summary>Пытается найти зону, в которой находится точка.</summary>
        /// <param name="position">Позиция в мире; высота Y игнорируется.</param>
        /// <param name="zone">Найденная зона или <c>null</c>.</param>
        /// <returns><c>true</c>, если точка внутри какой-то зоны.</returns>
        /// <remarks>
        /// Если точка лежит ровно на стыке зон (общая граница), она принадлежит зоне, объявленной
        /// в конфиге раньше. Правило детерминировано и не зависит от порядка перебора.
        /// </remarks>
        public bool TryGetZone(Vector3 position, out ZoneConfig? zone)
        {
            for (var i = 0; i < _zones.Length; i++)
            {
                if (_zones[i].Contains(position))
                {
                    zone = _zones[i];
                    return true;
                }
            }

            zone = null;
            return false;
        }

        /// <summary>Возвращает зону по идентификатору.</summary>
        /// <param name="zoneId">Идентификатор зоны.</param>
        /// <exception cref="InvalidOperationException">Если такой зоны нет.</exception>
        public ZoneConfig GetZoneById(uint zoneId)
        {
            for (var i = 0; i < _zones.Length; i++)
            {
                if (_zones[i].Id == zoneId)
                {
                    return _zones[i];
                }
            }

            throw new InvalidOperationException("В конфигурации нет зоны с id " + zoneId + ".");
        }

        /// <summary>Пытается найти зону по ключу.</summary>
        /// <param name="key">Ключ зоны (например, <c>blue</c>); сравнение без учёта регистра.</param>
        /// <param name="zone">Найденная зона или <c>null</c>.</param>
        /// <returns><c>true</c>, если зона с таким ключом объявлена.</returns>
        public bool TryGetZoneByKey(string key, out ZoneConfig? zone)
        {
            for (var i = 0; i < _zones.Length; i++)
            {
                if (string.Equals(_zones[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    zone = _zones[i];
                    return true;
                }
            }

            zone = null;
            return false;
        }

        /// <summary>Удерживает точку внутри прямоугольника мира, не меняя высоту.</summary>
        /// <param name="position">Позиция, возможно вышедшая за границы мира.</param>
        /// <returns>Точка, прижатая к ближайшей границе мира.</returns>
        public Vector3 ClampToBounds(Vector3 position)
        {
            var x = Math.Clamp(position.X, MinX, MaxX);
            var z = Math.Clamp(position.Z, MinZ, MaxZ);
            return new Vector3(x, position.Y, z);
        }

        private void ValidateUniqueZones()
        {
            for (var i = 0; i < _zones.Length; i++)
            {
                for (var j = i + 1; j < _zones.Length; j++)
                {
                    if (_zones[i].Id == _zones[j].Id)
                    {
                        throw new ArgumentException("Дубликат id зоны: " + _zones[i].Id + ".");
                    }

                    if (string.Equals(_zones[i].Key, _zones[j].Key, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException("Дубликат ключа зоны: " + _zones[i].Key + ".");
                    }
                }
            }
        }

        private void ValidateNoOverlaps()
        {
            for (var i = 0; i < _zones.Length; i++)
            {
                for (var j = i + 1; j < _zones.Length; j++)
                {
                    var left = _zones[i];
                    var right = _zones[j];

                    // Касание границами допустимо: зоны стыкуются, но не пересекаются.
                    if (left.MinX < right.MaxX && right.MinX < left.MaxX
                        && left.MinZ < right.MaxZ && right.MinZ < left.MaxZ)
                    {
                        throw new ArgumentException(
                            "Зоны '" + left.Key + "' и '" + right.Key + "' пересекаются.");
                    }
                }
            }
        }

        private void ComputeBounds()
        {
            MinX = _zones[0].MinX;
            MaxX = _zones[0].MaxX;
            MinZ = _zones[0].MinZ;
            MaxZ = _zones[0].MaxZ;

            for (var i = 1; i < _zones.Length; i++)
            {
                MinX = Math.Min(MinX, _zones[i].MinX);
                MaxX = Math.Max(MaxX, _zones[i].MaxX);
                MinZ = Math.Min(MinZ, _zones[i].MinZ);
                MaxZ = Math.Max(MaxZ, _zones[i].MaxZ);
            }
        }

        private void ValidateCoverage()
        {
            var boxArea = (double)(MaxX - MinX) * (MaxZ - MinZ);
            var zonesArea = 0d;
            for (var i = 0; i < _zones.Length; i++)
            {
                zonesArea += _zones[i].Area;
            }

            var tolerance = Math.Max(AbsoluteAreaTolerance, boxArea * RelativeAreaTolerance);
            if (Math.Abs(boxArea - zonesArea) > tolerance)
            {
                throw new ArgumentException(
                    "Зоны не покрывают мир целиком: площадь мира " + boxArea.ToString("0.###", CultureInfo.InvariantCulture)
                    + ", суммарная площадь зон " + zonesArea.ToString("0.###", CultureInfo.InvariantCulture)
                    + ". Покройте мир зонами без разрывов и пересечений.");
            }
        }
    }
}
