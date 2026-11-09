using System;

namespace Simulation.Core
{
    /// <summary>
    /// Параметры способности из конфига (M3, ADR-005). В коде нет ни урона, ни радиуса, ни кулдауна.
    /// </summary>
    public sealed class AbilityConfig
    {
        /// <summary>Создаёт параметры способности.</summary>
        /// <param name="id">Идентификатор способности (уникален в конфиге).</param>
        /// <param name="damage">Урон за применение; неотрицательный.</param>
        /// <param name="range">Радиус применения в единицах мира; неотрицательный и конечный.</param>
        /// <param name="cooldownSeconds">Кулдаун между применениями, с; неотрицательный и конечный.</param>
        /// <param name="resourceCost">Стоимость применения в ресурсе; неотрицательная.</param>
        public AbilityConfig(uint id, int damage, float range, float cooldownSeconds, int resourceCost)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id способности должен быть положительным.");
            }

            if (damage < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(damage), damage, "Damage не может быть отрицательным.");
            }

            if (range < 0f || float.IsInfinity(range))
            {
                throw new ArgumentOutOfRangeException(nameof(range), range, "Range должен быть конечным неотрицательным числом.");
            }

            if (cooldownSeconds < 0f || float.IsInfinity(cooldownSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(cooldownSeconds), cooldownSeconds, "CooldownSeconds должен быть конечным неотрицательным числом.");
            }

            if (resourceCost < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resourceCost), resourceCost, "ResourceCost не может быть отрицательным.");
            }

            Id = id;
            Damage = damage;
            Range = range;
            CooldownSeconds = cooldownSeconds;
            ResourceCost = resourceCost;
        }

        /// <summary>Идентификатор способности.</summary>
        public uint Id { get; }

        /// <summary>Урон за применение.</summary>
        public int Damage { get; }

        /// <summary>Радиус применения в единицах мира.</summary>
        public float Range { get; }

        /// <summary>Кулдаун между применениями в секундах.</summary>
        public float CooldownSeconds { get; }

        /// <summary>Стоимость применения в ресурсе.</summary>
        public int ResourceCost { get; }
    }
}
