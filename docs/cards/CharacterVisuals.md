# CharacterVisuals

**Файл:** `unity-client/Assets/Scripts/View/CharacterVisuals.cs`  
**Размер:** 787 строк ≈ 8 823 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Анимация воина: состояния, гистерезис, экипировка.

**Из кода:** Presentation adapter for an imported humanoid prefab. It does not own movement, combat or inventory state: it only turns snapshots into Animator parameters and mounts authored weapon prefabs on hand bones. Put it on any character prefab (or let <see cref="CharacterVisualSettings"/> add it at runtime).

## API (детерминированно из графа trail)

```
- L13-L786  class CharacterVisuals  [DisallowMultipleComponent]
- L107-L115  class WeaponBinding  [Serializable]
- L118-L118  variable Animator  public Animator Animator => animator;
- L121-L121  variable Facing  public Transform Facing => facing != null ? facing : transform;
- L124-L124  variable Equipped  public WeaponKind Equipped => _equipped;
- L127-L127  variable LastMotionSpeed  public float LastMotionSpeed { get; private set; }
- L130-L130  variable LastRequestedState  public string LastRequestedState { get; private set; } = string.Empty;
- L131-L131  variable LastRequestedStateExists  public bool LastRequestedStateExists { get; private set; }
- L133-L133  variable WalkStateName  public string WalkStateName => walkState;
- L134-L134  variable RunStateName  public string RunStateName => runState;
- L135-L135  variable AttackStateName  public string AttackStateName => string.IsNullOrEmpty(_lastAttackState) ? attackState : _lastAttackState;
- L138-L149  variable EquippedTransform  public Transform EquippedTransform
- L152-L163  variable ActiveWeaponSlotCount  public int ActiveWeaponSlotCount
- L166-L179  variable VisibleEmbeddedEquipmentCount  public int VisibleEmbeddedEquipmentCount
- L182-L189  variable RightHandSocket  public Transform RightHandSocket
- L192-L199  variable LeftHandSocket  public Transform LeftHandSocket
- L202-L202  variable IsAttackPlaying  public bool IsAttackPlaying => Time.time < _attackUntil;
- L204-L210  method Awake  private void Awake()
- L213-L217  method SetWeapons  public void SetWeapons(WeaponBinding[] bindings)
- L224-L233  method SetStateNames  public void SetStateNames(string idle, string walk, string run, string attack,
- L236-L254  method Initialize  public void Initialize(Transform facingRoot, Animator targetAnimator,
- L257-L261  method BindSockets  public void BindSockets(Animator targetAnimator)
- L264-L325  method SetMotion  public void SetMotion(Vector3 velocity, bool dead, bool reset, float deltaTime)
- L342-L370  method PlayLocomotionWithHysteresis  private void PlayLocomotionWithHysteresis(float speed)
- L373-L381  method Face  public void Face(Vector3 direction)
- L384-L400  method PlayAttack  public void PlayAttack()
- L406-L422  method NextAttackState  private string NextAttackState()
- L424-L446  method ClipLength  private float ClipLength(string stateName)
- L449-L465  method PlayHit  public void PlayHit()
- L472-L500  method Equip  public void Equip(WeaponKind kind)
- L515-L521  method RestorePose  private void RestorePose()
- L536-L560  class PoseWatch  private sealed class PoseWatch : MonoBehaviour
- L541-L545  method Configure  public void Configure(CharacterVisuals owner, float seconds)
- L547-L559  method LateUpdate  private void LateUpdate()
- L563-L573  method WatchPose  private void WatchPose()
- L575-L625  method BuildWeaponSlots  private void BuildWeaponSlots()
- L627-L635  method ClearWeaponSlots  private void ClearWeaponSlots()
- L638-L654  method HideEmbeddedEquipment  private void HideEmbeddedEquipment()
- L656-L665  method IsMountedWeapon  private static bool IsMountedWeapon(Transform transform)
- L667-L674  method IsEmbeddedEquipmentName  private static bool IsEmbeddedEquipmentName(string objectName)
- L677-L692  method EquipFromArchetype  public void EquipFromArchetype(string archetype)
- L694-L702  method FindBinding  private WeaponBinding FindBinding(WeaponKind kind)
- L704-L714  method ResolveSockets  private void ResolveSockets()
- L716-L722  method ResolveRightHand  private Transform ResolveRightHand()
- L724-L736  method FindSocket  private Transform FindSocket(params string[] names)
- L738-L751  method PlayState  private void PlayState(string state, float fade, bool restart)
- L753-L757  method SetFloat  private void SetFloat(int parameter, float value)
- L759-L763  method SetBool  private void SetBool(int parameter, bool value)
- L765-L769  method SetTrigger  private void SetTrigger(int parameter)
- L771-L775  method ResetTrigger  private void ResetTrigger(int parameter)
- L777-L785  method HasParameter  private bool HasParameter(int hash, AnimatorControllerParameterType type)
```

## Как пользоваться

1. Нужен ответ про CharacterVisuals — читайте здесь, а не unity-client/Assets/Scripts/View/CharacterVisuals.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py CharacterVisuals`.
