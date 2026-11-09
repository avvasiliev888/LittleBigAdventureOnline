# State

Last updated: 2026-11-09 11:35 UTC
Last heartbeat: 2026-11-09 11:35 UTC
Current milestone: M6 — Unity-клиент: ввод, отображение, интерполяция
Current task: ожидание Human-Gate M6
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
| M6 | ✅ | ⏳ | awaiting human review |
| M7 | — | — | todo |
| M8 | — | — | todo |
| M9 | — | — | todo |
| M10 | — | — | todo |
| M11 | — | — | todo |
| M12 | — | — | todo |

## M6 tasks

| Task | Status | Notes |
|---|---|---|
| 1. `IInputSource` в Unity (WASD → `MoveCommand`) | done | `UnityInputSource`; ввод опрашивается по клиентским тикам 20 Гц в `ClientPipeline` |
| 2. `IView`: буфер снапшотов + интерполяция | done | `SnapshotBuffer` в `Client.Network` (чистая логика, тестируется xUnit); `UnityView` только применяет |
| 3. Простая сцена | done | клиент создаёт плоскость/свет/камеру сам при старте (`ClientRuntime`), сцена не требуется |
| 4. Камера со сглаживанием | done | `CameraFollow` с экспоненциальным сглаживанием в `LateUpdate` |
| 5. Отправка команд через `LiteNetLibTransport` | done | `ClientPipeline` поверх `ITransport` + `IInputSource` + `IView` |
| 6. Smoke-тест | done | xUnit `ClientPipelineTests`: подключение → HelloAck → команды → снапшоты → отрисовка |
| 7. CI-grep границ Unity | done | `tools/ci/check-unity-boundaries.sh` + шаг в workflow |

## M6: принятые решения (на ревью оператору)

1. **Логика интерполяции — в `Client.Network`, а не в MonoBehaviour.** `SnapshotBuffer` (2–8 снапшотов, лаг 2 тика = 100 мс) и `ClientPipeline` тестируются xUnit-ом, Unity-слой только двигает капсулы по готовым позициям. Это закрывает AI-Failure Mode M6: в `MonoBehaviour` нет движения.
2. **Клиент запускается сам** через `[RuntimeInitializeOnLoadMethod]`; мир (плоскость, свет, камера) создаётся в рантайме. Сцену/префаб править не нужно — меньше риска при импорте проекта оператором.
3. **Сборки для Unity — генерируемые плагины** `Assets/Plugins/Albion/*.dll` (`tools/unity/build-plugins.ps1`), каталог в `.gitignore`. Коммитить DLL-бинарники в репозиторий не стали: они пересобираются одной командой, а Unity не умеет ссылаться на внешние csproj.
4. **Рендер — встроенный `Standard`, не URP.** URP отложен до M10: `packages.unity.com` недоступен из процесса UPM (Discovered Issue #2), поэтому `ClientRuntime` берёт `Universal Render Pipeline/Lit`, если он есть, иначе `Standard`.
5. **Границы Unity в CI** (`check-unity-boundaries.sh`): клиент не создаёт/не тикает `Simulation` вне `Bootstrap`/`Prediction`, `View`/`Input` не двигают объекты напрямую (`transform.position +=`, `Translate`), `View` не читает ввод. Тип-зависимость от `Simulation.Core` (снапшоты, `MoveCommand`) допустима — запрещена именно авторитетная логика.
6. **Smoke-тест — xUnit, не Unity Test Framework**: пакет `com.unity.test-framework` в проекте не установлен (UPM), поэтому сквозной клиентский тест гоняется в dotnet на in-memory транспорте; вживую — Human-Gate оператора.
7. **`ConfigLoader` переехал из `Shared` в `Server` (namespace `Server`).** Причина: `Shared` для netstandard2.1 тянул пакет `System.Text.Json`, которого нет в профиле Unity — сборка `Shared.dll` не загружалась в редакторе (`Reference has errors 'Shared'`). Загрузка конфигов — серверная обязанность, клиенту она не нужна; `Shared` теперь без внешних зависимостей. Тесты используют `Server`, как и раньше.
8. **Из `unity-client/Packages/manifest.json` убран `com.unity.multiplayer.center`.** Его редакторская сборка видит глобальный namespace `Client` из наших плагинов и не компилируется (`Client.Search` vs `UnityEditor.PackageManager.Client`). Пакет — UI-окно Multiplayer Center, прототипу не нужен. Альтернатива (переименовать namespace `Client.Network`) отклонена: namespace заморожен решением оператора.

## M6 Auto-Gate (проверено 2026-11-09)

- [x] Excel-сборка C# без ошибок: `dotnet build Prototype.sln -c Release` — 0 ошибок.
- [x] Unity-скрипты компилируются: проверено отдельной сборкой против стабов UnityEngine (0 ошибок); в Unity — сборкой плеера (ниже).
- [x] Smoke-тест зелёный: `ClientPipelineTests.Pipeline_Connects_MovesAndRenders` (снапшоты доходят, персонаж двигается), `Pipeline_RequestsFullSnapshot_WhenSnapshotsStop`, `Pipeline_ReportsDisconnect_AndStopsSession`.
- [x] Интерполяция: `SnapshotBufferTests` (середина интервала, клэмпы, устаревшие снапшоты, новые сущности).
- [x] CI-grep границ Unity: `check-unity-boundaries.sh` — ok (3 проверки).
- [x] 135/135 тестов, покрытие `Simulation` 100% line, `check-architecture.sh` зелёный.
- [x] Unity-проект собирается: batchmode `ProjectSetup.BuildWindowsPlayer` → `result=Succeeded, errors=0, size=89629688 bytes` (Unity 6000.3.28f1, локально).
- [x] CI-run: [42601857536](https://github.com/avvasiliev888/albion-prototype/actions/runs/42601857536) — success (build + arch-checks + Unity boundaries + 135 тестов + coverage gate).

## M6 Human-Gate (ожидает оператора)

**PR:** [#7 — M6: Unity-клиент, ввод и интерполяция](https://github.com/avvasiliev888/albion-prototype/pull/7), ветка `agent/M6/unity-client`.

1. Собрать плагины для Unity:
   ```
   pwsh -File tools/unity/build-plugins.ps1
   ```
2. Сервер в терминале 1:
   ```
   dotnet run --project src/Server -c Release -- --udp 9050
   ```
3. Unity 6.3 → открыть `unity-client` → сцена `Assets/Scenes/SampleScene.unity` → **Play**. Клиент стартует сам.
4. Проверить:
   - капсула появляется и стоит на плоскости, HUD показывает `connected, entity 1` и растущий счётчик snapshots;
   - WASD — капсула двигается **плавно** (лаг отображения ~100 мс);
   - остановить сервер (Ctrl+C) — капсула замирает, клиент не крашится, в консоли `соединение потеряно`;
   - «искусственная задержка сервера»: на M6 серверного флага задержки нет — плавность при задержке подтверждается тестами интерполяции; живой флаг добавлю в M7 вместе с предсказанием (если нужно раньше — скажите).

## M5: закрыто

Human-Gate M5 пройден 2026-11-09: два терминала (сервер `--udp 9050` + `tools/test-client`), 298 снапшотов за сессию, корректный disconnect, 0 запросов полного снапшота (стабильный канал). PR #6 смержен (`cfccaa6`).

## Environment

| Что | Значение |
|---|---|
| ОС | Windows 11 (10.0.22631), PowerShell 7.5.4 |
| .NET SDK | 10.0.403, `global.json` фиксирует версию |
| Unity | 6000.3.28f1 LTS, `N:\Program Files\Unity Hub\Editor\6000.3.28f1` |
| Репозиторий | `https://github.com/avvasiliev888/albion-prototype` (private), `main` |
| Git LFS | 3.7.1, `--local` |
| Транспорт | LiteNetLib 2.1.4 (NuGet), UDP; порт по умолчанию 9050 |
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
2. **Unity Package Manager не может достучаться до `packages.unity.com`** — `ETIMEDOUT` из процесса UPM, хотя хост-процесс получает `200`. Не критично до M10 (URP): проект собирается из `Library/PackageCache`; в M6 это значит Built-in pipeline вместо URP. Приоритет: средний к M10.
3. **`bash` в PATH на этом хосте — WSL**, а не Git Bash: скрипты `tools/ci/*.sh` локально запускаются через `C:\Program Files\Git\bin\bash.exe` (в CI — обычный bash). Приоритет: низкий.
4. **CRLF-предупреждения git** на Windows — косметика. Приоритет: низкий.
5. **LiteNetLib 2.1.4: `NetPeer.Id` — публичное поле, не свойство**; в коде сервера используется `peer.Id`/`peer.Tag`. Приоритет: низкий.
6. **Unity не может ссылаться на csproj вне `Assets`** — плагины для клиента генерируются скриптом и не коммитятся. Перед открытием Unity нужно запускать `tools/unity/build-plugins.ps1` (описано в README). Приоритет: низкий.
7. **Плагины в `Assets/Plugins` видны всем сборкам Unity** — глобальный namespace `Client` конфликтовал с `com.unity.multiplayer.center` (ошибка `Client.Search`). Решено удалением ненужного пакета; если появится другой пакет с namespace `Client` — вернуться к вопросу переименования namespace. Приоритет: низкий (пока один случай).
8. **netstandard2.1-сборки для Unity не должны тянуть пакеты вне профиля .NET Standard 2.1** — `System.Text.Json` в `Shared` ломал загрузку `Shared.dll` в редакторе. Исправлено переносом `ConfigLoader` в `Server`. Приоритет: низкий (правило записано).

## Next Actions (для следующей сессии)

1. Дождаться Human-Gate M6 (Unity Play + сервер), после merge PR #7 — M7.
2. M7: предсказание и реконсиляция — буфер истории, локальное применение команд, `ServerAck`, `RestoreFromSnapshot` + повтор неподтверждённых команд, тесты сходимости (100 мс задержка, 20% потерь, ≤ 3 тика) и намеренного рассинхрона.
3. M7 также добавит флаг искусственной задержки/потерь на сервере (для живого Human-Gate по плавности).
