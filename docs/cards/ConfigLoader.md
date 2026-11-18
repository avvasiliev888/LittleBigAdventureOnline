# ConfigLoader

**Файл:** `src/Shared/ConfigLoader.cs`  
**Размер:** 1353 строк ≈ 15 166 токенов — читать целиком дорого, пользуйтесь этой карточкой.

**Назначение:** Загрузка и валидация JSON-конфигов (ADR-005).

**Из кода:** Исключение конфигурации: невалидный JSON, пропущенное поле, дубликат или неразрешённая ссылка. Сообщение обязано объяснять, что именно и где не так (ADR-005).

## API (детерминированно из графа trail)

```
- L10-L1353  module Shared  namespace Shared
- L16-L32  class ConfigValidationException  public sealed class ConfigValidationException : Exception
- L20-L23  method ConfigValidationException  public ConfigValidationException(string message)
- L28-L31  method ConfigValidationException  public ConfigValidationException(string message, Exception inner)
- L44-L1352  class ConfigLoader  public static class ConfigLoader
- L103-L121  method LoadFromDirectory  public static SimulationConfig LoadFromDirectory(string directory)
- L138-L218  method Load  public static SimulationConfig Load(
- L233-L379  method LoadFromJson  public static SimulationConfig LoadFromJson(
- L394-L412  method ComputeHash  public static string ComputeHash(string directory)
- L427-L510  method ComputeHashFromJson  public static string ComputeHashFromJson(
- L512-L516  method AppendSection  private static void AppendSection(MemoryStream stream, byte[] section)
- L518-L526  method ReadFile  private static string ReadFile(string path)
- L532-L540  method ReadOptionalFile  private static string ReadOptionalFile(string? path)
- L542-L573  method ParseArray  private static List<Dictionary<string, object?>> ParseArray(string json, string section, string source)
- L575-L595  method ParseObject  private static Dictionary<string, object?> ParseObject(string json, string section, string source)
- L597-L615  method ToSkillTree  private static SkillTree ToSkillTree(string json, string source)
- L617-L637  method ToSkillArchetype  private static SkillArchetype ToSkillArchetype(Dictionary<string, object?> dto, string source, int index)
- L639-L659  method ToSkillNode  private static SkillNode ToSkillNode(Dictionary<string, object?> dto, string archetypeLabel, int index)
- L661-L682  method ToResourceNode  private static ResourceNodeConfig ToResourceNode(Dictionary<string, object?> dto, string source, int index)
- L684-L701  method ToWorldCollider  private static WorldCollider ToWorldCollider(Dictionary<string, object?> dto, string source, int index)
- L703-L743  method ToActor  private static ActorConfig ToActor(Dictionary<string, object?> dto, string source, int index)
- L745-L779  method ToRecipe  private static Recipe ToRecipe(Dictionary<string, object?> dto, string source, int index)
- L781-L817  method ToTrader  private static NpcTrader ToTrader(Dictionary<string, object?> dto, string source, int index)
- L819-L901  method ToMob  private static MobDefinition ToMob(Dictionary<string, object?> dto, string source, int index)
- L904-L914  method ToPoints  private static List<Vector2> ToPoints(List<Dictionary<string, object?>> dtos, string label)
- L917-L938  method ToStarterItems  private static IReadOnlyList<LoadoutItem> ToStarterItems(Dictionary<string, object?> dto, string label)
- L941-L961  method ToStarterEquipment  private static IReadOnlyList<LoadoutEquipment> ToStarterEquipment(Dictionary<string, object?> dto, string label)
- L963-L1003  method ToZone  private static ZoneConfig ToZone(Dictionary<string, object?> dto, string source, int index)
- L1006-L1028  method ToZoneGates  private static List<ZoneGate> ToZoneGates(Dictionary<string, object?> dto, string label)
- L1030-L1062  method ToItem  private static ItemDefinition ToItem(Dictionary<string, object?> dto, string source, int index)
- L1064-L1076  method ToActorKind  private static ActorKind ToActorKind(string text, string label)
- L1078-L1092  method ToZoneKind  private static ZoneKind ToZoneKind(string text, string label)
- L1094-L1106  method ToItemKind  private static ItemKind ToItemKind(string text, string label)
- L1108-L1120  method ToSkillNodeKind  private static SkillNodeKind ToSkillNodeKind(string text, string label)
- L1122-L1139  method ToEquipmentSlot  private static EquipmentSlot ToEquipmentSlot(string text, string label)
- L1141-L1158  method ToAbility  private static AbilityConfig ToAbility(Dictionary<string, object?> dto, string source, int index)
- L1160-L1168  method RequireValue  private static object RequireValue(Dictionary<string, object?> dto, string label, string field)
- L1170-L1183  method RequireUint  private static uint RequireUint(Dictionary<string, object?> dto, string label, string field)
- L1185-L1198  method RequireInt  private static int RequireInt(Dictionary<string, object?> dto, string label, string field)
- L1200-L1213  method RequireFloat  private static float RequireFloat(Dictionary<string, object?> dto, string label, string field)
- L1215-L1225  method RequireString  private static string RequireString(Dictionary<string, object?> dto, string label, string field)
- L1227-L1237  method RequireBool  private static bool RequireBool(Dictionary<string, object?> dto, string label, string field)
- L1239-L1256  method OptionalInt  private static int OptionalInt(Dictionary<string, object?> dto, string label, string field, int fallback)
- L1258-L1275  method OptionalFloat  private static float OptionalFloat(Dictionary<string, object?> dto, string label, string field, float fallback)
- L1277-L1290  method OptionalString  private static string? OptionalString(Dictionary<string, object?> dto, string label, string field)
- L1293-L1305  method OptionalObjectArray  private static List<Dictionary<string, object?>> OptionalObjectArray(
- L1308-L1320  method RequireObjectArray  private static List<Dictionary<string, object?>> RequireObjectArray(
- L1322-L1351  method OptionalObjectArrayOrNull  private static List<Dictionary<string, object?>>? OptionalObjectArrayOrNull(
```

## Как пользоваться

1. Нужен ответ про ConfigLoader — читайте здесь, а не src/Shared/ConfigLoader.cs.
2. Нужна деталь конкретного метода — `trail ask "<вопрос>" --source`
   или `read` с `offset`/`limit` вокруг нужной строки.
3. Правка кода — обычным образом; после крупных изменений пересоберите
   карточку: `python tools/tokens/make_cards.py ConfigLoader`.
