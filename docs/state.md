# State

Last updated: 2026-11-09 16:35 UTC
Last heartbeat: 2026-11-09 16:35 UTC
Current milestone: M9 — Data-driven контент в Unity-клиенте
Current task: Auto-Gate пройден, PR открыт, ожидание Human-Gate M9
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
| M4 | ✅ | ✅ | done (2026-11-09, `d827c9a`, ADR-008 одобрен) |
| M5 | ✅ | ✅ | done (2026-11-09, merged `cfccaa6`) |
| M6 | ✅ | ✅ | done (2026-11-09, merged `d292d27`) |
| M7 | ✅ | ✅ | done (2026-11-09, merged `9206efe`, PR #8) |
| M8 | ✅ | ✅ | done (Human-Gate пройден 2026-11-09, PR #9 ждёт merge) |
| M9 | ✅ | ⏳ | awaiting human review (PR #10) |
| M10 | — | — | todo |
| M11 | — | — | todo |
| M12 | — | — | todo |

## M9 tasks

| Task | Status | Notes |
|---|---|---|
| 1. Копирование JSON в `StreamingAssets` при сборке | done | `tools/unity/build-plugins.ps1` копирует `config/*.json` → `Assets/StreamingAssets/Config`; каталог генерируемый и не коммитится |
| 2. Загрузка конфигов на клиенте тем же загрузчиком | done | `ConfigLoader` переехал в `Shared` (netstandard2.1), `ClientContent` в `Client.Network` грузит `StreamingAssets/Config` |
| 3. Unity-совместимый парсер вместо System.Text.Json | done | `Shared/MiniJson.cs`: объекты/массивы/строки/числа, комментарии, висячие запятые, ошибки с позицией (ADR-011) |
| 4. Использование конфига для предсказания (радиус, кулдаун) | done | `ClientPrediction` из `ActorConfig`/`AbilityConfig`: скорость, `AbilityRange`, `AbilityCooldownTicks`, `PredictAbility`; реконсиляция заменяет кулдаун серверным по ack |
| 5. Использование конфига для UI (HP, кулдаун) | done | HUD: `cfg: <hash8>`, `hp`, `cd: N тиков`, `range`; Space → `AbilityCommand` (ближайшая цель, иначе сам) |
| 6. Хеш конфига как проверка «один файл» | done | `ConfigLoader.ComputeHash` (SHA-256), сервер печатает hash при старте, клиент — в лог и HUD |
| 7. Тест «изменил JSON → изменилось поведение клиента» | done | `ClientContentTests.ChangedMoveSpeedJson_ChangesPredictionStep_WithoutRecompile`, `ChangedCooldownJson_ChangesPredictedCooldown_WithoutRecompile` |
| 8. Тест «клиент и сервер используют один файл» | done | `ClientContentTests.ClientAndServerReadSameFile_SameHash`, `ChangedJsonFile_ChangesHash` |
| 9. Исправления по итогам первой вживую (Human-Gate): ввод Space, локальный кулдаун, часы кулдауна | done | латч нажатия в `UnityInputSource.PollFrame`, гейт `TryStartAbility` в конвейере, кулдаун в относительных тиках, тесты `Pipeline_IgnoresAbilityWhileCooldownActive_AndFiresAfterIt` и др. |

## M9: принятые решения (на ревью оператору)

1. **Загрузчик в `Shared`, без System.Text.Json (ADR-011).** `ConfigLoader` собирается под netstandard2.1 и грузится в Unity; `System.Text.Json` из Unity-сборок исключён архитектурно (Discovered Issue #8 закрыт). `Server/ConfigLoader.cs` удалён, сервер и клиент идут через один код.
2. **Собственный минимальный JSON-парсер.** `MiniJson` поддерживает ровно нужную грамматику (объекты, массивы, escape/`\u`, комментарии `//` и `/* */`, висячие запятые) и даёт ошибки с позицией. Это не общий JSON-движок, а часть валидации конфигов; глубина ограничена 64.
3. **Хеш вместо протокола.** `ComputeHash` (SHA-256 по двум файлам) печатается сервером и клиентом, виден в HUD (`cfg: …`). Несовпадение не блокирует соединение (протокол остался v4), но пишется в лог; тесты сравнивают хеши, посчитанные серверным и клиентским путём.
4. **Доставка файлов — шаг сборки.** `build-plugins.ps1` копирует JSON в `StreamingAssets/Config`; JSON в репозитории существует в одном экземпляре (`config/`). Забыли скрипт — клиент явно показывает `config error: …`, тихого фолбэка на константы нет.
5. **Что берётся из конфига.** Предсказание движения — `MoveSpeed` актора; кулдаун — `CooldownSeconds` (в тиках через `ceil(sec/0.05)`), `PredictAbility` ставит кулдаун сразу, реконсиляция откатывает его по ack, если сервер команду отклонил; радиус — `Range`; UI — `maxHp` актора, `hp` из снапшота, кулдаун и хеш.
6. **Space как способность (M9, минимум).** Ввод: Space → `AbilityCommand` по ближайшей сущности, иначе по себе (distance 0 ≤ radius, сервер разрешает). Это даёт наблюдаемые кулдаун/урон в HUD без NPC; полноценный таргетинг — вне M9.

## M9 Auto-Gate (проверено 2026-11-09)

- [x] Тест «изменил JSON → изменилось поведение клиента» зелёный: `ChangedMoveSpeedJson_ChangesPredictionStep_WithoutRecompile` (4 → 8 ед/с, шаг 0.2 → 0.4 без перекомпиляции), `ChangedCooldownJson_ChangesPredictedCooldown_WithoutRecompile` (2 → 5 с, 40 → 100 тиков).
- [x] Клиент и сервер используют один файл: `ClientAndServerReadSameFile_SameHash` (SHA-256 совпадает), `ChangedJsonFile_ChangesHash` (правка файла меняет хеш).
- [x] Загрузчик и парсер: `MiniJsonTests` (валидные документы, комментарии, escape, ошибки с позицией), `ConfigTests` (нет дефолтов, объяснимые ошибки), `ClientContentTests` (загрузка, hash, предсказание).
- [x] Конфиг реально управляет предсказанием и UI: `Pipeline_UsesConfigForPredictionAndAbilityCooldown` — `AbilityCooldownTicks=40`, `AbilityRange=3`, после Space кулдаун появляется сразу, сервер применяет способность (hp < maxHp), кулдаун подтверждается.
- [x] `dotnet build` всех проектов без ошибок (в temp-копии из-за Win32Exception хоста); **202/202** теста, покрытие `Simulation` 100% line, arch-checks и Unity-boundaries зелёные.
- [x] Плагины и конфиги для Unity собраны: `tools/unity/build-plugins.ps1` → 4 DLL + `StreamingAssets/Config/{actors,abilities}.json`.
- [x] Сервер печатает `config: <path> (акторов: 1, способностей: 1), hash: <sha256>`; клиент логирует тот же хеш (проверено в headless-тесте и live-прогоном).
- [x] Unity batchmode-сборка не перезапускалась: Editor открыт оператором; скрипты компилируются (stub), Play-проверка — на Human-Gate.
- [x] CI-run: [проверки PR #10](https://github.com/avvasiliev888/albion-prototype/pull/10/checks) — success (202/202 тестов после исправлений Human-Gate, coverage gate).

## M9 Human-Gate (ожидает оператора)

**PR:** #10 — M9: data-driven контент в Unity-клиенте, ветка `agent/M9/data-driven-content`.

1. Плагины и конфиги: `pwsh -File tools/unity/build-plugins.ps1` (копирует JSON в `StreamingAssets/Config`).
2. Сервер: `dotnet run --project src/Server -c Release -- --udp 9050` — в логе строка `config: … hash: <sha256>`.
3. Unity 6.3 → `unity-client` → дождаться реимпорта → `Assets/Scenes/SampleScene.unity` → Play. В HUD видно `cfg: <первые 8 символов хеша>` — он должен совпадать с серверным.
4. Проверить способность: стоя на месте нажать Space — HP падает на 25, в HUD `cd: 40` и уменьшается до 0; во время кулдауна Space игнорируется (HP не меняется, команда не уходит); по истечении 2 с Space снова работает. В движении — то же самое.\n5. Проверить data-driven: изменить `config/abilities.json` (`cooldownSeconds: 2.0` → `6.0`, `damage: 25` → `5`) **и** `config/actors.json` (`moveSpeed: 4.0` → `6.0`), перезапустить сервер, снова `build-plugins.ps1`, перезапустить Play (код не перекомпилируется).
   - движение стало быстрее (6 ед/с);
   - после Space кулдаун в HUD ~120 тиков вместо 40, HP падает на 5, а не на 25;
   - `cfg:` хеш изменился.
5. Проверить отсутствие тихого фолбэка: временно переименовать `StreamingAssets/Config` → в HUD `config error: …` с подсказкой про скрипт, клиент не подключается.
6. Space по себе — ожидаемо (других сущностей нет); HP восстанавливается только респавном/новым подключением, это прототип M9.

## M9 Human-Gate: исправления после первой проверки (2026-11-09)

Оператор проверил M9 вживую и нашёл 4 связанных дефекта. Разбор и исправления:

1. **Space стоя на месте «не срабатывает».** `UnityInputSource` читал `Input.GetKeyDown(Space)` только внутри клиентского тика (20 Гц), а Unity вызывает `Update` ~60 раз в секунду: фронт нажатия терялся на кадрах, где тик не выполнялся (стоя на месте это почти всегда, в движении — «через раз»). **Исправление:** `PollFrame()` вызывается каждый кадр из `ClientRuntime.Update` и запоминает нажатие; `TryReadCommand` забирает его на ближайшем тике. Способность больше не зависит от движения.
2. **Кулдаун в HUD «cd: 3098 тиков».** `ClientPrediction` хранил абсолютный тик `serverTick + 40`, а `CooldownRemainingTicks` вычитал клиентский тик, который начинается с нуля при каждом Play. **Исправление:** кулдаун хранится в относительных тиках и уменьшается на 1 каждый клиентский тик (`AdvanceCooldown`); при реконсиляции серверный `CooldownUntilTick` переводится в остаток как `cooldownUntil - snapshot.Tick`. Тест `Reconcile_MapsServerCooldownToClientTicks_NotAbsoluteTick`.
3. **Спам Space во время кулдауна.** Клиент отправлял команду на каждое нажатие и только сервер её отбрасывал. **Исправление:** `ClientPipeline` проверяет `ClientPrediction.TryStartAbility` **до отправки**; если кулдаун идёт — команда не уходит, предсказание не накручивается. Тест `Pipeline_IgnoresAbilityWhileCooldownActive_AndFiresAfterIt` (пока кулдаун идёт — 0 новых команд и HP не меняется; после истечения — снова урон).
4. **«Локальный урон во время кулдауна».** Локального применения урона в клиенте нет: HP приходит только из серверных снапшотов (`Simulation.ApplyAbility`), сервер проверяет `CooldownUntilTick` (строка 188). Наблюдаемый эффект давал баг №2: HUD показывал «кулдаун 3000+ тиков» постоянно, и каждый успешный каст (раз в 2 с) выглядел как удар «во время кулдауна». После исправления часов и гейта урон в HUD идёт строго раз в конфиг-кулдаун. `LocalPredictionSmoother` кулдаун/урон не считает и не должен: он отвечает только за сглаживание позиции.

## M8: Human-Gate пройден

M8 проверен вживую 2026-11-09: движение плавное, дельты не сломали предсказание; `delta` растёт почти каждый тик, `fullReq` низкий; Ctrl+C сервера — капсула замирает, Unity не крашится. PR #9 ждёт Squash and merge (мержит оператор).

## M7: закрыто

Human-Gate M7 пройден 2026-11-09: оператор подтвердил плавное движение, мгновенную остановку, «острое» управление и корректный dead reckoning; PR #8 смержен (`9206efe`). Одобрены ADR-009 (ack + предсказание только движения) и раздельная отрисовка локального/удалённых игроков.

## M6: закрыто

Human-Gate M6 пройден 2026-11-09: оператор подключился из Unity, капсула двигалась плавно по WASD, сервер остановлен без краша клиента. PR #7 смержен (`d292d27`). Одобрены перенос `ConfigLoader` в `Server` и удаление `com.unity.multiplayer.center`.

## Environment

| Что | Значение |
|---|---|
| ОС | Windows 11 (10.0.22631), PowerShell 7.5.4 |
| .NET SDK | 10.0.403, `global.json` фиксирует версию |
| Unity | 6000.3.28f1 LTS, `N:\Program Files\Unity Hub\Editor\6000.3.28f1` |
| Репозиторий | `https://github.com/avvasiliev888/albion-prototype` (private), `main` |
| Git LFS | 3.7.1, `--local` |
| Транспорт | LiteNetLib 2.1.4 (NuGet), UDP; порт по умолчанию 9050; **protocol v4** |
| Unity-клиент | `unity-client/`, скрипты в `Assets/Scripts`, плагины и JSON-конфиги генерируются `tools/unity/build-plugins.ps1` |
| Конфиги | `config/actors.json`, `config/abilities.json` — единственный экземпляр; клиент получает копию в `StreamingAssets/Config` при сборке |

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
2. **Unity Package Manager не может достучаться до `packages.unity.com`** — `ETIMEDOUT` из процесса UPM, хотя хост-процесс получает `200`. Не критично до M10 (URP): проект собирается из `Library/PackageCache`; в M6–M9 это значит Built-in pipeline вместо URP. Приоритет: средний к M10.
3. **`bash` в PATH на этом хосте — WSL**, а не Git Bash: скрипты `tools/ci/*.sh` локально запускаются через `C:\Program Files\Git\bin\bash.exe` (в CI — обычный bash). Приоритет: низкий.
4. **CRLF-предупреждения git** на Windows — косметика. Приоритет: низкий.
5. **LiteNetLib 2.1.4: `NetPeer.Id` — публичное поле, не свойство**; в коде сервера используется `peer.Id`/`peer.Tag`. Приоритет: низкий.
6. **Unity не может ссылаться на csproj вне `Assets`** — плагины для клиента генерируются скриптом и не коммитятся. Перед открытием Unity нужно запускать `tools/unity/build-plugins.ps1` (описано в README). Приоритет: низкий.
7. **Плагины в `Assets/Plugins` видны всем сборкам Unity** — глобальный namespace `Client` конфликтовал с `com.unity.multiplayer.center`; пакет удалён. Приоритет: низкий (пока один случай).
8. **Закрыто в M9: System.Text.Json больше не участвует в Unity-сборках.** `ConfigLoader` переехал в `Shared` и использует собственный `MiniJson` (ADR-011), поэтому конфликт netstandard2.1-профиля снят архитектурно. Приоритет: снят.
9. **ADR-006 в части «копия `Simulation` на клиенте» заменён ADR-009** — клиент предсказывает только движение по `moveSpeed` из HelloAck. **M9: конфиг синхронизирован файлом и хешем; предсказание способностей сведено к кулдауну.**
10. **Клиент знает только `moveSpeed`** — в M9 клиент читает весь конфиг; если `moveSpeed` клиента расходится с HelloAck, в лог пишется `[config] клиент и сервер расходятся…`. Приоритет: низкий.
11. **История снапшотов клиента — 16 тиков (0.8 с).** При задержке сети больше 0.8 с каждая дельта теряет базу и клиент переходит на полные снапшоты (работает, но трафик растёт). Приоритет: низкий (100 мс в Human-Gate покрыты).
12. **Первый снапшот сессии — всегда полный и надёжный.** На подключении это небольшой всплеск трафика; альтернатива (сразу дельта) невозможна без базы. Приоритет: низкий.
13. **JSON в `StreamingAssets` — артефакт сборки.** Забыли `build-plugins.ps1` — клиент явно сообщает `config error` и не подключается. Приоритет: низкий (есть диагностика).
14. **Space по себе наносит урон себе** — в M9 нет других сущностей, цель выбирается как ближайшая, иначе сам. Полноценный таргетинг/боевой UI — M10+. Приоритет: средний к M10.

## Next Actions (для следующей сессии)

1. Дождаться Human-Gate M9; после merge PR #10 — M10 (URP, стилизация, два профиля качества).
2. M10: URP и материалы, LOD/occlusion, desktop/mobile профили, замена капсул ассетами; учесть, что UPM на этом хосте не ходит в сеть (Issue #2).
3. При появлении таргетинга способностей — расширить `ClientPrediction` (радиус уже из конфига) и убрать самоцель по Space.
