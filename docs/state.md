# State

Last updated: 2026-11-08 18:02 UTC
Last heartbeat: 2026-11-08 18:02 UTC
Current milestone: M0 — Видение и архитектурные решения
Current task: ожидание Human-Gate M0
Onboarding: completed

## Onboarding: completed

Онбординг завершён 2026-11-08, 14 вопросов, все ответы получены от оператора (см. `## Resolved Questions`).

## Milestones

| Milestone | Auto-Gate | Human-Gate | Status |
|---|---|---|---|
| M0 | ✅ | ⏳ | awaiting human review |
| M1 | — | — | todo |
| M2 | — | — | todo |
| M3 | — | — | todo |
| M4 | — | — | todo |
| M5 | — | — | todo |
| M6 | — | — | todo |
| M7 | — | — | todo |
| M8 | — | — | todo |
| M9 | — | — | todo |
| M10 | — | — | todo |
| M11 | — | — | todo |
| M12 | — | — | todo |

## Current milestone tasks

| Task | Status | Commit | Notes |
|---|---|---|---|
| Проверить `docs/product-vision.md` (все 10 разделов) | done | — | разделы 1–10 присутствуют (проверено grep по `^## N.`) |
| ADR-001…ADR-007 в `docs/adr/` | done | — | 7 файлов, в каждом «Контекст / Решение / Альтернативы / Последствия» |
| Создать `docs/state.md` | done | — | |

## M0 Auto-Gate (проверено локально 2026-11-08)

- [x] Все ADR существуют и содержат контекст, решение, альтернативы, последствия — 7/7, проверено скриптом.
- [x] `docs/state.md` создан.
- [x] `docs/product-vision.md` содержит все 10 разделов (1…10).

## M0 Human-Gate (ожидает оператора)

**PR:** [#1 — M0: ADR-001…ADR-007 и проверка Product Vision](https://github.com/avvasiliev888/albion-prototype/pull/1), ветка `agent/M0/adr-and-vision`, коммит `2388e64`.

- [ ] Оператор подтвердил ADR-001…ADR-007. После этого они **заморожены**.
- [ ] Оператор ответил на «Вопросы к заказчику» из Product Vision (допущения 1–3 вынесены в `## Resolved Questions`, п.11).

**Что смотреть оператору:**
- ADR-001 — границы проектов и запрет `UnityEngine` в `Simulation.Core`.
- ADR-002 — LiteNetLib 2.1.4, режимы доставки (команды ReliableOrdered, снапшоты Unreliable).
- ADR-003 — тик 20 Гц, `dt = 0.05`, бюджет тика 50 мс.
- ADR-004 — бинарный формат снапшота/дельты, `Protocol.Version`, handshake-отказ при несовпадении.
- ADR-005 — JSON-конфиги, fail-fast валидация, отсутствие фолбэков, секунды→тики.
- ADR-006 — предсказание движения, откат + повтор команд, `EPSILON = 0.01`, сглаживание, а не телепорт.
- ADR-007 — xUnit, 4 уровня тестов, 100% покрытие `Simulation.Tick`/snapshot, флап = дефект.

## Environment

| Что | Значение |
|---|---|
| ОС | Windows 11 (10.0.22631), PowerShell 7.5.4 |
| .NET SDK | **10.0.403** (установлен 2026-11-08 через winget, по решению оператора) |
| Unity | 6.3 LTS (6000.3.x) — устанавливает и активирует оператор; агент установку не трогает |
| Репозиторий | `https://github.com/avvasiliev888/albion-prototype` (private), ветка `main`, remote `origin` |
| GitHub CLI | авторизован как `avvasiliev888`, scopes `repo`, `workflow`, `gist`, `read:org` |
| git identity | `Blaze <avvasiliev888@gmail.com>` |
| Git LFS | 3.7.1, `git lfs install --local`; бинарники > 100 KB — в LFS, `*.meta` трекаются обычным git |
| Node.js | v24.14.1 |

## Open Questions

1. **Branch protection на `main` невозможна на текущем плане GitHub.** `gh api ... /branches/main/protection` и `POST /rulesets` возвращают `403: Upgrade to GitHub Pro or make this repository public to enable this feature`. Варианты:
   - (a) Оформить GitHub Pro (~$4/мес) — protection включается, репозиторий остаётся private;
   - (b) сделать репозиторий public — protection бесплатна, но код открыт (для портфолио это, возможно, и нужно);
   - (c) оставить как есть: правило «агент пушит только в `agent/*`, в `main` мержит только оператор» соблюдается дисциплиной агента + проверкой в PR; серверного запрета нет.
   Вопрос блокирует только п.9 ответов оператора (branch protection), не блокирует M0/M1. Пока действует (c).
2. **Требуемые статус-чеки CI** (require CI green) добавить в protection/ruleset после появления workflow в M1 — сейчас настраивать нечего (CI ещё нет, а required-check без воркфлоу заблокирует PR навсегда).

## Resolved Questions

1. **Какой .NET SDK и кто ставит?** → .NET 10 LTS (не 8: 8 и 9 теряют поддержку 10.11.2026, у 10 LTS — до ноября 2028). Установку делает агент через winget/официальный скрипт. (2026-11-08)
2. **Какая Unity и кто ставит?** → Unity 6.3 LTS (6000.3.x), не «просто 6» и не 2022.3 (2022.3 без поддержки с мая 2025, у 6.3 — до декабря 2027). Ставит и активирует лицензию оператор; агент установку не трогает. (2026-11-08)
3. **Как проверяется «Unity собирается» (Gate M1/M6)?** → локально оператором, на M6. GameCI с secrets оператора — только если/когда M6 дойдёт до реального билда. CI для Unity до этого не делаем. (2026-11-08)
4. **Где хостится репозиторий?** → `avvasiliev888/albion-prototype`, private. (2026-11-08)
5. **Кто создаёт репозиторий?** → агент: `git init` + `gh repo create --push`; URL оператор не даёт. (2026-11-08)
6. **Монорепо и Git LFS?** → монорепо подтверждено; LFS включается сразу, до первого коммита в `/unity-client`. Правило: любой бинарный файл > 100 KB — в LFS. `*.meta` трекаются, в `.gitignore` не добавлять. (2026-11-08)
7. **Модель доступа?** → агент пушит только в `agent/*`, PR открывает агент, мержит только оператор. (2026-11-08)
8. **CI-раннер и лимиты?** → `ubuntu-latest`, без жёстких лимитов минут, но полный прогон только на push в `agent/*` и PR в `main`. (2026-11-08)
9. **Branch protection?** → настраивает агент через `gh api` (require PR, require CI green, no force push в `main`), после настройки показать оператору вывод. **Частично выполнено:** GitHub отказывает на бесплатном плане для private-репо (см. Open Questions п.1). (2026-11-08)
10. **LiteNetLib?** → NuGet, версия 2.1.4, не вендорить. Других запрещённых пакетов нет; при сомнении — спрашивать. (2026-11-08)
11. **Допущения Product Vision?** → 10–20 CCU для нагрузочного теста подтверждено; NPC-якорей рынка НЕТ в прототипе; цифры убывающей отдачи — позже, не в прототипе. (2026-11-08)
12. **Противоречие vision/план (экономика)?** → снято: в M1–M12 экономики, крафта, инвентаря и торговли НЕТ, осознанно — прототип проверяет архитектуру, а не контент; экономика — на следующей вехе (vertical slice). (2026-11-08)
13. **M1 и M2?** → строго последовательно; параллелить подготовку M2 нельзя, контракты M1 замораживаются до начала M2. (2026-11-08)
14. **Namespace/имена проектов?** → `Simulation.Core`, `Shared`, `Server`, `Client.Network` — буквально как в плане, никаких `Albion.*`. (2026-11-08)

## Discovered Issues

1. **CRLF-предупреждения git на Windows.** Файлы в индексе нормализованы (`core.autocrlf=false`), но `git` предупреждает о замене LF→CRLF при checkout. Косметика; при появлении `.cs` проверить, что файлы не «мигают» в diff. Приоритет: низкий.

## Next Actions (для следующей сессии)

1. Дождаться Human-Gate M0: оператор подтверждает ADR-001…ADR-007 (PR `agent/M0/adr-and-vision`).
2. Решить Open Question п.1 (branch protection: Pro / public / дисциплина).
3. После закрытия M0 — M1: структура репозитория, `.sln`, CI (ubuntu), пустые интерфейсы с XML-док, `SimulationState`, smoke-тест.
