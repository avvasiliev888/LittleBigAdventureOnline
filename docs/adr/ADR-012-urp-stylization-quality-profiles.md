# ADR-012: URP, два профиля качества и стилизация (M10)

- **Статус**: принято (M10)
- **Дата**: 2026-11-09
- **Контекст**: M6–M9 дали рабочий геймплей в Built-in pipeline: капсулы, плоский свет, без
  пост-обработки. M10 требует URP, стилизации и двух профилей качества, при этом UPM на этом
  хосте не выходит в сеть (Discovered Issue #2), а бесплатные ассеты (Synty POLYGON и аналоги)
  нельзя скачать и лицензировать автоматически.

## Решение

### 1. URP подключается из встроенного пакета Unity, без сети

`unity-client/Packages/manifest.json` содержит `com.unity.render-pipelines.universal@17.3.0`.
Пакет, `core`, `shadergraph` и `universal-config` поставляются вместе с редактором 6000.3.28f1
в `Editor/Data/Resources/PackageManager/BuiltInPackages`; registry-зависимости `core`
(`burst`, `mathematics`, `collections`) и `shadergraph` (`searcher`) на этой машине уже лежат
в глобальном кэше UPM, поэтому разрешение прошло без обращения к сети. Блокер Issue #2 для M10
снят на окружении оператора; на чистом клоне без кэша UPM возможен один онлайн-резолв.

### 2. Ассеты URP генерируются редакторским скриптом и коммитятся

`Assets/Editor/AlbionUrpSetup.cs` (меню `Albion/Rendering/Setup URP`, batchmode
`-executeMethod Albion.Editor.AlbionBatch.All`) создаёт:

- `Assets/Resources/Rendering/UniversalRendererDesktop.asset` — **deferred**;
- `Assets/Resources/Rendering/UniversalRendererMobile.asset` — **forward**;
- `Assets/Resources/Rendering/UrpDesktop.asset` — HDR, MSAA выключен (не поддерживается
  deferred; сглаживание SMAA на камере), render scale 1.0, мягкие тени 2048, два каскада,
  shadow distance 60, GPU Resident Drawer включён;
- `Assets/Resources/Rendering/UrpMobile.asset` — без HDR/MSAA, render scale 0.8, тени 1024,
  shadow distance 25, тени выключены, GPU Resident Drawer выключен;
- `Assets/Resources/Rendering/AlbionPostProcess.asset` — VolumeProfile (Bloom, Vignette,
  ColorAdjustments, ACES Tonemapping); overrides сохраняются как subassets через
  `AssetDatabase.AddObjectToAsset` и проверяются после синхронного reimport с диска.

Ассеты лежат в `Resources`, потому что `QualityProfile` грузит их через `Resources.Load`:
переключение профиля не требует ни перезапуска, ни отдельной сборки. Desktop назначается
`GraphicsSettings.defaultRenderPipeline`, URP global settings регистрируются автоматически.

### 3. Профиль выбирается в рантайме

`Assets/Scripts/Rendering/QualityProfile.cs`: профиль из `ALBION_QUALITY`, аргумента
`-quality desktop|mobile` или дефолта сборки (`ALBION_MOBILE_DEFAULT` в mobile-сборке).
Профиль подменяет URP-ассет, включает/выключает пост-обработку на камере и Volume, задаёт
render scale (в ассете), shadow distance, vsync и target frame rate (120 desktop / 30 mobile).
HUD показывает `profile: desktop|mobile` и сглаженный FPS, раз в 10 секунд FPS пишется в лог —
это доказательство для Auto-Gate и Human-Gate.

### 4. Стилизация: toon-шейдеры и процедурные персонажи

- `Assets/Shaders/ToonLit.shader` — двухцветная рампа, мягкая тень, rim-подсветка, ambient и
  **второй проход-контур** (inverted hull). Проход помечен `UniversalForwardOnly`, поэтому
  материал работает и в forward, и в deferred renderer (в deferred такие материалы идут в
  forward-only проход). Отдельные `ShadowCaster` и `DepthOnly` работают по исходной геометрии,
  не по расширенному контуру. Основной проход и контур компилируют fog variants и применяют `MixFog`.
- `Assets/Shaders/ToonGround.shader` — шахматка в мировых координатах + toon-освещение, без текстур.
- `UnityView` собирает персонажа из простых форм (тело, голова, наплечники, оружие) с
  палитрой: герой зелёно-золотой, чужие синие, мёртвые серые. Коллайдеры удаляются — физики на
  клиенте нет.

**Отклонение от плана:** вместо скачивания Synty POLYGON (нет сети/лицензии) используются
процедурные стилизованные формы. Human-Gate оценивает именно «выглядит стилизованно-фэнтезийно»;
замена на готовые ассеты — задача оператора/следующего этапа, интерфейс материалов к этому готов.

### 5. Сцена, LOD, occlusion culling

`Assets/Editor/AlbionWorldBuilder.cs` (меню `Albion/World/Rebuild Arena`) собирает
`Assets/Scenes/Arena.unity`: пол с toon-материалом, 8 колонн и 6 камней с `LODGroup` (2 уровня),
кристаллы, тёплый направленный свет, трёхцветный ambient, туман, глобальный Volume и камеру.
Статичная геометрия помечена `BatchingStatic | OccluderStatic | OccludeeStatic`;
`StaticOcclusionCulling.Compute()` вызывается в batchmode; данные запекаются в
`Assets/Scenes/Arena/OcclusionCullingData.asset` (в логе batchmode предупреждение о размере
возможно, при этом ассет создаётся; причина расхождения размера не установлена). Валидатор проверяет
наличие данных; если их нет, оператор повторяет запекание в редакторе
(`Window/Rendering/Occlusion Culling`).
GPU Resident Drawer включён в desktop-ассете через `m_GPUResidentDrawerMode` (Instanced) — это
аналог «GPU-driven» из плана; на неподдерживаемом железе URP логирует предупреждение.

### 6. Сборки

`Assets/Editor/AlbionBuild.cs` (меню `Albion/Build/Desktop|Mobile`) собирает два Windows-плеера
в `Builds/desktop` и `Builds/mobile`; mobile-сборка получает символ `ALBION_MOBILE_DEFAULT`.
«Оба профиля собираются» проверяется именно двумя `BuildPipeline.BuildPlayer`, а не только
переключением в редакторе. `Builds/` и `Library/` в `.gitignore`.

## Последствия

- URP-ассеты и сцена — обычные ассеты в git (YAML), их можно править в редакторе; скрипты
  генерации остаются для воспроизводимости на чистом клоне.
- Стилизация держится на двух шейдерах без внешних зависимостей; замена ассетов не меняет код.
- Профили различаются и по пайплайну (deferred/forward), и по настройкам (HDR, MSAA, post,
  render scale, тени, FPS cap) — это видно в HUD и в логе `QualityProfile`.
- Occlusion-данные в batchmode не запекаются; это задокументировано в инструкции Human-Gate.
