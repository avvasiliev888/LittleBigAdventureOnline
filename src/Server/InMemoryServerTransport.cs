using System;
using System.Collections.Generic;
using Client.Network;

namespace Server
{
    /// <summary>
    /// In-memory транспорт: сервер и клиент живут в одном процессе (тесты M4 и демо-режим).
    /// Пакеты складываются в очереди и выдаются на <see cref="Poll"/> — поведение детерминировано.
    /// </summary>
    public sealed class InMemoryServerTransport : IServerTransport
    {
        private readonly Dictionary<int, Queue<byte[]>> _toClient = new Dictionary<int, Queue<byte[]>>();
        private readonly Queue<(int ConnectionId, byte[] Packet)> _toServer = new Queue<(int, byte[])>();
        private int _nextConnectionId = 1;
        private bool _disposed;

        /// <inheritdoc />
        public event Action<int>? ClientConnected;

        /// <inheritdoc />
        public event Action<int, string>? ClientDisconnected;

        /// <inheritdoc />
        public event Action<int, byte[]>? PacketReceived;

        /// <summary>Подключает фейкового клиента и возвращает его идентификатор соединения.</summary>
        /// <returns>Идентификатор нового соединения.</returns>
        public int ConnectClient()
        {
            var connectionId = _nextConnectionId++;
            _toClient[connectionId] = new Queue<byte[]>();
            ClientConnected?.Invoke(connectionId);
            return connectionId;
        }

        /// <summary>Отключает клиента и сообщает серверу причину.</summary>
        /// <param name="connectionId">Идентификатор соединения.</param>
        /// <param name="reason">Причина отключения для лога.</param>
        public void DisconnectClient(int connectionId, string reason)
        {
            if (_toClient.Remove(connectionId))
            {
                ClientDisconnected?.Invoke(connectionId, reason);
            }
        }

        /// <summary>Кладёт пакет «от клиента» в очередь сервера.</summary>
        /// <param name="connectionId">Идентификатор соединения.</param>
        /// <param name="packet">Пакет целиком.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="packet"/> равен <c>null</c>.</exception>
        public void SendFromClient(int connectionId, byte[] packet)
        {
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            if (_toClient.ContainsKey(connectionId))
            {
                _toServer.Enqueue((connectionId, packet));
            }
        }

        /// <summary>Забирает пакет, отправленный сервером этому клиенту.</summary>
        /// <param name="connectionId">Идентификатор соединения.</param>
        /// <param name="packet">Пакет или <c>null</c>, если очередь пуста.</param>
        /// <returns><c>true</c>, если пакет был.</returns>
        public bool TryReceive(int connectionId, out byte[]? packet)
        {
            packet = null;
            return _toClient.TryGetValue(connectionId, out var queue) && queue.Count > 0 && (packet = queue.Dequeue()) != null;
        }

        /// <inheritdoc />
        public void Start()
        {
            // In-memory транспорту открывать нечего.
        }

        /// <inheritdoc />
        public void Poll()
        {
            while (_toServer.Count > 0)
            {
                var item = _toServer.Dequeue();
                PacketReceived?.Invoke(item.ConnectionId, item.Packet);
            }
        }

        /// <inheritdoc />
        public void Send(int connectionId, byte[] packet, DeliveryMode mode)
        {
            if (_toClient.TryGetValue(connectionId, out var queue))
            {
                queue.Enqueue(packet);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _toClient.Clear();
            _toServer.Clear();
        }
    }
}
