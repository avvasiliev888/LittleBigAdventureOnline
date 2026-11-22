# Отчёт по задаче T-005 — CLI-импорт и вердикт

- **Задача:** `docs/plan_for_gemma.md` → `T-005`
- **Ветка:** `agent/T-005` (правка лежит в рабочем дереве, не закоммичена)
- **Дата:** 2026-11-22 19:41
- **Модели:** исполнитель `ois-gemma-4-26B`, ревьюер `deepseek-v4-flash`
- **Попытка:** 2-я (1-я — 19:32, шесть ошибок CS в плане, «Факты провала» § 1–6)

## Что сделано

В `MobBodyImport` добавлены `ImportFromCommandLine()` и `JsonString(string)`; шапка файла
получила `using System.Globalization;`. Все шесть must-fix прошлой попытки закрыты по коду:
`[MenuItem]` снова на `Run()`, `runtimeAnimatorController as AnimatorController`,
JSON собирается в `StringBuilder` без `MiniJson.Serialize`, `albedo` считается через
`HasAlbedo(Material)`, после `EditorApplication.Exit(2)` стоит `return`, `Run()` не тронут.

**Поведение по-прежнему не заработало.** Правка снесла последние две строки файла —
закрывающие `}` класса и `}` namespace: `git diff --stat` даёт `+161/−2`, удалены ровно
`    }` и `}`. Unity не компилирует проект (`CS1513: } expected`), поэтому CLI-прогоны
не выполнялись, `docs/imports/WizardBody/verdict.json` не создан, ПК-билд не собран.
Дефект ровно один и он механический — не архитектурный.

## Изменённые файлы

| Файл | Строк +/- | Что именно |
|---|---|---|
| `unity-client/Assets/Editor/MobBodyImport.cs` | +161/−2 | `ImportFromCommandLine()` (~106 строк), `JsonString()` (~26), `using System.Globalization;`; **удалены** закрывающие скобки класса и namespace |

## Гейты

| Шаг | Команда | Результат | Время |
|---|---|---|---|
| build | `dotnet build Prototype.sln -c Release` | PASS (0 ошибок, 0 предупреждений) | 2,6 с |
| test | `dotnet test src/Tests/Tests.csproj --no-build` | PASS (483 теста, 0 провалов, покрытие 99,8 %) | ~4 с |
| arch | `bash tools/ci/check-architecture.sh` | PASS | 1 с |
| unity boundaries | `tools/ci/check-unity-boundaries.sh` | PASS | 1 с |
| unity compile | `tools/unity/build-windows.ps1` (гейт `build-windows`, n=21) | **FAIL** — `Scripts have compiler errors`, exit 1 | 4,2 с |
| CLI-прогоны | `-executeMethod ...ImportFromCommandLine` | не выполнялись (компиляция красная) | — |

Единственная ошибка компиляции (в логе продублирована трижды):

```
Assets\Editor\MobBodyImport.cs(822,10): error CS1513: } expected
```

Баланс скобок в файле: `open=92 close=90`, глубина на EOF = **2**. Файл обрывается на
закрытии `JsonString` и пустой строке, дальше нет ни `    }` класса, ни `}` namespace
(`Albion.Editor`).

Логи: `docs/reports/logs/T-005-build.log`, `T-005-test.log`,
`T-005-check-architecture.log`, `T-005-check-unity-boundaries.log`,
`T-005-unity.log`.

## Вердикт ревью

Вердикта нет: результаты задач в конвейер не доставлены (`[]`), ревьюер вызывался (n=20),
но JSON-вердикт не пришёл. Вывод ниже — из диффа и гейта.
Риск: **low** — код по существу готов, дефект в двух удалённых строках.

Must-fix (одна правка, тот же файл):

1. Вернуть в конец `unity-client/Assets/Editor/MobBodyImport.cs` две строки:
   `    }` (закрытие класса `MobBodyImport`) и `}` (закрытие namespace `Albion.Editor`).
   После этого повторить гейт `build-windows` и оба CLI-прогона.

Ограничение, которое надо назвать явно: правка `+161/−2` показывает, что исполнитель
вставил код «поверх» хвоста файла. Это второй раз, когда он портит структуру файла
(в 1-й попытке — атрибут `[MenuItem]` на месте тела метода). В промпте следующей
попытки формулировку «вставь в конец класса» нужно заменить на «допиши в конец файла
и проверь, что последние две строки — `    }` и `}`».

## Что проверить пользователю

1. Меню `Albion/Mobs/Import Bodies From Sets` в редакторе по-прежнему импортирует
   `red_warlord` и печатает `entry set: red_warlord -> WizardBody h=2.45`.
2. `ImportFromCommandLine` на `red_warlord` (тело `WizardBody`) → `EXIT=0`,
   `docs/imports/WizardBody/verdict.json` с `"ok": true`, `"states": 6`, `"albedo": true`.
3. Прогон с ключом `ghost_mob` → `EXIT=1`, в вердикте есть `"error"` про ненайденный ключ.
4. Повторный прогон не плодит дублей записи `red_warlord` в `MobVisuals.asset`.

## ПК-билд

**Не собран.** `unity-client/Builds/desktop/AlbionPrototype.exe` существует (667 136 байт),
но это артефакт предыдущей сборки: `LastWriteTime` 22.11.2026 19:21:27, тогда как прогон
шёл в 19:39–19:40. За результат текущего прогона его выдавать нельзя.

## Расход

Платных вызовов: 3 (n=20 reviewer, n=21 gate `build-windows`, n=22 architect).
Оценка: $0.0137 (`estimated: true`, токены не измерены). Строки в `.agent/spend.jsonl`
за задачу: 3. Итого за месяц по журналу: 22 вызова из 60, ~$0.096 из $8.

## Ждёт решения

- [ ] Заказчик: третья попытка T-005 (одна правка — две закрывающие скобки) или правка
      вручную архитектором/человеком с последующим прогоном гейтов
- [ ] «принято» на мерж ветки в `main` — **только после зелёной Unity-компиляции и EXIT=0/1
      на обоих CLI-прогонах**

## Итог (после ручной правки, 22.11.2026)

Две попытки конвейера провалились; вторая довела код до состояния «всё верно, кроме двух
закрывающих скобок». Правка вручную: дописаны `    }` класса и `}` namespace, баланс 92/92.

Проверено замерами:

- `pwsh -File tools/tandem/gates.ps1 -Task T-005` → `ok=true`: build 2.2 с, test 4.3 с («покрытие 100%»,
  483 теста), arch 0, boundaries 0, Unity 19.4 с.
- `-executeMethod … -mobKey red_warlord … -height 2.45` → `EXIT=0`, `docs/imports/WizardBody/verdict.json`
  с `"ok": true`, `"states": 6`, `"albedo": true`, `"materials_ok": true`.
- `-mobKey ghost_mob` → `EXIT=1`, `error` = «Ключ моба 'ghost_mob' не найден в MobVisuals.asset…».
  Артефакт негативного теста (`docs/imports/GhostBody`) удалён.

Урок для промпта: «вставь в конец класса» дважды привело к потере хвоста файла. Формулировать так:
«допиши в конец файла; последние две строки должны быть `    }` и `}`; проверь баланс скобок».
