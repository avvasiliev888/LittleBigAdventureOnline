using System;

namespace Simulation.Core
{
    /// <summary>
    /// Пара «сущность + команда»: симуляция не знает, от какого соединения пришла команда,
    /// привязку выполняет сервер по своей сессии (защита от подмены EntityId клиентом).
    /// </summary>
    public readonly struct CommandEnvelope
    {
        /// <summary>Создаёт пару «сущность + команда».</summary>
        /// <param name="entityId">Идентификатор сущности, к которой применяется команда.</param>
        /// <param name="command">Команда игрока.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="command"/> равен <c>null</c>.</exception>
        public CommandEnvelope(uint entityId, ICommand command)
        {
            EntityId = entityId;
            Command = command ?? throw new ArgumentNullException(nameof(command));
        }

        /// <summary>Идентификатор сущности, к которой применяется <see cref="Command"/>.</summary>
        public uint EntityId { get; }

        /// <summary>Команда игрока.</summary>
        public ICommand Command { get; }
    }
}
