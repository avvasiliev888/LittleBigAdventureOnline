# State

Last updated: 2026-11-09 16:30 UTC
Last heartbeat: 2026-11-09 16:30 UTC
Current milestone: M7 — Prediction и reconciliation
Current task: ожидание Human-Gate M7
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
| M7 | ✅ | ⏳ | awaiting human review |
| M8 | — | — | todo |
| M9 | — | — | todo |
| M10 | — | — | todo |
| M11 | — | — | todo |
| M12 | — | — | todo |

## M7 tasks

| Task | Status | Notes |
|---|---|---|
| 1. Буфер истории предсказания | done | очередь неподтверждённых команд + `ClientPrediction`; полная копия `Simulation` не нужна (ADR-009) |
| 2. Локальное применение команды | done | `ClientPrediction.ApplyLocalCommand` — `position += direction * moveSpeed * 0.05` |
| 3. Обработка `ServerAck` и снапшота | done | ack-история по тикам (40), пара «тик + sequence» |
| 4. Реконсиляция откатом, не телепортом | done | база — серверная позиция, повтор команд с `sequence > ack`; тест «позиция = предсказанная, не серверная» |
| 5. Тест сходимости при 100 мс и 20% потерь | done | `Pipeline_ConvergesWithDelayAndLoss` |
| 6. Тест намеренного рассинхрона | done | `ClientPredictionTests.Reconcile_DeliberateDesync_ConvergesToServer` + `..._EndsAtPredictedPositionNotServerPosition` |
| 7. Флаг искусственной задержки/потерь | done | `--udp 9050 --delay 100 --loss 20`, задержка в обе стороны |

## M7: принятые решения (на ревью оператору)

1. **Протокол версии 3 (ADR-009).** В `HelloAck` добавлена `moveSpeed` (полезная нагрузка 16 байт), появилось сообщение `ServerAck = 7` (12 байт: entityId, acknowledgedSequence, tick). Запись сущности ADR-008 не менялась. Старые клиенты (v2) получат отказ в соединении — это ожидаемо.
2. **Отступление от ADR-006: предсказание без копии `Simulation`.** Клиент не может построить `SimulationConfig` (в M6 `ConfigLoader` уехал в `Server`, потому что `System.Text.Json` не грузится в Unity). Поэтому `ClientPrediction` повторяет только правило движения (`direction * moveSpeed * dt`), скорость приходит в HelloAck. ADR-006 п.1–2 (копия симуляции + буфер 128 тиков) в этой части заменены ADR-009; предсказывается только движение (ADR-006 п.8), корректность подтверждается тестом сходимости.
3. **Подтверждение по `Sequence`, а не по клиентскому тику.** Сервер сдвигает `AcknowledgedSequence` при получении любой декодируемой команды — **включая отклонённые** валидатором, иначе клиент повторял бы отклонённую команду при каждой реконсиляции.
4. **Ack отправляется до снапшота того же тика.** Клиент сверяет пару «тик + sequence» из истории ack-ов (40 тиков), поэтому задержка снапшота на 2 тика не ломает реконсиляцию и не даёт ложных срабатываний.
5. **Локальный игрок — без задержки (dead reckoning), удалённые — буфер 100 мс.** `LocalPredictionSmoother` (в `Client.Network`, юнит-тесты): позиция сразу по последнему предсказанию, экстраполяция по времени между тиками по скорости из отправленной команды (горизонт 1 тик), сэмплы каждый клиентский тик — тайминг не зависит от джиттера сети; коррекции «назад» гасятся визуальным оффсетом ~120 мс, расхождения < 0.02 игнорируются, телепорт только при разрыве > 2 единиц. Удалённые игроки — `SnapshotBuffer` с `LagTicks = 2` (100 мс), как в M6. `CameraFollow` уже работал в `LateUpdate` и не менялся.
6. **Имитация сети — флаги сервера.** `--delay <мс>` (в тиках, задержка в обе стороны) и `--loss <процент>` (только ненадёжные пакеты). Для Human-Gate: `--udp 9050 --delay 100 --loss 20`.

## M7 Auto-Gate (проверено 2026-11-09)

- [x] Тест сходимости зелёный: `Pipeline_ConvergesWithDelayAndLoss` — 100 мс, 20% потерь, после остановки ввода ошибка ≤ 0.01 за ≤ 20 тиков.
- [x] Тест намеренного рассинхрона зелёный: `Reconcile_DeliberateDesync_ConvergesToServer`, `Reconcile_AfterDesync_EndsAtPredictedPositionNotServerPosition`.
- [x] Лог клиента содержит записи о реконсиляции: `ClientPipeline.LogEntries` (`[reconcile] tick=… error=… predicted=… server=…`) + `Debug.Log` в Unity.
- [x] При стабильной сети реконсиляция не срабатывает: `Pipeline_StableNetwork_DoesNotReconcile` (0 реконсиляций за 60 тиков).
- [x] C#-сборка без ошибок: `dotnet build Prototype.sln -c Release` — 0 ошибок.
- [x] **150/150** тестов, покрытие `Simulation` 100% line, arch-checks и Unity-boundaries зелёные.
- [x] Unity-проект собирается: batchmode `BuildWindowsPlayer` → `result=Succeeded, errors=0, size=89639416 bytes`.
- [x] Живой прогон: сервер `--udp 9061 --delay 100 --loss 20` + `tools/test-client` → protocol v3, HelloAck «скорость 4», 123 снапшота, 155 acks (последний sequence 27).
- [x] CI-run: [42624327841](https://github.com/avvasiliev888/albion-prototype/actions/runs/42624327841) — success (build + arch-checks + Unity boundaries + 150 тестов + coverage gate).

## M7 Human-Gate (ожидает оператора)

**PR:** [#8 — M7: предсказание движения, реконсиляция и флаг задержки](https://github.com/avvasiliev888/albion-prototype/pull/8), ветка `agent/M7/prediction-reconciliation`.

1. Плагины: `pwsh -File tools/unity/build-plugins.ps1`.
2. Сервер с плохой сетью: `dotnet run --project src/Server -c Release -- --udp 9050 --delay 100 --loss 20`.
3. Unity 6.3 → `unity-client` → `Assets/Scenes/SampleScene.unity` → Play.
4. Проверить:
   - WASD двигает капсулу **без ощутимой задержки** (предсказание), даже при `--delay 100`;
   - редкие плавные «подтягивания» при коррекциях, без телепортов;
   - HUD: `reconciles` растёт редко и только при рывках сети, `unacked` обычно 0–3;
   - в консоли Unity записи `ClientRuntime: реконсиляция tick=…` появляются только при реальных расхождениях, а не каждый кадр;
   - остановка сервера (Ctrl+C) — капсула замирает, клиент не крашится.
5. Для сравнения: без флагов (`--udp 9050`) `reconciles` должен остаться 0.

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
| Транспорт | LiteNetLib 2.1.4 (NuGet), UDP; порт по умолчанию 9050; **protocol v3** |
| Unity-клиент | `unity-client/`, скрипты в `Assets/Scripts`, плагины генерируются `tools/unity/build-plugins.ps1` |

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
2. **Unity Package Manager не может достучаться до `packages.unity.com`** — `ETIMEDOUT` из процесса UPM, хотя хост-процесс получает `200`. Не критично до M10 (URP): проект собирается из `Library/PackageCache`; в M6/M7 это значит Built-in pipeline вместо URP. Приоритет: средний к M10.
3. **`bash` в PATH на этом хосте — WSL**, а не Git Bash: скрипты `tools/ci/*.sh` локально запускаются через `C:\Program Files\Git\bin\bash.exe` (в CI — обычный bash). Приоритет: низкий.
4. **CRLF-предупреждения git** на Windows — косметика. Приоритет: низкий.
5. **LiteNetLib 2.1.4: `NetPeer.Id` — публичное поле, не свойство**; в коде сервера используется `peer.Id`/`peer.Tag`. Приоритет: низкий.
6. **Unity не может ссылаться на csproj вне `Assets`** — плагины для клиента генерируются скриптом и не коммитятся. Перед открытием Unity нужно запускать `tools/unity/build-plugins.ps1` (описано в README). Приоритет: низкий.
7. **Плагины в `Assets/Plugins` видны всем сборкам Unity** — глобальный namespace `Client` конфликтовал с `com.unity.multiplayer.center`; пакет удалён. Приоритет: низкий (пока один случай).
8. **netstandard2.1-сборки для Unity не должны тянуть пакеты вне профиля** — `System.Text.Json` в `Shared` ломал загрузку `Shared.dll`; исправлено переносом `ConfigLoader` в `Server`. Приоритет: низкий (правило записано).
9. **ADR-006 в части «копия `Simulation` на клиенте» заменён ADR-009** — клиент предсказывает только движение по `moveSpeed` из HelloAck. Требует подтверждения оператором; при появлении предсказания способностей (M9+) понадобится синхронизация конфигурации.
10. **Клиент знает только `moveSpeed`** — при смене актора в конфиге HelloAck должен пересчитываться; конфиг-хеш (M9) пока не сверяется.

## Next Actions (для следующей сессии)

1. Дождаться Human-Gate M7, после merge PR #8 — M8 (дельта-снапшоты).
2. M8: `SerializeDelta`/`ApplyDelta`, восстановление при потере базового снапшота, тест трафика (дельта меньше полного), тест 30% потерь.
3. M8 также стоит расширить `SimulatedLink` потерями базовых снапшотов (сейчас теряются все ненадёжные одинаково).
