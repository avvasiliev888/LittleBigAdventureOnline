Говори мне: "Здравствуйте!"каждый раз когда читаешь этот файл. Это нужно чтобы я понял, читаешь ли ты его после операции компакт.

## ЭКОНОМИЯ КОНТЕКСТА — читать первым

Замер по 10 сессиям этого проекта: **1,58 млрд токенов**, из них **92,5% —
перечитывание собственного контекста**. Средний контекст на шаге **133 тыс.**
Цена сессии = шаги × средний контекст, поэтому экономия достигается только
двумя рычагами: **меньше шагов** и **меньше контекста на шаге**.

Измеренные источники трат:

| Источник | Доля контекста инструментов |
|---|---|
| `read` (2902 вызова) | **49,7%** |
| `pwsh` (5754 вызова) | **39,5%** |
| `job_output`, `grep`, `read_image` | 7% |

**38% всех `read` — повторные.** `Simulation.cs` прочитан 177 раз лишний раз,
`ConfigLoader.cs` — 153. Это оплаченные дубли.

### Обязательно, до чтения файлов

1. **`docs/cards/<Имя>.md` — сначала карточка.** Она даёт весь публичный API
   за **600–1600 токенов** вместо **4–15 тыс.** чтения исходника.
   Есть для: Simulation, ConfigLoader, SimulationConfig, EntityState,
   ClientPipeline, ClientPrediction, UnityView, CharacterVisuals, ClientRuntime,
   UnityInputSource, InputValidator, GameServer.
2. **Файл, прочитанный в этой сессии, повторно НЕ читай.** Нужен другой
   фрагмент — `read` с `offset`/`limit`, не весь файл заново.
3. **`trail skeleton <файл>`** — API файла за ~10% цены (экономит **93%**).
   `trail ask "<вопрос>" --source` — понимание без чтения файла.
4. **`grep` до `read`.** Сначала найти строку, потом прочитать ±40 строк.

### Обязательно, при вызове команд

5. **Каждый вывод ограничивай.** `Select-Object -First 20`,
   `Select-String ... | Select-Object -First 30`.
6. **Никогда не искать в `obj/`, `Library/`, `Builds/`, `node_modules`, `.git/`.**
   Один такой `Select-String` по бинарнику дал **12,5 тыс. токенов** мусора
   в контексте, который потом перечитывался на каждом шаге.
7. **`dotnet build`/`test`** — только `2>&1 | Select-String "error|warning" | Select-Object -First 30`.
8. **`git diff`** — только `--stat` или `-- <конкретный файл>`. Полный diff
   сотен строк оседает в контексте навсегда.

### Обязательно, при работе

9. **Одна задача — одна сессия.** Не тянуть сессию через много задач: контекст
   в ней только растёт. Итог задачи — записать в `docs/`, не в чат.
10. **Проверять фактами, а не гипотезами.** Перед правкой — число из лога,
    аудита или `Probe`. Каждая неподтверждённая гипотеза стоит круга правок.
11. **Скрипт вместо рассуждения.** Если ответ можно посчитать — посчитать
    скриптом (`tools/tokens/`, `tools/unity/`), а не выводить рассуждением.

### Инструменты учёта

```
python tools/tokens/check.py          # проверка, что всё на месте
python tools/tokens/report.py --simulate   # факт расхода + прогноз экономии
python tools/tokens/benchmark.py mark "<маркер>"   # замер стоимости задачи
python tools/tokens/make_cards.py     # пересобрать карточки подсистем
```

Прогноз модели: **1,58 млрд → 605 млн токенов (−62%)** при том же объёме
работы. Подробности и расчёт — `docs/tokens-plan.md`.

## ЖЁСТКИЕ ЗАПРЕТЫ — нарушение = откат работы

Проверяются скриптом `pwsh tools/check-rules.ps1`, помнить их не требуется:
запускать перед коммитом, он падает громко.

1. `src/Simulation.Core` — без `UnityEngine` и без `MonoBehaviour` (ADR-001).
2. Геймплейные числа не хардкодить, только `config/*.json` (ADR-005).
3. В `Shared` и `Simulation.Core` нет `System.Text.Json` — только `MiniJson` (ADR-011).
4. `Protocol.Version` поднимается только вместе с ADR (ADR-004).
5. Падающий тест не отключать: чинить причину или `blocked` (см. `docs/rules.md` § 9).
6. Вне scope: гильдии, города, переработка, рейды, классы, уровни персонажа.

Остальные правила — тесты, сеть, конфиги, стиль, коммиты, Definition of Done,
приоритеты при конфликте — в `docs/rules.md`. Читать, когда задача их касается;
в каждой сессии они не нужны и потому сюда не положены.

---

<!-- trail:start -->
## Trail — repo context graph

This repo is indexed in `.trail/cache/`: small linked markdown nodes that explain each
system and carry exact file:line spans, kept in sync with the code through git.

For ANY task here — understanding how something works, finding where code lives,
or scoping a change — get context from the graph before grepping or opening
source files. Re-ask freely (it's cheap) and reuse literal identifiers you
already have (symbol, error string, file name) as the query. New to this repo?
Run `trail map` first — a token-budgeted orientation (dir clusters, hubs,
hotspots), no LLM, no key.

- Run `trail ask "<your question>" --source` → ranked nodes with the relevant
  code spans inlined (each hit's ≤8-line crux by default; `--full` for whole
  definitions when the crux isn't enough). Match the tool to the task shape:
  for understanding or editing, the top node IS the answer — cite its
  `covers:` file:line spans and edit straight from `--source`. For
  exhaustive tasks ("every occurrence / every caller of this pattern"), ranked
  results are top-N, not complete — run `trail grep "<literal>"` instead
  (exhaustive over indexed files, grouped by enclosing symbol), falling back
  to raw `grep -rn` only for unindexed files.
- `trail skeleton <file>` → every definition's signature + span, ~10× cheaper
  than reading the file; use it to skim an API surface.
- `trail callers <symbol>` gives precomputed, exact edges — who calls this.
  Add `--direction out` for what it calls, or `--depth N` to walk
  transitively for the full blast radius. For structural questions, skip
  ranking and use this directly.
- Or browse: `.trail/cache/INDEX.md` lists every node; follow the links.
- Monorepos and folders of multiple repos rank fairly across sub-projects —
  hits carry `[scope/]` labels naming which one they're from. Narrow with
  `trail ask "<task>" --in <scope>/` once you know where you're working.

If a returned span is truncated ("+N more lines"), open the file at that exact
range before finalizing. Only open source files when a node genuinely lacks a
needed detail, and then at the exact file:line the node points to — never
re-read whole files.

After big code changes, refresh the graph with `trail build` (deterministic,
no API key, $0).
<!-- trail:end -->
## Warrior animation — проверено на практике

Перед любой правкой анимации воина (поза, смерть, оружие, лук, «плывёт»)
прочитать `docs/warrior-animation-ledger.md`. Там зафиксированы:

- доказанная главная причина (состояние `Idle` ссылалось на fileID `7400000`
  вместо `internalID` клипа `1827226128182048838` — Unity молча резолвит это
  в `null`, состояние не анимирует ничего);
- список гипотез, которые **уже опровергнуты** — не проверять их повторно;
- как читать `[diag]`-лог и подпись bind pose (`head=0,535 anchorY=0,793
  hipsRot=0,0,0,0,0,0`);
- чек-лист перед сборкой клиента.

Правило, из-за нарушения которого были потрачены часы:
**сначала число из лога или `Probe`, потом правка; без числа правка не готова.**
Проверка через `-executeMethod Albion.Editor.WarriorAnimationProbe.Probe`
на копии занимает **~150 секунд** (замерено 19.11.2026), а не 11 минут —
гонять её дёшево, отговорок нет. Копию делать на `F:`, не в `%TEMP%`
(на `C:` свободно ~18 ГБ). Готовая команда и три ловушки — журнал § 9.
После любого `Fix` перечитывать ассет: `Fix` рапортует об успехе, ничего не записав.

## Ассеты Unity — проверено на практике

Перед работой с ассетами (коммит, `.meta`, клипы, зависимости, LFS) прочитать
`docs/unity-assets.md`. Там зафиксированы проверенные факты и грабли:

- Git LFS обязателен: `*.fbx`, `*.png`, `*.tga` и др. идут указателем ~131 Б
  вместо мегабайт; как проверить — в документе;
- **Unity молча резолвит битую ссылку в `null`**, без ошибки в консоли.
  `fileID: 7400000` = «первый клип в файле» (работает всегда), а конкретный
  `internalID` работает только если поле есть в `.meta`;
- каждый файл в `Assets` требует `.meta`; новый `.cs` коммитить вместе с ним;
- `AllStarCharacterLibrary` — сторонний пакет: 196 файлов (маркетинг, женские
  анимации, чужие префабы) **намеренно не в git**, не добавлять «на всякий»;
- мусор, не коммитимый никогда: `unity-client/.utmp/`, `unity-client/1/`,
  `Assets/_Recovery/`.

**Не считать это вручную.** Всё считается одним вызовом:

```
python tools/unity/asset-map.py                 # сводка по ядровым ассетам
python tools/unity/asset-map.py --deps Warrior  # зависимости по GUID
python tools/unity/asset-map.py --untracked     # что не в git и почему
python tools/unity/asset-map.py --lfs           # бинарник вне LFS
```

Тревога о потере `internalID` в шести `.fbx.meta` после `0e5caab` **закрыта
замером 19.11.2026**: все пять клипов разрешились (`isHuman=True empty=False`),
восстанавливать `.meta` не нужно. Не проверять повторно —
`docs/warrior-animation-ledger.md` § 9.