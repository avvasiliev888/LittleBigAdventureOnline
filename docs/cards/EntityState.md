# EntityState

**Файл:** `src/Simulation.Core/EntityState.cs`  
**Размер:** 294 строк ≈ 4 084 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Состояние сущности: позиция, HP, инвентарь, кулдауны.

**Из кода:** Состояние одной сущности в мире. Все геймплейные параметры берутся из <see cref="ActorConfig"/>.

## API (детерминированно из графа trail)

```
- L9-L293  class EntityState  public sealed class EntityState
- L17-L39  method EntityState  public EntityState(uint id, ActorConfig actor, Vector3 spawnPosition)
- L50-L55  method EntityState  public EntityState(uint id, MobDefinition mob, Vector3 spawnPosition)
- L58-L58  variable Id  public uint Id { get; }
- L61-L61  variable ActorId  public uint ActorId { get; }
- L64-L64  variable Actor  public ActorConfig Actor { get; }
- L70-L70  variable Mob  public MobDefinition? Mob { get; }
- L73-L73  variable AiState  public MobAiState AiState { get; set; }
- L76-L76  variable AiTargetId  public uint AiTargetId { get; set; }
- L79-L79  variable NextAttackTick  public uint NextAttackTick { get; set; }
- L82-L82  variable PatrolIndex  public uint PatrolIndex { get; set; }
- L85-L85  variable AiWaitUntilTick  public uint AiWaitUntilTick { get; set; }
- L92-L92  variable AiAvoidSide  public int AiAvoidSide { get; set; }
- L95-L95  variable SpawnPosition  public Vector3 SpawnPosition { get; }
- L98-L98  variable Position  public Vector3 Position { get; set; }
- L101-L101  variable Hp  public int Hp { get; set; }
- L104-L104  variable MaxHp  public int MaxHp { get; private set; }
- L107-L107  variable MoveSpeed  public float MoveSpeed { get; private set; }
- L110-L110  variable AbilityId  public uint AbilityId { get; private set; }
- L113-L113  variable DamageMultiplier  public float DamageMultiplier { get; private set; }
- L116-L116  variable Resource  public int Resource { get; set; }
- L122-L122  variable ResourceRegenRemainder  public float ResourceRegenRemainder { get; set; }
- L125-L125  variable MaxResource  public int MaxResource { get; }
- L128-L128  variable CooldownUntilTick  public uint CooldownUntilTick { get; set; }
- L131-L131  variable RespawnAtTick  public uint RespawnAtTick { get; set; }
- L138-L138  variable HostileUntilTick  public uint HostileUntilTick { get; set; }
- L141-L141  variable GatherCooldownUntilTick  public uint GatherCooldownUntilTick { get; set; }
- L144-L144  variable Fame  public FameProgress Fame { get; } = new FameProgress();
- L150-L150  variable Silver  public int Silver { get; set; }
- L153-L153  variable Inventory  public Inventory Inventory { get; }
- L156-L156  variable Equipment  public Equipment Equipment { get; } = new Equipment();
- L159-L159  variable IsDead  public bool IsDead => Hp <= 0;
- L164-L164  method IsHostile  public bool IsHostile(uint currentTick) => HostileUntilTick > currentTick;
- L175-L195  method Equip  public void Equip(SimulationConfig config, ItemDefinition item)
- L206-L232  method AwardFame  public int AwardFame(SimulationConfig config, int amount)
- L241-L262  method RecalculateStats  public void RecalculateStats(SimulationConfig config)
- L270-L292  method GrantStarterLoadout  public void GrantStarterLoadout(SimulationConfig config)
```

## Как пользоваться

1. Нужен ответ про EntityState — читайте здесь, а не src/Simulation.Core/EntityState.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py EntityState`.
