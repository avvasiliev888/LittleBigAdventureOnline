using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Client.Network;
using Shared;
using Simulation.Core;

namespace BotClient
{
    /// <summary>
    /// Headless bot client for the M11 load test: connects over UDP, sends Hello, moves in a
    /// per-bot sine pattern and periodically activates the ability. It intentionally uses the
    /// same wire protocol as the Unity client; it does not render or predict.
    /// </summary>
    public static class Program
    {
        /// <summary>Runs the bot swarm.</summary>
        /// <param name="args">
        /// Flags: <c>--host</c>, <c>--port</c>, <c>--bots</c>, <c>--seconds</c>,
        /// <c>--move-ms</c>, <c>--ability-ms</c>, <c>--target-actor</c>.
        /// </param>
        /// <returns>0 if every bot connected and received snapshots; 1 otherwise.</returns>
        public static int Main(string[] args)
        {
            var host = "127.0.0.1";
            var port = 9050;
            var bots = 20;
            var seconds = 60;
            var moveIntervalMs = 100;
            var abilityIntervalMs = 2200;
            var targetActorId = 2u;

            for (var i = 0; i < args.Length; i++)
            {
                if (i + 1 >= args.Length)
                {
                    break;
                }

                switch (args[i])
                {
                    case "--host":
                        host = args[++i];
                        break;
                    case "--port":
                        port = ParseInt(args[++i], port, 1, 65535);
                        break;
                    case "--bots":
                        bots = ParseInt(args[++i], bots, 1, 200);
                        break;
                    case "--seconds":
                        seconds = ParseInt(args[++i], seconds, 1, 3600);
                        break;
                    case "--move-ms":
                        moveIntervalMs = ParseInt(args[++i], moveIntervalMs, 10, 10000);
                        break;
                    case "--ability-ms":
                        abilityIntervalMs = ParseInt(args[++i], abilityIntervalMs, 100, 60000);
                        break;
                    case "--target-actor":
                        targetActorId = (uint)ParseInt(args[++i], (int)targetActorId, 1, int.MaxValue);
                        break;
                }
            }

            Console.WriteLine("[bots] connecting " + bots + " bots to " + host + ":" + port
                + " for " + seconds + "s (move " + moveIntervalMs + "ms, ability " + abilityIntervalMs + "ms)");

            var clients = new List<Bot>(bots);
            try
            {
                for (var i = 0; i < bots; i++)
                {
                    var bot = new Bot(i, host, port, moveIntervalMs, abilityIntervalMs, targetActorId);
                    bot.Start();
                    clients.Add(bot);
                }

                var stopwatch = Stopwatch.StartNew();
                var nextProgress = 10_000L;
                while (stopwatch.ElapsedMilliseconds < seconds * 1000L)
                {
                    var now = stopwatch.ElapsedMilliseconds;
                    for (var i = 0; i < clients.Count; i++)
                    {
                        clients[i].Poll(now);
                    }

                    if (now >= nextProgress)
                    {
                        PrintProgress(clients, now);
                        nextProgress += 10_000L;
                    }

                    Thread.Sleep(5);
                }

                var connected = 0;
                var helloAcked = 0;
                var snapshotBots = 0;
                var snapshots = 0;
                var deltas = 0;
                var acks = 0;
                var moves = 0;
                var abilities = 0;
                var disconnects = 0;
                foreach (var bot in clients)
                {
                    if (bot.IsConnected) connected++;
                    if (bot.HelloAcked) helloAcked++;
                    if (bot.Snapshots > 0) snapshotBots++;
                    snapshots += bot.Snapshots;
                    deltas += bot.Deltas;
                    acks += bot.Acks;
                    moves += bot.MovesSent;
                    abilities += bot.AbilitiesSent;
                    if (bot.DisconnectReason != null) disconnects++;
                }

                Console.WriteLine("[bots] done: connected=" + connected + "/" + clients.Count
                    + " hello=" + helloAcked + "/" + clients.Count
                    + " snapshotBots=" + snapshotBots + "/" + clients.Count
                    + " snapshots=" + snapshots + " deltas=" + deltas + " acks=" + acks
                    + " moves=" + moves + " abilities=" + abilities
                    + " disconnects=" + disconnects);
                return helloAcked == clients.Count
                    && snapshotBots == clients.Count
                    && disconnects == 0
                    ? 0
                    : 1;
            }
            finally
            {
                foreach (var bot in clients)
                {
                    bot.Dispose();
                }
            }
        }

        private static void PrintProgress(List<Bot> clients, long elapsedMs)
        {
            var hello = 0;
            var snapshots = 0;
            long moves = 0;
            long abilities = 0;
            foreach (var bot in clients)
            {
                if (bot.HelloAcked) hello++;
                snapshots += bot.Snapshots;
                moves += bot.MovesSent;
                abilities += bot.AbilitiesSent;
            }

            Console.WriteLine("[bots] t=" + (elapsedMs / 1000) + "s hello=" + hello + "/" + clients.Count
                + " snapshots=" + snapshots + " moves=" + moves + " abilities=" + abilities);
        }

        private static int ParseInt(string text, int fallback, int min, int max)
        {
            return int.TryParse(text, out var value) ? Math.Clamp(value, min, max) : fallback;
        }

        private sealed class Bot : IDisposable
        {
            private readonly int _index;
            private readonly LiteNetLibTransport _transport;
            private readonly BinarySnapshotSerializer _serializer = new BinarySnapshotSerializer();
            private readonly int _moveIntervalMs;
            private readonly int _abilityIntervalMs;
            private readonly uint _targetActorId;
            private readonly Stopwatch _clock;
            private uint _sequence;
            private uint _targetId;
            private long _nextHelloAt;
            private long _nextMoveAt;
            private long _nextAbilityAt;
            private long _nextFullRequestAt;
            private bool _disposed;

            public Bot(
                int index,
                string host,
                int port,
                int moveIntervalMs,
                int abilityIntervalMs,
                uint targetActorId)
            {
                _index = index;
                _transport = new LiteNetLibTransport(host, port);
                _moveIntervalMs = moveIntervalMs;
                _abilityIntervalMs = abilityIntervalMs;
                _targetActorId = targetActorId;
                _clock = Stopwatch.StartNew();
            }

            public bool IsConnected => _transport.IsConnected;

            public bool HelloAcked { get; private set; }

            public uint EntityId { get; private set; }

            public int Snapshots { get; private set; }

            public int Deltas { get; private set; }

            public int Acks { get; private set; }

            public int MovesSent { get; private set; }

            public int AbilitiesSent { get; private set; }

            public string? DisconnectReason { get; private set; }

            public void Start()
            {
                _transport.PacketReceived += OnPacket;
                _transport.Disconnected += reason =>
                {
                    DisconnectReason = reason;
                    Console.WriteLine("[bot " + _index + "] disconnected: " + reason);
                };
                _transport.Start();
            }

            public void Poll(long elapsedMs)
            {
                _transport.Poll();
                if (!_transport.IsConnected)
                {
                    return;
                }

                if (!HelloAcked && elapsedMs >= _nextHelloAt)
                {
                    _transport.Send(
                        MessageFraming.Wrap(MessageType.Hello, Array.Empty<byte>()),
                        DeliveryMode.ReliableOrdered);
                    _nextHelloAt = elapsedMs + 500;
                }

                if (!HelloAcked)
                {
                    return;
                }

                // Periodic full snapshot keeps the nearest-target choice fresh even though the
                // server otherwise sends deltas. 4 requests/s for 20 bots is negligible load.
                if (elapsedMs >= _nextFullRequestAt)
                {
                    _transport.Send(
                        MessageFraming.Wrap(MessageType.FullSnapshotRequest, Array.Empty<byte>()),
                        DeliveryMode.ReliableOrdered);
                    _nextFullRequestAt = elapsedMs + 5000;
                }

                if (elapsedMs >= _nextMoveAt)
                {
                    // Every bot gets a different phase, so the server sees mixed directions.
                    var phase = (_clock.Elapsed.TotalSeconds * 0.7) + (_index * 0.37);
                    var direction = new System.Numerics.Vector2(
                        (float)Math.Cos(phase),
                        (float)Math.Sin(phase));
                    _transport.Send(
                        MessageFraming.Wrap(
                            MessageType.Command,
                            CommandCodec.Encode(new MoveCommand(direction, (uint)(elapsedMs / 50), _sequence++))),
                        DeliveryMode.ReliableOrdered);
                    MovesSent++;
                    _nextMoveAt = elapsedMs + _moveIntervalMs;
                }

                if (elapsedMs >= _nextAbilityAt)
                {
                    _transport.Send(
                        MessageFraming.Wrap(
                            MessageType.Command,
                            CommandCodec.Encode(new AbilityCommand(_targetId, (uint)(elapsedMs / 50), _sequence++))),
                        DeliveryMode.ReliableOrdered);
                    AbilitiesSent++;
                    _nextAbilityAt = elapsedMs + _abilityIntervalMs;
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _transport.Dispose();
            }

            private void OnPacket(byte[] packet)
            {
                if (MessageFraming.TryUnwrap(packet, out var frame) != FramingError.None)
                {
                    return;
                }

                switch (frame.Type)
                {
                    case MessageType.HelloAck:
                        if (frame.Payload.Length >= 4)
                        {
                            EntityId = ReadUInt32(frame.Payload, 0);
                            HelloAcked = true;
                        }

                        break;
                    case MessageType.ServerAck:
                        Acks++;
                        break;
                    case MessageType.Snapshot:
                        Snapshots++;
                        UpdateTarget(frame.Payload);
                        break;
                    case MessageType.Delta:
                        Deltas++;
                        break;
                }
            }

            private void UpdateTarget(byte[] payload)
            {
                SimulationSnapshot snapshot;
                try
                {
                    snapshot = _serializer.Deserialize(payload);
                }
                catch (Exception)
                {
                    return;
                }

                var bestId = 0u;
                var bestScore = float.PositiveInfinity;
                var ownPosition = Vector3.Zero;
                var hasOwnPosition = false;
                foreach (var entity in snapshot.Entities)
                {
                    if (entity.Id == EntityId)
                    {
                        ownPosition = entity.Position;
                        hasOwnPosition = true;
                        break;
                    }
                }

                foreach (var entity in snapshot.Entities)
                {
                    // Target the training dummy, not other bots: this avoids PvP deaths that
                    // would turn the rest of the run into DeadCaster rejects and would stop
                    // exercising movement/ability. Missing/dying dummy = legal miss.
                    if (entity.Id == EntityId
                        || entity.IsDead
                        || entity.ActorId != _targetActorId
                        || !hasOwnPosition)
                    {
                        continue;
                    }

                    // Prefer the closest live dummy; ties are broken by id.
                    var score = Vector3.Distance(entity.Position, ownPosition) + entity.Id * 0.001f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestId = entity.Id;
                    }
                }

                _targetId = bestId;
            }

            private static uint ReadUInt32(byte[] buffer, int offset)
            {
                return (uint)(buffer[offset] | (buffer[offset + 1] << 8)
                    | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
            }
        }
    }
}
