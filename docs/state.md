# State

Last updated: 2026-11-09 09:20 UTC
Last heartbeat: 2026-11-09 09:20 UTC
Current milestone: M4 — Сервер: цикл, валидация, снапшоты
Current task: ожидание Human-Gate M4
Onboarding: completed

## Onboarding: completed

Онбординг завершён 2026-11-08, 14 вопросов, все ответы получены от оператора (см. `## Resolved Questions`).

## Milestones

| Milestone | Auto-Gate | Human-Gate | Status |
|---|---|---|---|
| M0 | ✅ | ✅ | done (2026-11-08, ADR заморожены) |
| M1 | ✅ | ✅ | done (2026-11-09, контракты заморожены) |
| M2 | ✅ | ✅ | done (2026-11-09, `045c24b`) |
| M3 | ✅ | ✅ | done (2026-11-09) |
| M4 | ✅ | ⏳ | awaiting human review |
| M5 | — | — | todo |
| M6 | — | — | todo |
| M7 | — | — | todo |
| M8 | — | — | todo |
| M9 | — | — | todo |
| M10 | — | — | todo |
| M11 | — | — | todo |
| M12 | — | — | todo |

## M4 tasks

| Task | Status | Notes |
|---|---|---|
| 1. `ServerLoop` 20 Гц с компенсацией дрейфа | done | `ServerLoop` + `IClock`/`SystemClock`; дедлайн двигается на фиксированный интервал |
| 2. `InputValidator`: дистанция, кулдаун, ресурс, лимит частоты | done | + проверка длины вектора направления |
| 3. `ISnapshotSerializer` (полный снапшот, без дельт) | done | `BinarySnapshotSerializer` (57 байт/сущность) |
| 4. Транспорт-заглушка для отладки | done | `InMemoryServerTransport` за `IServerTransport`; LiteNetLib — M5 |
| 5. Сессии, подключение/отключение | done | `GameServer` + `ClientSession`; сущность удаляется при отключении |
| 6. Интеграционный тест сервер + фейковый клиент | done | Hello → команда → тик → снапшот → состояние совпадает |

## M4: принятые решения (на ревью оператору)

1. **ADR-008: запись сущности расширена до 57 байт, `Protocol.Version` = 2.** Эскиза ADR-004 (`id/x/y/z/hp/flags`) не хватает для восстановления статов и реконсиляции (ADR-006). Документ — `docs/adr/ADR-008-snapshot-entity-record.md`, требует «ок» на Human-Gate.
2. **Валидатор не «мягкий»:** отклонённая команда не попадает в симуляцию, но пишется в лог с причиной (`rejected (Причина) — деталь`). Отдельный тест проверяет, что состояние после отказа не изменилось.
3. **Проверка длины вектора направления** (`|dir| ≤ 1.05`) — защита от «ускорения» читом; клиент обязан присылать единичный вектор.
4. **Лимит команд** — `ServerOptions.MaxCommandsPerTick = 5` (политика сервера, не геймплей; в JSON не выносится).
5. **Спавн сессий** — вдоль оси X с шагом `SpawnSpacing = 2` (политика сервера; заменится конфигом мира, когда он появится).
6. **`SimulationState.AddEntity`/`RemoveEntity`** — мир стал динамическим (подключение/отключение), симуляция по-прежнему не знает про сеть.
7. **Транспорт-абстракция сервера — `IServerTransport`** (события + `Send(connId, packet, DeliveryMode)`), в M5 её реализует LiteNetLib. `ITransport` (клиентский, заморожен в M1) не трогали.
8. **Handshake в M4 — `Hello`/`HelloAck`** с версией в заголовке; отказ по версии и `Reason.VersionMismatch` — M5 (там появляется реальная сеть).

## M4 Auto-Gate (проверено 2026-11-09)

- [x] CI зелёный: run [42585257077](https://github.com/avvasiliev888/albion-prototype/actions/runs/42585257077) на PR #5 (build + arch-checks + 116 тестов + coverage gate).
- [x] Интеграционный тест зелёный: `Hello_Move_ProducesSnapshotMatchingServerState`, снапшот совпадает с состоянием сервера.
- [x] Валидатор отклоняет: вне радиуса (`OutOfRange`), на кулдауне (`OnCooldown`), превышение частоты (`RateLimit`), плюс `DeadTarget`, `NotEnoughResource`, `DirectionTooLarge`, `UnknownEntity`.
- [x] Сервер логирует отказы с причиной — тесты проверяют `LogEntries` на `UnknownTarget`/`RateLimit`/`отклонён`.
- [x] Тик стабилен: `ServerLoopTests.HundredTicks_TakeFiveSecondsWithinTolerance` — 101 тик = 5000 мс на фейковых часах (допуск ±100 мс).
- [x] **116/116** тестов, покрытие `Simulation` — 100% line, arch-checks зелёные.

## M4 Human-Gate (ожидает оператора)

**PR:** [#5 — M4: серверный цикл 20 Гц, валидация и снапшоты](https://github.com/avvasiliev888/albion-prototype/pull/5), ветка `agent/M4/server-loop-validation`.

1. **Запустите сервер с демо-клиентом:**
   ```
   dotnet run --project src/Server -c Release
   ```
   Ожидаемо в логе: `Hello принят`, строки `[tN] tick: ...`, `[client] snapshot tick=N ... pos=(...) hp=100` и отказы `rejected (UnknownTarget) — цели 999 нет`.
2. **Ложные срабатывания:** в демо команды движения принимаются (`commands=1`, позиция растёт на 0.2), а отклоняется только заведомо невалидная цель. Юнит-тесты `ValidMove_IsAccepted`/`ValidAbility_IsAccepted` фиксируют отсутствие ложных отказов.

Ожидаемо: 116 тестов, покрытие `Simulation` 100%, arch-checks зелёные.

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
2. **Unity Package Manager не может достучаться до `packages.unity.com`** — `ETIMEDOUT` из процесса UPM, хотя хост-процесс получает `200`. Причина — сетевое ограничение дочерних процессов на этом хосте. Не критично до M10 (URP): проект собирается из `Library/PackageCache`.
3. **`bash` в PATH на этом хосте — WSL**, а не Git Bash: `tools/ci/check-architecture.sh` локально запускается через `C:\Program Files\Git\bin\bash.exe` (в CI — обычный bash). Приоритет: низкий.
4. **CRLF-предупреждения git** на Windows — косметика. Приоритет: низкий.

## Next Actions (для следующей сессии)

1. Дождаться Human-Gate M4 (демо-запуск сервера + подтверждение ADR-008), после — merge PR.
2. M5: `LiteNetLibTransport` (`ITransport`/`IServerTransport`), `tools/test-client`, режимы доставки, интеграционный тест с 30% потерь и 100 мс задержки, отказ по версии протокола.
