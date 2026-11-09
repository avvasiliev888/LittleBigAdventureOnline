using System;

namespace Client.Network
{
    /// <summary>Режим доставки пакета (ADR-002).</summary>
    public enum DeliveryMode : byte
    {
        /// <summary>Надёжно и по порядку: команды игрока, handshake, запрос полного снапшота.</summary>
        ReliableOrdered = 0,

        /// <summary>Ненадёжно: снапшоты, устаревшие данные бесполезны.</summary>
        Unreliable = 1,
    }

    /// <summary>
    /// Транспорт сообщений между клиентом и сервером. Абстракция скрывает LiteNetLib (ADR-002):
    /// за её пределами не должно быть типов конкретной библиотеки.
    /// </summary>
    /// <remarks>
    /// Контракт заморожен в M1. Реализация решает, является ли она серверным слушателем
    /// (принимает соединения) или клиентским коннектором (подключается к заданному адресу).
    /// </remarks>
    public interface ITransport : IDisposable
    {
        /// <summary>Признак установленного соединения (для сервера — наличие хотя бы одного пира).</summary>
        bool IsConnected { get; }

        /// <summary>Вызывается при получении пакета. Буфер принадлежит получателю.</summary>
        event Action<byte[]>? PacketReceived;

        /// <summary>Вызывается при разрыве соединения; аргумент — причина для лога.</summary>
        event Action<string>? Disconnected;

        /// <summary>Запускает транспорт: сервер начинает слушать порт, клиент — подключаться.</summary>
        void Start();

        /// <summary>
        /// Обрабатывает входящие события и таймеры транспорта. Должен вызываться регулярно
        /// (в серверном цикле — каждый тик, на клиенте — каждый кадр).
        /// </summary>
        void Poll();

        /// <summary>Отправляет пакет.</summary>
        /// <param name="packet">Байты пакета целиком, включая заголовок протокола.</param>
        /// <param name="mode">Требуемый режим доставки.</param>
        void Send(byte[] packet, DeliveryMode mode);
    }
}
