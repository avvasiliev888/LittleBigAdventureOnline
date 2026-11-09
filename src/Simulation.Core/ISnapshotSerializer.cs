using System;

namespace Simulation.Core
{
    /// <summary>
    /// Преобразует снапшот в байты и обратно. Тело снапшота сериализуется в <c>Simulation.Core</c>,
    /// транспортная обёртка (magic/version/type) — в <c>Shared</c> (ADR-004).
    /// </summary>
    public interface ISnapshotSerializer
    {
        /// <summary>Сериализует полный снапшот в байты без обёртки сообщения.</summary>
        /// <param name="snapshot">Снапшот для сериализации.</param>
        /// <returns>Байты тела снапшота.</returns>
        byte[] Serialize(SimulationSnapshot snapshot);

        /// <summary>Восстанавливает снапшот из байтов тела сообщения.</summary>
        /// <param name="payload">Байты, полученные из <see cref="Serialize"/>.</param>
        /// <returns>Восстановленный снапшот.</returns>
        /// <exception cref="FormatException">Если данные повреждены или не соответствуют формату.</exception>
        SimulationSnapshot Deserialize(ReadOnlySpan<byte> payload);

        /// <summary>
        /// Сериализует дельту между двумя снапшотами (M8): только изменившиеся поля сущностей,
        /// удалённые идентификаторы и полные записи новых сущностей.
        /// </summary>
        /// <param name="baseline">Базовый снапшот, который уже есть у получателя.</param>
        /// <param name="current">Текущий снапшот (<c>Tick</c> больше, чем у базового).</param>
        /// <returns>Байты дельты без обёртки сообщения.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="baseline"/> или <paramref name="current"/> равны <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если тик текущего снапшота не больше базового.</exception>
        byte[] SerializeDelta(SimulationSnapshot baseline, SimulationSnapshot current);

        /// <summary>Применяет дельту к базовому снапшоту и получает полный снапшот текущего тика.</summary>
        /// <param name="baseline">Базовый снапшот; его тик должен совпадать с тиком, записанным в дельте.</param>
        /// <param name="payload">Байты, полученные из <see cref="SerializeDelta"/>.</param>
        /// <returns>Полный снапшот текущего тика.</returns>
        /// <exception cref="FormatException">Если данные повреждены или база не совпадает.</exception>
        SimulationSnapshot ApplyDelta(SimulationSnapshot baseline, ReadOnlySpan<byte> payload);

        /// <summary>Читает тик базового снапшота из дельты, не применяя её (для поиска базы).</summary>
        /// <param name="payload">Байты, полученные из <see cref="SerializeDelta"/>.</param>
        /// <returns>Тик базового снапшота.</returns>
        /// <exception cref="FormatException">Если данных меньше заголовка дельты.</exception>
        uint ReadBaselineTick(ReadOnlySpan<byte> payload);
    }
}
