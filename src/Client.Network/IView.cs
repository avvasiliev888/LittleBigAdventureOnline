using Simulation.Core;

namespace Client.Network
{
    /// <summary>
    /// Представление мира на клиенте: интерполяция между снапшотами и отрисовка.
    /// Реализуется в Unity; авторитетной логики здесь быть не может (ADR-001, ADR-006).
    /// </summary>
    public interface IView
    {
        /// <summary>
        /// Принимает очередной снапшот от сервера и кладёт его в буфер интерполяции.
        /// Вызывается из сетевого слоя, а не из <c>Update</c>.
        /// </summary>
        /// <param name="snapshot">Снапшот, полученный от сервера.</param>
        void ApplySnapshot(SimulationSnapshot snapshot);

        /// <summary>
        /// Отрисовывает кадр: интерполирует состояние между последними снапшотами
        /// и обновляет визуальные объекты. Вызывается каждый кадр.
        /// </summary>
        /// <param name="frameDeltaTime">Время с предыдущего кадра в секундах.</param>
        void Render(float frameDeltaTime);
    }
}
