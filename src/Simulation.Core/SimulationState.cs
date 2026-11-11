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
        private readonly List<CorpseState> _corpses;

        /// <summary>Создаёт состояние мира на указанном тике.</summary>
        /// <param name="tick">Номер тика; 0 — начальное состояние.</param>
        /// <param name="entities">Сущности мира. Порядок сохраняется и влияет на порядок в снапшоте.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="entities"/> равен <c>null</c>.</exception>
        public SimulationState(uint tick, IEnumerable<EntityState> entities)
            : this(tick, entities, null)
        {
        }

        /// <summary>Создаёт состояние мира вместе с трупами мобов.</summary>
        /// <param name="tick">Номер тика; 0 — начальное состояние.</param>
        /// <param name="entities">Сущности мира. Порядок сохраняется и влияет на порядок в снапшоте.</param>
        /// <param name="corpses">Трупы мобов; <c>null</c> — пусто.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="entities"/> равен <c>null</c>.</exception>
        public SimulationState(uint tick, IEnumerable<EntityState> entities, IEnumerable<CorpseState>? corpses)
        {
            if (entities == null)
            {
                throw new ArgumentNullException(nameof(entities));
            }

            Tick = tick;
            _entities = new List<EntityState>(entities);
            _corpses = corpses == null ? new List<CorpseState>() : new List<CorpseState>(corpses);
        }

        /// <summary>Номер текущего тика. Увеличивается симуляцией ровно на 1 за тик.</summary>
        public uint Tick { get; internal set; }

        /// <summary>Сущности мира в детерминированном порядке (порядок добавления).</summary>
        public IReadOnlyList<EntityState> Entities => _entities;

        /// <summary>Трупы мобов в детерминированном порядке (порядок смерти).</summary>
        public IReadOnlyList<CorpseState> Corpses => _corpses;

        /// <summary>Добавляет труп в мир.</summary>
        /// <param name="corpse">Труп для добавления.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="corpse"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если труп с таким Id уже есть в мире.</exception>
        public void AddCorpse(CorpseState corpse)
        {
            if (corpse == null)
            {
                throw new ArgumentNullException(nameof(corpse));
            }

            if (TryGetCorpse(corpse.Id, out _))
            {
                throw new ArgumentException("Труп с Id " + corpse.Id + " уже есть в мире.", nameof(corpse));
            }

            _corpses.Add(corpse);
        }

        /// <summary>Удаляет труп из мира.</summary>
        /// <param name="corpseId">Идентификатор трупа.</param>
        /// <returns><c>true</c>, если труп был и удалён.</returns>
        public bool RemoveCorpse(uint corpseId)
        {
            for (var i = 0; i < _corpses.Count; i++)
            {
                if (_corpses[i].Id == corpseId)
                {
                    _corpses.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Пытается найти труп по идентификатору.</summary>
        /// <param name="corpseId">Идентификатор трупа.</param>
        /// <param name="corpse">Найденный труп или <c>null</c>.</param>
        /// <returns><c>true</c>, если труп найден.</returns>
        public bool TryGetCorpse(uint corpseId, [NotNullWhen(true)] out CorpseState? corpse)
        {
            for (var i = 0; i < _corpses.Count; i++)
            {
                if (_corpses[i].Id == corpseId)
                {
                    corpse = _corpses[i];
                    return true;
                }
            }

            corpse = null;
            return false;
        }

        /// <summary>Удаляет трупы, время жизни которых истекло.</summary>
        /// <param name="tick">Текущий тик мира.</param>
        /// <returns>Сколько трупов удалено.</returns>
        public int RemoveExpiredCorpses(uint tick)
        {
            var removed = 0;
            for (var i = _corpses.Count - 1; i >= 0; i--)
            {
                if (tick >= _corpses[i].ExpireAtTick)
                {
                    _corpses.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

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
