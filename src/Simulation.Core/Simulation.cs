using System;
using System.Collections.Generic;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Реализация <see cref="ISimulation"/>: фиксированный тик, применение команд движения,
    /// полные снапшоты и восстановление из них.
    /// </summary>
    public sealed class Simulation : ISimulation
    {
        private readonly SimulationConfig? _config;
        private SimulationState _state;

        /// <summary>
        /// Создаёт пустой мир без игровых параметров. Допустим только до появления команд:
        /// первая же команда потребует <see cref="SimulationConfig"/>.
        /// </summary>
        public Simulation()
            : this(new SimulationState(0, Array.Empty<EntityState>()), null)
        {
        }

        /// <summary>Создаёт симуляцию над готовым состоянием мира без конфигурации.</summary>
        /// <param name="initialState">Начальное состояние мира.</param>
        public Simulation(SimulationState initialState)
            : this(initialState, null)
        {
        }

        /// <summary>Создаёт пустой мир с игровыми параметрами.</summary>
        /// <param name="config">Игровые параметры (в M3 загружаются из JSON).</param>
        public Simulation(SimulationConfig config)
            : this(new SimulationState(0, Array.Empty<EntityState>()), config)
        {
        }

        /// <summary>Создаёт симуляцию над состоянием мира с игровыми параметрами.</summary>
        /// <param name="initialState">Начальное состояние мира.</param>
        /// <param name="config">Игровые параметры; <c>null</c> допустим только для мира без команд.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="initialState"/> равен <c>null</c>.</exception>
        public Simulation(SimulationState initialState, SimulationConfig? config)
        {
            _state = initialState ?? throw new ArgumentNullException(nameof(initialState));
            _config = config;
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

            if (commands.Count > 0 && _config == null)
            {
                throw new InvalidOperationException(
                    "Команды требуют SimulationConfig: скорость движения не может быть захардкожена (ADR-005).");
            }

            for (var i = 0; i < commands.Count; i++)
            {
                ApplyCommand(commands[i], deltaTime);
            }

            _state.Tick++;
        }

        /// <inheritdoc />
        public SimulationSnapshot CreateSnapshot()
        {
            var entities = new EntitySnapshot[_state.Entities.Count];
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = _state.Entities[i];
                entities[i] = new EntitySnapshot(entity.Id, entity.Position, entity.MaxHp, entity.Hp, entity.IsDead);
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
                entities[i] = new EntityState(entity.Id, entity.Position, entity.MaxHp)
                {
                    Hp = entity.Hp,
                };
            }

            _state = new SimulationState(snapshot.Tick, entities);
        }

        private void ApplyCommand(CommandEnvelope envelope, float deltaTime)
        {
            if (!(envelope.Command is MoveCommand move))
            {
                throw new NotSupportedException(
                    "Тип команды " + envelope.Command.GetType().FullName + " не поддерживается симуляцией.");
            }

            // Команда для сущности, которой уже нет в мире, игнорируется: решение о логировании принимает сервер.
            if (!_state.TryGetEntity(envelope.EntityId, out var entity))
            {
                return;
            }

            var direction = new Vector3(move.Direction.X, 0f, move.Direction.Y);
            entity.Position += direction * (_config!.MoveSpeed * deltaTime);
        }
    }
}
