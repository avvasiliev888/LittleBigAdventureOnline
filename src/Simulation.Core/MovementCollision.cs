using System;
using System.Collections.Generic;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Серверно-авторитетное разрешение движения с круговыми препятствиями.
    /// </summary>
    /// <remarks>
    /// Физика Unity не участвует в передвижении: клиент и сервер вызывают этот же код, поэтому
    /// предсказание не расходится с авторитетной позицией у деревьев, камней и других игроков.
    /// Шаг сначала ограничивается зонами (<see cref="ZoneMap.ResolveMovement"/>), затем
    /// «скользит» по окружностям: нормальная составляющая шага отбрасывается, касательная
    /// остаётся. Столкнувшиеся акторы мягко расталкиваются, чтобы не застревать.
    /// </remarks>
    public static class MovementCollision
    {
        private const float Epsilon = 0.0001f;
        private const int SlidePasses = 8;

        /// <summary>
        /// Разрешает шаг движения с учётом границ мира, статических и динамических препятствий.
        /// </summary>
        /// <param name="start">Начальная позиция актора.</param>
        /// <param name="end">Желаемая конечная позиция.</param>
        /// <param name="radius">Радиус актора; положительный.</param>
        /// <param name="zones">Карта зон; <c>null</c> — без границ.</param>
        /// <param name="world">Статические препятствия; <c>null</c> — нет.</param>
        /// <param name="actors">Круги других акторов; <c>null</c> — нет.</param>
        /// <param name="selfId">Идентификатор движущегося актора, чтобы не столкнуться с самим собой.</param>
        /// <returns>Допустимая позиция; высота берётся из <paramref name="end"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если радиус не положителен или не конечен.</exception>
        public static Vector3 Resolve(
            Vector3 start,
            Vector3 end,
            float radius,
            ZoneMap? zones,
            WorldColliders? world,
            IReadOnlyList<CollisionCircle>? actors = null,
            uint selfId = 0)
        {
            if (!(radius > 0f) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Радиус актора должен быть конечным положительным числом.");
            }

            // Границы зон и проходы — уже проверенное правило; поверх него накладываем круги.
            var target = zones == null ? end : zones.ResolveMovement(start, end);
            var position = Depenetrate(start, radius, world, actors, selfId);

            var remaining = new Vector3(target.X - position.X, 0f, target.Z - position.Z);
            for (var pass = 0; pass < SlidePasses; pass++)
            {
                if (remaining.LengthSquared() <= Epsilon * Epsilon)
                {
                    break;
                }

                var bestT = float.PositiveInfinity;
                var normalX = 0f;
                var normalZ = 0f;
                var found = false;

                if (world != null)
                {
                    var items = world.Items;
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (TryFirstHit(position, position + remaining, items[i].X, items[i].Z,
                                radius + items[i].Radius, out var t, out var nx, out var nz)
                            && t < bestT)
                        {
                            bestT = t;
                            normalX = nx;
                            normalZ = nz;
                            found = true;
                        }
                    }
                }

                if (actors != null)
                {
                    for (var i = 0; i < actors.Count; i++)
                    {
                        var actor = actors[i];
                        if (actor.EntityId == selfId || !(actor.Radius > 0f))
                        {
                            continue;
                        }

                        if (TryFirstHit(position, position + remaining, actor.X, actor.Z,
                                radius + actor.Radius, out var t, out var nx, out var nz)
                            && t < bestT)
                        {
                            bestT = t;
                            normalX = nx;
                            normalZ = nz;
                            found = true;
                        }
                    }
                }

                if (!found || bestT > 1f)
                {
                    position += remaining;
                    break;
                }

                var normal = new Vector3(normalX, 0f, normalZ);
                var into = (remaining.X * normal.X) + (remaining.Z * normal.Z);
                if (bestT <= Epsilon)
                {
                    // Уже на границе: не двигаемся внутрь, но оставляем касательную часть шага.
                    if (into < -Epsilon)
                    {
                        remaining -= normal * into;
                        continue;
                    }

                    position += remaining;
                    break;
                }

                position += remaining * bestT;
                position += normal * 0.001f;

                // Скольжение: убираем нормальную составляющую, сохраняя касательную.
                remaining -= normal * into;
            }

            position = Depenetrate(position, radius, world, actors, selfId);

            // Круг может вытолкнуть актора за границу с проходом. Возвращаем его в разрешённую
            // область тем же правилом зон и ещё раз убираем остаточное перекрытие.
            if (zones != null)
            {
                position = zones.ResolveMovement(start, new Vector3(position.X, end.Y, position.Z));
                position = Depenetrate(position, radius, world, actors, selfId);
            }

            return new Vector3(position.X, end.Y, position.Z);
        }

        /// <summary>
        /// Расталкивает пересекающиеся круги акторов. Нужен для спавнов и восстановления снапшота,
        /// где две сущности могли оказаться в одной точке; каждый сдвигается на половину перекрытия.
        /// </summary>
        /// <param name="entities">Все акторы мира.</param>
        /// <param name="zones">Карта зон для повторной проверки границ.</param>
        /// <param name="world">Статические препятствия.</param>
        /// <param name="iterations">Количество проходов расталкивания; положительное.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="entities"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="iterations"/> не положителен.</exception>
        public static void ResolveOverlaps(
            IReadOnlyList<EntityState> entities,
            ZoneMap? zones,
            WorldColliders? world,
            int iterations = 3)
        {
            if (entities == null)
            {
                throw new ArgumentNullException(nameof(entities));
            }

            if (iterations <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Количество проходов должно быть положительным.");
            }

            for (var pass = 0; pass < iterations; pass++)
            {
                var moved = false;
                for (var i = 0; i < entities.Count; i++)
                {
                    var first = entities[i];
                    if (first.IsDead)
                    {
                        continue;
                    }

                    for (var j = i + 1; j < entities.Count; j++)
                    {
                        var second = entities[j];
                        if (second.IsDead)
                        {
                            continue;
                        }

                        var dx = second.Position.X - first.Position.X;
                        var dz = second.Position.Z - first.Position.Z;
                        var minDistance = first.Actor.CollisionRadius + second.Actor.CollisionRadius;
                        var distanceSquared = (dx * dx) + (dz * dz);
                        if (distanceSquared >= minDistance * minDistance)
                        {
                            continue;
                        }

                        var distance = MathF.Sqrt(distanceSquared);
                        float nx;
                        float nz;
                        if (distance > Epsilon)
                        {
                            nx = dx / distance;
                            nz = dz / distance;
                        }
                        else
                        {
                            // Одна точка: направление детерминировано Id, иначе сервер и replay
                            // могут разойтись в выборе стороны расталкивания.
                            nx = first.Id < second.Id ? 1f : -1f;
                            nz = 0f;
                        }

                        var overlap = (minDistance - distance) * 0.5f;
                        var firstTarget = new Vector3(first.Position.X - (nx * overlap), first.Position.Y, first.Position.Z - (nz * overlap));
                        var secondTarget = new Vector3(second.Position.X + (nx * overlap), second.Position.Y, second.Position.Z + (nz * overlap));
                        first.Position = Resolve(first.Position, firstTarget, first.Actor.CollisionRadius, zones, world);
                        second.Position = Resolve(second.Position, secondTarget, second.Actor.CollisionRadius, zones, world);
                        moved = true;
                    }
                }

                if (!moved)
                {
                    return;
                }
            }
        }

        private static Vector3 Depenetrate(
            Vector3 position,
            float radius,
            WorldColliders? world,
            IReadOnlyList<CollisionCircle>? actors,
            uint selfId)
        {
            for (var pass = 0; pass < 4; pass++)
            {
                var changed = false;
                if (world != null)
                {
                    var items = world.Items;
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (PushOut(position, items[i].X, items[i].Z, radius + items[i].Radius, position, out var pushed))
                        {
                            position = pushed;
                            changed = true;
                        }
                    }
                }

                if (actors != null)
                {
                    for (var i = 0; i < actors.Count; i++)
                    {
                        var actor = actors[i];
                        if (actor.EntityId == selfId || !(actor.Radius > 0f))
                        {
                            continue;
                        }

                        if (PushOut(position, actor.X, actor.Z, radius + actor.Radius, position, out var pushed))
                        {
                            position = pushed;
                            changed = true;
                        }
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            return position;
        }

        private static bool PushOut(Vector3 position, float centerX, float centerZ, float minDistance, Vector3 fallback, out Vector3 pushed)
        {
            var dx = position.X - centerX;
            var dz = position.Z - centerZ;
            var distanceSquared = (dx * dx) + (dz * dz);
            if (distanceSquared >= minDistance * minDistance)
            {
                pushed = position;
                return false;
            }

            var distance = MathF.Sqrt(distanceSquared);
            if (distance <= Epsilon)
            {
                dx = fallback.X - centerX;
                dz = fallback.Z - centerZ;
                distance = MathF.Sqrt((dx * dx) + (dz * dz));
                if (distance <= Epsilon)
                {
                    dx = 1f;
                    dz = 0f;
                    distance = 1f;
                }
            }

            pushed = new Vector3(
                centerX + (dx / distance * minDistance),
                position.Y,
                centerZ + (dz / distance * minDistance));
            return true;
        }

        private static bool TryFirstHit(
            Vector3 from,
            Vector3 to,
            float centerX,
            float centerZ,
            float combinedRadius,
            out float t,
            out float normalX,
            out float normalZ)
        {
            var dx = to.X - from.X;
            var dz = to.Z - from.Z;
            var a = (dx * dx) + (dz * dz);
            if (a <= Epsilon * Epsilon)
            {
                t = 0f;
                normalX = 0f;
                normalZ = 0f;
                return false;
            }

            var fx = from.X - centerX;
            var fz = from.Z - centerZ;
            var c = (fx * fx) + (fz * fz) - (combinedRadius * combinedRadius);
            if (c <= 0f)
            {
                // Начало внутри: его уже вытолкнул Depenetrate. Пересечение «изнутри» не считаем,
                // иначе игрок не сможет выйти из перекрытия.
                t = 0f;
                normalX = 0f;
                normalZ = 0f;
                return false;
            }

            var b = 2f * ((fx * dx) + (fz * dz));
            var discriminant = (b * b) - (4f * a * c);
            if (discriminant < 0f)
            {
                t = 0f;
                normalX = 0f;
                normalZ = 0f;
                return false;
            }

            var root = MathF.Sqrt(discriminant);
            var hit = (-b - root) / (2f * a);
            if (hit < 0f || hit > 1f)
            {
                t = 0f;
                normalX = 0f;
                normalZ = 0f;
                return false;
            }

            t = hit;
            var px = from.X + (dx * hit) - centerX;
            var pz = from.Z + (dz * hit) - centerZ;
            var length = MathF.Sqrt((px * px) + (pz * pz));
            if (length <= Epsilon)
            {
                normalX = -dx / MathF.Sqrt(a);
                normalZ = -dz / MathF.Sqrt(a);
            }
            else
            {
                normalX = px / length;
                normalZ = pz / length;
            }

            return true;
        }
    }
}
