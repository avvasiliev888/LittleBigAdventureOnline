# State

Last updated: 2026-11-09 16:05 UTC
Last heartbeat: 2026-11-09 16:05 UTC
Current milestone: M8 — Дельта-снапшоты и обработка пропусков
Current task: Auto-Gate пройден, PR открыт, ожидание Human-Gate M8
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
| M8 | ✅ | ⏳ | awaiting human review (PR #9) |
| M9 | — | — | todo |
| M10 | — | — | todo |
| M11 | — | — | todo |
| M12 | — | — | todo |

## M8 tasks

| Task | Status | Notes |
|---|---|---|
| 1. Формат дельты `SerializeDelta`/`ApplyDelta` | done | заголовок 12 байт: `baselineTick`, `tick`, `removedCount`+ids, `recordCount`; запись — `id`, маска полей, только изменённые поля |
| 2. Сервер: дельта от предыдущего тика | done | `_previousSnapshot` + `LastSentTick` сессии; дельта уходит только если у клиента гарантированно есть база, иначе полный (надёжно) |
| 3. Клиент: история снапшотов и применение дельты | done | `_snapshotsByTick` (16 тиков), `ApplyDelta`, затем обычный путь `IView` + реконсиляция M7 |
| 4. Потеря базы → `FullSnapshotRequest` | done | запрос надёжный, обрабатывается в `Poll` без ожидания тика, кулдаун 0.3 с от шторма запросов |
| 5. Восстановление при 30% потерь ≤ 3 тиков | done | `LossyTransportTests.BurstOfThreeLostSnapshots_RecoversWithinThreeTicks` (тик 13–15 после серии 10–12), `Pipeline_AppliesDeltas_AndRecoversGapsWithFullSnapshot` |
| 6. Тест трафика: дельта меньше полного | done | `Delta_IsSmallerThanFullSnapshot` (дельта < половины полного), `Delta_WithNoChanges_IsHeaderOnly` (12 байт) |
| 7. Тест «дельта + полный = полный» | done | `Delta_RoundTrip_EqualsFullSnapshot`, `DeltaChain_MatchesSequenceOfFullSnapshots` |
| 8. Демо-клиенты, HUD, live-прогон | done | `tools/test-client` и demo-режим сервера понимают дельты; HUD `delta/fullReq` |

## M8: принятые решения (на ревью оператору)

1. **Протокол версии 4 (ADR-010).** `MessageType.Delta = 5` (значение зарезервировано с M1) получает семантику; версия поднята до 4, потому что клиент v3 не умеет применять дельты. Как и в M7, старые клиенты получают отказ на handshake — это ожидаемо.
2. **Формат дельты.** `baselineTick:u32`, `tick:u32`, `removedCount:u16` + ids, `recordCount:u16`, затем записи `id:u32`, `mask:u16`, поля по битам маски (actor, position, spawn, hp, maxHp, resource, maxResource, cooldown, respawn, isDead). Новая сущность пишется с маской `0x3FF`. Заголовок — 12 байт; движение одной сущности — 18 байт записи; дельта на тик ≈ 30 байт против 63 байт полного снапшота (для одного игрока).
3. **Сервер: дельта — только при гарантированной базе.** `LastSentTick` сессии сравнивается с тиком `_previousSnapshot`. Совпало — дельта (ненадёжно); нет — полный снапшот (надёжно): первый после подключения, ответ на `FullSnapshotRequest`, разрыв. Дельта одинакова для всех сессий и сериализуется один раз на тик; порядок «сначала `ServerAck`, потом снапшот» сохранён.
4. **Клиент: история 16 тиков и запрос полного.** При отсутствии `baselineTick` в истории дельта отбрасывается, уходит `FullSnapshotRequest` (надёжно, кулдаун 0.3 с). Полный снапшот применяется сразу при `Poll`, не дожидаясь тика; восстановление укладывается в 1–2 тика, что и проверяют тесты.
5. **Защита от «дельты-притворяшки» (AI-Failure Mode).** Тест размера: на движении двух сущностей дельта обязана быть меньше половины полного снапшота; дельта без изменений — ровно 12 байт; roundtrip/chain-тесты гарантируют, что экономия не куплена потерей состояния.
6. **Unity-клиент не менял логику рендера.** `ClientPipeline` отдаёт во `IView` уже полный снапшот (результат `ApplyDelta`), поэтому `SnapshotBuffer`/`UnityView`/`LocalPredictionSmoother` работают как в M6–M7. В HUD добавлены `delta` и `fullReq`.

## M8 Auto-Gate (проверено 2026-11-09)

- [x] Формат дельты: `Delta_RoundTrip_EqualsFullSnapshot`, `Delta_AllFieldsChanged_StillRoundTrips`, `DeltaChain_MatchesSequenceOfFullSnapshots` — состояние совпадает с полным снапшотом по всем полям.
- [x] Трафик: `Delta_IsSmallerThanFullSnapshot` — дельта меньше половины полного снапшота на движении двух сущностей; `Delta_WithNoChanges_IsHeaderOnly` — 12 байт.
- [x] Явные отказы: `ApplyDelta_WrongBaseline_Throws`, `ApplyDelta_TruncatedPayload_Throws`, `SerializeDelta_RequiresNewerTick`, `ReadBaselineTick_ReturnsBaselineTick`.
- [x] Восстановление при 30% потерь: `Pipeline_AppliesDeltas_AndRecoversGapsWithFullSnapshot` (delta>0, fullReq>0, лаг ≤ 8 тиков с учётом 100 мс задержки) и `BurstOfThreeLostSnapshots_RecoversWithinThreeTicks` (серия 10–12, следующий применённый тик 13–15).
- [x] Надёжные пакеты не теряются: `ReliablePackets_AreNotDropped_EvenAt100PercentLoss` — при 100% потерь приходят `HelloAck` и `ServerAck`, а первый (полный) снапшот доставлен надёжно; дельта на следующем тике теряется.
- [x] **171/171** тестов, покрытие `Simulation` 100% line, arch-checks и Unity-boundaries зелёные.
- [x] Живой прогон (2026-11-09): `--udp 9064 --delay 100 --loss 20` + `tools/test-client` → protocol v4: 114 снапшотов, из них 62 дельты, 53 запроса полного; стабильная сеть (`--udp 9065`): 158 снапшотов, 157 дельт, 0 запросов полного.
- [x] Unity-проект собирается (batchmode) — см. журнал агента; при открытом Editor билд пропускается с пометкой.
- [x] CI-run: [42651125133](https://github.com/avvasiliev888/albion-prototype/actions/runs/42651125133) — success (build + arch-checks + Unity boundaries + 171 тестов + coverage gate).

## M8 Human-Gate (ожидает оператора)

**PR:** #9 — M8: дельта-снапшоты и восстановление при пропусках, ветка `agent/M8/delta-snapshots`.

1. Плагины: `pwsh -File tools/unity/build-plugins.ps1`.
2. Сервер с плохой сетью: `dotnet run --project src/Server -c Release -- --udp 9050 --delay 100 --loss 20`.
3. Unity 6.3 → `unity-client` → `Assets/Scenes/SampleScene.unity` → Play.
4. Проверить:
   - движение по WASD остаётся плавным, без «залипаний» и телепортов — дельты не ломают предсказание;
   - HUD: `snapshots` растёт, `delta` растёт почти каждый тик, `fullReq` изредка увеличивается при потерях;
   - при 30% потерь картинка не замирает и не «догоняет» рывком дольше ~0.2 с;
   - при стабильной сети (`--udp 9050` без флагов) `fullReq` не растёт после первых снапшотов, `delta` растёт;
   - остановка сервера (Ctrl+C) — капсула замирает, клиент не крашится.
5. Для сравнения объёма: `dotnet run --project tools/test-client -c Release -- 127.0.0.1 9050 10` — в итоговой строке видно `(дельтами N)` и `запросов полного снапшота`.

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
11. **История снапшотов клиента — 16 тиков (0.8 с).** При задержке сети больше 0.8 с каждая дельта теряет базу и клиент переходит на полные снапшоты (работает, но трафик растёт). Для сценариев с большей задержкой в M9/M10 можно увеличить историю или слать ack на снапшоты. Приоритет: низкий (100 мс в Human-Gate покрыты).
12. **Первый снапшот сессии — всегда полный и надёжный.** На подключении это небольшой всплеск трафика; альтернатива (сразу дельта) невозможна без базы. Приоритет: низкий.

## Next Actions (для следующей сессии)

1. Дождаться Human-Gate M8; после merge PR #9 — M9 (предсказание способностей и хеш конфигурации).
2. M9: синхронизация `SimulationConfig` (хеш в HelloAck), предсказание кулдаунов/урона, расширение `ClientPrediction`.
3. При росте сущностей (>20) замерить реальный трафик дельт на 20 Гц и при необходимости перейти к квантизации позиции (u16) — сейчас поля идут полными `f32`.
