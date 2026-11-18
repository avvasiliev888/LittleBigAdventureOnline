# UnityInputSource

**Файл:** `unity-client/Assets/Scripts/Input/UnityInputSource.cs`  
**Размер:** 357 строк ≈ 4 342 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Ввод: WASD, клик-к-цели, способность, сброс при смерти/респавне.

**Из кода:** Ввод: WASD + ПКМ (движение к курсору) → <see cref="MoveCommand"/>; Space/ЛКМ → <see cref="AbilityCommand"/> (M9/M10.10). Атака работает как авто-атака: пока клавиша/кнопка удерживается, на каждом кадре выставляется флаг, а клиентский тик отправляет способность, как только кулдаун равен 0 и текущий удар уже доиграл. Отпускание прекращает новые команды; короткий тап не теряется между тиками. Движение и атака не блокируют друг друга.

## API (детерминированно из графа trail)

```
- L19-L356  class UnityInputSource  public sealed class UnityInputSource : IInputSource
- L84-L110  method UnityInputSource  public UnityInputSource(
- L122-L125  method SetScriptedCommandProvider  public void SetScriptedCommandProvider(Func<uint, ICommand?>? provider)
- L131-L134  method CancelPointerDestination  public void CancelPointerDestination()
- L150-L155  method ResetHeldInput  public void ResetHeldInput()
- L157-L180  method PollFrame  public void PollFrame()
- L183-L237  method TryReadCommand  public bool TryReadCommand(uint clientTick, out ICommand command)
- L239-L280  method ReadMovementDirection  private System.Numerics.Vector2 ReadMovementDirection()
- L282-L303  method PollMouseDestination  private void PollMouseDestination()
- L305-L325  method TryReadMouseDirection  private bool TryReadMouseDirection(out System.Numerics.Vector2 direction)
- L327-L351  method TryGroundPoint  private bool TryGroundPoint(out UnityVector3 point)
- L353-L353  method IsAttackPlaying  private bool IsAttackPlaying() => _isAttackPlaying != null && _isAttackPlaying();
- L355-L355  method IsAbilityReady  private bool IsAbilityReady() => _isAbilityReady == null || _isAbilityReady();
```

## Как пользоваться

1. Нужен ответ про UnityInputSource — читайте здесь, а не unity-client/Assets/Scripts/Input/UnityInputSource.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py UnityInputSource`.
