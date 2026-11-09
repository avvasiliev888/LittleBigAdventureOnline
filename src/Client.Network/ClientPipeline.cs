using System;
using System.Collections.Generic;
using System.Numerics;
using Shared;
using Simulation.Core;

namespace Client.Network
{
    /// <summary>
    /// Клиентский конвейер (M6, M7): приём снапшотов, отправка команд ввода, предсказание движения,
    /// реконсиляция по ServerAck и отрисовка.
    /// Не содержит авторитетной логики: только транспорт, ввод и представление (ADR-001, ADR-006).
    /// </summary>
    public sealed class ClientPipeline : IDisposable
    {
        /// <summary>Длительность клиентского тика, секунды (20 Гц, как у сервера).</summary>
        public const float TickIntervalSeconds = 0.05f;

        private const float HelloRetrySeconds = 0.5f;
        private const float FullSnapshotTimeoutSeconds = 1.0f;
        private const int AckHistorySize = 40;

        private readonly ITransport _transport;
        private readonly ISnapshotSerializer _serializer;
        private readonly IInputSource _input;
        private readonly IView _view;
        private readonly BinarySnapshotSerializer _fallbackSerializer = new BinarySnapshotSerializer();
        private readonly Dictionary<uint, uint> _acknowledgedByTick = new Dictionary<uint, uint>();
        private readonly List<string> _log = new List<string>();
        private SimulationSnapshot? _lastSnapshot;
        private float _tickAccumulator;
        private float _sinceHello;
        private float _sinceSnapshot;
        private uint _clientTick;
        private bool _disposed;

        /// <summary>Создаёт конвейер.</summary>
        /// <param name="transport">Транспорт (UDP или in-memory в тестах).</param>
        /// <param name="input">Источник ввода.</param>
        /// <param name="view">Представление мира.</param>
        /// <param name="serializer">Десериализатор снапшотов; по умолчанию — бинарный формат ADR-008.</param>
        /// <exception cref="ArgumentNullException">Если обязательный аргумент равен <c>null</c>.</exception>
        public ClientPipeline(ITransport transport, IInputSource input, IView view, ISnapshotSerializer? serializer = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _serializer = serializer ?? _fallbackSerializer;

            _transport.PacketReceived += OnPacketReceived;
            _transport.Disconnected += OnTransportDisconnected;
        }

        /// <summary>Идентификатор сущности игрока (0, пока сервер не ответил HelloAck).</summary>
        public uint EntityId { get; private set; }

        /// <summary>Есть ли сессия (получен HelloAck).</summary>
        public bool HasSession { get; private set; }

        /// <summary>Сколько снапшотов применено к представлению.</summary>
        public int SnapshotsApplied { get; private set; }

        /// <summary>Предсказание движения; <c>null</c>, пока нет HelloAck со скоростью актора.</summary>
        public ClientPrediction? Prediction { get; private set; }

        /// <summary>Записи клиентского лога (реконсиляции, ошибки протокола) — для HUD и тестов.</summary>
        public IReadOnlyList<string> LogEntries => _log;

        /// <summary>Сессия готова: пришёл HelloAck с идентификатором сущности.</summary>
        public event Action<uint>? SessionReady;

        /// <summary>Соединение потеряно или отклонено (причина для лога).</summary>
        public event Action<string>? Disconnected;

        /// <summary>Запускает транспорт и отправляет первый Hello.</summary>
        public void Start()
        {
            _transport.Start();
            SendHello();
        }

        /// <summary>
        /// Один кадр: приём пакетов, отправка ввода по тикам, отрисовка.
        /// </summary>
        /// <param name="deltaTime">Время с предыдущего кадра, секунды.</param>
        public void Update(float deltaTime)
        {
            if (_disposed)
            {
                return;
            }

            _transport.Poll();
            _sinceHello += deltaTime;
            _sinceSnapshot += deltaTime;

            if (!HasSession)
            {
                if (_sinceHello >= HelloRetrySeconds)
                {
                    SendHello();
                }
            }
            else if (_sinceSnapshot >= FullSnapshotTimeoutSeconds)
            {
                _transport.Send(MessageFraming.Wrap(MessageType.FullSnapshotRequest, ReadOnlySpan<byte>.Empty), DeliveryMode.ReliableOrdered);
                _sinceSnapshot = 0f;
            }

            _tickAccumulator += deltaTime;
            while (_tickAccumulator >= TickIntervalSeconds)
            {
                _tickAccumulator -= TickIntervalSeconds;
                _clientTick++;

                ICommand? command = null;
                var hasCommand = HasSession && _input.TryReadCommand(_clientTick, out command) && command != null;
                if (hasCommand)
                {
                    _transport.Send(MessageFraming.Wrap(MessageType.Command, CommandCodec.Encode(command!)), DeliveryMode.ReliableOrdered);
                    Prediction?.ApplyLocalCommand(command!);
                }

                // Сэмпл каждый клиентский тик, а не только на снапшоте: тайминг сэмплов ровный даже
                // при джиттере сети, поэтому сглаживание рисует движение, а не сетевые рывки (M7).
                // Скорость берём из команды: остановка мгновенная, ложной скорости из скачков нет.
                if (Prediction != null && _view is IPredictedView predictedLocalView)
                {
                    var velocity = Vector3.Zero;
                    if (hasCommand && command is MoveCommand move)
                    {
                        velocity = new Vector3(move.Direction.X * Prediction.MoveSpeed, 0f, move.Direction.Y * Prediction.MoveSpeed);
                    }

                    predictedLocalView.SetPredictedPlayerPosition(_clientTick, Prediction.Position, velocity, false);
                }
            }

            _view.Render(deltaTime);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _transport.PacketReceived -= OnPacketReceived;
            _transport.Disconnected -= OnTransportDisconnected;
            _transport.Dispose();
        }

        private void SendHello()
        {
            _transport.Send(MessageFraming.Wrap(MessageType.Hello, ReadOnlySpan<byte>.Empty), DeliveryMode.ReliableOrdered);
            _sinceHello = 0f;
        }

        private void OnPacketReceived(byte[] packet)
        {
            if (MessageFraming.TryUnwrap(packet, out var frame) != FramingError.None)
            {
                return;
            }

            switch (frame.Type)
            {
                case MessageType.HelloAck:
                    HandleHelloAck(frame.Payload);
                    break;

                case MessageType.ServerAck:
                    HandleServerAck(frame.Payload);
                    break;

                case MessageType.Snapshot:
                    HandleSnapshot(frame.Payload);
                    break;
            }
        }

        private void HandleHelloAck(byte[] payload)
        {
            EntityId = ReadUInt32(payload, 0);
            var moveSpeed = payload.Length >= 16 ? ReadSingle(payload, 12) : 0f;

            if (Prediction != null)
            {
                Prediction.Reconciled -= OnReconciled;
            }

            Prediction = new ClientPrediction(EntityId, moveSpeed);
            Prediction.Reconciled += OnReconciled;

            HasSession = true;
            _acknowledgedByTick.Clear();
            _log.Add("[hello] сущность " + EntityId + ", скорость " + moveSpeed.ToString("0.###"));
            SessionReady?.Invoke(EntityId);
        }

        private void HandleServerAck(byte[] payload)
        {
            if (!AckCodec.TryDecode(payload, out var ack))
            {
                _log.Add("[ack] некорректная полезная нагрузка (" + payload.Length + " байт)");
                return;
            }

            if (ack.EntityId != EntityId)
            {
                return;
            }

            _acknowledgedByTick[ack.Tick] = ack.AcknowledgedSequence;

            // Снапшот мог прийти раньше подтверждения (разные каналы доставки). Тогда реконсилируем
            // здесь, иначе этот тик не попадёт в буфер отрисовки и движение станет неравномерным.
            if (Prediction != null && _lastSnapshot != null && _lastSnapshot.Tick == ack.Tick)
            {
                Reconcile(_lastSnapshot, ack.AcknowledgedSequence);
            }

            if (_acknowledgedByTick.Count > AckHistorySize)
            {
                var oldest = ack.Tick > AckHistorySize ? ack.Tick - AckHistorySize : 0;
                var stale = new List<uint>();

                foreach (var tick in _acknowledgedByTick.Keys)
                {
                    if (tick < oldest)
                    {
                        stale.Add(tick);
                    }
                }

                foreach (var tick in stale)
                {
                    _acknowledgedByTick.Remove(tick);
                }
            }
        }

        private void HandleSnapshot(byte[] payload)
        {
            _sinceSnapshot = 0f;
            SnapshotsApplied++;

            var snapshot = _serializer.Deserialize(payload);
            _view.ApplySnapshot(snapshot);
            _lastSnapshot = snapshot;

            if (Prediction == null || !_acknowledgedByTick.TryGetValue(snapshot.Tick, out var acknowledgedSequence))
            {
                return;
            }

            // Пара «снапшот + подтверждение на одном тике» — единственный консистентный вход
            // для реконсиляции: снапшот мог прийти с задержкой, а ack уже уйти вперёд (M7).
            Reconcile(snapshot, acknowledgedSequence);
        }

        private void Reconcile(SimulationSnapshot snapshot, uint acknowledgedSequence)
        {
            if (Prediction == null)
            {
                return;
            }

            // Представление получит исправленную позицию на ближайшем клиентском тике (сэмплы идут
            // с частотой 20 Гц), поэтому коррекция приходит с задержкой не больше одного тика.
            Prediction.Reconcile(snapshot, acknowledgedSequence);
        }

        private void OnReconciled(PredictionReconciliation reconciliation)
        {
            _log.Add("[reconcile] tick=" + reconciliation.Tick
                + " error=" + reconciliation.Error.ToString("0.###")
                + " predicted=(" + reconciliation.Predicted.X.ToString("0.###") + ", " + reconciliation.Predicted.Z.ToString("0.###") + ")"
                + " server=(" + reconciliation.Server.X.ToString("0.###") + ", " + reconciliation.Server.Z.ToString("0.###") + ")");
        }

        private void OnTransportDisconnected(string reason)
        {
            HasSession = false;
            _log.Add("[disconnect] " + reason);
            Disconnected?.Invoke(reason);
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
