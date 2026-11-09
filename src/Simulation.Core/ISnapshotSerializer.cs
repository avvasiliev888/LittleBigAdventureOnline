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
    }
}
