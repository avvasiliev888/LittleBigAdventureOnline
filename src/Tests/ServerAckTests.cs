using System;
using System.Numerics;
using Server;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// M7 (ADR-009): сервер подтверждает последнюю обработанную команду и сообщает скорость актора,
    /// без которой клиент не может предсказывать движение.
    /// </summary>
    public sealed class ServerAckTests
    {
        [Fact]
        public void HelloAck_ContainsMoveSpeed()
        {
            using var transport = new InMemoryServerTransport();
            using var server = new GameServer(TestWorld.Config(), transport, new ServerOptions());
            server.Start();

            var clientId = transport.ConnectClient();
            transport.SendFromClient(clientId, MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()));
            server.Poll();

            var frame = Receive(transport, clientId, MessageType.HelloAck);

            Assert.Equal(16, frame.Payload.Length);
            Assert.Equal(4f, ReadSingle(frame.Payload, 12), 3);
            Assert.Equal(TestWorld.PlayerActorId, ReadUInt32(frame.Payload, 8));
        }

        [Fact]
        public void Tick_SendsAckWithLastProcessedSequence()
        {
            using var transport = new InMemoryServerTransport();
            using var server = new GameServer(TestWorld.Config(), transport, new ServerOptions());
            server.Start();

            var clientId = transport.ConnectClient();
            transport.SendFromClient(clientId, MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()));
            server.Poll();
            _ = Receive(transport, clientId, MessageType.HelloAck);
            Assert.True(server.TryGetSessionEntity(clientId, out var entityId));

            transport.SendFromClient(
                clientId,
                MessageFraming.Wrap(MessageType.Command, CommandCodec.Encode(new MoveCommand(new Vector2(1f, 0f), 1, 7))));
            server.Poll();
            server.Tick();

            var ack = Decode(Receive(transport, clientId, MessageType.ServerAck));
            Assert.Equal(entityId, ack.EntityId);
            Assert.Equal(7u, ack.AcknowledgedSequence);
            Assert.Equal(1u, ack.Tick);
        }

        [Fact]
        public void RejectedCommand_IsAcknowledged_ButDoesNotChangeState()
        {
            using var transport = new InMemoryServerTransport();
            using var server = new GameServer(TestWorld.Config(), transport, new ServerOptions());
            server.Start();

            var clientId = transport.ConnectClient();
            transport.SendFromClient(clientId, MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()));
            server.Poll();
            _ = Receive(transport, clientId, MessageType.HelloAck);
            Assert.True(server.TryGetSessionEntity(clientId, out var entityId));

            // Цель 999 не существует — команда отклоняется валидатором, но подтверждается клиенту,
            // иначе клиент повторял бы её при реконсиляции бесконечно.
            transport.SendFromClient(
                clientId,
                MessageFraming.Wrap(MessageType.Command, CommandCodec.Encode(new AbilityCommand(999, 1, 4))));
            server.Poll();
            server.Tick();

            var ack = Decode(Receive(transport, clientId, MessageType.ServerAck));
            Assert.Equal(4u, ack.AcknowledgedSequence);

            Assert.True(server.Simulation.State.TryGetEntity(entityId, out var entity));
            Assert.Equal(100, entity!.Hp);
            Assert.Contains(server.LogEntries, entry => entry.Contains("UnknownTarget", StringComparison.Ordinal));
        }

        private static MessageFrame Receive(InMemoryServerTransport transport, int clientId, MessageType type)
        {
            while (transport.TryReceive(clientId, out var packet) && packet != null)
            {
                var error = MessageFraming.TryUnwrap(packet, out var frame);
                Assert.Equal(FramingError.None, error);

                if (frame.Type == type)
                {
                    return frame;
                }
            }

            throw new Xunit.Sdk.XunitException("Пакет " + type + " не найден в очереди клиента.");
        }

        private static AckMessage Decode(MessageFrame frame)
        {
            Assert.True(AckCodec.TryDecode(frame.Payload, out var ack), "ServerAck не декодируется");
            return ack;
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }

        private static float ReadSingle(byte[] buffer, int offset)
        {
            return BitConverter.Int32BitsToSingle((int)ReadUInt32(buffer, offset));
        }
    }
}
