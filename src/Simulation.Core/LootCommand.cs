using System;

namespace Simulation.Core
{
    /// <summary>
    /// Команда сбора лута из трупа (ADR-018, ADR-019): забирает всё, что помещается в инвентарь,
    /// и серебро, если игрок в радиусе сбора.
    /// </summary>
    public sealed class LootCommand : ICommand
    {
        /// <summary>Создаёт команду сбора лута.</summary>
        /// <param name="corpseId">Идентификатор трупа; положительный.</param>
        /// <param name="clientTick">Клиентский тик команды.</param>
        /// <param name="sequence">Порядковый номер команды клиента.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если идентификатор трупа равен нулю.</exception>
        public LootCommand(uint corpseId, uint clientTick, uint sequence)
        {
            if (corpseId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(corpseId), corpseId, "Id трупа должен быть положительным.");
            }

            CorpseId = corpseId;
            ClientTick = clientTick;
            Sequence = sequence;
        }

        /// <summary>Идентификатор трупа.</summary>
        public uint CorpseId { get; }

        /// <inheritdoc />
        public uint ClientTick { get; }

        /// <inheritdoc />
        public uint Sequence { get; }
    }
}
