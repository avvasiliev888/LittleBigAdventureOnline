using System;
using System.Collections.Generic;
using Client.Network;
using Server;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// M5: детерминированная имитация плохой сети — 30% потерь снапшотов и задержка 100 мс (2 тика).
    /// Проверяется, что клиент не зависает и восстанавливается после серии потерь.
    /// </summary>
    public sealed class LossyTransportTests
    {
        [Fact]
        public void ThirtyPercentLoss_WithTwoTickDelay_ClientKeepsUpAndRecovers()
        {
            var inner = new InMemoryServerTransport();
            GameServer? server = null;
            var lossy = new LossyServerTransport(inner, lossPercent: 30, delayTicks: 2, seed: 2026, currentTick: () => server!.Simulation.State.Tick);
            var serializer = new BinarySnapshotSerializer();
            var receivedTicks = new List<uint>();
            SimulationSnapshot? lastSnapshot = null;

            try
            {
                server = new GameServer(TestWorld.Config(), lossy, new ServerOptions());
                server.Start();

                var clientId = inner.ConnectClient();
                inner.SendFromClient(clientId, MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()));

                for (var i = 0; i < 300; i++)
                {
                    server.Poll();
                    server.Tick();

                    while (inner.TryReceive(clientId, out var packet) && packet != null)
                    {
                        if (MessageFraming.TryUnwrap(packet, out var frame) == FramingError.None && frame.Type == MessageType.Snapshot)
                        {
                            lastSnapshot = serializer.Deserialize(frame.Payload);
                            receivedTicks.Add(lastSnapshot.Tick);
                        }
                    }
                }

                Assert.True(receivedTicks.Count > 0, "клиент не получил ни одного снапшота");
                Assert.True(lossy.DroppedPackets > 0, "имитатор не потерял ни одного пакета");
                Assert.True(lossy.DelayedPackets > 0, "имитатор не задержал ни одного пакета");

                // Полный снапшот: потеря — пропуск обновления, а не потеря состояния.
                // При 30% i.i.d. потерь серия из 7+ пропусков практически невероятна (0.3^7 ≈ 2e-4);
                // порог 8 оставляет запас, не превращая тест в проверку генератора случайных чисел.
                var maxGap = 0u;
                for (var i = 1; i < receivedTicks.Count; i++)
                {
                    Assert.True(receivedTicks[i] > receivedTicks[i - 1], "тики снапшотов должны возрастать");
                    maxGap = Math.Max(maxGap, receivedTicks[i] - receivedTicks[i - 1]);
                }

                Assert.InRange(maxGap, 1u, 8u);
                Assert.True(receivedTicks[^1] >= 295, "клиент отстал от сервера: последний снапшот " + receivedTicks[^1]);

                // Сходимость: последний снапшот описывает ту же сущность с теми же статами,
                // что и сервер; позиция отличается не больше чем на скорость тика × отставание.
                Assert.NotNull(lastSnapshot);
                var serverEntity = server!.Simulation.State.Entities[0];
                Assert.Equal(serverEntity.Id, lastSnapshot!.Entities[0].Id);
                Assert.Equal(serverEntity.ActorId, lastSnapshot.Entities[0].ActorId);
                Assert.Equal(serverEntity.MaxHp, lastSnapshot.Entities[0].MaxHp);

                var positionDrift = (lastSnapshot.Entities[0].Position - serverEntity.Position).Length();
                var lagTicks = server.Simulation.State.Tick - lastSnapshot.Tick;
                Assert.True(
                    positionDrift <= (0.2f * lagTicks) + 0.001f,
                    "позиция разошлась: drift=" + positionDrift + ", lagTicks=" + lagTicks);
            }
            finally
            {
                server?.Dispose();
            }
        }

        [Fact]
        public void BurstOfThreeLostSnapshots_RecoversWithinThreeTicks()
        {
            var inner = new InMemoryServerTransport();
            GameServer? server = null;
            var dropping = new DropTicksTransport(inner, () => server!.Simulation.State.Tick, 10u, 11u, 12u);
            var serializer = new BinarySnapshotSerializer();
            var receivedTicks = new List<uint>();

            try
            {
                server = new GameServer(TestWorld.Config(), dropping, new ServerOptions());
                server.Start();

                var clientId = inner.ConnectClient();
                inner.SendFromClient(clientId, MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()));

                for (var i = 0; i < 20; i++)
                {
                    server.Poll();
                    server.Tick();

                    while (inner.TryReceive(clientId, out var packet) && packet != null)
                    {
                        if (MessageFraming.TryUnwrap(packet, out var frame) == FramingError.None && frame.Type == MessageType.Snapshot)
                        {
                            receivedTicks.Add(serializer.Deserialize(frame.Payload).Tick);
                        }
                    }
                }

                Assert.Equal(3, dropping.Dropped);

                // Серия потерь на тиках 10–12: следующий снапшот (тик 13) должен прийти сразу,
                // то есть восстановление — 1 тик, и уж точно ≤ 3.
                Assert.Contains(9u, receivedTicks);
                Assert.Contains(13u, receivedTicks);
                Assert.True(receivedTicks[^1] == 20u, "после серии потерь поток снапшотов оборвался");

                var indexOfNine = receivedTicks.IndexOf(9u);
                var indexOfThirteen = receivedTicks.IndexOf(13u);
                Assert.True(indexOfThirteen > indexOfNine);
            }
            finally
            {
                server?.Dispose();
            }
        }

        [Fact]
        public void ReliablePackets_AreNotDropped_EvenAt100PercentLoss()
        {
            var inner = new InMemoryServerTransport();
            GameServer? server = null;
            var lossy = new LossyServerTransport(inner, lossPercent: 100, delayTicks: 5, seed: 1, currentTick: () => server!.Simulation.State.Tick);

            try
            {
                server = new GameServer(TestWorld.Config(), lossy, new ServerOptions());
                server.Start();

                var clientId = inner.ConnectClient();
                inner.SendFromClient(clientId, MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()));

                server.Poll();

                Assert.True(inner.TryReceive(clientId, out var packet) && packet != null);
                Assert.Equal(FramingError.None, MessageFraming.TryUnwrap(packet!, out var frame));
                Assert.Equal(MessageType.HelloAck, frame.Type);

                server.Tick();

                var received = new System.Collections.Generic.List<MessageType>();

                while (inner.TryReceive(clientId, out var queued) && queued != null)
                {
                    Assert.Equal(FramingError.None, MessageFraming.TryUnwrap(queued, out var queuedFrame));
                    received.Add(queuedFrame.Type);
                }

                Assert.DoesNotContain(MessageType.Snapshot, received);
                Assert.Contains(MessageType.ServerAck, received);
                Assert.Equal(1, lossy.DroppedPackets);
                Assert.Equal(0, lossy.DelayedPackets);
            }
            finally
            {
                server?.Dispose();
            }
        }

        [Fact]
        public void Constructor_ValidatesArguments()
        {
            var inner = new InMemoryServerTransport();
            using (inner)
            {
                Assert.Throws<ArgumentNullException>(() => new LossyServerTransport(null!, 30, 2, 1, () => 0u));
                Assert.Throws<ArgumentNullException>(() => new LossyServerTransport(inner, 30, 2, 1, null!));
                Assert.Throws<ArgumentOutOfRangeException>(() => new LossyServerTransport(inner, -1, 2, 1, () => 0u));
                Assert.Throws<ArgumentOutOfRangeException>(() => new LossyServerTransport(inner, 101, 2, 1, () => 0u));
                Assert.Throws<ArgumentOutOfRangeException>(() => new LossyServerTransport(inner, 30, -1, 1, () => 0u));
            }
        }

        /// <summary>Теряет ненадёжные пакеты ровно на заданных тиках (для проверки восстановления).</summary>
        private sealed class DropTicksTransport : IServerTransport
        {
            private readonly IServerTransport _inner;
            private readonly Func<uint> _currentTick;
            private readonly HashSet<uint> _droppedTicks;

            public DropTicksTransport(IServerTransport inner, Func<uint> currentTick, params uint[] droppedTicks)
            {
                _inner = inner;
                _currentTick = currentTick;
                _droppedTicks = new HashSet<uint>(droppedTicks);

                _inner.ClientConnected += connectionId => ClientConnected?.Invoke(connectionId);
                _inner.ClientDisconnected += (connectionId, reason) => ClientDisconnected?.Invoke(connectionId, reason);
                _inner.PacketReceived += (connectionId, packet) => PacketReceived?.Invoke(connectionId, packet);
            }

            public int Dropped { get; private set; }

            public event Action<int>? ClientConnected;

            public event Action<int, string>? ClientDisconnected;

            public event Action<int, byte[]>? PacketReceived;

            public void Start() => _inner.Start();

            public void Poll() => _inner.Poll();

            public void Send(int connectionId, byte[] packet, DeliveryMode mode)
            {
                if (mode == DeliveryMode.Unreliable && _droppedTicks.Contains(_currentTick()))
                {
                    Dropped++;
                    return;
                }

                _inner.Send(connectionId, packet, mode);
            }

            public void Dispose() => _inner.Dispose();
        }
    }
}
