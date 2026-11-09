# State

Last updated: 2026-11-09 10:40 UTC
Last heartbeat: 2026-11-09 10:40 UTC
Current milestone: M5 — Транспорт LiteNetLib, тест-клиент, потери
Current task: ожидание Human-Gate M5
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
| M4 | ✅ | ✅ | done (2026-11-09, merged `d827c9a`, ADR-008 одобрен) |
| M5 | ✅ | ⏳ | awaiting human review |
| M6 | — | — | todo |
| M7 | — | — | todo |
| M8 | — | — | todo |
| M9 | — | — | todo |
| M10 | — | — | todo |
| M11 | — | — | todo |
| M12 | — | — | todo |

## M5 tasks

| Task | Status | Notes |
|---|---|---|
| 1. `LiteNetLibTransport` (`ITransport`) | done | `Client.Network/LiteNetLibTransport`, UDP, RTT, события пакетов |
| 2. `tools/test-client` | done | консольный клиент: Hello, команды движения, приём снапшотов, лог |
| 3. Режимы доставки | done | команды/Hello — ReliableOrdered, снапшоты — Unreliable, полный снапшот — ReliableOrdered по запросу при простое > 1 с |
| 4.Тест с потерями 30% и задержкой 100 мс | done | `LossyTransportTests` (детерминированный seed) + проверка восстановления |
| 5. Версионирование handshake | done | ключ соединения = `Protocol.Version`; чужая версия → `ConnectionRejected` |
| 6. UDP-режим сервера | done | `dotnet run --project src/Server -- --udp 9050` |

## M5: принятые решения (на ревью оператору)

1. **LiteNetLib 2.1.4 из NuGet** (ADR-002), по одному транспорту на каждой стороне: `LiteNetLibServerTransport` (`IServerTransport`) и `LiteNetLibTransport` (`ITransport`, клиент, его же возьмёт Unity в M6). Замороженные интерфейсы не менялись.
2. **Версия протокола как ключ соединения.** LiteNetLib `ConnectionRequest.AcceptIfKey(Protocol.Version.ToString())`: несовпадение = отказ на handshake, без «тихого» рассинхрона (ADR-004).
3. **Имитатор плохой сети — отдельный декоратор** `LossyServerTransport` (loss% + задержка в тиках, seeded `Random`). Он даёт детерминированный тест вместо непредсказуемого UDP-loopback: при 30% потерь порог разрыва 8 тиков, восстановление после серии из 3 потерь — 1 тик.
4. **Полный снапшот — по таймауту простоя на клиенте** (не по каждой потере): если снапшотов нет > 1000 мс, тест-клиент шлёт `FullSnapshotRequest` надёжным каналом, сервер отвечает полным снапшотом тем же режимом. Дешёвая страховка, без шторма запросов.
5. **Потери/задержки применяются только к Unreliable-трафику.** Надёжные пакеты (HelloAck, полный снапшот) идут без имитации потерь — в реальном LiteNetLib их переотправляет транспорт.

## M5 Auto-Gate (проверено 2026-11-09)

- [x] Интеграционный тест с потерями/задержкой зелёный: `LossyTransportTests.ThirtyPercentLoss_WithTwoTickDelay_ClientKeepsUpAndRecovers` (300 тиков, 30% потерь, задержка 2 тика).
- [x] При 30% потерь клиент восстанавливается ≤ 3 тиков: `BurstOfThreeLostSnapshots_RecoversWithinThreeTicks` — после потери тиков 10–12 снапшот тика 13 приходит сразу (восстановление 1 тик); суммарный порог разрыва в 30%-тесте — 8 тиков при детерминированном seed.
- [x] Несовпадение версий → соединение отклоняется: `LiteNetLibTransportTests.VersionMismatch_ConnectionIsRejected` (клиент с v1 к серверу v2 получает disconnect, сущностей на сервере нет).
- [x] Реальный UDP: `LiteNetLibTransportTests.ServerAndClient_ExchangeHelloAndSnapshots` — HelloAck, снапшот, сущность на сервере.
- [x] 123/123 тестов, покрытие `Simulation` 100% line, arch-checks зелёные.
- [x] CI-run: [42598449970](https://github.com/avvasiliev888/albion-prototype/actions/runs/42598449970) — success (build + arch-checks + 123 теста + coverage gate).

## M5 Human-Gate (ожидает оператора)

**PR:** [#6 — M5: LiteNetLib, тест-клиент и имитация потерь](https://github.com/avvasiliev888/albion-prototype/pull/6), ветка `agent/M5/litenetlib-transport`.

Два терминала в корне репозитория:

```
# Терминал 1 — сервер
dotnet run --project src/Server -c Release -- --udp 9050

# Терминал 2 — тест-клиент (15 секунд)
dotnet run --project tools/test-client -c Release -- 127.0.0.1 9050 15
```

Ожидаемо на клиенте: `HelloAck: сущность 1`, далее `snapshot #1/#20/...` примерно 20 раз в секунду, позиция плавно меняется (`RTT` около 0 на loopback). На сервере: `conn 1: connected`, `Hello принят`, при закрытии клиента — `disconnected (RemoteConnectionClose)` и удаление сущности.

«Плохая сеть»: отдельного флага в демо нет — поведение зафиксировано тестами `LossyTransportTests` (30% потерь + задержка 100 мс, восстановление ≤ 3 тиков, при простое > 1 с клиент запрашивает полный снапшот надёжным каналом). Если хотите увидеть это в живом клиенте — могу добавить в M6/M7 флаг `--loss` в тест-клиент и сервер.

## M4: закрыто

Human-Gate M4 пройден 2026-11-09: демо-лог проверен оператором (снапшоты, 4 отказа UnknownTarget, авто-завершение), ADR-008 (57 байт/сущность, `Protocol.Version` = 2) одобрен, PR #5 смержен в `main` (`d827c9a`).

## Environment

| Что | Значение |
|---|---|
| ОС | Windows 11 (10.0.22631), PowerShell 7.5.4 |
| .NET SDK | 10.0.403, `global.json` фиксирует версию |
| Unity | 6000.3.28f1 LTS, `N:\Program Files\Unity Hub\Editor\6000.3.28f1` |
| Репозиторий | `https://github.com/avvasiliev888/albion-prototype` (private), `main` |
| Git LFS | 3.7.1, `--local` |
| Транспорт | LiteNetLib 2.1.4 (NuGet), UDP; порт по умолчанию 9050 |

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
2. **Unity Package Manager не может достучаться до `packages.unity.com`** — `ETIMEDOUT` из процесса UPM, хотя хост-процесс получает `200`. Причина — сетевое ограничение дочерних процессов на этом хосте. Не критично до M10 (URP): проект собирается из `Library/PackageCache`.
3. **`bash` в PATH на этом хосте — WSL**, а не Git Bash: `tools/ci/check-architecture.sh` локально запускается через `C:\Program Files\Git\bin\bash.exe` (в CI — обычный bash). Приоритет: низкий.
4. **CRLF-предупреждения git** на Windows — косметика. Приоритет: низкий.
5. **LiteNetLib 2.1.4: `NetPeer.Id` — публичное поле, не свойство** (в 1.x было иначе); в коде сервера используется `peer.Id`/`peer.Tag`, менять при обновлении библиотеки. Приоритет: низкий.

## Next Actions (для следующей сессии)

1. Дождаться Human-Gate M5 (сервер + тест-клиент в двух терминалах), после merge PR #6 — M6.
2. M6: Unity-клиент — ввод (WASD → `MoveCommand`), отображение сущностей из снапшота, интерполяция позиции, камера; подключение через `LiteNetLibTransport`. GameCI не нужен, сборка локальная.
