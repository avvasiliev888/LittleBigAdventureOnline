# Отчёт по задаче T-002 — Закрыть непокрытые строки `Simulation`: мёртвые ветки + один тест

- **Задача:** `docs/plan_for_gemma.md` → `T-002`
- **Ветка:** `agent/T-002`
- **Дата:** 2026-11-22 16:55
- **Модели:** исполнитель `ois-gemma-4-26B`, ревьюер `deepseek-v4-flash`

## Что сделано

Покрытие `Simulation.Core.Simulation` доведено с 98,97% до 100% без понижения порога
и без тестов-пустышек. Из прод-кода удалены три ветки, недостижимые по инвариантам
конфига и вызовов: проверка зоны в `ApplyTeleport`, нулевой steering в `MoveMobAway`,
повторная проверка дистанции в `TryMobAttack`. Один настоящий тест закрывает
достижимый fallback `MoveMobAway`, когда моб стоит ровно в точке цели. Поведение
игры не менялось: удалённые ветки не исполнялись ни при каком легальном конфиге.

## Изменённые файлы

| Файл | Строк +/- | Что именно |
|---|---|---|
| `src/Simulation.Core/Simulation.cs` | +2/−14 | удалены три недостижимые проверки (`ApplyTeleport`, `MoveMobAway`, `TryMobAttack`), вместо проверки зоны оставлен комментарий про инвариант `ZoneMap.ValidateCoverage` |
| `src/Tests/SimulationCoverageTests.cs` | +30/−0 | новый файл, тест `MoveMobAway_TargetOnTopOfMob_RetreatsWithFallbackDirection` (моб в точке цели → отход по fallback-направлению) |

## Гейты

| Шаг | Команда | Результат | Время |
|---|---|---|---|
| build | `dotnet build Prototype.sln` | PASS (exit 0) | 2,1 с |
| test | `dotnet test` с `/p:Threshold=100` по `Simulation` | PASS, «покрытие 100%» | 4,1 с |
| arch | `bash tools/ci/check-architecture.sh` | PASS (exit 0) | 0,0 с |
| boundaries | `bash tools/ci/check-unity-boundaries.sh` | PASS (exit 0) | 0,0 с |
| unity | Unity batchmode compile | PASS (exit 0) | 11 с |

Сводный вердикт гейтов: `ok=true`, `failed_step=null`. Логи: `docs/reports/logs/`
(`log_dir` гейтов T-002), полный прогон — `pwsh -File tools/tandem/gates.ps1 -Task T-002`.

## Вердикт ревью

`PASS` — must-fix: нет. Риск: **low**.

Ревьюер подтвердил независимой проверкой по коду, что все три удалённых блока
недостижимы: `ZoneMap` вызывает `ValidateCoverage` в конструкторе (`ZoneMap.cs:85`),
`MobDefinition` запрещает `KeepDistance > AttackRange` (`MobDefinition.cs:168`),
оба вызова `TryMobAttack` гарантируют `targetDistance ≤ AttackRange`
(`Simulation.cs:836`, `:843`), `MoveMobAway` всегда отступает на 2 единицы, поэтому
steering не нулевой. API теста проверены по месту: `TestWorld.Config/Entity/Mob/Simulation/Tick`,
`SpawnMobs`, `State.Entities`.

Замечание ревьюера (не блокирующее, учтено перед мержем): правка не закоммичена,
`src/Tests/SimulationCoverageTests.cs` — **untracked**, файл нужно добавить в git,
иначе тест не попадёт в ветку. `docs/plan_for_gemma.md` в диффе против `main` —
это коммит архитектора `d6e06fd`, а не правка исполнителя.

## Что проверить пользователю

1. `pwsh -File tools/tandem/gates.ps1 -Task T-002` → в конце лога `"ok":true` и
   «покрытие 100%», ни одного `FAIL`.
2. Debug-телепорт по карте → персонаж перемещается как раньше, за границу мира не выходит.
3. Моб вплотную к игроку → отходит на ~2 единицы; моб, стоящий ровно в точке цели,
   тоже отходит (fallback), без исключений в логе.
4. Моб бьёт игрока на дистанции удара и не бьёт вне её — поведение не изменилось.
5. `src/Tests/SimulationCoverageTests.cs` добавлен в git (`git status` не показывает `??`).

## ПК-билд

`unity-client/Builds/desktop/AlbionPrototype.exe` (собран 2026-11-22, `ok=true`;
PASS ревью был до сборки, код ветки в билд не входит — билд собран с `main`).

## Расход

Платных вызовов: 4. Оценка: $0.017. Строки в `.agent/spend.jsonl` за задачу: 4.

| # | Роль | Что | Оценка |
|---|---|---|---|
| 5 | reviewer | T-002 reviewer | $0.0067 |
| 6 | gate | T-002 unity-gates | $0.003 |
| 7 | gate | build-windows | $0.003 |
| 8 | architect | report (этот отчёт) | $0.004 |

Токены не измерялись — все четыре строки записаны с `"estimated": true`,
оценка по тарифам `.agent/config.json`.

## Ждёт решения

- [ ] «принято» на мерж ветки `agent/T-002` в `main`
- [ ] перед мержем: `git add src/Tests/SimulationCoverageTests.cs`
