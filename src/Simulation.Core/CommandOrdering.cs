using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Канонический порядок команд: (ClientTick, Sequence, EntityId).
    /// Нужен, чтобы две одинаковые пачки команд, пришедшие в разном порядке, давали один и тот же результат.
    /// Симуляция порядок не меняет (контракт <see cref="ISimulation"/>), сортировка — обязанность вызывающего кода.
    /// </summary>
    public static class CommandOrdering
    {
        /// <summary>Сортирует команды в каноническом порядке, не меняя входную коллекцию.</summary>
        /// <param name="commands">Команды в произвольном порядке.</param>
        /// <returns>Новый массив в порядке (ClientTick, Sequence, EntityId).</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="commands"/> равен <c>null</c>.</exception>
        public static CommandEnvelope[] Sort(IReadOnlyList<CommandEnvelope> commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            var sorted = new CommandEnvelope[commands.Count];
            for (var i = 0; i < commands.Count; i++)
            {
                sorted[i] = commands[i];
            }

            Array.Sort(sorted, Compare);

            return sorted;
        }

        private static int Compare(CommandEnvelope left, CommandEnvelope right)
        {
            var byTick = left.Command.ClientTick.CompareTo(right.Command.ClientTick);
            if (byTick != 0)
            {
                return byTick;
            }

            var bySequence = left.Command.Sequence.CompareTo(right.Command.Sequence);
            if (bySequence != 0)
            {
                return bySequence;
            }

            return left.EntityId.CompareTo(right.EntityId);
        }
    }
}
