using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Client.Network;
using Shared;
using Simulation.Core;

namespace TestClient
{
    /// <summary>
    /// Консольный тест-клиент M5: подключается по UDP, проходит Hello, шлёт команды движения
    /// и печатает получаемые снапшоты. Нужен для Human-Gate: два терминала — сервер и клиент.
    /// </summary>
    public static class Program
    {
        /// <summary>Запускает тест-клиент.</summary>
        /// <param name="args">[host] [port] [seconds]</param>
        /// <returns>0 — успех, 2 — соединение отклонено или потеряно.</returns>
        public static int Main(string[] args)
        {
            var host = args.Length > 0 ? args[0] : "127.0.0.1";
            var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 9050;
            var seconds = args.Length > 2 && int.TryParse(args[2], out var parsedSeconds) ? parsedSeconds : 15;

            var serializer = new BinarySnapshotSerializer();
            var disconnectedReason = string.Empty;
            var helloAcked = false;
            var snapshots = 0;
            var commandsSent = 0;
            var fullSnapshotRequests = 0;
            var lastSnapshotAt = 0L;
            uint entityId = 0;
            var lastPosition = Vector3.Zero;

            using var transport = new LiteNetLibTransport(host, port);

            transport.Disconnected += reason =>
            {
                disconnectedReason = reason;
                Console.WriteLine("[client] disconnected: " + reason);
            };

            transport.PacketReceived += packet =>
            {
                if (MessageFraming.TryUnwrap(packet, out var frame) != FramingError.None)
                {
                    return;
                }

                switch (frame.Type)
                {
                    case MessageType.HelloAck:
                        entityId = ReadUInt32(frame.Payload, 0);
                        helloAcked = true;
                        Console.WriteLine("[client] HelloAck: сущность " + entityId + ", тик " + ReadUInt32(frame.Payload, 4)
                            + ", актор " + ReadUInt32(frame.Payload, 8));
                        break;

                    case MessageType.Snapshot:
                        var snapshot = serializer.Deserialize(frame.Payload);
                        snapshots++;
                        lastSnapshotAt = Environment.TickCount64;
                        if (snapshot.Entities.Count > 0)
                        {
                            lastPosition = snapshot.Entities[0].Position;
                        }

                        if (snapshots == 1 || snapshots % 20 == 0)
                        {
                            Console.WriteLine("[client] snapshot #" + snapshots + " tick=" + snapshot.Tick
                                + " entities=" + snapshot.Entities.Count + " pos=("
                                + lastPosition.X.ToString("0.##") + ", " + lastPosition.Z.ToString("0.##") + ")"
                                + " RTT=" + transport.RoundTripTimeMs + " мс");
                        }

                        break;
                }
            };

            transport.Start();
            Console.WriteLine("[client] connecting to " + host + ":" + port + " (protocol v" + Protocol.Version + ")");

            var stopwatch = Stopwatch.StartNew();
            var nextHelloAt = 0L;

            while (stopwatch.Elapsed.TotalSeconds < seconds)
            {
                transport.Poll();

                if (!transport.IsConnected)
                {
                    if (disconnectedReason.Length > 0)
                    {
                        return 2;
                    }

                    Thread.Sleep(20);
                    continue;
                }

                // Hello повторяем, пока нет HelloAck: UDP-пакет мог потеряться.
                if (!helloAcked && stopwatch.ElapsedMilliseconds >= nextHelloAt)
                {
                    transport.Send(MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()), DeliveryMode.ReliableOrdered);
                    nextHelloAt = stopwatch.ElapsedMilliseconds + 500;
                }

                // Снапшоты идут Unreliable; если поток оборвался, запрашиваем полный снапшот надёжно.
                if (helloAcked && Environment.TickCount64 - lastSnapshotAt > 1000)
                {
                    transport.Send(MessageFraming.Wrap(MessageType.FullSnapshotRequest, Array.Empty<byte>()), DeliveryMode.ReliableOrdered);
                    fullSnapshotRequests++;
                    lastSnapshotAt = Environment.TickCount64;
                    Console.WriteLine("[client] нет снапшотов > 1000 мс — запрошен полный снапшот (ReliableOrdered)");
                }

                if (stopwatch.ElapsedMilliseconds / 50 % 4 == 0)
                {
                    var direction = new Vector2((float)Math.Cos(stopwatch.Elapsed.TotalSeconds), (float)Math.Sin(stopwatch.Elapsed.TotalSeconds));
                    transport.Send(
                        MessageFraming.Wrap(MessageType.Command, CommandCodec.Encode(new MoveCommand(direction, (uint)stopwatch.ElapsedMilliseconds, (uint)commandsSent))),
                        DeliveryMode.ReliableOrdered);
                    commandsSent++;
                }

                Thread.Sleep(50);
            }

            Console.WriteLine(
                "[client] готово: снапшотов=" + snapshots + ", команд отправлено=" + commandsSent
                + ", запросов полного снапшота=" + fullSnapshotRequests
                + ", последняя позиция=(" + lastPosition.X.ToString("0.##") + ", " + lastPosition.Z.ToString("0.##") + ")"
                + ", RTT=" + transport.RoundTripTimeMs + " мс");

            return snapshots > 0 ? 0 : 1;
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }
    }
}
