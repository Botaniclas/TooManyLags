# TooManyLags mob bridge — current handoff

## Active candidate

- `TooManyLags-2.0.0-performance-safe`, independent copy of verified performance
  debug E9F3132FBE092ECBA99F2373629EE42DDB5843B1E76463158EA22978DFB9F1A3.
- User: worthwhile optimizations without functionality loss. Work/commentary
  English + Caveman; final German. Hytale irrelevant. No subagents requested.
- Only new runtime changes: MobBridgeAnimation.RigPaths scans each current
  sibling set once; MobBridgeEffects pools up to eight cleared NativeFxScopes.
- Rig-path IDs, native callback order, nested scopes, cleanup/error retry, effect
  processing and session teardown preserved. No persistent hierarchy cache.
- New test-bridge-performance.ps1 compares extracted actual source bodies with
  hash-verified performance reference: 155828 assertions; managed Unity seam.
  Fixture256 siblings: GetChild32896->256, name reads65792->256. FX1000 matched
  calls:1000->1 scope/list pairs; idle/unbound0. No measured live FPS claim.
- Full verify.ps1 -Release now includes27 suites. verification.json is definitive
  status/source/DLL receipt. No source edits while verifier runs.
- Checkpoint reflection/relay-pass consolidation/scene or Animator pooling remain
  deferred: smaller or uncertain gains need more evidence/lifetime proof.
- Prior candidates, prepared root ZIPs and installed profiles unchanged. No
  installation/package/publication/push. No Git repository in candidate.

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
