using System.Numerics;

namespace Client.Network
{
    /// <summary>
    /// Расширение <see cref="IView"/> для клиентов с предсказанием (M7): представление получает
    /// предсказанную позицию и скорость локального игрока на каждом клиентском тике. Локальный
    /// игрок рисуется без задержки (<see cref="LocalPredictionSmoother"/>), удалённые — по буферу
    /// снапшотов.
    /// </summary>
    public interface IPredictedView : IView
    {
        /// <summary>Добавляет предсказанную позицию игрока на текущем клиентском тике.</summary>
        /// <param name="tick">Клиентский тик, к которому относится позиция.</param>
        /// <param name="position">Предсказанная позиция с учётом неподтверждённых команд.</param>
        /// <param name="velocity">Скорость из отправленной команды (ноль, если ввода нет).</param>
        /// <param name="reconciled">Совпала ли позиция с сервером на этом тике (для диагностики).</param>
        void SetPredictedPlayerPosition(uint tick, Vector3 position, Vector3 velocity, bool reconciled);
    }
}
