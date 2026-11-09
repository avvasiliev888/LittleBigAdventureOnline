# Unity-проект (клиент)

Здесь живёт Unity-клиент. Правила (ADR-001, ADR-006):

- авторитетной игровой логики в `MonoBehaviour` нет — только отображение и ввод;
- `Simulation.Core` подключается к клиенту **только** через `Bootstrap` и `Prediction`;
- `*.meta` трекаются git, бинарные ассеты > 100 KB — через Git LFS.

Версия редактора фиксируется в `ProjectSettings/ProjectVersion.txt` (Unity 6.3 LTS, 6000.3.x).

## Как запустить (M6)

1. Собрать плагины (netstandard2.1 → `Assets/Plugins/Albion`, каталог не коммитится):

   ```
   pwsh -File tools/unity/build-plugins.ps1
   ```

2. Запустить сервер в отдельном терминале из корня репозитория:

   ```
   dotnet run --project src/Server -c Release -- --udp 9050
   ```

3. Открыть `unity-client` в Unity 6.3, открыть сцену `Assets/Scenes/SampleScene.unity`, нажать **Play**.

Клиент запускается сам (`ClientRuntime` через `RuntimeInitializeOnLoadMethod`): создаёт плоскость,
свет, камеру с `CameraFollow`, подключается к серверу, шлёт `MoveCommand` по WASD и двигает капсулы
по интерполированным снапшотам (лаг отображения 2 тика = 100 мс).

Настройки подключения:

- переменные окружения `ALBION_SERVER_HOST`, `ALBION_SERVER_PORT`;
- аргументы командной строки `-serverHost 127.0.0.1 -serverPort 9050` (для билда).

## Ограничения M6

- Рендер-пайплайн — встроенный (Built-in) `Standard`, URP отложен до M10 из-за недоступности
  `packages.unity.com` из процесса Unity Package Manager на этом хосте.
- Тестов Unity Test Framework в проекте нет (пакет не установлен); smoke-тест клиента — xUnit
  `ClientPipelineTests` в `src/Tests` (подключение → ввод → снапшоты → отрисовка).
- Предсказания и реконсиляции нет — это M7. Клиент только отображает серверное состояние.
