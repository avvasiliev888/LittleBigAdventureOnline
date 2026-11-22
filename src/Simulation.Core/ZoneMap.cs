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

        private const float GateHalfThickness = 0.5f;
        private const float GateTolerance = 0.001f;

        private readonly ZoneConfig[] _zones;
        private readonly List<GateOpening> _gates = new List<GateOpening>();

        /// <summary>Прямоугольник прохода в плоскости XZ: пересекать границу можно только внутри него.</summary>
        private readonly struct GateOpening
        {
            public GateOpening(uint fromZoneId, uint toZoneId, float minX, float maxX, float minZ, float maxZ)
            {
                FromZoneId = fromZoneId;
                ToZoneId = toZoneId;
                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
            }

            public uint FromZoneId { get; }

            public uint ToZoneId { get; }

            public float MinX { get; }

            public float MaxX { get; }

            public float MinZ { get; }

            public float MaxZ { get; }
        }

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
            BuildGates();
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
            if (TryGetZoneById(zoneId, out var zone))
            {
                return zone!;
            }

            throw new InvalidOperationException("В конфигурации нет зоны с id " + zoneId + ".");
        }

        /// <summary>Пытается найти зону по идентификатору.</summary>
        /// <param name="zoneId">Идентификатор зоны.</param>
        /// <param name="zone">Найденная зона или <c>null</c>.</param>
        /// <returns><c>true</c>, если зона с таким id объявлена.</returns>
        public bool TryGetZoneById(uint zoneId, out ZoneConfig? zone)
        {
            for (var i = 0; i < _zones.Length; i++)
            {
                if (_zones[i].Id == zoneId)
                {
                    zone = _zones[i];
                    return true;
                }
            }

            zone = null;
            return false;
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

        /// <summary>
        /// Разрешает шаг движения с учётом границ зон: границу с объявленными проходами можно
        /// пересечь только внутри прохода, иначе шаг скользит вдоль стены и не проходит насквозь.
        /// </summary>
        /// <param name="start">Начало шага, внутри мира.</param>
        /// <param name="end">Желаемый конец шага; может выходить за мир.</param>
        /// <returns>Допустимая позиция шага; высота берётся из <paramref name="end"/>.</returns>
        /// <remarks>
        /// Правило одно для сервера и клиентского предсказания (ADR-006): обе стороны вызывают
        /// этот метод, поэтому стена не «телепортирует» игрока при реконсиляции. Границы без
        /// объявленных проходов остаются открытыми — так работают тестовые миры и арены.
        /// </remarks>
        public Vector3 ResolveMovement(Vector3 start, Vector3 end)
        {
            var target = ClampToBounds(end);
            if (CanTraverse(start, target))
            {
                return target;
            }

            // Скольжение вдоль вертикальной границы: X заморожен, Z свободен.
            var alongZ = new Vector3(start.X, target.Y, target.Z);
            if (Math.Abs(alongZ.Z - start.Z) > GateTolerance || Math.Abs(alongZ.X - start.X) > GateTolerance)
            {
                if (CanTraverse(start, alongZ))
                {
                    return alongZ;
                }
            }

            // Скольжение вдоль горизонтальной границы: Z заморожен, X свободен.
            var alongX = new Vector3(target.X, target.Y, start.Z);
            if (Math.Abs(alongX.X - start.X) > GateTolerance)
            {
                if (CanTraverse(start, alongX))
                {
                    return alongX;
                }
            }

            return new Vector3(start.X, target.Y, start.Z);
        }

        private bool CanTraverse(Vector3 start, Vector3 end)
        {
            if (!TryGetZone(start, out var from) || !TryGetZone(end, out var to))
            {
                return false;
            }

            if (from!.Id == to!.Id)
            {
                return true;
            }

            // Неуправляемая граница (нет ни одного прохода в эту зону) остаётся открытой.
            if (!HasGateTowards(from, to.Id) && !HasGateTowards(to, from.Id))
            {
                return true;
            }

            return PassesThroughGate(from.Id, to.Id, start, end)
                || PassesThroughGate(to.Id, from.Id, start, end);
        }

        private static bool HasGateTowards(ZoneConfig zone, uint toZoneId)
        {
            for (var i = 0; i < zone.Gates.Count; i++)
            {
                if (zone.Gates[i].ToZoneId == toZoneId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool PassesThroughGate(uint fromZoneId, uint toZoneId, Vector3 start, Vector3 end)
        {
            for (var i = 0; i < _gates.Count; i++)
            {
                var gate = _gates[i];
                if (gate.FromZoneId != fromZoneId || gate.ToZoneId != toZoneId)
                {
                    continue;
                }

                if (SegmentIntersects(start.X, start.Z, end.X, end.Z, gate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentIntersects(float x0, float z0, float x1, float z1, GateOpening gate)
        {
            // Liang–Barsky: шаг может быть длиннее прохода, поэтому проверяем весь отрезок.
            var dx = x1 - x0;
            var dz = z1 - z0;
            var t0 = 0f;
            var t1 = 1f;
            if (!Clip(-dx, x0 - gate.MinX, ref t0, ref t1)
                || !Clip(dx, gate.MaxX - x0, ref t0, ref t1)
                || !Clip(-dz, z0 - gate.MinZ, ref t0, ref t1)
                || !Clip(dz, gate.MaxZ - z0, ref t0, ref t1))
            {
                return false;
            }

            return t0 <= t1;
        }

        private static bool Clip(float denominator, float numerator, ref float t0, ref float t1)
        {
            if (Math.Abs(denominator) < 0.000001f)
            {
                return numerator >= 0f;
            }

            var t = numerator / denominator;
            if (denominator < 0f)
            {
                if (t > t1)
                {
                    return false;
                }

                if (t > t0)
                {
                    t0 = t;
                }
            }
            else
            {
                if (t < t0)
                {
                    return false;
                }

                if (t < t1)
                {
                    t1 = t;
                }
            }

            return true;
        }

        private void BuildGates()
        {
            for (var i = 0; i < _zones.Length; i++)
            {
                var zone = _zones[i];
                for (var g = 0; g < zone.Gates.Count; g++)
                {
                    var gate = zone.Gates[g];
                    if (!TryGetZoneById(gate.ToZoneId, out var neighbor))
                    {
                        throw new ArgumentException(
                            "Зона '" + zone.Key + "': проход ведёт в несуществующую зону " + gate.ToZoneId + ".");
                    }

                    AddGateOpening(zone, neighbor!, gate);
                }
            }
        }

        private void AddGateOpening(ZoneConfig zone, ZoneConfig neighbor, ZoneGate gate)
        {
            var vertical = IsAdjacentOnX(zone, neighbor);
            var horizontal = IsAdjacentOnZ(zone, neighbor);
            if (!vertical && !horizontal)
            {
                throw new ArgumentException(
                    "Зона '" + zone.Key + "': проход в '" + neighbor.Key + "' возможен только через общую границу.");
            }

            if (vertical && horizontal)
            {
                throw new ArgumentException(
                    "Зоны '" + zone.Key + "' и '" + neighbor.Key + "' соприкасаются углом: проход неоднозначен.");
            }

            if (vertical)
            {
                var border = Math.Abs(zone.MaxX - neighbor.MinX) < GateTolerance ? zone.MaxX : zone.MinX;
                if (Math.Abs(gate.X - border) > GateTolerance)
                {
                    throw new ArgumentException(
                        "Зона '" + zone.Key + "': X прохода должен лежать на границе X=" + border.ToString("0.###", CultureInfo.InvariantCulture) + ".");
                }

                var minZ = Math.Max(zone.MinZ, neighbor.MinZ);
                var maxZ = Math.Min(zone.MaxZ, neighbor.MaxZ);
                var lower = gate.Z - gate.HalfWidth;
                var upper = gate.Z + gate.HalfWidth;
                if (lower < minZ - GateTolerance || upper > maxZ + GateTolerance)
                {
                    throw new ArgumentException(
                        "Зона '" + zone.Key + "': проход выходит за общую границу с '" + neighbor.Key + "'.");
                }

                _gates.Add(new GateOpening(zone.Id, neighbor.Id,
                    border - GateHalfThickness, border + GateHalfThickness, lower, upper));
                return;
            }

            var borderZ = Math.Abs(zone.MaxZ - neighbor.MinZ) < GateTolerance ? zone.MaxZ : zone.MinZ;
            if (Math.Abs(gate.Z - borderZ) > GateTolerance)
            {
                throw new ArgumentException(
                    "Зона '" + zone.Key + "': Z прохода должен лежать на границе Z=" + borderZ.ToString("0.###", CultureInfo.InvariantCulture) + ".");
            }

            var minX = Math.Max(zone.MinX, neighbor.MinX);
            var maxX = Math.Min(zone.MaxX, neighbor.MaxX);
            var left = gate.X - gate.HalfWidth;
            var right = gate.X + gate.HalfWidth;
            if (left < minX - GateTolerance || right > maxX + GateTolerance)
            {
                throw new ArgumentException(
                    "Зона '" + zone.Key + "': проход выходит за общую границу с '" + neighbor.Key + "'.");
            }

            _gates.Add(new GateOpening(zone.Id, neighbor.Id,
                left, right, borderZ - GateHalfThickness, borderZ + GateHalfThickness));
        }

        private static bool IsAdjacentOnX(ZoneConfig left, ZoneConfig right)
        {
            var touches = Math.Abs(left.MaxX - right.MinX) < GateTolerance
                || Math.Abs(left.MinX - right.MaxX) < GateTolerance;
            return touches
                && Math.Min(left.MaxZ, right.MaxZ) > Math.Max(left.MinZ, right.MinZ) + GateTolerance;
        }

        private static bool IsAdjacentOnZ(ZoneConfig top, ZoneConfig bottom)
        {
            var touches = Math.Abs(top.MaxZ - bottom.MinZ) < GateTolerance
                || Math.Abs(top.MinZ - bottom.MaxZ) < GateTolerance;
            return touches
                && Math.Min(top.MaxX, bottom.MaxX) > Math.Max(top.MinX, bottom.MinX) + GateTolerance;
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

            var xEdges = new List<float>();
            for (var i = 0; i < _zones.Length; i++)
            {
                xEdges.Add(_zones[i].MinX);
                xEdges.Add(_zones[i].MaxX);
            }
            xEdges.Sort();

            for (var i = 0; i < xEdges.Count - 1; i++)
            {
                var x1 = xEdges[i];
                var x2 = xEdges[i + 1];
                if (x2 <= x1) continue;

                var stripZIntervals = new List<(float Min, float Max)>();
                for (var j = 0; j < _zones.Length; j++)
                {
                    var z = _zones[j];
                    if (z.MinX <= x1 && z.MaxX >= x2)
                    {
                        stripZIntervals.Add((z.MinZ, z.MaxZ));
                    }
                }
                stripZIntervals.Sort((a, b) => a.Min.CompareTo(b.Min));

                var coveredZ = MinZ;
                for (var j = 0; j < stripZIntervals.Count; j++)
                {
                    var interval = stripZIntervals[j];
                    if (interval.Min > coveredZ) break;
                    if (interval.Max > coveredZ)
                    {
                        coveredZ = interval.Max;
                    }
                }

                if (coveredZ < MaxZ)
                {
                    throw new ArgumentException(
                        "Зоны не покрывают мир целиком: обнаружен разрыв в покрытии по Z на полосе X=[" 
                        + x1.ToString("0.###", CultureInfo.InvariantCulture) + ", " 
                        + x2.ToString("0.###", CultureInfo.InvariantCulture) + "].");
                }
            }
        }
    }
}
