using System;
using System.Collections.Generic;
using System.Numerics;
using Client.Network;
using Shared;
using Simulation.Core;
using CoreSimulation = Simulation.Core.Simulation;

namespace Server
{
    /// <summary>
    /// Сервер прототипа: сессии клиентов, приём и валидация команд, тик симуляции и рассылка полных снапшотов.
    /// Транспорт подменяем (M4 — in-memory, M5 — LiteNetLib).
    /// </summary>
    public sealed class GameServer : IDisposable
    {
        private readonly SimulationConfig _config;
        private readonly IServerTransport _transport;
        private readonly ServerOptions _options;
        private readonly InputValidator _validator;
        private readonly BinarySnapshotSerializer _serializer = new BinarySnapshotSerializer();
        private readonly Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();
        private readonly List<CommandEnvelope> _pendingCommands = new List<CommandEnvelope>();
        private readonly List<string> _log = new List<string>();
        private readonly Action<string>? _logSink;
        private uint _nextEntityId = 1;
        private bool _disposed;

        /// <summary>Создаёт сервер поверх конфигурации и транспорта.</summary>
        /// <param name="config">Конфигурация акторов и способностей.</param>
        /// <param name="transport">Транспорт (M4 — in-memory).</param>
        /// <param name="options">Политики сервера.</param>
        /// <param name="logSink">Дополнительный приёмник лога (обычно <c>Console.WriteLine</c>).</param>
        /// <exception cref="ArgumentNullException">Если обязательный аргумент равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если <see cref="ServerOptions.TickRate"/> не положителен.</exception>
        public GameServer(
            SimulationConfig config,
            IServerTransport transport,
            ServerOptions options,
            Action<string>? logSink = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logSink = logSink;

            if (options.TickRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), options.TickRate, "TickRate должен быть положительным.");
            }

            _validator = new InputValidator(config, options);
            Simulation = new CoreSimulation(config);

            _transport.ClientConnected += OnClientConnected;
            _transport.ClientDisconnected += OnClientDisconnected;
            _transport.PacketReceived += OnPacketReceived;
        }

        /// <summary>Симуляция мира.</summary>
        public CoreSimulation Simulation { get; }

        /// <summary>Валидатор входящих команд.</summary>
        public InputValidator Validator => _validator;

        /// <summary>Записи лога сервера (включая отклонённые команды) — для тестов и демо.</summary>
        public IReadOnlyList<string> LogEntries => _log;

        /// <summary>Запускает транспорт.</summary>
        public void Start()
        {
            _transport.Start();
            Log("[t0] server started: tickRate=" + _options.TickRate + ", maxCommandsPerTick=" + _options.MaxCommandsPerTick);
        }

        /// <summary>Обрабатывает входящие пакеты (перед тиком).</summary>
        public void Poll()
        {
            _transport.Poll();
        }

        /// <summary>Выполняет один тик: применяет накопленные команды и рассылает снапшот.</summary>
        public void Tick()
        {
            var commands = _pendingCommands.Count == 0 ? Array.Empty<CommandEnvelope>() : _pendingCommands.ToArray();
            _pendingCommands.Clear();

            Simulation.Tick(SimulationConstants.FixedDeltaTime, commands);

            var snapshot = _serializer.Serialize(Simulation.CreateSnapshot());
            var packet = MessageFraming.Wrap(MessageType.Snapshot, snapshot);

            foreach (var session in _sessions.Values)
            {
                _transport.Send(session.ConnectionId, packet, DeliveryMode.Unreliable);
                session.CommandsThisTick = 0;
            }

            if (_options.LogTicks)
            {
                Log("[t" + Simulation.State.Tick + "] tick: entities=" + Simulation.State.Entities.Count
                    + ", commands=" + commands.Length + ", sessions=" + _sessions.Count);
            }
        }

        /// <summary>Возвращает сущность клиента, если он уже прошёл Hello.</summary>
        /// <param name="connectionId">Идентификатор соединения.</param>
        /// <param name="entityId">Идентификатор сущности.</param>
        /// <returns><c>true</c>, если сессия есть.</returns>
        public bool TryGetSessionEntity(int connectionId, out uint entityId)
        {
            if (_sessions.TryGetValue(connectionId, out var session))
            {
                entityId = session.EntityId;
                return true;
            }

            entityId = 0;
            return false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _transport.ClientConnected -= OnClientConnected;
            _transport.ClientDisconnected -= OnClientDisconnected;
            _transport.PacketReceived -= OnPacketReceived;
            _transport.Dispose();
        }

        private void OnClientConnected(int connectionId)
        {
            Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": connected, ожидает Hello");
        }

        private void OnClientDisconnected(int connectionId, string reason)
        {
            if (_sessions.TryGetValue(connectionId, out var session))
            {
                Simulation.State.RemoveEntity(session.EntityId);
                _sessions.Remove(connectionId);
                Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": disconnected (" + reason + "), сущность "
                    + session.EntityId + " удалена");
                return;
            }

            Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": disconnected (" + reason + ")");
        }

        private void OnPacketReceived(int connectionId, byte[] packet)
        {
            var error = MessageFraming.TryUnwrap(packet, out var frame);
            if (error != FramingError.None)
            {
                Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": пакет отклонён (" + error + ")");
                return;
            }

            switch (frame.Type)
            {
                case MessageType.Hello:
                    HandleHello(connectionId, frame.Payload);
                    break;

                case MessageType.Command:
                    HandleCommand(connectionId, frame.Payload);
                    break;

                case MessageType.FullSnapshotRequest:
                    SendSnapshot(connectionId);
                    break;

                default:
                    Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": неподдерживаемый тип сообщения "
                        + frame.Type);
                    break;
            }
        }

        private void HandleHello(int connectionId, byte[] payload)
        {
            if (_sessions.ContainsKey(connectionId))
            {
                Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": повторный Hello проигнорирован");
                return;
            }

            var actorId = payload.Length >= 4 ? ReadUInt32(payload, 0) : 0u;
            var actor = actorId == 0 ? DefaultActor() : _config.GetActor(actorId);

            var entityId = _nextEntityId++;
            var spawn = new Vector3((_sessions.Count + 1) * _options.SpawnSpacing, 0f, 0f);
            Simulation.State.AddEntity(new EntityState(entityId, actor, spawn));

            _sessions[connectionId] = new ClientSession(connectionId, entityId);

            var ack = new byte[12];
            WriteUInt32(ack, 0, entityId);
            WriteUInt32(ack, 4, Simulation.State.Tick);
            WriteUInt32(ack, 8, actor.Id);
            _transport.Send(connectionId, MessageFraming.Wrap(MessageType.HelloAck, ack), DeliveryMode.ReliableOrdered);

            Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": Hello принят, сущность " + entityId
                + " (актор " + actor.Id + ") в точке " + spawn);
        }

        private void HandleCommand(int connectionId, byte[] payload)
        {
            if (!_sessions.TryGetValue(connectionId, out var session))
            {
                Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": rejected (CommandBeforeHello) — команда до Hello");
                return;
            }

            if (!CommandCodec.TryDecode(payload, out var command) || command == null)
            {
                Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": rejected (BadPayload) — команда не декодируется");
                return;
            }

            var envelope = new CommandEnvelope(session.EntityId, command);
            var result = _validator.Validate(Simulation.State, Simulation.State.Tick, envelope, session.CommandsThisTick);
            session.CommandsThisTick++;

            if (!result.IsValid)
            {
                Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": rejected (" + result.Reason + ") — " + result.Detail);
                return;
            }

            _pendingCommands.Add(envelope);
        }

        private void SendSnapshot(int connectionId)
        {
            var snapshot = _serializer.Serialize(Simulation.CreateSnapshot());
            _transport.Send(connectionId, MessageFraming.Wrap(MessageType.Snapshot, snapshot), DeliveryMode.ReliableOrdered);
        }

        private ActorConfig DefaultActor()
        {
            var best = uint.MaxValue;
            ActorConfig? chosen = null;

            foreach (var pair in _config.Actors)
            {
                if (pair.Key < best)
                {
                    best = pair.Key;
                    chosen = pair.Value;
                }
            }

            return chosen ?? throw new InvalidOperationException("В конфигурации нет ни одного актора.");
        }

        private void Log(string message)
        {
            _log.Add(message);
            _logSink?.Invoke(message);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }

        private sealed class ClientSession
        {
            public ClientSession(int connectionId, uint entityId)
            {
                ConnectionId = connectionId;
                EntityId = entityId;
            }

            public int ConnectionId { get; }

            public uint EntityId { get; }

            public int CommandsThisTick { get; set; }
        }
    }
}
