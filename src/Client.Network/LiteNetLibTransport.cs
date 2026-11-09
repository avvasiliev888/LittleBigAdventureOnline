using System;
using LiteNetLib;
using Shared;

namespace Client.Network
{
    /// <summary>
    /// UDP-транспорт клиента на LiteNetLib (ADR-002): надёжные команды, ненадёжные снапшоты.
    /// Ключ соединения — версия протокола: сервер отклоняет несовпадающую версию на handshake.
    /// </summary>
    public sealed class LiteNetLibTransport : ITransport
    {
        private readonly NetManager _manager;
        private readonly EventBasedNetListener _listener;
        private readonly string _host;
        private readonly int _port;
        private readonly string _connectionKey;
        private NetPeer? _peer;
        private bool _disposed;

        /// <summary>Создаёт клиентский транспорт (подключение начнётся в <see cref="Start"/>).</summary>
        /// <param name="host">Адрес сервера.</param>
        /// <param name="port">UDP-порт сервера.</param>
        /// <param name="protocolVersion">Версия протокола; должна совпасть с серверной.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="host"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если <paramref name="port"/> вне диапазона 1..65535.</exception>
        public LiteNetLibTransport(string host, int port, ushort protocolVersion = Protocol.Version)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));

            if (port <= 0 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(port), port, "Порт должен быть в диапазоне 1..65535.");
            }

            _port = port;
            _connectionKey = protocolVersion.ToString();

            _listener = new EventBasedNetListener();
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkErrorEvent += OnNetworkError;
            _listener.NetworkReceiveEvent += OnNetworkReceive;

            _manager = new NetManager(_listener);
        }

        /// <inheritdoc />
        public bool IsConnected => _peer != null && _peer.ConnectionState == ConnectionState.Connected;

        /// <summary>Последний измеренный RTT до сервера, мс; -1 — соединения нет.</summary>
        public int RoundTripTimeMs => _peer?.RoundTripTime ?? -1;

        /// <inheritdoc />
        public event Action<byte[]>? PacketReceived;

        /// <inheritdoc />
        public event Action<string>? Disconnected;

        /// <inheritdoc />
        public void Start()
        {
            _manager.Start();
            _peer = _manager.Connect(_host, _port, _connectionKey);
        }

        /// <inheritdoc />
        public void Poll()
        {
            _manager.PollEvents();
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Если <paramref name="packet"/> равен <c>null</c>.</exception>
        public void Send(byte[] packet, DeliveryMode mode)
        {
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            if (_peer == null || _peer.ConnectionState != ConnectionState.Connected)
            {
                return;
            }

            _peer.Send(packet, 0, ToDeliveryMethod(mode));
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
        }

        private static DeliveryMethod ToDeliveryMethod(DeliveryMode mode)
        {
            return mode == DeliveryMode.ReliableOrdered ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
        }

        private void OnPeerConnected(NetPeer peer)
        {
            _peer = peer;
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            _peer = null;
            Disconnected?.Invoke(disconnectInfo.Reason.ToString());
        }

        private void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
        {
            Disconnected?.Invoke("network error: " + socketError);
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            PacketReceived?.Invoke(reader.GetRemainingBytes());
        }
    }
}
