# UnityView

**Файл:** `unity-client/Assets/Scripts/View/UnityView.cs`  
**Размер:** 726 строк ≈ 8 747 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Отрисовка: интерполяция снапшотов, скорость для анимации, оружие.

**Из кода:** Представление мира в Unity (M6, M7): держит буфер снапшотов, интерполирует позиции между двумя последними снапшотами и двигает корни персонажей. Своей логики движения здесь нет: чужие сущности идут из снапшотов, игрок — из предсказания клиента (M7), которое сглаживается, чтобы коррекции реконсиляции не выглядели телепортом.

## API (детерминированно из графа trail)

```
- L15-L725  class UnityView  public sealed class UnityView : IPredictedView
- L37-L57  class EntityVisual  private sealed class EntityVisual
- L85-L92  method UnityView  public UnityView(Transform root, ClientContent? content = null)
- L95-L95  variable PlayerEntityId  public uint PlayerEntityId { get; set; }
- L98-L98  variable Player  public Transform Player { get; private set; }
- L104-L124  variable PlayerForward  public Vector3 PlayerForward
- L127-L127  variable SnapshotsApplied  public int SnapshotsApplied { get; private set; }
- L130-L130  variable VisualEntityCount  public int VisualEntityCount => _entities.Count;
- L133-L133  variable CorpseVisualCount  public int CorpseVisualCount => _corpses.Count;
- L136-L139  method GetCharacterVisual  public CharacterVisuals? GetCharacterVisual(uint entityId)
- L145-L150  method GetEntityPosition  public Vector3? GetEntityPosition(uint entityId)
- L153-L153  variable LatestSnapshot  public SimulationSnapshot? LatestSnapshot => _latestSnapshot;
- L156-L156  variable LastMobAttackDebug  public string LastMobAttackDebug { get; private set; } = string.Empty;
- L159-L159  variable LastReconciliationCorrected  public bool LastReconciliationCorrected { get; private set; }
- L162-L162  variable PredictedPosition  public Vector3 PredictedPosition => _predictedPosition;
- L165-L174  method SetPredictedPlayerPosition  public void SetPredictedPlayerPosition(uint tick, System.Numerics.Vector3 position, System.Numerics.Vector3 velocity, bool reconciled)
- L177-L187  method ApplySnapshot  public void ApplySnapshot(SimulationSnapshot snapshot)
- L190-L239  method Render  public void Render(float frameDeltaTime)
- L253-L279  method RetireMissingEntities  private void RetireMissingEntities()
- L282-L320  method ApplyCorpses  private void ApplyCorpses()
- L322-L325  method CreateCorpse  private GameObject CreateCorpse(uint corpseId)
- L327-L439  method ApplyEntity  private void ApplyEntity(InterpolatedEntity entity, float deltaTime, bool renderCatchUp)
- L446-L470  method TriggerAttack  public void TriggerAttack(uint targetEntityId)
- L473-L496  method PlayCombatEvent  public void PlayCombatEvent(CombatEvent evt)
- L499-L529  method FaceAttackTarget  private Vector3 FaceAttackTarget(EntityVisual attacker, uint targetEntityId)
- L536-L545  method AttackOrigin  private static Vector3 AttackOrigin(EntityVisual state)
- L551-L562  method ProjectileOrigin  private static Vector3 ProjectileOrigin(EntityVisual state, Vector3 targetPosition)
- L564-L577  method SpawnProjectile  private static void SpawnProjectile(EntityVisual state, Vector3 targetPosition)
- L580-L583  method ShowClickMarker  public void ShowClickMarker(Vector3 position)
- L586-L589  method TriggerGather  public void TriggerGather(Vector3 position)
- L592-L599  method PlayDamage  public void PlayDamage(uint entityId)
- L605-L612  method EquipWeapon  public void EquipWeapon(WeaponKind kind)
- L618-L644  method PlayMobAttackNear  private void PlayMobAttackNear(Vector3 playerPosition)
- L646-L658  method ApplyWeapon  private void ApplyWeapon(EntityVisual state, InterpolatedEntity entity)
- L670-L682  method ConsumePlayerRespawnEdge  public bool ConsumePlayerRespawnEdge()
- L684-L693  method IsPlayerDeadInLatestSnapshot  private bool IsPlayerDeadInLatestSnapshot()
- L699-L708  variable IsPlayerAttackPlaying  public bool IsPlayerAttackPlaying
- L710-L711  method IsFinite  private static bool IsFinite(Vector3 value) =>
- L713-L713  method CreateCharacter  private Transform CreateCharacter(uint entityId) => _visualSettings.Create(_root, entityId);
- L715-L723  method CreateMob  private Transform CreateMob(uint actorId)
```

## Как пользоваться

1. Нужен ответ про UnityView — читайте здесь, а не unity-client/Assets/Scripts/View/UnityView.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py UnityView`.
