using System;
using System.Collections.Generic;
using Client.Network;
using LiteNetLib;
using Shared;

namespace Server
{
    /// <summary>
    /// UDP-транспорт сервера на LiteNetLib (ADR-002). Версия протокола проверяется на handshake:
    /// несовпадение ключа — соединение отклоняется (<c>ConnectionRejected</c> у клиента).
    /// </summary>
    public sealed class LiteNetLibServerTransport : IServerTransport
    {
        private readonly NetManager _manager;
        private readonly EventBasedNetListener _listener;
        private readonly Dictionary<int, NetPeer> _peers = new Dictionary<int, NetPeer>();
        private readonly string _connectionKey;
        private readonly int _port;
        private int _nextConnectionId = 1;
        private bool _disposed;

        /// <summary>Создаёт серверный транспорт.</summary>
        /// <param name="port">UDP-порт прослушивания.</param>
        /// <param name="protocolVersion">Ожидаемая версия протокола (ключ соединения).</param>
        /// <exception cref="ArgumentOutOfRangeException">Если порт вне диапазона 0..65535.</exception>
        public LiteNetLibServerTransport(int port, ushort protocolVersion = Protocol.Version)
        {
            if (port < 0 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, "Порт должен быть в диапазоне 0..65535.");
            }

            _port = port;
            _connectionKey = protocolVersion.ToString();

            _listener = new EventBasedNetListener();
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;

            _manager = new NetManager(_listener);
        }

        /// <summary>Фактический порт прослушивания (полезно при <c>port = 0</c>).</summary>
        public int LocalPort => _manager.LocalPort;

        /// <inheritdoc />
        public event Action<int>? ClientConnected;

        /// <inheritdoc />
        public event Action<int, string>? ClientDisconnected;

        /// <inheritdoc />
        public event Action<int, byte[]>? PacketReceived;

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">Если порт занят или сокет не открылся.</exception>
        public void Start()
        {
            if (!_manager.Start(_port))
            {
                throw new InvalidOperationException("Не удалось открыть UDP-порт " + _port + ".");
            }
        }

        /// <inheritdoc />
        public void Poll()
        {
            _manager.PollEvents();
        }

        /// <inheritdoc />
        public void Send(int connectionId, byte[] packet, DeliveryMode mode)
        {
            if (_peers.TryGetValue(connectionId, out var peer))
            {
                peer.Send(packet, 0, mode == DeliveryMode.ReliableOrdered ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
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
            _manager.Stop(sendDisconnectMessages: true);
            _peers.Clear();
        }

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (request.AcceptIfKey(_connectionKey) == null)
            {
                request.Reject();
            }
        }

        private void OnPeerConnected(NetPeer peer)
        {
            var connectionId = _nextConnectionId++;
            _peers[connectionId] = peer;
            peer.Tag = connectionId;
            ClientConnected?.Invoke(connectionId);
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (peer.Tag is int connectionId)
            {
                _peers.Remove(connectionId);
                ClientDisconnected?.Invoke(connectionId, disconnectInfo.Reason.ToString());
            }
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (peer.Tag is int connectionId)
            {
                PacketReceived?.Invoke(connectionId, reader.GetRemainingBytes());
            }
        }
    }
}
