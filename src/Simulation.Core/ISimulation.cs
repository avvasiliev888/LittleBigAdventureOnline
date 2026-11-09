using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Авторитетная симуляция мира. Один и тот же контракт используют сервер и клиент
    /// (клиент — для предсказания, ADR-006).
    /// </summary>
    /// <remarks>
    /// Контракт заморожен в M1. Инварианты:
    /// <list type="bullet">
    /// <item><description>Симуляция не знает о сети, времени и графике (ADR-001).</description></item>
    /// <item><description><see cref="Tick"/> выполняется с фиксированным шагом <see cref="SimulationConstants.FixedDeltaTime"/> (ADR-003).</description></item>
    /// <item><description>Одинаковый ввод при одинаковом состоянии даёт одинаковый результат (ADR-007, replay-тест).</description></item>
    /// </list>
    /// </remarks>
    public interface ISimulation
    {
        /// <summary>Текущее состояние мира. Ссылка стабильна в пределах жизни симуляции.</summary>
        SimulationState State { get; }

        /// <summary>
        /// Продвигает мир на один тик: применяет команды и увеличивает <see cref="SimulationState.Tick"/> на 1.
        /// </summary>
        /// <param name="deltaTime">
        /// Длительность шага в секундах. На сервере и в предсказании клиента всегда
        /// <see cref="SimulationConstants.FixedDeltaTime"/>; параметр не удаляется — контракт заморожен.
        /// </param>
        /// <param name="commands">
        /// Команды, уже привязанные к сущностям и отсортированные по (ClientTick, Sequence).
        /// Симуляция не переупорядочивает их и не фильтрует по владельцу.
        /// </param>
        void Tick(float deltaTime, IReadOnlyList<CommandEnvelope> commands);

        /// <summary>Создаёт полный снапшот текущего состояния (без дельт).</summary>
        /// <returns>Снапшот на текущем тике.</returns>
        SimulationSnapshot CreateSnapshot();

        /// <summary>
        /// Восстанавливает состояние из полного снапшота. Используется загрузкой мира и реконсиляцией.
        /// </summary>
        /// <param name="snapshot">Снапшот, полученный от сервера или из replay.</param>
        void RestoreFromSnapshot(SimulationSnapshot snapshot);
    }
}
