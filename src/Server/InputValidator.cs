using System;
using Simulation.Core;

namespace Server
{
    /// <summary>Причина отказа команды (M4). Отказ — это не «просто лог»: команда не попадает в симуляцию.</summary>
    public enum CommandRejectReason
    {
        /// <summary>Команда принята.</summary>
        None = 0,

        /// <summary>Превышен лимит команд на тик.</summary>
        RateLimit = 1,

        /// <summary>Сущности клиента нет в мире.</summary>
        UnknownEntity = 2,

        /// <summary>Сущность мертва.</summary>
        DeadCaster = 3,

        /// <summary>Длина вектора направления больше допустимой (попытка ускорения).</summary>
        DirectionTooLarge = 4,

        /// <summary>Тип команды не поддерживается.</summary>
        UnsupportedCommand = 5,

        /// <summary>Цели нет в мире (историческая причина; теперь такой удар — miss без урона).</summary>
        UnknownTarget = 6,

        /// <summary>Цель мертва (историческая причина; теперь такой удар — miss без урона).</summary>
        DeadTarget = 7,

        /// <summary>Цель вне радиуса способности (историческая причина; теперь такой удар — miss без урона).</summary>
        OutOfRange = 8,

        /// <summary>Способность на кулдауне.</summary>
        OnCooldown = 9,

        /// <summary>Не хватает ресурса.</summary>
        NotEnoughResource = 10,

        /// <summary>Цель совпадает с кастером: урон по себе запрещён.</summary>
        SelfTarget = 11,
    }

    /// <summary>Результат валидации команды.</summary>
    public readonly struct ValidationResult
    {
        private ValidationResult(bool isValid, CommandRejectReason reason, string detail)
        {
            IsValid = isValid;
            Reason = reason;
            Detail = detail;
        }

        /// <summary>Команда допустима.</summary>
        public bool IsValid { get; }

        /// <summary>Причина отказа.</summary>
        public CommandRejectReason Reason { get; }

        /// <summary>Подробность для лога (цель, дистанция, тик кулдауна).</summary>
        public string Detail { get; }

        /// <summary>Создаёт результат «принято».</summary>
        /// <returns>Принятая команда.</returns>
        public static ValidationResult Accept() => new ValidationResult(true, CommandRejectReason.None, string.Empty);

        /// <summary>Создаёт результат «отклонено».</summary>
        /// <param name="reason">Причина.</param>
        /// <param name="detail">Подробность для лога.</param>
        /// <returns>Отклонённая команда.</returns>
        public static ValidationResult Reject(CommandRejectReason reason, string detail) => new ValidationResult(false, reason, detail);

        /// <inheritdoc />
        public override string ToString() => IsValid ? "ok" : Reason + " (" + Detail + ")";
    }

    /// <summary>
    /// Валидатор входящих команд (M4): лимит частоты, направление, кулдаун и ресурс способности.
    /// Отклонённая команда не применяется — иначе валидатор был бы «мягким» (AI-Failure Mode M4).
    /// </summary>
    public sealed class InputValidator
    {
        private readonly SimulationConfig _config;
        private readonly ServerOptions _options;

        /// <summary>Создаёт валидатор.</summary>
        /// <param name="config">Конфигурация акторов и способностей.</param>
        /// <param name="options">Политики сервера (лимиты ввода).</param>
        /// <exception cref="ArgumentNullException">Если аргумент равен <c>null</c>.</exception>
        public InputValidator(SimulationConfig config, ServerOptions options)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Проверяет команду перед применением.</summary>
        /// <param name="state">Текущее состояние мира.</param>
        /// <param name="tick">Текущий серверный тик.</param>
        /// <param name="envelope">Команда с привязкой к сущности.</param>
        /// <param name="commandsFromClientThisTick">Сколько команд этот клиент уже прислал на этом тике.</param>
        /// <returns>Результат проверки с причиной отказа.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="state"/> равен <c>null</c>.</exception>
        public ValidationResult Validate(
            SimulationState state,
            uint tick,
            CommandEnvelope envelope,
            int commandsFromClientThisTick)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (commandsFromClientThisTick >= _options.MaxCommandsPerTick)
            {
                return ValidationResult.Reject(
                    CommandRejectReason.RateLimit,
                    "лимит " + _options.MaxCommandsPerTick + " команд/тик");
            }

            if (!state.TryGetEntity(envelope.EntityId, out var caster))
            {
                return ValidationResult.Reject(CommandRejectReason.UnknownEntity, "сущности " + envelope.EntityId + " нет");
            }

            if (caster.IsDead)
            {
                return ValidationResult.Reject(CommandRejectReason.DeadCaster, "сущность мертва");
            }

            switch (envelope.Command)
            {
                case MoveCommand move:
                    var magnitude = move.Direction.Length();
                    if (magnitude > _options.MaxDirectionMagnitude)
                    {
                        return ValidationResult.Reject(
                            CommandRejectReason.DirectionTooLarge,
                            "|direction|=" + magnitude.ToString("0.###") + " > " + _options.MaxDirectionMagnitude.ToString("0.###"));
                    }

                    return ValidationResult.Accept();

                case AbilityCommand ability:
                    return ValidateAbility(tick, caster, ability);

                default:
                    return ValidationResult.Reject(
                        CommandRejectReason.UnsupportedCommand,
                        envelope.Command.GetType().Name);
            }
        }

        private ValidationResult ValidateAbility(uint tick, EntityState caster, AbilityCommand ability)
        {
            var abilityConfig = _config.GetAbility(_config.GetActor(caster.ActorId).AbilityId);

            // Self-target is still invalid; a missing, dead or out-of-range target is a legal miss.
            // The cooldown is charged for any valid activation, hit or miss (M10.8).
            if (ability.TargetId == caster.Id)
            {
                return ValidationResult.Reject(CommandRejectReason.SelfTarget, "цель совпадает с кастером");
            }

            if (caster.CooldownUntilTick > tick)
            {
                return ValidationResult.Reject(
                    CommandRejectReason.OnCooldown,
                    "кулдаун до тика " + caster.CooldownUntilTick);
            }

            if (caster.Resource < abilityConfig.ResourceCost)
            {
                return ValidationResult.Reject(
                    CommandRejectReason.NotEnoughResource,
                    "ресурс " + caster.Resource + " < " + abilityConfig.ResourceCost);
            }

            return ValidationResult.Accept();
        }
    }
}
