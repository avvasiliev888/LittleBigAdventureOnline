using System;
using System.Collections.Generic;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Реализация <see cref="ISimulation"/>: фиксированный тик, движение и способности по конфигу,
    /// смерть, респавн, полные снапшоты и восстановление из них.
    /// </summary>
    public sealed class Simulation : ISimulation
    {
        private readonly SimulationConfig? _config;
        private SimulationState _state;

        /// <summary>
        /// Создаёт пустой мир без конфигурации. Допустим только до появления команд:
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

        /// <summary>Создаёт пустой мир с конфигурацией.</summary>
        /// <param name="config">Типы акторов и способности; в M4 приходит из JSON.</param>
        public Simulation(SimulationConfig config)
            : this(new SimulationState(0, Array.Empty<EntityState>()), config)
        {
        }

        /// <summary>Создаёт симуляцию над состоянием мира с конфигурацией.</summary>
        /// <param name="initialState">Начальное состояние мира.</param>
        /// <param name="config">Конфигурация; <c>null</c> допустим только для мира без команд.</param>
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
                    "Команды требуют SimulationConfig: геймплейные параметры не могут быть захардкожены (ADR-005).");
            }

            // Регенерация идёт до способностей: на тике, где ресурс добрал до стоимости,
            // удар уже возможен. Мёртвые не регенерируют; респавн ниже восстанавливает полный запас.
            RegenerateResources(deltaTime);

            for (var i = 0; i < commands.Count; i++)
            {
                ApplyCommand(commands[i], deltaTime);
            }

            ProcessRespawns();

            _state.Tick++;
        }

        /// <inheritdoc />
        public SimulationSnapshot CreateSnapshot()
        {
            var entities = new EntitySnapshot[_state.Entities.Count];
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = _state.Entities[i];
                entities[i] = new EntitySnapshot(
                    entity.Id,
                    entity.ActorId,
                    entity.Position,
                    entity.SpawnPosition,
                    entity.Hp,
                    entity.MaxHp,
                    entity.Resource,
                    entity.MaxResource,
                    entity.CooldownUntilTick,
                    entity.RespawnAtTick,
                    entity.IsDead);
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

            if (_config == null)
            {
                throw new InvalidOperationException(
                    "Восстановление снапшота требует SimulationConfig: тип актора берётся из конфига.");
            }

            var entities = new EntityState[snapshot.Entities.Count];
            for (var i = 0; i < entities.Length; i++)
            {
                var saved = snapshot.Entities[i];
                var entity = new EntityState(saved.Id, _config.GetActor(saved.ActorId), saved.SpawnPosition)
                {
                    Position = saved.Position,
                    Hp = saved.Hp,
                    Resource = saved.Resource,
                    CooldownUntilTick = saved.CooldownUntilTick,
                    RespawnAtTick = saved.RespawnAtTick,
                };
                entities[i] = entity;
            }

            _state = new SimulationState(snapshot.Tick, entities);
        }

        private void ApplyCommand(CommandEnvelope envelope, float deltaTime)
        {
            switch (envelope.Command)
            {
                case MoveCommand move:
                    // Команда для сущности, которой уже нет в мире, игнорируется: решение о логировании принимает сервер.
                    if (_state.TryGetEntity(envelope.EntityId, out var mover))
                    {
                        ApplyMove(mover, move, deltaTime);
                    }

                    return;

                case AbilityCommand ability:
                    if (_state.TryGetEntity(envelope.EntityId, out var caster))
                    {
                        ApplyAbility(caster, ability);
                    }

                    return;

                default:
                    throw new NotSupportedException(
                        "Тип команды " + envelope.Command.GetType().FullName + " не поддерживается симуляцией.");
            }
        }

        private void ApplyMove(EntityState entity, MoveCommand command, float deltaTime)
        {
            if (entity.IsDead)
            {
                return;
            }

            var speed = _config!.GetActor(entity.ActorId).MoveSpeed;
            var direction = new Vector3(command.Direction.X, 0f, command.Direction.Y);
            entity.Position += direction * (speed * deltaTime);

            // Граница мира — правило зон, а не вежливость: из мира нельзя выйти и «спрятаться» вне зон.
            entity.Position = _config.Zones.ClampToBounds(entity.Position);
        }

        private void ApplyAbility(EntityState caster, AbilityCommand command)
        {
            if (caster.IsDead)
            {
                return;
            }

            var ability = _config!.GetAbility(_config.GetActor(caster.ActorId).AbilityId);

            // Self-target is never a valid hit. It is also the one target error that does not
            // activate the ability, so a direct simulation caller cannot spend the cooldown on it.
            if (command.TargetId == caster.Id)
            {
                return;
            }

            if (caster.CooldownUntilTick > _state.Tick)
            {
                return;
            }

            if (caster.Resource < ability.ResourceCost)
            {
                return;
            }

            EntityState? target = null;
            ZoneConfig? targetZone = null;

            if (command.TargetId != 0
                && _state.TryGetEntity(command.TargetId, out var found)
                && !found.IsDead)
            {
                target = found;

                if (target.Actor.Kind == ActorKind.Player)
                {
                    targetZone = _config.Zones.GetZone(target.Position);

                    // Безопасная зона: ударить игрока нельзя, и способность не активируется —
                    // иначе клиент тратил бы ресурс и кулдаун на действие, которое сервер всё равно отменит.
                    if (!targetZone.PvpEnabled)
                    {
                        return;
                    }
                }
            }

            // Activation is charged first: a miss (no target, dead target, out of range) still
            // spends the resource and starts the cooldown, exactly like the client prediction.
            caster.Resource -= ability.ResourceCost;
            caster.CooldownUntilTick = _state.Tick + ToTicks(ability.CooldownSeconds);

            if (target == null)
            {
                return;
            }

            if (HorizontalDistance(caster.Position, target.Position) > ability.Range)
            {
                return;
            }

            target.Hp -= ability.Damage;

            if (targetZone != null)
            {
                // Флаг враждебности: напавший на игрока сам становится законной целью на время из конфига.
                caster.HostileUntilTick = _state.Tick + ToTicks(targetZone.HostileFlagSeconds);
            }

            if (target.Hp <= 0)
            {
                Kill(target);
            }
        }

        /// <summary>
        /// Применяет смерть по правилам зоны жертвы (ADR-013): в красной зоне игрок теряет снаряжение
        /// и инвентарь, в остальных зонах — только прочность; мобы правилам игроков не подчиняются.
        /// </summary>
        private void Kill(EntityState victim)
        {
            victim.Hp = 0;
            victim.CooldownUntilTick = 0;
            victim.HostileUntilTick = 0;
            victim.RespawnAtTick = _state.Tick + ToTicks(victim.Actor.RespawnSeconds);

            if (victim.Actor.Kind != ActorKind.Player)
            {
                return;
            }

            var zone = _config!.Zones.GetZone(victim.Position);
            if (zone.FullLootOnDeath)
            {
                victim.Inventory.Clear();
                victim.Equipment.Clear();
                return;
            }

            victim.Equipment.ApplyDurabilityLoss(zone.DurabilityLossFraction);
        }

        private void RegenerateResources(float deltaTime)
        {
            for (var i = 0; i < _state.Entities.Count; i++)
            {
                var entity = _state.Entities[i];
                if (entity.IsDead || entity.Resource >= entity.MaxResource)
                {
                    entity.ResourceRegenRemainder = 0f;
                    continue;
                }

                var regen = entity.Actor.ResourceRegenPerSecond;
                if (!(regen > 0f))
                {
                    entity.ResourceRegenRemainder = 0f;
                    continue;
                }

                entity.ResourceRegenRemainder += regen * deltaTime;
                if (entity.ResourceRegenRemainder < 1f)
                {
                    continue;
                }

                // Floor, а не Round: дробный остаток копится, целые единицы начисляются по мере
                // накопления. При достижении максимума остаток обнуляется, чтобы не «переполнить»
                // ресурс и не тратить накопленное за впустую.
                var gain = (int)MathF.Floor(entity.ResourceRegenRemainder);
                var room = entity.MaxResource - entity.Resource;
                if (gain >= room)
                {
                    entity.Resource = entity.MaxResource;
                    entity.ResourceRegenRemainder = 0f;
                }
                else
                {
                    entity.Resource += gain;
                    entity.ResourceRegenRemainder -= gain;
                }
            }
        }

        private void ProcessRespawns()
        {
            for (var i = 0; i < _state.Entities.Count; i++)
            {
                var entity = _state.Entities[i];
                if (!entity.IsDead || entity.RespawnAtTick == 0 || _state.Tick + 1 < entity.RespawnAtTick)
                {
                    continue;
                }

                entity.Hp = entity.MaxHp;
                entity.Resource = entity.MaxResource;
                entity.ResourceRegenRemainder = 0f;
                entity.Position = _config == null ? entity.SpawnPosition : _config.Zones.ClampToBounds(entity.SpawnPosition);
                entity.CooldownUntilTick = 0;
                entity.HostileUntilTick = 0;
                entity.RespawnAtTick = 0;

                // Страховочный набор: игрок не возрождается голым, если терять было нечего
                // (полный лут в красной зоне). Если предметы уцелели, набор не дублируется.
                if (_config != null
                    && entity.Actor.Kind == ActorKind.Player
                    && entity.Inventory.IsEmpty
                    && entity.Equipment.IsEmpty)
                {
                    entity.GrantStarterLoadout(_config);
                }
            }
        }

        private static uint ToTicks(float seconds)
        {
            var ticks = (uint)MathF.Round(seconds * SimulationConstants.TickRate);
            return ticks == 0 ? 1u : ticks;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            var dx = left.X - right.X;
            var dz = left.Z - right.Z;
            return MathF.Sqrt((dx * dx) + (dz * dz));
        }
    }
}
