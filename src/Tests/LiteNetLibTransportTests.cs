using System;
using System.Collections.Generic;
using System.Threading;
using Client.Network;
using Server;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// M5: реальный UDP через LiteNetLib на loopback — handshake, версия протокола, снапшоты.
    /// Тесты сетевые, поэтому с щедрыми таймаутами и без требований к точной частоте.
    /// </summary>
    public sealed class LiteNetLibTransportTests
    {
        [Fact]
        public void ServerAndClient_ExchangeHelloAndSnapshots()
        {
            using var serverTransport = new LiteNetLibServerTransport(0);
            using var server = new GameServer(TestWorld.Config(), serverTransport, new ServerOptions());
            server.Start();

            var port = serverTransport.LocalPort;
            Assert.True(port > 0);

            var harness = new ClientHarness();
            using var client = new LiteNetLibTransport("127.0.0.1", port);
            client.PacketReceived += harness.OnPacket;

            client.Start();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            var nextHelloAt = DateTime.UtcNow.AddMilliseconds(300);

            while (DateTime.UtcNow < deadline && harness.Snapshots.Count == 0)
            {
                server.Poll();
                client.Poll();

                if (client.IsConnected && !harness.HelloAckReceived && DateTime.UtcNow >= nextHelloAt)
                {
                    client.Send(MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()), DeliveryMode.ReliableOrdered);
                    nextHelloAt = DateTime.UtcNow.AddMilliseconds(300);
                }

                server.Tick();
                Thread.Sleep(20);
            }

            Assert.True(harness.HelloAckReceived, "клиент не получил HelloAck");
            Assert.True(harness.EntityId > 0, "HelloAck без идентификатора сущности");
            Assert.NotEmpty(harness.Snapshots);
            Assert.Single(server.Simulation.State.Entities);
            Assert.Equal(harness.EntityId, server.Simulation.State.Entities[0].Id);
        }

        [Fact]
        public void VersionMismatch_ConnectionIsRejected()
        {
            using var serverTransport = new LiteNetLibServerTransport(0, protocolVersion: 2);
            using var server = new GameServer(TestWorld.Config(), serverTransport, new ServerOptions());
            server.Start();

            var disconnected = new ManualResetEventSlim();
            var reason = string.Empty;

            using var client = new LiteNetLibTransport("127.0.0.1", serverTransport.LocalPort, protocolVersion: 1);
            client.Disconnected += text =>
            {
                reason = text;
                disconnected.Set();
            };

            client.Start();

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && !disconnected.IsSet)
            {
                server.Poll();
                client.Poll();
                Thread.Sleep(20);
            }

            Assert.True(disconnected.IsSet, "клиент с чужой версией протокола не получил отказ");
            Assert.False(client.IsConnected);
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.Empty(server.Simulation.State.Entities);
        }

        private sealed class ClientHarness
        {
            private readonly BinarySnapshotSerializer _serializer = new BinarySnapshotSerializer();

            public bool HelloAckReceived { get; private set; }

            public uint EntityId { get; private set; }

            public List<SimulationSnapshot> Snapshots { get; } = new List<SimulationSnapshot>();

            public void OnPacket(byte[] packet)
            {
                if (MessageFraming.TryUnwrap(packet, out var frame) != FramingError.None)
                {
                    return;
                }

                if (frame.Type == MessageType.HelloAck)
                {
                    EntityId = ReadUInt32(frame.Payload, 0);
                    HelloAckReceived = true;
                }
                else if (frame.Type == MessageType.Snapshot)
                {
                    Snapshots.Add(_serializer.Deserialize(frame.Payload));
                }
            }

            private static uint ReadUInt32(byte[] buffer, int offset)
            {
                return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
            }
        }
    }
}
