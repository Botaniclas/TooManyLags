# TooManyLags mob bridge — current handoff

## Active candidate

- TooManyLags-2.0.0-short-buffer, from verified performance-safe debug
  B13E7723C3A1A47B692EE6662BA59E371BBE6A15F58004E713C8C21A4BA37B2D.
- User: low-latency position movement appears almost stopped; animation runs
  independently. Work English/Caveman; final German; Hytale irrelevant.
- Completed fixedTime body clocks now publish in existing LateUpdate Maintain
  for active handoffs, independently of AI callbacks. AI tick/dt remain separate.
  Cadence keeps periodic50ms phase; bounded native-step tail near checkpoint
  timing hint; same layout. All endpoints should use this candidate.
- Frame/observer accepts same AI tick only for strictly newer body/sample time
  with unchanged AI dt. Matching tests actual received displayed horizon before
  spending another fake frame. Frozen barriers retain their old gate.
- Buffer planner accounts for next completed native endpoint, initial route,
  actual short notification spacing. Feasible deadlines use continuous constant
  braking; original critically damped fallback/wait remains when late/infeasible.
  No animation/AI/native body/capture-rate changes; base0.5x, sealed history.
- Modeled20ms physics/50ms AI/50ms route at8ms frames: minimum old0,new0.334x.
  Timing/observer/idle/native and extracted actual publication checks included.
  Detailed assumptions/limits in VALIDATION.md. Latest Player.log only startup/
  menu, no transition evidence. Live matched-endpoint Wolf retest remains open.
- Full verify.ps1 -Release:27 suites; verification.json definitive result/hashes.
  No source changes while verifier runs. Earlier builds/ZIPs/profiles untouched.
  No install/package/publish/push. No Git repository. Prior handoff archived at
  history/HANDOFF-before-short-buffer.md; older history there is preserved too.
## Current simulation/presentation invariants

- Native shadow AI/body/physics continues source recording; sealed samples play
  continuously at0.5x base. Only unplayed finite buffer can slow when updates lag;
  wait if insufficient. Do not replay or edit already recorded history.
- Matching uses accepted observed destination progress, never future extrapolated
  state. Monotonic epochs/time. Never accelerate a fake already ahead to catch real.
- Final correction base is half actual playback, with geometric extension and
  direction protection; no forced expiry before alignment. No arbitrary distance
  or rotation caps. No new timing changes in this candidate.
- Source presentation unchanged for observer support. Observers share recordings
  through area/capability/epoch-gated raw relay; server validates all wire fields.
- Production new owner is target; account for source/target/server test topology.
  Target/RNG matching applies only to the handoff mob/window, not global gameplay.
- Fake knockback intentionally omitted. Visual attacks may play, never produce
  authoritative duplicate damage/events. Hits route once to current owner.
- Native locomotion full-weight fast path skips unused history interpolation but
  preserves late Apply/Restore snapshots, IK, equipment, hitboxes, HUD and FX.
- Visual model/proxy/attached FX use presented pose; physical/network root remains
  authoritative. FX objects/particles/audio/timers/network identity are not cloned.
- Prior sub50ms fixes retained: no duplicate zero-progress clock burst, packet
  cadence filter avoids startup gaps, planner counts current display frame;
  sub20ms recording primes existing partial body endpoint without future AI;
  clean final correction has no additional20ms floor.
- Late finite-buffer updates can still require waiting. Live rendering/physics/
  transport/performance not established by headless/differential fixtures.

## Validation and artifact paths

- Build: .\build.ps1 defaults debug; .\build.ps1 -Release explicit release.
- Full: .\verify.ps1 -Release; Windows File.Replace fixture needs unsandboxed
  execution as previously verified. Redirect verbose output to bin log.
- Debug DLL: bin/debug-system/TooManyLags.dll (ai_test).
- Release DLL: bin/TooManyLags.dll. Use same candidate on both endpoints.
- Tests: new test-bridge-performance.ps1, existing animation passthrough, native
  presentation/weapon/state/observer/timing and debug-idle audits.
- Detailed evidence: VALIDATION.md; version/user text: CHANGELOG.md.
- Prior full history preserved in history/HANDOFF-before-safe-performance.md.
- Chat report: C:/Users/Niclas/Documents/Codex/2026-10-26/toomanylags-mobhandoff-next/
  work/performance-opportunities-2.0.md.

## Next live checks

Wolf at ai_test0/20/50/100; Troll duplicate-name rig/equipment; swim/flight,
continuous attached FX and particle alignment. Compare prior performance DLL.
Keep actual samples/physics/animation precision, bandwidth and idle behavior.
