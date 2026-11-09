# State

Last updated: 2026-11-09 19:10 UTC
Last heartbeat: 2026-11-09 19:10 UTC
Current milestone: M10 — Визуализация: URP, стилизация, два профиля качества
Current task: Auto-Gate пройден, PR #11 открыт, ожидание Human-Gate M10
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
| M4 | ✅ | ✅ | done (2026-11-09, `d827c9a`, ADR-008 одобрен) |
| M5 | ✅ | ✅ | done (2026-11-09, merged `cfccaa6`) |
| M6 | ✅ | ✅ | done (2026-11-09, merged `d292d27`) |
| M7 | ✅ | ✅ | done (2026-11-09, merged `9206efe`, PR #8) |
| M8 | ✅ | ✅ | done (2026-11-09, merged `ccfb19d`, PR #9) |
| M9 | ✅ | ✅ | done (Human-Gate пройден 2026-11-09, PR #10 смержен `1e85782`) |
| M10 | ✅ | ⏳ | awaiting human review (URP, стилизация, два профиля) |
| M11 | — | — | todo |
| M12 | — | — | todo |

## M10 tasks

| Task | Status | Notes |
|---|---|---|
| 1. URP: Lit-материалы, направленный свет, ambient | done | URP 17.3.0 из встроенного пакета Unity (офлайн, Issue #2 закрыт для M10); `GraphicsSettings.defaultRenderPipeline` = desktop-ассет; тёплый directional + trilight ambient + fog |
| 2. Стилизованные шейдеры (toon, контур) | done | `Assets/Shaders/ToonLit.shader` (2-цветная рампа, тень, rim, контур inverted hull, `UniversalForwardOnly`) и `ToonGround.shader` (шахматка без текстур) |
| 3. LOD, GPU Resident Drawer/аналог, occlusion culling | done | `LODGroup` на 8 колоннах и 6 камнях (2 уровня); статические флаги + `StaticOcclusionCulling` (данные `Scenes/Arena/OcclusionCullingData.asset`); GPU Resident Drawer включён в desktop-ассете (`m_GPUResidentDrawerMode = Instanced`) |
| 4. Два профиля: desktop (post, deferred) и mobile (forward, без post) | done | Два URP-ассета + два renderer data; `QualityProfile` переключает в рантайме; HUD `profile`/`fps`, лог `QualityProfile` |
| 5. Заменить капсулы на ассеты | частично | Внешние Synty POLYGON недоступны (нет сети/лицензии) — процедурные стилизованные персонажи (тело/голова/наплечники/оружие, палитра). Замена на готовые ассеты остаётся оператору |
| 6. Сцена арены | done | `AlbionWorldBuilder`: пол, колонны, камни, кристаллы, свет, volume, камера; `Assets/Scenes/Arena.unity` в Build Settings |
| 7. Сборки обоих профилей | done | `AlbionBuild`: `Builds/desktop` и `Builds/mobile` (символ `ALBION_MOBILE_DEFAULT`), обе `Result: Success` |

## M10: принятые решения (на ревью оператору)

1. **URP без сети на этой машине.** Сам `com.unity.render-pipelines.universal@17.3.0`, `core`, `shadergraph` и `universal-config` поставляются с редактором (BuiltInPackages); registry-зависимости `core` (`burst`, `mathematics`, `collections`) и `shadergraph` (`searcher`) взялись из глобального кэша UPM на этой машине. UPM в сеть не ходил. На чистом клоне без сети и без этого кэша возможен один онлайн-резолв — риск отмечен в Issues (#2).
2. **Профиль = готовый URP-ассет, а не настройки в рантайме.** Desktop: deferred, HDR, MSAA 4x, render scale 1.0, тени 2048/60, GPU Resident Drawer, Bloom+Vignette+ColorAdjustments. Mobile: forward, без HDR/MSAA/пост-обработки, render scale 0.8, тени 1024/25 и выключены, target 30 FPS. Переключение — `ALBION_QUALITY` / `-quality desktop|mobile` / дефолт сборки; ассеты в `Resources`, поэтому без перезапуска.
3. **Стилизация без внешних ассетов.** Toon-шейдер с контуром и процедурные персонажи из простых форм; палитра «герой зелёно-золотой, чужие синие, мёртвые серые». Это осознанное отклонение от «Synty POLYGON»: скачать и лицензировать ассеты агент не может. Human-Gate решает, достаточно ли стилизации, или оператор подменит модели (интерфейс материалов готов).
4. **Deferred + forward-only шейдер.** Toon-материалы помечены `UniversalForwardOnly` и рендерятся forward-проходом даже в deferred desktop-профиле; статичная геометрия (пол/колонны/камни) тоже toon-шейдер, поэтому визуально профили отличаются пост-обработкой и качеством, а не материалом.
5. **LOD и occlusion — на статичной геометрии.** Колонны/камни имеют LODGroup (2 уровня), помечены Static (Batching/Occluder/Occludee), данные occlusion запечены (`Scenes/Arena/OcclusionCullingData.asset`, ~6.6 КБ). GPU Resident Drawer — «аналог GPU-driven» из плана; на D3D12 работает, на неподдерживаемом железе URP предупреждает.
6. **FPS — вручную.** Headless-замер FPS на GUI-плеере в этом окружении недоступен (запуск окна блокируется), поэтому FPS виден в HUD и пишется в лог раз в 10 секунд; Auto-Gate по FPS закрывается на Human-Gate (≥60 desktop, ≥30 mobile).

## M10 Auto-Gate (проверено 2026-11-09)

- [x] URP-ассеты созданы и валидированы: `URP: валидация профилей и контента пройдена.` (deferred/forward, HDR, MSAA, render scale, тени, Resources.Load, шейдеры, сцена в Build Settings, Linear color space).
- [x] Оба профиля собираются: `Builds/desktop` — `result=Succeeded, size=86 500 373, errors=0`; `Builds/mobile` — `result=Succeeded, size=86 500 389, errors=0` (`ALBION_MOBILE_DEFAULT`).
- [x] Сцена арены: `Assets/Scenes/Arena.unity` (112 593 байта), 14 LODGroup (8 колонн + 6 камней), OcclusionCullingSettings, Volume, Ground, Directional Light, Main Camera; включена в Build Settings.
- [x] Occlusion-данные запечены: `Assets/Scenes/Arena/OcclusionCullingData.asset` (6 595 байт).
- [x] dotnet-слой не затронут: 202/202 тестов, покрытие `Simulation` 100%, arch-checks и Unity-boundaries зелёные (прогон `tools/dev/test-local.ps1` + `tools/ci/*.sh`).
- [x] HUD: `profile: desktop|mobile`, сглаженный `fps`; `Debug.Log("[fps] profile=… avg=…")` раз в 10 с — данные для Human-Gate.
- [x] CI-run [42691148773](https://github.com/avvasiliev888/albion-prototype/actions/runs/42691148773) — success (dotnet-слой; Unity-сборки проверены локально в batchmode).

## M10 Human-Gate (ожидает оператора)

**PR:** [#11](https://github.com/avvasiliev888/albion-prototype/pull/11), ветка `agent/M10/urp-stylization`.

1. Откройте Unity 6.3 → `unity-client` (проект уже настроен: URP, `Assets/Scenes/Arena.unity`, `Assets/Resources/Rendering`). Если сцена не открылась — откройте `Assets/Scenes/Arena`.
2. Для сцены: `Window/Rendering/Occlusion Culling` → `Bake` (данные уже есть, но перезапеките под своё железо), затем Play.
3. Запустите сервер: `dotnet run --project src/Server -c Release -- --udp 9050`.
4. В Play проверьте: арена с колоннами/камнями/кристаллами, toon-освещение с контуром и rim, туман и тёплый свет; HUD `profile: desktop | fps: N`.
5. Сравните с референсами Product Vision: «стилизованно-фэнтезийно». Если ассеты-примитивы выглядят бедно — скажите, подменим на ваши ассеты (материалы/шейдеры готовы) или подкрутим палитру/свет.
6. Профили: `Builds/desktop/AlbionPrototype.exe -quality mobile` (или `ALBION_QUALITY=mobile`) — картинка без пост-обработки, render scale 0.8, HUD `profile: mobile`, FPS ≥ 30. Desktop-плеер — FPS ≥ 60 (HUD/лог `[fps]`).
7. Две сборки лежат в `unity-client/Builds/desktop` и `unity-client/Builds/mobile` (в git не коммитятся); исходники сборки — `Assets/Editor/AlbionBuild.cs`.
8. GPU Resident Drawer: если железо не D3D12, URP напишет предупреждение — это ожидаемо, профиль продолжает работать.

## M9: закрыто

Human-Gate M9 пройден 2026-11-09: способность срабатывает с первого нажатия стоя и в движении, `cd: 40 → 0`, спам во время кулдауна игнорируется, data-driven (cooldown 6 / damage 5) проверен и возвращён к 25/2. PR #10 смержен в `main` (`1e85782`).

## M8: закрыто

Human-Gate M8 пройден 2026-11-09: дельта-снапшоты не сломали предсказание, `delta` растёт почти каждый тик, `fullReq` низкий, Ctrl+C сервера не крашит клиент. PR #9 смержен (`ccfb19d`).

## M7: закрыто

Human-Gate M7 пройден: плавное движение, мгновенная остановка, корректный dead reckoning; PR #8 смержен (`9206efe`). Одобрены ADR-009 и раздельная отрисовка локального/удалённых игроков.

## M6: закрыто

Human-Gate M6 пройден: подключение из Unity, движение по WASD, остановка сервера без краша. PR #7 смержен (`d292d27`).

## Environment

| Что | Значение |
|---|---|
| ОС | Windows 11 (10.0.22631), PowerShell 7.5.4 |
| .NET SDK | 10.0.403, `global.json` фиксирует версию |
| Unity | 6000.3.28f1 LTS, `N:\Program Files\Unity Hub\Editor\6000.3.28f1` |
| URP | `com.unity.render-pipelines.universal@17.3.0` (BuiltInPackages, офлайн); два профиля в `Assets/Resources/Rendering` |
| Репозиторий | `https://github.com/avvasiliev888/albion-prototype` (private), `main` |
| Git LFS | 3.7.1, `--local` |
| Транспорт | LiteNetLib 2.1.4 (NuGet), UDP; порт 9050; **protocol v4** |
| Unity-клиент | `unity-client/`, сцена `Assets/Scenes/Arena.unity`, стилизация `Assets/Shaders`, генераторы `Assets/Editor` |
| Сборки | `Builds/desktop`, `Builds/mobile` (генерируются `AlbionBuild`, не коммитятся) |

## Open Questions

(нет)

## Resolved Questions

1. **Какой .NET SDK и кто ставит?** → .NET 10 LTS, устанавливает агент. (2026-11-08)
2. **Какая Unity и кто ставит?** → Unity 6.3 LTS (6000.3.x), ставит и активирует оператор. (2026-11-08)
3. **Как проверяется «Unity собирается»?** → локально оператором/агентом в batchmode; GameCI — только при реальном билде. (2026-11-08)
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

1. **VSTest не работает внутри workspace на `F:`** — testhost падает с `Win32Exception (5)`; обход `tools/dev/test-local.ps1`. Приоритет: низкий (ограничение хоста).
2. **Unity Package Manager и сеть — для M10 снято на этой машине.** URP/core/shadergraph/universal-config — в BuiltInPackages редактора; `burst`/`mathematics`/`collections`/`searcher` — из глобального кэша UPM. На чистом клоне без кэша нужен один онлайн-резолв. Приоритет: низкий (окружение оператора кэш имеет).
3. **`bash` в PATH — WSL**, скрипты CI локально через `C:\Program Files\Git\bin\bash.exe`. Приоритет: низкий.
4. **CRLF-предупреждения git** на Windows — косметика. Приоритет: низкий.
5. **LiteNetLib: `NetPeer.Id` — поле, не свойство.** Приоритет: низкий.
6. **Unity не ссылается на csproj вне `Assets`** — плагины генерируются `tools/unity/build-plugins.ps1`. Приоритет: низкий.
7. **Плагины в `Assets/Plugins` видны всем сборкам Unity** — глобальный namespace `Client` конфликтовал с `com.unity.multiplayer.center`; пакет удалён. Приоритет: низкий.
8. **System.Text.Json не грузится в Unity** — закрыто в M9: `ConfigLoader` в `Shared` на `MiniJson`. Приоритет: снят.
9. **ADR-006 в части «копия Simulation на клиенте» заменён ADR-009**; M9 синхронизировал конфиг файлом и хешем. Приоритет: низкий.
10. **Клиент знает только `moveSpeed`** — M9 читает весь конфиг; расхождение с сервером логируется. Приоритет: низкий.
11. **История снапшотов клиента — 16 тиков (0.8 с).** При большей задержке — полные снапшоты (работает, трафик растёт). Приоритет: низкий.
12. **Первый снапшот сессии — всегда полный** (нужна база). Приоритет: низкий.
13. **JSON в `StreamingAssets` — артефакт сборки**; забыли скрипт — `config error`. Приоритет: низкий.
14. **Space по себе наносит урон себе** — нет других сущностей; таргетинг вне M9. Приоритет: средний к M11.
15. **Внешние ассеты (Synty POLYGON) недоступны агенту** — нет сети/лицензии; M10 использует процедурные формы. Приоритет: средний (решается оператором).
16. **Occlusion bake в batchmode** создаёт `OcclusionCullingData.asset`, но пишет warning о размере (гонка с асинхронным завершением). Данные есть; перезапекание в редакторе рекомендовано. Приоритет: низкий.
17. **GPU Resident Drawer требует D3D12/поддерживаемого железа**; на остальном — warning URP, профиль работает. Приоритет: низкий.
18. **Mobile-профиль без теней** (осознанно, ради FPS ≥ 30); если оператор захочет тени — включим и замерим. Приоритет: низкий.
19. **Не удалось снять FPS headless**: GUI-плеер в этом окружении не запускается фоновым процессом (Start-Process зависает). FPS — HUD + лог раз в 10 с, замер на Human-Gate. Приоритет: низкий.

## Next Actions (для следующей сессии)

1. Human-Gate M10 по PR #11; после одобрения — merge оператором (Squash and merge), затем M11.
2. Human-Gate M10: визуальная оценка стилизации, сравнение с референсами, FPS desktop/mobile; при необходимости — замена процедурных моделей на ассеты оператора, подстройка палитры/света.
3. M11: нагрузочный тест ботами (20 ботов, тик ≤ 50 мс) — серверная работа, Unity не требуется; учесть ограничения хоста (VSTest).
