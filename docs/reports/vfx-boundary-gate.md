# Отчёт по задаче T-001 — Узаконить движение частиц в проверке границ

- **Задача:** `docs/plan_for_gemma.md` → `T-001` (фича `vfx-boundary-gate`)
- **Ветка:** `agent/T-001`
- **Дата:** 2026-11-22 15:20
- **Модели:** исполнитель `ois-gemma-4-26B`, ревьюер `deepseek-v4-flash`

## Что сделано

Проверка границ Unity (`tools/ci/check-unity-boundaries.sh`) снова даёт exit 0 на
текущем дереве. Причина падения — `unity-client/Assets/Scripts/View/FrostVfx.cs:369`
(`transform.position += _velocity * deltaTime` в `Update`): осколок льда летит сам,
потому что это частица эффекта, а не игровая сущность с позицией из снапшота.
Исключение узкое — по basename `*Vfx.cs`; правило 2 продолжает ловить движение в
обычных View/Input-скриптах (`WarriorView.cs`, `UnityView.cs` и т.п.).
Правила 1 и 3 и `.github/workflows/ci.yml` не тронуты.

## Изменённые файлы

| Файл | Строк +/- | Что именно |
|---|---|---|
| `tools/ci/check-unity-boundaries.sh` | +2/−1 | В блоке «== 2. View/Input не двигают объекты сами» к `grep -RInE` добавлен фильтр `\| grep -vE '/([A-Za-z0-9_]+)Vfx\.cs:'` и комментарий, почему частицы — не игровое состояние |

## Гейты

| Шаг | Команда | Результат | Время |
|---|---|---|---|
| build | `dotnet build Prototype.sln -c Release` | PASS (0 ошибок, 0 предупреждений) | 2 с |
| test | `dotnet test src/Tests/Tests.csproj --no-build` | PASS (480 пройдено, 0 не пройдено) | ~1 с |
| arch | `bash tools/ci/check-architecture.sh` | PASS | ~1 с |
| unity | `Unity.exe -batchmode -quit` (компиляция скриптов) | PASS (`Tundra build success`, `error CS` нет) | 20,5 с |

Отдельно: `bash tools/ci/check-unity-boundaries.sh` → exit 0, печатает
`ok: прямого движения в View/Input нет`.

Логи: `docs/reports/logs/T-001-<шаг>.log`

## Вердикт ревью

`PASS` — must-fix: нет. Риск: low.

Что проверено ревьюером: под Git Bash (тем самым, что выбирает `gates.ps1`) скрипт
даёт exit 0; dry-run без `grep -v` действительно падает на `View/FrostVfx.cs:369`;
синтетические `WarriorView.cs` / `UnityView.cs` фильтр не отбрасывает — правило 2
живое. Scope чистый: 1 файл, +2/−1, правила 1 и 3 и `ci.yml` не тронуты.

Замечание без блокировки: рабочая копия файла перезаписана в CRLF (в базе был LF).
Git при коммите хранит LF (`hash-object` с фильтром = `382af37`), поэтому Linux-CI и
Git Bash проходят, но WSL-заглушка bash падает на строке 4
(`set: pipefail: invalid option name`). Для гигиены стоит вернуть LF.

## Что проверить пользователю

1. `bash tools/ci/check-unity-boundaries.sh` → видно `ok: прямого движения в View/Input нет`, код возврата 0.
2. `grep -n "Vfx" tools/ci/check-unity-boundaries.sh` → исключение на месте, есть комментарий.
3. Убрать `grep -vE` из строки правила 2 и запустить скрипт → снова падает на `FrostVfx.cs:369` (исключение не «заливает» правило).
4. `file tools/ci/check-unity-boundaries.sh` (или `git diff --stat`) → файл в LF, посторонних правок нет.
5. Запустить билд `AlbionPrototype.exe` — эффект осколков льда по-прежнему двигается.

## ПК-билд

`unity-client/Builds/desktop/AlbionPrototype.exe` (собран 2026-11-22 15:16, PASS ревью был до сборки)

## Расход

Платных вызовов: 4 — ревьюер ($0.0067), гейт-раннер `unity-gates` (оценка $0.003),
сборка `build-windows` (оценка $0.003), архитектор-отчёт ($0.0058). Итого ≈$0.018.
Строк в `.agent/spend.jsonl` за задачу: 4 (2 измеренные, 2 с `estimated: true`:
в этом прогоне архитектор не получил журнал вызовов — исправлено в оркестраторе).
Локальный исполнитель `ois-gemma-4-26B` в лимит вызовов не входит; его токены в `usd` не считались (цена локальной модели нулевая).
Месячный лимит: 4 из 60 вызовов, ≈$0.018 из $8.

## Ждёт решения

- [ ] «принято» на мерж ветки `agent/T-001` в `main`
- [ ] (необязательно) вернуть LF для `tools/ci/check-unity-boundaries.sh`, чтобы WSL-заглушка bash не падала на `set -o pipefail`
