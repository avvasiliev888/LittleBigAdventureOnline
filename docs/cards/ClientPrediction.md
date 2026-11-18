# ClientPrediction

**Файл:** `src/Client.Network/ClientPrediction.cs`  
**Размер:** 449 строк ≈ 6 196 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Предсказание и реконсиляция локального игрока (ADR-006).

**Из кода:** Клиентское предсказание движения и реконсиляция (M7, ADR-009, ADR-010).

## API (детерминированно из графа trail)

```
- L18-L419  class ClientPrediction  public sealed class ClientPrediction
- L47-L56  method ClientPrediction  public ClientPrediction(
- L69-L86  method ClientPrediction  public ClientPrediction(
- L88-L116  method ClientPrediction  private ClientPrediction(
- L119-L119  variable EntityId  public uint EntityId => _entityId;
- L122-L122  variable MoveSpeed  public float MoveSpeed => _moveSpeed;
- L125-L125  variable CollisionRadius  public float CollisionRadius => _collisionRadius;
- L128-L128  variable Actor  public ActorConfig? Actor => _actor;
- L131-L131  variable Ability  public AbilityConfig? Ability => _ability;
- L139-L147  method SetMoveSpeed  public void SetMoveSpeed(float moveSpeed)
- L155-L161  method SetAbility  public void SetAbility(AbilityConfig? ability)
- L164-L164  variable AbilityRange  public float AbilityRange => _ability?.Range ?? 0f;
- L167-L168  variable AbilityCooldownTicks  public int AbilityCooldownTicks =>
- L174-L174  variable CooldownRemainingTicks  public int CooldownRemainingTicks => _cooldownRemainingTicks;
- L177-L177  variable IsAbilityReady  public bool IsAbilityReady => _cooldownRemainingTicks <= 0;
- L182-L188  method AdvanceCooldown  public void AdvanceCooldown()
- L197-L213  method TryStartAbility  public bool TryStartAbility(uint sequence)
- L216-L216  variable Tolerance  public float Tolerance { get; }
- L219-L219  variable Position  public Vector3 Position => _position;
- L222-L222  variable Reconciliations  public int Reconciliations { get; private set; }
- L225-L225  variable LastError  public float LastError { get; private set; }
- L228-L228  variable Unacknowledged  public IReadOnlyList<ICommand> Unacknowledged => _unacknowledged;
- L236-L253  method ApplyLocalCommand  public void ApplyLocalCommand(ICommand command)
- L266-L360  method Reconcile  public bool Reconcile(
- L364-L372  method Reset  public void Reset(Vector3 position)
- L374-L403  method Step  private Vector3 Step(Vector3 position, ICommand command)
- L405-L418  method TryFindOwnEntity  private bool TryFindOwnEntity(SimulationSnapshot snapshot, out EntitySnapshot entity)
- L422-L448  struct PredictionReconciliation  public readonly struct PredictionReconciliation
- L429-L435  method PredictionReconciliation  public PredictionReconciliation(uint tick, float error, Vector3 predicted, Vector3 server)
- L438-L438  variable Tick  public uint Tick { get; }
- L441-L441  variable Error  public float Error { get; }
- L444-L444  variable Predicted  public Vector3 Predicted { get; }
- L447-L447  variable Server  public Vector3 Server { get; }
```

## Как пользоваться

1. Нужен ответ про ClientPrediction — читайте здесь, а не src/Client.Network/ClientPrediction.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py ClientPrediction`.
