# SimulationConfig

**Файл:** `src/Simulation.Core/SimulationConfig.cs`  
**Размер:** 587 строк ≈ 6 884 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Типизированный доступ к конфигам симуляции.

**Из кода:** Полная конфигурация мира (ADR-005): типы акторов, способности, зоны и предметы. Создаётся загрузчиком из JSON; ни одно геймплейное число в коде не дублируется.

## API (детерминированно из графа trail)

```
- L11-L586  class SimulationConfig  public sealed class SimulationConfig
- L39-L172  method SimulationConfig  public SimulationConfig(
- L175-L175  variable Actors  public IReadOnlyDictionary<uint, ActorConfig> Actors { get; }
- L178-L178  variable Abilities  public IReadOnlyDictionary<uint, AbilityConfig> Abilities { get; }
- L181-L181  variable Items  public IReadOnlyDictionary<uint, ItemDefinition> Items => _items;
- L184-L184  variable Zones  public ZoneMap Zones { get; }
- L187-L187  variable WorldColliders  public WorldColliders WorldColliders => _worldColliders;
- L190-L190  variable SkillTree  public SkillTree SkillTree { get; }
- L193-L193  variable ResourceNodes  public IReadOnlyList<ResourceNodeConfig> ResourceNodes => _resourceNodes;
- L196-L196  variable Recipes  public IReadOnlyDictionary<uint, Recipe> Recipes => _recipes;
- L199-L199  variable Traders  public IReadOnlyList<NpcTrader> Traders => _traders;
- L202-L202  variable Mobs  public IReadOnlyDictionary<uint, MobDefinition> Mobs => _mobs;
- L205-L205  variable MobTypes  public IReadOnlyList<MobDefinition> MobTypes => _mobTypes;
- L210-L218  method GetMob  public MobDefinition GetMob(uint mobId)
- L224-L227  method TryGetMobByActorId  public bool TryGetMobByActorId(uint actorId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MobDefinition? mob)
- L232-L240  method GetActor  public ActorConfig GetActor(uint actorId)
- L245-L253  method GetAbility  public AbilityConfig GetAbility(uint abilityId)
- L258-L266  method GetItem  public ItemDefinition GetItem(uint itemId)
- L271-L279  method GetRecipe  public Recipe GetRecipe(uint recipeId)
- L284-L295  method GetTrader  public NpcTrader GetTrader(uint traderId)
- L297-L334  method ValidateRecipes  private void ValidateRecipes()
- L336-L386  method ValidateTraders  private void ValidateTraders()
- L388-L465  method ValidateMobs  private void ValidateMobs()
- L467-L516  method ValidateStarterLoadout  private void ValidateStarterLoadout(ActorConfig actor)
- L518-L530  method ValidateItemArchetypes  private void ValidateItemArchetypes()
- L532-L548  method ValidateSkillTreeAbilities  private void ValidateSkillTreeAbilities()
- L550-L585  method ValidateResourceNodes  private void ValidateResourceNodes()
```

## Как пользоваться

1. Нужен ответ про SimulationConfig — читайте здесь, а не src/Simulation.Core/SimulationConfig.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py SimulationConfig`.
