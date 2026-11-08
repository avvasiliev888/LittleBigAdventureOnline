# ADR-001. Разделение симуляции и визуализации; запрет `UnityEngine` в `Simulation.Core`

**Статус:** предложен (ожидает Human-Gate M0)
**Дата:** 2026-11-08
**Связано:** `docs/prototype-plan.md` (M0, M1, M2, финальный чеклист), `docs/product-vision.md` §10

## Контекст

Игра клиент-серверная: авторитетное состояние мира живёт на сервере, клиент отправляет ввод и интерполирует результат. Один и тот же код симуляции должен исполняться:

- на сервере (консольное .NET-приложение, Linux CI, без графики);
- на клиенте (Unity, внутри `MonoBehaviour`-обвязки, но **не** в ней самой) — для предсказания в M7.

План требует, чтобы `Simulation.Core` не зависел ни от чего и чтобы CI падал при появлении `UnityEngine` в этом проекте. Причина не в эстетике: зависимость от Unity делает невозможными headless-тесты на `ubuntu-latest`, детерминированный replay, нагрузочные тесты ботами и серверный билд без Unity-рантайма.

## Решение

1. Три уровня, зависимость строго в одну сторону:

   | Проект | Может зависеть от | Не может зависеть от |
   |---|---|---|
   | `src/Simulation.Core` | только BCL (`System.*`, `System.Numerics`) | `UnityEngine`, `Shared`, `Server`, `Client.Network`, Unity |
   | `src/Shared` | BCL (`System.Text.Json`) | `UnityEngine`, `Simulation.Core` |
   | `src/Server` | `Simulation.Core`, `Shared`, `Client.Network` | Unity |
   | `src/Client.Network` | `Shared` | Unity (транспортный слой — чистый .NET) |
   | `unity-client` | `Shared`, `Client.Network`; `Simulation.Core` — **только** из `Bootstrap` и `Prediction` | — |

2. Симуляция — чистая функция состояния и команд: `ISimulation.Tick(deltaTime, commands)` не знает ни о вводе, ни о сети, ни о рендере. Вся работа с реальным временем (часы, `Stopwatch`, `Thread.Sleep`) — снаружи, в серверном цикле и в Unity-адаптерах.

3. Внутри `Simulation.Core` запрещены: `UnityEngine.*`, `UnityEngine.Random`, `Time.deltaTime`, `MonoBehaviour`, `ScriptableObject`, `Debug.Log`. Математика — `System.Math`, `System.MathF`, `System.Numerics.Vector3`/`Quaternion`; если нужной операции нет — пишем её сами в `Simulation.Core.Math` с юнит-тестами.

4. Детерминизм: случайность — только через явный `IRandom`/`SeededRandom` с seed из снапшота; никаких глобальных синглтонов, статических изменяемых полей, `DateTime.Now`, `Guid.NewGuid()`, `Dictionary`-итерации в порядке, влияющем на состояние.

5. Автоматическая проверка (M1, CI):
   - grep по `src/Simulation.Core/**/*.cs` на `using UnityEngine` / `UnityEngine.` → падение сборки;
   - проверка графа `ProjectReference` (скрипт или `dotnet list reference`) — `Simulation.Core` не ссылается ни на что;
   - Unity-сторона: `asmdef` с явным списком зависимостей, чтобы ссылка на `Simulation.Core` из `View` не компилировалась.

## Альтернативы

| Вариант | Почему отклонён |
|---|---|
| Единый проект с `#if UNITY_EDITOR` / `#if UNITY_5_3_OR_NEWER` | Тесты перестают быть headless; серверный билд тянет Unity-зависимости; ошибки ловятся позже |
| Симуляция внутри Unity (DOTS/ECS или обычные `MonoBehaviour`) | Серверу нужен Unity-рантайм и лицензия; CI на Linux усложняется; детерминизм под вопросом |
| Отдельный проект, но с разрешённым `UnityEngine.Mathf` | `Mathf` тянет сборку `UnityEngine.CoreModule`; ломает headless-тесты и сервер |
| Кодогенерация DTO из схемы | Преждевременно: 3–4 сообщения, ручной код проверяется тестами roundtrip |

## Последствия

- Все тесты `Simulation.Core` идут на `ubuntu-latest` без Unity — быстрый и дешёвый CI.
- Клиент обязан быть тонким: любая «настоящая» логика, положенная в `MonoBehaviour`, — дефект архитектуры (ловится ревью и grep-правилом из M6).
- Появляется обязанность поддерживать собственные математические хелперы и `SeededRandom` вместо готовых Unity-аналогов.
- `System.Numerics.Vector3` — `float`; детерминизм гарантируется в пределах одной платформы (IEEE-754, одинаковый порядок операций). Кросс-платформенная битовая идентичность клиента (Unity, Windows) и сервера (Linux) **не** гарантируется и не требуется: расхождение поглощается реконсиляцией (ADR-006).
- Любое предложение «положить это в Unity, потом вынесем» отклоняется: временных решений нет (`AGENT.md` §11).
