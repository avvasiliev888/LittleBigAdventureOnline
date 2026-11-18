# InputValidator

**Файл:** `src/Server/InputValidator.cs`  
**Размер:** 524 строк ≈ 5 588 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Серверная валидация команд: скорость, кулдаун, дистанция.

**Из кода:** Причина отказа команды (M4). Отказ — это не «просто лог»: команда не попадает в симуляцию.

## API (детерминированно из графа trail)

```
- L5-L524  module Server  namespace Server
- L8-L99  enum CommandRejectReason  public enum CommandRejectReason
- L102-L132  struct ValidationResult  public readonly struct ValidationResult
- L104-L109  method ValidationResult  private ValidationResult(bool isValid, CommandRejectReason reason, string detail)
- L112-L112  variable IsValid  public bool IsValid { get; }
- L115-L115  variable Reason  public CommandRejectReason Reason { get; }
- L118-L118  variable Detail  public string Detail { get; }
- L122-L122  method Accept  public static ValidationResult Accept() => new ValidationResult(true, CommandRejectReason.None, string.Empty);
- L128-L128  method Reject  public static ValidationResult Reject(CommandRejectReason reason, string detail) => new ValidationResult(false, reason, detail);
- L131-L131  method ToString  public override string ToString() => IsValid ? "ok" : Reason + " (" + Detail + ")";
- L138-L523  class InputValidator  public sealed class InputValidator
- L147-L151  method InputValidator  public InputValidator(SimulationConfig config, ServerOptions options)
- L160-L230  method Validate  public ValidationResult Validate(
- L232-L252  method ValidateTeleport  private ValidationResult ValidateTeleport(TeleportCommand teleport)
- L254-L280  method ValidateAbility  private ValidationResult ValidateAbility(uint tick, EntityState caster, AbilityCommand ability)
- L282-L314  method ValidateEquip  private ValidationResult ValidateEquip(EntityState caster, EquipCommand command)
- L316-L343  method ValidateCraft  private ValidationResult ValidateCraft(EntityState caster, CraftCommand command)
- L345-L384  method ValidateBuy  private ValidationResult ValidateBuy(EntityState caster, BuyCommand command)
- L386-L419  method ValidateSell  private ValidationResult ValidateSell(EntityState caster, SellCommand command)
- L421-L438  method ValidateLoot  private ValidationResult ValidateLoot(SimulationState state, EntityState caster, LootCommand command)
- L440-L451  method FindTrader  private NpcTrader? FindTrader(uint traderId)
- L453-L458  method IsNear  private static bool IsNear(Vector3 left, Vector3 right, float range)
- L460-L478  method CanCraftOutput  private static bool CanCraftOutput(Inventory inventory, Recipe recipe)
- L480-L522  method ValidateGather  private ValidationResult ValidateGather(uint tick, EntityState caster, GatherCommand command)
```

## Как пользоваться

1. Нужен ответ про InputValidator — читайте здесь, а не src/Server/InputValidator.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py InputValidator`.
