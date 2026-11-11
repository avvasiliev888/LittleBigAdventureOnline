using System;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Простое steering-поведение мобов: если впереди статическое препятствие, моб отклоняется
    /// влево или вправо и обходит его, вместо того чтобы упираться в камень каждый тик.
    /// </summary>
    /// <remarks>
    /// Это не полноценный pathfinding: моб не строит маршрут по всей карте, а удерживает выбранную
    /// сторону обхода, пока препятствие остаётся на луче. Для открытого мира с редкими кругами
    /// этого достаточно, а стоимость — один запрос spatial hash на шаг. Если моб потерял цель,
    /// <see cref="EntityState.AiAvoidSide"/> сбрасывается и следующий обход выбирается заново.
    /// </remarks>
    public static class MobSteering
    {
        /// <summary>Минимальная длина луча, на которой моб замечает препятствие.</summary>
        private const float BaseLookAhead = 2.2f;

        /// <summary>Доля прямого направления в обходном, чтобы моб не уходил строго по касательной.</summary>
        private const float ForwardBias = 0.2f;

        private const float Epsilon = 0.0001f;

        /// <summary>
        /// Корректирует желаемое направление моба вокруг первого статического препятствия.
        /// </summary>
        /// <param name="position">Текущая позиция моба.</param>
        /// <param name="destination">Куда моб хочет попасть.</param>
        /// <param name="radius">Радиус моба; положительный.</param>
        /// <param name="world">Статические препятствия; <c>null</c> — открытый мир.</param>
        /// <param name="entityId">Идентификатор моба; используется для детерминированного выбора стороны при точной симметрии.</param>
        /// <param name="avoidSide">Текущая сторона обхода; обновляется.</param>
        /// <returns>Единичное направление шага.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Если радиус не положителен или не конечен.</exception>
        public static Vector3 Resolve(
            Vector3 position,
            Vector3 destination,
            float radius,
            WorldColliders? world,
            uint entityId,
            ref int avoidSide)
        {
            if (!(radius > 0f) || float.IsInfinity(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Радиус моба должен быть конечным положительным числом.");
            }

            var dx = destination.X - position.X;
            var dz = destination.Z - position.Z;
            var distance = MathF.Sqrt((dx * dx) + (dz * dz));
            if (distance <= Epsilon)
            {
                avoidSide = 0;
                return Vector3.Zero;
            }

            var direction = new Vector3(dx / distance, 0f, dz / distance);
            if (world == null || world.Count == 0)
            {
                avoidSide = 0;
                return direction;
            }

            // Не смотрим дальше цели: иначе моб начнёт обходить камень, который уже прошёл.
            var lookAhead = MathF.Min(distance, MathF.Max(BaseLookAhead, radius * 3f));
            var to = new Vector3(
                position.X + (direction.X * lookAhead),
                0f,
                position.Z + (direction.Z * lookAhead));

            if (!MovementCollision.TryGetFirstObstacleHit(position, to, radius, world,
                    out _, out var normalX, out var normalZ))
            {
                avoidSide = 0;
                return direction;
            }

            var leftX = -normalZ;
            var leftZ = normalX;
            var rightX = normalZ;
            var rightZ = -normalX;

            if (avoidSide == 0)
            {
                var leftDot = (leftX * direction.X) + (leftZ * direction.Z);
                var rightDot = (rightX * direction.X) + (rightZ * direction.Z);
                if (MathF.Abs(leftDot - rightDot) <= Epsilon)
                {
                    // Точно по центру камня: сторона выбирается по Id, а не по случайности.
                    avoidSide = (entityId & 1u) == 0u ? 1 : -1;
                }
                else
                {
                    avoidSide = leftDot > rightDot ? 1 : -1;
                }
            }

            var tangentX = avoidSide > 0 ? leftX : rightX;
            var tangentZ = avoidSide > 0 ? leftZ : rightZ;
            var steerX = tangentX + (direction.X * ForwardBias);
            var steerZ = tangentZ + (direction.Z * ForwardBias);
            var length = MathF.Sqrt((steerX * steerX) + (steerZ * steerZ));
            if (length <= Epsilon)
            {
                return direction;
            }

            return new Vector3(steerX / length, 0f, steerZ / length);
        }
    }
}
