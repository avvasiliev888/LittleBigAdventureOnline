using System.Diagnostics.CodeAnalysis;
using Simulation.Core;

namespace Client.Network
{
    /// <summary>
    /// Источник ввода игрока: превращает состояние устройств в команды. Реализуется в Unity
    /// (WASD) и в headless-тестах/ботах; симуляция о нём не знает (ADR-001).
    /// </summary>
    public interface IInputSource
    {
        /// <summary>
        /// Забирает команду для указанного клиентского тика, если игрок что-то ввёл.
        /// </summary>
        /// <param name="clientTick">Тик, к которому будет отнесена команда.</param>
        /// <param name="command">Готовая команда или <c>null</c>, если ввода нет.</param>
        /// <returns><c>true</c>, если команда получена.</returns>
        bool TryReadCommand(uint clientTick, [NotNullWhen(true)] out ICommand? command);
    }
}
