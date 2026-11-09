# State

Last updated: 2026-11-09 08:35 UTC
Last heartbeat: 2026-11-09 08:35 UTC
Current milestone: M2 — Симуляция: детерминированный тик и движение
Current task: ожидание Human-Gate M2
Onboarding: completed

## Onboarding: completed

Онбординг завершён 2026-11-08, 14 вопросов, все ответы получены от оператора (см. `## Resolved Questions`).

## Milestones

| Milestone | Auto-Gate | Human-Gate | Status |
|---|---|---|---|
| M0 | ✅ | ✅ | done (2026-11-08, ADR заморожены) |
| M1 | ✅ | ✅ | done (2026-11-09, контракты заморожены) |
| M2 | ✅ | ⏳ | awaiting human review |
| M3 | — | — | todo |
| M4 | — | — | todo |
| M5 | — | — | todo |
| M6 | — | — | todo |
| M7 | — | — | todo |
| M8 | — | — | todo |
| M9 | — | — | todo |
| M10 | — | — | todo |
| M11 | — | — | todo |
| M12 | — | — | todo |

## M2 tasks

| Task | Status | Commit | Notes |
|---|---|---|---|
| 1. `MoveCommand` (направление, `ClientTick`, `Sequence`) | done | — | без лишних полей |
| 2. `Simulation.Tick`: движение с `deltaTime` и `moveSpeed` из конфига | done | — | `SimulationConfig`, без хардкода |
| 3. `CreateSnapshot`/`RestoreFromSnapshot` (полный снапшот) | done | — | 1:1, включая `MaxHp` |
| 4. Сортировка команд по `(ClientTick, Sequence)` | done | — | `CommandOrdering.Sort` (канонический порядок + `EntityId`) |
| 5. Unit-тесты детерминизма, порядка, восстановления | done | — | 31 тест всего |
| 6. Replay-тест: 1000 команд с `seed` | done | — | два прогона → идентичный снапшот |

## M2: принятые решения (на ревью оператору)

1. **`EntitySnapshot` получил `MaxHp`.** Без него восстановление «1:1» теряло максимальное здоровье (M2 требует 1:1). Это DTO, а не замороженный интерфейс; сигнатуры `ISimulation`/`ISnapshotSerializer` не менялись. При кодировании в байты (M4) поле попадёт в формат — `Protocol.Version` пока не поднимался, потому что провод ещё не реализован (ADR-004 требует поднимать версию при изменении формата).
2. **`SimulationConfig` — отдельный тип в `Simulation.Core`** с единственным параметром `MoveSpeed`. Хардкод скорости запрещён: `Tick` с командами без конфига бросает `InvalidOperationException`. M3 заменит ручное создание на загрузку из JSON, тип останется тем же.
3. **`CommandOrdering.Sort` — отдельная утилита, а не логика внутри `Tick`.** Замороженный контракт `ISimulation` говорит: команды приходят уже отсортированными, симуляция порядок не меняет. Сортировка — обязанность вызывающего кода (сервер/клиент), утилита даёт канонический порядок `(ClientTick, Sequence, EntityId)`, детерминированный при любом входном порядке.
4. **Команда для несуществующей сущности игнорируется** (не бросает): сущность могла умереть/исчезнуть; решение о логировании — на сервере (M4), а не в симуляции.
5. **Неизвестный тип команды бросает `NotSupportedException`** — вместо тихого игнорирования, чтобы ошибки протокола не прятались.

## M2 Auto-Gate (проверено 2026-11-09)

- [x] CI зелёный: run [42578589472](https://github.com/avvasiliev888/albion-prototype/actions/runs/42578589472) (build, arch-check, tests + coverage gate) на PR #3.
- [x] Все unit-тесты зелёные: **31/31** (движение, порядок команд, снапшоты, детерминизм, контракты).
- [x] Replay-тест зелёный: 1000 команд с `seed=20261108`, два прогона → идентичный финальный снапшот; другой seed → другое состояние.
- [x] CI-grep: `UnityEngine` в `Simulation.Core` отсутствует (`tools/ci/check-architecture.sh`).
- [x] Покрытие `Simulation.Tick` и snapshot-методов — **100% line** (coverlet, порог в CI: `Threshold=100`, `Include=[Simulation.Core]Simulation.Core.Simulation`).

## M2 Human-Gate (ожидает оператора)

**PR:** [#3 — M2: детерминированное движение и снапшоты](https://github.com/avvasiliev888/albion-prototype/pull/3), ветка `agent/M2/movement-and-snapshots`.

- [ ] Оператор прогнал replay-тест дважды — результат идентичен.
- [ ] Оператор проверил, что `MoveCommand` не содержит лишних полей (`speedMultiplier` и т.п. — только `Direction`, `ClientTick`, `Sequence`).

**Как прогнать replay:**
```
pwsh -File tools/dev/test-local.ps1 -Configuration Release
# или точечно, вне workspace:
dotnet test src/Tests/Tests.csproj --filter "FullyQualifiedName~ReplayTests" -c Release
```
Ожидаемо: `Replay_1000SeededCommands_ProducesIdenticalFinalState` и `Replay_DifferentSeed_ProducesDifferentState` — зелёные.

## Environment

| Что | Значение |
|---|---|
| ОС | Windows 11 (10.0.22631), PowerShell 7.5.4 |
| .NET SDK | 10.0.403, `global.json` фиксирует версию |
| Unity | 6000.3.28f1 LTS, `N:\Program Files\Unity Hub\Editor\6000.3.28f1` |
| Репозиторий | `https://github.com/avvasiliev888/albion-prototype` (private), `main` |
| Git LFS | 3.7.1, `--local` |

## Open Questions

(нет)

## Resolved Questions

1. **Какой .NET SDK и кто ставит?** → .NET 10 LTS, устанавливает агент. (2026-11-08)
2. **Какая Unity и кто ставит?** → Unity 6.3 LTS (6000.3.x), ставит и активирует оператор. (2026-11-08)
3. **Как проверяется «Unity собирается»?** → локально оператором на M6; GameCI — только при реальном билде. (2026-11-08)
4. **Где хостится репозиторий?** → `avvasiliev888/albion-prototype`, private. (2026-11-08)
5. **Кто создаёт репозиторий?** → агент. (2026-11-08)
6. **Монорепо и Git LFS?** → монорепо, LFS сразу, бинарники > 100 KB, `*.meta` трекаются. (2026-11-08)
7. **Модель доступа?** → агент пушит только в `agent/*`, PR открывает агент, мержит оператор. (2026-11-08)
8. **CI-раннер и лимиты?** → `ubuntu-latest`, прогон на push в `agent/*` и PR в `main`. (2026-11-08)
9. **Branch protection?** → private-репо, protection недоступна на Free; действует дисциплина. (2026-11-08)
10. **LiteNetLib?** → NuGet 2.1.4, не вендорить. (2026-11-08)
11. **Допущения Product Vision?** → 10–20 CCU; NPC-якорей рынка нет; цифры убывающей отдачи позже. (2026-11-08)
12. **Экономика в прототипе?** → нет (M1–M12). (2026-11-08)
13. **M1 → M2?** → строго последовательно. (2026-11-08)
14. **Namespace?** → `Simulation.Core`, `Shared`, `Server`, `Client.Network`. (2026-11-08)

## Discovered Issues

1. **VSTest не работает внутри workspace на `F:`** — testhost падает с `Win32Exception (5)` на `Process.set_EnableRaisingEvents`; тот же проект из `%TEMP%` проходит. Обход: `tools/dev/test-local.ps1`. CI не затронут. Приоритет: низкий (ограничение хоста).
2. **Unity Package Manager не может достучаться до `packages.unity.com`** — `ETIMEDOUT` из процесса UPM, хотя хост-процесс получает `200` (проверено `Invoke-WebRequest` к тому же хосту). Причина — сетевое ограничение дочерних процессов на этом хосте (та же семья, что и п.1), не диск F: и не проект. **Не критично:** `Packages/manifest.json` и `packages-lock.json` в репозитории, все 37 пакетов разрешены из `Library/PackageCache`, проект компилируется и собирается, сцена открывается. Влияние: поиск/установка новых пакетов из registry (понадобится на M10 для URP) — тогда либо оператор ставит пакет через Hub, либо добавляем тарбол локально (`file:`), либо ищем снятие ограничения. Приоритет: средний к M10.
3. **CRLF-предупреждения git** на Windows — косметика (`core.autocrlf=false`). Приоритет: низкий.

## Next Actions (для следующей сессии)

1. Дождаться Human-Gate M2 (replay дважды + проверка полей `MoveCommand`), после — merge PR.
2. Затем M3: JSON-схема конфигов (`abilities`, `actors`), загрузчик с валидацией, `AbilityCommand`, урон/кулдаун/смерть/респавн, тесты «изменил JSON → изменилось поведение».
