using System;
using System.Collections.Generic;
using Client.Network;
using Server;

namespace Tests
{
    /// <summary>
    /// Детерминированный канал «клиент ↔ сервер» с потерями и задержкой (M7).
    /// Ненадёжные пакеты (снапшоты) теряются с вероятностью и доставляются через задержку в тиках;
    /// надёжные (Hello, HelloAck, команды, ServerAck) проходят по порядку и без потерь.
    /// </summary>
    internal sealed class SimulatedLink : IServerTransport
    {
        private readonly List<Packet> _toClient = new List<Packet>();
        private readonly List<Packet> _toServer = new List<Packet>();
        private readonly HashSet<int> _clients = new HashSet<int>();
        private readonly Random _random;
        private readonly int _lossPercent;
        private readonly int _delayTicks;
        private readonly Func<uint> _currentTick;
        private int _nextConnectionId = 1;

        public SimulatedLink(int lossPercent, int delayTicks, int seed, Func<uint> currentTick)
        {
            _lossPercent = lossPercent;
            _delayTicks = delayTicks;
            _random = new Random(seed);
            _currentTick = currentTick;
        }

        public int DroppedPackets { get; private set; }

        public event Action<int>? ClientConnected;

        public event Action<int, string>? ClientDisconnected;

        public event Action<int, byte[]>? PacketReceived;

        public int ConnectClient()
        {
            var connectionId = _nextConnectionId++;
            _clients.Add(connectionId);
            ClientConnected?.Invoke(connectionId);
            return connectionId;
        }

        public void DisconnectClient(int connectionId, string reason)
        {
            if (_clients.Remove(connectionId))
            {
                ClientDisconnected?.Invoke(connectionId, reason);
            }
        }

        public void Start()
        {
        }

        public void Poll()
        {
            var now = _currentTick();

            for (var i = 0; i < _toServer.Count; i++)
            {
                var packet = _toServer[i];
                if (packet.DeliverAt > now)
                {
                    continue;
                }

                _toServer.RemoveAt(i);
                i--;
                PacketReceived?.Invoke(packet.ConnectionId, packet.Payload);
            }
        }

        public void Send(int connectionId, byte[] packet, DeliveryMode mode)
        {
            if (!_clients.Contains(connectionId))
            {
                return;
            }

            if (mode == DeliveryMode.Unreliable && _random.Next(100) < _lossPercent)
            {
                DroppedPackets++;
                return;
            }

            _toClient.Add(new Packet(_currentTick() + (uint)_delayTicks, connectionId, packet));
        }

        public void SendFromClient(int connectionId, byte[] packet)
        {
            if (_clients.Contains(connectionId))
            {
                // Задержка в обе стороны: иначе команда доходит до сервера мгновенно и предсказание
                // не опережает подтверждённое состояние (RTT = 2 × delayTicks).
                _toServer.Add(new Packet(_currentTick() + (uint)_delayTicks, connectionId, packet));
            }
        }

        public bool TryReceive(int connectionId, out byte[]? packet)
        {
            packet = null;

            for (var i = 0; i < _toClient.Count; i++)
            {
                var candidate = _toClient[i];
                if (candidate.ConnectionId != connectionId)
                {
                    continue;
                }

                if (candidate.DeliverAt > _currentTick())
                {
                    // Порядок канала сохраняем: более поздний пакет не обгоняет отложенный.
                    return false;
                }

                _toClient.RemoveAt(i);
                packet = candidate.Payload;
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            _toClient.Clear();
            _toServer.Clear();
            _clients.Clear();
        }

        private readonly struct Packet
        {
            public Packet(uint deliverAt, int connectionId, byte[] payload)
            {
                DeliverAt = deliverAt;
                ConnectionId = connectionId;
                Payload = payload;
            }

            public uint DeliverAt { get; }

            public int ConnectionId { get; }

            public byte[] Payload { get; }
        }
    }
}
