using System;

namespace Simulation.Core
{
    /// <summary>
    /// Параметры типа актора из конфига (M3, ADR-005). Значения приходят из JSON и в коде не дублируются.
    /// </summary>
    public sealed class ActorConfig
    {
        /// <summary>Создаёт параметры актора.</summary>
        /// <param name="id">Идентификатор типа актора (уникален в конфиге).</param>
        /// <param name="maxHp">Максимальное здоровье; положительное.</param>
        /// <param name="moveSpeed">Скорость движения, ед/с; положительная и конечная.</param>
        /// <param name="maxResource">Максимальный запас ресурса; неотрицательный.</param>
        /// <param name="respawnSeconds">Время респавна после смерти, с; положительное и конечное.</param>
        /// <param name="abilityId">Идентификатор способности актора; должен существовать в конфиге способностей.</param>
        public ActorConfig(uint id, int maxHp, float moveSpeed, int maxResource, float respawnSeconds, uint abilityId)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id актора должен быть положительным.");
            }

            if (maxHp <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "MaxHp должен быть положительным.");
            }

            if (!(moveSpeed > 0f) || float.IsInfinity(moveSpeed))
            {
                throw new ArgumentOutOfRangeException(nameof(moveSpeed), moveSpeed, "MoveSpeed должен быть конечным положительным числом.");
            }

            if (maxResource < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxResource), maxResource, "MaxResource не может быть отрицательным.");
            }

            if (!(respawnSeconds > 0f) || float.IsInfinity(respawnSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(respawnSeconds), respawnSeconds, "RespawnSeconds должен быть конечным положительным числом.");
            }

            if (abilityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(abilityId), abilityId, "AbilityId должен быть положительным.");
            }

            Id = id;
            MaxHp = maxHp;
            MoveSpeed = moveSpeed;
            MaxResource = maxResource;
            RespawnSeconds = respawnSeconds;
            AbilityId = abilityId;
        }

        /// <summary>Идентификатор типа актора.</summary>
        public uint Id { get; }

        /// <summary>Максимальное здоровье.</summary>
        public int MaxHp { get; }

        /// <summary>Скорость движения в единицах мира в секунду.</summary>
        public float MoveSpeed { get; }

        /// <summary>Максимальный запас ресурса.</summary>
        public int MaxResource { get; }

        /// <summary>Время до респавна после смерти в секундах.</summary>
        public float RespawnSeconds { get; }

        /// <summary>Идентификатор способности актора.</summary>
        public uint AbilityId { get; }
    }
}
