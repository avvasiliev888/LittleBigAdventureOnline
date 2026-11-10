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
        private readonly List<PendingCommand> _pendingCommands = new List<PendingCommand>();
        private SimulationSnapshot? _previousSnapshot;
        private readonly List<string> _log = new List<string>();
        private readonly Action<string>? _logSink;
        private readonly uint _trainingDummyId;
        private readonly float _trainingDummyMinX;
        private readonly float _trainingDummyMaxX;
        private float _trainingDummyDirection = 1f;
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

            if (options.SpawnTrainingDummy)
            {
                if (options.TrainingDummyEntityId == 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(options),
                        options.TrainingDummyEntityId,
                        "TrainingDummyEntityId должен быть положительным.");
                }

                var dummyActor = config.GetActor(options.TrainingDummyActorId);
                var dummy = new EntityState(options.TrainingDummyEntityId, dummyActor, options.TrainingDummySpawn);
                Simulation.State.AddEntity(dummy);
                _trainingDummyId = dummy.Id;
                _trainingDummyMinX = options.TrainingDummySpawn.X - 0.5f;
                _trainingDummyMaxX = options.TrainingDummySpawn.X + 0.5f;
                Log("[t0] training dummy: entity " + dummy.Id + ", actor " + dummyActor.Id
                    + ", spawn " + dummy.SpawnPosition);
            }

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

        /// <summary>Сколько дельта-снапшотов отправлено (M8, для замеров трафика).</summary>
        public int DeltasSent { get; private set; }

        /// <summary>Сколько полных снапшотов отправлено (M8).</summary>
        public int FullSnapshotsSent { get; private set; }

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
            var pending = _pendingCommands.Count == 0 ? Array.Empty<PendingCommand>() : _pendingCommands.ToArray();
            _pendingCommands.Clear();

            var commands = new List<CommandEnvelope>(pending.Length + 1);
            for (var i = 0; i < pending.Length; i++)
            {
                commands.Add(pending[i].Envelope);
            }

            // The dummy is server-driven: it walks a short patrol so remote interpolation and
            // the Walk blend can be verified without trusting client input for an NPC.
            AddTrainingDummyCommand(commands);

            Simulation.Tick(SimulationConstants.FixedDeltaTime, commands);
            ClampTrainingDummyToPatrolWindow();

            var current = Simulation.CreateSnapshot();
            var fullPayload = _serializer.Serialize(current);
            var fullPacket = MessageFraming.Wrap(MessageType.Snapshot, fullPayload);
            var ackTick = current.Tick;

            // Одна дельта на всех: база — предыдущий тик мира (M8, ADR-010).
            byte[]? deltaPacket = null;
            if (_previousSnapshot != null)
            {
                var deltaPayload = _serializer.SerializeDelta(_previousSnapshot, current);
                deltaPacket = MessageFraming.Wrap(MessageType.Delta, deltaPayload);
            }

            foreach (var session in _sessions.Values)
            {
                // Сначала ack, потом снапшот: клиент сверяет пару «тик + sequence» и не ждёт
                // подтверждение до следующего снапшота (M7).
                var ack = MessageFraming.Wrap(
                    MessageType.ServerAck,
                    AckCodec.Encode(session.EntityId, session.AcknowledgedSequence, ackTick));
                _transport.Send(session.ConnectionId, ack, DeliveryMode.ReliableOrdered);

                // Дельта уместна, только если у клиента гарантированно есть база (предыдущий тик).
                // Иначе (первый снапшот, разрыв, запрос полного) отправляем полный — надёжно.
                var hasBaseline = deltaPacket != null && session.LastSentTick == _previousSnapshot!.Tick;
                if (hasBaseline)
                {
                    _transport.Send(session.ConnectionId, deltaPacket!, DeliveryMode.Unreliable);
                    DeltasSent++;
                }
                else
                {
                    _transport.Send(session.ConnectionId, fullPacket, DeliveryMode.ReliableOrdered);
                    FullSnapshotsSent++;
                }

                session.LastSentTick = current.Tick;
                session.CommandsThisTick = 0;
            }

            _previousSnapshot = current;

            if (_options.LogTicks)
            {
                Log("[t" + Simulation.State.Tick + "] tick: entities=" + Simulation.State.Entities.Count
                    + ", commands=" + commands.Count + ", sessions=" + _sessions.Count);
            }
        }

        private void ClampTrainingDummyToPatrolWindow()
        {
            if (_trainingDummyId == 0
                || !Simulation.State.TryGetEntity(_trainingDummyId, out var dummy)
                || dummy.IsDead)
            {
                return;
            }

            var x = Math.Clamp(dummy.Position.X, _trainingDummyMinX, _trainingDummyMaxX);
            if (x != dummy.Position.X)
            {
                dummy.Position = new Vector3(x, dummy.Position.Y, dummy.Position.Z);
            }
        }

        private void AddTrainingDummyCommand(List<CommandEnvelope> commands)
        {
            if (_trainingDummyId == 0
                || !Simulation.State.TryGetEntity(_trainingDummyId, out var dummy)
                || dummy.IsDead)
            {
                return;
            }

            if (dummy.Position.X >= _trainingDummyMaxX)
            {
                _trainingDummyDirection = -1f;
            }
            else if (dummy.Position.X <= _trainingDummyMinX)
            {
                _trainingDummyDirection = 1f;
            }

            commands.Add(new CommandEnvelope(
                _trainingDummyId,
                new MoveCommand(new Vector2(_trainingDummyDirection, 0f), Simulation.State.Tick, 0)));
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
            // Never reuse the training dummy's reserved id, even if a player disconnects.
            if (entityId == _trainingDummyId)
            {
                entityId = _nextEntityId++;
            }

            var spawn = new Vector3((_sessions.Count + 1) * _options.SpawnSpacing, 0f, 0f);
            Simulation.State.AddEntity(new EntityState(entityId, actor, spawn));

            _sessions[connectionId] = new ClientSession(connectionId, entityId);

            // M7: клиенту нужна скорость актора для локального предсказания движения (ADR-009).
            var ack = new byte[16];
            WriteUInt32(ack, 0, entityId);
            WriteUInt32(ack, 4, Simulation.State.Tick);
            WriteUInt32(ack, 8, actor.Id);
            WriteSingle(ack, 12, actor.MoveSpeed);
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

            // Подтверждаем и отклонённые команды: иначе клиент предсказывает то, что сервер не принял,
            // и будет повторять её в реконсиляции бесконечно (M7).
            session.Acknowledge(command.Sequence);

            if (!result.IsValid)
            {
                Log("[t" + Simulation.State.Tick + "] conn " + connectionId + ": rejected (" + result.Reason + ") — " + result.Detail);
                return;
            }

            _pendingCommands.Add(new PendingCommand(connectionId, envelope));
        }

        private void SendSnapshot(int connectionId)
        {
            var snapshot = _serializer.Serialize(Simulation.CreateSnapshot());
            _transport.Send(connectionId, MessageFraming.Wrap(MessageType.Snapshot, snapshot), DeliveryMode.ReliableOrdered);
            FullSnapshotsSent++;

            if (_sessions.TryGetValue(connectionId, out var session))
            {
                session.LastSentTick = Simulation.State.Tick;
            }
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

        private static void WriteSingle(byte[] buffer, int offset, float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            WriteUInt32(buffer, offset, (uint)bits);
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

            public uint AcknowledgedSequence { get; private set; }

            /// <summary>Тик последнего снапшота, реально отправленного этому клиенту (для выбора дельты).</summary>
            public uint LastSentTick { get; set; }

            public int CommandsThisTick { get; set; }

            /// <summary>Сдвигает подтверждение только вперёд: команды приходят по порядку, а acks ретраятся.</summary>
            public void Acknowledge(uint sequence)
            {
                if (sequence > AcknowledgedSequence)
                {
                    AcknowledgedSequence = sequence;
                }
            }
        }

        private readonly struct PendingCommand
        {
            public PendingCommand(int connectionId, CommandEnvelope envelope)
            {
                ConnectionId = connectionId;
                Envelope = envelope;
            }

            public int ConnectionId { get; }

            public CommandEnvelope Envelope { get; }
        }
    }
}
