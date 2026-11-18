# Simulation

**Файл:** `src/Simulation.Core/Simulation.cs`  
**Размер:** 1254 строк ≈ 12 825 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Ядро симуляции: тик 20 Гц, движение, бой, смерть/респавн, снапшоты.

**Из кода:** Реализация <see cref="ISimulation"/>: фиксированный тик, движение и способности по конфигу, смерть, респавн, полные снапшоты и восстановление из них.

## API (детерминированно из графа trail)

```
- L11-L1253  class Simulation  public sealed class Simulation : ISimulation
- L21-L24  method Simulation  public Simulation()
- L28-L31  method Simulation  public Simulation(SimulationState initialState)
- L35-L38  method Simulation  public Simulation(SimulationConfig config)
- L44-L75  method Simulation  public Simulation(SimulationState initialState, SimulationConfig? config)
- L78-L78  variable State  public SimulationState State => _state;
- L85-L95  method AllocateEntityId  public uint AllocateEntityId()
- L102-L122  method SpawnMobs  public int SpawnMobs()
- L125-L167  method Tick  public void Tick(float deltaTime, IReadOnlyList<CommandEnvelope> commands)
- L170-L218  method CreateSnapshot  public SimulationSnapshot CreateSnapshot()
- L220-L228  method GetZoneId  private uint GetZoneId(Vector3 position)
- L231-L267  method RestoreFromSnapshot  public void RestoreFromSnapshot(SimulationSnapshot snapshot)
- L269-L350  method ApplyCommand  private void ApplyCommand(CommandEnvelope envelope, float deltaTime)
- L352-L369  method ApplyTeleport  private void ApplyTeleport(EntityState entity, TeleportCommand command)
- L371-L392  method ApplyMove  private void ApplyMove(EntityState entity, MoveCommand command, float deltaTime)
- L394-L481  method ApplyAbility  private void ApplyAbility(EntityState caster, AbilityCommand command)
- L489-L520  method ApplyEquip  private void ApplyEquip(EntityState entity, EquipCommand command)
- L528-L554  method ApplyGather  private void ApplyGather(EntityState entity, GatherCommand command)
- L563-L602  method ApplyCraft  private void ApplyCraft(EntityState entity, CraftCommand command)
- L610-L642  method ApplyBuy  private void ApplyBuy(EntityState entity, BuyCommand command)
- L650-L682  method ApplySell  private void ApplySell(EntityState entity, SellCommand command)
- L691-L724  method ApplyLoot  private void ApplyLoot(EntityState looter, LootCommand command)
- L726-L729  method FindRecipe  private Recipe? FindRecipe(uint recipeId)
- L731-L742  method FindTrader  private NpcTrader? FindTrader(uint traderId)
- L745-L763  method CanCraftOutput  private static bool CanCraftOutput(Inventory inventory, Recipe recipe)
- L765-L776  method FindNode  private ResourceNodeConfig? FindNode(uint nodeId)
- L783-L801  method UpdateMobs  private void UpdateMobs(float deltaTime)
- L803-L895  method UpdateMob  private void UpdateMob(EntityState mob, MobDefinition definition, float deltaTime)
- L897-L903  method ArriveHome  private void ArriveHome(EntityState mob, MobDefinition definition)
- L905-L948  method FindMobTarget  private EntityState? FindMobTarget(EntityState mob, ZoneConfig zone, out float distance)
- L950-L994  method MoveMobTowards  private bool MoveMobTowards(EntityState mob, Vector3 destination, float deltaTime)
- L999-L1012  method BuildActorCircles  private List<CollisionCircle> BuildActorCircles(uint selfId)
- L1014-L1022  method ResetMobAi  private static void ResetMobAi(EntityState mob)
- L1029-L1088  method CreateCorpse  private void CreateCorpse(EntityState victim)
- L1090-L1102  method LootHash  private static uint LootHash(uint entityId, uint salt, uint tick)
- L1111-L1152  method Kill  private void Kill(EntityState victim, EntityState killer)
- L1154-L1194  method RegenerateResources  private void RegenerateResources(float deltaTime)
- L1196-L1234  method ProcessRespawns  private void ProcessRespawns()
- L1236-L1240  method ToTicks  private static uint ToTicks(float seconds)
- L1242-L1247  method HorizontalDistance  private static float HorizontalDistance(Vector3 left, Vector3 right)
- L1249-L1252  method IsNear  private static bool IsNear(Vector3 left, Vector3 right, float range)
```

## Как пользоваться

1. Нужен ответ про Simulation — читайте здесь, а не src/Simulation.Core/Simulation.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py Simulation`.
