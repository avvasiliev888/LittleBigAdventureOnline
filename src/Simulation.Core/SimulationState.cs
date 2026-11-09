using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Simulation.Core
{
    /// <summary>
    /// Полное состояние мира на конкретном тике: номер тика и список сущностей.
    /// Не содержит логики — только данные, которые симуляция меняет, а снапшот фиксирует.
    /// </summary>
    public sealed class SimulationState
    {
        private readonly List<EntityState> _entities;

        /// <summary>Создаёт состояние мира на указанном тике.</summary>
        /// <param name="tick">Номер тика; 0 — начальное состояние.</param>
        /// <param name="entities">Сущности мира. Порядок сохраняется и влияет на порядок в снапшоте.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="entities"/> равен <c>null</c>.</exception>
        public SimulationState(uint tick, IEnumerable<EntityState> entities)
        {
            if (entities == null)
            {
                throw new ArgumentNullException(nameof(entities));
            }

            Tick = tick;
            _entities = new List<EntityState>(entities);
        }

        /// <summary>Номер текущего тика. Увеличивается симуляцией ровно на 1 за тик.</summary>
        public uint Tick { get; internal set; }

        /// <summary>Сущности мира в детерминированном порядке (порядок добавления).</summary>
        public IReadOnlyList<EntityState> Entities => _entities;

        /// <summary>Добавляет сущность в мир (подключение игрока, спавн).</summary>
        /// <param name="entity">Сущность для добавления.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="entity"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если сущность с таким Id уже есть в мире.</exception>
        public void AddEntity(EntityState entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (TryGetEntity(entity.Id, out _))
            {
                throw new ArgumentException("Сущность с Id " + entity.Id + " уже есть в мире.", nameof(entity));
            }

            _entities.Add(entity);
        }

        /// <summary>Удаляет сущность из мира (отключение игрока).</summary>
        /// <param name="entityId">Идентификатор сущности.</param>
        /// <returns><c>true</c>, если сущность была и удалена.</returns>
        public bool RemoveEntity(uint entityId)
        {
            for (var i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Id == entityId)
                {
                    _entities.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Пытается найти сущность по идентификатору.</summary>
        /// <param name="entityId">Идентификатор сущности.</param>
        /// <param name="entity">Найденная сущность или <c>null</c>.</param>
        /// <returns><c>true</c>, если сущность найдена.</returns>
        public bool TryGetEntity(uint entityId, [NotNullWhen(true)] out EntityState? entity)
        {
            for (var i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Id == entityId)
                {
                    entity = _entities[i];
                    return true;
                }
            }

            entity = null;
            return false;
        }
    }
}
