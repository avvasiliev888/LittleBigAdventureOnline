# M10.8.1 — hold-to-attack, respawn targeting and miss cooldown

Date: 2026-11-10
Branch: `agent/M10/graphics-repair`
PR: #12 (open, not merged)

## Live feedback

After M10.8 the operator reported three combat bugs:

1. Holding Space attacks once; it should auto-repeat while held and cooldown is zero.
2. After the dummy dies and respawns, the first hit works but following hits deal no damage
   (“client cached the dead id”).
3. Hitting a dead target or the void shows no cooldown and allows infinite Space spam.

## Root causes

- `UnityInputSource.PollFrame` used `Input.GetKeyDown`; a held key produces a single down edge.
- `ClientPipeline.FindNearestEntity` skipped every dead entity and returned 0. Between death and the
  client observing the respawn snapshot (or while `IsDead` is stale), the resolver fell back to the
  void instead of the dummy id. The id itself is not cached; the dead filter was the unstable part.
- Prediction already starts the cooldown on send, but the targetless path was only covered before the
  first reconciliation. Added a regression that waits for the server Ack/snapshot: the miss cooldown
  must remain active. The server accepts the miss; it is not a rejection.

## Fixes

- **Hold-to-attack**: `PollFrame` reads `Input.GetKey(KeyCode.Space)` and latches it until the next
  client tick. `ClientPipeline` still gates sending on `ClientPrediction.TryStartAbility`, so holding
  produces one command per cooldown, not one per tick. A short tap remains latched across frames.
- **Stable target fallback**: `FindNearestEntity` returns the nearest living entity if one exists,
  otherwise the nearest dead entity. `TargetId = 0` is now only for an empty world. A dead target is a
  legal miss: the server charges resource/cooldown and deals no damage. After respawn the same id 2 is
  returned again, even if the client's `IsDead` snapshot is one frame behind.
- **No cooldown change needed in the happy path**: `ClientPrediction.TryStartAbility` sets 40 ticks at
  send time, before target resolution and before the server answer. `CooldownHud` reads that value.
  The added test proves it survives Ack/reconciliation for a targetless miss.
- Self-target is still rejected; server damage to caster remains impossible.

## Tests

- `Pipeline_FindNearestEntity_PrefersLiving_AndFallsBackToDead`: no other entity → 0; living → 2;
  dead only → 2 (not 0).
- `Pipeline_TargetsRespawnedDummy_Again_AfterSnapshotRefresh`: kill dummy, wait for respawn, first
  hit 75 HP, next hit after cooldown 50 HP, client snapshot contains alive dummy id 2.
- `Pipeline_TargetlessMiss_StartsCooldown_AndDealsNoDamage`: after two more ticks (Ack + snapshot),
  `CooldownRemainingTicks > 0`.
- Result: **213/213 tests**, coverage **100%** line/branch/method for Client.Network, Server, Shared and
  Simulation.Core; architecture/Unity-boundaries passed.

## Unity/build evidence

- `Albion.Editor.AlbionCharacterSetup.ConfigureAndVerify`: exit 0, Unity 6000.3.28f1, RX 5700 /
  D3D12, GPU differential 13/13, Warrior combat checks passed. Log `Builds/m10-8-1-configure.log`.
- Clients were rebuilt from the updated plugins/config; desktop and mobile results recorded below.
- CI on `d33cf64`: [pull_request 42814723909](https://github.com/avvasiliev888/albion-prototype/actions/runs/42814723909)
  and [push 42814738483](https://github.com/avvasiliev888/albion-prototype/actions/runs/42814738483) — success.

## Errors and fixes

- First version of the respawn regression asserted target 0 immediately after forcing death; it failed
  because the client had not yet received the death snapshot. The test now advances two ticks, which
  reflects the real network/render lag rather than changing production behavior.
- `ConfigureAndVerify` again regenerated `WarriorLocomotion.controller` and `ProjectSettings.asset`;
  both were restored before commit. Operator Graphics/Quality/URP/vendor files remain untouched.

## Limitations

- Unity `Input.GetKey` auto attack is not covered by xUnit (Unity assembly); the editor combat check
  and the live client verify it.
- The dead fallback means a dead nearby entity can be selected over a live one only when **no** living
  entity exists; if a living entity exists, it wins.
- Cooldown rollback on a real server rejection (cooldown/resource/self) is still intentional.
- Live FPS/HUD readability and the full Human-Gate remain operator-side.
