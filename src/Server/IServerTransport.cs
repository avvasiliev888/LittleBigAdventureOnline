using System;
using Client.Network;

namespace Server
{
    /// <summary>
    /// Серверный транспорт: несколько пиров, соединения/разрывы и приём пакетов.
    /// За абстракцией скрывается LiteNetLib (M5, ADR-002); в M4 — in-memory реализация для тестов и демо.
    /// </summary>
    public interface IServerTransport : IDisposable
    {
        /// <summary>Вызывается при подключении нового клиента; аргумент — идентификатор соединения.</summary>
        event Action<int>? ClientConnected;

        /// <summary>Вызывается при отключении клиента; аргументы — идентификатор и причина.</summary>
        event Action<int, string>? ClientDisconnected;

        /// <summary>Вызывается при получении пакета от клиента; аргументы — соединение и байты пакета.</summary>
        event Action<int, byte[]>? PacketReceived;

        /// <summary>Запускает транспорт (в M5 — открыть порт).</summary>
        void Start();

        /// <summary>Обрабатывает входящие события. Вызывается перед каждым тиком.</summary>
        void Poll();

        /// <summary>Отправляет пакет конкретному клиенту.</summary>
        /// <param name="connectionId">Идентификатор соединения.</param>
        /// <param name="packet">Пакет целиком, включая заголовок протокола.</param>
        /// <param name="mode">Режим доставки (ADR-002): команды и handshake — надёжно, снапшоты — нет.</param>
        void Send(int connectionId, byte[] packet, DeliveryMode mode);
    }
}
