# UnityInputSource

**Файл:** `unity-client/Assets/Scripts/Input/UnityInputSource.cs`  
**Размер:** 754 строк ≈ 9 359 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Ввод: WASD, клик-к-цели, способность, сброс при смерти/респавне.

**Из кода:** What the client knows about a locked target right now. /// Three different situations used to collapse into "not found", and they need three different reactions: a corpse ends the fight, a target that vanished from a delta tick must be ridden out, and a missing dependency must change nothing at all.

## API (детерминированно из графа trail)

```
- L19-L29  enum AbilityTargetStatus  public enum AbilityTargetStatus
- L38-L753  class UnityInputSource  public sealed class UnityInputSource : IInputSource
- L149-L185  method UnityInputSource  public UnityInputSource(
- L197-L200  method SetScriptedCommandProvider  public void SetScriptedCommandProvider(Func<uint, ICommand?>? provider)
- L206-L209  method CancelPointerDestination  public void CancelPointerDestination()
- L225-L232  method ResetHeldInput  public void ResetHeldInput()
- L234-L314  method PollFrame  public void PollFrame()
- L317-L436  method TryReadCommand  public bool TryReadCommand(uint clientTick, out ICommand command)
- L443-L474  method ReadPlainMovement  private bool ReadPlainMovement(uint clientTick, out ICommand command)
- L481-L507  method KeyboardDirection  private System.Numerics.Vector2 KeyboardDirection()
- L517-L520  method CancelChase  private void CancelChase()
- L527-L539  method ReleaseEngagement  private void ReleaseEngagement(bool clearDestination)
- L546-L549  method TargetStatus  private AbilityTargetStatus TargetStatus(uint targetId)
- L556-L573  method IsWithinReach  private bool IsWithinReach(uint targetId)
- L575-L593  method ReadMovementDirection  private System.Numerics.Vector2 ReadMovementDirection()
- L604-L631  method TryApproachStep  private bool TryApproachStep(uint clientTick, out ICommand command)
- L638-L641  method IsStopped  private bool IsStopped()
- L651-L651  variable EngagedTarget  public uint EngagedTarget => _engagedTarget;
- L657-L661  method TryGetCursorPoint  public bool TryGetCursorPoint(out UnityVector3 point)
- L677-L700  method PollMouseDestination  private bool PollMouseDestination()
- L702-L722  method TryReadMouseDirection  private bool TryReadMouseDirection(out System.Numerics.Vector2 direction)
- L724-L748  method TryGroundPoint  private bool TryGroundPoint(out UnityVector3 point)
- L750-L750  method IsAttackPlaying  private bool IsAttackPlaying() => _isAttackPlaying != null && _isAttackPlaying();
- L752-L752  method IsAbilityReady  private bool IsAbilityReady() => _isAbilityReady == null || _isAbilityReady();
```

## Как пользоваться

1. Нужен ответ про UnityInputSource — читайте здесь, а не unity-client/Assets/Scripts/Input/UnityInputSource.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py UnityInputSource`.
