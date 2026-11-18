# GameServer

**Файл:** `src/Server/GameServer.cs`  
**Размер:** 667 строк ≈ 7 305 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Серверный цикл: приём команд, тик, рассылка снапшотов.

**Из кода:** Сервер прототипа: сессии клиентов, приём и валидация команд, тик симуляции и рассылка полных снапшотов. Транспорт подменяем (M4 — in-memory, M5 — LiteNetLib).

## API (детерминированно из графа trail)

```
- L9-L667  module Server  namespace Server
- L15-L666  class GameServer  public sealed class GameServer : IDisposable
- L42-L100  method GameServer  public GameServer(
- L103-L103  variable Simulation  public CoreSimulation Simulation { get; }
- L106-L106  variable Validator  public InputValidator Validator => _validator;
- L109-L109  variable Metrics  public ServerMetrics Metrics => _metrics;
- L112-L112  variable LogEntries  public IReadOnlyList<string> LogEntries => _log;
- L115-L115  variable DeltasSent  public int DeltasSent { get; private set; }
- L118-L118  variable FullSnapshotsSent  public int FullSnapshotsSent { get; private set; }
- L121-L125  method Start  public void Start()
- L128-L131  method Poll  public void Poll()
- L134-L152  method Tick  public void Tick()
- L156-L156  method LogMetrics  public void LogMetrics(string label = "tick") => Log(Metrics.Format(label));
- L158-L228  method TickCore  private void TickCore()
- L230-L264  method BroadcastCombatEvents  private void BroadcastCombatEvents(uint tick)
- L267-L284  method CombatStyleFor  private CombatStyle CombatStyleFor(EntityState attacker)
- L287-L298  struct PendingAttack  private readonly struct PendingAttack
- L289-L293  method PendingAttack  public PendingAttack(uint attackerEntityId, uint targetEntityId)
- L295-L295  variable AttackerEntityId  public uint AttackerEntityId { get; }
- L297-L297  variable TargetEntityId  public uint TargetEntityId { get; }
- L300-L314  method ClampTrainingDummyToPatrolWindow  private void ClampTrainingDummyToPatrolWindow()
- L316-L337  method AddTrainingDummyCommand  private void AddTrainingDummyCommand(List<CommandEnvelope> commands)
- L343-L353  method TryGetSessionEntity  public bool TryGetSessionEntity(int connectionId, out uint entityId)
- L356-L368  method Dispose  public void Dispose()
- L370-L373  method OnClientConnected  private void OnClientConnected(int connectionId)
- L375-L387  method OnClientDisconnected  private void OnClientDisconnected(int connectionId, string reason)
- L389-L417  method OnPacketReceived  private void OnPacketReceived(int connectionId, byte[] packet)
- L419-L452  method HandleHello  private void HandleHello(int connectionId, byte[] payload)
- L454-L496  method HandleCommand  private void HandleCommand(int connectionId, byte[] payload)
- L498-L507  method NeedsCommandResult  private static bool NeedsCommandResult(ICommand command)
- L509-L516  method SendCommandResult  private void SendCommandResult(ClientSession session, ICommand command, bool accepted, string detail)
- L518-L538  method MaybeSendInventory  private void MaybeSendInventory(ClientSession session)
- L540-L553  method InventoryFingerprint  private static ulong InventoryFingerprint(Inventory inventory)
- L555-L563  method Mix  private static ulong Mix(ulong hash, uint value)
- L565-L575  method SendSnapshot  private void SendSnapshot(int connectionId)
- L577-L592  method DefaultActor  private ActorConfig DefaultActor()
- L594-L598  method Log  private void Log(string message)
- L600-L606  method WriteUInt32  private static void WriteUInt32(byte[] buffer, int offset, uint value)
- L608-L612  method WriteSingle  private static void WriteSingle(byte[] buffer, int offset, float value)
- L614-L617  method ReadUInt32  private static uint ReadUInt32(byte[] buffer, int offset)
- L619-L652  class ClientSession  private sealed class ClientSession
- L621-L625  method ClientSession  public ClientSession(int connectionId, uint entityId)
- L627-L627  variable ConnectionId  public int ConnectionId { get; }
- L629-L629  variable EntityId  public uint EntityId { get; }
- L631-L631  variable AcknowledgedSequence  public uint AcknowledgedSequence { get; private set; }
- L634-L634  variable LastSentTick  public uint LastSentTick { get; set; }
- L636-L636  variable CommandsThisTick  public int CommandsThisTick { get; set; }
- L639-L639  variable InventoryFingerprint  public ulong InventoryFingerprint { get; set; }
- L642-L642  variable InventorySent  public bool InventorySent { get; set; }
- L645-L651  method Acknowledge  public void Acknowledge(uint sequence)
- L654-L665  struct PendingCommand  private readonly struct PendingCommand
- L656-L660  method PendingCommand  public PendingCommand(int connectionId, CommandEnvelope envelope)
- L662-L662  variable ConnectionId  public int ConnectionId { get; }
- L664-L664  variable Envelope  public CommandEnvelope Envelope { get; }
```

## Как пользоваться

1. Нужен ответ про GameServer — читайте здесь, а не src/Server/GameServer.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py GameServer`.
