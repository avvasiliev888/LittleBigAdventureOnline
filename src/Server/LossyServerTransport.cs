using System;
using System.Collections.Generic;
using Client.Network;

namespace Server
{
    /// <summary>
    /// Имитатор плохой сети поверх любого <see cref="IServerTransport"/> (M5, M7).
    /// Ненадёжные пакеты (снапшоты) теряются с заданной вероятностью и доставляются с задержкой в тиках;
    /// надёжные (HelloAck, ServerAck, ответ на запрос снапшота) проходят без потерь — как и в реальном
    /// LiteNetLib, где ReliableOrdered переотправляется. Входящие команды (M7) можно задержать на столько же
    /// тиков, чтобы имитировать RTT. Генератор случайных чисел детерминирован по seed.
    /// </summary>
    public sealed class LossyServerTransport : IServerTransport
    {
        private readonly IServerTransport _inner;
        private readonly Random _random;
        private readonly int _lossPercent;
        private readonly int _delayTicks;
        private readonly int _inboundDelayTicks;
        private readonly Func<uint> _currentTick;
        private readonly List<DelayedPacket> _delayed = new List<DelayedPacket>();
        private readonly List<DelayedInbound> _delayedInbound = new List<DelayedInbound>();

        /// <summary>Создаёт имитатор.</summary>
        /// <param name="inner">Реальный транспорт, поверх которого работаем.</param>
        /// <param name="lossPercent">Вероятность потери ненадёжного пакета, 0..100.</param>
        /// <param name="delayTicks">Задержка доставки ненадёжных пакетов в тиках (0 — без задержки).</param>
        /// <param name="seed">Seed генератора потерь (детерминизм тестов).</param>
        /// <param name="currentTick">Текущий тик сервера для расчёта задержки.</param>
        /// <param name="inboundDelayTicks">Задержка входящих команд в тиках (0 — без задержки).</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="inner"/> или <paramref name="currentTick"/> равны <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если проценты или задержка вне допустимого диапазона.</exception>
        public LossyServerTransport(
            IServerTransport inner,
            int lossPercent,
            int delayTicks,
            int seed,
            Func<uint> currentTick,
            int inboundDelayTicks = 0)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));

            if (lossPercent < 0 || lossPercent > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(lossPercent), lossPercent, "Проценты потерь: 0..100.");
            }

            if (delayTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delayTicks), delayTicks, "Задержка не может быть отрицательной.");
            }

            if (inboundDelayTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inboundDelayTicks), inboundDelayTicks, "Задержка не может быть отрицательной.");
            }

            _lossPercent = lossPercent;
            _delayTicks = delayTicks;
            _inboundDelayTicks = inboundDelayTicks;
            _random = new Random(seed);

            _inner.ClientConnected += connectionId => ClientConnected?.Invoke(connectionId);
            _inner.ClientDisconnected += (connectionId, reason) => ClientDisconnected?.Invoke(connectionId, reason);
            _inner.PacketReceived += OnInnerPacketReceived;
        }

        /// <summary>Сколько ненадёжных пакетов потеряно (диагностика демо/тестов).</summary>
        public int DroppedPackets { get; private set; }

        /// <summary>Сколько ненадёжных пакетов отложено (диагностика демо/тестов).</summary>
        public int DelayedPackets { get; private set; }

        /// <summary>Сколько входящих пакетов отложено (диагностика демо/тестов).</summary>
        public int DelayedInboundPackets { get; private set; }

        /// <inheritdoc />
        public event Action<int>? ClientConnected;

        /// <inheritdoc />
        public event Action<int, string>? ClientDisconnected;

        /// <inheritdoc />
        public event Action<int, byte[]>? PacketReceived;

        /// <inheritdoc />
        public void Start()
        {
            _inner.Start();
        }

        /// <summary>Сначала доставляет созревшие отложенные пакеты, затем опрашивает транспорт.</summary>
        public void Poll()
        {
            FlushDuePackets();
            FlushDueInbound();
            _inner.Poll();
        }

        /// <inheritdoc />
        public void Send(int connectionId, byte[] packet, DeliveryMode mode)
        {
            if (mode == DeliveryMode.ReliableOrdered)
            {
                _inner.Send(connectionId, packet, mode);
                return;
            }

            if (_random.Next(100) < _lossPercent)
            {
                DroppedPackets++;
                return;
            }

            if (_delayTicks == 0)
            {
                _inner.Send(connectionId, packet, mode);
                return;
            }

            DelayedPackets++;
            _delayed.Add(new DelayedPacket(_currentTick() + (uint)_delayTicks, connectionId, packet));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _delayed.Clear();
            _delayedInbound.Clear();
            _inner.Dispose();
        }

        private void FlushDuePackets()
        {
            if (_delayed.Count == 0)
            {
                return;
            }

            var now = _currentTick();
            for (var i = _delayed.Count - 1; i >= 0; i--)
            {
                if (_delayed[i].DeliverAt > now)
                {
                    continue;
                }

                var packet = _delayed[i];
                _delayed.RemoveAt(i);
                _inner.Send(packet.ConnectionId, packet.Packet, DeliveryMode.Unreliable);
            }
        }

        private void OnInnerPacketReceived(int connectionId, byte[] packet)
        {
            if (_inboundDelayTicks == 0)
            {
                PacketReceived?.Invoke(connectionId, packet);
                return;
            }

            DelayedInboundPackets++;
            _delayedInbound.Add(new DelayedInbound(_currentTick() + (uint)_inboundDelayTicks, connectionId, packet));
        }

        private void FlushDueInbound()
        {
            if (_delayedInbound.Count == 0)
            {
                return;
            }

            var now = _currentTick();
            for (var i = _delayedInbound.Count - 1; i >= 0; i--)
            {
                if (_delayedInbound[i].DeliverAt > now)
                {
                    continue;
                }

                var packet = _delayedInbound[i];
                _delayedInbound.RemoveAt(i);
                PacketReceived?.Invoke(packet.ConnectionId, packet.Packet);
            }
        }

        private readonly struct DelayedInbound
        {
            public DelayedInbound(uint deliverAt, int connectionId, byte[] packet)
            {
                DeliverAt = deliverAt;
                ConnectionId = connectionId;
                Packet = packet;
            }

            public uint DeliverAt { get; }

            public int ConnectionId { get; }

            public byte[] Packet { get; }
        }

        private readonly struct DelayedPacket
        {
            public DelayedPacket(uint deliverAt, int connectionId, byte[] packet)
            {
                DeliverAt = deliverAt;
                ConnectionId = connectionId;
                Packet = packet;
            }

            public uint DeliverAt { get; }

            public int ConnectionId { get; }

            public byte[] Packet { get; }
        }
    }
}
