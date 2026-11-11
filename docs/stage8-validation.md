# Stage 8 — полировка, визуальный апгрейд и релиз 0.7.0

Дата: 2026-11-11. База: коммит `8d7b28d` + рабочее дерево Stage 8. Релизные артефакты:
`artifacts/release/`.

## 1. Scope

Stage 8 закрывает визуальную заглушку персонажей и релизную сборку:

1. процедурный генератор гуманоидов из примитивов (сфера/бокс/цилиндр), применимый и в
   редакторе, и в рантайме;
2. оружие в руке через attachment point (`RightHandSocket`): меч, кинжал, топор, лук, посох;
3. Toon-материалы `Albion/Toon Lit` + inverted-hull обводка `Albion/Toon Outline`;
4. VFX удара, сбора ресурса, получения урона и метки клика;
5. инвентарь (`I`, мобильная «Сумка»), версия в правом нижнем углу;
6. ЛКМ: удержание = бег к курсору, клик = метка и бег в точку, клик по трупу = лут;
7. `PLAYTEST.md` для LAN/интернета и сброса мира;
8. релизные APK/Desktop/Server 0.7.0.

## 2. Реализация

### 2.1. Процедурные модели

| Файл | Роль |
|---|---|
| `unity-client/Assets/Scripts/View/ProceduralMeshLibrary.cs` | кэшированные low-poly меши: box, sphere, cylinder, cone, slash arc, ring |
| `unity-client/Assets/Scripts/View/ProceduralMaterials.cs` | общие `Toon Lit`/`Toon Outline` материалы |
| `unity-client/Assets/Scripts/View/ProceduralHumanoidVisual.cs` | сборка героя и мобов, анимация ходьбы/удара/хита |
| `unity-client/Assets/Scripts/View/ProceduralWeaponBuilder.cs` | меч, кинжал, топор, лук, посох из примитивов |
| `unity-client/Assets/Scripts/View/WeaponVisuals.cs` | выбор оружия по `WeaponItemId` |
| `unity-client/Assets/Editor/ProceduralCharacterGenerator.cs` | меню `Albion/Characters/Generate Procedural Humanoids` и превью-галерея |
| `unity-client/Assets/Editor/ProceduralCharacterChecks.cs` | Editor-проверки профилей и runtime-визуала |

Профили: `Hero`, `BlueMob`, `YellowMob`, `RedMob`. У героя шлем и гребень, у синего/жёлтого
капюшон, у красного рога и наплечники. Все части — `MeshRenderer`; Animator,
SkinnedMeshRenderer и Collider не создаются.

`CharacterVisualSettings` получил флаг `Procedural` и профиль; `WarriorVisual.asset`
переключён на процедурный режим (`Height: 1.95`, `Procedural: 1`, `Profile: 0`).
Импортированный warrior остался fallback-путём (`Configure Imported Warrior`,
`Procedural: 0`).

### 2.2. Оружие и обводка

`WeaponVisuals.AttachProcedural` создаёт все пять моделей и прячет невыбранные. Тип
выбирается по авторитетному `EntitySnapshot.WeaponItemId`, который теперь доходит до
`InterpolatedEntity` и `SnapshotBuffer`:

| ItemId | Архетип | Оружие |
|---|---|---|
| 10 | warrior | меч |
| 14 | archer | лук |
| 15 | mage | посох |

Обводка сделана отдельным дубликатом-рендерером с `Albion/Toon Outline`: URP выполняет
только один forward-pass, поэтому `AlbionOutline` внутри `ToonLit` сам по себе не рисуется.

### 2.3. VFX и анимации

`CombatVfx`:

- `AttackFlash` — дуга `SlashArc`, живёт 0,28 с, масштаб 0,85→1,25;
- `GatherBurst` — `ParticleSystem` (mesh render mode, 22 частицы, гравитация), автоудаление
  через 1,6 с;
- `ClickMarker` — кольцо на земле, сжимается 1,2→0,02 за 0,75 с;
- `PlayHit` — красный `MaterialPropertyBlock`-тинт на 0,18 с, без смены состояния.

Попадание по игроку во время замаха **не отменяет** атаку:

- процедурная модель только краснеет, `IsAttackPlaying` остаётся true;
- импортированный `WarriorVisual` при `Attack`/`IsAttackPlaying` ставит hit в очередь и
  проигрывает его после замаха (`_hitQueued`).

### 2.4. UI, ввод и лут

- `BuildInfo.Version = "0.7.0"`, `BuildInfo.Label` рисуется в правом нижнем углу на
  стартовом экране и в игре (`ClientRuntime.DrawBuildVersion`).
- `ClientHud`: `I` — инвентарь; панель показывает снаряжение из снапшота, серебро/Славу
  и журнал добычи сеанса. Мобильная кнопка «Сумка».
- ЛКМ/ПКМ: нажатие задаёт destination и рисует метку; удержание обновляет destination;
  клавиатура отменяет автобег.
- ЛКМ по трупу: если труп в радиусе — `LootCorpse`; иначе pending и авто-подбор при
  подходе. Новый клик сбрасывает pending.
- `F` — сбор ближайшей жилы, `Space` — атака, `E` оставлен как резервный лут.
- `ClientHud.TryGather` вызывает `ClientPipeline.SendGather` и локально пишет добычу в
  журнал; `UnityView.TriggerGather` показывает частицы.

## 3. Проверки

### 3.1. Сборка и тесты

```text
dotnet build Prototype.sln -c Release   -> 0 ошибок
tools/dev/test-local.ps1                -> 449/449 PASS
```

Покрытие: `Simulation.Core` 100% строк, 98,34% ветвей; `Shared`, `Server`, `Client.Network` —
100% строк.

### 3.2. Конфиг и нагрузка

```text
--validate-config-only -> config valid, hash 6f3af6965a1ceb17af9ef2978d93a2a5d3197d0d0535e7fca109c7c939f9effc
tools/load-test.ps1 -Bots 20 -Seconds 30 -> PASS avg=0.142ms p99=0.646ms max=13.239ms
```

20/20 ботов подключились, 140 снапшотов, 11 940 дельт, 0 дисконнектов.

### 3.3. Unity smoke и визуал

```text
pwsh -File tools/unity/agent-smoke.ps1 -Screenshot artifacts/stage8-smoke.png
PASS snapshots=5 deltas=4 visuals=8 mobs=6 corpses=0
[stage8] generated=8 renderers=532 outlines=220 vfx=attack+gather+damage
```

Скриншот: `artifacts/stage8-smoke.png` (1 728 666 байт), он же `artifacts/release/desktop-play.png`.

Проверка мира/производительности (Editor, 1280×720, после static batching):

```text
[agent-smoke] perf renderers=3579 static=3268 lodGroups=577 drawCalls=416
              renderFps=612,6 profile=desktop budgets=500/200
```

416 draw calls — в пределах desktop-бюджета 500. Мобильный бюджет 200 в этой сессии не
перемерялся (нет устройства/ADB); это ограничение, а не подтверждённый результат.

### 3.4. Релизные артефакты

| Артефакт | Размер | SHA-256 |
|---|---:|---|
| `AlbionPrototype-0.7.0.apk` | 40 876 817 | `532CE8C40856385C8189408125D867535924172A27207A18F449CB3F5ABCAD6E` |
| `AlbionPrototype-desktop-0.7.0-win-x64.zip` | 43 959 512 | `9BA92E46BD32C91BD10DC08B16C7A619E01A14318A8EF5E49DD04D80EF282568` |
| `AlbionPrototype-server-0.7.0-win-x64.zip` | 350 773 | `B0FD3F6CB3C564B1A0316B0EB93B237CC8D886C360E9E66FDD84A13C6E25B981` |

- APK: `versionName 0.7.0`, `versionCode 4`, minSdk 26, targetSdk 36, `arm64-v8a`,
  debug-подпись (APK Signature Scheme v2: `Verifies`); строка `0.7.0` найдена в
  `assets/bin/Data/globalgamemanagers`.
- Desktop: `0.7.0` найдена в `AlbionPrototype_Data/globalgamemanagers`; сборка 107,4 MB.
- Server: `Server.exe --validate-config-only` из распакованного `server/` — `config valid`,
  hash совпал с клиентским.

## 4. Ограничения

- APK не устанавливался на реальное устройство; Android FPS и touch-инвентарь не проверены.
- Инвентарь — клиентский журнал сеанса, не авторитетное хранилище; после рестарта сервера
  он пуст. Снаряжение, серебро/Слава и лут трупов авторитетны.
- VFX/модели — low-poly заглушки; outline-дубликаты добавляют draw calls, поэтому на
  мобильном нужен отдельный замер.
- `dotnet format --verify-no-changes` имеет прежние замечания (CRLF, порядок using в
  тестах), не связанные со Stage 8.
