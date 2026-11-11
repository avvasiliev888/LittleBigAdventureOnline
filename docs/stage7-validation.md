# Stage 7 — серверные коллизии: проверка

Дата: 2026-11-11. Проверялось на рабочей копии после коммита `3078c45`.
Ниже только фактические результаты команд и логов; непроверенное помечено явно.

## Что изменено

- Статические препятствия процедурного мира экспортируются из `Assets/Scenes/Arena.unity`
  в `config/world-colliders.json` (меню `Albion/World/Export World Colliders` или автоматически
  в конце `Albion/World/Rebuild Arena`). В файле **584** препятствия: **502** дерева,
  **75** камней и **7** столбов/стражей прохода.
- `Simulation.Core.MovementCollision` сначала применяет границы зон и ворота, затем ведёт шаг
  по окружностям: при касании нормальная составляющая отбрасывается, поэтому игрок скользит
  вдоль дерева/камня/стены, а не останавливается и не проходит насквозь. Пересечения
  расталкиваются, в том числе при спавне/респавне.
- Игроки и мобы имеют `collisionRadius` в `config/actors.json` / `config/mobs.json`
  (0.45 и 0.6 соответственно) и не проходят друг сквозь друга.
- Сервер, движение мобов и `ClientPrediction` вызывают один и тот же `MovementCollision`;
  клиент получает динамические круги из последнего снапшота.
- Физика Unity в передвижении не участвует: `Collider`/`Rigidbody` не добавлены, клиентских
  коллайдеров деревьев и камней нет. Это зафиксировано в ADR-021.

## Конфиг

| Параметр | Значение |
|---|---|
| Hash конфигов | `3b8122d66c1ecba5ac05d2c56ce9bdae557966f10c7ba337d0330852234c8768` |
| Статические препятствия | 584 (`tree` 502, `rock` 75, `column` 7) |
| `actors.json` | `collisionRadius` 0.45 у игрока, 0.6 у mob-актора |
| `mobs.json` | `collisionRadius` 0.6 у всех трёх мобов |
| Валидация | `dotnet artifacts/release/server/Server.dll --validate-config-only` — `config valid`, hash совпал |

## Тесты

- `dotnet test src/Tests/Tests.csproj -c Debug`: **443 пройдено, 0 не пройдено**.
- Новые тесты `CollisionTests` (6):
  1. `Rock_CannotBePassedThrough` — игрок не проходит сквозь камень.
  2. `TwoPlayers_CannotOccupySamePoint` — два игрока не остаются в одной точке.
  3. `MovingPlayer_DoesNotPassThroughAnotherPlayer` — движущийся игрок упирается в другого.
  4. `DiagonalMovement_SlidesAlongRockInsteadOfStopping` — диагональ скользит по камню.
  5. `DiagonalMovement_SlidesAlongZoneWall` — диагональ скользит вдоль стены с проходом.
  6. `ClientPrediction_UsesSameStaticCollision` — предсказание не проходит сквозь камень.
- `RepoConfigTests.RepoConfig_ContainsGeneratedStaticColliders` проверяет, что в реальном
  конфиге есть деревья, камни и столбы, а радиусы положительны.

## Unity и клиент

- `pwsh -File tools/unity/agent-smoke.ps1` на исходной сцене: **PASS**
  `snapshots=5 deltas=4 visuals=8 mobs=6 corpses=0`; проверки URI/BOM/хеша десяти конфигов,
  HUD и мобильной геометрии пройдены. Hash в логе — `3b8122d6…c8768`.
- Windows-плеер собран `tools/unity/build-windows.ps1` (107.4 MB). Запуск с `--batchmode`
  и логом: `ClientRuntime: конфиг … загружен, hash=3b8122d6…c8768`; сервер на 9050 принял
  `Hello` и создал сущность 8. Визуальный FPS в этой сессии не перемерялся.
- APK собран `tools/unity/build-android.ps1` (38.3 MB, ARM64 IL2CPP, debug-подпись).
  Внутри APK есть `assets/Config/world-colliders.json` (51 241 байт); хеш всех десяти
  конфигов из APK совпал с серверным. На реальном Android-устройстве не запускался.

## Нагрузка

`pwsh -File tools/load-test.ps1 -Bots 20 -Seconds 30`: 20/20 ботов подключились,
получили снапшоты, 0 разрывов; средний тик **0.197 ms**, p99 **1.104 ms**, max **12.171 ms**
при лимитах 25/50 ms. Это серверный тик, а не FPS клиента.

## Релизные файлы

| Файл | SHA-256 |
|---|---|
| `AlbionPrototype-0.6.0.apk` | `2836D2910B854DA427C106971F7F63C97CB4D566A9E15E105D0DB23C54CBE2AE` |
| `AlbionPrototype-desktop-0.6.0-win-x64.zip` | `0FC0C810C842F4D4A0AC546EA3875F5669E0A6976AD1615D2164925579CF57F2` |
| `AlbionPrototype-server-0.6.0-win-x64.zip` | `60EE92B919BD290D76ED372F7E70A3088A62D28346ACAFBF61318E85FDDAF82C` |

## Ограничения

- Клиент не использует физику Unity для движения; если в будущем понадобится невидимый
  коллайдер камеры/курсора, он не должен влиять на позицию персонажа.
- У мобов нет pathfinding: у большого препятствия они могут застрять, хотя сквозь него
  не проходят. Для среза это допустимо; при развитии AI нужен обход препятствий.
- Экспорт статики нужно повторять после изменения сцены; иначе визуальные и серверные
  препятствия разойдутся.
- 584 препятствия проверяются линейно; при росте мира понадобится spatial hash.
- APK не устанавливался на телефон; поведение на устройстве не проверено.
