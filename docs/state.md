# State

Last updated: 2026-11-10 17:30 UTC
Last heartbeat: 2026-11-10 17:30 UTC
Current milestone: M12 — Полировка, демо, документация
Current task: M12 final — 2026-11-10. README с нуля, `docs/demo.md`, `tools/demo/start-demo.ps1`, исправлен спавн второго игрока внутри манекена (dummy `(4,0,1.5)`), 230/230 tests, demo smoke 2/2 на стабильной сети и при 100 мс/30%. Ветка `agent/M12/final-polish`, финальный [PR #14](https://github.com/avvasiliev888/albion-prototype/pull/14) открыт, CI green; merge — за оператором.
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
| M10 | ✅ GPU + 223 tests | ✅ принят оператором 2026-11-10 | merged in #12 |
| M11 | ✅ 20/60: avg 0.080 ms, p99 0.348 ms | ✅ принят оператором; merged #13 `9aafbc1` | done (2026-11-10) |
| M12 | ✅ 230/230, demo smoke, README | ⏳ ждёт оператора | `agent/M12/final-polish`, [PR #14](https://github.com/avvasiliev888/albion-prototype/pull/14) open, CI green |

## M10 tasks

| Task | Status | Notes |
|---|---|---|
| 1. URP: Lit-материалы, направленный свет, ambient | done | URP 17.3.0 из встроенного пакета Unity (офлайн, Issue #2 закрыт для M10); `GraphicsSettings.defaultRenderPipeline` = desktop-ассет; тёплый directional + trilight ambient + fog |
| 2. Стилизованные шейдеры (toon, контур) | done | `Assets/Shaders/ToonLit.shader` (2-цветная рампа, тень, rim, контур inverted hull, `UniversalForwardOnly`) и `ToonGround.shader` (шахматка без текстур) |
| 3. LOD, GPU Resident Drawer/аналог, occlusion culling | done | `LODGroup` на 8 колоннах и 6 камнях (2 уровня); статические флаги + `StaticOcclusionCulling` (данные `Scenes/Arena/OcclusionCullingData.asset`); GPU Resident Drawer включён в desktop-ассете (`m_GPUResidentDrawerMode = Instanced`) |
| 4. Два профиля: desktop (post, deferred) и mobile (forward, без post) | done | Два URP-ассета + два renderer data; `QualityProfile` переключает в рантайме; HUD `profile`/`fps`, лог `QualityProfile` |
| 5. Заменить капсулы на ассеты | done (implementation), Human-Gate pending | Точный Warrior.prefab через Resources; live SkinnedMeshRenderer + Humanoid Idle/Walk/Run, Animator enabled/root motion OFF; Speed из prediction, поворот visual по движению; height rest 1.85 m, root/snapshot/prediction сохранены. M10.5 GPU/оба build/regression passed |
| 6. Сцена арены | done | `AlbionWorldBuilder`: пол, колонны, камни, кристаллы, свет, volume, камера; `Assets/Scenes/Arena.unity` в Build Settings |
| 7. Сборки обоих профилей | done | `AlbionBuild`: `Builds/desktop` и `Builds/mobile` (символ `ALBION_MOBILE_DEFAULT`), обе `Result: Success` |

## M10: принятые решения (на ревью оператору)

1. **URP без сети на этой машине.** Сам `com.unity.render-pipelines.universal@17.3.0`, `core`, `shadergraph` и `universal-config` поставляются с редактором (BuiltInPackages); registry-зависимости `core` (`burst`, `mathematics`, `collections`) и `shadergraph` (`searcher`) взялись из глобального кэша UPM на этой машине. UPM в сеть не ходил. На чистом клоне без сети и без этого кэша возможен один онлайн-резолв — риск отмечен в Issues (#2).
2. **Профиль = готовый URP-ассет, а не настройки в рантайме.** Desktop: deferred, HDR, SMAA камеры (MSAA выключен для deferred), render scale 1.0, мягкие тени 2048/60 с двумя каскадами, GPU Resident Drawer, Bloom+Vignette+ColorAdjustments+ACES Tonemapping. Mobile: forward, без HDR/MSAA/пост-обработки, render scale 0.8, тени 1024/25 и выключены, target 30 FPS. Переключение — `ALBION_QUALITY` / `-quality desktop|mobile` / дефолт сборки; ассеты в `Resources`, поэтому без перезапуска.
3. **Warrior вместо процедурного персонажа (указание оператора).** Исходная процедурная модель отклонена. Импортированный оператором Warrior.prefab подключён через Resources-конфигурацию; после M10.5 тело остаётся живым SkinnedMeshRenderer с Animator enabled и root motion OFF; коллизии отключены, URP/Lit сохранён. Humanoid Idle/Walk/Run ретаргетятся с общего BaseMale Avatar из скачанного All Star Characters. Height 1.85 m в rest pose; размер/опора только в calibration вне Animator, animated Idle feet≈−0.030 m/height≈1.871 m (поза меняет габариты). Меч+топор одновременно активны в исходном prefab, сохранены. Все игроки используют исходные материалы; смерть скрывает visual до респавна. Окружение остаётся примитивным; M10 визуально не принят. ADR/план не изменялись.
4. **Deferred + forward-only шейдер.** Toon-материалы помечены `UniversalForwardOnly` и рендерятся forward-проходом даже в deferred desktop-профиле; статичная геометрия (пол/колонны/камни) тоже toon-шейдер, поэтому визуально профили отличаются пост-обработкой и качеством, а не материалом.
5. **LOD и occlusion — на статичной геометрии.** Колонны/камни имеют LODGroup (2 уровня), помечены Static (Batching/Occluder/Occludee), данные occlusion запечены (`Scenes/Arena/OcclusionCullingData.asset`, ~6.6 КБ). GPU Resident Drawer — «аналог GPU-driven» из плана; на D3D12 работает, на неподдерживаемом железе URP предупреждает.
6. **FPS — вручную.** Headless-замер FPS на GUI-плеере в этом окружении недоступен (запуск окна блокируется), поэтому FPS виден в HUD и пишется в лог раз в 10 секунд; Auto-Gate по FPS закрывается на Human-Gate (≥60 desktop, ≥30 mobile).

## M10 Auto-Gate (проверено 2026-11-09)

- [x] URP-ассеты созданы и валидированы: `URP: валидация профилей и контента пройдена.` (deferred/forward, HDR, MSAA, render scale, тени, Resources.Load, шейдеры, сцена в Build Settings, Linear color space).
- [x] После graphics repair оба профиля собираются: desktop и mobile — `result=Succeeded, size=86 521 093, errors=0` каждый; mobile с `ALBION_MOBILE_DEFAULT`. Логи `Builds/m10-repair-build-desktop.log` и `Builds/m10-repair-build-mobile.log` (ignored).
- [x] Пересоздана `Assets/Scenes/Arena.unity`: 14 LODGroup, Volume с 4 сохранёнными overrides, Directional Light Soft/0.9, камера с URP AdditionalData/post/SMAA, fog Exp2/.032; включена в Build Settings.
- [x] Файл occlusion-данных присутствует; Compute в batchmode сообщает warning/неуспех, поэтому bake как успешный не заявляется — повторить в редакторе.
- [x] GPU probe `AlbionBatch.RepairAndVerify` — exit 0 на Radeon RX 5700 / D3D12, 1280×720. `Builds/visual-check/metrics.json`: fog desktop 911230 изменённых пикселей, shadows 71183 (59693 пикселя receiving ground темнее), post 918613, light direction 920164, fog mobile 913119; порог 921/epsilon 3. PNG просмотрены, теневые силуэты и атмосферный градиент видны. Это не одобрение художественного качества.
- [x] dotnet-слой не затронут: 202/202 тестов, покрытие `Simulation` 100%, arch-checks и Unity-boundaries зелёные (прогон `tools/dev/test-local.ps1` + `tools/ci/*.sh`).
- [x] HUD: `profile: desktop|mobile`, сглаженный `fps`; `Debug.Log("[fps] profile=… avg=…")` раз в 10 с — данные для Human-Gate.
- [x] Graphics repair CI-run [42697934134](https://github.com/avvasiliev888/albion-prototype/actions/runs/42697934134) — success (`5e88c92`, dotnet-слой; Unity/GPU проверены локально в batchmode).

## M10 Warrior repair: локальные проверки 2026-11-09 21:40 UTC

- `AlbionCharacterSetup.ConfigureAndVerify`: exit 0; точный prefab/URP, height 1.85/feet 0, единичный network root, локальная prediction + удалённый snapshot, смерть/респавн.
- GPU Radeon RX 5700 / D3D12, 1280×720: все 7 сравнений passed; персонажи дают 9719 desktop / 5807 mobile изменённых пикселей относительно кадра без моделей (порог 921), magenta отсутствует. Крупные планы и runtime-ракурс просмотрены.
- Реальные desktop/mobile Windows builds: `result=Succeeded, size=92978597, errors=0` каждый. UsedAssets обоих build подтверждает 4 FBX/5 PNG/2 материала/Warrior.prefab + Resources-конфиг; лишние demo/варианты не включены в commit.
- 202/202 .NET tests, Simulation coverage 100%, architecture/Unity-boundaries passed. CI для Warrior `02e11e2`: [pull_request 42705211576](https://github.com/avvasiliev888/albion-prototype/actions/runs/42705211576) и [push 42705200803](https://github.com/avvasiliev888/albion-prototype/actions/runs/42705200803) success. Unity/GPU/build — локальные проверки, не GitHub CI.
- Логи `Builds/m10-warrior-final-verify.log`, `Builds/m10-warrior-build-Desktop.log`, `Builds/m10-warrior-build-Mobile.log`; кадры/metrics в `Builds/visual-check` (ignored). Инструкция Inspector/materials/меню — `unity-client/README.md`, раздел Warrior.
- Локальные настройки оператора GraphicsSettings (linear/color temperature=1), QualitySettings (AA=4), URPProjectSettings и неиспользуемые vendor demo/варианты сохранены вне commit. После batch восстановлены только собственные defines/AA-изменения.
- Известный неиспользуемый external texture remap GUID в Warrior.fbx.meta не разрешается; назначенные Chainmail/Weapons текстуры валидны, build/GPU проходят. Vendor importer не редактировался ради этой посторонней записи.

## M10.8: cooldown-on-miss и боевой UI — 2026-11-10

- Живой отзыв оператора: манекен/урон/Death/respawn работают; остались спам-рестарт Attack, кулдаун только при попадании, невозможность замаха по мёртвой цели; запрошены HP-бары и индикатор кулдауна.
- **Семантика способности изменена на «активация важнее попадания»**: `AbilityCommand.TargetId = 0` — легальный удар в пустоту. Валидатор сервера проверяет только caster/кулдаун/ресурс/self-target, а неизвестная, мёртвая или дальняя цель больше не отклоняется. `Simulation.ApplyAbility` списывает ресурс и ставит кулдаун **до** разрешения цели; урон наносится только живой цели в радиусе. Протокол v4 не менялся: те же 13 байт, изменилась семантика нуля.
- **Клиент**: `UnityInputSource` всегда отправляет Space-команду, `ResolveAbilityTarget` возвращает ближайшую живую сущность или 0; prediction ставит кулдаун сразу, поэтому промах/мёртвая цель/пустота тоже охлаждают способность. Защита от self-target осталась на сервере и в симуляции.
- **Спам Attack**: `WarriorVisual.IsAttackPlaying` + guard в `PlayAttack` — повторный Space во время текущего swing игнорируется до exit-фазы (0.9), клип не перезапускается с первого кадра. Prediction-кулдаун остаётся вторым барьером.
- **HP-бары**: `HealthBar` — два world-space quad над головой (тёмная подложка + красное заполнение), всегда виден при полном/нулевом HP, billboard к камере; добавлен каждой сущности в `CharacterVisualSettings.Create`, обновляется из `UnityView` по snapshot HP.
- **Cooldown HUD**: `CooldownHud` — круглая иконка меча внизу по центру (процедурные текстуры, без UGUI/Canvas). Готова — золотая; на кулдауне — серая, золотое заполнение растёт снизу вверх по `CooldownRemainingTicks` prediction, реконсилированному с сервером.
- 212/212 .NET tests, coverage 100% четырёх модулей; architecture/Unity-boundaries passed. Unity `ConfigureAndVerify`: exit0, GPU 13/13, spam-guard пройден. Оба build: Succeeded, size93163186, errors0; `Assembly-CSharp.dll` 14:45:41/14:46:17; логи `Builds/m10-8-{desktop3,mobile3,configure2}.log`. CI `d850480`: [pull_request 42809430278](https://github.com/avvasiliev888/albion-prototype/actions/runs/42809430278), [push 42809439240](https://github.com/avvasiliev888/albion-prototype/actions/runs/42809439240) — success.
- GPU probe: red HP bars обнаружены над обеими сущностями (2 компонента, ~50×6 px каждый); HUD не входит в probe (OnGUI), проверяется живым клиентом. M10 не принят, M11 не начат.

## M10.8.1: живой отзыв по бою — 2026-11-10

Оператор проверил M10.8 и нашёл три дефекта; исправление в том же PR #12.

1. **Зажатый Space не давал авто-атаку.** `UnityInputSource.PollFrame` читал `GetKeyDown`, поэтому после первого удара удержание ничего не делало. Теперь `GetKey`: пока клавиша зажата, каждый кадр ставится флаг, а клиентский тик отправляет `AbilityCommand`, как только prediction-кулдаун равен 0. Отпускание прекращает новые команды; короткий тап по-прежнему не теряется между тиками.
2. **После респавна манекена клиент мог закешировать мёртвое состояние.** `ClientPipeline.FindNearestEntity` пропускал всех мёртвых и возвращал 0. Теперь приоритет у ближайшей живой сущности, но если живых нет — возвращается ближайшая мёртвая. Удар по мёртвой цели — легальный miss с кулдауном; тот же id 2 выбирается и после respawn, а сервер сам решает, наносить ли урон. `TargetId=0` остаётся только для пустого мира. Это устраняет зависимость клиента от момента, когда снапшот обновил `IsDead`.
3. **Промах/мёртвая цель не показывали кулдаун.** Prediction ставит `CooldownRemainingTicks=40` в момент отправки, до ответа сервера и независимо от цели; сервер принимает miss и подтверждает кулдаун в снапшоте. Добавлен regression-тест: после targetless-промаха и двух тиков реконсиляции `CooldownRemainingTicks > 0`, а не 0. HUD читает именно это значение.

Дополнительно:
- **213/213 tests**, coverage 100% четырёх модулей. Новые тесты: `FindNearestEntity_PrefersLiving_AndFallsBackToDead`, `TargetsRespawnedDummy_Again_AfterSnapshotRefresh` (мёртвый → respawn → два удара 75/50 HP), targetless miss + реконсиляция сохраняет кулдаун.
- Это не отменяет правило «сервер отклонил — кулдаун откатился»: откат происходит только при реальном отказе (cooldown/resource/self), обычный miss принимается.
- Unity `ConfigureAndVerify` после правок: exit0, GPU 13/13, combat checks passed (лог `m10-8-1-configure.log`). Оба build: Succeeded, size93163186, errors0; `Assembly-CSharp.dll` 15:16:39/15:17:27; логи `m10-8-1-{desktop,mobile}.log`.
- 213/213 и оба build проверены локально. CI `d33cf64`: [pull_request 42814723909](https://github.com/avvasiliev888/albion-prototype/actions/runs/42814723909), [push 42814738483](https://github.com/avvasiliev888/albion-prototype/actions/runs/42814738483) — success.


## M11: нагрузочный тест 20 ботами — 2026-11-10

| Task | Статус | Заметки |
|---|---|---|
| `ServerMetrics` + замер `GameServer.Tick` | ✅ | ring-buffer 12 000; avg/p50/p99/max |
| Лог метрик и CLI | ✅ | `--metrics-every`, `--seconds`, `tick final metrics` |
| `tools/bot-client` | ✅ | 20 UDP-ботов, Hello, движение, способность по тренировочному манекену |
| `tools/load-test.ps1` | ✅ | пороги avg≤25/p99≤50, артефакты в `artifacts/m11-load/` |
| Тесты | ✅ | 229/229, `ServerMetricsTests`; 100% покрытие Simulation; CI green |
| Прогон 20×60 | ✅ | avg 0.080 мс, p99 0.348 мс, max 5.5 мс, 0 разрывов, 0 reject |
| Human-Gate | ⏳ | оператор запускает 20 ботов, смотрит метрики/деградацию |

Подробно: [m11-load-test.md](m11-load-test.md). CI: [push 42850560389](https://github.com/avvasiliev888/albion-prototype/actions/runs/42850560389), [PR 42850591846](https://github.com/avvasiliev888/albion-prototype/actions/runs/42850591846).

## M12: полировка, демо, документация — 2026-11-10

M11 принят оператором и смержен squash-merge в `main` (`9aafbc1`). M12 — финальный этап.

| Задача | Статус | Заметки |
|---|---|---|
| README с нуля | ✅ | корневой `README.md`: LFS, .NET, тесты, плагины, сервер, Unity, desktop build, управление, демо, плохая сеть, боты, конфиг, troubleshooting |
| Демо-сценарий | ✅ | `docs/demo.md` + `tools/demo/start-demo.ps1` (два клиента, движение, способность, смерть/респавн, 100 мс/30%) |
| Полировка спавна | ✅ | dummy сдвинут `(4,0,0)` → `(4,0,1.5)`: второй игрок (x=4) больше не появляется внутри манекена; regression-тест |
| Финальные gate'ы | ✅ | 230/230 tests, coverage 100%, arch/Unity-boundaries, demo smoke, load test, desktop/mobile builds |
| `docs/state.md` | ✅ | этот раздел; done/deferred/cut ниже |
| Human-Gate | ⏳ | оператор проходит демо и чеклист готовности |

**Проверки M12:**

- `pwsh -File tools/dev/test-local.ps1 -Configuration Release` — **230/230**, `Simulation.Core.Simulation` 100%.
  Новый тест `TwoClientsAndDummy_DoNotShareSpawnPoint`.
- Demo smoke: `pwsh -File tools/demo/start-demo.ps1 -Seconds 8` — сервер принял **2/2 Hello**;
  тот же прогон с `-DelayMs 100 -LossPercent 30` — **2/2**.
- Нагрузка 20 ботов/60 с: `connected=20/20`, `disconnects=0`, `avg=0.05 ms`, `p99=0.202 ms`,
  `max=5.195 ms` (порог avg≤25/p99≤50), 0 reject.
- Unity desktop/mobile: `result=Succeeded, errors=0`, size **93 170 090**; desktop build лог
  `m12-build-desktop.log`, mobile — `m12-build-mobile.log`; `Assembly-CSharp.dll` mobile 20:21:11.
- Arch checks: `check-architecture.sh`, `check-unity-boundaries.sh` — passed.
- `dotnet build Prototype.sln -c Release` — 0 ошибок (3 pre-existing nullable warnings в тестах).

**Сделано:**
- README, демо-документ и лаунчер; инструкции проверены на чистом клоне (restore/build/test/server).
- Исправлен визуальный дефект: второй клиент появлялся в одной точке с манекеном.
- Уточнён порог восстановления: серия потерь 10–12 лечится за ≤3 тика (`BurstOfThreeLostSnapshots_RecoversWithinThreeTicks`);
  при 30% i.i.d. потерь и задержке 2 тика лаг снапшота ≤8 тиков — это разные метрики.

**Отложено (не блокирует прототип):**
- Автоматический визуальный regression-тест смерти/респавна и «плохой сети» — проверяется глазом на Human-Gate;
  headless-тесты покрывают симуляцию и сетевой конвейер.
- Полноценный reconnect к перезапущенному серверу: сейчас клиент нужно открыть заново.
- Реальный сетевой emulator (tc/netem): задержка/потери детерминированно имитируются `LossyServerTransport`.

**Вырезано из прототипа:**
- Аккаунты/БД/матчмейкинг/экономика/крафт, ИИ противников, античит, мобильные платформы iOS/Android,
  финальный арт и звук. Mobile-профиль — desktop-билд с forward-рендером, не мобильная сборка.

## M10.11: переключение оружия — 2026-11-10

Префаб Warrior содержит `Warrior_Sword` и `Warrior_Axe` **оба** под `RightHandSocket`; поэтому в руках
одновременно были меч и топор. Щит остаётся в левой руке.

Реализация:
- `WeaponVisuals.Attach` в `CharacterVisualSettings.Create` находит оба объекта и при спавне оставляет
  только меч; топор скрыт через `SetActive(false)` (не удалён).
- `1`/`2` (и numpad) в `UnityInputSource.PollFrame` вызывают `UnityView.EquipWeapon`; меняется ровно
  один GameObject. Это **клиентская визуальность**: `AbilityCommand`, урон/радиус, сервер и протокол
  не менялись, удалённые игроки остаются с мечом.
- Атака — общий humanoid-клип `1HAttack`; оружие висит на hand socket и следует за анимацией.
  `ValidateWeaponAnimation` проверяет, что и меч, и топор сдвигаются во время Attack, а также
  дефолт/ровно один активный меш.
- HUD-подсказка дополнена `1/2 — оружие`.

Проверки:
- `ConfigureAndVerify`: exit 0, GPU 13/13; в логе
  `Warrior weapon checks passed: sword by default, 1/2 toggles one mesh, equipped weapon follows Attack`
  (`m10-11-configure.log`), combat checks passed.
- Живой прогон: при спавне
  `WeaponVisuals: equipped Sword (sword active=True, axe active=False)`, после клавиши 2 —
  `equipped Axe (sword active=False, axe active=True)`, после 1 — снова меч. Клавиши доходят до игры.
- Desktop/mobile: Succeeded, size **93 170 070**, errors0; `Assembly-CSharp.dll` 18:34:15 / 18:37:47;
  логи `m10-11-{desktop,mobile}.log`.
- .NET-код не менялся: **223/223 tests**, coverage 100%.
- CI `bf2fd8d`: [pull_request 42846775323](https://github.com/avvasiliev888/albion-prototype/actions/runs/42846775323),
  [push 42846786129](https://github.com/avvasiliev888/albion-prototype/actions/runs/42846786129) — success.

## M10.10.2: полоска маны под HP — 2026-11-10

Классический RPG-индикатор: у **каждой** сущности (локальный игрок, манекен, удалённые игроки) прямо
под HP-баром появилась тонкая синяя полоска ресурса. Ширина та же — 0.9; высота 0.045; зазор 0.015.
Заполнение — `entity.Resource / entity.MaxResource` из снапшота; обновляется в `UnityView.Render`
через `HealthBar.SetResource`, как и HP. Тёмная подложка остаётся видимой при нуле, поэтому пустая
мана читается. `res: X/Y` в верхнем HUD сохранён.

Реализация — те же четыре world-space quad-меша без Canvas/UGUI:
`HealthBar.Build` создаёт `Back`, `Hp Fill`, `Mana Back`, `Mana Fill`; `SetHp` и `SetResource`
масштабируют fill слева и прячут его только при нуле. `AlbionCharacterSetup.Validate` проверяет
наличие `Mana Back`/`Mana Fill`.

Проверки:
- `ConfigureAndVerify`: exit 0, GPU 13/13, combat checks passed, `m10-10-2-configure.log`.
- По GPU-скриншоту `warrior-desktop-close.png` красный HP занимает `x≈544–735, y≈184–196`, синяя
  мана — сразу под ним `x≈542–736, y≈200`: ширина совпадает, полоска тонкая.
- Desktop/mobile: Succeeded, size **93 167 846**, errors0; `Assembly-CSharp.dll` 17:52:17 / 17:53:04;
  логи `m10-10-2-{desktop,mobile}.log`.
- .NET-код не менялся: **223/223 tests**, coverage 100%.
- CI `5ca5645`: [pull_request 42838964991](https://github.com/avvasiliev888/albion-prototype/actions/runs/42838964991),
  [push 42838984896](https://github.com/avvasiliev888/albion-prototype/actions/runs/42838984896) — success.

## M10.10.1: регенерация ресурса — 2026-11-10

Оператор повторил «бессмертие» манекена после респавна. Headless-тесты были зелёными, потому что
каждый из них начинал с полного ресурса; живой прогон с серверным логом показал настоящую причину:

```
[t1499] conn 1: rejected (NotEnoughResource) — ресурс 0 < 10
[t1540] conn 1: rejected (NotEnoughResource) — ресурс 0 < 10
```

Удар стоит 10, максимум 100, регенерации не было: после 10 активаций ресурс навсегда 0. Первый удар
после респавна ещё проходил, все следующие сервер отклонял; HP манекена застревал на 75. Это не кеш
`TargetId` и не кулдаун.

Исправления:
1. `ActorConfig.ResourceRegenPerSecond` (+ валидация конечного неотрицательного числа) и поле
   `resourceRegenPerSecond` в `actors.json`; у игрока **5.0** — ровно 10 ресурса за 2 секунды кулдауна.
2. `Simulation.Tick` регенерирует ресурс **до** применения способностей; дробная часть копится в
   `EntityState.ResourceRegenRemainder` (5/с × 0.05 = 0.25 за тик), поэтому нет потерь на округлении.
   Мёртвые не регенерируют; респавн восстанавливает полный ресурс и обнуляет остаток.
3. HUD показывает `res: X/Y` из снапшота; `OwnHpText`/`OwnResourceText` используют общий `OwnEntity`.
4. Клиентский prediction не симулирует ресурс (он серверный), но при отказе `NotEnoughResource`
   сохраняет текущий кулдаун и повторит удар после регенерации.

Проверки:
- **223/223 tests**, coverage 100%. Новые: `Resource_RegeneratesFractionally...`,
  `Resource_StopsAtMax...`, `DeadEntity_DoesNotRegenerate`, `StarvedCaster_RegeneratesAndDealsDamageAgain`,
  `RepeatedCasts_DoNotStarveAndKeepDamagingAfterRespawn` (12 ударов + респавн), проверки конфига.
- `ConfigureAndVerify`: exit0, GPU 13/13, combat checks passed, `m10-10-1-configure.log`.
- Desktop/mobile: Succeeded, size **93 167 334**, errors0; `Assembly-CSharp.dll` 17:37:40 / 17:38:27;
  логи `m10-10-1-{desktop,mobile}.log`.
- Живой прогон с новым сервером: 12 ударов, `NotEnoughResource` в логе нет; хеш конфига клиента и
  сервера совпадает (`7f97118…`).
- CI `f1cd878`: [pull_request 42836716179](https://github.com/avvasiliev888/albion-prototype/actions/runs/42836716179),
  [push 42836728679](https://github.com/avvasiliev888/albion-prototype/actions/runs/42836728679) — success.

## M10.10: классическое управление мышью — 2026-11-10

M10 принят оператором; M10.10 добавляет мышь поверх прежнего управления, не меняя протокол и не
ломая WASD/Space.

1. **Движение ПКМ.** Пока ПКМ зажата, `UnityInputSource` строит луч `Camera.ScreenPointToRay(Input.mousePosition)`
   и пересекает его с плоскостью `Y=0` (`Plane.Raycast`), поэтому коллайдеры не нужны. Вектор
   `точка − позиция игрока` (XZ) нормализуется и уходит в `MoveCommand`; пересчёт каждый клиентский тик,
   поэтому персонаж плавно меняет направление вслед за курсором. Если до точки ≤ **0.5 м**, движение
   не отправляется (мёртвая зона). Позиция игрока берётся из `Prediction.Position`, камера — из
   `CameraFollow`.
2. **Атака ЛКМ.** `PollFrame` выставляет тот же `_abilityPressed`, что и Space. Далее действует
   существующий путь: `IsAbilityReady` + `IsAttackPlaying`, авто-атака удержанием, кулдаун,
   `MoveCommand` вместо атаки во время кулдауна.
3. **Совместимость.** WASD и Space не изменены. Клавиатурное и мышиное направления складываются и
   нормализуются: W+ПКМ дают диагональ, противоположные гасятся. `Ctrl` применяет шаг и к мыши.
4. **Фокус/курсор.** В `ClientRuntime` включены `Application.runInBackground`, `Cursor.lockState=None`
   и `Cursor.visible=true`, чтобы курсор не был захвачен; в HUD подсказка `WASD/ПКМ`, на иконке
   способности — `SPACE / LMB`.

Проверки:
- **216/216 tests**, coverage 100% четырёх модулей (сетевой код не менялся).
- `ConfigureAndVerify`: exit0, GPU 13/13, combat checks passed, `m10-10-configure.log`.
- Desktop/mobile builds: Succeeded, size 93 165 746, errors0; `Assembly-CSharp.dll` 16:33:45/17:06:25;
  логи `m10-10-{desktop,mobile}.log`.
- CI `dbc0c10`: [pull_request 42831157749](https://github.com/avvasiliev888/albion-prototype/actions/runs/42831157749),
  [push 42831171202](https://github.com/avvasiliev888/albion-prototype/actions/runs/42831171202) — success.
- Runtime-смоук desktop: процесс поднялся, `ClientRuntime: сессия готова`, ошибок Input/камеры нет
  (`m10-10-player-smoke3.log`); движение мышью проверяется живым клиентом.

## M10.9: реконсиляция боя — 2026-11-10

Оператор проверил M10.8.1 и нашёл три связанных дефекта; исправление в том же PR #12.

1. **Дёрганье анимации при W + зажатом Space.** Клиент мог отправить новую способность сразу после кулдауна, даже если Attack-клип ещё играл. Добавлен `UnityView.IsPlayerAttackPlaying` и предикат в `UnityInputSource`: пока удар не доиграл, `AbilityCommand` не отправляется, а флаг удержания не сбрасывается. После конца клипа зажатый Space срабатывает один раз, клип не перезапускается с первого кадра.
2. **Цикл «отказ → cd=0 → новый удар».** `ClientPrediction.Reconcile` больше не обнуляет кулдаун при серверном отказе: если серверный `CooldownUntilTick` в будущем — берётся остаток; если отказ пришёл на границе кулдауна — 1 тик; при настоящем отказе — полный `AbilityCooldownTicks`. Дополнительно `ClientPipeline` читает ввод до декремента и не отправляет команду на тике с остатком 1, чтобы не попасть в `OnCooldown` на предыдущем серверном тике; новый удар не уменьшает свой же кулдаун в том же тике. Это устраняет голодание, которое появлялось при жёстком кулдауне.
3. **Цель после respawn.** Кеша цели в клиенте нет: `ClientRuntime.ResolveAbilityTarget` каждый раз вызывает `ClientPipeline.FindNearestEntity`, который читает **актуальный `LastSnapshot`**, а не prediction. Живая цель имеет приоритет; если живых нет — ближайшая мёртвая, поэтому ID 2 не теряется в окне смерти и после respawn. `TargetId=0` — только пустой мир.
4. **Остановка на бегу при зажатом Space.** Причина: `UnityInputSource` при `_abilityPressed` всегда возвращал `AbilityCommand`, даже когда кулдаун ещё идёт. Pipeline отбрасывал её, но `MoveCommand` в этом тике уже не читался, поэтому W не двигал персонажа весь кулдаун. Теперь источник отдаёт способность только при `IsAbilityReady() && !IsAttackPlaying()`; иначе возвращает `MoveCommand`, а флаг Space сохраняется до готовности.

Проверки:
- **216/216 tests**, coverage 100% четырёх модулей. Новые/обновлённые: `Reconcile_ServerRejectionAfterLocalCooldownExpired_AppliesFullPenalty`, `Reconcile_RejectionAtCooldownBoundary_WaitsOneTick`, `Pipeline_DeadTargetMiss_DoesNotBlockRespawnedTarget` (miss по мёртвому → respawn → удар 75 HP), respawn-тест расширен до трёх ударов 75→50→25 HP.
- `ConfigureAndVerify`: exit0, GPU 13/13, combat checks passed (`m10-9-2-configure.log`).
- Оба build: Succeeded, size93164210, errors0; `Assembly-CSharp.dll` 16:04:47/16:05:35; логи `m10-9-2-{desktop,mobile}.log`.
- CI `96f6be4`: [pull_request 42821707945](https://github.com/avvasiliev888/albion-prototype/actions/runs/42821707945), [push 42821720470](https://github.com/avvasiliev888/albion-prototype/actions/runs/42821720470) — success.



## M10.7: тренировочный манекен и запрет урона по себе — 2026-11-10

- Причина бага Human-Gate: `ClientRuntime.ResolveAbilityTarget` при отсутствии других сущностей возвращал **свой** EntityId. Сервер принимал self-target, наносил 25 урона кастеру; `UnityView` видел падение HP и запускал Hit, который обрывал Attack. Урон приходил в тик нажатия, до конца замаха.
- Исправление M10.7: клиент больше не подставляет себя — self-target отклоняется сервером (`SelfTarget`), `Simulation.ApplyAbility` дополнительно не применяет урон к кастеру (defense in depth). M10.8 добавил target0 для пустого мира; M10.8.1 — приоритет живой цели и fallback на ближайшую мёртвую, чтобы id переживал respawn.
- `config/actors.json`: добавлен actor 2 — 100 HP, moveSpeed 1.0, respawn 5 s. `ServerOptions.SpawnTrainingDummy` включается только серверной точкой входа; entity 2 спавнится в x=4 (игрок — x=2), патрулирует по X в окне ±0.5 m серверными `MoveCommand` (не клиентским вводом). Позиция клампится, чтобы манекен не выходил из окна.
- Клиент рендерит манекен тем же `UnityView`/Warrior без отдельной ветки; удалённый Walk выбирается из displacement при 1 m/s. Урон, Hit, Death и respawn манекена идут обычным snapshot/delta-путём; prediction локального игрока не затронут.
- `ServerIntegrationTests`: self-target через сеть → HP кастера 100 и лог `SelfTarget`; манекен entity2 100 HP/1 m/s; игрок бьёт манекен (75 HP), сам остаётся 100; патруль в обе стороны; `ClientPipelineTests` предпочитает живую цель и возвращает мёртвую только как fallback, себя не возвращает.
- 209/209 .NET tests, line/branch/method coverage 100% всех четырёх модулей; architecture/Unity-boundaries passed. `AlbionCharacterSetup.ConfigureAndVerify` — exit0; GPU 13/13. Оба build: Succeeded, size93158770, errors0; логи `Builds/m10-7-desktop.log`, `Builds/m10-7-mobile.log`. CI на `c528b52`: [pull_request 42799511533](https://github.com/avvasiliev888/albion-prototype/actions/runs/42799511533) и [push 42799520015](https://github.com/avvasiliev888/albion-prototype/actions/runs/42799520015) — success.
- Живой клиент ещё не проверен оператором. Манекен — тренировочная сущность, без AI-атаки; таргетинг по-прежнему ближайшая живая сущность без mouse-pick. M10 не принят, M11 не начат.

## M10.6: бой, смерть и реальный шаг — 2026-11-10

- Живой Human-Gate оператора: Idle, CombatRun, разворот корпуса и FPS120+ работают. Замечания: нет Walk (всегда Run), нет Attack по Space, нет Death при HP0.
- Причина Walk: в клипах `1HWalkF` есть и уже подключён, но фиксированная gameplay-скорость4 m/s всегда выбирала Run. Добавлен **Ctrl — шаг1.5 m/s**: команда отправляется с длиной0.375, сервер и prediction двигают с той же скоростью; протокол не менялся. Walk-клип тот же, без подмены анимации.
- Attack/Hit/Death физически есть в уже скачанном пакете: `1HAttack`0.7s, `1HPain`0.5s, `1HDeathA`1.8s. Извлечены минимально, Humanoid CopyFromOther; looped только Idle/Walk/Run, combat one-shot.
- `WarriorVisual` управляет one-shot states явно: Space → Attack только при фактическом старте способности (скачок кулдауна в pipeline, отклонённый spam не анимируется); падение HP → Hit; IsDead → Death один раз с удержанием последней позы; respawn → Idle. Повторный Space перезапускает удар; Death приоритетнее. Network root по-прежнему не двигается/не вращается Animator.
- `UnityView` больше не скрывает visual при смерти — тело остаётся для Death; материалы не меняются. HP-снижение даёт Hit, смерть — Death. Мёртвый локальный игрок не скользит при зажатом движении: до респавна позиция берётся из снапшота, а не из prediction.
- `AlbionCharacterSetup.ConfigureAndVerify` final: exit0, проверены Attack/Hit/Death states и клипы, возврат в Idle/Locomotion, приоритет смерти, движение root/scale/rotation invariant, а также все прежние Idle/Walk/Run/remote regressions.
- GPU: 13/13 differential checks passed; Attack temporal difference 54197desktop/39675mobile, Death vs Idle 55255desktop/41863mobile (>100). Кадры attack-00/01, death, idle/walk/run/cycle в `Builds/visual-check`; лог `Builds/m10-combat-final.log`.
- Оба build: Succeeded, size93158630, errors0; logs `Builds/m10-combat-release-{desktop,mobile}.log`. Добавлен .NET regression: направление0.375 двигает на1.5 m/s; тесты 203/203, coverage100%; architecture/Unity boundaries passed. Attack-сигнал требует готового кулдауна до апдейта, чтобы reconciliation не перезапускал удар.
- Импортёр `1HCombatRunF` по-прежнему даёт warning (Inspector); лицензия/прочие warnings те же. Проверка реального Attack/Death и Ctrl-шага в живом клиенте остаётся за оператором; M10 не принят, M11 не начат.

## M10.5: анимация — локальные проверки 2026-11-10

- Требование оператора: модель одобрена, но без Idle/Walk/Run M10 не принимается. Продолжен открытый PR #12; M11 не начат.
- Вместо frozen mesh — live skin. Shared BaseMale reference Avatar + только 3 Humanoid looped клипа из уже скачанного Female Warrior Princess / All Star Characters; dReal Warrior сам клипов не содержит. Source/лицензия задокументированы в README; FBX в LFS.
- Controller Speed: Idle ≤0.1; Walk1.5/Run4, blend transitions0.1s. Скорости циклов откалиброваны по averageSpeed. Local Speed/facing — raw planar prediction velocity, не reconciliation/render delta; network root translation/rotation/scale Animator не изменяет.
- `AlbionCharacterSetup.ConfigureAndVerify` final — exit0 на Unity6000.3.28f1 / RX5700 / D3D12. Проверена существенная смена позы Run (leg range109.31°), полный Idle (spine2.49°) и Walk (leg72.02°), выбор правильных клипов, threshold/stop/NaN/dead, 120 animation updates без root/calibration drift, respawn с продолжающейся Walk.
- Удалённые: проверены 30/60/144FPS, spawn/freeze/zero/NaNdt, catchup/teleport и 4m correction за обычный соседний tick: animation velocity сбрасывается одинаково на всех FPS без изменения интерполированной позиции.
- GPU: 9/9 differential checks passed, Run temporal difference45146desktop/29194mobile pixels (>100). PNG Idle/Walk/Run phases/A/D/stopped и 30frames cycle обоих профилей просмотрены; ignored evidence `Builds/visual-check`, `Builds/m10-animation-verify-final.log`. GIF цикла собран из реальных GPU-кадров (root неподвижен намеренно для root-motion контроля).
- Оба актуальных Windows64 profile build: Succeeded, errors0; логи `Builds/m10-animation-build-desktop.log`, `Builds/m10-animation-build-mobile.log`. .NET202/202, coverage100%, architecture и Unity boundaries passed. Unity/GPU/build проверены локально, не GitHub CI.
- Известные warnings: CombatRun importer сообщает animation import warning (детали следует смотреть в Inspector), лицензирование/nullable/obsolete и GPU Resident Drawer compatibility; успешный occlusion bake и фактический FPS не заявляются. Нужен живой визуальный Human-Gate и замер FPS оператором.
- Локальные GraphicsSettings/AA4/URPProjectSettings и vendor demo/варианты оператора остаются вне commit. StaticCharacterMesh удалён как ненужный владелец старой frozen mesh; vendor prefab не редактировался.

## M10 Human-Gate (ожидает оператора)

**Исходный PR:** [#11](https://github.com/avvasiliev888/albion-prototype/pull/11) уже merged оператором 2026-11-09 20:19 UTC (`76e419c`), но Human-Gate визуала не пройден. Оператор разрешил отдельный repair-PR; ветка `agent/M10/graphics-repair` от актуального main. **Repair PR:** [#12](https://github.com/avvasiliev888/albion-prototype/pull/12), открыт, не merged.

1. Откройте Unity 6.3 → `unity-client` (проект уже настроен: URP, `Assets/Scenes/Arena.unity`, `Assets/Resources/Rendering`). Если сцена не открылась — откройте `Assets/Scenes/Arena`.
2. Для сцены: `Window/Rendering/Occlusion Culling` → `Bake` (данные уже есть, но перезапеките под своё железо), затем Play.
3. Запустите сервер: `dotnet run --project src/Server -c Release -- --udp 9050`.
4. В Play проверьте: арена с колоннами/камнями/кристаллами, toon-освещение с контуром и rim, туман и тёплый свет; HUD `profile: desktop | fps: N`. Рядом с игроком медленно патрулирует тренировочный Warrior (entity2, 100 HP): он должен быть виден и проигрывать Walk.
5. Убедитесь, что игрок — текстурированный Warrior: Idle без ввода, Run при WASD, **Ctrl — Walk**, Space — видимый удар; **удержание Space даёт авто-атаку, отпускание останавливает**, а зажатые W+Space не дёргают клип: пока Attack играет, новая команда не уходит и клип не перезапускается. Space по манекену наносит урон/Hit, при HP0 — Death, затем respawn (5 s); **своего HP удар не снимает**. После respawn нанесите два удара с паузой на кулдаун — оба должны пройти (75, затем 50 HP). Отойдите так, чтобы цель была вне радиуса, или добейте манекен: Space должен всё равно дать замах и **сразу показать серый кулдаун**, урона не будет; спам по промаху/мёртвому не должен обнулять кулдаун. Над игроком и манекеном видны красные HP-полоски с тёмной подложкой; внизу по центру — круглая иконка меча: золотая при готовности, серая с заполнением снизу вверх на кулдауне. A/D поворачивают корпус по движению, отпускание кнопок возвращает Idle даже при reconciliation. Root motion OFF, нет magenta. Настройка — Resources/Characters/WarriorVisual (Prefab=исходный Warrior, Controller=WarriorLocomotion, Height=1.85, Yaw=0 для Humanoid, Ground Offset=0); меню Albion/Characters/Validate Warrior Visual. Сравните общий визуал с Product Vision: окружение остаётся простым, технические проверки не означают художественного одобрения.
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
14. **Space по себе наносил урон себе** — закрыто в M10.7: клиент не подставляет себя, сервер отклоняет `SelfTarget`, симуляция не применяет урон к кастеру. M10.8.1: при отсутствии живой цели выбирается ближайшая мёртвая (легальный miss), target0 — только пустой мир; self-урон всё ещё невозможен. Приоритет: снят.
15. **Внешний персонаж подключён по указанию оператора** — Warrior из Asset Store, runtime-набор в private git/LFS; анимации — минимальные 3 Humanoid клипа из другого скачанного пакета, также LFS. Окружение всё ещё процедурное; одновременно меч+топор сохранены из исходного prefab. Дальнейшее художественное качество — Human-Gate M10. Не перепубликовывать vendor-ассеты отдельно.
16. **Occlusion bake в batchmode** создаёт `OcclusionCullingData.asset`, но Compute сообщает неуспех/пишет warning о размере. Причина не установлена; наличие файла не доказывает успешный bake. Повторить Bake в редакторе. Приоритет: низкий.
17. **GPU Resident Drawer требует D3D12/поддерживаемого железа**; на остальном — warning URP, профиль работает. Приоритет: низкий.
18. **Mobile-профиль без теней** (осознанно, ради FPS ≥ 30); если оператор захочет тени — включим и замерим. Приоритет: низкий.
19. **Не удалось снять FPS headless**: GUI-плеер в этом окружении не запускается фоновым процессом (Start-Process зависает). FPS — HUD + лог раз в 10 с, замер на Human-Gate. Приоритет: низкий.

## Next Actions (для следующей сессии)

1. Повторный Human-Gate по [repair-PR #12](https://github.com/avvasiliev888/albion-prototype/pull/12), теперь с Warrior prefab, Idle/Walk/Run, M10.7-манекеном и M10.9 attack-lock/cooldown/respawn; проверять по шагам выше. PR #11 уже merged. Не мержить и не начинать M11.
2. Проверить в живом клиенте: зажатые W+Space не дёргают Attack; удержание Space даёт авто-атаку, отпускание останавливает; после Death/respawn два удара проходят; промах/мёртвая/дальняя цель сразу даёт серый кулдаун без урона и без обнуления при спаме; HP-бары над игроком и манекеном видны при полном и нулевом HP; нижний меч золотой готов, серый и заполняется снизу вверх; **HP игрока не падает**; Ctrl-Walk, A/D-разворот, отсутствие дрейфа root, освещение/тени/туман; измерить FPS обоих профилей. Визуал M10 остаётся непринятым до ответа оператора.
3. Только после одобрения визуала — Squash and merge repair-PR оператором. При необходимости повторить occlusion Bake в редакторе.
