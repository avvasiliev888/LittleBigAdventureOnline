using System;
using System.Collections.Generic;
using System.Numerics;
using Simulation.Core;

namespace Client.Network
{
    /// <summary>
    /// Буфер последних снапшотов и интерполяция между двумя соседними (M6).
    /// Чистая логика без Unity: её же использует <c>UnityView</c>, и её можно тестировать xUnit-ом.
    /// </summary>
    public sealed class SnapshotBuffer
    {
        private readonly List<SimulationSnapshot> _snapshots;
        private readonly int _capacity;

        /// <summary>Создаёт буфер.</summary>
        /// <param name="capacity">Сколько последних снапшотов хранить (минимум 2: интерполяция требует пару).</param>
        /// <exception cref="ArgumentOutOfRangeException">Если ёмкость меньше 2.</exception>
        public SnapshotBuffer(int capacity = 8)
        {
            if (capacity < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Для интерполяции нужно минимум 2 снапшота.");
            }

            _capacity = capacity;
            _snapshots = new List<SimulationSnapshot>(capacity);
        }

        /// <summary>Сколько снапшотов в буфере.</summary>
        public int Count => _snapshots.Count;

        /// <summary>Снапшоты в порядке возрастания тика.</summary>
        public IReadOnlyList<SimulationSnapshot> Snapshots => _snapshots;

        /// <summary>Тик самого свежего снапшота (0, если буфер пуст).</summary>
        public uint LatestTick => _snapshots.Count == 0 ? 0u : _snapshots[_snapshots.Count - 1].Tick;

        /// <summary>Тик самого старого снапшота (0, если буфер пуст).</summary>
        public uint EarliestTick => _snapshots.Count == 0 ? 0u : _snapshots[0].Tick;

        /// <summary>Добавляет снапшот; устаревшие и дубликаты игнорируются.</summary>
        /// <param name="snapshot">Снапшот от сервера.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="snapshot"/> равен <c>null</c>.</exception>
        public void Add(SimulationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (_snapshots.Count > 0 && snapshot.Tick <= LatestTick)
            {
                return;
            }

            _snapshots.Add(snapshot);
            if (_snapshots.Count > _capacity)
            {
                _snapshots.RemoveAt(0);
            }
        }

        /// <summary>
        /// Возвращает состояние сущностей на момент <paramref name="renderTimeTicks"/> (непрерывное время в тиках).
        /// До первого снапшота и после последнего — ближайший снапшот без экстраполяции.
        /// </summary>
        /// <param name="renderTimeTicks">Время отрисовки в тиках; дробная часть управляет интерполяцией.</param>
        /// <returns>Сущности для отрисовки; пустой список, если буфер пуст.</returns>
        public IReadOnlyList<InterpolatedEntity> Sample(float renderTimeTicks)
        {
            if (_snapshots.Count == 0)
            {
                return Array.Empty<InterpolatedEntity>();
            }

            if (_snapshots.Count == 1 || renderTimeTicks <= _snapshots[0].Tick)
            {
                return Project(_snapshots[0]);
            }

            if (renderTimeTicks >= LatestTick)
            {
                return Project(_snapshots[_snapshots.Count - 1]);
            }

            for (var i = 1; i < _snapshots.Count; i++)
            {
                var next = _snapshots[i];
                if (next.Tick < renderTimeTicks)
                {
                    continue;
                }

                var previous = _snapshots[i - 1];
                var span = next.Tick - previous.Tick;
                var alpha = span == 0 ? 1f : (renderTimeTicks - previous.Tick) / span;
                return Interpolate(previous, next, alpha);
            }

            return Project(_snapshots[_snapshots.Count - 1]);
        }

        private static IReadOnlyList<InterpolatedEntity> Project(SimulationSnapshot snapshot)
        {
            var result = new InterpolatedEntity[snapshot.Entities.Count];
            for (var i = 0; i < snapshot.Entities.Count; i++)
            {
                var entity = snapshot.Entities[i];
                result[i] = new InterpolatedEntity(
                    entity.Id,
                    entity.ActorId,
                    entity.Position,
                    entity.Hp,
                    entity.MaxHp,
                    entity.Resource,
                    entity.MaxResource,
                    entity.IsDead);
            }

            return result;
        }

        private static IReadOnlyList<InterpolatedEntity> Interpolate(SimulationSnapshot previous, SimulationSnapshot next, float alpha)
        {
            var result = new List<InterpolatedEntity>(previous.Entities.Count);

            foreach (var from in previous.Entities)
            {
                var to = FindById(next, from.Id);
                var position = to.HasValue ? Vector3.Lerp(from.Position, to.Value.Position, alpha) : from.Position;

                result.Add(new InterpolatedEntity(
                    from.Id,
                    from.ActorId,
                    position,
                    from.Hp,
                    from.MaxHp,
                    from.Resource,
                    from.MaxResource,
                    from.IsDead));
            }

            // Сущности, которых не было в старом снапшоте (подключились), показываем с середины интервала.
            if (alpha >= 0.5f)
            {
                foreach (var to in next.Entities)
                {
                    var exists = false;
                    foreach (var from in previous.Entities)
                    {
                        if (from.Id == to.Id)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (exists)
                    {
                        continue;
                    }

                    result.Add(new InterpolatedEntity(
                        to.Id,
                        to.ActorId,
                        to.Position,
                        to.Hp,
                        to.MaxHp,
                        to.Resource,
                        to.MaxResource,
                        to.IsDead));
                }
            }

            return result;
        }

        private static EntitySnapshot? FindById(SimulationSnapshot snapshot, uint id)
        {
            for (var i = 0; i < snapshot.Entities.Count; i++)
            {
                if (snapshot.Entities[i].Id == id)
                {
                    return snapshot.Entities[i];
                }
            }

            return null;
        }
    }
}
