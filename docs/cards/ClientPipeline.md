# ClientPipeline

**Файл:** `src/Client.Network/ClientPipeline.cs`  
**Размер:** 928 строк ≈ 10 889 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Клиентский конвейер: 20 Гц тик, ввод, предсказание, рендер.

**Из кода:** Клиентский конвейер (M6, M7): приём снапшотов, отправка команд ввода, предсказание движения, реконсиляция по ServerAck и отрисовка. Не содержит авторитетной логики: только транспорт, ввод и представление (ADR-001, ADR-006).

## API (детерминированно из графа trail)

```
- L14-L927  class ClientPipeline  public sealed class ClientPipeline : IDisposable
- L54-L69  method ClientPipeline  public ClientPipeline(
- L72-L72  variable EntityId  public uint EntityId { get; private set; }
- L75-L75  variable HasSession  public bool HasSession { get; private set; }
- L78-L78  variable ClientTick  public uint ClientTick => _clientTick;
- L81-L81  variable SnapshotsApplied  public int SnapshotsApplied { get; private set; }
- L84-L84  variable Prediction  public ClientPrediction? Prediction { get; private set; }
- L87-L87  variable LogEntries  public IReadOnlyList<string> LogEntries => _log;
- L90-L90  variable LastSnapshot  public SimulationSnapshot? LastSnapshot => _lastSnapshot;
- L93-L93  variable LastUiSequence  public uint LastUiSequence => _lastUiSequence;
- L96-L96  variable LastCommandResult  public CommandResult? LastCommandResult => _lastCommandResult;
- L99-L99  variable LastCombatEvent  public CombatEvent? LastCombatEvent => _lastCombatEvent;
- L102-L102  variable HasInventory  public bool HasInventory => _inventory != null;
- L105-L106  variable Inventory  public IReadOnlyList<InventoryStack> Inventory =>
- L109-L109  variable DeltasApplied  public int DeltasApplied { get; private set; }
- L112-L112  variable FullSnapshotRequests  public int FullSnapshotRequests { get; private set; }
- L121-L121  variable Content  public ClientContent? Content => _content;
- L127-L131  method Start  public void Start()
- L136-L139  method SendCraft  public bool SendCraft(uint recipeId)
- L146-L149  method SendBuy  public bool SendBuy(uint traderId, uint itemId, int quantity)
- L156-L159  method SendSell  public bool SendSell(uint traderId, uint itemId, int quantity)
- L164-L167  method SendLoot  public bool SendLoot(uint corpseId)
- L172-L175  method SendEquip  public bool SendEquip(uint itemId)
- L180-L188  method SendGather  public bool SendGather(uint nodeId)
- L197-L212  method SendTeleport  public bool SendTeleport(float x, float z)
- L214-L224  method SendUiCommand  private bool SendUiCommand(ICommand command)
- L230-L313  method Update  public void Update(float deltaTime)
- L316-L327  method Dispose  public void Dispose()
- L329-L333  method SendHello  private void SendHello()
- L335-L372  method OnPacketReceived  private void OnPacketReceived(byte[] packet)
- L374-L419  method HandleHelloAck  private void HandleHelloAck(byte[] payload)
- L421-L421  method ShortHash  private static string ShortHash(string hash) => hash.Length <= 8 ? hash : hash.Substring(0, 8);
- L423-L463  method HandleServerAck  private void HandleServerAck(byte[] payload)
- L465-L475  method HandleCommandResult  private void HandleCommandResult(byte[] payload)
- L477-L494  method HandleCombatEvent  private void HandleCombatEvent(byte[] payload)
- L496-L510  method HandleInventory  private void HandleInventory(byte[] payload)
- L512-L516  method HandleSnapshot  private void HandleSnapshot(byte[] payload)
- L518-L572  method HandleDeltaSnapshot  private void HandleDeltaSnapshot(byte[] payload)
- L574-L594  method ApplyReceivedSnapshot  private void ApplyReceivedSnapshot(SimulationSnapshot snapshot)
- L596-L630  method SyncPredictionFromSnapshot  private void SyncPredictionFromSnapshot(SimulationSnapshot snapshot)
- L640-L677  method FindNearestEntity  public uint FindNearestEntity(uint excludeEntityId)
- L700-L740  method FindAbilityTargetTo  public uint FindAbilityTargetTo(Vector3 cursorPoint, uint excludeEntityId, ViewFrustum? view)
- L754-L758  method IsBodyOnScreen  private static bool IsBodyOnScreen(ViewFrustum view, Vector3 position, float radius)
- L774-L804  method FindNearestEntityTo  public uint FindNearestEntityTo(Vector3 point, uint excludeEntityId)
- L817-L821  method BodyRadius  private float BodyRadius(uint actorId)
- L835-L848  method TryGetEntity  public bool TryGetEntity(uint entityId, out EntitySnapshot entity)
- L850-L860  method RequestFullSnapshot  private void RequestFullSnapshot(bool force = false)
- L862-L883  method TrimSnapshots  private void TrimSnapshots(uint currentTick)
- L885-L898  method Reconcile  private void Reconcile(SimulationSnapshot snapshot, uint acknowledgedSequence)
- L900-L906  method OnReconciled  private void OnReconciled(PredictionReconciliation reconciliation)
- L908-L916  method OnTransportDisconnected  private void OnTransportDisconnected(string reason)
- L918-L921  method ReadUInt32  private static uint ReadUInt32(byte[] buffer, int offset)
- L923-L926  method ReadSingle  private static float ReadSingle(byte[] buffer, int offset)
```

## Как пользоваться

1. Нужен ответ про ClientPipeline — читайте здесь, а не src/Client.Network/ClientPipeline.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py ClientPipeline`.
