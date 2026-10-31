# TooManyLags mob bridge — current handoff

## Exact user-supplied test build, 2026-10-31

Required test SHA256:
56128F3962930854925E76EF475B549A37FE8A1A47EF43D5E15493DB80632DB6.
Confirmed identical installed client DLL and original half-speed build-mode.json.
Fresh snapshot debug build has a different PE hash, so byte hashes alone cannot
establish executable equivalence after this legacy compiler rebuild. The added
ReleaseBuildEquivalence tool compared 6,022 assembly/reference/type/field/method
entries, including IL, constants, attributes and handlers: all matched. PE/MVID
and compiler GUID spelling are ignored; initializer byte data remains compared.
No runtime source changes. User-reference DLL is pinned only under bin; it is
not installed, published or substituted after verification. Release variant uses
the same source with diagnostics/test symbols excluded by the normal mode audit.

Release verifier scope omitted existing MobBridgeWarmup.cs; that reviewed file is
now included. This is a validation-script correction, not a runtime edit. Source
package additionally includes test-bridge-continuity.ps1 and release-input.json.
Fresh full verification receipt remains definitive for the release/debug hashes.

## Release snapshot, 2026-10-31

Independent preparation folder TooManyLags-2.0.0-release-2026-10-31 copies all
140 runtime sources from current TooManyLags-2.0.0-half-speed exactly. The old
14:57 full receipt predates seven changed files; it is not reused as proof for
this snapshot. release-input.json stores the exact current source hashes.
No gameplay edits, active test-folder builds, installed profile changes,
publication or Git push are part of this task. README, development instructions
and changelog describe the current final behaviour; historical .86 test text
is removed from the new test-package README, not from archived releases.

Fresh verify.ps1 -Release will determine both DLL hashes and all suite results.
Headless/native contract coverage cannot prove zero pauses or live Unity
rendering/physics/transport behaviour. Earlier Wolf investigation describes a
prior candidate; current owner-gain/continuity tests must be considered separately.

## Active candidate

- TooManyLags-2.0.0-half-speed. Current request: remove the brief stop when
  retirement smoothing hands presentation back to a server-owned replica.
- Root cause found in checkpoint application: equal replicated pose returned
  before restoring source body velocity, so the first new-owner clock exposed a
  stale/zero tangent. `ApplySourcePose` now applies rigidbody pose and velocity
  independently; checkpoint v5 source-pose/velocity roundtrip and old-version
  rejection are covered by tests.
- Smoothing base now includes an owner-ready floor: render frame, sender/local
  frame, observed destination interval and native physics-step duration.
  Geometric residual may expand to `2 * simulation horizon`, hard-capped at
  `500 ms`; small residual remains at the normal horizon. Target root/velocity
  stay live every frame while the stored relative correction decays.
- Server-to-player low-latency pause found after user retest. Checkpoint pose
  was applied before native `ZSyncTransform.OwnerSync`, which could overwrite
  the root/velocity from delayed ZDO state. Arm checkpoint pose when
  `m_wasOwner=false`; reapply once in the post-`OwnerSync` finalizer before
  visual rebase. This is the ordinary path when no fake track remains.
- Bridge target gate removed for every non-initial owner revision. Test and
  normal owner adjustments now bridge any live Character target, including when
  target != destination. Observer readiness no longer consults ownership target
  policy. Initial revision-zero ownership remains excluded; ownership-transfer
  policy itself is unchanged.
- Server-to-player forward snap: local root/body still receive the authoritative
  checkpoint immediately, but the visual subtree starts at the pre-owner replica
  pose and decays that world-space offset to zero. This owner-gain presentation
  is independent of fake-track playback and is capped at 500 ms.
- Prior candidate: TooManyLags-2.0.0-debug-logging, from verified reserve-window sources.
  User requests diagnostic messages ON in debug, error/warning-only release.
  Debug build adds TML_BRIDGE_DIAGNOSTICS; no runtime source changes. AI-test,
  bridge summary/cost/draw and client relay logging independent of EnableLogs.
  Inactive test guards retained. Active diagnostic instrumentation has costs.
  Build-policy/idle audits positively require debug logs; release audit still
  rejects routine diagnostics. Full verification receipt is definitive.
- Prior reserve-window was from verified short-buffer source receipt
  debug197D15C05DAC232F0A184644444207C12068BCFF1774DFE691F8DDACEF38DF1D.
- User: improve arrival expectation with time until first simulation step can
  publish;20% extra recording; always observed same-horizon join. Reserve use
  alone keeps0.5x. Smooth braking only if full remaining capacity is insufficient.
- MobBridgeTiming uses cached physics/frame/first-positive timing + RTT, finds
  expected actual matching point, records1.2*max(half matching wall time,one motor
  step at cached rate). Distinct route, first arrival, record duration and horizon.
- Existing0.5s rate RPC adds4 floats,32-byte body. Local/cache-hit reuse verified;
  no additional per-mob polling/timer/AI hook. Readiness TTL2s, native fallback.
- Checkpoint v4/capability v5; observer v2. New timing header/scopes cover reserved
  horizon on source/owner/observer. Updated endpoints together; old caps native.
- Timeline budgets matching deadline including receipt phase once, not twice;
  real matching data disables needless braking. Handoff follows current rendered
  frame advancement; no previous-frame pose-repeat shortcut. No forced reserve
  playback. Late matching advances displayed horizon and can prolong correction.
- All earlier rig/FX/idle optimizations retained. Animation/body/controller code
  unchanged. A full source motor interval avoids sub-step-only controller omission.
  Target lead still uses real route, never inflated recording duration.
- New test-bridge-timing.ps1: model budgets/cache/cadence/CPU/physics/frame/delay;
  actual checkpoint header and extracted cache/RPC methods. Native wiring audits.
  Full verify.ps1 -Release now28 suites; verification.json definitive status/hashes.
- Earlier publication alternative audited only, not implemented in this change.
  Known loops remain Unity main-thread. No live zero-pause/FPS claim.
- Earlier sources/ZIPs/profiles untouched. No install/package/publish/push.
  History preserved in history/HANDOFF-before-reserve-window.md.
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


