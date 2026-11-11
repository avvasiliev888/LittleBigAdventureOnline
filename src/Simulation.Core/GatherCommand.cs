namespace Simulation.Core
{
    /// <summary>
    /// Команда сбора ресурса из жилы. Радиус, добыча, кулдаун и Слава берутся из конфига жилы (ADR-015).
    /// </summary>
    public sealed class GatherCommand : ICommand
    {
        /// <summary>Создаёт команду сбора ресурса.</summary>
        /// <param name="nodeId">Идентификатор ресурсной жилы; положительный.</param>
        /// <param name="clientTick">Клиентский тик, к которому относится команда.</param>
        /// <param name="sequence">Порядковый номер команды внутри тика.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">Если идентификатор жилы равен нулю.</exception>
        public GatherCommand(uint nodeId, uint clientTick, uint sequence)
        {
            if (nodeId == 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(nodeId), nodeId, "NodeId должен быть положительным.");
            }

            NodeId = nodeId;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Идентификатор ресурсной жилы.</summary>
        public uint NodeId { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }
    }
}
