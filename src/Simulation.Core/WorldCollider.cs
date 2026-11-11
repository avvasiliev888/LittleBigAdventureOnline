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
    public sealed class WorldColliders
    {
        private readonly WorldCollider[] _items;

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
        }

        /// <summary>Все статические препятствия в детерминированном порядке.</summary>
        public IReadOnlyList<WorldCollider> Items => _items;

        /// <summary>Количество препятствий.</summary>
        public int Count => _items.Length;
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
