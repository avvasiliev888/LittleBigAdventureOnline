# M10.10 — классическое управление мышью (ПКМ/ЛКМ)

## Задача

M10 принят. До M11 (нагрузочный тест) добавить управление как в Albion Online:

- ПКМ (удержание) — бег к курсору, луч камеры в плоскость Y=0, смена направления на ходу,
  мёртвая зона 0.5 м;
- ЛКМ (удержание) — атака с теми же правилами, что Space (кулдаун, `IsAttackPlaying`, движение);
- WASD/Space не ломать, мышь и клавиатура комбинируются.

## Изменения

### `Assets/Scripts/Input/UnityInputSource.cs`

- Добавлены провайдеры `playerPosition` и `camera`, порог `mouseMoveThreshold` (0.5 м).
- `TryReadMouseDirection`: `camera.ScreenPointToRay(Input.mousePosition)` пересекается с
  `Plane(Vector3.up, Vector3.zero)`; точка XZ минус позиция игрока; при расстоянии > порога
  возвращается нормализованное направление.
- `ReadMovementDirection`: WASD и ПКМ складываются, затем нормализуются; Ctrl применяет шаг.
- `PollFrame`: `_abilityPressed` выставляется по `Space` **или** `Input.GetMouseButton(0)`.
- Ветка способности не изменена: ЛКМ проходит тот же `IsAbilityReady`/`IsAttackPlaying` и не
  блокирует движение во время кулдауна.

### `Assets/Scripts/Bootstrap/ClientRuntime.cs`

- Передаёт `PlayerPosition` (из `Prediction.Position`, fallback — трансформ игрока) и камеру из
  `CameraFollow`.
- Включает `Application.runInBackground`, `Cursor.lockState = None`, `Cursor.visible = true`.
- HUD: `WASD/ПКМ — движение, Ctrl — шаг, Space/ЛКМ — способность`.

### `Assets/Scripts/View/CooldownHud.cs`

- Подпись иконки: `SPACE / LMB`.

## Проверки

- `tools/dev/test-local.ps1 -Configuration Release`: **216/216**, coverage 100% четырёх модулей.
  Сетевой протокол/`Client.Network` не менялись.
- `ConfigureAndVerify`: exit 0, GPU 13/13, Warrior combat checks passed; лог
  `Builds/m10-10-configure.log`.
- Desktop: Succeeded, size 93 165 746, errors 0; `Assembly-CSharp.dll` 16:33:45.
- Mobile: Succeeded, size 93 165 746, errors 0; `Assembly-CSharp.dll` 17:06:25.
- Логи `Builds/m10-10-desktop.log`, `Builds/m10-10-mobile.log`.
- CI `dbc0c10`: [pull_request 42831157749](https://github.com/avvasiliev888/albion-prototype/actions/runs/42831157749)
  и [push 42831171202](https://github.com/avvasiliev888/albion-prototype/actions/runs/42831171202) — success.
- Runtime-смоук desktop: клиент запустился, `ClientRuntime: сессия готова`, исключений
  Input/камеры/курсора в `Player.log` нет.

## Ограничения

- Headless CI не эмулирует `Input.GetMouseButton`/`ScreenPointToRay`; геометрия мыши проверяется
  живым клиентом. Юнит-тесты покрывают сетевую логику, а не Unity Input.
- Порог 0.5 м и плоскость Y=0 фиксированы; арены с рельефом/высотой потребуют физического
  raycast/маски слоёв (вне M10.10).
- Луч пересекает бесконечную плоскость Y=0, а не границу меша пола; у края арены персонаж может
  выйти за визуальный пол — поведение совпадает с WASD.
- PR #12 в GitHub всё ещё OPEN (оператор сообщил о принятии M10; merge не выполнялся агентом).
