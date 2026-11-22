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

- [ ] статус: ожидает
- Цель: в batchmode одной командой импортируется одно тело, а результат пишется в
  `docs/imports/<body>/verdict.json` и определяет код выхода.
- Файлы: `unity-client/Assets/Editor/MobBodyImport.cs` (единственный файл; `ImportBody` из T-004)
- Контекст: `ImportBody` (T-004), `settings.Entries` (`MobVisualSettings.Entry.MobKey`),
  `settings.ValidateMaterials()`, существующий batchmode-перехват в `Run()` (строки 117–129:
  при исключении `EditorApplication.Exit(1)`).
- Что сделать:
  1. `public static void ImportFromCommandLine()` — читает `Environment.GetCommandLineArgs()`:
     `-mobKey`, `-sourceFbx`, `-packageRoot`, `-bodyName`, `-set`, `-height`; всё, кроме
     `-packageRoot` (по умолчанию — папка FBX), обязательно; отсутствие аргумента → `Exit(2)`
     с внятным текстом.
  2. **Отказ новым мобам**: если `mobKey` отсутствует в `settings.Entries`, бросить
     `InvalidOperationException("Ключ моба '<key>' не найден в MobVisuals.asset: инструмент
     только привязывает тело к существующему мобу.")` — новых мобов не заводим (решение
     заказчика 22.11.2026).
  3. Файл вердикта: `docs/imports/<bodyName>/verdict.json` (корень репозитория получить от
     `Application.dataPath`: `unity-client/Assets` → два уровня вверх; в batchmode
     относительные пути не работают — замер 21.11.2026, см. комментарий к `SetsPath`, строки 31–36).
     Поля: `body`, `mobKey`, `set`, `prefab` (путь), `controller` (путь), `states` (число
     состояний), `albedo` (bool), `materials_ok` (bool), `ok` (bool), `error` (строка или null).
  4. Код выхода: `ok=true` → `Exit(0)`, иначе `Exit(1)`.
- Ограничения: не создавать новых `MobVisualSettings.Entry`; не править `config/*.json`;
  не менять `Jobs` и тексты отчёта пункта меню; вердикт — в `docs/imports/`, не в `Assets/`.
- Критерии приёмки: прогон на `red_warlord` (тело `WizardBody`) в batchmode → `EXIT=0`,
  `verdict.json` с `"ok": true`, `"states": 6`; прогон с ключом `ghost_mob` → `EXIT=1`,
  сообщение про ненайденный ключ, файла вердикта с `"ok": true` нет.
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-005`; CLI-прогоны выше.
- Промпт для gemma: «Добавь в `MobBodyImport` метод `ImportFromCommandLine()` для
  `-executeMethod` в batchmode: аргументы `-mobKey -sourceFbx -packageRoot -bodyName -set -height`,
  вызов `ImportBody`, отказ, если ключа моба нет в `MobVisuals.asset`, запись
  `docs/imports/<body>/verdict.json` с полями из плана и `EditorApplication.Exit(0|1)`.
  Новых мобов не создавать, конфиги не трогать.»
- Размер: ~60 строк в одном файле.
- Зависимости: T-004.

## T-006 — Окно импорта: поля и кнопка «Импорт»

- [ ] статус: ожидает
- Цель: `Albion/Mobs/Import Body (Wizard)` открывает окно, в котором выбор тела и нажатие
  «Импорт» дают готовый префаб, контроллер и запись в `MobVisuals.asset` без правки кода.
- Файлы: `unity-client/Assets/Editor/BodyImportWindow.cs` (новый, единственный файл задачи
  помимо вызова `MobBodyImport.ImportBody` из T-004)
- Контекст: `MobBodyImport.ImportBody` (T-004), `MobVisualSettings.Entries`
  (`Assets/Resources/Characters/MobVisuals.asset`), `Assets/StreamingAssets/Config/animation-sets.json`
  (ключи: `unarmed`, `sword_1h`, `sword_2h`, `bow`, `magic`), существующий `BodyGallery.cs`
  как образец EditorWindow в этом проекте.
- Что сделать: `EditorWindow` с полями — `MobKey` (`PopupField` из ключей существующих записей
  `MobVisuals.asset`; пустой реестр → предупреждение «сначала импортируйте моба»), `BodyName`
  (string, по умолчанию — имя FBX без расширения), `SourceFbx` (`ObjectField` типа
  `GameObject`, перетаскивание FBX), `PackageRoot` (string, по умолчанию — папка FBX),
  `Set` (`PopupField` из ключей `animation-sets.json`), `Height` (float, подпись
  «подгонка под collisionRadius, см. Jobs: WizardBody = 2.45»). Кнопка «Импорт» вызывает
  `ImportBody`, показывает отчёт в `EditorGUILayout.TextArea` и итог PASS/FAIL; при FAIL —
  текст ошибки, при PASS — путь к префабу. Меню `[MenuItem("Albion/Mobs/Import Body (Wizard)")]`.
- Ограничения: не дублировать логику импорта в окне — только вызов T-004; не править
  `MobBodyImport` (кроме чтения публичных членов); не трогать конфиги; AssetDatabase
  операции — уже внутри `ImportBody`.
- Критерии приёмки: Unity компилирует проект без ошибок; меню `Albion/Mobs/Import Body (Wizard)`
  существует (проверяется компиляцией атрибута); заполнение полей и «Импорт» для тела
  `WizardBody` даёт тот же результат, что пункт меню `Import Bodies From Sets` (одна запись
  `red_warlord`, повтор не плодит дублей).
- Гейты: `pwsh -File tools/tandem/gates.ps1 -Task T-006` (Unity-компиляция).
- Промпт для gemma: «Создай `unity-client/Assets/Editor/BodyImportWindow.cs`: EditorWindow
  с меню `Albion/Mobs/Import Body (Wizard)`, полями MobKey (popup из записей MobVisuals.asset),
  BodyName, SourceFbx (ObjectField), PackageRoot, Set (popup из animation-sets.json), Height,
  кнопкой «Импорт», вызывающей `MobBodyImport.ImportBody` из T-004, и выводом отчёта с
  PASS/FAIL. Никакой своей логики импорта, конфиги не трогать.»
- Размер: ~120 строк, один новый файл.
- Зависимости: T-004.

## T-007 — Первый прогон: Low Poly Warrior на существующем мобе

- [ ] статус: ожидает; решено заказчиком 22.11.2026: ключ `blue_scavenger`, набор `sword_1h`,
  Height `2.45`
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
