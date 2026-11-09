using System;

namespace Simulation.Core
{
    /// <summary>
    /// Команда применения способности по цели.
    /// Урон, радиус, кулдаун и стоимость берутся из конфига — в команде их нет.
    /// </summary>
    public sealed class AbilityCommand : ICommand
    {
        /// <summary>Создаёт команду применения способности.</summary>
        /// <param name="targetId">Идентификатор цели.</param>
        /// <param name="clientTick">Клиентский тик, к которому относится команда.</param>
        /// <param name="sequence">Порядковый номер команды внутри тика.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если идентификатор цели равен нулю.</exception>
        public AbilityCommand(uint targetId, uint clientTick, uint sequence)
        {
            if (targetId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetId), targetId, "TargetId должен быть положительным.");
            }

            TargetId = targetId;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Идентификатор цели.</summary>
        public uint TargetId { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }
    }
}
