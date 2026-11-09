# State

Last updated: 2026-11-09 08:55 UTC
Last heartbeat: 2026-11-09 08:55 UTC
Current milestone: M3 — Способность, здоровье, смерть, data-driven конфиг
Current task: ожидание Human-Gate M3
Onboarding: completed

## Onboarding: completed

Онбординг завершён 2026-11-08, 14 вопросов, все ответы получены от оператора (см. `## Resolved Questions`).

## Milestones

| Milestone | Auto-Gate | Human-Gate | Status |
|---|---|---|---|
| M0 | ✅ | ✅ | done (2026-11-08, ADR заморожены) |
| M1 | ✅ | ✅ | done (2026-11-09, контракты заморожены) |
| M2 | ✅ | ✅ | done (2026-11-09, `045c24b`) |
| M3 | ✅ | ⏳ | awaiting human review |
| M4 | — | — | todo |
| M5 | — | — | todo |
| M6 | — | — | todo |
| M7 | — | — | todo |
| M8 | — | — | todo |
| M9 | — | — | todo |
| M10 | — | — | todo |
| M11 | — | — | todo |
| M12 | — | — | todo |

## M3 tasks

| Task | Status | Notes |
|---|---|---|
| 1. JSON-схема конфигов (`actors`, `abilities`) | done | `config/actors.json`, `config/abilities.json` в корне репо |
| 2. Загрузчик с валидацией | done | `Shared.Config.ConfigLoader` → `ConfigValidationException`; без дефолтов |
| 3. `AbilityCommand` (цель, `ClientTick`, `Sequence`) | done | `TargetId` |
| 4. Логика способности: дистанция, кулдаун, ресурс, урон | done | всё из конфига |
| 5. Смерть (HP ≤ 0) и респавн через N секунд | done | N из `actors.json` |
| 6. Unit-тесты | done | 69 тестов всего |

## M3: принятые решения (на ревью оператору)

1. **`Shared` теперь ссылается на `Simulation.Core`** — это уточнение таблицы зависимостей ADR-001 (там у `Shared` был только BCL). Причина: загрузчик JSON обязан построить `SimulationConfig`, а DTO-дубли внутри Core нарушили бы «Core не знает про JSON». Инвариант «`Simulation.Core` не зависит ни от чего» сохранён (проверка №2 в `check-architecture.sh`). Если оператор против — альтернатива: перенести маппинг DTO→Core в `Server`/`Tests` (дублирование ~40 строк).
2. **Ресурс не регенерируется в M3** — в плане M3 этого нет; запас восстанавливается только на респавне. Регенерация — отдельная веха.
3. **Способность нацеленная** (`TargetId`), дистанция считается в плоскости XZ, `Damage` — целое. Кулдаун в тиках = `round(cooldownSeconds * 20)`, минимум 1 тик.
4. **Мёртвая сущность** не двигается, не кастует, не может быть целью; команды для неё молча игнорируются.
5. **Респавн**: `RespawnAtTick = тик смерти + round(respawnSeconds * 20)`; на этом тике HP и ресурс полные, позиция = точка появления, кулдаун сброшен.
6. **`MoveSpeed` переехал в `ActorConfig`** (M2 держал его в `SimulationConfig`): скорость теперь per-actor и тоже из JSON.
7. **`EntitySnapshot` расширен** (actorId, spawnPosition, resource, maxResource, cooldownUntilTick, respawnAtTick) — восстановление 1:1 сохранено; провод ещё не реализован, поэтому `Protocol.Version` не поднимался (M4).
8. **Команды для неизвестного типа** по-прежнему бросают `NotSupportedException` (решение M2).

## M3 Auto-Gate (проверено 2026-11-09)

- [x] CI зелёный: run [42582357716](https://github.com/avvasiliev888/albion-prototype/actions/runs/42582357716) на PR #4 (build + arch-checks + 69 тестов + coverage gate).
- [x] Unit-тесты: **69/69** зелёные (`tools/dev/test-local.ps1`).
- [x] Тест «изменил JSON → изменилось поведение»: `ChangedJsonDamage_ChangesSimulationResult` (damage 40 → 60 HP) и `RepoConfig_DamageFromJson_IsAppliedInSimulation`.
- [x] Невалидный JSON → `ConfigValidationException` с понятным сообщением (6 тестов: битый JSON, не-массив, пропущенное поле, дубликат, висячая ссылка, отсутствующий файл).
- [x] CI-grep: проверка №5 в `tools/ci/check-architecture.sh` (геймплейные `const` в `Simulation.Core`) — чисто.
- [x] Покрытие `Simulation` — **100% line** (порог в CI не менялся).

## M3 Human-Gate (ожидает оператора)

**PR:** [#4 — M3: JSON-конфиги, способность, смерть и респавн](https://github.com/avvasiliev888/albion-prototype/pull/4), ветка `agent/M3/configs-abilities-respawn`.

1. **Измените урон и убедитесь, что поведение задаёт JSON:**
   - в `config/abilities.json` поменяйте `"damage": 25` на `40`;
   - `pwsh -File tools/dev/test-local.ps1 -Configuration Release`;
   - ожидаемо падает `RepoConfigTests.RepoConfig_Ability_HasDocumentedBalance` (ожидает 25, получил 40) — конфиг реально управляет поведением; верните 25.
2. **Проверьте отсутствие хардкода:** `tools/ci/check-architecture.sh` (нужен Git Bash: `bash` в PATH на этом хосте — WSL и падает) и глазами `src/Simulation.Core/`.

Ожидаемо: 69 тестов, покрытие `Simulation` 100%, arch-checks зелёные.

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

1. Дождаться Human-Gate M3, после — merge PR.
2. M4: серверный цикл 20 Гц, LiteNetLib, приём `MoveCommand`/`AbilityCommand`, валидация, рассылка снапшотов, нагрузочный тест 10–20 ботов.
