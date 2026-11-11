using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Статическое круглое препятствие мира (дерево, камень, столб) из <c>config/world-colliders.json</c>.
    /// </summary>
    /// <remarks>
    /// Коллайдеры процедурного мира не создаются физикой Unity: сервер и клиентское предсказание
    /// используют один и тот же JSON, поэтому препятствие не может существовать только на клиенте.
    /// </remarks>
    public sealed class WorldCollider
    {
        /// <summary>Создаёт статический коллайдер.</summary>
        /// <param name="id">Уникальный идентификатор для логов и диагностики.</param>
        /// <param name="x">Центр по X, мировые единицы.</param>
        /// <param name="z">Центр по Z, мировые единицы.</param>
        /// <param name="radius">Радиус круга; положительный и конечный.</param>
        /// <param name="kind">Тип препятствия: <c>tree</c>, <c>rock</c> или <c>column</c>.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="id"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если идентификатор пуст.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если координата или радиус не конечны, либо радиус не положителен.</exception>
        public WorldCollider(string id, float x, float z, float radius, string kind)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Id статического коллайдера не может быть пустым.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Kind статического коллайдера не может быть пустым.", nameof(kind));
            }

            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z))
            {
                throw new ArgumentOutOfRangeException(nameof(x), "Координаты коллайдера должны быть конечными.");
            }

            if (!(radius > 0f) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Радиус коллайдера должен быть конечным положительным числом.");
            }

            Id = id;
            X = x;
            Z = z;
            Radius = radius;
            Kind = kind;
        }

        /// <summary>Уникальный идентификатор препятствия.</summary>
        public string Id { get; }

        /// <summary>Центр препятствия по X.</summary>
        public float X { get; }

        /// <summary>Центр препятствия по Z.</summary>
        public float Z { get; }

        /// <summary>Радиус круга препятствия.</summary>
        public float Radius { get; }

        /// <summary>Тип препятствия для диагностики.</summary>
        public string Kind { get; }
    }

    /// <summary>Неизменяемый набор статических препятствий, общий для сервера и клиента.</summary>
    /// <remarks>
    /// Препятствия хранятся в равномерной сетке (spatial hash) с ячейкой 8 метров. Раньше движение
    /// перебирало все 584 объекта на каждый шаг; теперь проверяются только ячейки вокруг шага.
    /// Сетка строится один раз при загрузке конфига, поэтому сервер и клиент видят одинаковый
    /// детерминированный набор кандидатов.
    /// </remarks>
    public sealed class WorldColliders
    {
        /// <summary>Размер ячейки сетки в мировых единицах.</summary>
        public const float CellSize = 8f;

        private readonly WorldCollider[] _items;
        private readonly int[] _cellStart;
        private readonly int[] _cellItems;
        private readonly float _minX;
        private readonly float _minZ;
        private readonly int _columns;
        private readonly int _rows;

        /// <summary>Создаёт набор препятствий.</summary>
        /// <param name="items">Препятствия; <c>null</c> — пустой мир.</param>
        /// <exception cref="ArgumentException">Если список содержит <c>null</c> или повторяющийся Id.</exception>
        public WorldColliders(IEnumerable<WorldCollider>? items)
        {
            var copy = new List<WorldCollider>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null)
                    {
                        throw new ArgumentException("Список препятствий содержит null.", nameof(items));
                    }

                    if (!ids.Add(item.Id))
                    {
                        throw new ArgumentException("Дубликат Id препятствия: " + item.Id + ".", nameof(items));
                    }

                    copy.Add(item);
                }
            }

            _items = copy.ToArray();
            if (_items.Length == 0)
            {
                _minX = 0f;
                _minZ = 0f;
                _columns = 1;
                _rows = 1;
                _cellStart = new[] { 0, 0 };
                _cellItems = Array.Empty<int>();
                return;
            }

            var minX = float.MaxValue;
            var minZ = float.MaxValue;
            var maxX = float.MinValue;
            var maxZ = float.MinValue;
            var maxRadius = 0f;
            for (var i = 0; i < _items.Length; i++)
            {
                var item = _items[i];
                minX = MathF.Min(minX, item.X);
                minZ = MathF.Min(minZ, item.Z);
                maxX = MathF.Max(maxX, item.X);
                maxZ = MathF.Max(maxZ, item.Z);
                maxRadius = MathF.Max(maxRadius, item.Radius);
            }

            MaxRadius = maxRadius;
            // Запас на радиус препятствия: центры у края мира тоже попадают в сетку.
            _minX = minX - maxRadius;
            _minZ = minZ - maxRadius;
            var spanX = (maxX + maxRadius) - _minX;
            var spanZ = (maxZ + maxRadius) - _minZ;
            _columns = Math.Max(1, (int)MathF.Ceiling(spanX / CellSize));
            _rows = Math.Max(1, (int)MathF.Ceiling(spanZ / CellSize));

            var buckets = new List<int>[_columns * _rows];
            for (var i = 0; i < _items.Length; i++)
            {
                var cell = CellIndex(_items[i].X, _items[i].Z);
                buckets[cell] ??= new List<int>();
                buckets[cell].Add(i);
            }

            _cellStart = new int[buckets.Length + 1];
            for (var i = 0; i < buckets.Length; i++)
            {
                _cellStart[i + 1] = _cellStart[i] + (buckets[i]?.Count ?? 0);
            }

            _cellItems = new int[_cellStart[^1]];
            for (var cell = 0; cell < buckets.Length; cell++)
            {
                var bucket = buckets[cell];
                if (bucket == null)
                {
                    continue;
                }

                var target = _cellStart[cell];
                for (var j = 0; j < bucket.Count; j++)
                {
                    _cellItems[target + j] = bucket[j];
                }
            }
        }

        /// <summary>Все статические препятствия в детерминированном порядке.</summary>
        public IReadOnlyList<WorldCollider> Items => _items;

        /// <summary>Количество препятствий.</summary>
        public int Count => _items.Length;

        /// <summary>Максимальный радиус препятствия; 0 для пустого мира.</summary>
        public float MaxRadius { get; }

        /// <summary>Количество занятых ячеек сетки; для диагностики и тестов.</summary>
        public int BucketCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _cellStart.Length - 1; i++)
                {
                    if (_cellStart[i + 1] > _cellStart[i])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Добавляет в <paramref name="results"/> препятствия, чьи центры находятся не дальше
        /// <paramref name="radius"/> от точки. Радиус должен уже включать радиус препятствия.
        /// </summary>
        /// <param name="x">Центр запроса по X.</param>
        /// <param name="z">Центр запроса по Z.</param>
        /// <param name="radius">Радиус поиска центров; неотрицательный.</param>
        /// <param name="results">Приёмник; не очищается автоматически.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="results"/> равен <c>null</c>.</exception>
        public void Query(float x, float z, float radius, List<WorldCollider> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (!(radius >= 0f) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Радиус запроса должен быть конечным и неотрицательным.");
            }

            // QueryArea отбирает по прямоугольнику; здесь оставляем только круг, чтобы
            // вызывающий код не проверял заведомо далёкие углы.
            var start = results.Count;
            QueryArea(x - radius, z - radius, x + radius, z + radius, results);
            var write = start;
            var radiusSquared = radius * radius;
            for (var read = start; read < results.Count; read++)
            {
                var item = results[read];
                var dx = item.X - x;
                var dz = item.Z - z;
                if ((dx * dx) + (dz * dz) <= radiusSquared + 0.0001f)
                {
                    results[write++] = item;
                }
            }

            if (write < results.Count)
            {
                results.RemoveRange(write, results.Count - write);
            }
        }

        /// <summary>
        /// Добавляет препятствия из прямоугольной области. Один объект попадает в результат
        /// не более одного раза, порядок детерминирован порядком ячеек.
        /// </summary>
        /// <param name="minX">Минимум по X.</param>
        /// <param name="minZ">Минимум по Z.</param>
        /// <param name="maxX">Максимум по X.</param>
        /// <param name="maxZ">Максимум по Z.</param>
        /// <param name="results">Приёмник; не очищается автоматически.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="results"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если границы не конечны или перевёрнуты.</exception>
        public void QueryArea(float minX, float minZ, float maxX, float maxZ, List<WorldCollider> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (float.IsNaN(minX) || float.IsNaN(minZ) || float.IsNaN(maxX) || float.IsNaN(maxZ)
                || float.IsInfinity(minX) || float.IsInfinity(minZ) || float.IsInfinity(maxX) || float.IsInfinity(maxZ))
            {
                throw new ArgumentOutOfRangeException(nameof(minX), "Границы запроса должны быть конечными.");
            }

            if (maxX < minX || maxZ < minZ)
            {
                throw new ArgumentOutOfRangeException(nameof(minX), "Минимум области не может быть больше максимума.");
            }

            if (_items.Length == 0)
            {
                return;
            }

            var firstColumn = Math.Clamp(CellColumn(minX), 0, _columns - 1);
            var lastColumn = Math.Clamp(CellColumn(maxX), 0, _columns - 1);
            var firstRow = Math.Clamp(CellRow(minZ), 0, _rows - 1);
            var lastRow = Math.Clamp(CellRow(maxZ), 0, _rows - 1);

            for (var row = firstRow; row <= lastRow; row++)
            {
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    var cell = (row * _columns) + column;
                    for (var i = _cellStart[cell]; i < _cellStart[cell + 1]; i++)
                    {
                        var item = _items[_cellItems[i]];
                        if (item.X >= minX && item.X <= maxX && item.Z >= minZ && item.Z <= maxZ)
                        {
                            results.Add(item);
                        }
                    }
                }
            }
        }

        private int CellIndex(float x, float z)
        {
            var column = Math.Clamp(CellColumn(x), 0, _columns - 1);
            var row = Math.Clamp(CellRow(z), 0, _rows - 1);
            return (row * _columns) + column;
        }

        private int CellColumn(float x)
        {
            var value = (x - _minX) / CellSize;
            if (value <= 0f) return int.MinValue;
            if (value >= _columns) return int.MaxValue;
            return (int)value;
        }

        private int CellRow(float z)
        {
            var value = (z - _minZ) / CellSize;
            if (value <= 0f) return int.MinValue;
            if (value >= _rows) return int.MaxValue;
            return (int)value;
        }
    }

    /// <summary>Круг движущейся сущности для проверки взаимных столкновений.</summary>
    public readonly struct CollisionCircle
    {
        /// <summary>Создаёт круг сущности.</summary>
        /// <param name="entityId">Идентификатор сущности; 0 — статическое препятствие.</param>
        /// <param name="x">Центр по X.</param>
        /// <param name="z">Центр по Z.</param>
        /// <param name="radius">Радиус; положительный.</param>
        public CollisionCircle(uint entityId, float x, float z, float radius)
        {
            EntityId = entityId;
            X = x;
            Z = z;
            Radius = radius;
        }

        /// <summary>Идентификатор сущности; 0 — статическое препятствие.</summary>
        public uint EntityId { get; }

        /// <summary>Центр по X.</summary>
        public float X { get; }

        /// <summary>Центр по Z.</summary>
        public float Z { get; }

        /// <summary>Радиус круга.</summary>
        public float Radius { get; }

        /// <summary>Создаёт круг по текущему состоянию сущности.</summary>
        /// <param name="entity">Сущность сервера.</param>
        /// <returns>Круг с её позицией и радиусом актора.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="entity"/> равен <c>null</c>.</exception>
        public static CollisionCircle FromEntity(EntityState entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            return new CollisionCircle(entity.Id, entity.Position.X, entity.Position.Z, entity.Actor.CollisionRadius);
        }
    }
}
