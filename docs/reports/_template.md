# Отчёт по задаче <ID> — <название>

- **Задача:** `docs/plan_for_gemma.md` → `<ID>`
- **Ветка:** `agent/<slug>`
- **Дата:** <ГГГГ-ММ-ДД ЧЧ:ММ>
- **Модели:** исполнитель `ois-gemma-4-26B`, ревьюер `deepseek-v4-flash`

## Что сделано

2–5 строк: что изменилось в поведении, а не пересказ диффа.

## Изменённые файлы

| Файл | Строк +/- | Что именно |
|---|---|---|
| `путь/файл.cs` | +12/−3 | … |

## Гейты

| Шаг | Команда | Результат | Время |
|---|---|---|---|
| build | `dotnet build Prototype.sln -c Release` | PASS | 42 с |
| test | `dotnet test src/Tests/Tests.csproj --no-build` | PASS | 18 с |
| arch | `bash tools/ci/check-architecture.sh` | PASS | 1 с |
| unity | `Unity.exe -batchmode -quit` | PASS | 8 мин |

Логи: `docs/reports/logs/<ID>-<шаг>.log`

## Вердикт ревью

`PASS` / `FAIL` — must-fix: список или «нет». Риск: low/medium/high.

## Что проверить пользователю

1. …
2. …
3. …

## ПК-билд

`unity-client/Builds/desktop/AlbionPrototype.exe` (собран <дата>, PASS ревью был до сборки)

## Расход

Платных вызовов: N. Оценка: $X.XX. Строки в `.agent/spend.jsonl` за задачу: N.

## Ждёт решения

- [ ] «принято» на мерж ветки в `main`
