using System;
using Shared;
using Simulation.Core;

namespace Client.Network
{
    /// <summary>
    /// Клиентский конвейер (M6): приём снапшотов, отправка команд ввода, отрисовка.
    /// Не содержит авторитетной логики: только транспорт, ввод и представление (ADR-001, ADR-006).
    /// </summary>
    public sealed class ClientPipeline : IDisposable
    {
        /// <summary>Длительность клиентского тика, секунды (20 Гц, как у сервера).</summary>
        public const float TickIntervalSeconds = 0.05f;

        private const float HelloRetrySeconds = 0.5f;
        private const float FullSnapshotTimeoutSeconds = 1.0f;

        private readonly ITransport _transport;
        private readonly ISnapshotSerializer _serializer;
        private readonly IInputSource _input;
        private readonly IView _view;
        private readonly BinarySnapshotSerializer _fallbackSerializer = new BinarySnapshotSerializer();
        private float _tickAccumulator;
        private float _sinceHello;
        private float _sinceSnapshot;
        private uint _clientTick;
        private bool _disposed;

        /// <summary>Создаёт конвейер.</summary>
        /// <param name="transport">Транспорт (UDP или in-memory в тестах).</param>
        /// <param name="input">Источник ввода.</param>
        /// <param name="view">Представление мира.</param>
        /// <param name="serializer">Десериализатор снапшотов; по умолчанию — бинарный формат ADR-008.</param>
        /// <exception cref="ArgumentNullException">Если обязательный аргумент равен <c>null</c>.</exception>
        public ClientPipeline(ITransport transport, IInputSource input, IView view, ISnapshotSerializer? serializer = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _serializer = serializer ?? _fallbackSerializer;

            _transport.PacketReceived += OnPacketReceived;
            _transport.Disconnected += OnTransportDisconnected;
        }

        /// <summary>Идентификатор сущности игрока (0, пока сервер не ответил HelloAck).</summary>
        public uint EntityId { get; private set; }

        /// <summary>Есть ли сессия (получен HelloAck).</summary>
        public bool HasSession { get; private set; }

        /// <summary>Сколько снапшотов применено к представлению.</summary>
        public int SnapshotsApplied { get; private set; }

        /// <summary>Сессия готова: пришёл HelloAck с идентификатором сущности.</summary>
        public event Action<uint>? SessionReady;

        /// <summary>Соединение потеряно или отклонено (причина для лога).</summary>
        public event Action<string>? Disconnected;

        /// <summary>Запускает транспорт и отправляет первый Hello.</summary>
        public void Start()
        {
            _transport.Start();
            SendHello();
        }

        /// <summary>
        /// Один кадр: приём пакетов, отправка ввода по тикам, отрисовка.
        /// </summary>
        /// <param name="deltaTime">Время с предыдущего кадра, секунды.</param>
        public void Update(float deltaTime)
        {
            if (_disposed)
            {
                return;
            }

            _transport.Poll();
            _sinceHello += deltaTime;
            _sinceSnapshot += deltaTime;

            if (!HasSession)
            {
                if (_sinceHello >= HelloRetrySeconds)
                {
                    SendHello();
                }
            }
            else if (_sinceSnapshot >= FullSnapshotTimeoutSeconds)
            {
                _transport.Send(MessageFraming.Wrap(MessageType.FullSnapshotRequest, ReadOnlySpan<byte>.Empty), DeliveryMode.ReliableOrdered);
                _sinceSnapshot = 0f;
            }

            _tickAccumulator += deltaTime;
            while (_tickAccumulator >= TickIntervalSeconds)
            {
                _tickAccumulator -= TickIntervalSeconds;
                _clientTick++;

                if (_input.TryReadCommand(_clientTick, out var command))
                {
                    _transport.Send(MessageFraming.Wrap(MessageType.Command, CommandCodec.Encode(command!)), DeliveryMode.ReliableOrdered);
                }
            }

            _view.Render(deltaTime);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _transport.PacketReceived -= OnPacketReceived;
            _transport.Disconnected -= OnTransportDisconnected;
            _transport.Dispose();
        }

        private void SendHello()
        {
            _transport.Send(MessageFraming.Wrap(MessageType.Hello, ReadOnlySpan<byte>.Empty), DeliveryMode.ReliableOrdered);
            _sinceHello = 0f;
        }

        private void OnPacketReceived(byte[] packet)
        {
            if (MessageFraming.TryUnwrap(packet, out var frame) != FramingError.None)
            {
                return;
            }

            switch (frame.Type)
            {
                case MessageType.HelloAck:
                    EntityId = ReadUInt32(frame.Payload, 0);
                    HasSession = true;
                    SessionReady?.Invoke(EntityId);
                    break;

                case MessageType.Snapshot:
                    _sinceSnapshot = 0f;
                    SnapshotsApplied++;
                    _view.ApplySnapshot(_serializer.Deserialize(frame.Payload));
                    break;
            }
        }

        private void OnTransportDisconnected(string reason)
        {
            HasSession = false;
            Disconnected?.Invoke(reason);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }
    }
}
