using System;
using System.Collections.Generic;
using System.Numerics;
using Simulation.Core;

namespace Client.Network
{
    /// <summary>
    /// Клиентское предсказание движения и реконсиляция (M7, ADR-009, ADR-010).
    /// </summary>
    /// <remarks>
    /// Клиент не создаёт копию <see cref="Simulation"/>; с M9 он читает тот же JSON-конфиг, что и сервер
    /// (<see cref="ClientContent"/>), и берёт из него скорость, радиус и кулдаун. Предсказание повторяет
    /// правило движения симуляции (<c>position += direction * moveSpeed * dt</c>) и кулдаун способности.
    /// При расхождении состояние восстанавливается из серверного снапшота и к нему заново применяются
    /// неподтверждённые команды — клиент <b>не</b> телепортируется в серверную позицию (AI-Failure Mode M7).
    /// </remarks>
    public sealed class ClientPrediction
    {
        /// <summary>Допустимое расхождение предсказания и реконсиляции, мировые единицы.</summary>
        public const float DefaultTolerance = 0.01f;

        /// <summary>Длительность тика симуляции, секунды (20 Гц, как у сервера).</summary>
        public const float TickDelta = 0.05f;

        private readonly uint _entityId;
        private readonly float _moveSpeed;
        private readonly ActorConfig? _actor;
        private readonly AbilityConfig? _ability;
        private readonly List<ICommand> _unacknowledged = new List<ICommand>();
        private Vector3 _position;
        private bool _hasPosition;
        private uint _cooldownUntilTick;
        private uint _pendingAbilitySequence;

        /// <summary>Создаёт предсказание для сущности игрока.</summary>
        /// <param name="entityId">Идентификатор сущности, выданный сервером.</param>
        /// <param name="moveSpeed">Скорость перемещения актора, единиц в секунду.</param>
        /// <param name="tolerance">Допустимое расхождение для реконсиляции.</param>
        public ClientPrediction(uint entityId, float moveSpeed, float tolerance = DefaultTolerance)
            : this(entityId, moveSpeed, null, null, tolerance)
        {
        }

        /// <summary>
        /// Создаёт предсказание из конфига клиента (M9): скорость движения, радиус и кулдаун способности
        /// берутся из того же JSON, что и у сервера, а не из констант клиента.
        /// </summary>
        /// <param name="entityId">Идентификатор сущности, выданный сервером.</param>
        /// <param name="actor">Тип актора из конфига.</param>
        /// <param name="ability">Способность актора из конфига.</param>
        /// <param name="tolerance">Допустимое расхождение для реконсиляции.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="actor"/> или <paramref name="ability"/> равны <c>null</c>.</exception>
        public ClientPrediction(uint entityId, ActorConfig actor, AbilityConfig ability, float tolerance = DefaultTolerance)
            : this(
                entityId,
                (actor ?? throw new ArgumentNullException(nameof(actor))).MoveSpeed,
                actor,
                ability ?? throw new ArgumentNullException(nameof(ability)),
                tolerance)
        {
        }

        private ClientPrediction(uint entityId, float moveSpeed, ActorConfig? actor, AbilityConfig? ability, float tolerance)
        {
            if (float.IsNaN(moveSpeed) || float.IsInfinity(moveSpeed) || moveSpeed < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(moveSpeed), moveSpeed, "Скорость должна быть конечной и неотрицательной.");
            }

            _entityId = entityId;
            _moveSpeed = moveSpeed;
            _actor = actor;
            _ability = ability;
            Tolerance = tolerance;
        }

        /// <summary>Сущность, за которую предсказывает клиент.</summary>
        public uint EntityId => _entityId;

        /// <summary>Скорость актора из HelloAck (или из конфига, если он передан).</summary>
        public float MoveSpeed => _moveSpeed;

        /// <summary>Тип актора из конфига клиента (M9); <c>null</c>, если конфиг не передан.</summary>
        public ActorConfig? Actor => _actor;

        /// <summary>Способность актора из конфига клиента (M9); <c>null</c>, если конфиг не передан.</summary>
        public AbilityConfig? Ability => _ability;

        /// <summary>Радиус способности из конфига (M9), 0 если конфига нет.</summary>
        public float AbilityRange => _ability?.Range ?? 0f;

        /// <summary>Кулдаун способности в тиках из конфига (M9), 0 если конфига нет.</summary>
        public int AbilityCooldownTicks =>
            _ability == null ? 0 : (int)Math.Ceiling(_ability.CooldownSeconds / TickDelta);

        /// <summary>Кулдаун, предсказанный локально (тик, до которого способность недоступна).</summary>
        public uint CooldownUntilTick => _cooldownUntilTick;

        /// <summary>Сколько тиков осталось до конца предсказанного кулдауна.</summary>
        /// <param name="tick">Текущий тик.</param>
        /// <returns>Число тиков; 0, если способность готова.</returns>
        public int CooldownRemainingTicks(uint tick) => _cooldownUntilTick > tick ? (int)(_cooldownUntilTick - tick) : 0;

        /// <summary>
        /// Предсказывает кулдаун после отправки <see cref="AbilityCommand"/> (M9): длительность берётся
        /// из конфига способности. Сервер подтвердит или отменит предсказание при реконсиляции.
        /// </summary>
        /// <param name="clientTick">Клиентский тик команды.</param>
        /// <param name="sequence">Sequence команды способности.</param>
        public void PredictAbility(uint clientTick, uint sequence)
        {
            if (_ability == null)
            {
                return;
            }

            _pendingAbilitySequence = Math.Max(_pendingAbilitySequence, sequence);
            var until = clientTick + (uint)AbilityCooldownTicks;
            if (until > _cooldownUntilTick)
            {
                _cooldownUntilTick = until;
            }
        }

        /// <summary>Порог, выше которого расхождение считается реальным и запускает реконсиляцию.</summary>
        public float Tolerance { get; }

        /// <summary>Текущая предсказанная позиция (после последней локальной команды/реконсиляции).</summary>
        public Vector3 Position => _position;

        /// <summary>Сколько раз реально срабатывала реконсиляция (для лога и Human-Gate).</summary>
        public int Reconciliations { get; private set; }

        /// <summary>Расхождение, зафиксированное последней реконсиляцией.</summary>
        public float LastError { get; private set; }

        /// <summary>Неподтверждённые команды (sequence по возрастанию) — их повторно применяют при реконсиляции.</summary>
        public IReadOnlyList<ICommand> Unacknowledged => _unacknowledged;

        /// <summary>Сработала реконсиляция: расхождение больше допуска.</summary>
        public event Action<PredictionReconciliation>? Reconciled;

        /// <summary>Локально применяет команду, которую клиент отправил серверу (предсказание).</summary>
        /// <param name="command">Отправленная команда.</param>
        /// <exception cref="ArgumentNullException">Если команда равна <c>null</c>.</exception>
        public void ApplyLocalCommand(ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            _unacknowledged.Add(command);

            // До первого снапшота базы нет: шагать не от чего. Команда попадёт в список и будет
            // применена к серверной позиции при первой реконсиляции (это не «реконсиляция», а инициализация).
            if (!_hasPosition)
            {
                return;
            }

            _position = Step(_position, command);
        }

        /// <summary>
        /// Реконсилирует предсказание по серверному снапшоту и подтверждённой команде:
        /// база — серверная позиция, к ней заново применяются неподтверждённые команды.
        /// </summary>
        /// <param name="snapshot">Снапшот сервера на том же тике, что и подтверждение.</param>
        /// <param name="acknowledgedSequence">Последняя команда, обработанная сервером на этом тике.</param>
        /// <returns><c>true</c>, если предсказание было скорректировано.</returns>
        public bool Reconcile(SimulationSnapshot snapshot, uint acknowledgedSequence)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (!TryFindOwnEntity(snapshot, out var ownEntity))
            {
                return false;
            }

            var serverPosition = new Vector3(ownEntity.Position.X, ownEntity.Position.Y, ownEntity.Position.Z);
            if (_pendingAbilitySequence <= acknowledgedSequence)
            {
                // Сервер обработал способность: его кулдаун — истина (0, если команду отклонили).
                _cooldownUntilTick = ownEntity.CooldownUntilTick;
                _pendingAbilitySequence = 0;
            }

            var predicted = serverPosition;
            foreach (var command in _unacknowledged)
            {
                if (command.Sequence > acknowledgedSequence)
                {
                    predicted = Step(predicted, command);
                }
            }

            var error = Vector3.Distance(predicted, _position);
            LastError = error;

            _unacknowledged.RemoveAll(command => command.Sequence <= acknowledgedSequence);

            var corrected = !_hasPosition || error > Tolerance;
            if (corrected)
            {
                if (_hasPosition)
                {
                    Reconciliations++;
                    Reconciled?.Invoke(new PredictionReconciliation(snapshot.Tick, error, predicted, serverPosition));
                }

                _position = predicted;
            }

            _hasPosition = true;
            return corrected;
        }

        /// <summary>Сбрасывает состояние (новый HelloAck или переподключение).</summary>
        /// <param name="position">Известная позиция для старта.</param>
        public void Reset(Vector3 position)
        {
            _unacknowledged.Clear();
            _position = position;
            _hasPosition = true;
            _cooldownUntilTick = 0;
            _pendingAbilitySequence = 0;
        }

        private Vector3 Step(Vector3 position, ICommand command)
        {
            if (command is MoveCommand move)
            {
                return new Vector3(
                    position.X + (move.Direction.X * _moveSpeed * TickDelta),
                    position.Y,
                    position.Z + (move.Direction.Y * _moveSpeed * TickDelta));
            }

            return position;
        }

        private bool TryFindOwnEntity(SimulationSnapshot snapshot, out EntitySnapshot entity)
        {
            foreach (var candidate in snapshot.Entities)
            {
                if (candidate.Id == _entityId)
                {
                    entity = candidate;
                    return true;
                }
            }

            entity = default;
            return false;
        }
    }

    /// <summary>Событие реконсиляции: тик, расхождение и обе позиции — для лога и отладки.</summary>
    public readonly struct PredictionReconciliation
    {
        /// <summary>Создаёт запись о реконсиляции.</summary>
        /// <param name="tick">Тик сервера.</param>
        /// <param name="error">Расхождение до коррекции.</param>
        /// <param name="predicted">Позиция после повторного применения команд.</param>
        /// <param name="server">Серверная позиция из снапшота.</param>
        public PredictionReconciliation(uint tick, float error, Vector3 predicted, Vector3 server)
        {
            Tick = tick;
            Error = error;
            Predicted = predicted;
            Server = server;
        }

        /// <summary>Тик сервера.</summary>
        public uint Tick { get; }

        /// <summary>Расхождение до коррекции.</summary>
        public float Error { get; }

        /// <summary>Позиция после реконсиляции (сервер + неподтверждённые команды).</summary>
        public Vector3 Predicted { get; }

        /// <summary>Серверная позиция из снапшота.</summary>
        public Vector3 Server { get; }
    }
}
