# M10.9/M10.9.2 — attack lock, movement, hard cooldown and respawn targeting

Date: 2026-11-10
Branch: `agent/M10/graphics-repair`
PR: #12 (open, not merged)

## Live feedback

After M10.8.1 the operator reported three network/reconciliation bugs:

1. Holding W + Space makes the attack animation stutter: a new swing starts before the previous
   one finishes.
2. Hitting the void or a dead dummy lets Space be spammed forever; the client rolls the cooldown
   back to 0 after a server rejection.
3. After the dummy respawns, the first hit lands and the following ones do not; the operator
   suspected the client cached the target id.

## Root causes and fixes

### 1. Attack animation restart

`UnityInputSource` only checked the cooldown, not the visual state. A command could be sent as soon
as the prediction cooldown reached 0 even if the Attack clip was still playing; `WarriorVisual`
guarded the clip, but the command was already leaving the client.

- Added `UnityView.IsPlayerAttackPlaying`.
- `UnityInputSource` accepts an `isAttackPlaying` predicate. While it returns true,
  `TryReadCommand` does not consume the ability flag and does not send `AbilityCommand`.
- The held flag survives the animation, so auto-attack fires once after the clip ends. It cannot
  restart the clip from frame 0.
- The pipeline cooldown remains a second gate.

### 2. Rejection → cooldown 0 → spam (reconciliation loop)

`ClientPrediction.Reconcile` treated a server snapshot with `CooldownUntilTick == 0` as truth and
set the local cooldown to 0. If the server rejected the command (self-target, resource, rate limit,
or a one-tick-early `OnCooldown`), the next tick sent the ability again.

- A server rejection no longer zeroes the cooldown:
  - if server `CooldownUntilTick` is in the future, use that remaining value;
  - if the rejection is exactly on the cooldown boundary (`snapshot.Tick <= CooldownUntilTick + 1`),
    wait one tick instead of a full penalty;
  - if there is no server cooldown at all, apply the full `AbilityCooldownTicks` penalty.
- `ClientPipeline` reads input before decrementing the cooldown and refuses an ability on a tick
  with 1 tick left. The command is therefore sent on the next tick, when the server tick has
  already crossed `CooldownUntilTick`. This avoids the one-tick-early rejection that, combined with
  the hard cooldown, previously starved the client.
- A newly started ability does not decrement its own fresh cooldown in the same tick.
- The rollback-on-real-rejection rule is preserved in the sense that the player cannot spam, but
  the cooldown is never reset to 0. A legitimate server acceptance with a future cooldown still
  becomes the authoritative value.

### 4. Movement freeze while W + held Space (M10.9.2)

`UnityInputSource` checked `_abilityPressed` first and returned `AbilityCommand` on every tick,
even while the cooldown was still running. The pipeline dropped that command locally, but by then
the input had already consumed the single command slot, so no `MoveCommand` was produced for the
whole cooldown. The warrior visually stopped while W was held.

- Added an `isAbilityReady` predicate (wired to `_pipeline.Prediction.IsAbilityReady`).
- The ability branch now requires `!IsAttackPlaying() && IsAbilityReady()`.
- When the ability is on cooldown or the Attack clip is playing, the method falls through to
  WASD movement and returns `MoveCommand`; the held-Space flag remains set.
- One ability tick still has no movement command (the protocol sends one command per tick), but
  the character no longer freezes for the cooldown.

### 5. Target after respawn

There is no target cache in the client. `ClientRuntime.ResolveAbilityTarget` calls
`ClientPipeline.FindNearestEntity` on every command; that method reads `LastSnapshot`, which is
replaced on every full/delta snapshot. The previous dead-entity filter was the real problem: while
the dummy was dead the resolver returned 0, and the prediction/reconciliation loop could keep the
client from attacking again after respawn.

- The resolver now prefers the nearest living entity and falls back to the nearest dead entity.
  A dead target is a legal miss with cooldown and no damage.
- The id 2 remains selected during the death window and after respawn, so the client does not
  depend on the exact frame when `IsDead` flips in its snapshot.
- `TargetId = 0` remains only for an empty world.
- Added regression: a miss against a dead dummy charges the server cooldown, the dummy respawns,
  and the next attack deals damage again. The main respawn regression now checks three hits
  (75 → 50 → 25 HP), so the target is not “one-shot after respawn”.

## Tests

- `Reconcile_ServerRejectionAfterLocalCooldownExpired_AppliesFullPenalty`.
- `Reconcile_RejectionAtCooldownBoundary_WaitsOneTick`.
- `Pipeline_DeadTargetMiss_DoesNotBlockRespawnedTarget`.
- Existing `TargetsRespawnedDummy_Again_AfterSnapshotRefresh` and targetless-miss regression still
  pass.
- **216/216 tests**, coverage **100%** line/branch/method for Client.Network, Server, Shared and
  Simulation.Core; architecture/Unity-boundaries passed.

## Unity/build evidence

- `Albion.Editor.AlbionCharacterSetup.ConfigureAndVerify`: exit 0, Unity 6000.3.28f1, RX 5700 /
  D3D12, GPU differential 13/13, Warrior combat checks passed. Log `Builds/m10-9-2-configure.log`.
- Desktop build: Succeeded, size 93 164 210, errors 0; `Assembly-CSharp.dll` 16:04:47.
- Mobile build: Succeeded, size 93 164 210, errors 0; `Assembly-CSharp.dll` 16:05:35.
- Logs `Builds/m10-9-2-desktop.log`, `Builds/m10-9-2-mobile.log`.
- CI `96f6be4`: [pull_request 42821707945](https://github.com/avvasiliev888/albion-prototype/actions/runs/42821707945)
  and [push 42821720470](https://github.com/avvasiliev888/albion-prototype/actions/runs/42821720470) — success.

## Limitations

- The Unity `Input.GetKey` / `IsAttackPlaying` path is not covered by xUnit (Unity assembly); the
  editor combat check and the live client remain the verification.
- Cooldown is still client-predicted; server acceptance with a future cooldown is authoritative.
  A rejection is deliberately penalized, not rolled back to free.
- The dead target fallback only selects a dead entity when no living one exists.
- Movement uses one command per tick: on the exact tick the ability starts, the movement command
  is not sent; the next tick resumes. The character does not freeze for the cooldown.
- HUD (`OnGUI`) is outside the GPU probe; its readability is a live check.
- No merge; Human-Gate and FPS remain operator-side.
