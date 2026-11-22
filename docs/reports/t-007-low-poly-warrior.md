# Отчёт по задаче T-007 — Low Poly Warrior на существующем мобе

- **Задача:** `docs/plan_for_gemma.md` → `T-007`
- **Ветка:** `agent/T-007`
- **Дата:** 2026-11-22 23:19
- **Модели:** исполнитель `ois-gemma-4-26B`, ревьюер `deepseek-v4-flash`
- **Попытка:** 1-я, PASS

## Что сделано

Тело `LowPolyWarrior` из пакета `asoliddev - Low Poly Fantasy Warrior` импортировано в игру
одной CLI-командой `MobBodyImport.ImportFromCommandLine` для моба `blue_scavenger`. Импорт
прошёл проверки A–F: `docs/imports/LowPolyWarrior/verdict.json` содержит `"ok": true`,
`"states": 6`, `"materials_ok": true`. В `MobVisuals.asset` изменена ровно одна запись —
`blue_scavenger`: `Height = 2.45`, `AllowFlatColour = 1`. Сгенерированы префаб (humanoid-риг,
кости смаплены) и контроллер на 6 состояний; все 10 `m_Materials` разрешаются в URP Lit.
Кода не меняли — только прогон и сгенерированные ассеты.

## Изменённые файлы

61 файл, все — ассеты и отчёты импорта; ни одного `.cs`.

| Файл | Строк +/- | Что именно |
|---|---|---|
| `unity-client/Assets/Resources/Characters/MobVisuals.asset` | правка | `blue_scavenger`: `Height = 2.45`, `AllowFlatColour = 1`; других записей не тронуто |
| `unity-client/Assets/Art/Characters/Generated/LowPolyWarrior.prefab` | новые | префаб тела, корень с `Animator` → сгенерированный контроллер, 10 материалов URP Lit |
| `unity-client/Assets/Art/Characters/Generated/LowPolyWarriorController.controller` | новый | контроллер на 6 состояний; все clip `fileID` присутствуют в `Knight.fbx.meta` |
| `unity-client/Assets/Art/Characters/LowPolyWarrior/` (FBX, 26 `.mat`/`.meta`) | новые | скопированный пакет тела и материалы |
| `docs/imports/LowPolyWarrior/verdict.json` | новый | вердикт импорта: `ok=true`, `states=6`, `albedo=false`, `materials_ok=true` |

Лог импорта: `docs/reports/logs/t007-import.log` (75 КБ) — `flat colour body: True`,
`prefab saved`, ни одного `error CS`/`Exception`.

## Гейты

| Шаг | Команда | Результат | Время |
|---|---|---|---|
| build | `dotnet build Prototype.sln -c Release` | PASS (exit 0) | 2,2 с |
| test | `dotnet test src/Tests/Tests.csproj --no-build` | PASS (exit 0, покрытие 100 %) | 4,1 с |
| arch | `tools/ci/check-architecture.sh` | PASS (exit 0) | — |
| unity boundaries | `tools/ci/check-unity-boundaries.sh` | PASS (exit 0) | — |
| unity compile | `tools/unity/build-windows.ps1` | PASS (exit 0) | 11,3 с |

`Unity.exe` до запуска гейтов не был запущен — убивать процессы не пришлось.
Логи: `docs/reports/logs`.

## Вердикт ревью

`PASS`, риск **medium**, must-fix: нет.

Все четыре критерия подтверждены фактами: `verdict.json` (`ok`, 6 состояний, `materials_ok`);
запись `blue_scavenger` в `MobVisuals` (Height 2.45, AllowFlatColour 1, guid префаба и fileID
корня сходятся); префаб humanoid (`animationType=3`, кости смаплены), Animator ссылается на
сгенерированный контроллер, 10 `m_Materials` резолвятся в URP Lit (тот же шейдер, что у KayKit),
пустых нет; в логе есть `flat colour body: True` и `prefab saved`, `error CS` нет.

Замечание на будущее (не блокер): все 13 `.mat` — дефолтный серый `_BaseColor 0.8` без текстур,
цвета пакета (`skin`, `hair black`, `leather`, `steel`) не перенесены, поэтому тело будет
однотонно-серым, а `AllowFlatColour=1` это молча пропускает (`ledger`: проверка только
«не белый ≥ 0.99»). Дополнительно: в коммит попали 13 неиспользуемых дублей материалов без
префикса `M_`.

## Что проверить пользователю

1. Запустить ПК-билд, найти моба `blue_scavenger` → у него новое тело Low Poly Warrior ростом
   ~2.45, не T-pose и не провалено в землю.
2. Посмотреть анимации: покой, бег, удар мечом переключаются (6 состояний), не «плывёт» и не
   скользит по земле.
3. Убедиться, что тело ожидаемо однотонно-серое (цвета пакета не перенесены — замечание ревью).
4. Убить и дать мобу заново заспавниться → тело и анимации восстанавливаются без ошибок в логе.
5. Проверить, что остальные мобы не изменились (тронута только запись `blue_scavenger`).

## ПК-билд

`unity-client/Builds/desktop/AlbionPrototype.exe` — собран после зелёных гейтов (`ok=true`),
PASS ревью был до сборки.

## Расход

Платных вызовов: 4 (n=32 reviewer, n=33 gate `unity-gates`, n=34 gate `build-windows`,
n=35 architect `report`). Оценка: $0.015 (`estimated: true`, токены не измерены).
Строки в `.agent/spend.jsonl` за задачу: 4. Итого за месяц по журналу: 35 вызовов из 60,
~$0.159 из $8.

## Ждёт решения

- [ ] «принято» на мерж ветки `agent/T-007` в `main`
- [ ] (опционально) задача на перенос цветов пакета: 13 `.mat` вместо дефолтного серого,
      удалить неиспользуемые дубли без префикса `M_`
