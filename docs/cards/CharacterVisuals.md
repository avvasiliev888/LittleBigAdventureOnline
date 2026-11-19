# CharacterVisuals

**Файл:** `unity-client/Assets/Scripts/View/CharacterVisuals.cs`  
**Размер:** 890 строк ≈ 10 081 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Анимация воина: состояния, гистерезис, экипировка.

**Из кода:** Presentation adapter for an imported humanoid prefab. It does not own movement, combat or inventory state: it only turns snapshots into Animator parameters and mounts authored weapon prefabs on hand bones. Put it on any character prefab (or let <see cref="CharacterVisualSettings"/> add it at runtime).

## API (детерминированно из графа trail)

```
- L13-L889  class CharacterVisuals  [DisallowMultipleComponent]
- L121-L129  class WeaponBinding  [Serializable]
- L132-L132  variable Animator  public Animator Animator => animator;
- L135-L135  variable Facing  public Transform Facing => facing != null ? facing : transform;
- L138-L138  variable Equipped  public WeaponKind Equipped => _equipped;
- L141-L141  variable LastMotionSpeed  public float LastMotionSpeed { get; private set; }
- L144-L144  variable LastRequestedState  public string LastRequestedState { get; private set; } = string.Empty;
- L145-L145  variable LastRequestedStateExists  public bool LastRequestedStateExists { get; private set; }
- L147-L147  variable WalkStateName  public string WalkStateName => walkState;
- L148-L148  variable RunStateName  public string RunStateName => runState;
- L149-L149  variable AttackStateName  public string AttackStateName => string.IsNullOrEmpty(_lastAttackState) ? attackState : _lastAttackState;
- L152-L163  variable EquippedTransform  public Transform EquippedTransform
- L166-L177  variable ActiveWeaponSlotCount  public int ActiveWeaponSlotCount
- L180-L193  variable VisibleEmbeddedEquipmentCount  public int VisibleEmbeddedEquipmentCount
- L196-L203  variable RightHandSocket  public Transform RightHandSocket
- L206-L213  variable LeftHandSocket  public Transform LeftHandSocket
- L216-L216  variable IsAttackPlaying  public bool IsAttackPlaying => Time.time < _attackUntil;
- L219-L219  variable IsTurning  public bool IsTurning => _turning;
- L226-L238  variable YawOffsetDeg  public float YawOffsetDeg
- L241-L245  variable TurnSeconds  public float TurnSeconds
- L248-L257  variable BowYawOffsetDeg  public float BowYawOffsetDeg
- L259-L265  method Awake  private void Awake()
- L268-L272  method SetWeapons  public void SetWeapons(WeaponBinding[] bindings)
- L279-L288  method SetStateNames  public void SetStateNames(string idle, string walk, string run, string attack,
- L291-L309  method Initialize  public void Initialize(Transform facingRoot, Animator targetAnimator,
- L312-L316  method BindSockets  public void BindSockets(Animator targetAnimator)
- L319-L393  method SetMotion  public void SetMotion(Vector3 velocity, bool dead, bool reset, float deltaTime)
- L410-L438  method PlayLocomotionWithHysteresis  private void PlayLocomotionWithHysteresis(float speed)
- L441-L450  method Face  public void Face(Vector3 direction)
- L461-L476  method TurnTo  public void TurnTo(Vector3 direction, float yawOffsetDeg = 0f)
- L479-L484  method ApplyFacingQuaternion  private void ApplyFacingQuaternion(float baseYaw, bool remember = false)
- L487-L503  method PlayAttack  public void PlayAttack()
- L509-L525  method NextAttackState  private string NextAttackState()
- L527-L549  method ClipLength  private float ClipLength(string stateName)
- L552-L568  method PlayHit  public void PlayHit()
- L575-L603  method Equip  public void Equip(WeaponKind kind)
- L618-L624  method RestorePose  private void RestorePose()
- L639-L663  class PoseWatch  private sealed class PoseWatch : MonoBehaviour
- L644-L648  method Configure  public void Configure(CharacterVisuals owner, float seconds)
- L650-L662  method LateUpdate  private void LateUpdate()
- L666-L676  method WatchPose  private void WatchPose()
- L678-L728  method BuildWeaponSlots  private void BuildWeaponSlots()
- L730-L738  method ClearWeaponSlots  private void ClearWeaponSlots()
- L741-L757  method HideEmbeddedEquipment  private void HideEmbeddedEquipment()
- L759-L768  method IsMountedWeapon  private static bool IsMountedWeapon(Transform transform)
- L770-L777  method IsEmbeddedEquipmentName  private static bool IsEmbeddedEquipmentName(string objectName)
- L780-L795  method EquipFromArchetype  public void EquipFromArchetype(string archetype)
- L797-L805  method FindBinding  private WeaponBinding FindBinding(WeaponKind kind)
- L807-L817  method ResolveSockets  private void ResolveSockets()
- L819-L825  method ResolveRightHand  private Transform ResolveRightHand()
- L827-L839  method FindSocket  private Transform FindSocket(params string[] names)
- L841-L854  method PlayState  private void PlayState(string state, float fade, bool restart)
- L856-L860  method SetFloat  private void SetFloat(int parameter, float value)
- L862-L866  method SetBool  private void SetBool(int parameter, bool value)
- L868-L872  method SetTrigger  private void SetTrigger(int parameter)
- L874-L878  method ResetTrigger  private void ResetTrigger(int parameter)
- L880-L888  method HasParameter  private bool HasParameter(int hash, AnimatorControllerParameterType type)
```

## Как пользоваться

1. Нужен ответ про CharacterVisuals — читайте здесь, а не unity-client/Assets/Scripts/View/CharacterVisuals.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py CharacterVisuals`.
