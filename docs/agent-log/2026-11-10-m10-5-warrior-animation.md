# M10.5 — Warrior locomotion (PR #12)

## 07:40–08:25 UTC — M10.5 animation integration

**Did:**
- По требованию оператора продолжен PR #12 / agent/M10/graphics-repair: одобренную модель сохранено, статическая T-pose заменена live Humanoid Idle/Walk/Run. M10 визуально не закрыт, M11 не начат.
- Проверены скачанные Unity packages. dReal Warrior.fbx содержит valid Human Avatar, но не animation clips. Из Female Warrior Princess (All Star Characters) извлечены только BaseMale reference Avatar и 1HIdle/1HWalkF/1HCombatRunF + original metas. Полный пакет не импортировался, источники Asset Store и ограничение приватного распространения описаны в README.
- Animator enabled, ApplyRootMotion=false, AlwaysAnimate, keepAnimatorStateOnDisable. Controller Speed: Idle ≤0.1 / Locomotion >0.1; Walk/Run blend thresholds1.5/4, transitions0.1sec. Clip cycle rates откалиброваны по averageSpeed. Нет gameplay sprint/скоростных изменений.
- Локальный Speed/facing берутся из planar prediction velocity, не reconciliation displacement. Удалённые используют render interpolation displacement/dt с guard для discontinuity/catchup/spawn/respawn/sourcechange/invaliddt. Network root не вращается/не масштабируется Animator.
- Calibration scale/grounding вынесены вне Animator hierarchy; SMR enabled, старый static mesh owner удалён. Runtime hand sockets привязаны к Humanoid hands с сохранением исходной rest-pose посадки; vendor prefab не изменён.
- Добавлены regression checks clip selection/full-cycle Idle/Walk/Run, threshold/NaN/dead/stop/correction/A-D, root-motion/calibration invariants, respawn recovery. Remote tested30/60/144FPS, включая ordinary-tick4m correction без изменения позиции.
- GPU probe сохраняет Idle/Walk/Run/A-D/stopped +30frames cycle для обоих профилей; GIF из реальных кадров доступен локально. Reviewed representative GPU phases/facing, not a claim of artistic acceptance.

**Decisions:**
- CopyFromOther с общим BaseMale reference-pose Avatar, а не CreateFromThisModel на отдельных animation FBX: при создании Avatar из первого motion pose ноги/руки ретаргетились неверно. Исправление дало Run lower-leg angular range109.31° вместо визуально слабой позы.
- Humanoid forward +Z требует Yaw0 (старый frozen rest mesh требовал180). Направления проверены реальными кадрами left/right.
- Скорость remote discontinuity ограничивается только в анимации, 2m/networktick=40m/s, независимо от FPS; не clamp gameplay/root position. Найдено и исправлено при независимом read-only review; добавлен regression на consecutive ticks.
- Fixed read-only delegation нельзя расширить follow-up: запущен отдельный write-scoped test child. Его изменения прочитаны и проверены parent в Unity.

**Auto-Gates:**
- Unity6000.3.28f1 / Radeon RX5700 / D3D12: ConfigureAndVerify final exit0.
- Idle spine full-cycle2.49°, Walk leg72.02°, Run stride109.31°; clip selection and respawn animation pass. Network root, imported transform/calibration fixed through explicit animation updates.
- GPU9/9 differential tests passed; temporal Run45146desktop /29194mobile pixels (minimum100), no magenta. Evidence: unity-client/Builds/visual-check and Builds/m10-animation-verify-final.log (ignored).
- Final desktop/mobile Windows64 builds succeeded, errors0, logs Builds/m10-animation-build-desktop.log / m10-animation-build-mobile.log (ignored).
- .NET202/202; line/branch/method100% across all four modules. Architecture and Unity-boundaries gates passed. CI for `6c5a452`: [pull_request 42783007036](https://github.com/avvasiliev888/albion-prototype/actions/runs/42783007036) and [push 42783017809](https://github.com/avvasiliev888/albion-prototype/actions/runs/42783017809) both success.

**Errors / warnings:**
- Initial Unity guard falsely caught Unity auth broker (not Editor); inspected command line, narrowed guard to exact Editor executable. No user editor killed. Guard-aborted invocation did not run Unity and is not an implementation failure.
- Existing importer warning for CombatRun remains (Inspector Import Messages needed); no warning-free claim. Known license/nullable/obsolete/GPU Resident Drawer warnings also remain. Occlusion Compute success/FPS not claimed.
- A diagnostic-only import-warning reflection experiment did not expose details and was removed; no reflection diagnostic code kept.
- Web search helper returned HTTP402 while looking up warning API, not needed for completion; no endpoint/settings changed.
- Both profile builds wrote empty Standalone scripting defines; restore only this own side effect before commit. Operator GraphicsSettings linear/color temperature, QualitySettingsAA4, URP settings and vendor demo/variants remain untouched/unstaged.

**Blocked:**
- No implementation blocker. Human-Gate/FPS requires operator; no merge permitted.

**Next:**
- Stage only task code/controller/source FBX+metas/docs, push PR12, verify new CI and append exact result.
- Operator: Play/server or rebuilt player, verify Idle→Run→Idle, A/D facing, no reconciliation walking in place, death/respawn, root motion OFF; visual/FPS review both profiles. Do not start M11.

## 09:05–09:45 UTC — M10.6 combat/death/walk

**Did:**
- Operator live-tested M10.5: Idle, CombatRun, facing and FPS120+ good; Walk never appeared, no Attack on Space, no Death at HP0. PR #12 kept open, M11 not started.
- Inventoried the already-downloaded All Star package. It contains `1HWalkF` (already wired), `1HAttack`0.7s, `1HPain`0.5s, `1HDeathA/B/C`1.8s. Extracted minimal `1HAttack`, `1HPain`, `1HDeathA` + vendor metas; Humanoid CopyFromOther; one-shots loop off.
- Controller now has Attack/Hit/Dead states with the same shared BaseMale Avatar. `WarriorVisual` drives one-shots explicitly by crossfade: Space → Attack only when ability actually starts, HP decrease → Hit, IsDead → Death once holding last pose, respawn → Idle; new Attack restarts a swing; Death interrupts.
- `UnityView` no longer deactivates the visual on death; it keeps the body for the Death state. HP-drop detection is per entity and suppressed on lethal/initial snapshots. A dead local player uses snapshot position instead of prediction, so held movement input cannot slide the held Death pose.
- Walk: the clip was always present, but at fixed gameplay speed4 m/s the 1D blend selects Run. Added **Ctrl walk at1.5 m/s** by sending direction magnitude0.375. Server already accepts analog magnitude and multiplies it by moveSpeed, and local prediction uses the same vector, so movement and animation agree without protocol changes. HUD mentions Ctrl.
- Added Unity regressions for Attack/Hit/Death selection, interruption, return to Idle/Locomotion, death priority, respawn and unchanged network root; added .NET regression that0.375 direction moves1.5 m/s. GPU probe now captures attack-00/01 and death.
- Updated README/state with combat controls, clip list and limitations.

**Decisions:**
- Walk needed a real slow command, not a fake slow animation. Command magnitude keeps server authority and prediction in sync; no wire/contract change. If the operator prefers Shift-sprint/default-walk instead, only input policy changes (documented as open choice).
- One-shots are presentation-driven rather than relying on Animator trigger/AnyState transitions. In batch, triggers were set (`GetBool` true) but AnyState and explicit trigger transitions did not fire, while `Animator.Play`/`CrossFade` to the same state worked. Driving state explicitly removed this fragility; the controller still holds states/motions and the blend tree.
- `1HDeathA` (fall) chosen; DeathB/C/Pain not additionally imported beyond Hit. No weapon switching/environment scope expansion.
- Death still holds the last pose until the server snapshot respawns the entity; no client-side timer hides it (server authority preserved).

**Auto-Gates:**
- `AlbionCharacterSetup.Configure` and combat regression: exit0. Attack/Hit/Death states/dominant clips pass; Attack interrupts locomotion and resumes; root/scale unchanged; Death has priority; respawn Idle.
- Full `ConfigureAndVerify`: exit0; GPU13/13 comparisons passed. Attack difference54197desktop/39675mobile; Death-vs-Idle55255/41863 (>100). Evidence `Builds/m10-combat-final.log`, `Builds/visual-check`.
- Both profile builds: Succeeded, size93158630, errors0. Logs `Builds/m10-combat-release-{desktop,mobile}.log`.
- .NET203/203 (new walk-magnitude regression), coverage100%, architecture/boundaries passed after test addition. Local `Configure` additionally exercises the UnityView HP-drop path (`RemoteSnapshot` HP75 → Hit/1HPain). Attack trigger requires cooldown readiness before the update plus the jump, so a reconciliation correction mid-cooldown cannot restart the swing.
- Commit `d0cd20c` pushed to PR #12; GitHub CI runs [42791563392](https://github.com/avvasiliev888/albion-prototype/actions/runs/42791563392) and [42791571314](https://github.com/avvasiliev888/albion-prototype/actions/runs/42791571314) both success. Final commits `a4b5902` (dead-local freeze) and docs: CI [42792779130](https://github.com/avvasiliev888/albion-prototype/actions/runs/42792779130), [42792788695](https://github.com/avvasiliev888/albion-prototype/actions/runs/42792788695) success; post-freeze `Configure` exit0.

**Errors / warnings:**
- Extracted FBXs first landed under workspace-root `Assets/` instead of `unity-client/Assets/`; moved the exact files into the Unity project, no duplicates left.
- Animator trigger/AnyState transitions did not transition in batch even with trigger true; direct Play proved states existed. Replaced with explicit state-crossfade driver, then verified GPU.
- `1HCombatRunF` importer warning and existing Unity/license/GPU warnings remain; no warning-free claim.
- Build side effect rewrote empty Standalone scripting defines; restore only that before commit.

**Next:**
- Operator: live check Ctrl-Walk, Space attack (including spam), damage Hit, HP0 Death and respawn; then M10 visual/FPS verdict. No merge/M11.
