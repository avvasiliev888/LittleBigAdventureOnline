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

- [ ] статус: ожидает
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
