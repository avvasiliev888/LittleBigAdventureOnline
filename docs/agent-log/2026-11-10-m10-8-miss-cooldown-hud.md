# M10.8 — cooldown on miss, attack-spam guard, HP bars and cooldown HUD

Date: 2026-11-10
Branch: `agent/M10/graphics-repair`
PR: #12 (open, not merged)

## Live feedback

Operator verified M10.7 dummy, damage, death and respawn. Remaining defects/requests:

1. Spamming Space restarts Attack from frame 0 before the previous swing ends.
2. Cooldown only starts when a hit lands; a whiff or an out-of-range enemy does not spend it.
3. While the dummy is dead Space does nothing; the client must still allow a swing and spend cooldown.
4. Add red HP bars above local player and dummy, always visible at full and zero HP.
5. Add a bottom-centre sword cooldown indicator, coloured when ready and filling bottom-up while cooling.

## Root causes

- Ability validation rejected `UnknownTarget`, `DeadTarget` and `OutOfRange` before simulation. The
  cooldown/resource were applied only after that validation and after target checks, so misses were free.
- `ClientRuntime.ResolveAbilityTarget` returned 0 when no living target existed; `UnityInputSource`
  treated 0 as "send nothing". A dead dummy therefore produced no command at all.
- `WarriorVisual.PlayAttack` always crossfaded into Attack. The prediction cooldown normally blocked
  spam, but any accepted activation during the 0.7 s swing could restart the clip.

## Decisions

- **Target 0 is a legal void swing.** `AbilityCommand` no longer rejects zero, and `CommandCodec`
  accepts zero. Wire format stays protocol v4: the same 13-byte ability payload; only the zero
  semantics changed. This is the smallest way to express "cast without a target".
- **Activation precedes hit resolution.** `Simulation.ApplyAbility` validates caster, cooldown and
  resource, then charges resource/cooldown, then looks for a living target in range. Missing, dead,
  unknown and out-of-range targets are legal misses; only self-target is refused. Client prediction
  already starts cooldown on send, so it agrees with the server.
- **Self-target remains impossible.** `InputValidator` still returns `SelfTarget`; the simulation also
  refuses to charge/damage a self-target command. The client target resolver never returns self.
  `UnknownTarget`/`DeadTarget`/`OutOfRange` remain enum values for log compatibility but are no longer
  emitted by the validator.
- **Client always sends Space.** `UnityInputSource` no longer drops a command when the target is 0.
  `ResolveAbilityTarget` returns nearest living entity or 0. A dead dummy, an empty world or an
  out-of-range target all produce a visible swing and a spent cooldown.
- **Visual spam guard.** `WarriorVisual.IsAttackPlaying` is true while the Attack state is entering or
  below normalized time 0.9; `PlayAttack` returns early in that window. Cooldown remains the second
  barrier.
- **HP bars without UGUI.** `HealthBar` creates two world-space quads (dark backing, red fill) above
  the entity, billboards to `Camera.main` in `LateUpdate`, and stays visible at 0 HP. It is attached in
  `CharacterVisualSettings.Create`, so local player, dummy and remote players all get one; `UnityView`
  updates it from snapshot HP. URP Lit is used because Warrior materials already guarantee the shader is
  in the player build (Unlit can be stripped).
- **Cooldown HUD without Canvas.** `CooldownHud` draws a generated circular backdrop and sword texture
  in `OnGUI`. Ready = gold; cooldown = pale grey base plus a gold copy clipped to the bottom fraction
  (`CooldownRemainingTicks / AbilityCooldownTicks`). It uses the prediction value that reconciliation
  replaces with the server's `CooldownUntilTick`. No per-frame textures or Canvas rebuilds.
- The editor regression now ignores health-bar renderers when checking Warrior materials and asserts
  that spamming `PlayAttack` does not reset the current normalized time.

## Tests

- `AbilityTests`: target 0 activates and cools down; out-of-range, dead and unknown targets deal no
  damage but spend resource/cooldown; self-target still deals no damage and spends nothing.
- `InputValidatorTests`: out-of-range/dead/unknown/zero targets are accepted as misses; self-target is
  rejected; cooldown/resource still reject.
- `CommandCodecTests`: zero-target ability round-trips.
- `ServerIntegrationTests`: unknown target is accepted, charges cooldown/resource, logs no
  `UnknownTarget`; self-target remains rejected with resource unchanged.
- `ServerAckTests`: miss is acknowledged and charges cooldown/resource.
- `ClientPipelineTests`: a target-0 command is sent, starts the predicted cooldown and leaves HP intact
  while the server spends resource.
- Result: **212/212 tests**, line/branch/method coverage **100%** for Client.Network, Server, Shared and
  Simulation.Core; architecture/Unity-boundaries passed.

## Unity/build evidence

- `Albion.Editor.AlbionCharacterSetup.ConfigureAndVerify`: exit 0, Unity 6000.3.28f1, RX 5700 /
  D3D12, no `-nographics`; GPU differential 13/13; Warrior regression including the new spam assertion
  passed. Log: `Builds/m10-8-configure2.log`.
- GPU probe detected two red health-bar components above the two entities in `desktop-full.png`
  (~50×6 px each), confirming the bars render in the actual URP frame.
- Desktop and mobile Windows64 builds: `Result: Success`, size 93,163,186, errors 0; logs
  `Builds/m10-8-desktop3.log` / `Builds/m10-8-mobile3.log`; `Assembly-CSharp.dll` timestamps
  `14:45:41` / `14:46:17`; build log lists both `HealthBar.cs` and `CooldownHud.cs`.
- The cooldown HUD is `OnGUI`, not part of the render probe; only a live client can confirm its final
  screen position/readability.
- CI on `d850480`: [pull_request 42809430278](https://github.com/avvasiliev888/albion-prototype/actions/runs/42809430278)
  and [push 42809439240](https://github.com/avvasiliev888/albion-prototype/actions/runs/42809439240) — success.

## Errors and fixes

- `AbilityCommand` zero rejection was duplicated in `CommandCodec`; both had to change, otherwise the
  client would encode a legal zero but the server would drop it.
- `InputValidator` had a now-unused `System.Numerics`/distance helper; removed after moving range
  resolution to simulation.
- `ConfigureAndVerify` regenerates `WarriorLocomotion.controller` file IDs; the committed controller was
  restored afterwards. Build also touches `ProjectSettings.asset`; restored. Operator's
  `GraphicsSettings.asset`, `QualitySettings.asset` and `URPProjectSettings.asset` remain untouched.
- A lingering `Server.exe` from the aborted GUI smoke test blocked a solution rebuild; stopped before
  the successful build.

## Limitations

- Live visual check is still the operator's: the GUI player cannot be launched reliably as a background
  process in this environment.
- The dummy still has no AI attack; remote Attack is not animated.
- Damage is still applied in the server tick of the command, not at the visual impact frame. The swing
  is no longer interrupted, and misses now cost cooldown.
- No mouse picking; Space uses nearest living entity or target 0.
- Existing importer warning `1HCombatRunF` and Unity license/nullable/obsolete/GPU Resident Drawer
  warnings remain.
