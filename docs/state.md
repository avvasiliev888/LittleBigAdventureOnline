# State

Last updated: 2026-11-08 21:10 UTC
Last heartbeat: 2026-11-08 21:10 UTC
Current milestone: M1 — Walking skeleton
Current task: ожидание Human-Gate M1
Onboarding: completed

## Onboarding: completed

Онбординг завершён 2026-11-08, 14 вопросов, все ответы получены от оператора (см. `## Resolved Questions`).

## Milestones

| Milestone | Auto-Gate | Human-Gate | Status |
|---|---|---|---|
| M0 | ✅ | ✅ | done (2026-11-08, ADR заморожены) |
| M1 | ✅ | ⏳ | awaiting human review |
| M2 | — | — | todo |
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

## M1 tasks

| Task | Status | Commit | Notes |
|---|---|---|---|
| 1. Структура репозитория (`/src/*`, `/unity-client`, `/tools`, `/docs`) | done | — | только dotnet + Unity |
| 2. `.sln` с проектами и зависимостями | done | — | `Prototype.sln`, зависимости по ADR-001 |
| 3. CI: сборка, `dotnet test`, grep-проверка `UnityEngine` | done | — | `.github/workflows/ci.yml` + `tools/ci/check-architecture.sh` |
| 4. Пустые интерфейсы с XML-документацией | done | — | 6 контрактов, см. ниже |
| 5. `SimulationState` (тик, id/позиция/HP) | done | — | без игровой логики |
| 6. Smoke-тест: симуляция создаётся, `Tick` не падает | done | — | 9 тестов, все зелёные |
| 7. Unity-проект (пустая сцена) | done | — | Unity 6000.3.28f1, сцена + сборка Windows-плеера |

## M1: созданные контракты (замораживаются по Human-Gate)

| Контракт | Проект | Состав |
|---|---|---|
| `ISimulation` | `Simulation.Core` | `State`, `Tick(float deltaTime, IReadOnlyList<CommandEnvelope>)`, `CreateSnapshot()`, `RestoreFromSnapshot(SimulationSnapshot)` |
| `ICommand` | `Simulation.Core` | `ClientTick`, `Sequence` (без геймплейных полей) |
| `ISnapshotSerializer` | `Simulation.Core` | `Serialize(SimulationSnapshot) → byte[]`, `Deserialize(ReadOnlySpan<byte>)` |
| `ITransport` | `Client.Network` | `Start()`, `Poll()`, `Send(byte[], DeliveryMode)`, `IsConnected`, события `PacketReceived` / `Disconnected` |
| `IInputSource` | `Client.Network` | `TryReadCommand(uint clientTick, out ICommand?)` |
| `IView` | `Client.Network` | `ApplySnapshot(SimulationSnapshot)`, `Render(float frameDeltaTime)` |

Вспомогательные типы: `SimulationState`, `EntityState`, `CommandEnvelope` (EntityId + ICommand), `SimulationSnapshot`, `EntitySnapshot`, `SimulationConstants` (`TickRate = 20`, `FixedDeltaTime = 0.05`).

## M1 Auto-Gate (проверено 2026-11-08)

- [x] CI зелёный: run [42522296907](https://github.com/avvasiliev888/albion-prototype/actions/runs/42522296907) (build, architecture checks, test) на PR #2.
- [x] `dotnet build Prototype.sln -c Release` — 0 предупреждений, 0 ошибок.
- [x] `dotnet test` — 9/9 зелёные (локально через `tools/dev/test-local.ps1`, см. Discovered Issues).
- [x] `tools/ci/check-architecture.sh` — все 4 проверки пройдены локально.
- [x] `Simulation.Core` не ссылается на другие проекты; `UnityEngine` отсутствует.
- [x] Unity-проект: `6000.3.28f1`, сцена `Assets/Scenes/SampleScene.unity`, Windows-плеер собран (`BuildWindowsPlayer: result=Succeeded, errors=0`).
- [x] `docs/state.md` обновлён: интерфейсы и контракты перечислены выше.

## M1 Human-Gate (ожидает оператора)

**PR:** [#2 — M1: Walking skeleton](https://github.com/avvasiliev888/albion-prototype/pull/2), ветка `agent/M1/walking-skeleton`.

- [ ] Оператор прочитал интерфейсы и подтвердил контракты. **После этого интерфейсы заморожены.**
- [ ] Оператор вручную проверил, что CI краснеет при `using UnityEngine;` в `Simulation.Core`.
- [ ] Оператор открыл `unity-client` в Unity Hub (проект должен открыться без ошибок, сцена `SampleScene` в build settings).

**Как проверить CI-красноту (п.2):** в ветке `agent/M1/walking-skeleton` добавить `using UnityEngine;` в любой файл `src/Simulation.Core`, запушить — job `build-test` должен упасть на шаге «Architecture checks». Коммит с этой правкой не должен попасть в PR.

## Environment

| Что | Значение |
|---|---|
| ОС | Windows 11 (10.0.22631), PowerShell 7.5.4 |
| .NET SDK | 10.0.403 (winget), `global.json` фиксирует версию |
| Unity | 6000.3.28f1 LTS, `N:\Program Files\Unity Hub\Editor\6000.3.28f1` (лицензия активирована оператором) |
| Репозиторий | `https://github.com/avvasiliev888/albion-prototype` (private), `main`, remote `origin` |
| Git LFS | 3.7.1, `--local`; бинарники > 100 KB — в LFS (`.gitattributes`) |
| Node.js | v24.14.1 |

## Open Questions

(нет — branch protection решена вариантом «дисциплина»: агент пушит только в `agent/*`, мержит оператор)

## Resolved Questions

1. **Какой .NET SDK и кто ставит?** → .NET 10 LTS, устанавливает агент. (2026-11-08)
2. **Какая Unity и кто ставит?** → Unity 6.3 LTS (6000.3.x), ставит и активирует оператор. (2026-11-08)
3. **Как проверяется «Unity собирается»?** → локально оператором на M6; GameCI — только при реальном билде. (2026-11-08)
4. **Где хостится репозиторий?** → `avvasiliev888/albion-prototype`, private. (2026-11-08)
5. **Кто создаёт репозиторий?** → агент. (2026-11-08)
6. **Монорепо и Git LFS?** → монорепо, LFS сразу, бинарники > 100 KB, `*.meta` трекаются. (2026-11-08)
7. **Модель доступа?** → агент пушит только в `agent/*`, PR открывает агент, мержит оператор. (2026-11-08)
8. **CI-раннер и лимиты?** → `ubuntu-latest`, полный прогон только на push в `agent/*` и PR в `main`. (2026-11-08)
9. **Branch protection?** → GitHub Free не даёт protection для private-репо; оператор выбрал вариант (c): репозиторий остаётся private, действует дисциплина. (2026-11-08)
10. **LiteNetLib?** → NuGet 2.1.4, не вендорить. (2026-11-08)
11. **Допущения Product Vision?** → 10–20 CCU; NPC-якорей рынка нет; цифры убывающей отдачи позже. (2026-11-08)
12. **Экономика в прототипе?** → нет (M1–M12), только архитектура. (2026-11-08)
13. **M1 → M2?** → строго последовательно, контракты M1 замораживаются. (2026-11-08)
14. **Namespace?** → `Simulation.Core`, `Shared`, `Server`, `Client.Network`. (2026-11-08)

## Решения, принятые в M1 (на ревью оператору)

1. **Мультитаргет `netstandard2.1;net10.0`** для `Simulation.Core`, `Shared`, `Client.Network`; `LangVersion 9.0` и `ImplicitUsings=disable` — иначе Unity 6.3 (netstandard2.1, C# 9) не сможет сослаться на библиотеки.
2. **`ISnapshotSerializer` живёт в `Simulation.Core`** (сериализует тело снапшота), а `Shared` содержит только обёртку сообщения (`Protocol.Magic/Version`, `MessageType`) — так `Simulation.Core` остаётся без зависимостей (ADR-001).
3. **`IView`/`IInputSource`/`ITransport` живут в `Client.Network`**, который ссылается на `Simulation.Core` + `Shared`: интерфейсам нужны `SimulationSnapshot` и `ICommand`. Инвариант «`Simulation.Core` ни от чего не зависит» соблюдён.
4. **Класс `Simulation` требует полного имени `Simulation.Core.Simulation`** извне — конфликт с корневым namespace `Simulation`. В тестах используется алиас `CoreSimulation`.
5. **Тесты: xUnit, 9 штук** (smoke + контракты), проект `src/Tests` на `net10.0`.
6. **`Simulation.CreateSnapshot/RestoreFromSnapshot` реализованы сразу** как простое копирование состояния (без игровой логики); M2 добавляет тесты детерминизма и сортировку команд, а не «плумбинг».
7. **Unity-проект инициализируется из CLI** (`Assets/Editor/ProjectSetup.cs`, `-executeMethod`): сцена + build settings + сборка плеера. Это же — основа для проверки Gate «Unity собирается».

## Discovered Issues

1. **VSTest не работает внутри workspace на `F:`** — testhost падает с `Win32Exception (5)` на `Process.set_EnableRaisingEvents` (попытка открыть handle родительского процесса). Тот же проект из `%TEMP%` (или с `F:` вне workspace) проходит. Обход: `tools/dev/test-local.ps1` (копирует исходники во временный каталог и гоняет тесты там). CI на `ubuntu-latest` проблемы не имеет. Приоритет: низкий для проекта, требует внимания оператора (возможно ограничение песочницы хоста).
2. **CRLF-предупреждения git** при `git add` на Windows. Файлы в индексе нормализованы (`core.autocrlf=false`); косметика. Приоритет: низкий.

## Next Actions (для следующей сессии)

1. Дождаться Human-Gate M1: подтверждение контрактов (после — заморозка), проверка красноты CI, открытие Unity-проекта.
2. После подтверждения — M2: `MoveCommand`, `Tick` с движением, сортировка команд, snapshot-тесты, replay-тест.
