# UnityInputSource

**Файл:** `unity-client/Assets/Scripts/Input/UnityInputSource.cs`  
**Размер:** 424 строк ≈ 5 144 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Ввод: WASD, клик-к-цели, способность, сброс при смерти/респавне.

**Из кода:** Ввод: WASD + ПКМ (движение к курсору) → <see cref="MoveCommand"/>; Space/ЛКМ → <see cref="AbilityCommand"/> (M9/M10.10). Атака работает как авто-атака: пока клавиша/кнопка удерживается, на каждом кадре выставляется флаг, а клиентский тик отправляет способность, как только кулдаун равен 0 и текущий удар уже доиграл. Отпускание прекращает новые команды; короткий тап не теряется между тиками. Движение и атака не блокируют друг друга.

## API (детерминированно из графа trail)

```
- L19-L423  class UnityInputSource  public sealed class UnityInputSource : IInputSource
- L88-L120  method UnityInputSource  public UnityInputSource(
- L132-L135  method SetScriptedCommandProvider  public void SetScriptedCommandProvider(Func<uint, ICommand?>? provider)
- L141-L144  method CancelPointerDestination  public void CancelPointerDestination()
- L160-L165  method ResetHeldInput  public void ResetHeldInput()
- L167-L190  method PollFrame  public void PollFrame()
- L193-L258  method TryReadCommand  public bool TryReadCommand(uint clientTick, out ICommand command)
- L260-L301  method ReadMovementDirection  private System.Numerics.Vector2 ReadMovementDirection()
- L312-L347  method TryApproachStep  private bool TryApproachStep(uint clientTick, out ICommand command)
- L349-L370  method PollMouseDestination  private void PollMouseDestination()
- L372-L392  method TryReadMouseDirection  private bool TryReadMouseDirection(out System.Numerics.Vector2 direction)
- L394-L418  method TryGroundPoint  private bool TryGroundPoint(out UnityVector3 point)
- L420-L420  method IsAttackPlaying  private bool IsAttackPlaying() => _isAttackPlaying != null && _isAttackPlaying();
- L422-L422  method IsAbilityReady  private bool IsAbilityReady() => _isAbilityReady == null || _isAbilityReady();
```

## Как пользоваться

1. Нужен ответ про UnityInputSource — читайте здесь, а не unity-client/Assets/Scripts/Input/UnityInputSource.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py UnityInputSource`.
