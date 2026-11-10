# M10.10.1 — «бессмертный» манекен: регенерация ресурса

## Симптом

После смерти и респавна тренировочного манекена первый удар наносит урон (75 HP), все последующие
удары урона не наносят; манекен навсегда остаётся с 75 HP. Баг воспроизводился и после M10.9.2, и
после M10.10, поэтому target/cooldown/анимация были не при чём.

## Диагностика

Запустил локальный сервер с логом отказов и desktop-клиент, эмулировал удары. Сервер показал:

```
[t1499] conn 1: rejected (NotEnoughResource) — ресурс 0 < 10
[t1540] conn 1: rejected (NotEnoughResource) — ресурс 0 < 10
[t1581] conn 1: rejected (NotEnoughResource) — ресурс 0 < 10
```

Удар стоит `resourceCost=10`, `maxResource=100`, а регенерации ресурса не было вообще. После 10
активаций (включая промахи/удары по мёртвому) ресурс становился 0 навсегда. Headless-тесты этого не
ловили: каждый начинал с полного ресурса и делал 1–3 удара.

## Исправление

- `ActorConfig.ResourceRegenPerSecond` — новое обязательное поле конфига `resourceRegenPerSecond`,
  конечное неотрицательное. У игрока `5.0` (10 ресурса за 2 с кулдауна), у манекена `0.0`.
- `Simulation.Tick` вызывает `RegenerateResources(deltaTime)` **до** команд:
  - мёртвые не регенерируют;
  - при полном ресурсе остаток сбрасывается;
  - дробная часть копится в `EntityState.ResourceRegenRemainder` (5 × 0.05 = 0.25/тик), целые
    единицы начисляются `Floor`, остаток сохраняется;
  - при достижении максимума остаток обнуляется;
  - респавн восстанавливает полный ресурс и обнуляет остаток.
- `ClientRuntime` HUD: `res: X/Y`; `OwnHpText`/`OwnResourceText` читают сущность через общий
  `OwnEntity`.
- `config/actors.json`, тестовые JSON-фикстуры и `ConfigLoader` обновлены; поле обязательное, без
  дефолта в JSON.
- Регенерация серверная, протокол не менялся; ресурс уже был в снапшоте.

## Тесты

- `ResourceRegenTests`:
  - дробная регенерация 5/с (1 единица за 5 тиков, remainder не теряется);
  - остановка на максимуме и сброс remainder;
  - мёртвый не регенерирует;
  - `StarvedCaster_RegeneratesAndDealsDamageAgain` — 0 → 10 → второй удар снова наносит урон;
  - `RepeatedCasts_DoNotStarveAndKeepDamagingAfterRespawn` — 12 ударов, респавн через 1 с, манекен
    снова убит, ресурс не 0.
- `ConfigTests`: отсутствие `resourceRegenPerSecond` — ошибка с именем поля; отрицательное/NaN/∞ —
  `ArgumentOutOfRangeException`.
- `RepoConfigTests`: у игрока 5.0.
- Итого **223/223**, coverage 100%.

## Сборка и живой прогон

- `ConfigureAndVerify`: exit 0, GPU 13/13, combat checks passed; `m10-10-1-configure.log`.
- Desktop/mobile: Succeeded, size 93 167 334, errors 0; `Assembly-CSharp.dll` 17:37:40 / 17:38:27;
  `m10-10-1-{desktop,mobile}.log`.
- Новый сервер + новый клиент: 12 ударов, `NotEnoughResource` в логе отсутствует, хеш конфига
  совпадает (`7f97118…`).
- CI `f1cd878`: [pull_request 42836716179](https://github.com/avvasiliev888/albion-prototype/actions/runs/42836716179)
  и [push 42836728679](https://github.com/avvasiliev888/albion-prototype/actions/runs/42836728679) — success.

## Остаточный риск

- Регенерация 5/с выбрана как «стоимость удара за кулдаун»; если оператор захочет Albion-подобный
  дефицит маны, число меняется в `actors.json` без кода.
- HUD `res` — из снапшота, отстаёт на сетевую задержку.
- Живой прогон подтвердил отсутствие отказов по ресурсу, но визуальный урон/HP проверяется
  оператором.
