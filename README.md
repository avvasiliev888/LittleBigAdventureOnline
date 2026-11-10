# Albion Prototype

Сетевой прототип боевой арены в духе Albion Online: авторитетный сервер на .NET 10, Unity-клиент
с предсказанием движения, одна способность ближнего боя, тренировочный манекен и два профиля
качества. Репозиторий — финальная версия прототипа (M0–M12), не production-MMO.

- Сервер: фиксированный тик **20 Гц**, LiteNetLib UDP, JSON-конфиги, валидация команд.
- Клиент: Unity **6000.3.28f1**, URP, предсказание + реконсиляция, интерполяция удалённых
  сущностей, дельта-снапшоты.
- Проверки: **230 unit-тестов**, покрытие `Simulation.Core.Simulation` 100%, архитектурные
  CI-проверки, нагрузочный тест **20 ботов / 60 с**.

## Быстрый старт

Из корня репозитория, в PowerShell 7:

```powershell
git lfs install
git lfs pull
dotnet restore Prototype.sln
dotnet build Prototype.sln -c Release

# 1) плагины и JSON-конфиги для Unity
pwsh -File tools/unity/build-plugins.ps1

# 2) сервер (оставить терминал открытым)
dotnet run --project src/Server -c Release -- --udp 9050
```

Затем откройте папку `unity-client` в Unity 6000.3.28f1, сцену `Assets/Scenes/Arena.unity`
и нажмите **Play**. Клиент сам подключится к `127.0.0.1:9050`.

Если Unity-редактор уже открыт, после `build-plugins.ps1` дождитесь реимпорта и нажмите Play
заново. Плагины и `StreamingAssets/Config` — генерируемые каталоги, в git их нет.

## Требования

| Компонент | Версия / примечание |
|---|---|
| ОС | Windows 10/11 x64 (проверено на Windows 11) |
| .NET SDK | **10.0.x** (`dotnet --version`) |
| PowerShell | PowerShell 7 (`pwsh`) |
| Unity | **6000.3.28f1**, модуль **Windows Build Support (IL2CPP/Mono)** |
| Git | с Git LFS |
| GPU | любой с D3D11/D3D12; desktop-профиль использует URP deferred |

Unity Hub → Installs → 6000.3.28f1 → Add modules → Windows Build Support. Точная версия
зафиксирована в `unity-client/ProjectSettings/ProjectVersion.txt`.

## 1. Клонирование и ассеты

```powershell
git clone https://github.com/avvasiliev888/albion-prototype.git
cd albion-prototype
git lfs install
git lfs pull
```

Без `git lfs pull` FBX/PNG останутся указателями LFS: персонаж будет без текстур/анимаций.
Проект приватный; ассеты Asset Store не предназначены для перепубликации.

## 2. Сборка .NET и тесты

```powershell
dotnet restore Prototype.sln
dotnet build Prototype.sln -c Release
pwsh -File tools/dev/test-local.ps1 -Configuration Release
```

`test-local.ps1` копирует исходники во временный каталог: на этом хосте прямой
`dotnet test` из workspace может упасть на VSTest-testhost (Win32 access denied). На CI
используется обычный `dotnet test`.

Ожидаемо: **230/230**, покрытие `Simulation.Core.Simulation` — 100%.

Дополнительно (нужен Git Bash или Linux/macOS):

```bash
bash tools/ci/check-architecture.sh
bash tools/ci/check-unity-boundaries.sh
```

Эти же команды выполняет GitHub Actions на каждый push в `agent/**` и PR в `main`.

## 3. Плагины и конфиги для Unity

```powershell
pwsh -File tools/unity/build-plugins.ps1
```

Скрипт собирает `Client.Network` в `netstandard2.1` и копирует в
`unity-client/Assets/Plugins/Albion`:

- `Simulation.Core.dll`, `Shared.dll`, `Client.Network.dll`, `LiteNetLib.dll`;
- `config/actors.json` и `config/abilities.json` в
  `unity-client/Assets/StreamingAssets/Config/`.

Запускайте его после каждого изменения C#-кода в `src/` или JSON-конфигов. Клиент и сервер
читают **один и тот же** JSON; хеш конфига виден в HUD (`cfg: …`) и в логе сервера
(`config: … hash: …`) — они должны совпадать.

## 4. Запуск сервера

```powershell
dotnet run --project src/Server -c Release -- --udp 9050
```

Порт по умолчанию — `9050`. Полезные флаги:

| Флаг | Назначение |
|---|---|
| `--udp [порт]` | UDP-сервер; без числа — 9050 |
| `--delay <мс>` | задержка в обе стороны (для демо плохой сети) |
| `--loss <0..100>` | потери ненадёжных снапшотов, % |
| `--metrics-every <тиков>` | период лога метрик тика (по умолчанию 200, 0 — выкл.) |
| `--seconds <сек>` | остановиться через N секунд (нагрузочный/автотест) |

Без `--udp` запускается короткая in-process демонстрация «команда → валидация → тик → снапшот»
без Unity.

## 5. Запуск клиента

### Вариант А — Unity Editor

1. Unity Hub → **Add project from disk** → `unity-client`.
2. Откройте `Assets/Scenes/Arena.unity`.
3. Нажмите **Play**.

`ClientRuntime` стартует сам через `RuntimeInitializeOnLoadMethod`: камера, HUD, подключение,
конфиг. Отдельную сцену/префаб создавать не нужно.

Подключение можно переопределить:

```powershell
# аргументы билда
.\AlbionPrototype.exe -serverHost 127.0.0.1 -serverPort 9051

# или переменные окружения
$env:ALBION_SERVER_HOST = "127.0.0.1"
$env:ALBION_SERVER_PORT = "9051"
```

### Вариант Б — standalone Windows build

В редакторе: **Albion → Build → Desktop**. Либо из командной строки:

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe"
& $unity -batchmode -quit -projectPath "$PWD\unity-client" `
  -executeMethod Albion.Editor.AlbionBuild.BuildDesktopBatch `
  -logFile "$PWD\unity-client\Builds\m12-build-desktop.log"
```

Результат: `unity-client/Builds/desktop/AlbionPrototype.exe`.

```powershell
.\unity-client\Builds\desktop\AlbionPrototype.exe -serverPort 9050
```

Mobile-профиль: **Albion → Build → Mobile** или `BuildMobileBatch`; в рантайме профиль можно
переключить аргументом `-quality mobile` / `-quality desktop`.

## Управление

| Ввод | Действие |
|---|---|
| **WASD** | движение |
| **ПКМ (удержание)** | движение к курсору по плоскости Y=0 |
| **Ctrl** | шаг (медленно, 1.5 m/s) |
| **Space / ЛКМ (удержание)** | авто-атака по ближайшей цели; отпускание останавливает |
| **1 / 2** | меч / топор (только визуально, клиентски) |
| **Esc** | освободить курсор (в редакторе — остановить Play) |

Камера следует за игроком. HUD показывает подключение, снапшоты/дельты, `cfg`-хеш, профиль,
FPS, HP/ману, кулдаун и подсказку по управлению.

## Демо-сценарий на двух клиентах

Нужны два окна клиента и один сервер. Проще всего собрать desktop-билд (см. выше) и запустить
его дважды; альтернатива — один Play в редакторе + один билд.

```powershell
# терминал 1 — сервер
dotnet run --project src/Server -c Release -- --udp 9050

# терминал 2 — два клиента (каждый в своём окне)
cmd /c start "" ".\unity-client\Builds\desktop\AlbionPrototype.exe" -serverPort 9050
cmd /c start "" ".\unity-client\Builds\desktop\AlbionPrototype.exe" -serverPort 9050
```

Либо готовый лаунчер:

```powershell
pwsh -File tools/demo/start-demo.ps1
```

Сценарий:

1. **Движение.** В первом окне пройдите WASD вправо/влево; во втором видно удалённого игрока,
   который едет через интерполяцию (задержка отображения 2 тика).
2. **Способность.** Подойдите к тренировочному манекену (entity 2, 100 HP) и удерживайте
   **Space/ЛКМ**. Удары идут каждые 2 с; HP-бар манекена падает 100 → 75 → 50 → 25 → 0.
3. **Смерть/респавн.** После четвёртого удара манекен проигрывает Death и лежит; через **5 с**
   он респавнится с полным HP в исходной точке. Оба клиента видят это по снапшотам.
4. **Два клиента.** Второй игрок виден первому; оружие **1/2** переключается локально, у
   удалённого игрока остаётся меч. HP/мана-бары видны над всеми сущностями.
5. **Плохая сеть.** Остановите сервер, запустите его с задержкой и потерями, затем заново
   откройте клиентов (клиент не переподключается к перезапущенному серверу автоматически):

   ```powershell
   dotnet run --project src/Server -c Release -- --udp 9050 --delay 100 --loss 30
   ```

   Ожидаемо: движение остаётся отзывчивым благодаря предсказанию, при коррекциях возможны
   плавные подтягивания, в HUD растут `reconciles` и `fullReq`, но клиент не зависает и
   продолжает получать снапшоты.

Подробный чеклист и ожидаемые значения — `docs/demo.md`.

## Плохая сеть без второго клиента

Быстрая проверка протокола и дельт:

```powershell
dotnet run --project src/Server -c Release -- --udp 9050 --delay 100 --loss 30
dotnet run --project tools/test-client -c Release -- 127.0.0.1 9050 10
```

Тест-клиент печатает число снапшотов/дельт/ack и запросов полного снапшота.

## Тесты и нагрузка

```powershell
# все unit-тесты + coverage gate
pwsh -File tools/dev/test-local.ps1 -Configuration Release

# 20 ботов, 60 секунд, пороги avg <= 25 ms / p99 <= 50 ms
pwsh -File tools/load-test.ps1 -Bots 20 -Seconds 60
```

Нагрузочный скрипт поднимает отдельный UDP-сервер, запускает `tools/bot-client`, парсит
`tick final metrics` и пишет `artifacts/m11-load/{server.log,bots.log,summary.json}`. Боты
подключаются по тому же протоколу, двигаются и бьют тренировочный манекен. Подробности —
`docs/m11-load-test.md`.

Ручной запуск ботов:

```powershell
dotnet run --project tools/bot-client -c Release -- `
  --host 127.0.0.1 --port 9050 --bots 20 --seconds 60
```

## Конфигурация

- `config/actors.json` — HP, скорость, мана, регенерация, время респавна, способность актора.
- `config/abilities.json` — урон, радиус, кулдаун, стоимость ресурса.

Меняйте JSON, затем **перезапустите сервер** и **пересоберите плагины** (`build-plugins.ps1`),
чтобы клиент получил те же значения. Клиент не делает тихий фолбэк на константы: при ошибке
конфига в HUD будет `config error`.

## Структура репозитория

```text
src/Simulation.Core/   детерминированная симуляция без Unity (общая для сервера и клиента)
src/Shared/            протокол, JSON-загрузчик, кодеки, снапшоты
src/Client.Network/    клиентский конвейер, предсказание, транспорт LiteNetLib
src/Server/            UDP-сервер, тик 20 Гц, валидация, снапшоты, метрики
src/Tests/             xUnit-тесты (230)
tools/test-client/     одиночный консольный клиент
tools/bot-client/      headless-боты для нагрузочного теста
tools/load-test.ps1    автоматический прогон 20 ботов
tools/unity/           сборка плагинов для Unity
tools/ci/              архитектурные проверки
unity-client/          Unity-проект (сцена Arena, URP, UI, ввод)
config/                общие JSON-конфиги сервера и клиента
docs/                  план, ADR, состояние, логи, демо
```

## Troubleshooting

| Симптом | Что проверить |
|---|---|
| Unity не видит `Simulation.Core`/`Client.Network` | `pwsh -File tools/unity/build-plugins.ps1`, дождаться реимпорта |
| Персонаж magenta / без анимаций | `git lfs pull`, затем Reimport All |
| `config error` или `cfg`-хеш не совпадает с сервером | пересобрать плагины, перезапустить сервер и клиент |
| Клиент не подключается | сервер запущен? порт 9050 свободен? `-serverPort` совпадает? |
| `Address already in use` | другой сервер на порту; взять `--udp 9051` и `-serverPort 9051` |
| Пустая сцена / нет арены | открыть `Assets/Scenes/Arena.unity`; при необходимости **Albion → World → Rebuild Arena** |
| Тесты падают на VSTest | использовать `pwsh -File tools/dev/test-local.ps1` |
| Unity build не стартует | проверить модуль Windows Build Support и `ProjectVersion.txt` (6000.3.28f1) |

## Ограничения

- Один тип актора игрока и один тренировочный манекен; одна способность ближнего боя.
- Нет аккаунтов, БД, сохранения между сессиями, матчмейкинга, регионов, экономики и крафта.
- ИИ противников, полноценный античит и кроссплатформенные сборки не входят в прототип.
- Mobile-профиль — это desktop-билд с URP forward и без теней/пост-обработки, не сборка под
  iOS/Android.
- Визуальное качество окружения — стилизованный прототип, не финальный арт.
- Сетевые потери/задержка имитируются детерминированно (`LossyServerTransport`), а не реальным
  сетевым стеком.

## Документация

- `docs/prototype-plan.md` — план M0–M12 и финальный чеклист.
- `docs/state.md` — что сделано, отложено, вырезано; текущий статус.
- `docs/agent-log/` — логи по milestone'ам.
- `docs/adr/` — архитектурные решения (ADR-001…ADR-012).
- `docs/demo.md` — демо-сценарий и проверки.
- `docs/m11-load-test.md` — нагрузочный тест 20 ботами.
- `unity-client/README.md` — детали Unity-клиента, анимаций, URP и визуальных проверок.

## Лицензия и ассеты

Код прототипа — приватный учебный проект. Сторонние ассеты (dReal Warrior, All Star Characters)
распространяются по лицензии Unity Asset Store и не предназначены для самостоятельной
перепубликации; в репозиторий включён только минимальный runtime-набор.

