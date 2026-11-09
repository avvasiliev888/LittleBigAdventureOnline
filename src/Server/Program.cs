using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Shared;
using Simulation.Core;

namespace Server
{
    /// <summary>
    /// Точка входа серверного процесса. M4: консольный сервер с фиксированным тиком, валидацией команд
    /// и рассылкой полных снапшотов. M5: UDP-режим через LiteNetLib (`--udp [порт]`).
    /// В демо-режиме клиент живёт в том же процессе (in-memory транспорт),
    /// поэтому видно полный путь «команда → валидация → тик → снапшот».
    /// </summary>
    public static class Program
    {
        /// <summary>Запускает сервер: демо с in-process клиентом или UDP-сервер.</summary>
        /// <param name="args">
        /// `--udp [порт]` для реального UDP; `--delay &lt;мс&gt;` и `--loss &lt;процент&gt;` — имитация плохой сети (M7);
        /// иначе путь к каталогу конфигов (необязательно).
        /// </param>
        /// <returns>Код возврата процесса.</returns>
        public static int Main(string[] args)
        {
            var positional = new List<string>();
            int? udpPort = null;
            var delayMs = 0;
            var lossPercent = 0;

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--udp")
                {
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var port))
                    {
                        udpPort = port;
                        i++;
                    }
                    else
                    {
                        udpPort = 9050;
                    }

                    continue;
                }

                if (args[i] == "--delay" && i + 1 < args.Length && int.TryParse(args[i + 1], out var delay))
                {
                    delayMs = Math.Max(0, delay);
                    i++;
                    continue;
                }

                if (args[i] == "--loss" && i + 1 < args.Length && int.TryParse(args[i + 1], out var loss))
                {
                    lossPercent = Math.Clamp(loss, 0, 100);
                    i++;
                    continue;
                }

                positional.Add(args[i]);
            }

            var configDirectory = ResolveConfigDirectory(positional.ToArray());
            SimulationConfig config;

            try
            {
                config = ConfigLoader.LoadFromDirectory(configDirectory);
            }
            catch (ConfigValidationException exception)
            {
                Console.Error.WriteLine("Конфигурация не загружена: " + exception.Message);
                return 1;
            }

            Console.WriteLine(
                "config: " + configDirectory + " (акторов: " + config.Actors.Count + ", способностей: " + config.Abilities.Count + ")"
                + ", hash: " + ConfigLoader.ComputeHash(configDirectory));

            if (udpPort.HasValue)
            {
                return RunUdpServer(config, udpPort.Value, delayMs, lossPercent);
            }

            using var transport = new InMemoryServerTransport();
            using var server = new GameServer(config, transport, new ServerOptions { LogTicks = true }, Console.WriteLine);
            var serializer = new BinarySnapshotSerializer();
            SimulationSnapshot? lastSnapshot = null;
            var loop = new ServerLoop(server, new SystemClock(), 20);

            server.Start();

            var clientId = transport.ConnectClient();
            transport.SendFromClient(clientId, MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()));

            const int ticks = 40;
            for (var i = 0; i < ticks; i++)
            {
                if (i % 4 == 0)
                {
                    transport.SendFromClient(
                        clientId,
                        MessageFraming.Wrap(MessageType.Command, CommandCodec.Encode(new MoveCommand(new Vector2(1f, 0f), (uint)i, 0))));
                }

                if (i % 10 == 5)
                {
                    // Цель, которой нет в мире, — демонстрация отказа валидатора с причиной в логе.
                    transport.SendFromClient(
                        clientId,
                        MessageFraming.Wrap(MessageType.Command, CommandCodec.Encode(new AbilityCommand(999, (uint)i, 0))));
                }

                loop.RunOnce();

                while (transport.TryReceive(clientId, out var packet) && packet != null)
                {
                    var framing = MessageFraming.TryUnwrap(packet, out var frame);
                    if (framing != FramingError.None)
                    {
                        continue;
                    }

                    SimulationSnapshot? shown = null;
                    if (frame.Type == MessageType.Snapshot)
                    {
                        lastSnapshot = serializer.Deserialize(frame.Payload);
                        shown = lastSnapshot;
                    }
                    else if (frame.Type == MessageType.Delta && lastSnapshot != null)
                    {
                        // M8: дельта от последнего полного/восстановленного снапшота.
                        lastSnapshot = serializer.ApplyDelta(lastSnapshot, frame.Payload);
                        shown = lastSnapshot;
                    }

                    if (shown != null)
                    {
                        var first = shown.Entities.Count > 0 ? shown.Entities[0] : default;
                        Console.WriteLine(
                            "[client] " + frame.Type + " tick=" + shown.Tick + " entities=" + shown.Entities.Count
                            + " pos=(" + first.Position.X.ToString("0.###") + ", " + first.Position.Z.ToString("0.###") + ")"
                            + " hp=" + first.Hp);
                    }
                }
            }

            Console.WriteLine("demo завершено: тиков=" + loop.TicksExecuted + ", лог-записей=" + server.LogEntries.Count
                + ", отклонённых=" + CountRejected(server));

            return 0;
        }

        private static int RunUdpServer(SimulationConfig config, int port, int delayMs, int lossPercent)
        {
            using var socket = new LiteNetLibServerTransport(port);
            IServerTransport transport = socket;
            GameServer? started = null;

            if (delayMs > 0 || lossPercent > 0)
            {
                // Задержка в тиках: 50 мс на тик при 20 Гц. Задерживаем и снапшоты, и входящие команды,
                // чтобы оператор чувствовал RTT, а предсказание клиента было видно в деле (M7).
                var delayTicks = (int)Math.Round(delayMs / 50.0);

                if (delayMs > 0 && delayTicks == 0)
                {
                    delayTicks = 1;
                }

                transport = new LossyServerTransport(
                    socket,
                    lossPercent,
                    delayTicks,
                    seed: 2026,
                    currentTick: () => started?.Simulation.State.Tick ?? 0u,
                    inboundDelayTicks: delayTicks);
                Console.WriteLine("сеть: задержка " + delayMs + " мс (" + delayTicks + " тик(а)), потери "
                    + lossPercent + "% ненадёжных пакетов");
            }

            using var server = new GameServer(config, transport, new ServerOptions(), Console.WriteLine);
            started = server;
            var loop = new ServerLoop(server, new SystemClock(), 20);

            server.Start();
            Console.WriteLine("UDP-сервер слушает порт " + socket.LocalPort + " (protocol v" + Protocol.Version
                + ", тик " + loop.TickRate + " Гц). Ctrl+C — остановка.");

            var running = true;
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                running = false;
            };

            while (running)
            {
                loop.RunOnce();
            }

            Console.WriteLine("остановлено: тиков=" + loop.TicksExecuted + ", лог-записей=" + server.LogEntries.Count);
            return 0;
        }

        private static int CountRejected(GameServer server)
        {
            var count = 0;
            foreach (var entry in server.LogEntries)
            {
                if (entry.Contains("rejected", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static string ResolveConfigDirectory(string[] args)
        {
            foreach (var arg in args)
            {
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    return arg;
                }
            }

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "config");
                if (File.Exists(Path.Combine(candidate, ConfigLoader.ActorsFileName)))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "config");
        }
    }
}
