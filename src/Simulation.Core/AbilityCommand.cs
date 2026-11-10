namespace Simulation.Core
{
    /// <summary>
    /// Команда применения способности по цели.
    /// Урон, радиус, кулдаун и стоимость берутся из конфига — в команде их нет.
    /// <see cref="TargetId"/> = 0 — удар в пустоту: способность активируется и тратит кулдаун,
    /// но никого не повреждает.
    /// </summary>
    public sealed class AbilityCommand : ICommand
    {
        /// <summary>Создаёт команду применения способности.</summary>
        /// <param name="targetId">Идентификатор цели; 0 — удар в пустоту.</param>
        /// <param name="clientTick">Клиентский тик, к которому относится команда.</param>
        /// <param name="sequence">Порядковый номер команды внутри тика.</param>
        public AbilityCommand(uint targetId, uint clientTick, uint sequence)
        {
            TargetId = targetId;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Идентификатор цели; 0 — удар в пустоту.</summary>
        public uint TargetId { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }
    }
}
