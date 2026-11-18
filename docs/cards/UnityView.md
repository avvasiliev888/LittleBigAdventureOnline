# UnityView

**Файл:** `unity-client/Assets/Scripts/View/UnityView.cs`  
**Размер:** 688 строк ≈ 8 270 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Отрисовка: интерполяция снапшотов, скорость для анимации, оружие.

**Из кода:** Представление мира в Unity (M6, M7): держит буфер снапшотов, интерполирует позиции между двумя последними снапшотами и двигает корни персонажей. Своей логики движения здесь нет: чужие сущности идут из снапшотов, игрок — из предсказания клиента (M7), которое сглаживается, чтобы коррекции реконсиляции не выглядели телепортом.

## API (детерминированно из графа trail)

```
- L15-L687  class UnityView  public sealed class UnityView : IPredictedView
- L37-L57  class EntityVisual  private sealed class EntityVisual
- L85-L92  method UnityView  public UnityView(Transform root, ClientContent? content = null)
- L95-L95  variable PlayerEntityId  public uint PlayerEntityId { get; set; }
- L98-L98  variable Player  public Transform Player { get; private set; }
- L104-L124  variable PlayerForward  public Vector3 PlayerForward
- L127-L127  variable SnapshotsApplied  public int SnapshotsApplied { get; private set; }
- L130-L130  variable VisualEntityCount  public int VisualEntityCount => _entities.Count;
- L133-L133  variable CorpseVisualCount  public int CorpseVisualCount => _corpses.Count;
- L136-L139  method GetCharacterVisual  public CharacterVisuals? GetCharacterVisual(uint entityId)
- L142-L142  variable LatestSnapshot  public SimulationSnapshot? LatestSnapshot => _latestSnapshot;
- L145-L145  variable LastMobAttackDebug  public string LastMobAttackDebug { get; private set; } = string.Empty;
- L148-L148  variable LastReconciliationCorrected  public bool LastReconciliationCorrected { get; private set; }
- L151-L151  variable PredictedPosition  public Vector3 PredictedPosition => _predictedPosition;
- L154-L163  method SetPredictedPlayerPosition  public void SetPredictedPlayerPosition(uint tick, System.Numerics.Vector3 position, System.Numerics.Vector3 velocity, bool reconciled)
- L166-L176  method ApplySnapshot  public void ApplySnapshot(SimulationSnapshot snapshot)
- L179-L228  method Render  public void Render(float frameDeltaTime)
- L242-L268  method RetireMissingEntities  private void RetireMissingEntities()
- L271-L309  method ApplyCorpses  private void ApplyCorpses()
- L311-L314  method CreateCorpse  private GameObject CreateCorpse(uint corpseId)
- L316-L428  method ApplyEntity  private void ApplyEntity(InterpolatedEntity entity, float deltaTime, bool renderCatchUp)
- L435-L459  method TriggerAttack  public void TriggerAttack(uint targetEntityId)
- L462-L485  method PlayCombatEvent  public void PlayCombatEvent(CombatEvent evt)
- L488-L512  method FaceAttackTarget  private Vector3 FaceAttackTarget(EntityVisual attacker, uint targetEntityId)
- L515-L524  method AttackOrigin  private static Vector3 AttackOrigin(EntityVisual state)
- L526-L539  method SpawnProjectile  private static void SpawnProjectile(EntityVisual state, Vector3 targetPosition)
- L542-L545  method ShowClickMarker  public void ShowClickMarker(Vector3 position)
- L548-L551  method TriggerGather  public void TriggerGather(Vector3 position)
- L554-L561  method PlayDamage  public void PlayDamage(uint entityId)
- L567-L574  method EquipWeapon  public void EquipWeapon(WeaponKind kind)
- L580-L606  method PlayMobAttackNear  private void PlayMobAttackNear(Vector3 playerPosition)
- L608-L620  method ApplyWeapon  private void ApplyWeapon(EntityVisual state, InterpolatedEntity entity)
- L632-L644  method ConsumePlayerRespawnEdge  public bool ConsumePlayerRespawnEdge()
- L646-L655  method IsPlayerDeadInLatestSnapshot  private bool IsPlayerDeadInLatestSnapshot()
- L661-L670  variable IsPlayerAttackPlaying  public bool IsPlayerAttackPlaying
- L672-L673  method IsFinite  private static bool IsFinite(Vector3 value) =>
- L675-L675  method CreateCharacter  private Transform CreateCharacter(uint entityId) => _visualSettings.Create(_root, entityId);
- L677-L685  method CreateMob  private Transform CreateMob(uint actorId)
```

## Как пользоваться

1. Нужен ответ про UnityView — читайте здесь, а не unity-client/Assets/Scripts/View/UnityView.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py UnityView`.
