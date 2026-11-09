using System;
using System.Collections.Generic;
using System.Numerics;
using Server;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Интеграционный тест M4: сервер + фейковый клиент в одном процессе.
    /// Путь целиком: Hello → HelloAck → команда → валидация → тик → снапшот → состояние сходится.
    /// </summary>
    public sealed class ServerIntegrationTests
    {
        [Fact]
        public void Hello_Move_ProducesSnapshotMatchingServerState()
        {
            using var harness = new Harness();
            var clientId = harness.Connect();

            harness.Send(clientId, MessageType.Hello, Array.Empty<byte>());
            harness.Server.Poll();

            var ack = harness.Receive(clientId, MessageType.HelloAck);
            var entityId = ReadUInt32(ack.Payload, 0);

            Assert.True(harness.Server.TryGetSessionEntity(clientId, out var sessionEntity));
            Assert.Equal(entityId, sessionEntity);

            harness.Server.Tick();
            var first = harness.ReceiveSnapshot(clientId);
            Assert.Equal(1u, first.Tick);
            Assert.Single(first.Entities);

            var spawnX = first.Entities[0].Position.X;

            harness.Send(clientId, MessageType.Command, CommandCodec.Encode(new MoveCommand(new Vector2(1f, 0f), 1, 0)));
            harness.Server.Poll();
            harness.Server.Tick();

            var second = harness.ReceiveSnapshot(clientId);
            Assert.Equal(2u, second.Tick);
            Assert.Equal(spawnX + 0.2f, second.Entities[0].Position.X, 3);
            Assert.Equal(harness.Server.Simulation.State.Entities[0].Position.X, second.Entities[0].Position.X);
        }

        [Fact]
        public void RejectedCommand_DoesNotChangeState_AndIsLoggedWithReason()
        {
            using var harness = new Harness();
            var clientId = harness.Connect();

            harness.Send(clientId, MessageType.Hello, Array.Empty<byte>());
            harness.Server.Poll();
            var ack = harness.Receive(clientId, MessageType.HelloAck);
            var entityId = ReadUInt32(ack.Payload, 0);

            harness.Send(clientId, MessageType.Command, CommandCodec.Encode(new AbilityCommand(999, 1, 0)));
            harness.Server.Poll();
            harness.Server.Tick();

            var snapshot = harness.ReceiveSnapshot(clientId);
            Assert.Equal(100, snapshot.Entities[0].Hp);
            Assert.Contains(harness.Server.LogEntries, entry => entry.Contains("UnknownTarget", StringComparison.Ordinal));

            Assert.True(harness.Server.Simulation.State.TryGetEntity(entityId, out var entity));
            Assert.Equal(100, entity!.Hp);
        }

        [Fact]
        public void RateLimit_RejectsExcessCommands_ButKeepsAccepted()
        {
            using var harness = new Harness(new ServerOptions { MaxCommandsPerTick = 5 });
            var clientId = harness.Connect();

            harness.Send(clientId, MessageType.Hello, Array.Empty<byte>());
            harness.Server.Poll();
            harness.Receive(clientId, MessageType.HelloAck);

            for (var i = 0; i < 6; i++)
            {
                harness.Send(clientId, MessageType.Command, CommandCodec.Encode(new MoveCommand(new Vector2(1f, 0f), (uint)i, 0)));
            }

            harness.Server.Poll();
            harness.Server.Tick();

            Assert.Contains(harness.Server.LogEntries, entry => entry.Contains("RateLimit", StringComparison.Ordinal));

            // Пять команд приняты — сущность сдвинулась на 5 * 0.2 = 1.0.
            var entity = harness.Server.Simulation.State.Entities[0];
            Assert.Equal(2f + 1f, entity.Position.X, 3);
        }

        [Fact]
        public void Disconnect_RemovesSessionEntity()
        {
            using var harness = new Harness();
            var clientId = harness.Connect();

            harness.Send(clientId, MessageType.Hello, Array.Empty<byte>());
            harness.Server.Poll();
            var ack = harness.Receive(clientId, MessageType.HelloAck);
            var entityId = ReadUInt32(ack.Payload, 0);

            harness.Transport.DisconnectClient(clientId, "bye");

            Assert.False(harness.Server.Simulation.State.TryGetEntity(entityId, out _));
            Assert.Contains(harness.Server.LogEntries, entry => entry.Contains("disconnected", StringComparison.Ordinal));
        }

        [Fact]
        public void GarbagePacket_IsLoggedAndIgnored()
        {
            using var harness = new Harness();
            var clientId = harness.Connect();

            harness.Transport.SendFromClient(clientId, new byte[] { 1, 2, 3 });
            harness.Server.Poll();

            Assert.Contains(harness.Server.LogEntries, entry => entry.Contains("отклонён", StringComparison.Ordinal));
        }

        [Fact]
        public void FullSnapshotRequest_ReturnsSnapshotWithoutWaitingForTick()
        {
            using var harness = new Harness();
            var clientId = harness.Connect();

            harness.Send(clientId, MessageType.Hello, Array.Empty<byte>());
            harness.Server.Poll();
            harness.Receive(clientId, MessageType.HelloAck);

            harness.Send(clientId, MessageType.FullSnapshotRequest, Array.Empty<byte>());
            harness.Server.Poll();

            var snapshot = harness.ReceiveSnapshot(clientId);
            Assert.Equal(harness.Server.Simulation.State.Tick, snapshot.Tick);
            Assert.Equal(harness.Server.Simulation.State.Entities.Count, snapshot.Entities.Count);
        }

        [Fact]
        public void SecondHello_IsIgnored()
        {
            using var harness = new Harness();
            var clientId = harness.Connect();

            harness.Send(clientId, MessageType.Hello, Array.Empty<byte>());
            harness.Server.Poll();
            harness.Receive(clientId, MessageType.HelloAck);

            harness.Send(clientId, MessageType.Hello, Array.Empty<byte>());
            harness.Server.Poll();

            Assert.Single(harness.Server.Simulation.State.Entities);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }

        private sealed class Harness : IDisposable
        {
            private readonly BinarySnapshotSerializer _serializer = new BinarySnapshotSerializer();

            public Harness(ServerOptions? options = null)
            {
                Transport = new InMemoryServerTransport();
                Server = new GameServer(TestWorld.Config(), Transport, options ?? new ServerOptions());
                Server.Start();
            }

            public InMemoryServerTransport Transport { get; }

            public GameServer Server { get; }

            public int Connect() => Transport.ConnectClient();

            public void Send(int connectionId, MessageType type, byte[] payload)
            {
                Transport.SendFromClient(connectionId, MessageFraming.Wrap(type, payload));
            }

            public MessageFrame Receive(int connectionId, MessageType type)
            {
                var visited = new List<MessageType>();

                while (Transport.TryReceive(connectionId, out var packet) && packet != null)
                {
                    var error = MessageFraming.TryUnwrap(packet, out var frame);
                    Assert.Equal(FramingError.None, error);

                    if (frame.Type == type)
                    {
                        return frame;
                    }

                    visited.Add(frame.Type);
                }

                throw new Xunit.Sdk.XunitException(
                    "Пакет " + type + " не найден; получено: " + string.Join(", ", visited));
            }

            public SimulationSnapshot ReceiveSnapshot(int connectionId)
            {
                return _serializer.Deserialize(Receive(connectionId, MessageType.Snapshot).Payload);
            }

            public void Dispose() => Server.Dispose();
        }
    }
}
