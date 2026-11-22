# План для gemma — очередь задач конвейера «Тандем»

Этот файл читает исполнитель (gemma). Одна задача — один блок `##`. Конвейер берёт
**первую** задачу со статусом `- [ ]`. Статусы: `- [ ]` ожидает, `- [~]` в работе,
`- [x]` сделано и принято пользователем, `- [!]` блокер.

Правила нарезки и формат — `.agent/protocol.md` § 2, шаблон ниже. Если задача не влезает
в лимиты (≤2 файла, ≤150 строк) — дели на две, лимит не расширяй.

## Шаблон задачи

```markdown
## T-012 — короткое название глаголом

- [ ] статус: ожидает
- Цель: одно проверяемое поведение, одна строка.
- Файлы: `путь/файл.cs` (максимум 2 файла; чужие файлы не трогать)
- Контекст: классы/символы, которые надо прочитать (grep/trail, не весь файл)
- Ограничения: что нельзя менять (из AGENTS.md + локальные для этой задачи)
- Критерии приёмки: как проверить — тест, поведение, строка в логе
- Гейты: `dotnet build`, `dotnet test`, Unity compile
- Промпт для gemma: готовый текст задания, без домыслов и без «сделай красиво»
- Размер: ≤2 файла, ≤150 строк изменений
- Зависимости: T-00X или «нет»
```

## Пример (формат, не к исполнению)

## T-000 — Пример: переименовать приватное поле без смены поведения

- [ ] статус: ожидает
- Цель: поле `_moveSpped` в `ClientInput` называется `_moveSpeed`; поведение не меняется.
- Файлы: `unity-client/Assets/Scripts/Client/ClientInput.cs`
- Контекст: `ClientInput`, поля `_moveSpped` (объявление и все использования);
  найти через `grep -n "_moveSpped" unity-client/Assets/Scripts`.
- Ограничения: не менять логику, порядок полей и сериализацию; не трогать другие файлы.
- Критерии приёмки: `dotnet build` зелёный; `grep -rn "_moveSpped"` не находит ничего;
  `grep -n "_moveSpeed"` находит объявление и все использования.
- Гейты: `dotnet build`, `dotnet test`, Unity compile.
- Промпт для gemma:
  «В файле `unity-client/Assets/Scripts/Client/ClientInput.cs` переименуй приватное поле
  `_moveSpped` в `_moveSpeed` во всех местах этого файла. Логику, типы и порядок полей
  не меняй. Другие файлы не трогай. В конце перечисли изменённые строки.»
- Размер: 1 файл, ~5 строк.
- Зависимости: нет.

## Очередь

<!-- Новые задачи добавляются сюда. Первая задача с "- [ ]" уходит в конвейер. -->

## T-001 — Узаконить движение частиц в проверке границ

- [x] статус: принято (22.11.2026, слито в `main`; отчёт `docs/reports/vfx-boundary-gate.md`)
- Цель: `bash tools/ci/check-unity-boundaries.sh` снова даёт exit 0 на `main`, при этом
  правило 2 продолжает ловить движение в обычных View-скриптах.
- Файлы: `tools/ci/check-unity-boundaries.sh`
- Контекст: блок «== 2. View/Input не двигают объекты сами» — `grep -n "transform"
  tools/ci/check-unity-boundaries.sh`, строки 31–38. Причина падения:
  `unity-client/Assets/Scripts/View/FrostVfx.cs:369` — осколок льда летит по
  `transform.position += _velocity * deltaTime` в `Update`; это визуальные частицы,
  а не игровая сущность, позиция которой приходит из снапшота.
- Ограничения: правила 1, 3 и остальные не трогать; `.github/workflows/ci.yml` не
  менять; исключение только для файлов, чьё имя оканчивается на `Vfx.cs` — для
  игровых View-скриптов (`WarriorView.cs`, `UnityView.cs` и т.п.) проверка остаётся.
- Критерии приёмки: `bash tools/ci/check-unity-boundaries.sh` → exit 0 и печать
  `ok: прямого движения в View/Input нет`; `grep -n "Vfx" tools/ci/check-unity-boundaries.sh`
  показывает исключение; если в исключении убрать `grep -v`, скрипт снова падает на
  `FrostVfx.cs:369` (проверяется одной командой в dry-run).
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-001` (build, test, arch, Unity).
- Промпт для gemma:
  «В файле `tools/ci/check-unity-boundaries.sh` найди блок `== 2. View/Input не двигают
  объекты сами` — это `grep -RInE` по `transform.(position +=|Translate()` в папках
  `View` и `Input`. Добавь после вывода этого grep фильтр `| grep -vE '/([A-Za-z0-9_]+)Vfx\.cs:'`,
  чтобы визуальные эффекты (`*Vfx.cs`, например `FrostVfx.cs`) не считались движением
  игрового объекта. Перед строкой добавь комментарий на русском: почему частицы —
  не игровое состояние и почему исключены только `*Vfx.cs`. Другие правила и
  `.github/workflows/ci.yml` не трогай. В конце покажи изменённые строки.»
- Размер: 1 файл, ≤10 строк.
- Зависимости: нет.

## T-002 — Закрыть непокрытые строки `Simulation`: убрать мёртвые ветки и добавить один тест

- [x] статус: сделано конвейером (22.11.2026, PASS ревью, ветка `agent/T-002`, ждёт «принято»)
- Цель: `pwsh -File tools/tandem/gates.ps1 -Task T-002` даёт **ok=true целиком**, то есть
  `dotnet test` с `/p:Threshold=100` по `[Simulation.Core]Simulation.Core.Simulation`
  проходит. Исходный замер 22.11.2026: 480 тестов, покрытие 98,97% (6 непокрытых строк из 585).
- Файлы: `src/Tests/SimulationCoverageTests.cs` (новый) плюс **ровно три удаления** в
  `src/Simulation.Core/Simulation.cs`. Разрешение заказчика на правку прод-кода получено
  22.11.2026: удалять мёртвые ветки можно, понижать порог и писать тесты-пустышки — нельзя.
- Факты, добытые замерами (две неудачные попытки gemma и четыре пробы):

  1. `Simulation.cs:365` (строки 363–366, `if (!_config.Zones.TryGetZone(target, out _)) return;`)
     **недостижима легальным конфигом**: `ZoneMap.ValidateCoverage` отвергает и разрывы
     («площадь мира 600, суммарная площадь зон 400»), и перекрытия («Зоны 'a' и 'b' пересекаются»),
     а `ZoneConfig.Contains` включает обе границы. Валидный конфиг тайлит мир целиком, поэтому
     `TryGetZone` после `ClampToBounds` всегда находит зону. Дешёвый «обход» — щель тоньше
     допуска `max(1e-4, boxArea·1e-5)` — это фиксация артефакта, а не поведение; заказчик
     выбрал удаление.
  2. `1013–1015` (`MoveMobAway`, fallback `dx=1, dz=0, distance=1`) **достижимы** и закрываются
     одним честным тестом: моб стоит ровно в точке цели (`distance <= 0.0001`). Замер: тест
     зелёный, строки исполняются.
  3. `1031` (`if (direction.LengthSquared() <= 0.000001f) return;`) **недостижима**:
     `MobSteering.Resolve` возвращает `Vector3.Zero` только когда `destination == position`,
     а `MoveMobAway` всегда отступает на 2 единицы (`distance` не меньше 1 после fallback).
     Нулевого вектора на этом пути не бывает.
  4. `1055` (`if (targetDistance > definition.AttackRange) return;`) **недостижима**:
     `MobDefinition` сам запрещает `KeepDistance > AttackRange`
     («иначе моб отходит дальше, чем достаёт его удар»), а оба места вызова
     (`Retreat`: `targetDistance < KeepDistance ≤ AttackRange`; `Attack`: `targetDistance ≤ AttackRange`)
     гарантируют `targetDistance ≤ AttackRange`.

- Проверено на копии (22.11.2026): после удаления трёх ветвей и добавления одного теста
  `gates.ps1 -SkipUnity` даёт build PASS, test PASS «покрытие 100%», arch PASS, boundaries PASS,
  всего 484 теста. Значит цель достижима честно.
- Что удалить в `src/Simulation.Core/Simulation.cs` (ровно это и ничего больше):

  а) в `ApplyTeleport` — проверку зоны, оставив комментарий про инвариант:

```csharp
            // Телепорт — debug-инструмент, но границы мира остаются авторитетными:
            // валидатор уже отклонил точку вне мира, здесь дополнительно зажимаем.
            // Отдельная проверка «точка попала в зону» не нужна: ZoneMap.ValidateCoverage
            // гарантирует, что зоны без разрывов и пересечений покрывают мир целиком.
            var target = _config.Zones.ClampToBounds(
                new Vector3(command.Destination.X, entity.Position.Y, command.Destination.Y));
```

  б) в `MoveMobAway` — блок `if (direction.LengthSquared() <= 0.000001f) { return; }` целиком;

  в) в `TryMobAttack` — блок `if (targetDistance > definition.AttackRange) { return; }` целиком.
  Параметр `targetDistance` после этого не используется: оставь его, если сборка проходит без
  предупреждений; если компилятор ругается — убери параметр и из двух мест вызова.

- Тест (проверен пробой, можно брать как есть):

```csharp
using System.Linq;
using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Цель стоит вплотную: моб берёт запасное направление dx=1, dz=0, distance=1
    /// и всё равно отходит, а не остаётся делить точку с целью.
    /// </summary>
    public class SimulationCoverageTests
    {
        [Fact]
        public void MoveMobAway_TargetOnTopOfMob_RetreatsWithFallbackDirection()
        {
            var config = TestWorld.Config(mobs: new[]
            {
                TestWorld.Mob(id: 1, spawns: new[] { new Vector2(5f, 5f) },
                    attackRange: 5f, keepDistance: 4f, weaponType: "staff",
                    aggroRange: 10f, leashRange: 30f),
            });
            var player = TestWorld.Entity(1, config, 5f, 5f);
            var simulation = TestWorld.Simulation(config, player);
            Assert.Equal(1, simulation.SpawnMobs());
            var mob = simulation.State.Entities.Single(entity => entity.Mob != null);

            TestWorld.Tick(simulation);

            Assert.True(Vector3.Distance(mob.Position, new Vector3(5f, 0f, 5f)) > 0.9f);
        }
    }
}
```

- Ограничения: никаких других правок в `src/Simulation.Core/**`; не понижать `Threshold`,
  не добавлять `-SkipCoverage`, не отключать тесты, не коммитить файлы вне
  `src/Tests/**` и `src/Simulation.Core/Simulation.cs`.
- Критерии приёмки: `gates.ps1 -Task T-002` (полный, с покрытием) → `ok=true`, шаг `test` —
  «покрытие 100%»; `git diff --stat -- src/Simulation.Core` показывает только удаления
  (−10 строк, 0 добавлений кроме комментария); тестов больше 480.
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-002` (build → test с покрытием →
  arch → boundaries → Unity).
- Промпт для gemma:
  «Две неудачные попытки уже были, причина разобрана замерами и записана в этом блоке плана —
  прочитай `## T-002` целиком, там готовый диф. Кратко: три непокрытые ветки в
  `src/Simulation.Core/Simulation.cs` недостижимы по инвариантам (валидация зон, валидация
  `KeepDistance ≤ AttackRange`, невозможность нулевого steering), заказчик разрешил их удалить;
  четвёртая группа строк (1013–1015) закрывается одним тестом, код теста в плане готов.
  Удали три блока (в `ApplyTeleport`, `MoveMobAway`, `TryMobAttack`), оставь комментарий про
  инвариант зон, создай `src/Tests/SimulationCoverageTests.cs` с тестом
  `MoveMobAway_TargetOnTopOfMob_RetreatsWithFallbackDirection` из плана. Ничего другого не
  меняй, порог не трогай, тесты не отключай. Готово, когда
  `pwsh -File tools/tandem/gates.ps1 -Task T-002 -SkipUnity` даёт шаг test „покрытие 100% ».
- Размер: 1 новый файл тестов (~30 строк) + 3 удалённых блока в одном файле.
- Зависимости: нет.

## T-003 — `ValidateCoverage` должен проверять замощение, а не площадь

- [x] статус: принято 22.11.2026, слито в main (`42a67f0`, конвейер: 1 попытка, PASS ревью, риск low)
- Цель: `new ZoneMap(zones)` бросает `ArgumentException` («не покрывают мир») на конфиге со
  щелью тоньше допуска площади, при этом действующие зоны (`config/zones.json`,
  `ZoneFixture.ThreeZones()`) грузятся без исключения.
- Замер 23.11.2026 (проба в `src/Tests`, после замера удалена): мир 20×20, зоны
  `a = [0..10]×[0..20]`, `b = [10.0001..20]×[0..20]`. `new ZoneMap(zones)` **не бросает**:
  площадь мира 400, сумма зон 399.998, допуск `max(1e-4, 400·1e-5) = 0.004`, недостача
  0.002 проходит. При этом `map.TryGetZone(new Vector3(10.00005f, 0f, 5f), out _)` даёт
  **false** — точка в щели бесхозна. Валидатор утверждает «покрыто», а `TryGetZone` этого
  не подтверждает.
- Почему это не косметика: на инварианте «валидный конфиг покрывает мир целиком» T-002
  удалил проверку зоны в `ApplyTeleport` (`Simulation.cs:363–366`) и оставил комментарий,
  что `ValidateCoverage` гарантирует покрытие. Гарантия ложна, пока допуск площади конечен:
  любая щель тоньше `tolerance / длина` проходит.
- Файлы (только эти): `src/Simulation.Core/ZoneMap.cs` (`ValidateCoverage` и, при желании,
  один приватный хелпер ниже неё) и `src/Tests/ZoneMapTests.cs` (два теста).
- Что сделать — заменить проверку площади проверкой замощения полосами по X:

  1. Проверку площади с допуском можно оставить как быстрый отсев, но **не** как
     единственное условие покрытия.
  2. Собери все X-границы зон (`MinX`, `MaxX`), отсортируй, убери точные повторы.
  3. Для каждой полосы `[e[i], e[i+1]]`, где `e[i] < e[i+1]`:
     - собери Z-интервалы `[MinZ..MaxZ]` всех зон, у которых `MinX <= e[i]` и `MaxX >= e[i+1]`;
     - отсортируй интервалы по `MinZ`, веди `covered = MinZ` мира (`ComputeBounds()` уже
       выполнен до `ValidateCoverage()` — конструктор, строки 81–85);
     - интервал принимается, если `interval.MinZ <= covered`; тогда
       `covered = Math.Max(covered, interval.MaxZ)`;
     - если `interval.MinZ > covered` — разрыв внутри полосы, бросок;
     - после последнего интервала `covered` должен достичь `MaxZ` мира.
  4. Если хоть одна полоса не покрыта по Z целиком — `ArgumentException` с тем же текстом
     «Зоны не покрывают мир целиком: …».
  5. Сравнения координат — точные (float, пришедшие из одного JSON-числа, равны бит в бит).
     Допуск не расширять: именно конечный допуск и есть баг.

- Тесты (код проверен по формулировке, см. замер выше):

```csharp
[Fact]
public void SliverGapBetweenZones_Throws()
{
    var zones = new[]
    {
        new ZoneConfig(1, "a", ZoneKind.Yellow, 0f, 0f, 10f, 20f, true, false, 0.1f, 60f, 1, 2),
        new ZoneConfig(2, "b", ZoneKind.Yellow, 10.0001f, 0f, 20f, 20f, true, false, 0.1f, 60f, 1, 2),
    };

    var exception = Assert.Throws<ArgumentException>(() => new ZoneMap(zones));

    Assert.Contains("не покрывают мир", exception.Message);
}

[Fact]
public void StaggeredZones_CoveredByTwoZonesInStrip()
{
    var zones = new[]
    {
        new ZoneConfig(1, "a", ZoneKind.Blue, 0f, 0f, 10f, 20f, false, false, 0.1f, 0f, 1, 2),
        new ZoneConfig(2, "b", ZoneKind.Yellow, 10f, 0f, 20f, 10f, true, false, 0.1f, 60f, 1, 2),
        new ZoneConfig(3, "c", ZoneKind.Red, 10f, 10f, 20f, 20f, true, false, 0f, 90f, 3, 4),
    };

    var map = new ZoneMap(zones);

    Assert.Equal(20f, map.MaxX);
    Assert.Equal(20f, map.MaxZ);
}
```

- Ограничения: не менять `ValidateUniqueZones`, `ValidateNoOverlaps`, `Contains`, `TryGetZone`,
  `IsAdjacentOnX/Z`, `GateTolerance`; не трогать `config/zones.json` и `Simulation.cs`;
  не понижать `Threshold`, не добавлять `-SkipCoverage`, не отключать тесты.
- Критерии приёмки: `pwsh -File tools/tandem/gates.ps1 -Task T-003` → `ok=true`; новый
  `SliverGapBetweenZones_Throws` зелёный; `ConfigTests` и `ZoneFixture`-тесты зелёные
  (действующие зоны грузятся); `git diff --stat` — только `ZoneMap.cs` и `ZoneMapTests.cs`.
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-003` (build → test → arch → boundaries → Unity).
- Промпт для gemma: «Прочитай `## T-003` в `docs/plan_for_gemma.md` целиком. `ValidateCoverage`
  в `src/Simulation.Core/ZoneMap.cs:506` сравнивает только площади с допуском `max(1e-4,
  boxArea·1e-5)` и пропускает щель тоньше допуска: валидатор говорит „покрыто“, а `TryGetZone`
  для точки в щели возвращает false. Замени проверку площади проверкой замощения полосами по X
  по алгоритму из плана, добавь два теста из плана в `src/Tests/ZoneMapTests.cs`. Действующие
  зоны (`config/zones.json`, `ZoneFixture.ThreeZones()`) должны грузиться без исключения.
  Допуск не расширять, ничего другого не менять.»
- Размер: ~35 строк в `ValidateCoverage` + 2 теста.
- Зависимости: нет.

## T-004 — Импорт одного тела как публичный метод (подготовка окна)

- [x] статус: принято 22.11.2026, слито в main (`06eeee8`; конвейер: 1 попытка, PASS ревью, риск low)
- Цель: `MobBodyImport.ImportBody(...)` импортирует одно тело и возвращает результат;
  пункт меню `Albion/Mobs/Import Bodies From Sets` делает то же, что раньше, через этот метод.
- Файлы: `unity-client/Assets/Editor/MobBodyImport.cs` (единственный файл)
- Контекст: `Jobs` — массив из одной записи `("red_warlord", "Assets/WizardPBR/Mesh/WizardBodyMesh.fbx",
  "Assets/WizardPBR", "WizardBody", "magic", 2.45f)` (строка 50); цикл `Run()` (55–133):
  `CopyIntoArt` → `BuildPrefab` → `UpsertEntry` → `settings.ValidateMaterials()` (111).
  Все эти методы уже есть — вызовы не переписывать, только вынести в новый метод.
- Что сделать: добавить `public static bool ImportBody(string mobKey, string sourceFbx,
  string packageRoot, string bodyName, string set, float height, System.Text.StringBuilder report,
  List<MobVisualSettings.Entry> entries, out GameObject prefab)`. Тело цикла из `Run()` (копирование,
  сборка префаба, `UpsertEntry`) перенести туда без изменения порядка и текстов отчёта; в `Run()`
  остаётся цикл по `Jobs`, вызывающий `ImportBody`. Поведение и отчёт — байт в байт прежние.
- Ограничения: не менять `BuildPrefab`, `AssignPackageAlbedo`, `UpsertEntry`, `LoadSets`;
  не менять `Jobs`; не трогать `ValidateMaterials`; не менять формат отчёта (`entry set: ...`, `SKIP ...`).
- Критерии приёмки: `Unity.exe -batchmode -executeMethod Albion.Editor.MobBodyImport.Run -quit -logFile -`
  завершается с `EXIT=0`, в логе `entry set: red_warlord -> WizardBody h=2.45` и `ValidateMaterials: OK`;
  запись `red_warlord` в `MobVisuals.asset` остаётся одна (повторный прогон не плодит дубли).
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-004` (Unity-компиляция ловит ошибки CS).
- Промпт для gemma: «В `unity-client/Assets/Editor/MobBodyImport.cs` вынеси тело цикла из `Run()`
  (строки 55–133) в новый публичный метод `ImportBody(mobKey, sourceFbx, packageRoot, bodyName, set,
  height, report, entries, out prefab)` того же класса. `Run()` оставь циклом по массиву `Jobs`,
  который вызывает `ImportBody` и печатает те же строки отчёта. Ничего другого не меняй: ни `Jobs`,
  ни `BuildPrefab`, ни `AssignPackageAlbedo`, ни `UpsertEntry`, ни `ValidateMaterials`, ни тексты
  отчёта.»
- Размер: ~40 строк переноса внутри одного файла.
- Зависимости: нет.

## T-005 — CLI-вход и файл вердикта; новых мобов не создаём

- [x] статус: принято 22.11.2026, слито в main (`2844423`). Две попытки конвейера провалились:
  (1) Unity CS0117 `MiniJson.Serialize` + CS1061 `RuntimeAnimatorController.layers` + атрибут
  `[MenuItem]` уехал на CLI-метод; (2) код закрыл все шесть замечаний, но правка удалила две
  последние скобки файла (`CS1513`). Скобки дописаны вручную (баланс 92/92), полный
  `gates.ps1 -Task T-005` → `ok=true`, Unity-компиляция 19,4 с. CLI проверен замерами:
  `red_warlord` → `EXIT=0`, `docs/imports/WizardBody/verdict.json` (`ok=true, states=6,
  albedo=true, materials_ok=true`); `ghost_mob` → `EXIT=1` с ошибкой про ненайденный ключ.
  Ассеты `WizardBody` после прогона откачены: задача про код, тело вернётся при T-007.
  Отчёт: `docs/reports/t-005-cli-импорт-и-вердикт.md`
- Цель: в batchmode одной командой импортируется одно тело, а результат пишется в
  `docs/imports/<body>/verdict.json` и определяет код выхода.
- Файлы: `unity-client/Assets/Editor/MobBodyImport.cs` (единственный файл; `ImportBody` из T-004)
- Контекст: `ImportBody` (T-004), `settings.Entries` (`MobVisualSettings.Entry.MobKey`),
  `settings.ValidateMaterials()`, существующий batchmode-перехват в `Run()` (строки 117–129:
  при исключении `EditorApplication.Exit(1)`).
- Что сделать: добавить `using System.Globalization;` в шапку файла и вставить в **конец класса**
  (перед закрывающей `}`) два члена — метод и хелпер. `Run()`, его атрибут и `ImportBody` не трогать.
  Готовый код (вставлять как есть; сверить с «Фактами провала» ниже):

```csharp
        /// <summary>
        /// Batchmode-вход: -executeMethod Albion.Editor.MobBodyImport.ImportFromCommandLine
        /// с аргументами -mobKey -sourceFbx -packageRoot -bodyName -set -height.
        /// </summary>
        public static void ImportFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i].StartsWith("-", StringComparison.Ordinal))
                {
                    values[args[i].TrimStart('-')] = args[i + 1];
                }
            }

            string Required(string name)
            {
                if (values.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                {
                    return value;
                }

                Debug.Log("MISSING ARGUMENT: -" + name);
                EditorApplication.Exit(2);
                return null;
            }

            var mobKey = Required("mobKey");
            var sourceFbx = Required("sourceFbx");
            var bodyName = Required("bodyName");
            var setKey = Required("set");
            var heightText = Required("height");
            if (mobKey == null || sourceFbx == null || bodyName == null || setKey == null || heightText == null)
            {
                return;
            }

            if (!float.TryParse(heightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
            {
                Debug.Log("BAD ARGUMENT: -height must be a number, got '" + heightText + "'");
                EditorApplication.Exit(2);
                return;
            }

            var packageRoot = values.TryGetValue("packageRoot", out var root) && !string.IsNullOrEmpty(root)
                ? root
                : Path.GetDirectoryName(sourceFbx);

            var report = new System.Text.StringBuilder();
            report.AppendLine("########## MOB IMPORT CLI ##########");
            var prefabPath = "";
            var controllerPath = "";
            var states = 0;
            var albedo = false;
            var materialsOk = false;
            var ok = false;
            string error = null;

            try
            {
                var settings = AssetDatabase.LoadAssetAtPath<MobVisualSettings>(MobVisualsPath);
                if (settings == null)
                {
                    throw new InvalidOperationException("MobVisuals.asset not found: " + MobVisualsPath);
                }

                if (settings.Entries == null || !settings.Entries.Any(entry => entry.MobKey == mobKey))
                {
                    throw new InvalidOperationException("Ключ моба '" + mobKey
                        + "' не найден в MobVisuals.asset: инструмент только привязывает тело к существующему мобу.");
                }

                var entries = settings.Entries.ToList();
                if (!ImportBody(mobKey, sourceFbx, packageRoot, bodyName, setKey, height, report, entries, out var prefab))
                {
                    throw new InvalidOperationException("ImportBody вернул false, см. отчёт выше.");
                }

                settings.Entries = entries.ToArray();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                settings.ValidateMaterials();
                materialsOk = true;

                prefabPath = AssetDatabase.GetAssetPath(prefab);
                var animator = prefab.GetComponent<Animator>();
                var controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
                if (controller != null)
                {
                    controllerPath = AssetDatabase.GetAssetPath(controller);
                    if (controller.layers.Length > 0)
                    {
                        states = controller.layers[0].stateMachine.states.Length;
                    }
                }

                // Тело — это SkinnedMeshRenderer; накладки вроде M_Glow без текстуры законны.
                var body = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                albedo = body != null && body.sharedMaterials.Any(material => material != null && HasAlbedo(material));
                ok = true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                report.AppendLine("FAILED: " + error);
            }

            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            var verdictDir = Path.Combine(repoRoot, "docs", "imports", bodyName);
            Directory.CreateDirectory(verdictDir);
            var verdictPath = Path.Combine(verdictDir, "verdict.json");
            var json = new System.Text.StringBuilder();
            json.Append("{\n");
            json.Append("  \"body\": ").Append(JsonString(bodyName)).Append(",\n");
            json.Append("  \"mobKey\": ").Append(JsonString(mobKey)).Append(",\n");
            json.Append("  \"set\": ").Append(JsonString(setKey)).Append(",\n");
            json.Append("  \"prefab\": ").Append(JsonString(prefabPath)).Append(",\n");
            json.Append("  \"controller\": ").Append(JsonString(controllerPath)).Append(",\n");
            json.Append("  \"states\": ").Append(states).Append(",\n");
            json.Append("  \"albedo\": ").Append(albedo ? "true" : "false").Append(",\n");
            json.Append("  \"materials_ok\": ").Append(materialsOk ? "true" : "false").Append(",\n");
            json.Append("  \"ok\": ").Append(ok ? "true" : "false").Append(",\n");
            json.Append("  \"error\": ").Append(error == null ? "null" : JsonString(error)).Append("\n");
            json.Append("}\n");
            File.WriteAllText(verdictPath, json.ToString());
            report.AppendLine("verdict: " + verdictPath + " ok=" + ok);
            Debug.Log(report.ToString());
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Строка JSON в кавычках: экранирование \ " и переводов строк.</summary>
        private static string JsonString(string value)
        {
            if (value == null)
            {
                return "null";
            }

            var builder = new System.Text.StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: builder.Append(character); break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }
```

- Факты провала 22.11.2026 (эти шесть ошибок уже были — не повторять):
  1. `MiniJson` умеет **только** `Parse` (`src/Shared/MiniJson.cs`), `Serialize` не существует (CS0117);
     ADR-011 запрещает `System.Text.Json` в `Shared`/`Simulation.Core`. JSON собирается строкой — `JsonString`.
  2. `[MenuItem("Albion/Mobs/Import Bodies From Sets")]` обязан остаться на `Run()`. В провальной
     попытке атрибут уехал на CLI-метод — пункт меню в редакторе стал бы вызывать batchmode-вход.
  3. `Animator.runtimeAnimatorController` — тип `RuntimeAnimatorController`, у него нет `layers` (CS1061);
     нужен `as AnimatorController` (`using UnityEditor.Animations;` в файле уже есть).
  4. `hasAlbedo = true` константой даёт ложный PASS. Альбедо спрашивается с тела (`SkinnedMeshRenderer`)
     через существующий `HasAlbedo(Material)` (строка ~475), а не с имени материала.
  5. После `EditorApplication.Exit(2)` нужен `return`: в редакторе (не batchmode) `Exit` не прерывает
     выполнение, и метод пошёл бы дальше с null-аргументами.
  6. Форматирование `Run()` не трогать: в провальной попытке тело CLI-метода вставили на месте атрибута,
     и файл поехал. Новый код — только после `Run()`.

- Ограничения: не создавать новых `MobVisualSettings.Entry`; не править `config/*.json`;
  не менять `Jobs`, `Run()`, `ImportBody` и тексты отчёта пункта меню; вердикт — в `docs/imports/`,
  не в `Assets/`; не добавлять пакетов и не менять `Protocol.Version`.
- Критерии приёмки: `gates.ps1 -Task T-005` → `ok=true` (Unity-компиляция зелёная); batchmode-прогон
  `-executeMethod Albion.Editor.MobBodyImport.ImportFromCommandLine -mobKey red_warlord
  -sourceFbx Assets/WizardPBR/Mesh/WizardBodyMesh.fbx -packageRoot Assets/WizardPBR
  -bodyName WizardBody -set magic -height 2.45 -quit -logFile -` → `EXIT=0`,
  `docs/imports/WizardBody/verdict.json` с `"ok": true`, `"states": 6`, `"albedo": true`;
  прогон с `-mobKey ghost_mob` → `EXIT=1` и `"error"` про ненайденный ключ.
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-005`; CLI-прогоны выше (Unity batchmode ~40–60 с каждый).
- Промпт для gemma: «Прочитай `## T-005` в `docs/plan_for_gemma.md` целиком, включая «Факты провала».
  Первая попытка уже провалилась на компиляции Unity — готовый код и шесть ошибок описаны в плане.
  Добавь `using System.Globalization;` в шапку `unity-client/Assets/Editor/MobBodyImport.cs` и вставь
  в конец класса `ImportFromCommandLine()` и `JsonString(string)` дословно из плана. `Run()` с его
  атрибутом `[MenuItem(...)]`, `Jobs` и `ImportBody` не меняй. Проверь гейтом
  `pwsh -File tools/tandem/gates.ps1 -Task T-005 -SkipUnity`, затем (если Unity свободен) прогони
  batchmode для `red_warlord` и для `ghost_mob` и покажи коды выхода.»
- Размер: ~130 строк в одном файле (готовый код в плане).
- Зависимости: T-004.

## T-006 — Окно импорта: поля и кнопка «Импорт»

- [x] статус: принято 22.11.2026, слито в main (`3152889`); окно проверено заказчиком вручную.
  Первый прогон конвейера провалился честно:
  три попытки, ревью FAIL, отладчик, ПК-билд пропущен (`done=false`) — и нашёл **две ошибки
  в коде этого плана**: `CS0104 MessageType` неоднозначен из-за `using Shared;` (теперь
  `UnityEditor.MessageType`) и `parsed["sets"]` без `TryGetValue` (теперь безопасно).
  После правки `gates.ps1 -Task T-006` → `ok=true`, Unity-компиляция 15,1 с. Кнопка «Импорт»
  в batchmode не нажимается — путь импорта проверен через тот же `ImportBody` в T-005
  (CLI, `EXIT=0`); нажатие остаётся ручной проверкой заказчика.
  Отчёт: `docs/reports/t-006-окно-импорта-тела.md`
- Цель: `Albion/Mobs/Import Body (Wizard)` открывает окно, в котором выбор тела и нажатие
  «Импорт» дают готовый префаб, контроллер и запись в `MobVisuals.asset` без правки кода.
- Файлы: `unity-client/Assets/Editor/BodyImportWindow.cs` (новый, единственный файл задачи
  помимо вызова `MobBodyImport.ImportBody` из T-004)
- Контекст: `MobBodyImport.ImportBody` (T-004), `MobVisualSettings.Entries`
  (`Assets/Resources/Characters/MobVisuals.asset`), `Assets/StreamingAssets/Config/animation-sets.json`
  (ключи: `unarmed`, `sword_1h`, `sword_2h`, `bow`, `magic`), существующий `BodyGallery.cs`
  как образец EditorWindow в этом проекте.
- Что сделать: создать один новый файл `unity-client/Assets/Editor/BodyImportWindow.cs` —
  код ниже вставляется целиком. `MobBodyImport` НЕ правится: все нужные члены уже публичные
  или читаются напрямую (`MobVisualSettings.Entries`, `MiniJson`, путь к `animation-sets.json`).

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Albion.Client;
using Shared;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Albion/Mobs/Import Body (Wizard): одно окно — один импорт. Своей логики импорта здесь нет:
    /// окно собирает аргументы и вызывает MobBodyImport.ImportBody (T-004).
    /// </summary>
    public sealed class BodyImportWindow : EditorWindow
    {
        private const string MobVisualsPath = "Assets/Resources/Characters/MobVisuals.asset";
        private const string SetsPath = "Assets/StreamingAssets/Config/animation-sets.json";

        private string[] mobKeys = Array.Empty<string>();
        private string[] setKeys = Array.Empty<string>();
        private int mobIndex;
        private int setIndex;
        private string bodyName = "";
        private GameObject sourceFbx;
        private string packageRoot = "";
        private float height = 2.45f;
        private string report = "";
        private Vector2 scroll;

        [MenuItem("Albion/Mobs/Import Body (Wizard)")]
        private static void Open()
        {
            var window = GetWindow<BodyImportWindow>(false, "Импорт тела", true);
            window.minSize = new Vector2(460f, 440f);
            window.Reload();
            window.Show();
        }

        private void OnEnable()
        {
            Reload();
        }

        private static MobVisualSettings LoadSettings()
        {
            return AssetDatabase.LoadAssetAtPath<MobVisualSettings>(MobVisualsPath);
        }

        private void Reload()
        {
            var settings = LoadSettings();
            mobKeys = settings != null && settings.Entries != null
                ? settings.Entries
                    .Select(entry => entry.MobKey)
                    .Where(key => !string.IsNullOrEmpty(key))
                    .ToArray()
                : Array.Empty<string>();
            mobIndex = Mathf.Clamp(mobIndex, 0, Mathf.Max(0, mobKeys.Length - 1));

            setKeys = ReadSetKeys();
            setIndex = Mathf.Clamp(setIndex, 0, Mathf.Max(0, setKeys.Length - 1));
        }

        /// <summary>Ключи наборов анимаций: тот же файл, что читает MobBodyImport.LoadSets.</summary>
        private static string[] ReadSetKeys()
        {
            if (!File.Exists(SetsPath))
            {
                return Array.Empty<string>();
            }

            var parsed = MiniJson.Parse(File.ReadAllText(SetsPath)) as Dictionary<string, object>;
            if (parsed == null
                || !parsed.TryGetValue("sets", out var setsValue)
                || !(setsValue is List<object> list))
            {
                return Array.Empty<string>();
            }

            var keys = new List<string>();
            foreach (var item in list)
            {
                if (item is Dictionary<string, object> entry
                    && entry.TryGetValue("key", out var key)
                    && key is string name)
                {
                    keys.Add(name);
                }
            }

            return keys.ToArray();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Импорт тела моба", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Окно только вызывает MobBodyImport.ImportBody: префаб, контроллер и запись "
                + "в MobVisuals.asset делает он.",
                UnityEditor.MessageType.None);

            if (mobKeys.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "В MobVisuals.asset нет ни одного моба — импорт недоступен.",
                    UnityEditor.MessageType.Warning);
            }

            mobIndex = EditorGUILayout.Popup("Моб (ключ)", mobIndex, mobKeys);
            sourceFbx = (GameObject)EditorGUILayout.ObjectField("FBX тела", sourceFbx, typeof(GameObject), false);

            if (sourceFbx != null)
            {
                var fbxPath = AssetDatabase.GetAssetPath(sourceFbx);
                if (string.IsNullOrEmpty(bodyName))
                {
                    bodyName = Path.GetFileNameWithoutExtension(fbxPath);
                }

                if (string.IsNullOrEmpty(packageRoot))
                {
                    packageRoot = Path.GetDirectoryName(fbxPath);
                }
            }

            bodyName = EditorGUILayout.TextField("Имя тела", bodyName);
            packageRoot = EditorGUILayout.TextField("Папка пакета", packageRoot);
            setIndex = EditorGUILayout.Popup("Набор анимаций", setIndex, setKeys);
            height = EditorGUILayout.FloatField("Высота", height);
            EditorGUILayout.HelpBox(
                "Подгонка под collisionRadius: см. Jobs, WizardBody = 2.45.",
                UnityEditor.MessageType.None);

            using (new EditorGUI.DisabledScope(mobKeys.Length == 0 || sourceFbx == null))
            {
                if (GUILayout.Button("Импорт", GUILayout.Height(28f)))
                {
                    RunImport();
                }
            }

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180f));
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void RunImport()
        {
            var builder = new StringBuilder();
            builder.AppendLine("########## MOB IMPORT (WINDOW) ##########");

            try
            {
                var settings = LoadSettings();
                if (settings == null)
                {
                    throw new InvalidOperationException("MobVisuals.asset not found: " + MobVisualsPath);
                }

                if (mobKeys.Length == 0)
                {
                    throw new InvalidOperationException(
                        "В MobVisuals.asset нет мобов: сначала импортируйте моба.");
                }

                if (sourceFbx == null)
                {
                    throw new InvalidOperationException("Не выбран FBX тела.");
                }

                if (string.IsNullOrEmpty(bodyName))
                {
                    throw new InvalidOperationException("Не задано имя тела.");
                }

                if (string.IsNullOrEmpty(packageRoot))
                {
                    throw new InvalidOperationException("Не задана папка пакета.");
                }

                var setKey = setKeys.Length > 0 ? setKeys[setIndex] : "";
                if (string.IsNullOrEmpty(setKey))
                {
                    throw new InvalidOperationException("Не выбран набор анимаций.");
                }

                var mobKey = mobKeys[mobIndex];
                builder.AppendLine("mob=" + mobKey + " set=" + setKey + " height=" + height);

                var entries = settings.Entries != null
                    ? settings.Entries.ToList()
                    : new List<MobVisualSettings.Entry>();

                if (!MobBodyImport.ImportBody(
                        mobKey,
                        AssetDatabase.GetAssetPath(sourceFbx),
                        packageRoot,
                        bodyName,
                        setKey,
                        height,
                        builder,
                        entries,
                        out var prefab))
                {
                    throw new InvalidOperationException("ImportBody вернул false, см. отчёт выше.");
                }

                // Тот же порядок, что в MobBodyImport.Run (T-004): записать реестр, сохранить,
                // затем провалидировать материалы — валидатор ловит белый emission и отсутствие альбедо.
                settings.Entries = entries.ToArray();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                settings.ValidateMaterials();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                builder.AppendLine("PASS: prefab "
                    + (prefab != null ? AssetDatabase.GetAssetPath(prefab) : "?"));
            }
            catch (Exception exception)
            {
                builder.AppendLine("FAILED: " + exception.GetType().Name + ": " + exception.Message);
            }

            report = builder.ToString();
            Reload();
            Repaint();
        }
    }
}
```

- Факты (сверено по коду 22.11.2026, не менять на «как кажется»):
  1. `ImportBody` — публичный статический, порядок аргументов:
     `(string mobKey, string sourceFbx, string packageRoot, string bodyName, string setKey,
     float height, StringBuilder report, List<MobVisualSettings.Entry> entries, out GameObject prefab)`,
     возвращает `bool`; при неизвестном наборе бросает `InvalidOperationException`.
  2. Реестр сохраняет **вызывающий**: `settings.Entries = entries.ToArray(); SetDirty; SaveAssets;
     ValidateMaterials();` — ровно как в `MobBodyImport.Run()` (строки 89–100).
  3. `MobVisualsPath` и `LoadSets()` в `MobBodyImport` **приватные** — окно берёт путь своей
     константой и читает `animation-sets.json` через `MiniJson.Parse` (только чтение, логику
     импорта не дублирует).
  4. `MiniJson.Parse` возвращает `Dictionary<string, object>`; ключи наборов лежат в массиве
     `sets` (`parsed["sets"] as List<object>`), у элемента поле `key`.
  5. Класс — в namespace `Albion.Editor` (как `MobBodyImport`), `MobVisualSettings` — в `Albion.Client`.
  6. **`MessageType` писать только как `UnityEditor.MessageType`** (замер 22.11.2026, CS0104):
     в проекте есть `Shared.MessageType`, поэтому `using Shared;` (нужен для `MiniJson`) делает
     неквалифицированное `MessageType` неоднозначным и Unity-компиляция падает. `dotnet build`
     этого не видит — клиент Unity в решение не входит, — поэтому «build зелёный» здесь ничего не значит.
  7. `parsed["sets"]` не индексировать напрямую: `Reload()` вызывается из `OnEnable`, и при
     отсутствии ключа `KeyNotFoundException` не даст открыть окно. Только `TryGetValue`.
- Ограничения: `MobBodyImport` не править; `Jobs` не трогать; конфиги не менять; AssetDatabase
  операции делает `ImportBody`/вызывающий, окно только передаёт аргументы; не добавлять
  собственных проверок материалов.
- Критерии приёмки: `gates.ps1 -Task T-006` → `ok=true` (Unity-компиляция нового файла);
  файл `BodyImportWindow.cs` содержит `[MenuItem("Albion/Mobs/Import Body (Wizard)")]` и вызов
  `MobBodyImport.ImportBody(`; окно не содержит копии логики импорта (нет `CopyIntoArt`,
  `BuildPrefab`, `CollectSetClips`).
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-006`.
- Промпт для gemma: «Создай новый файл `unity-client/Assets/Editor/BodyImportWindow.cs` —
  вставь дословно готовый C#-код из раздела `## T-006` в `docs/plan_for_gemma.md` (блок
  ```csharp), ничего не добавляя и не переставляя. Файл должен закончиться строкой `}` и
  закрывающей `}` класса — проверь, что последние две строки непустые и это `    }` и `}`.
  `MobBodyImport.cs` не трогай. Готово, когда `pwsh -File tools/tandem/gates.ps1 -Task T-006 -SkipUnity`
  не даёт ошибок компиляции C# (Unity-компиляцию запустит конвейер).»
- Размер: ~200 строк, один новый файл (код в плане).
- Зависимости: T-004.

## T-007 — Первый прогон: Low Poly Warrior на существующем мобе

- [x] статус: **принято 22.11.2026, слито в main** (`75ecd59`, вместе с T-011 `8a5515d`).
  Механически прогон прошёл с 1-й попытки (PASS ревью, риск medium, отчёт
  `docs/reports/t-007-low-poly-warrior.md`), но тело собралось на дефолтном сером — цвета пака
  перенесла T-011. Итог: 10 слотов, 7 цветных материалов, `blue_scavenger` = `Height 2.45`,
  `AllowFlatColour 1`. Пакет импортирован заказчиком 22.11.2026 в
  `unity-client/Assets/asoliddev - Low Poly Fantasy Warrior/` (37 файлов). Замеры по нему:
  - `Models/Low Poly Warrior.fbx` (2 054 092 Б), `Models/Low Poly Warrior.fbx.meta` есть;
  - `Prefabs/Low Poly Warrior.prefab` — в пакете есть свой префаб, но он не импортный: риг и
    клипы берём из FBX через `ImportBody`;
  - **текстур нет ни одной** (0 файлов `.png/.tga/.jpg/.psd`), 10 материалов на Standard-шейдере
    с `_MainTex: {fileID: 0}` и намеренными цветами (`skin 1` 0,776/0,671/0,612, `leather`
    0,565/0,416/0,329, `hair black` 0,088/0,054/0,035…). Поэтому валидатор обязан пропускать
    плоское тело — это делает **T-009** (`AllowFlatColour`), без неё T-007 упадёт на
    «no textured body material» по определению;
  - клипы: `pose.controller` — поза пакета, для набора `sword_1h` берём клипы из
    `Assets/StreamingAssets/Config/animation-sets.json`, как у остальных тел.
  Решено заказчиком 22.11.2026: ключ `blue_scavenger`, набор `sword_1h`, Height `2.45`.
  Порядок: сначала **T-009** (плоское тело, папка пакета, единое «есть альбедо»), затем T-007.
- Команда прогона (после T-009):
  `Unity.exe -batchmode -executeMethod Albion.Editor.MobBodyImport.ImportFromCommandLine
  -mobKey blue_scavenger -sourceFbx "Assets/asoliddev - Low Poly Fantasy Warrior/Models/Low Poly Warrior.fbx"
  -packageRoot "Assets/asoliddev - Low Poly Fantasy Warrior" -bodyName LowPolyWarrior
  -set sword_1h -height 2.45 -quit -logFile -`

- Цель: тело `Low Poly Warrior` из пака `Assets/asoliddev - Low Poly Fantasy Warrior/` проходит
  A–F одной командой CLI и получает вердикт `ok=true`.
- Файлы: генерируемые ассеты (`Assets/Art/Characters/...`, `.../Generated/*.prefab`,
  `*.controller`, `*.mat`), `docs/imports/lowpoly-warrior/` (вердикт, снимки, отчёт).
  Код не меняется — только прогон T-005.
- Факты: `docs/imports/bodies/bodies.json` — `{"file": "Low Poly Warrior", "path":
  "Assets/asoliddev - Low Poly Fantasy Warrior/Models/Low Poly Warrior.fbx", "rig": "humanoid"}`;
  тело в проект уже импортировано, но префаба у него нет (`docs/mob-ranged-magic.md`: «остальные
  тела … не импортированы»). Набор — `sword_1h` (мечник; решение заказчика 22.11.2026),
  ключ — `blue_scavenger` (в `config/mobs.json` у него `weaponType: sword_1h`,
  `collisionRadius: 0.6`), Height `2.45` — по образцу записи мага.
- Что сделать: запустить `Unity.exe -batchmode -executeMethod Albion.Editor.MobBodyImport.ImportFromCommandLine
  -mobKey blue_scavenger -sourceFbx "Assets/asoliddev - Low Poly Fantasy Warrior/Models/Low Poly Warrior.fbx"
  -bodyName LowPolyWarrior -set sword_1h -height 2.45 -quit -logFile -`; затем
  `Albion/Mobs/Render Mob Portraits` для снимков; записать `docs/imports/lowpoly-warrior/report.md`
  с путями префаба, контроллера, числом состояний, вердиктом и снимками.
- Ограничения: не править код; не создавать новых мобов и не править `config/mobs.json`;
  не менять тела других мобов; при FAIL — `blocked` с полным текстом ошибки, а не подмена тела.
- Критерии приёмки: `verdict.json` с `"ok": true`, `"states": 6`; в `MobVisuals.asset`
  обновлена ровно одна запись выбранного ключа; префаб лежит в `Assets/Art/Characters/Generated/`;
  `Albion → Art → Validate Imported Art` не находит ошибок; снимки 4 поз в `docs/imports/lowpoly-warrior/`.
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-007`.
- Промпт для gemma: «Не пиши код. Прогони batchmode-импорт тела Low Poly Warrior по параметрам
  из этой задачи, затем `Render Mob Portraits`, и запиши `docs/imports/lowpoly-warrior/report.md`
  с путями, числом состояний и вердиктом. Если импорт упал — верни status=blocked и полный текст
  ошибки.»
- Размер: 0 строк кода, прогон + отчёт.
- Зависимости: T-005 (и T-004).

## T-008 — Конвейер не должен терять вердикты и бить ПК при провале (приоритет №1)

- [x] статус: принято 22.11.2026, слито в main (`15a321e`); приоритет №1 был до T-006/T-007.
  Причина найдена стендом `tools/tandem/probe-orchestrator.js`: блок ревью стоял после
  `while`-цикла попыток, поэтому `continue` при FAIL уходил во внешний цикл по задачам —
  задача терялась (`tasks: []`), `stopped` оставался `null`, `if (!stopped)` запускал ПК-билд,
  а `done = !stopped` давало `true`. Теперь исполнитель, ревью и гейт — один цикл задачи;
  внедрены инварианты 1–5 (запись на каждую задачу, билд только при полном успехе, честный
  `done`, журнал `decisions`). Проверено: стенд 6 сценариев (ревью-FAIL: было `done=true,
  tasks=[]`, стало `done=false, needs-human-debug, build.skipped`); живой прогон на заведомо
  падающей T-900 → `done=false, tasks[0].status=gate-fail, build.skipped=true`, чекбоксы
  плана не тронуты. Отчёт: `docs/reports/t-008-конвейер-не-теряет-вердикты.md`
- Цель: прогон, в котором хотя бы одна задача не дошла до `pass`, возвращает `done=false`,
  содержит запись о каждой задаче и **не** пытается собирать ПК. Сейчас: `done=true, tasks=[],
  build.ok=false`.
- Файлы: `tools/tandem/orchestrator.workflow.js` (единственный файл)
- Замеры 22.11.2026 (два провальных прогона T-005, картина одинаковая):
  - конвейер вызвал ровно 4 агента: исполнитель, ревьюер, `build-windows`, отчёт;
  - возврат: `{"done": true, "stopped": null, "tasks": [], "build": {"ok": false, ...}}`;
  - вердикт ревьюера в `tasks` не попал, но `build-windows` запустился — на провальной
    Unity-компиляции (`CS1513`), то есть после «провала» ушёл в финальную фазу;
  - в первом прогоне в `.agent/spend.jsonl` легли n=17 reviewer, n=18 gate `build-windows`,
    n=19 architect — значит ревьюер действительно вызывался, то есть цикл шёл.
- Почему это опасно: при пустом `tasks` и `done: true` прогон выглядит успешным. Кто читает
  только `done`/`reason` (а так и делает протокол), увидит «все задачи прошли» там, где код
  не компилируется. Диагноз в тех прогонах восстанавливали из диффа, а не из результата.
- Что сделать:
  1. **Журнал решений.** По каждой задаче писать JSON-строку в лог, который агент отдаёт
     в `docs/reports/logs/tandem-<задача>-<отметка>.json`: номер попытки, `exec.status`,
     `exec.gates_ok`, `review.verdict`, `gate.ok` и принятое решение (`continue` /
     `stop:<причина>`). Писать и при исключении (в `finally`).
  2. **Инвариант 1 — запись на каждую задачу.** Тело цикла обернуть в `try/catch`:
     исключение → `results.push({id, status: "exception", error: message})`,
     `stopped = "exception"`, `break`. После цикла, если `results.length < tasks.length`,
     дописать `{id, status: "missing"}` для пропущенных.
  3. **Инвариант 2 — финальная фаза только при полном успехе.** ПК-билд и отчёт запускать
     лишь если `stopped === null && results.length === tasks.length && results.every(r => r.status === "pass")`.
     Иначе `buildInfo = {ok: false, skipped: true, error: "пропущено: не все задачи прошли"}`,
     отчёт — без билда.
  4. **Инвариант 3 — null/невалидный вердикт ревьюера останавливает прогон.** Ветка `if (!review)`
     в коде есть, но по замерам не срабатывала: проверить, что в `results` реально попадает
     `status: "review-error"` с `notes` исполнителя, и что `stopped` выставляется до финальной фазы.
  5. **Инвариант 4 — `done` не врёт.** `done = stopped === null && results.length === tasks.length
     && results.every(r => r.status === "pass")`.
  6. Успешный прогон не менять: одна проходящая задача → `done: true`, `tasks[0].status: "pass"`,
     билд и отчёт на месте.
- Ограничения: не трогать `tools/tandem/prompts/**`, `gates.ps1`, `notify.ps1`; схему `args`
  не менять; `docs/plan_for_gemma.md` не править (чекбоксы — на отчёте); тело скрипта без
  файловой системы и зависимостей — журнал пишет агент, скрипт только формирует данные.
- Критерии приёмки (живой прогон делает вызывающий агент: у исполнителя нет инструмента `workflow`):
  1. `pwsh -File tools/tandem/gates.ps1 -Task T-008 -SkipUnity` → `ok=true` (C#-регрессия).
  2. Прогон с заведомо падающей задачей → `done=false`, `tasks.length === 1`,
     `tasks[0].status !== "pass"`, `build.skipped === true`, в логе `docs/reports/logs/tandem-*`
     есть строка решения `stop:*`.
  3. Прогон с проходящей задачей → `done=true`, `tasks[0].status === "pass"`, билд собран.
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-008`; живой прогон конвейера — выше.
- Промпт для gemma: «Правь только `tools/tandem/orchestrator.workflow.js`. Два провальных прогона
  22.11.2026 вернули `done:true`, `tasks:[]` и всё равно запустили ПК-билд — замеры в `## T-008`
  в `docs/plan_for_gemma.md`. Добавь: журнал решений по каждой задаче (попытка, вердикт ревью,
  гейт, решение); гарантированную запись в `results` для каждой задачи (`try/catch`, недостающие —
  `status:"missing"`); запрет финальной фазы без полного успеха
  (`stopped === null && results.length === tasks.length && all status === "pass"`), иначе
  `buildInfo.skipped = true`; `done` считать по тому же условию. Поведение успешного прогона
  не меняй. Готово, когда `gates.ps1 -Task T-008 -SkipUnity` зелёный.»
- Размер: ~60 строк в одном файле.
- Зависимости: нет (идёт до T-006/T-007).

## T-009 — Плоское тело без текстуры законно; папка пакета и единое «есть альбедо»

- [x] статус: принято 22.11.2026, слито в main (`af28a37`). Прогон конвейера: gemma написала код с
  **4 ошибками компиляции** (`materials.Count` вместо `.Length` в двух файлах, повторное
  объявление `assigned`, вызов удалённого `HasAlbedo`), ревьюер повис на 16 минут без записи в
  файлы — прогон снят, ошибки исправлены архитектором, гейты и приёмка прогнаны вручную.
  Дополнительно найдено при проверке: «подъём вверх» по `FindAssets("t:Material")` рекурсивно
  находил материалы в `Assets/Art` и **молча подставлял чужой материал** — заменено на строгий
  признак «папка `Materials/` или `.mat` прямо в папке» (`HasMaterialsHere`).
  Замеры приёмки: `gates.ps1 -Task T-009` → `ok=true` (build 2.3 с, test 4.4 с, покрытие 100%);
  маг `-packageRoot Assets/WizardPBR` → `albedo: true`, `materials_ok: true`, префаб ссылается на
  `M_WizardBody.mat` (`a28c785b…`) вместо `M_lambert1`; `-packageRoot Assets/Art/Characters/Generated`
  → `EXIT=1`, «в папке пакета нет материалов», префаб **не создан**.
- Замеры 22.11.2026, после того как заказчик импортировал пакет Low Poly Warrior:
  - в пакете `Assets/asoliddev - Low Poly Fantasy Warrior` **ноль** файлов изображений
    (`.png/.tga/.jpg/.psd` — 0 из 37 файлов);
  - 10 материалов, все на Standard-шейдере (`m_Shader: {fileID: 46…}`), у всех
    `_MainTex: {m_Texture: {fileID: 0}}` — текстуры нет ни у одного;
  - цвета заданы намеренно и разные: `skin 1` (0,776 0,671 0,612), `leather` (0,565 0,416 0,329),
    `hair black` (0,088 0,054 0,035), `gray 1` (0,299 0,299 0,299), `steel 2` (0,592 0,596 0,608).
  Вывод: это стилизованный пакет «чистых цветов». Требование текстуры альбедо делает его
  неимпортируемым **по определению** — искать текстуру негде.
- Замер провала окна (тогда же): `package has no materials: Assets\Art\Characters\Generated`
  → префаб собрался на безтекстурном `M_lambert1.mat` → `FAILED: Mob prefab has no textured
  body material: WizardBody` уже после сохранения префаба. Настоящий `M_WizardBody.mat`
  (`_BaseMap` = `AlbedoWizardBody.png`) привязан не был.
- Три разных определения «текстурный материал»: `MobBodyImport.HasAlbedo` — `_BaseMap` или
  `_MainTex`; `ArtMaterialUtility.AlbedoProperties` — `_BaseMap/_MainTex/_BaseColorMap/_Albedo`;
  `MobVisualSettings.ValidateMaterials` — только `_BaseMap`. Материал с одним `_MainTex`
  проходит импорт и падает на валидаторе.
- Цвет в конверсии не теряется: `ArtMaterialUtility.CopyColor` берёт `_BaseColor`, иначе `_Color`
  (проверено по коду, строка ~180) — плоское тело доедет до URP Lit своим цветом.
- Файлы: `unity-client/Assets/Scripts/View/MobVisualSettings.cs`,
  `unity-client/Assets/Editor/MobBodyImport.cs`, `unity-client/Assets/Editor/ArtMaterialUtility.cs`,
  `unity-client/Assets/Editor/BodyImportWindow.cs`
- Что сделать:
  1. **Единое «есть альбедо».** `public static bool HasAlbedo(Material)` в `ArtMaterialUtility`
     по списку `AlbedoProperties`; использовать в `MobVisualSettings.ValidateMaterials`,
     `MobBodyImport` (приватный `HasAlbedo` удалить, не дублировать) и в окне.
  2. **Флаг плоского тела.** В `MobVisualSettings.Entry` добавить `public bool AllowFlatColour;`
     (по умолчанию `false`, старые записи не меняются). Импорт ставит `true`, когда в папке
     пакета **нет ни одного** файла изображений (`*.png/.tga/.jpg/.psd` рекурсивно), и пишет
     в отчёт `flat colour body: true/false`.
  3. **Валидатор.** Тело годно, если материал имеет альбедо (`HasAlbedo`) **или**
     (`entry.AllowFlatColour` и цвет ≠ белый: `_BaseColor`/`_Color`, любой канал < 0,99).
     `M_lambert1` мага (0,5 0,5 0,5) без флага остаётся провалом — защита от «серого по умолчанию»
     сохраняется. Сообщение об ошибке дополнить: «если пакет без текстур — выставьте AllowFlatColour».
     Белая эмиссия запрещена, как раньше.
  4. **Папка пакета.** В `AssignPackageAlbedo`: если в `packageRoot` нет `.mat`, подниматься вверх
     (родитель, не глубже 3 уровней) до папки с материалами; если и там нет —
     `throw new InvalidOperationException("в папке пакета нет материалов: <путь>; укажите папку с .mat (Generated не подходит)")`
     **до** сборки префаба, а не после.
  5. **Фолбэк по имени файла** (для паков, где текстуры есть, но материал-альбедо не находится):
     искать `.png/.tga/.jpg` с ключевыми словами `albedo|diffuse|basecolor|base_color|_base`,
     предпочитая имя с `bodyName`; присваивать в `_BaseMap` и `_MainTex`; писать в отчёт.
  6. **Окно.** `SourceFbx` принимает только `.fbx/.obj` (иначе ошибка «выберите модель, а не
     префаб/материал»); `PackageRoot` по умолчанию — ближайшая вверх папка с материалами.
- Ограничения: не менять имена существующих материалов; конфиги и `Jobs` не трогать;
  `Protocol.Version` не поднимать; валидатор остаётся единственным судьёй.
- Критерии приёмки:
  1. `pwsh -File tools/tandem/gates.ps1 -Task T-009` → `ok=true`.
  2. Регрессия мага: прогон `red_warlord` / `WizardBody` с `-packageRoot Assets/WizardPBR` →
     `verdict.json`: `"albedo": true`, `"materials_ok": true`, `"ok": true`; префаб ссылается
     на материал с `_BaseMap` = `AlbedoWizardBody.png`, а не на `M_lambert1`.
  3. Ошибочная папка: `-packageRoot Assets/Art/Characters/Generated` → ошибка **до** сборки префаба
     с подсказкой про папку с `.mat`.
  4. Плоское тело: пакет без изображений → `AllowFlatColour: true` в записи, валидатор не бросает;
     проверяется живым прогоном в T-007 (в T-009 — только код и строка в отчёте).
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-009`; CLI-прогоны из критериев.
- Промпт для gemma: «Сделай одно определение `HasAlbedo` в `ArtMaterialUtility` (свойства
  `_BaseMap`, `_MainTex`, `_BaseColorMap`, `_Albedo`) и используй его в
  `MobVisualSettings.ValidateMaterials`, `MobBodyImport`, `BodyImportWindow`; приватный
  `HasAlbedo` в `MobBodyImport` удали. Добавь в `MobVisualSettings.Entry` поле
  `public bool AllowFlatColour;` (по умолчанию false); импорт ставит его true, если в папке
  пакета нет файлов изображений. Валидатор: тело годно при альбедо ИЛИ при
  `AllowFlatColour` и цвете ≠ белом. В `AssignPackageAlbedo` поднимайся вверх до папки с `.mat`
  (не глубже 3 уровней), иначе бросай исключение ДО сборки префаба. Фолбэк: искать текстуру
  по ключевым словам `albedo|diffuse|basecolor|base_color|_base`. В окне: только `.fbx/.obj`,
  папка пакета — ближайшая вверх папка с материалами. Готово, когда
  `gates.ps1 -Task T-009 -SkipUnity` зелёный.»
- Размер: ~150 строк в четырёх файлах.
- Зависимости: T-004/T-005 (готово).
## T-010 — Зависший агент не должен съедать прогон: heartbeat, отключаемое ревью, протокол сторожа

- [x] статус: сделано 22.11.2026, ждёт «принято» (ветка `agent/T-010`, коммит `2d471a2`).
  Gemma выполнила все пять пунктов сама: heartbeat с `toISOString()`, `review: false` /
  `cfg.skipReview`, ужесточённый промпт ревьюера (один JSON, запрет гейтов и Unity, `PASS`+`risk
  medium` с перечнем непроверенного), раздел «Правила сторожа» в `.agent/protocol.md`, сценарий
  `review-skip` в пробе. Проба `node tools/tandem/probe-orchestrator.js` — все 7 сценариев
  зелёные, в `review-skip` ревьюер не вызывается (`review: {notes: "skipped by config"}`),
  `done: true`. Гейты: build 5.0 с, test 5.0 с (100 %), arch/boundaries, Unity без `error CS`.
  Ирония проверки: **сам ревьюер на этом прогоне снова повис** (14 минут без записи в файлы) —
  прогон снят по новому правилу сторожа, работа доделана архитектором (коммит + проба).
- Замер 22.11.2026, T-009: ревьюер работал **16 минут без единой записи в файлы**, статус фазы
  не менялся, прогон пришлось снять `job_kill`; вердикта нет, отчёт написан вручную.
- Замер возможностей песочницы `workflow` (22.11.2026, прогон `probe-timers`, 0 агентов):
  `typeof setTimeout === "undefined"`, `clearTimeout === undefined`, `setInterval === undefined`,
  `AbortController === undefined`, `queueMicrotask === undefined`; при этом `Date` — `function`,
  `Date.now()` возвращает время. Попытка `Promise.race([pending, setTimeout(...)])` даёт
  `ReferenceError: setTimeout is not defined`.
  Вывод: **таймаут агента внутри скрипта невозможен** — ни отменить обещание, ни замерять его
  нельзя. Поэтому «сторож» строится на наблюдаемости (heartbeat) и на отключаемом ревью, а не на
  таймере. Не пытаться обойти это через `Date.now()` в цикле — цикл заблокирует скрипт насмерть.
- Файлы: `tools/tandem/orchestrator.workflow.js`, `tools/tandem/probe-orchestrator.js`,
  `.agent/protocol.md`
- Что сделать:
  1. **Heartbeat.** Перед каждым `agent(...)` писать в лог строку с моментом времени и ролью:
     `log("[HH:MM:SS] start executor #1 T-009")` (через `new Date(Date.now()).toISOString()`),
     после возврата — `log("[HH:MM:SS] done executor #1: <status>")`. Сторож видит, где именно
     висит прогон, за секунды, а не догадывается по имени фазы.
  2. **Отключаемое ревью на задачу.** `cfg.tasks[].review === false` (и общий `cfg.skipReview`)
     пропускают фазу ревью: сразу гейт Unity. Нужно для задач-прогонов без правок кода
     (T-007): ревьюеру там нечего проверять, а висит он так же. В журнал решений писать
     `review:skipped`.
  3. **Ограничить ревьюера по инструментам.** В промпте ревью — явно: ответить одним
     JSON-сообщением; не запускать гейты и Unity; читать только `git diff --stat` и
     `git diff -- <файлы задачи>`; если чего-то не проверил — `verdict: PASS`, `risk: medium`,
     в `notes` перечислить, что осталось непроверенным. Причина зависания — неограниченное
     исследование репозитория.
  4. **Протокол сторожа** в `.agent/protocol.md`: если в лог прогона не добавилось ни одной
     строки **6 минут** — снять прогон (`job_kill`), доделать задачу вручную, записать в отчёт
     `stopped: review-timeout` и дописать строку в `.agent/spend.jsonl`. Зафиксировать там же
     замер из песочницы (пункт выше), чтобы никто снова не искал «таймаут в скрипте».
  5. **Сценарий в пробе.** В `tools/tandem/probe-orchestrator.js` добавить `review-skip`:
     задача с `review: false` проходит без вызова ревьюера (счётчик вызовов ревьюера не растёт),
     статус задачи `pass`, `done=true`. Сценарий `review-null` оставить как есть.
- Ограничения: не менять контракт возврата (`{done, stopped, reason, tasks, decisions, build,
  report, paidCalls, executorCalls}`); инварианты T-008 не ослаблять (`done` = все задачи `pass`
  и `stopped === null`); в `docs/plan_for_gemma.md` из оркестратора не писать.
- Критерии приёмки:
  1. `node tools/tandem/probe-orchestrator.js` → все сценарии проходят, включая новый `review-skip`.
  2. В логе прогона перед каждым вызовом агента есть строка с временем и ролью.
  3. `pwsh -File tools/tandem/gates.ps1 -Task T-010` → `ok=true`.
- Гейты: `node tools/tandem/probe-orchestrator.js`; `pwsh -File tools/tandem/gates.ps1 -Task T-010`.
- Промпт для gemma: «В `tools/tandem/orchestrator.workflow.js` добавь heartbeat-логи с временем
  вокруг каждого вызова `agent(...)`, поддержку `review: false` на задачу и `cfg.skipReview`
  (пропуск фазы ревью с записью `review:skipped` в журнал решений), и ужесточи промпт ревьюера
  (один JSON-ответ, без гейтов и Unity, только `git diff`). В
  `tools/tandem/probe-orchestrator.js` добавь сценарий `review-skip`. В `.agent/protocol.md`
  опиши правило сторожа: 6 минут без новых строк лога → снять прогон и доделать вручную.
  Инварианты T-008 не менять. Готово, когда проба зелёная и `gates.ps1 -Task T-010` зелёный.»
- Размер: ~80 строк в трёх файлах.
- Зависимости: T-008 (готово).
## T-011 — Цвета материалов пака должны доехать до тела (T-007 собрал серого человечка)

- [x] статус: **принято 22.11.2026, слито в main** (`8a5515d`). 1 попытка, PASS ревью, риск medium;
  отчёт `docs/reports/t-011-цвета-материалов-пака.md`. Перенос по индексу слота gemma сделала;
  пункт 3 (валидатор) и пункт 4 (дубли) она **не выполнила** — дописаны архитектором:
  валидатор теперь отклоняет цвет, равный дефолту URP Lit `(0,8; 0,8; 0,8)` (проверено живым
  прогоном `GreyProbe`: `EXIT=1`, `materials_ok: false`), 26 неиспользуемых материалов удалены
  (осталось 7 цветных, на которые ссылается префаб). Гейты: build 6,4 с, test 4,3 с (100 %),
  arch/boundaries, Unity-компиляция без `error CS`.
- Замеры 22.11.2026 (прогон T-007, ветка `agent/T-007` @ `a2c7241`):
  - `docs/imports/LowPolyWarrior/verdict.json`: `"ok": true`, `"states": 6`,
    `"materials_ok": true`, **`"albedo": false`**;
  - лог `docs/reports/logs/t007-import.log`: `flat colour body: True`,
    `no authored body material found in Assets/asoliddev - Low Poly Fantasy Warrior`,
    `prefab saved: ...`;
  - префаб `Assets/Art/Characters/Generated/LowPolyWarrior.prefab` — 1 `SkinnedMeshRenderer`,
    **10 слотов**, все материалы `M_1 … M_shulders` с `_BaseColor: {r: 0.8, g: 0.8, b: 0.8}`;
  - пакетный префаб `Prefabs/Low Poly Warrior.prefab` — тот же меш, **10 слотов**, и они
    раскрашены автором: `steel 2` (0,592/0,596/0,608), `leather` (0,565/0,416/0,329),
    `skin 1` (0,776/0,671/0,612), `hair black` (0,088/0,054/0,035), `leather 5`, `leather 2`,
    `leather 6` — 7 разных материалов на 10 слотов.
  Вывод: `AssignPackageAlbedo` искал материал **по имени** (`LowPolyWarrior`, `HasAlbedo`), не
  нашёл (пакет безтекстурный, имена слотов FBX — `1`, `2`, `belt`, `skin`…), вернулся молча,
  и тело получило дефолтный серый URP Lit. Моб функционален, но визуально пустой —
  цель фичи «полностью настроенный префаб с рабочими материалами» не достигнута.
- Второй дефект: валидатор T-009 принимает плоское тело по «цвет ≠ белый», а дефолт URP Lit
  0.8 — не белый, поэтому **серый человек проходит** проверку. Нужен запрет на 0.8-дефолт.
- Файлы: `unity-client/Assets/Editor/MobBodyImport.cs`,
  `unity-client/Assets/Scripts/View/MobVisualSettings.cs`
- Что сделать:
  1. **Материалы из префаба пакета.** В `AssignPackageAlbedo` первым делом искать в
     `packageRoot/Prefabs` префаб с `SkinnedMeshRenderer`, у которого число слотов совпадает
     с числом слотов тела; если найден — присвоить его `sharedMaterials` **по индексу слота**
     (меш один и тот же, порядок слотов сохраняется) вместо поиска по имени. В отчёт:
     `materials from pack prefab: <name>, slots=<n>`. Меш проверять по `m_Mesh`/`sharedMesh`
     (совпадение или общий источник), чтобы не подставить материалы чужого тела.
  2. Остальные пути (совпадение имён → поиск текстуры по ключевым словам) сохранить как
     фолбэки **после** префаба пакета.
  3. **Валидатор.** `AllowFlatColour` не должен пропускать цвет, равный дефолту URP Lit
     `(0,8; 0,8; 0,8)` (допуск 0,02 по каналам): такой материал означает «цвет не перенесён».
     Текст ошибки: «материал тела остался дефолтным (0.8) — цвета пакета не перенесены».
  4. Убрать создание неиспользуемых дублей материалов без префикса `M_` в
     `Assets/Art/Characters/<Body>/Materials/` (в T-007 их создано 13: `1.mat`, `belt.mat`,
     `skin.mat`…), либо объяснить в отчёте, откуда они и почему нужны.
- Критерии приёмки:
  1. Повторный прогон CLI для `blue_scavenger` / `-packageRoot "Assets/asoliddev - Low Poly
     Fantasy Warrior"` → в префабе `LowPolyWarrior.prefab` у 10 слотов `_BaseColor` принимают
     значения пакета: есть `skin 1` (0,776/0,671/0,612), `hair black` (0,088/0,054/0,035),
     `steel 2` (~0,592/0,596/0,608), `leather` (0,565/0,416/0,329) — не менее 4 разных цветов.
  2. `verdict.json` — `"ok": true`, `"materials_ok": true`; запись `blue_scavenger` в
     `MobVisuals.asset` сохраняет `Height: 2.45`.
  3. Тело с дефолтным 0.8 (собранное до фикса) валидатор **не** пропускает.
  4. `pwsh -File tools/tandem/gates.ps1 -Task T-011` → `ok=true`.
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-011`; CLI-прогон из критерия 1.
- Промпт для gemma: «В `MobBodyImport.AssignPackageAlbedo` сначала бери материалы из префаба
  пакета: найди в `packageRoot/Prefabs` префаб с `SkinnedMeshRenderer` с тем же числом слотов,
  что у тела, и присвой его `sharedMaterials` по индексу слота; в отчёт пиши
  `materials from pack prefab: <name>, slots=<n>`. В `MobVisualSettings.ValidateMaterials`
  не считай плоским тело, у которого цвет равен дефолту URP Lit (0,8; 0,8; 0,8). Убери создание
  дублей материалов без префикса `M_`. Готово, когда после прогона CLI у 10 слотов префаба
  цвета пакета и `gates.ps1 -Task T-011` зелёный.»
- Размер: ~90 строк в двух файлах.
- Зависимости: T-007 (ветка `agent/T-007`), T-009 (готово).
## T-013 — Импорт не должен оставлять битую ссылку на префаб (моб одевал процедурного волка)

- [x] статус: сделано 23.11.2026, ждёт «принято» (ветка `agent/T-013`, коммит `e2fee42`).
  Написано архитектором: исполнитель gemma не сделал ни одной записи в файлы за 15 минут,
  прогон снят по правилу сторожа (`stopped: executor-timeout`). Код: `RebindEntryOnDisk`
  перечитывает `MobVisuals` с диска и присваивает записи `Prefab` из
  `AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)`, затем сохраняет ассет;
  `ValidateReferences` падает с `stale prefab reference for mob key …`, если у записи пустой
  префаб или в префабе нет `CharacterVisuals`/`SkinnedMeshRenderer`. Замеры: два прогона CLI
  подряд (`ImportFromCommandLine` с пакетом `Assets/asoliddev - Low Poly Fantasy Warrior`) —
  оба `ok: true`, `states: 6`, `materials_ok: true`; `mob-visuals-check.ps1` после каждого
  прогона даёт три `Ref: ok`. Гейты: build 2.4 с, test 4.6 с (100 %), arch/boundaries, Unity 10.7 с.
- Замеры 23.11.2026 (новый лог клиента, билд 00:48):
  ```
  [mob] actor=101 key=blue_scavenger expected=<none> -> primitive fallback, zone=blue
  [mob] actor=102 key=yellow_bandit  expected=Skeleton_Rogue height=1,95 mesh=… avatar=…
  [mob] actor=103 key=red_warlord    expected=WizardBody    height=2,45 mesh=… avatar=…
  ```
  `expected=<none>` означает, что `MobVisualSettings.Find`/`Create` не нашли запись или у неё
  `Prefab == null`; `UnityView.CreateMob` уходит в резервный путь, а `FantasyVisuals.CreateMob`
  строит **процедурного волка** (для красной зоны — голема). Отсюда «собаки» в стартовой зоне.
  Корневая причина (замер по файлам): в `MobVisuals.asset` у `blue_scavenger` стояла ссылка
  `{fileID: 5204452140323384066, guid: 092f3bb8…}`, но объекта с таким id в
  `LowPolyWarrior.prefab` **нет** (корневой GameObject — `5291887348217133432`). Unity молча
  превращает такую ссылку в `null`. У `red_warlord` id живой (`5990088983646062280` есть в
  `WizardBody.prefab`) — поэтому маг работал. Ручное исправление id и пересборка (билд 00:57)
  вернули тело; ссылка проверена скриптом (`Ref: ok`).
- Файлы: `unity-client/Assets/Editor/MobBodyImport.cs`, `tools/unity/mob-visuals-check.ps1`
- Что сделать:
  1. В `MobBodyImport` после `PrefabUtility.SaveAsPrefabAsset` + `AssetDatabase.SaveAssets`
     **перечитать запись с диска**: загрузить `MobVisualSettings` через
     `AssetDatabase.LoadAssetAtPath<MobVisualSettings>(MobVisualsPath)`, взять в ней запись по
     `mobKey` и присвоить `entry.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)`
     (свежий ассет даёт актуальный id), затем сохранить ассет ещё раз.
  2. После сохранения **проверить**: у каждой записи `Prefab != null`, у префаба есть
     `CharacterVisuals` и `SkinnedMeshRenderer`; иначе
     `throw new InvalidOperationException("stale prefab reference for mob key " + key + ": перезапишите MobVisuals")`.
  3. В `tools/unity/mob-visuals-check.ps1` колонка `Ref` уже проверяет, что `fileID` из записи
     существует в префабе — оставить и упомянуть в отчёте как средство диагностики.
- Критерии приёмки:
  1. После прогона CLI для `blue_scavenger` скрипт `pwsh -File tools/unity/mob-visuals-check.ps1`
     показывает `Ref: ok` у всех трёх записей.
  2. Повторный прогон CLI **дважды подряд** не ломает ссылку (второй прогон пересохраняет
     префаб с новым корневым id — запись обязана обновиться).
  3. `verdict.json` — `ok: true`; `pwsh -File tools/tandem/gates.ps1 -Task T-013` → `ok=true`.
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-013`; прогоны CLI из критериев.
- Промпт для gemma: «В `MobBodyImport` после сохранения префаба и ассетов перечитай
  `MobVisuals` с диска и присвой записи моба `Prefab` из
  `AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)`, затем сохрани ассет. Добавь проверку:
  `Prefab != null`, есть `CharacterVisuals` и `SkinnedMeshRenderer`, иначе бросай
  `InvalidOperationException("stale prefab reference for mob key …")`. Готово, когда два прогона
  CLI подряд оставляют `Ref: ok` в `tools/unity/mob-visuals-check.ps1`.»
- Размер: ~40 строк в одном файле.
- Зависимости: T-007, T-011 (готово).
## T-012 — Диагностика тела моба: лог `[mob]`, скрипт проверки связей, исправленная ссылка

- [x] статус: принято 23.11.2026, слито в `main` (`346dc33`). Найдено по жалобе «в синей зоне волки вместо Warrior».
- Симптом и замер: клиент показывал в стартовой (синей) зоне **процедурного волка**. Лог
  `[mob] actor=101 key=blue_scavenger expected=<none> -> primitive fallback, zone=blue` показал,
  что `MobVisualSettings.Create` вернул null и сработал резервный путь `FantasyVisuals.CreateMob`
  (он строит волка, для красной зоны — голема). Корневая причина по файлам: в
  `MobVisuals.asset` у `blue_scavenger` стояла ссылка `fileID: 5204452140323384066`, но такого
  объекта в `LowPolyWarrior.prefab` нет (корень — `5291887348217133432`): префаб пересохранялся,
  id сменился, Unity молча читает битую ссылку как `null`.
- Что сделано:
  1. `UnityView.CreateMob` + `MobVisualSettings.Describe`: лог
     `[mob] actor=… key=… expected=… height=… mesh=… avatar=…`, а при провале —
     `-> primitive fallback, zone=…`.
  2. `tools/unity/mob-visuals-check.ps1`: разбирает `MobVisuals.asset`, разрешает GUID префаба,
     меша и аватара в пути и проверяет колонкой `Ref`, что `fileID` из записи **есть** в префабе.
  3. Ссылка `blue_scavenger` исправлена на живой root id; замер после правки: у всех трёх
     записей `Ref: ok`, в игре — `[mob] actor=101 … mesh=body avatar=LowPolyWarriorAvatar`.
- Чем проверено: `pwsh -File tools/unity/mob-visuals-check.ps1` → три `Ref: ok`; лог клиента
  билда 00:57; заказчик подтвердил «Воины на месте, всё работает».
- Постоянная защита — задача T-013 (импортёр перечитывает запись с диска и падает на битой ссылке).