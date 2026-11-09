using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Минимальная реализация <see cref="ISimulation"/> (walking skeleton, M1).
    /// Игровая логика появляется в M2–M3; сейчас тик лишь продвигает счётчик и фиксирует контракт.
    /// </summary>
    public sealed class Simulation : ISimulation
    {
        private SimulationState _state;

        /// <summary>Создаёт пустой мир на тике 0 без сущностей.</summary>
        public Simulation()
            : this(new SimulationState(0, Array.Empty<EntityState>()))
        {
        }

        /// <summary>Создаёт симуляцию над готовым состоянием мира (загрузка, тесты).</summary>
        /// <param name="initialState">Начальное состояние мира.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="initialState"/> равен <c>null</c>.</exception>
        public Simulation(SimulationState initialState)
        {
            _state = initialState ?? throw new ArgumentNullException(nameof(initialState));
        }

        /// <inheritdoc />
        public SimulationState State => _state;

        /// <inheritdoc />
        public void Tick(float deltaTime, IReadOnlyList<CommandEnvelope> commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (!(deltaTime > 0f) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime),
                    deltaTime,
                    "deltaTime должен быть конечным положительным числом (NaN и Infinity запрещены).");
            }

            // M2: применение команд (движение). M1: тик продвигается, состояние не меняется.
            _state.Tick++;
        }

        /// <inheritdoc />
        public SimulationSnapshot CreateSnapshot()
        {
            var entities = new EntitySnapshot[_state.Entities.Count];
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = _state.Entities[i];
                entities[i] = new EntitySnapshot(entity.Id, entity.Position, entity.Hp, entity.IsDead);
            }

            return new SimulationSnapshot(_state.Tick, entities);
        }

        /// <inheritdoc />
        public void RestoreFromSnapshot(SimulationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var entities = new EntityState[snapshot.Entities.Count];
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = snapshot.Entities[i];
                entities[i] = new EntityState(entity.Id, entity.Position, Math.Max(entity.Hp, 1))
                {
                    Hp = entity.Hp,
                };
            }

            _state = new SimulationState(snapshot.Tick, entities);
        }
    }
}
