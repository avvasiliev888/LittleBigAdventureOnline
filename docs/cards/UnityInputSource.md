# UnityInputSource

**Файл:** `unity-client/Assets/Scripts/Input/UnityInputSource.cs`  
**Размер:** 561 строк ≈ 6 959 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Ввод: WASD, клик-к-цели, способность, сброс при смерти/респавне.

**Из кода:** Ввод: WASD + ПКМ (движение к курсору) → <see cref="MoveCommand"/>; Space/ЛКМ → <see cref="AbilityCommand"/> (M9/M10.10). Атака работает как авто-атака: пока клавиша/кнопка удерживается, на каждом кадре выставляется флаг, а клиентский тик отправляет способность, как только кулдаун равен 0 и текущий удар уже доиграл. Отпускание прекращает новые команды; короткий тап не теряется между тиками. Движение и атака не блокируют друг друга.

## API (детерминированно из графа trail)

```
- L19-L560  class UnityInputSource  public sealed class UnityInputSource : IInputSource
- L101-L135  method UnityInputSource  public UnityInputSource(
- L147-L150  method SetScriptedCommandProvider  public void SetScriptedCommandProvider(Func<uint, ICommand?>? provider)
- L156-L159  method CancelPointerDestination  public void CancelPointerDestination()
- L175-L186  method ResetHeldInput  public void ResetHeldInput()
- L188-L258  method PollFrame  public void PollFrame()
- L261-L341  method TryReadCommand  public bool TryReadCommand(uint clientTick, out ICommand command)
- L348-L374  method KeyboardDirection  private System.Numerics.Vector2 KeyboardDirection()
- L381-L386  method CancelAttack  private void CancelAttack()
- L388-L406  method ReadMovementDirection  private System.Numerics.Vector2 ReadMovementDirection()
- L417-L453  method TryApproachStep  private bool TryApproachStep(uint clientTick, out ICommand command)
- L460-L463  method IsStopped  private bool IsStopped()
- L469-L473  method TryGetCursorPoint  public bool TryGetCursorPoint(out UnityVector3 point)
- L480-L507  method PollMouseDestination  private bool PollMouseDestination()
- L509-L529  method TryReadMouseDirection  private bool TryReadMouseDirection(out System.Numerics.Vector2 direction)
- L531-L555  method TryGroundPoint  private bool TryGroundPoint(out UnityVector3 point)
- L557-L557  method IsAttackPlaying  private bool IsAttackPlaying() => _isAttackPlaying != null && _isAttackPlaying();
- L559-L559  method IsAbilityReady  private bool IsAbilityReady() => _isAbilityReady == null || _isAbilityReady();
```

## Как пользоваться

1. Нужен ответ про UnityInputSource — читайте здесь, а не unity-client/Assets/Scripts/Input/UnityInputSource.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py UnityInputSource`.
