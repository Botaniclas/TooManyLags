## Release-only verification maintenance

The current test-stop paths call MobBridgeProtocol.Cancel; the older standalone
combat-policy fixture lacked this dependency and failed to compile in debug.
The disposable fixture now models the existing cancellation boundary, and the
solo test asserts presentation cancellation without resetting ownership. Focused
actual-source combat suite passes 2,200 policy/runtime assertions and 226 native
IL contracts. No production source was edited. The fresh complete verifier run
follows this fixture repair; prior partial-run results are not substituted for it.

Source packages include both continuity test script and release provenance JSONs.
Prepared output ZIP collisions are rejected before any staging/output writes.
Earlier prepared 2.0.0 archives will be preserved before replacing current aliases.
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
# TooManyLags 2.0.0 release preparation

## 2.0.0 release candidate (2026-10-29)

### Source cutoff velocity at ownership gain (2026-10-31, current)

Independent candidate `TooManyLags-2.0.0-half-speed` keeps the finite
half-speed bridge and source-pose checkpoint. Retired-source smoothing and the
new owner now start from the same position, rotation and velocity.

Final smoothing now uses two independent timing bounds. The normal duration is
still the fake simulation horizon. A measured owner-ready floor covers the next
native `PhysicsStep / OwnerRate`, observed destination update interval, sender
and receiver frame spacing. A large position/rotation residual may expand the
blend to twice that horizon; the hard limit is `500 ms`. A sub-frame result
still receives one render frame. Small residual keeps the normal horizon and is
not slowed merely because the larger cap exists.

The target is not frozen when smoothing begins. Every frame uses the current
native root and authoritative velocity, while the stored world-space correction
and rotation offset decay relative to that root. Target changes therefore remain
continuous; the initial residual determines the time budget, not repeatedly
restarting the blend from each packet.

Low-latency server-to-player retest exposed a separate owner-gain ordering
fault. The checkpoint source pose was applied when ownership metadata arrived,
but native `ZSyncTransform.OwnerSync` could subsequently overwrite that root
and body velocity from the delayed ZDO before the first local simulation step.
That produced the observed short pause followed by a small forward correction.

The bridge now arms the checkpoint pose only when native `m_wasOwner` is still
false. Its finalizer restores the source position, rotation and velocity
immediately after `OwnerSync`, before presentation rebase code reads the root.
The immediate checkpoint application and initial owner clock remain unchanged;
the post-sync repeat is one-shot, bounded, pruned and scoped to the owning
endpoint. This path also runs when no visual fake track exists, which is the
ordinary server-to-player case.

Server-to-player still showed a small forward jump because the authoritative
source cutoff is newer than the visible replica. Root/body correction must stay
immediate for local simulation authority. A separate one-shot visual offset now
starts at the pre-owner model pose, follows the moving native root and decays to
zero with a monotone smoothstep. Duration is the larger of owner-ready/frame
time and a movement/turn estimate, capped at `500 ms`. AI, physics, hits and
native simulation continue on the corrected local root throughout. The initial
offset is applied synchronously at owner gain, before the first camera callback,
so a frame ordering change cannot expose one raw authoritative jump frame.

Bridge eligibility is now separated from ownership policy. Initial native owner
creation (`OwnerRevision == 0`) is not bridged. Every later owner adjustment may
run the source recording, checkpoint, fake simulation and observer relay
regardless of whether the AI target is a player, an NPC Character or currently
different from the destination owner. Normal ownership-transfer decisions still
retain their existing player-target policy; the bridge no longer adds a second
player-only veto after a transfer has happened. Observer readiness no longer
calls `TargetAllowsOwnership`.

Root cause of the short stop at smoothing-to-server release: checkpoint origin,
rotation and velocity were applied together behind one pose-equality return.
When the destination replica had already reached the source transform, the
method returned before restoring body velocity. The first new-owner clock then
captured the stale/zero body tangent, so final smoothing approached a stopped
root and native movement resumed only after another owner simulation step.

`ApplySourcePose` now restores checkpoint rigidbody position, rotation and
linear velocity independently from the root-transform equality check. The rules
are unchanged: one owner-gain application, no periodic correction, no extra
network traffic and no body write while the actor is a non-owner after gain.
Checkpoint version 5 carries source position/rotation/velocity; versions 1-4 are
rejected before state decode.

Fixtures assert the source pose/rotation/velocity round trip, old checkpoint
rejection, velocity assignment before the transform early-return path, the
owner-ready floor, two-horizon geometric expansion, `500 ms` hard cap, one-frame
minimum, post-`OwnerSync` pose reapplication ordering and the existing 20%
recording reserve. Native audit rejects player type checks in non-initial bridge
eligibility, fake simulation and observer readiness. `test-bridge-timing.ps1`
reports 40,955 checks; debug/release mob-bridge and observer suites include the actual emitted IL and
Valheim Mono Harmony contracts. Combat suites retain 2,199 debug and 1,978
normal-build assertions. `verification.json` is definitive for the final build
hashes and complete suite result. Live multiplayer retest remains required; this
is not a zero-pause claim for unrelated network stalls.

### Debug diagnostics restored (2026-10-30, current)

Independent copy `TooManyLags-2.0.0-debug-logging` retains reserve-window runtime
sources unchanged. Only the debug compiler defines add TML_BRIDGE_DIAGNOSTICS.
AI-test start/progress/completion/wait messages, bridge summaries, draw/capture
measurements and client-to-debug-server diagnostic relay are restored regardless
of EnableLogs. Active diagnostics intentionally have measurement/output costs.
Inactive AI/latency polls retain their first-operation return guards. No gameplay,
timing, simulation, animation, packet cadence or reserve policy changes.

Release compiler defines remain TML_RELEASE;TML_MOB_BRIDGE. Its assembly-wide
audit rejects informational log/profiling calls, diagnostic argument evaluation,
per-frame diagnostic fields and active report sinks. Errors/warnings and explicit
command replies remain. Debug policy audits now positively require test/bridge
logs and relay, while retaining inactive guards. Normal/debug comparisons exempt
methods containing named diagnostic calls/fields; unrelated bodies still match.
verification.json is definitive for the completed suites and build hashes.
No profile installation, packaging, publication or live Unity test.

### Predicted arrival and20% recording reserve (2026-10-30, previous)

Independent copy `TooManyLags-2.0.0-reserve-window` starts from the verified
short-buffer sources (debug receipt197D15C05DAC232F0A184644444207C12068BCFF1774DFE691F8DDACEF38DF1D).
User authorizes improved arrival prediction,20% extra recording and handoff only
at matching observed progress. Entering reserve alone must not slow playback.

MobBridgeTiming separates route RTT, first positive-state arrival, expected
matching arrival, required recording, reserved recording and native game horizon.
Prediction uses cached CPU rate, native step, sender/receiver main-loop spacing,
first successful positive clock delay/progress and ordinary50ms publication phase.
It jumps between candidate matching frontiers rather than scanning native ticks;
bounded refinement includes useful native-step notifications near the actual
reserved horizon. A first packet can still trail the fake display, so its arrival
alone does not define a successful match.

RequiredWindow=max(0.5*ExpectedMatch,nativeStep/ownerRate).
RecordingWindow=1.2*RequiredWindow, Horizon=RecordingWindow*ownerRate.
The motor-step minimum prevents a sub-step-only recording from skipping all
native controller work. It is capacity, never a minimum visible fake phase.
Route stays unchanged for target input prediction. A20ms needed window records
24ms, not a longer fictitious network RTT. A closed recording stays immutable.

Existing0.5s peer-rate broadcasts carry four extra floats (step, frame, first
ready delay and first progress):32-byte body instead of16, same RPC count/cadence.
Local cache and known-peer lookups reuse their entries; fallback/prediction occurs
only at handoff. Measured readiness expires after2s and is replaced by current
native/frame estimates. First success is measured once per active local epoch.
No new per-mob polling, native AI/body hooks, timers or routine logs. Dynamic
forecasting runs on accepted changed delivery estimates without allocating a new
plan; it does not grow the fixed recording or authorize an unreceived state.

Timeline uses the predicted matching deadline with full capacity including
reserve. Receive-frame quantization is budgeted once, not added again as latency.
Known actual states covering the next frame bypass needless future-packet braking.
Visual advances the current frame before joining; the old previous-frame shortcut
could repeat a displayed pose. Healthy sufficient buffers keep0.5x, including
reserve use. Updated predictions that cannot fit within the full remaining buffer
brake smoothly; exhausted buffers may wait. Prediction errors or delays beyond
20% are not guaranteed invisible.

Checkpoint v4 / bridge capability v5 carry route, first arrival, recording window,
reserved game horizon and owner native step. Target/RNG matching scopes receive
the same game horizon. Source match does not discard remaining observer RNG/
target scope while an observer still demands recorded reserve. Scope is mob-only;
no duplicate authoritative hits/effects/attacks introduced.
Observer capability v2 carries separate recording/startup/matching timing and
accepts samples through the reserved recording bound. It forwards the same
recording, does not rerun AI and uses actual received progress for takeover.
All endpoints require this candidate. Old capabilities fail closed to native.

New test-bridge-timing.ps1 checks prediction across1..2000ms routes,0.1..1.5
cached game rates,10/20/33ms physics steps and4..100ms sender/receiver frames;
exact20% size, full native interval, first-packet-vs-match distinction, readonly
capacity, healthy playback, unused reserve/early join, improved and delayed route,
and later actual matching after reserve use. Actual checkpoint production code
round-trips the new header with a native field-codec seam and rejects old/truncated
headers. Actual extracted LocalTiming/TimingFor/PollRate/ReceiveRate bodies check
cache-hit identity, expiry/fallback,32-byte wire order, unchanged broadcast count,
trust, malformed inputs and server relay. Existing native audits check actual DLL
wiring, extended scopes, no per-frame prediction and release/debug boundaries.

10ms/20ms-native/8ms-frame modeled example: positive state arrives by40ms,
required recording20ms, reserved recording24ms. Healthy playback joins at actual
progress20ms without playing the unused4ms. This is deterministic fixture evidence,
not a live rendering/physics/transport result. Existing animation/body/native
controller implementation is unchanged; only its recording budget is longer.
LateUpdate publication remains as in the prior build; earlier publication was a
read-only separate audit and is not silently installed here.

Full verify.ps1 -Release now includes28 suites; verification.json records final
results, source and DLL hashes. No profiles/packages/publication/push changed.

### Prior low-latency position near-stop follow-up (2026-10-30)

Independent copy `TooManyLags-2.0.0-short-buffer` starts from verified
performance-safe debug B13E7723C3A1A47B692EE6662BA59E371BBE6A15F58004E713C8C21A4BA37B2D.
User clarified this is position movement, independent of the continuing animation.
Latest Player.log is menu/startup-only and provides no bridge timeline; no claim
that it proves the exact live pause. No animation/body/AI recording code changed.

Reproduced timing causes and changes:

- Owner clocks were sent only by MonsterAI callbacks despite carrying body
  progress. Publish completed fixedTime progress after physics through existing
  LateUpdate Maintain, only for active epochs. AI tick and AI dt remain separate;
  running physics cannot invent an AI decision or release an unsupported barrier.
- First-progress/attack packets reset the regular send deadline. Maintain a
  distinct periodic phase. Normal publication remains50ms, independent of AI
  rate. No-progress duplicates are omitted. A bounded native-step notification
  tail uses the checkpoint's handoff timing hint within one ordinary interval
  of its horizon; it ends when crossed. No extra global mob/physics hook/poll.
  Reusable epoch snapshots avoid mutation during synchronous transport callbacks;
  current owner/revision is checked again before a due send. Failed sends retry.
- Receiver/observer permits same AI tick only with strictly newer body progress
  and sample time and identical AI elapsed time. Stale, duplicate, reversed or
  invented-AI packets cannot refresh the frontier. Wire layout is unchanged;
  deploy the candidate to every endpoint for these semantics.
- Visual.Advance checks the already displayed horizon before spending another
  frame of fake time. A newly received actual match no longer becomes behind
  again and waits for an extra packet; Frozen barriers retain their old gate.
- Short-buffer planning uses the next completed native physics endpoint (a
  fractional fake endpoint has no corresponding fractional real physics packet),
  the cached route and measured short publication spacing. Remainder is readonly.
  Known feasible deadlines use nonnegative constant deceleration:
  a=2*(remaining-entrySpeed*wait)/wait^2, with wait>=frame and
  2*remaining>=entrySpeed*wait; apply only braking. Position/speed stay continuous.
  Missing/late/infeasible budgets retain the original critically damped outage
  path and finite-buffer waiting. No speed floor, distance/rotation cap, future
  authoritative pose or unobserved takeover. Base remains0.5x.

PhysicsPublicationChecks model native20ms physics independently of50ms AI,
sender/receiver4/8/16/33ms frames,1/5/10/20/33/49/50/100/500/1000ms cached routes,
AI phase offsets, half-rate CPU/game progress and improved/delayed routes.
50ms/8ms-frame modeled counterexample: old AI-bound publication/advance ordering
reaches zero; current minimum0.334x. This is a deterministic timing reproduction,
not a live speed/FPS measurement or guarantee of constant0.5x under missing data.
Observer ordering, default idle/debug checks and actual addon/native wiring are
also checked; extracted actual PublishPhysicsClock body runs with clock/transport
seams for first progress, separate AI dt/tick, horizon, invalid epoch and retries.

Full verify.ps1 -Release records final status/hashes in verification.json.
Prior comparison builds/ZIPs/profiles unchanged. No installation/publication/push.
Live Wolf low-latency movement still needs retesting on matching endpoints;
native rendering/physics/transport are not proved by headless fixtures.

### Prior safe performance follow-up (2026-10-29)

Independent copy `TooManyLags-2.0.0-performance-safe` starts from verified
performance debug E9F3132FBE092ECBA99F2373629EE42DDB5843B1E76463158EA22978DFB9F1A3.
Only two production files change in this follow-up:

- Animation.RigPaths enumerates each current sibling set once and counts exact
  same-name ordinals. Single-child chains need no sibling enumeration. Temporary
  value-type name/ordinal entries avoid extra segment strings. The table ends
  with the call; renamed/reordered/new FX/equipment retain fresh exact paths.
- Effects.NativeFxScope reuses at most eight successfully unwound scopes/lists.
  Empty/idle/unbound callbacks still allocate none. Every active nested callback
  owns a distinct scope; depth beyond eight still works through ordinary allocation.
  Values.Clear removes Unity references before pooling; session teardown drops
  the pool. Failed cleanup leaves Harmony's original out-state intact and is not
  recycled, preserving retry/error behavior and original callback order.

test-bridge-performance.ps1 extracts actual production RigPaths, NativeFxScope,
BeforeContinuous and AfterContinuous bodies from both this candidate and its
hash-verified prior source. Allocation instrumentation only wraps construction.
Managed Transform/attachment seams compare 155828 assertions over duplicate and
delimiter/Unicode names, current hierarchy changes/subsets, native success/failure,
reentry during Enter/Exit, 24 nested scopes, teardown and failed cleanup retry.
Emitted DLL audits check actual pool wiring/teardown in both build variants.

Operation/allocation examples from the fixtures, not live FPS measurements:
256 same-parent bones reduce GetChild calls 32896 -> 256 and native name reads
65792 -> 256. 1000 consecutive matched FX callbacks reduce scope/list pairs
1000 -> 1, with no loss of callback/effect processing. Check verification.json
for full-suite status and current hashes. Playback rates, sub50ms clocks, native
AI/physics, packets, hitboxes and rendering/IK boundaries are unchanged in this
follow-up. Live Wolf/Troll/FX performance/rendering retest remains open.

Other reviewed candidates (checkpoint reflection, recipient pass consolidation,
physics/Animator pooling) are not implemented: event-only or smaller uncertain
benefits do not justify additional state/transport/lifetime changes here.
Prior DLLs, prepared ZIPs and installed profiles remain unchanged.

### Prior performance and sub-50ms candidate (2026-10-29)

Independent copy `TooManyLags-2.0.0-performance` starts from the verified
low-latency debug DLL 50F850C6F92ACB52BECF69F8B2AAD68BC42B409E89154AE5805CC6C490FFBE96.
Earlier candidate and prepared release/source ZIPs remain unchanged.

Implemented performance changes preserve capture frequency and precision:

- Full native locomotion (realWeight=1) skips interpolation of recorded bones
  that contribute zero to the display. TryPose reads the same live native
  transforms before Apply and its same late saved snapshot afterwards. Apply's
  snapshot timing, root/model placement, attack/partial blending, IK, hitbox and
  FX chain handling remain intact. A fixture extracts actual Prepare/TryPose/
  Apply/Restore bodies from this source and the hash-verified prior source,
  then compares them over weights, times, root rotations, nonuniform scale,
  uncaptured equipment attachments and external late bone changes. Only engine
  Transform/matrix behavior and a history-call counter are modeled.
- Server observer decoding consumes and validates the same fields, masks,
  inherited baseline, finite values, counts, order and trailing bytes without
  constructing dense bone arrays it never uses. Clients retain the original
  full decoding. Dedicated server still relays original ZPackage bytes. Tests
  compare full/lean accept-reject results under truncation and byte mutations,
  nonfinite vectors/quaternions/scales, all control kinds, then check raw relay
  identity with a64-frame/130-bone batch. That modeled batch avoids192 bone
  arrays and332800 raw pose bytes; this is allocation arithmetic, not a live
  server memory/FPS benchmark.
- Recorded frames share unchanged immutable scale snapshots. Position/rotation
  arrays still copy every sample. Tiny actual scale changes create a new owned
  snapshot; mutable source scratch arrays never enter the recording. Export
  only reads these arrays. In a50-frame/100-bone constant-scale example, arrays
  fall150->101 and raw pose storage200000->141200bytes. No cross-recording pool
  or early recycling is introduced, avoiding playback/export lifetime hazards.

Below50ms, two additional timing boundaries were corrected:

- A recording shorter than the native20ms motor step previously had only t0
  until its deadline, despite full motor endpoints being primed at startup.
  CaptureClock can now prime the existing partial final physics endpoint when
  no earlier AI decision is pending. The partial step still skips the full
  controller pass, stays inside the sealed endpoint and is not integrated twice.
- SettleForPose no longer adds a fixed20ms minimum on top of half-playback
  timing. Its existing1ms numerical epsilon and geometric position/rotation
  extension remain. Real residuals still receive sufficient correction time;
  matching continues to require observed new-owner progress.

Timing tests cover1/2/5/10/15/19/20/25/33/40/49ms capture windows at4/8/16/20/33ms
render steps, plus1-49ms playback with varying first-positive owner progress.
They verify moving initial interpolation, native controller cadence, exact
integrated duration, monotonic sealed playback and observed matching. A very
short finite recording can still slow/wait when no matching state is available;
no future world state is invented to avoid that constraint.

Full verification includes the new extracted-body differential suite. Source
and DLL hashes/pass counts are in verification.json. Live Wolf/Troll rendering
still needs an in-game retest. No installed profiles or prepared ZIPs changed.

### Prior low-latency playback candidate (2026-10-29)

Current independent copy: `TooManyLags-2.0.0-low-latency`. Prepared release copy,
root output ZIPs and installed profiles remain unchanged. User reports Wolf
playback nearly stopping early in 50/100ms tests. This candidate changes timing
bookkeeping only; native AI/physics, animation speed, prediction, half-speed base,
sealed recording and observed-horizon takeover remain unchanged.

Two code-level defects were reproduced:

1. ApplyPending sent its initial clock without recording the send timestamp.
   First AI finalizer then sent again immediately, even with zero new body time.
   ObserveClock treated the sub-period gap as ordinary cadence and also used it
   as the playback-rate filter response. An occasional burst could therefore
   replace the expected 50ms clock spacing with a tiny value, distort ageing of
   owner progress, and aggressively brake a short recording.
2. Buffer availability was compared from the start of a presentation frame,
   against packet wait measured at its end. Near a packet deadline, a short
   buffer could be spent before the expected packet callback. For observed
   budgets, the comparison now includes this frame's advance and the damping
   horizon includes that frame. A sufficient healthy buffer avoids damping;
   only numerical float-roundoff tolerance is used. Unknown/outage envelopes
   and their frame-subdivision-invariance behavior remain unchanged.

MobBridgeClockCadence is value state inside active ClockEpoch, no allocation or
global scan. Initial/AI/event sends share scheduling, with the original double
wall timestamp retained rather than reconstructing it from float packet time.
First useful positive simulation progress and new attacks still send immediately;
only redundant zero-progress startup sends are suppressed. Regular scheduling
remains50ms. Early useful/event packets retain their full pose/progress, but
cannot redefine the ordinary send interval or rate-filter response as sub-period.
Genuine slower periodic cadence and later recovery are still accepted. There are
no additional packets, polls, diagnostic logging, or changes to network payloads.

Managed regression uses actual timeline code with a startup/event burst followed
by normal50ms sends. Over the first80% of each recording, unfiltered cadence gave
minimum0.294x/0.357x for50/100ms windows; corrected cadence gives0.474x/0.474x in
that specific scenario. This comparison isolates cadence handling in the new
integrator; it is not a live Wolf performance measurement. Ordinary no-burst
50/100ms handoffs retain0.5x at4/8/16/20/33ms frame durations. Event-burst checks
also reject a zero-speed endpoint hold before the received matching state.
Existing delayed-update/CPU/monotonicity/correction/native-isolation tests remain.

Full release/debug verification results and new hashes are recorded below by
verification.json. Live Wolf retest remains necessary; no universal no-pause
claim for finite buffers with missing updates. Existing prepared release ZIPs
are deliberately not replaced during this candidate test.

Independent working copy: `TooManyLags-2.0.0-release`, based on verified
1.0.94 debug-idle DLL E953BF9A7F1503ED4CE29481DC5417E673CB21CEF135FD4AE830942D07ECFE0E.
Prior copy and installed profiles remain unchanged. This task prepares packages;
it does not authorize publication, installation or Git push.

Assembly, plugin metadata, manifest, Thunderstore metadata, package naming and
verification receipt use 2.0.0. New changelog entries use short user-facing
Markdown rather than fenced implementation notes; 2.0.0 deliberately includes
a longer overview. Historical released entries remain unchanged. PhysicsObjects
is the real config key: movable world props and destructible sources of physical
successors; ordinary loot, projectiles and cosmetic debris stay excluded.

### Native bridge promoted to release

Previously the native bridge lived under TML_DEBUG_SYSTEM, so simply building
-Release would have removed the feature described in the changelog. Build.ps1
now defines TML_MOB_BRIDGE in both variants. Solo commands, artificial transport
latency, test overrides, test polls and selected-mob tracing stay debug-only.
Native capture, physics, target prediction, shared observer recording, hitboxes,
HUD/FX positioning and matching/correction run in release too. Routine profiling
and logging stay compiled out. Native gameplay authority and the 0.5 playback
rate/half-playback smoothing policy remain unchanged.

Build-boundary checks compare unchanged debug IL against verified .94 DLL,
normalizing version metadata. Only explicit config text, test-guard expression
layout and presentation RTT selection changes are exempted. Release/debug IL
parity covers shared gameplay methods; explicit test seams remain exceptions.
Full bridge/native presentation/weapon audits run against both actual DLLs.
Error-only/idle instrumentation audit runs against release as well as debug.

### Low-latency presentation RTT correction

Native FGN 1.5.72 IL confirms CreatureOwnership.Rtt returns 400ms when echo and
LinkController status have no useful sample. That conservative ownership policy
value previously sized the visual recording. A cold LAN connection could get a
400ms fake recording rather than its real RTT. Taking Max with active injected
RTT could also keep a stale large echo when starting a 50ms test. This establishes
a code-level cause, not attribution of a particular unlogged live frame.

MobBridgeLatency.Measure uses current injected test timing first, otherwise real
FGN measured echo/link data, then connected native socket quality. Rounded 0ms
socket timing uses a numerical 1ms recording epsilon. Only unavailable/invalid
routes retain the prior 100ms unknown fallback. Active injected timing bypasses
an earlier route cache for the solo client->server test. Server route timing
replies also use measured presentation data instead of policy RTT. No new poll,
packet or cache is added; work occurs at existing ownership events. Policy
eligibility/ping thresholds are unchanged.

Focused fixtures cover cold zero/4ms LAN, valid echo, stale 500ms echo replaced
by a 50ms test, invalid/disconnected routes and no unnecessary socket reads.
Real 50ms clock/AI send cadence and geometric residual correction can still
exceed ideal 2L + L timing at very low latency. Matching continues to require
received progress; no shortened timer forces a snap. Live feedback at ai_test 0
and 50ms motivates this check; no live retest claimed.

Final suite results, DLL/source hashes and publication/install flags are recorded
in verification.json. Package audits verify manifest/content hashes and ensure
the main ZIP contains release DLL rather than development DLL.

## 1.0.94 mob bridge rebuild

### Idle debug and error-only logging audit (2026-10-29, current candidate)

Independent copy: `TooManyLags-1.0.94-debug-idle`, from the verified
owner-gain-continuity candidate A63F3777AB8ACA88F5FD5A6A570684EF520DAE1B4F0801D97D0EE4C4CCEF0110.
Prior copy and installed profiles remain unchanged.

Default debug previously executed bridge cost allocation, Stopwatch phase
timings, StringBuilder reports, per-frame pose-history divergence comparisons,
per-camera bone rotation probes and routine diagnostic RPC/log formatting during
ordinary handoffs. These are now excluded from the default emitted DLL.
`TML_BRIDGE_DIAGNOSTICS` explicitly opts a source build back into instrumentation;
normal build.ps1 does not define it. Compiler-conditional calls omit argument
evaluation as well as the logging call. The legacy diagnostic RPC has a stateless
empty receive sink so older clients do not produce unknown-RPC errors.

Pure boundary/boss/patch counters are now fixture-only unless TML_TEST_METRICS is
explicitly defined. The selected-test PUP postfix checks active test state before
Unity liveness, actor component or server lookups. Inactive solo test/latency
polls already return before Unity clocks or queue work. Explicit start/stop/status
and cadence feedback continues through the requesting console, without duplicate
BepInEx log output. EnableLogs still gates on-demand console endpoints, with no
continuous informational logging. Genuine native frame failures, unsupported
visual attacks, invalid native movement and artificial-latency buffer exhaustion
remain warnings even with EnableLogs=false.

Functional simulation-rate caching, capability negotiation, matching/observer
clock traffic and normal bridge capture/playback are retained. These are gameplay
requirements, not idle diagnostics. No literal zero CPU claim: inactive test polls
and empty native hooks retain their cheap guards, and the rate cache still samples
and exchanges its required low-frequency timing data.
Capability retries remain independent of main bridge readiness: a connected peer
may still need to negotiate the separate observer protocol.

Build-policy emitted-DLL audit checks all nested method bodies for retained
routine log/profiling calls, camera probes, diagnostic object creation, timers,
metric fields and early idle guards. A negative control confirms reintroduced
diagnostic calls are detected. Managed PUP fixtures cover a formerly selected mob
while the test is inactive, followed by normal tracing when active. Full suite
results and source/DLL hashes are recorded in verification.json after verification.
No live Unity performance measurements or multiplayer validation claimed.

### Rare ownership overlap correction (2026-10-29, preceding candidate)

Independent work copy: `TooManyLags-1.0.94-owner-gain-continuity`, based on the
fully verified observer candidate7447FA316BCAD7C1ACF1C84570D16BBAF1D2567F410E8E504FBDCF84948FECCD.
That prior copy and installed profiles remain unchanged.

Live client log showed an old correction with matched discrepancy3.062m and
duration2.187s still active through server->player completion20. Installed DLL
and prior observer candidate share the same old AdvancePosition/Progress IL.
An isolated managed scenario reproduced a1.547m backwards display step in10ms
when native root restoration and velocity reversal interrupted that correction.
The exact reported live backwards frame was not logged, so attribution remains
an inference supported by the overlap and reproduction, not a live pose trace.

Active correction validity now compares the native owner revision. Only a changed
epoch creates ownership state. Local gains inspect the existing ZSyncTransform's
m_wasOwner field once: if native OwnerSync has not yet restored its root, a lazy
watcher uses the already-installed FX sync finalizer. Callback is null otherwise;
there is no new Harmony patch, per-mob scan, global pose history or network packet.
Pending native reset cannot retire an apparently aligned old correction early.
Track retirement and world shutdown remove the watcher.

After the observed native reset, a separate presentation-only position/rotation
correction begins at the last displayed pose and velocity. Its finite Hermite
steps use the current displayed state rather than a stale absolute curve, so
previous direction protection cannot accumulate a jump released by a later turn.
The remaining duration and residual distance/angle guide scheduling; actual
native reversal can brake continuously, and a stopped successor cannot pull a
forward display backwards. Settlement still requires current geometry. The
original animation clock and native-bone weighting continue unchanged. Model-local
offset/tilt also join from the current display without resetting skeletal weights.
Hit proxies, HUD point and attached FX use the existing shared presented pose.

Normal fake capture, matching, ordinary final-correction trajectory, target/RNG
locks, native root/physics and replication are unchanged. Only active correction
frames retain two value samples for an on-demand displayed-velocity estimate;
there is no normal-path velocity calculation or exceptional-state allocation.
The extra epoch check applies to existing visible tracks, not every mob/AI pass.

Focused timing/reproduction tests pass29,846 assertions including3D axes, multiple
render cadences, entry pose/velocity, unchanged animation clocks, native reversal,
stopped-successor waits and final geometric alignment. Native checks exercise
the existing sync finalizer with null/armed callbacks, failed native sync, actual
m_wasOwner field access, pre-reset waiting and watcher cleanup. Emitted DLL audits
check allocation/hook/gameplay isolation. The headless watcher fixture uses a
reference-key comparer for uninitialized Unity objects; native instance-ID hashing
is unavailable outside the engine. Production cleanup uses ReferenceEquals so a
destroyed Unity component is still removed from its pending dictionary.
Focused DLL audit passes1,708 assertions; native presentation audit passes122.
Full result/hash: verification.json.
Live Unity overlap and frame-cost confirmation remain open; no install/package.

### Recorded third-party observers (2026-10-29, current candidate)

Work copy: `TooManyLags-1.0.94-observer-recording`; based on the independently
verified `TooManyLags-1.0.94-ai-test-verified-20261029` snapshot, including the
newer read-only OwnerDebug phase API. Installed profiles remain untouched.

Old-owner recording inputs, native AI/physics, finite capture window and display
clock retain their existing policy. Source presentation now exports immutable
pose samples. Observers reference their own loaded model and reuse the existing
render, hit proxy, HUD point, attached FX and correction path. They create no
private simulation world, cloned actor, animation graph or gameplay authority.
Locomotion remains native skeletal passthrough; buffered visual attacks carry
exact bone data, with source-relative paths tolerating extra local FX/equipment.

Transport uses separate negotiated reliable RPCs, authenticated source UID and
ownership revision/token. Server relays to ready compatible peers selected by
native `ZNetScene.InActiveArea`, excluding old/new owners. No observer stream in
solo sessions. Initial samples prime interpolation, then source batches target
the native 50ms ZDO send-round interval; this is not a guaranteed per-object
network cadence or an AI tick change. Server does not create fake world ZDOs.

Each observer has its own bounded incoming recording, playback cursor, measured
destination clock arrival and shared adaptive slowdown/wait behavior. Only an
accepted observed frontier permits takeover. Correction uses half actual local
playback duration as its base, with existing residual-size behavior. Destination
clock demand may survive old-owner completion until the recording endpoint has
actually been sent; source completion still removes RNG/target matching locks.
Clock relay exceptions are isolated from the native AI finalizer. One cached
latest frontier retries after a failed recipient send, including the final clock;
duplicates remain rejected by the existing monotone destination-frame contract.
Active matching never expires merely because the initial latency estimate ended.
Unloaded pending receivers expire; disconnects seal accepted prefixes, retire
recipients and release demand. New ownership epochs supersede old streams.

Focused tests: 26,729 existing timing/recording assertions and native presentation
hook/weapon audits passed before final integration. New managed transport tests
cover codec truncation/finite checks, exact pose delta values, relevant recipients,
wrong source/revision/sequence, failed-send cursor retry, immutable seals,
early clocks, delayed matching, vanilla bypass, disconnects and buffer recovery.
Actual Valheim Mono audit also roundtrips all packet kinds using native ZPackage.
Latest recorded-observer fixtures pass 22,853 assertions on CLR and Valheim Mono;
native observer audit passes 880 contracts, including separate source/observer
capacity, exact source-presence epoch/token and attachment-independent rig mapping.
The final complete result and DLL hash are recorded in `verification.json`.

Open: live three-client target-switch rendering, packet costs and actual frame
costs. Managed RPC doubles and native hook installation do not validate those.
`ai_test` retains its existing solo guard; use real target-driven handoffs for
the three-client scenario. Joining the active area after the descriptor is not
bootstrapped into an in-progress recording; it keeps ordinary native display.
Server-origin handoffs and unsupported recordings retain the legacy observer
fallback. The new path applies when a client old owner has a native recording.
See `docs/observer-recording.md` for timing and transport details.

### Attached visual effects presentation (2026-10-29, current)

User observed burning behind the displayed mob during a 500ms test and asks
for mob-attached visual FX to follow the displayed body. Installed vfx_Burning
asset has its own ZNetView/ZSyncTransform, nested local/world-space particle
systems, a light and child audio. Native EffectList supports direct parenting
and ParentConstraint follow sources. Moving only Character.Visual did not move
an effect attached to Character.transform or a separate network-rooted FX.

MobBridgeEffects now creates transform-only presentation anchors for actual
attachment sources, including bones. Existing FX objects move under those
anchors; native follow-constraint sources are redirected while retaining their
weights/offsets and unrelated sources. No particle system, effect identity,
status, timer or sound is cloned/restarted. Actor/root/Rigidbody and gameplay
objects are unchanged. Visual-only admission excludes Character/Projectile/Aoe/
ItemDrop and hierarchies containing colliders/Rigidbodies; physics/gameplay
attachments are not repositioned as visual FX. Ordinary unattached world impact
effects are not adopted. Embedded mixed gameplay/visual components sharing the
same physics object remain native rather than moving gameplay geometry.
Anchors remain below the original source hierarchy so parent-Character lookups
and source activation still work. Anchor placement refreshes before camera
culling, after final bone application and after render restoration, preventing
double offsets when the model itself is temporarily moved/restored.

The live source SEMan is already cleared before MobBridgeVisual.Start. Status
shadow now retains only the existing FX ZDOIDs from the already captured payload.
Those references discover preexisting FX and late arrivals without reinstating
status gameplay. Existing child emitters/lights/audio are scanned once on the
affected mob. New vanilla EffectList outputs use native enabled/variant order,
preserving same-prefab entries attached to different bones. New network FX
receive attachment parent/path/local-pose metadata only for the affected owner
epoch, lasting through the source's final visual-completion notice. No periodic
RPC was added; inactive epochs do not tag ordinary FX. Tagged scene arrivals
and captured FX IDs discover remote instances. Delayed attachment tags are
retried only for newly created visual FX associated with an active mob.
Existing native water/slide/flight instance arrays are read once as well.

ZSyncTransform callbacks temporarily restore each registered effect's native
parent/local pose and original constraint sources. Finalizers save native
updates and reattach presentation even after exceptions. Owner-sync scopes run
only on owned FX; replica-sync scopes only on unowned FX. Unrelated/idle objects
take a dictionary/count fast path without Unity component/transform lookups.
There is no whole-scene scan and no per-mob AI work. Effect instances, original
parents and constraint bindings restore on normal completion/stop; vanilla
external detach is respected. Anchors follow source activation and are destroyed
after effects have been restored. Session hooks/loop entry stay warm between
handoffs and are removed on world shutdown.
Character.SetupContinuousEffect has its own scoped restore because vanilla
directly rewrites existing water/slide/flight FX positions there. Successful
updates retain native attachment-offset changes instead of leaving effects on
the hidden root. Native reparent/detach changes retire presentation ownership.

Anchor preparation is inserted before the first native constraint/particle
update phase in the existing PlayerLoop. This matters for world-space particle
births: render-only movement would leave emission at the hidden root. Existing
world-space particles remain in world space as a vanilla trail. The shared
presentation clock still advances once per frame. Late draw refreshes bone
anchors against the actual drawn pose; the FPS/skin/rotation fixes and half-
playback final correction remain. Only this callback is removed from the current
loop, preserving other plugins' entries. Both endpoints need this build for
attachment metadata on effects newly created by the destination.

Validation includes managed attachment/native-scope/constraint/nested-callback/
partial-failure/cleanup/external-detach fixtures; status FX ID retention after
source relinquishment; actual native Harmony installation for EffectList parent
parameter __2, scene arrivals and all three ZSyncTransform entrypoints; modeled
native PlayerLoop insert/remove order/idempotence; final epoch fencing; emitted
checks excluding FX cloning, particle re-simulation, damage and ownership claims.
No live Unity particle, audio or network/render fidelity claim; in-game 500ms
burning and mid-bridge new status tests remain necessary. Setup/presentation
cost logs now include attachedFx counts for live diagnosis. See verification.json
for completed full verification and exact DLL hash.

Primary Unity phase reference: https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/PlayerLoop/PlayerLoop.bindings.cs

### Half-playback final correction trial (2026-10-29, current)

User requests final correction base of 1/2 instead of 1/4 of stretched playback.
BeginTakeover now passes the full elapsed presentation duration to the explicit
SettleForPlayback helper, whose base is half that duration. Before this change
the caller halved elapsed duration before SettleForPose halved it again.
Example: actual 200ms playback -> 100ms base correction (previously 50ms);
1000ms playback -> 500ms (previously 250ms). Existing geometric extension for
large position/rotation residuals, minimum duration and completion rules remain.
Inherited-pose entry smoothing still uses its original separate timing path.
No fake playback rate, recording window, native animation or attack-bone fade
change. This is a duration experiment, not a fix for the other audited stalls.
Managed checks cover 40/100/200/400/1000/2000ms playback and large residuals;
emitted wiring verifies the final caller does not halve playback twice.
See refreshed verification receipt for completed checks; live result remains open.

### Continuous playback-rate updates (2026-10-29, current)

User confirms native locomotion presentation improves animation but reports
occasional small movement jerks during stretched playback. Found a concrete
clock discontinuity: ObserveRealRate immediately replaced playbackRate whenever
the destination rolling estimate changed. MatchHorizon also reset playbackRate
to scheduledRate each frame. Position interpolation remained linear and frame
driven, but an estimate update could instantly change rendered trajectory speed.

ObserveRealRate now sets only the target rate and a response interval derived
from successive accepted destination-local sample timestamps. Render advances
the current rate exponentially toward that target and analytically integrates
its elapsed distance in simulation time. This avoids both an instantaneous speed
change and subdivision-dependent progress. MatchHorizon no longer bypasses the
transition. Invalid/nonpositive sample spacing is ignored. Cached initial rate,
monotonic fake time, recorded poses, native physics, simulation cadence, observed
takeover frontier and native locomotion presentation remain unchanged. Response
uses measured sample spacing, not a fixed simulation raster or movement cap.

Fixtures cover faster/slower target rates, 20/50/113/400ms sample spacing,
one 200ms frame versus twenty 10ms frames, monotonic progress, invalid spacing
and horizon-matching calls during transitions. Native emitted checks require
continuous integration and measured-spacing wiring. Per-bridge presentation
summaries now add bufferStarvedFrames, endpointHeldFrames, maxPlaybackRateStep
and maxFrameMs, accumulated without per-frame string/log allocations. The first
counter includes startup frames with no completed native motor sample; the
endpoint counter records waiting for an observed owner frontier after sealed
recording exhaustion. These cases remain distinct from rate-update kinks.

Current Player.log also includes dungeon generation and other world work during
the test, plus native fake contacts against logs/terrain with substantial speed
changes. Those can cause real frame/motor pauses; no live per-frame evidence yet
assigns each reported jerk to this clock defect. No unbounded extrapolation,
buffer rewrite, extra physics tick, collision-curve approximation or new root
speed/rotation limit was added. Live smoothing quality remains to be tested.

### Continuous native locomotion presentation (2026-10-29, current)

User prioritizes continuous animation and matching native-to-fake poses over
equal animation/root speed. Ordinary locomotion now keeps native bones throughout
the bridge. The existing live Animator continues its walk/turn cycle and native
procedural head/foot pose; presentation no longer substitutes the private rig's
different walking pose at handoff. Root recording/playback, decisions, physics,
target prediction and authority are unchanged. Native animation can therefore
run faster than slowed root movement, as explicitly accepted by the user.

Visual attack playback remains available. Immutable animation frames carry an
attack/pending flag, interpolated into a smooth contribution around sampled
attack boundaries. Private bones contribute only there. Entry also fades over
half the planned simulation horizon in wall time; the last quarter of the
simulation horizon fades back to native, reaching full native at the endpoint.
Final takeover weight can only further remove private contribution. No frozen
endpoint is reintroduced while waiting for observed owner progress or during
final correction. Before a first animation interval exists, native bones remain
fully selected. All bones/hitboxes still share the prepared policy and fresh
late native sample. No extra graph, AI, clip event evaluation or buffer mutation.

This prevents bridge-imposed whole-rig locomotion freezes/pose replacement. It
does not force native idle/attack clips to move when vanilla intentionally holds
them, and root-position holds remain a separate issue. Native flight/attack
animation can differ from the predicted slowed body; visual attacks near the
buffer end fade early rather than freeze. Existing private transition/IK parity
limitations are now confined to the partial predicted-attack contribution.
Managed fixtures cover locomotion passthrough from start through exhausted
buffer, attack metadata interpolation, fade continuity and endpoint/native
takeover behavior over 20/50/100/500/1000ms. Emitted checks verify policy wiring;
live animation/render quality still requires the user's game test.

### Further stall/backstep and animation-start audit (2026-10-29)

Found and reproduced a missing initial direction in MobBridgeTakeover. Existing
protection only learned direction after a nonzero replica-root delta. A displayed
lead of x=1, stationary replica root x=0 and 100ms correction produced x=0.896
after 20ms, even though the accepted owner state could already report positive
velocity. BeginTakeover now passes the paired destination-frame velocity (actor
velocity only for an epoch-change fallback). The constructor seeds direction
from that finite nonzero velocity. Subsequent actual root deltas retain their
existing behavior. No new smoothing limit, trajectory prediction, timer hold or
fake-speed change. Regression fixtures cover both horizontal signs, vertical
and sideways movement, waiting for the first root delta, eventual catch-up and
zero-velocity correction. Emitted wiring checks require passing owner velocity.
This closes the initial backward step; an existing correction can still hold a
displayed lead until the root catches it. That tradeoff is not a new stall fix.

Animation findings, deliberately separate from this position fix:
- The visual-attack experiment introduced recording of the complete rig. Run
  cycles now follow the slowed fake clock; the earlier movement-only path left
  native skeletal animation running at full speed. This audit does not change
  animation speed. Later skin refresh made buffered poses reliably visible.
- Animation history starts with the native visible pose, while subsequent samples
  come from the private graph without native CharacterAnimEvent head look-at or
  foot IK. The first interpolation interval can therefore remove a native
  procedural pose, even with identical controller state. This is a real parity
  gap and plausible head-reset cause; no live per-bone proof yet.
- Constructor copies current state/time and parameters, but merely counts active
  Animator transitions; it does not restore their next state/blend progress.
  This can change a partially blended pose at recording start. Latest Player.log
  inspection contains 60 setup reports, all transitionLayers=0, so this gap is
  not supported as the cause in that session. No approximate CrossFade restore
  or replacement transition duration introduced.
- Immutable recording end still clamps body/bones while an observed destination
  frontier is behind. A fully replayed bone sample also stays at its endpoint
  as the final native blend starts at zero weight. These can look like a stop;
  continuously producing new fake states after closure would violate the current
  fixed live-input/immutable-buffer contract. No silent extension or extrapolation.
- Model-local tilt is another incomplete pose component: ApplyPose keeps the
  initial visual LocalRotation through correction, while native UpdateGroundTilt
  can keep changing the restored native visual. Private motor does not invoke
  UpdateGroundTilt. For mobs with ground/flying tilt, releasing presentation can
  reveal a different native model-local rotation even after actor-root correction
  completes. This is a source-level gap, not established as this Troll symptom;
  no speculative private-scene raycast or root/bone timing rewrite included.

Checked native update ordering: nonowner ZSyncTransform.ClientSync runs during
CustomFixedUpdate; its CustomLateUpdate invokes OwnerSync. A claim that normal
replica position is always finalized after HUD preparation is not supported by
these sources. Latest Player.log has 60 presentation reports with zero measured
draw-bone gaps and no reported frame/unsupported-decision errors. This rules out
those logged failures in that session, not every visible animation artifact.

The user-installed DLL hash during this audit matched the preceding verified
748FE6 build; profiles remain untouched. Focused/full outcomes for this patch
are recorded by the refreshed verification receipt. No live Unity validation.

### Smoothing regression comparison (2026-10-29, current)

Compared current MobBridgeVisual with task workspace/work/before-multi-hitboxes
(the snapshot directly before collider admission). Ignoring whitespace/comments,
Advance, BeginTakeover, ComputePose and CurrentPose are unchanged. The nearby
pre-visual-attack snapshot also has identical Timeline, Recording and CaptureClock;
Takeover only adds animation weight/time methods. The no-backwards position
hold and sealed-buffer endpoint hold are existing behavior, not proof of the
user's new regression. User now confirms FPS spikes and rotation flicker are
fixed, but reports roughness/brief stops, possibly animation only and possibly
near handoff start. Preserve the working performance/collider/rotation fixes.

A specific post-collider change was isolated: UpdatePresentation began mixing
all native bone values early in the HUD/LateUpdate preparation and caching that
blend. The camera subsequently applied that mixed pose after culling, when
native animation/IK may have newer transforms. The original pre-Troll path read
native bone values at application time. This ordering difference is a concrete
regression candidate; the log does not prove it explains all root movement.

Prepare now stores recorded pose, frame weight/time and model matrix, without
reading/blending native bone locals. Apply uses the fresh native local values
it already saves for restoration, mixing only then at the working late camera
boundary. The fake sample/weight are not advanced again. Pure fake (weight 0)
remains exactly the recorded pose. During a nonzero blend, hitbox shapes refresh
from the same saved native sample after bone application; they remain math-only
and do not re-evaluate AI, physics or animation. No extra full-rig blend pass or
native-bone reads in early preparation. Same restore/finalization semantics.

The installed Troll prefab enables CharacterAnimEvent head rotation and foot IK.
The private rig has no CharacterAnimEvent. Per-render skin refresh introduced
after collider admission makes private recorded bone poses reliably visible;
previous cached native skinning could mask their differences. This is a separate
incomplete procedural-pose parity limitation, particularly for pure fake start,
not a confirmed new native AI or position-smoothing error. No head-bone exclusion,
normal-speed locomotion override or untested IK reimplementation was introduced.

Emitted regression checks reject native local-position/rotation reads in Prepare,
require native rotation sampling + blending in late Apply, and require hitbox
refresh on the late blend path. Existing independent actor, callback, record
immutability, binary lookup and hook lifetime suites remain. See verification.json
for completed checks/hash. No live Unity confirmation or profile installation.

### New Troll log and further setup optimization (2026-10-29, previous verified build)

User attachment 3d424a4c-d767-4e3a-9170-a02126523122 contained five presentation
reports. maxDrawModelGapDeg is zero in all five; maxDrawBoneGapDeg is 8.238,
3.463, 22.479, 10.150 and 39.661 degrees. This proves a difference between the
cached intended skeleton-root rotation and the measured draw-boundary root,
not a duplicate model or the identity of an overwriting callback. Actor root
takeover yaw is 0.0-0.6 degrees in these samples. Active Animator transitions
were not reported at capture in this log (transitionLayers=0).

Root placement remains in onPreCull for spatial culling. Buffered bone
application/save now occurs once in onPreRender, after culling, retaining
post-render restoration. The early callback no longer applies bones, avoiding
a duplicate rig write. Diagnostic MeasureDraw remains read-only and runs at
pre/post boundaries. maxApplyBoneGapDeg additionally measures the immediate
post-application discrepancy: it can distinguish a cached pose reconstruction
mismatch from a later writer if flicker remains. Unity documents the callback
order and per-render skin matrix refresh requirement:
https://docs.unity3d.com/cn/6000.0/ScriptReference/Camera-onPreRender.html
https://docs.unity3d.com/ja/6000.0/ScriptReference/SkinnedMeshRenderer-forceMatrixRecalculationPerRender.html
No live render confirmation of this timing change; no rotation clamp, LOD or
global graphics setting change. Existing renderer-free recording rig remains.

The same live log isolates snapshotCreate at 98.743 ms on the first sample and
39.068-50.807 ms on repeated samples. Ground remapping was only 0.011-0.362 ms,
so the earlier suspicion of expensive ground remapping is not supported here.
Snapshot construction now caches exact slot constructor delegates by field
type. Each request still validates its field list and allocates fresh per-actor
slots with current native values; no actor or mutable snapshot is cached.

A native-class headless benchmark of 100 snapshot creations previously took
134.200 / 139.485 / 137.476 ms warm. With cached constructor dispatch, warm
passes took 12.716 / 7.610 / 14.087 ms; first-use pass was 34.127 ms. This is a
constructor microbenchmark, not a prediction of the remaining live frame cost.
Native tests exercise independent actors, fresh state, duplicate-field
rejection and restoration after injected failure.

PoseHistory uses lower-bound binary search over the existing circular data,
preserving capacity and interpolation arithmetic. 1,520 differential checks
compare exact vector/quaternion output with the former linear algorithm over
190 appends, repeated timestamps, tiny spans, endpoints and multiple wraps.
Existing recordings, prediction, RNG, collision coverage and clocks unchanged.
See verification.json for current completed suites and DLL hash.

### Troll render flicker follow-up (2026-10-29, previous diagnostic build)

User confirmed the club alignment is fixed, but reported rotation flicker or an
apparent second model at takeover. Motion Blur is off; no TAA/upscaling mod is
used. The private recording rig copies transforms plus an Animator/receiver,
never renderers. Installed Troll prefab inspection found Body and Hair skinned
renderers with the same root bone, in one LOD level. This does not prove the
absence of third-party runtime renderers or identify the remaining artifact.

Added active-track-only draw boundary diagnostics, not a speculative rotation
correction: camera pre/post render compare actual visual/skeleton-root rotation
with the cached intended pose, accumulating maxDrawModelGapDeg and
maxDrawBoneGapDeg. The once-per-bridge presentation report includes drawSamples;
zero means no camera measurement, not absence of a mismatch. These callbacks
read poses only, allocate no per-sample report, and unsubscribe with the track.
A live log from this build is required to distinguish late pose overwrite from
a skin/render artifact. No global graphics, LOD or motion-vector changes.

Latest live logs also showed repeated snapshot phase costs around 42-46 ms.
A separate headless 100-creation benchmark measured warm ShadowSnapshot.Create
around 134-139 ms total (about 1.34-1.39 ms per call), so it does not explain
that entire live phase. Added snapshotCreate / snapshotCallbacks /
snapshotInstall / snapshotGround phase timings to isolate construction from
ground-collider remapping/cooking and final state capture. No unmeasured pool
or collision omission was introduced. Prior performance changes remain.

### Active preview field/lookup overhead (2026-10-29)

Motor Step, animation-event processing and animation-hash hooks now use cached
exact-type FieldAccess delegates for the same native fields. Callback isolation
has per-actor typed capture/clear/restore slots, reusing the existing accessor
cache. It still captures the latest native delegates before each step, clears
all of them during the isolated motor, and restores them inside the existing
finally chain. Reflection remains in setup/metadata work; it is removed from
these repeated paths without changing field coverage or state order.

A headless benchmark on the real native Humanoid field ran 50,000 identical
float read/add/write pairs. Reflection took 20.677 / 22.327 / 28.619 ms versus
0.358 / 0.445 / 0.552 ms for typed access (three passes, warm loop). Final float
values matched exactly. This measures dispatch only, not a live frame, Unity
physics, total bridge CPU or FPS. The benchmark is included in the native
presentation audit output; no timing threshold controls test success.

Collision-shape Mirror now uses the existing membership table as a direct
source-to-Shape dictionary, preserving creation order and all copied geometry.
Observe retains each collider's stable Transform reference and reads the target
pose once per synchronous observation; changes/reparenting remain visible.
Pure fake playback samples the recording and model matrix as before, but skips
real-bone reads/blending at exactly zero real weight. Any nonzero weight retains
the original native-pose blend and its continuous timing.

Native checks compare every new scalar/vector/quaternion accessor with its
actual field. All game callback fields are exercised across capture, nulling,
restoration and recapture of changed/null callbacks. Emitted checks require
callback restoration in finally and no reflective field lookup in the motor
paths; direct collider lookup must not enumerate the scene. Existing timing,
RNG, weapon, status, collider and renderer checks still apply. See the current
verification.json receipt for completed suites/hash. No installation, live
Unity/multiplayer run or measured live FPS claim.

### Ownership-edge cost investigation (previous verified build)

Measured the former install/remove paths against native game methods under
Valheim's embedded Mono (headless, omitted Unity-only static initializers).
No third-party Harmony graph or live Unity scene is loaded by this benchmark.
Repeated native-recording + presentation installation alone took 52.853 and
70.568 ms; their removal took 52.187 and 56.929 ms. Solo-test and destination
AI hook costs are additional, separately listed in the timing output. Do not
sum endpoint phases into a claim about one live client frame.

Hooks now warm at enabled world connection and persist across individual
transfers, capture completion and idle gaps. The solo-test hooks warm on test
start and clear only at stop/reset. Shutdown/feature stop removes session
hooks. Ordinary AI callbacks with empty epoch dictionaries return before any
actor lookup or Unity clock access; native isolation prefixes retain their
existing empty-scope guards. Persistent wrappers have a small dispatch cost;
this is not a claim of zero overhead or a live CPU benchmark.

The retained lifecycle benchmark measured 2.280 ms on its first managed seam
call, then 0.040 / 0.031 ms on two repeated edges (including reflection). It
moves compilation to session start/end instead of removing required guards.
Reproduce with tools/benchmark-mob-bridge-hooks.ps1 after building. Native
hook tests cover retention, idempotence, stop cleanup and idle fast paths.
Native field/delegate metadata is cached. Private collision geometry now
receives pose/enable writes only when those exact values change; no motion
threshold, missing collision shapes, physics-rate reduction or AI cap added.

Debug `bridge cost` reports separate checkpoint seed/status/portable/send,
setup hitboxes/hooks/scene/geometry/body/rig/snapshot/context, capture AI/motor
and largest capture frame, plus disposal. Setup includes shape/bone counts.
Reports are queued until LateUpdate instead of writing between timed phases.
These reports are needed to identify the remaining Unity scene/rig, allocation
and first-capture costs in the actual mod stack; headless results cannot prove
the entire live spike is gone. Capture time includes existing diagnostic work
and recording close; detailed AI/motor totals need not sum to that total.

The user localized the Troll rotation flicker to the beginning of takeover
blending and reported a floating club after the multi-collider fix. Its 15
logged takeover yaw corrections range from -3.7 to +10.6 degrees; those samples
do not prove the cause of a one-frame rotation excursion. Two concrete gaps
were found: render callbacks resampled bone blending with a later wall time
than the root/hitboxes, and multi-collider FollowHit temporarily overwrote the
whole visible rig a second time before rendering. The latter also invalidates
the assumption that cached skin matrices describe the current bone transforms.

UpdatePresentation now prepares the complete bone/root blend once per frame;
all cameras apply cached local values with no repeated sampling/blending.
Bone hitboxes compose only their requested ancestor chains from prepared
local TRS values and an actor-parent-aware model matrix. They never write the
live rig. Existing partial construction/cleanup/weak-spot routing is retained.
Hit proxy poses/scales and environment shapes skip only exactly unchanged
writes. No epsilon threshold, collision omission or reduced simulation cadence.

A bridge-local MobBridgeSkin enables forceMatrixRecalculationPerRender on
existing skinned renderers, including inactive LOD meshes, and restores the
original setting at disposal. It is not polled for ordinary mobs and adds no
BakeMesh, per-frame rig scan, equipment spawning or animation reevaluation.
Official Unity API documentation explicitly calls for this flag when a skinned
mesh is rendered with multiple poses within one update:
https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/skinnedmeshrenderer/forcematrixrecalculationperrender
This addresses the matrix-cache contract, but resolution of the observed Troll
rotation/club symptoms still requires a live Unity render test. No measured
live GPU cost or claim that every renderer/third-party attachment is covered.

40 managed hitbox/skin tests include cached bone-position/scale delegation,
no visible-rig mutation and idempotent restoration of original renderer flags.
Emitted audits verify once-per-update preparation, clock-free camera readers,
math-only bone queries and renderer cleanup. Native lifecycle tests cover
retained hooks and shutdown. The private Animator still seeds current states
without reconstructing active transitions; that separate parity limitation was
not changed speculatively. Debug diagnostics retain transition-layer count,
initial root pose gap and maximum actor/skeleton-root angular steps.

See verification.json for the completed suite/hash. No installation or live
Unity/multiplayer test was performed by the agent.

### Multi-collider preview admission (2026-10-28, previous verified build)

The blanket collider/Rigidbody rejection below the visual is removed. Admission
now copies mob-owned capsule (all axes), sphere, box and mesh query shapes.
Nested Character/ZNetView entities are excluded. Native shapes are suppressed
only after complete construction; partial failure disposes copies and preserves
the original enable states. Unsupported custom collider classes still report a
shape-specific failure instead of silently rejecting every multi-collider mob.

Hitbox sampling applies the same buffered/blended bone pose as rendering and
restores the source rig in finally. Root/attack shapes retain actor-relative
transforms; bone shapes follow sampled animation. Proxies remain independent
kinematic hitbox-layer bodies, preserving geometry, scale, trigger state and
shared materials. Character.FindWeakSpotIndex receives the original collider
identity for this Character only; HitData keeps the proxy reference. Native
physics restoration immediately disables every proxy and runs only once, so
late disposal cannot overwrite the returned owner's collider changes.

34 managed regression checks cover shape fidelity, head movement, hidden-hit
rejection, weak-spot identity, nested entity exclusion, inactive/disabled shapes,
repeated cleanup and partial construction failure. Native hook audit includes
installation/removal of FindWeakSpotIndex. DLL audit checks shared pose use and
finally restoration. Installed Troll prefab was read directly: root capsule
on layer 9, attack capsule and head sphere on layer 27. See verification.json
for the completed full suite and current DLL hash. No live Unity physics/query
or multiplayer test; no profile installation, packaging or publication.

### Visual-only preview attacks (2026-10-28, previous verified build)

Completed full debug verification: all 19 suites passed. DLL SHA256:
`9CD2736BB66A1471925967D6F4CBCAC7A399439925A835F61DC2FDD9FB163B61`.
Focused results: 2089 timeline/recording/blend checks, 434 emitted isolation/
wiring checks, 46 native hook/scope/event checks and 498 native weapon/variant/
chain/snapshot checks. Receipt: verification.json. No live Unity world test.

The user's newer request supersedes the earlier attack barrier and decision to
omit animation recording. Predicted attacks now continue capture until the
normal latency deadline or observed takeover; they do not close/freeze capture.
Unsupported unrelated AI actions retain their previous safe-stop path.

A transform-only copy of the visual rig has a private native Animator and a
manually evaluated AnimatorControllerPlayable. It contains no Character,
CharacterAnimEvent, ZNetView, equipment, colliders or renderers. The selected
attack trigger uses the native random variant, chain precedence/cooldown and
native ammunition eligibility check. The private graph supplies attack tags,
movement/rotation factors, root motion, speed/stop and jump/flight events to the
private native movement producer. Attack age follows that physical animation
timeline. Source Actor fields, physics, RNG and authority still restore.

Animation events resolve only to MobBridgeAnimationRoot: Hit/OnAttackTrigger
can update private flight-toggle metadata, never invoke Attack.OnAttackTrigger.
Native jump is admitted only through its ground/water branch, excluding its
unrelated global local-player grapple fallback. No preview Attack.Start/Update,
hit tests, health/resource payments, ammunition consumption or projectile spawns.
Attack VFX/audio and weapon trails are outside this first visual-only attempt.

Vanilla controllers contain RandomIdle, AnimSetTrigger and StateController.
Scoped handlers map the first two into private graph parameters and preserve
per-preview idle phase/random state. StateController child/FX actions are skipped
on the private graph. These handlers pass through for every real Animator.
Unknown custom state behaviours reject preview before capture. Three-argument
declared callbacks are bound explicitly; Unity's inherited four-argument state
callback overloads otherwise cause AmbiguousMatchException during hook install.
Initial graph seeding does not replay state-entry triggers or reroll idle values.

Local bone transforms are copied into immutable pose frames on the simulation
timeline, interpolated for render and restored after rendering. Incoming real
animation keeps running independently. At observed horizon takeover, recorded
pose time continues from its existing phase and accelerates 0.5x -> 1x while its
weight fades into the real pose over the existing correction window. Rendering
does not execute any preview graph or animation events. No wire-schema change.

Focused validation covers zero-time attacks continuing capture for 20-1000ms,
bone interpolation/immutability, monotone mid-attack takeover and speed ramp,
native random/chain trigger selection, pending/in-attack deduplication, private
cooldowns, actual native hook installation, scope isolation and safe clip-event
handlers. Full verification receipt is verification.json when complete.

Live Unity Animator/PlayableGraph evaluation, skinned rendering, blend timing,
scene cost and multiplayer combat remain untested. This is a visual trial, not
a claim that unpredictable target movement yields identical future decisions.
Native ground tilt/foot IK, equipment model changes and controller child-visibility
actions are not newly replicated by the pose-only renderer. Existing combat hit
forwarding remains with the real owner. No profiles installed or modified.

### Native movement recording producer (2026-10-28)

Completed full debug verification: 19 suites passed. DLL SHA256:
`7AE07E5AFC6BFA6B41C7466E527565BCB85DDBCF30EE83E5E3FD9E33D5B16639`.
Receipt: verification.json, 2026-10-28 21:13:54 local. Focused checks include
1566 timeline/recording, 209 emitted wiring, 36 native hook/guard and 490
native weapon/snapshot assertions; 91 status-state checks include retained
private native liquid effects. No live Unity world or multiplayer validation.

User scope: change the source of buffered positions/rotations. Keep existing
target prediction, AI decisions, attack barrier, stretched playback, observed
destination horizon, hit/HUD routing and final correction. No animation-state
recording. Recorded poses are append-only; neither live target updates nor
destination packets rewrite them.

The source Character's original UpdateMotion (walking/flying/swimming),
UpdateWater, CalculateLiquidDepth, UpdateGroundContact, UpdateLayer,
UpdateLookTransition and OnCollisionStay execute against a transaction-local
field snapshot and a private Rigidbody/capsule in a local Physics3D scene.
There is no duplicate Character/MonsterAI/ZNetView and no copied custom motor.
Read-only nearby collider geometry is mirrored into that scene; moving inputs
are sampled only while the recording window is open. Target geometry uses the
same forward prediction as AI. Native Character root turning is copied into the
private Rigidbody before simulation. Source fields, callbacks, root/body pose,
velocity, root layer and global RNG restore in nested finally blocks. The native
motor sees the same owner collision layer on its Character root and private body.
Ground-body references
are remapped to private copies so native motor impulses cannot push real bodies.
Own RPCs, ZDO writes, damage, knockback, FX and animation writes are suppressed
only within the synchronous native step. Newly added private liquid statuses
persist in the private effect timeline; real status-manager contents restore.
Session hooks and private scene/resources are removed when recording ends.

For L=100ms at game rate 1: capture native movement for 100ms wall time while
playback advances at half speed. At wall 100ms capture seals at sim 100ms while
playback is around sim 50ms. At wall 200ms replay reaches sim 100ms; takeover
still requires the observed destination frontier. An earlier observed matching
frontier may start existing takeover earlier. An attack seals at its decision
horizon; ahead production never jumps playback to that barrier immediately.
Native motor ticks use Time.fixedDeltaTime, independent of render callbacks and
cached AI cadence. A fractional final interval integrates private physics only,
without applying a second controller/acceleration tick. The buffer keeps all
poses until disposal, so ahead capture cannot overwrite early replay after 64
samples. A closed buffer rejects every append and stops sampling live inputs.

Verification covers recording endpoints at 20/50/100/500/1000ms under four render
cadences, exact fixed motor tick counts, fractional physics tails, retained early
poses, read-only replay, attack endpoint playback, actual native delegate binding
and Harmony installation/removal, scoped ownership/side-effect guards, and
original-game scalar/reference snapshot restoration after injected failure.
Tests replace the obsolete handwritten contact-solver fixtures.

Limits: headless tests do not execute Unity PhysicsScene, render frames or live
multiplayer. Nearby shape copies do not clone gameplay components; external
moving bodies are kinematic inputs rather than reciprocally simulated bodies.
Ship/component-dependent contact behavior, scene setup cost and live collision/
water continuity need in-game verification. This is a native movement producer,
not a claim of deterministic full-world replication. Protocol 4/checkpoint 3 and
wire field schema remain unchanged. Profiles were not installed or modified.

### Short-handoff frame-clock regression (2026-10-28)

AdvanceFrame previously used min(Unity deltaTime, wall time since its previous
callback) every frame. EnemyHud.UpdateHuds runs in EnemyHud.LateUpdate and the
plugin also prepares the frame in LateUpdate. Variable callback phase therefore
entered the clock: an earlier callback shortened a step while a later callback
could not recover the lost time. Even steady native frames could produce uneven
and systematically slow playback. The comparison build's wall clock did not
have this particular frame-delta truncation.

Only the first frame is now shortened to exclude pre-handoff time. Subsequent
frames consume native deltaTime exactly once; cached readers still do not advance
time and Unity's own bounded delta still prevents unrendered wall-gap catch-up.
A 50ms-window regression drives 120 steady 16ms frames with alternating 6ms
callback-phase offsets and requires an 8ms fake increment on every frame.
This proves clock continuity under callback jitter, not live render smoothness.

### Terrain-contact start-stall follow-up (2026-10-28)

Current Player.log contains 153 early motion samples, including 11 with terrain
contact and zero allowed motion despite an active movement command. In one
handoff, fake progress advances .061 -> .177s and body progress .076 -> .188s,
while visible displacement remains .033m between wall .151s and .411s.
CanMove=True and attackHeld=False identify a body/contact stall in that case.
Eight attack-held samples are a separate, intentional barrier; their unstepped
body's CanMove diagnostic now initializes from the source instead of default false.

Unity documents that initial-overlap CapsuleCastAll results have distance zero
and a normal opposite the sweep, not an actual surface normal:
https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.CapsuleCastAll.html
The former body projected both physical velocity and controller m_currentVel
onto that artificial normal, zeroing motion. It also snapped the root to a
flat-foot height after each step. On a slope, a capsule needs different support
height; that snap can re-embed its lower sphere repeatedly.

The isolated body now uses OverlapCapsule and ComputePenetration at the fake
pose, resolves the minimum translation, then sweeps remaining motion through
contact planes. Only inward physical velocity is removed; controller input is
unchanged and outward/tangential velocity survives. Queries use the complete
native capsule geometry (including rotated axis), native contactOffset and the
mob Rigidbody's solverIterations. Overflow queries retry with complete arrays.
Native walking gravity stays enabled. The flat-foot snap and radius shrink are
removed. No authoritative root, Rigidbody or scene collider is moved by queries.
ComputePenetration accepts explicit poses without moving scene colliders:
https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/physics/computepenetration

Analytic capsule/plane regressions reproduce the zero-distance opposing-normal
failure, then cover 400 slope-contact steps, floor/wall corners, real wall stops,
outward separation and takeoff. Emitted checks require overlap/penetration
wiring and forbid the former flat-foot snap. These are numerical contact tests,
not an executed Unity physics world. Rigidbody friction, dynamic-body impulses,
terrain seams and live start-motion quality remain in-game validation work.

### Native attack-selection and start-motion follow-up (2026-10-28)

Bridge protocol 4/checkpoint 3 adds every inventory weapon cooldown, unarmed
fallback cooldown, selected weapon and native candidate ordering. Destination
rebases ages to its game clock and restores equipment without equip effects.
Different weapon identities/counts fail explicitly instead of silently binding
the equipped weapon's age to a different attack. Ordinary mob templates must
provide the same candidate inventory on both peers.

The preview copies native `MonsterAI.SelectBestAttack` and
`Humanoid.EquipBestWeapon` IL. Only equipment mutation, inventory views and
temporary candidate buffers are replaced with transaction-local state. Range,
minimum range, priorities, angle checks, cooldowns, eligibility and native
Random.Range calls stay in that copied graph. Native foreach try/finally
regions are preserved; catch/filter/fault regions remain rejected. Friend
queries now follow native iteration/range rules at the fake mob position,
instead of aborting friend/healing attacks.

Fake own-actor InAttack is isolated from newer replicated animations. Matching
starts at an attack-free source boundary and freezes at its own attack decision.
Body attack factors are captured at that boundary rather than re-read from
future owner animations. Attack age advances by elapsed physical game time,
not 50ms per AI call. Stagger/other live environment inputs and future Animator
root motion remain limitations; this is not a full second Unity physics world.

Near-horizon clock matching no longer briefly sets fake playback to zero.
Observed destination progress still gates takeover; only attack/unsupported
action barriers pause the fake. Two early motion samples per active preview
report fake/body progress, displayed displacement, steering, desired speed,
velocity, CanMove and blocking contact. No new per-mob idle work is introduced.

Latest pasted log (attachment 76d7bdf1-a875-4812-93ec-2f100d7c6479) contains
29 bridge starts and 3 attack barriers, including one at fake time zero.
It records fake flyingslam versus real land, and fake claw versus real takeoff.
Other apparent start freezes cannot be proven from that log's pose-free clock
messages. The new motion samples distinguish zero steering, animation gates,
collision blockage and a stationary display; no universal live fix is claimed.

Weapon tests execute native and copied selection against declared Unity
clock/pose/random-sequence, attack-state, health/altitude and equipment seams.
They check selection, random-call count, untouched real equipment/cooldowns,
candidate order and cooldown rebasing. Separate fork tests exercise finally on
early return and exception. Live Unity physics, animation and multiplayer
still need an in-game test.

### Dynamic matching and hitbox follow-up (2026-10-28)

The fake body queries `Floating.GetLiquidLevel` at its own position with native
wave factor 1 and `LiquidType.All`. It no longer reads `Character` liquid-contact
cache: that cache belongs to the replicated root and disabling its capsule can
clear it through native water-trigger exit. Swimming decisions and buoyancy use
the same per-step sample. Native water/tar query contracts were inspected; wave,
shoreline and trigger behavior still require in-game physics validation.

Game-time progress and AI pass progress are separate clocks. The pre-handoff
cache contains game-time rate and expected AI-per-game-time rate (from the peer's
smoothed frame interval); live paired packets refine both. Movement/position
matching uses elapsed native game time since checkpoint restoration. AI-dt
accumulation only measures decision cadence. Under 80ms frames with 50ms AI dt,
body progress .16s and AI progress .10s remain distinct; the fake schedules
native AI calls accordingly while body integration stays continuous. Attack
release requires the physical horizon, completed decision index and an actual
native attack marker. No 50ms duration is added to a physical-time boundary.

The initial paired pose is sent when applying the checkpoint, outside the
per-mob RNG scope. All network traffic stays outside native-AI random save/restore.


Completion sends that throw are retried through the existing active maintenance
path. Completion before instance loading still applies the native AI checkpoint;
only matching hooks are skipped. Committed pending checkpoints do not expire
merely because instance loading exceeds initial L. Stage-one completion removes
matching clocks/RNG/target hooks immediately at the receiving endpoint; ordinary
world updates own the remaining source-side visual correction. Proxy dictionary
cleanup also handles already-destroyed Unity objects.

The fake decision cadence is continuous for the active session; the legacy
bounded cadence remains unchanged. Active matching epochs end on a source
completion RPC, not initial L. Per-mob Unity RNG is saved/restored around each
matching AI pass; other game RNG is not locked. Destination applies the end when
that reliable message arrives, so cleanup necessarily includes its transport
latency. Ownership revision, token and source/destination fence completion; peer
loss, actor destruction and session shutdown retire obsolete epochs.

Protocol 3 clock messages carry sampled owner position/rotation/velocity and a
real successful attack marker. A zero-time sample at checkpoint application precedes the first AI pass.
Duplicate/old samples cannot refresh the frontier. Attack barriers require the completed
native AI pass index and an observed attack; they do not start a fake
attack. Paired pose history supplies matching-time diagnostics. The replicated
root is still used only as the moving endpoint of the subsequent visual
correction, not as proof of simulation agreement. A mismatched attack animation
is reported and presentation resumes at the observed native attack frontier.

Fake native cooldown ages now advance from the captured source game-time epoch,
without adding network delay to ages already rebased at destination restore.
Remote target prediction uses the sample's elapsed phase, route estimate and
simulation horizon (wall-route seconds are converted using the destination game-time rate); local test targets remain forward-predicted from the cutoff snapshot. Their
current live position is not advanced again by fake progress, and no position
history is replayed. New observed direction changes steer that forecast smoothly. Matching is
not a proof of identical future player input or Unity collision outcomes.

The local-player/server test still includes movement latency. With transfer
delay dT, target movement delay dM and destination elapsed wall time s, the
server approximately sees source input from dT + s - dM. Equal one-way delays
cancel to s, so the cutoff-based local forecast does not assume zero movement
latency. Packet cadence, interpolation and asymmetric delays add uncertainty.
Server-to-player starts no old-server fake presentation; the destination
applies the native checkpoint and runs real simulation.

Unity returns empty bounds for disabled capsules. Fake AI now computes centers
from configured capsule geometry instead. HUD preparation is once per Unity frame and runs before EnemyHud projection,
not only in LateUpdate. Clock-zero samples initialize pose history but not CPU
rate measurement, avoiding a spurious first-tick rate increase.

The hit proxy is an independent
kinematic Rigidbody and uses native Projectile.FindHitObject routing back to the
mob. Rigidbody pose setters keep hit queries current without global physics
synchronization. All HUD/hit/render consumers read one cached presentation pose.
Dispose disables the proxy before deferred destruction. A rapid owner return
restores the native physical collider; a new bridge inherits the prior displayed
pose rather than retaining duplicate tracks or resetting to its hidden root.

Slope normals and a saturated NonAlloc query no longer abort movement time;
queries retry with a complete result array only on overflow. Swimming deceleration,
gravity, native attack movement/turn factors and captured root-motion residue
follow native rules more closely. Root motion is included in the portable
checkpoint. Ongoing future Animator root motion is not claimed to be predicted;
knockback in the fake body remains intentionally omitted as requested.

Live fake progress uses Unity's native frame delta, so a local wall-time stall
cannot produce a catch-up burst. Active correction no longer expires at 4L+1;
its base duration follows one quarter of the actual stretched phase, extended
for substantial pose differences. Actor deletion/session teardown still dispose
presentation objects. No live Unity multiplayer/render quality guarantee.


The 2026-10-28 client-log follow-up fixes two independently reproduced gaps:
unobserved destination zero released four zero-time attacks; repeated presentation
reads could bypass the takeover direction guard when the replica root stayed
unchanged. Attack release now requires an observed matching clock (no attack
wall-time release). Position correction advances in the update path; presentation
reads are side-effect-free and the last observed movement direction survives
unchanged-root updates. Regression fixtures cover zero-time/no-clock/late-clock
attacks, repeated readers, stale/duplicate updates and real-root catch-up. Emitted
DLL checks verify observation wiring and read/update separation. Existing track
invalidation/expiry remains the lifecycle fallback; this does not guarantee a
matched pose after actor removal or clock loss through the entire track lifetime.
The reported metre-scale residuals remain a separate simulation/replication
investigation. Live visual improvement requires a new in-game run.

The `.94` branch starts from the verified `.86` source and replaces the old
preview architecture with an event-triggered old-owner bridge:

- continuous fake playback at base `0.5x`;
- native `MonsterAI` decisions in an isolated transaction;
- 62-field portable AI/control/root-motion checkpoint;
- target ID and deterministic per-handoff RNG seed;
- transient simulation-clock heartbeats and bounded CPU-rate estimation;
- monotone post-takeover position/yaw alignment;
- no permanent idle `MonsterAI.UpdateAI` hook.

The full debug verifier passes for the final status/FX/hit pass. The release
DLL omits the entire bridge implementation. Debug DLL SHA256 is recorded in
`verification.json`.

An earlier verifier run failed in `ConfigProbe` with
`UnauthorizedAccessException`. Isolation showed ordinary write/delete/move
worked, while Windows `File.Replace` was denied even with no Valheim process.
The failure was caused by the execution sandbox; the same verifier passed
outside that sandbox.

Headless fixtures do not execute live Unity multiplayer, Animator, physics or
transport timing. Fake attack visualization remains an explicit unresolved
policy item.

The final 50/500 ms timing pass adds authoritative-clock takeover, a
`L + 250 ms` clock-loss fallback, adaptive takeover settle duration and complete
integration of partial fixed steps. The mob-bridge timeline/takeover suite and
emitted DLL audit pass after these changes. A full verifier receipt was
generated for the final combat-decision parity build.

The final pacing correction evaluates fake AI on simulation time and renders
interpolated position/rotation between fixed pseudo-physics steps. A fixed step
changes simulation command/state, but never directly changes the rendered pose
at a frame boundary.

The target-input correction predicts a fast direction change by integrating a
smoothed measured velocity that turns toward the target's current `GetMoveDir`.
The target collider center is captured in target-local space and transformed by
the same predicted root and rotation. Fixtures assert a monotone turn with no
backward predicted-position jump. The native target-search timer remains part of
the transferred checkpoint and is decremented by the fake native `UpdateAI`
cadence; the handoff target stays pinned while the expected new owner remains
valid.

The combat-decision parity pass transfers 44 audited AI fields and 13 control
fields (portable schema hash `774526275`). Native absolute-time fields are
encoded relative to the destination, fake native code reads destination-
predicted game time instead of old-owner wall `Time`, and the attack gate copies
`CanUseAttack`. Attack age, current-weapon cooldown, weapon-review timer,
flight/water/lava state and the target-search timer are preserved. Shadow AI
uses the same Unity `Random.Range` and `Random.value` calls from the shared
per-handoff state; the state protocol is version 2 and requires matching
updated endpoints.

At the first fake attack request, the old-owner simulation freezes at that exact
simulation frontier and consumes no later movement. Destination clock
heartbeats use the native `0.05 s` AI cadence; the visual takeover begins when
the destination reports the same frontier. A one-time bounded fallback handles
clock loss. Destination target and RNG epochs are counted in simulation `dt`,
not wall time: both lock for `L`, then vanilla target selection and RNG resume.
The final bridge fixture contains 157 timeline/takeover assertions and 244
emitted wiring/no-live-mutation assertions.

The rewind audit rejects wall-clock and extrapolated-future takeover
completion. A live bridge now takes over only from the latest observed
destination simulation frontier. Rate extrapolation remains useful diagnostic
and clock-loss context, but cannot start takeover early when latency suddenly
improves. Post-takeover correction decays a relative fake/real position and
rotation offset around the moving real root. This keeps real forward velocity
during correction instead of blending two absolute poses, which could visually
stop or reverse while the real root advanced.

The target-lock Harmony prefix now uses the exact native `UpdateTarget`
parameter names: `canHearTarget` and `canSeeTarget`. The emitted wiring audit
checks all five prefix parameter names. AI hook installation is transactional:
both `UpdateAI` and `UpdateTarget` patches must succeed, otherwise the candidate
Harmony instance is unpatched and the apply remains retryable.

Runtime teleport diagnosis found zero-second presentation timing poisoning the
visual window. Timing samples below `0.02 s` per half-route are rejected, the
active solo-test RTT supplies the initial route duration, and cached windows
below `0.04 s` fall back to a valid estimate. Fake `MobBridgeBody` now follows
`SteeringFrame.Flying`; takeoff/landing changes ground/fly speeds and gravity
mode instead of leaving the body in its initial locomotion mode. The final
fixtures contain 463 timeline/takeover assertions, 286 emitted wiring/no-live-
mutation assertions and 41 production source-handoff assertions.

The locomotion parity audit also found that Run outranked Walk, near-zero move
input could still sprint, `CanMove` was ignored by the fake body, flying speed
incorrectly received walking status-effect movement modifiers, and takeover
correction duration depended only on latency. Fake locomotion now mirrors native
mode selection, and residual correction is bounded by configured mob movement
and turn rates using the same smoothstep curve.

Fake collision response now projects velocity/control direction onto the
collision plane and uses the native character/vehicle collision layers.
Previously the first blocking hit zeroed all velocity, while the real
Rigidbody slides along the contact. That divergence was especially visible for
flying Seekers near terrain and obstacles.

Takeover position correction is also monotone relative to current real-root
motion. If residual decay would move the display backward while the real root
advances, the residual remains held until the real root catches the displayed
projection. This prevents the final cleanup from looking like a teleport.

Swimming now uses the native swim depth threshold and swim timer, native swim
speed/acceleration/turn speed, status modifiers and the same water-level
buoyancy targets as `Character.UpdateSwimming`. Temporary vertical dips below
the water surface no longer fall through the ground-mode gravity path.

The status audit found that `MobCombatState.CompleteTransfer` relinquishes the
old owner's live `SEMan` immediately after commit, while the fake simulation
continues afterward. The Bridge now captures the same status payload before
commit and clones it into a transaction-local shadow. Fake AI/body reads
temporarily swap in that shadow, advance only effect time/speed state and then
restore the live manager. This preserves slows and expiry during the fake
window without source-side DoT damage, setup effects or gameplay writes.

Melee/projectile routing previously queried the replicated root collider while
the model was drawn at the fake pose. Bridge tracks now create a collider on the
native dedicated `hitbox` layer at the displayed pose and temporarily disable
the hidden replicated collider. `Character.Damage` rejects a hit carrying the
hidden collider and allows the fake-pose collider; `HitData`, status hash,
damage/effect forwarding and one-shot attack FX continue through vanilla paths.
The original collider is restored on takeover/cleanup.

The nameplate audit inspected vanilla `EnemyHud.UpdateHuds`: it is screen-space
and calls `Character.GetTopPoint`, not a transform parented under the visible
model. Moving the actor root would therefore be unnecessarily invasive. The
bridge instead redirects `GetTopPoint` only inside `EnemyHud.UpdateHuds`, using
the fake root, scaled collider center and configured visual-top height. The
authoritative root, Rigidbody, ZSyncTransform, physics and network writes remain
untouched. A native Mono audit installs the actual `Character.Damage`,
`EnemyHud.UpdateHuds` and `Character.GetTopPoint` patches and verifies all three
are removed on shutdown. This audit also caught and corrected an existing
`Character.Damage` prefix bug (`__1` used for a one-argument method).

The 500 ms teleport log contained one second-handoff window of `0.079 s`,
followed by normal approximately `0.5 s` windows. Root cause was the existing
five-second per-destination presentation timing cache: server-to-client timing
from before the new latency test could survive while client-to-server timing
was already fresh. Artificial-latency start, stop and reset now clear all
presentation timing entries on both client and server. Fixtures assert that an
invalidated cache returns the current fallback instead of stale pre-test RTT.

Latency-change fixtures cover both directions. With a cached `500 ms` window
but actual first state at `100 ms`, fake is `0.05 s`, observed destination is
already `0.08 s`, and takeover is immediate at that observed horizon. With a
cached `100 ms` window but actual first state at `500 ms`, fake continues past
the old `0.10 s` horizon to `0.25 s` instead of freezing; observed destination
moves `0.05 -> 0.25 -> 0.45 s` and takeover occurs at `0.45 s` when fake also
reaches `0.45 s`.

Fresh focused results: 478 bridge timeline/takeover assertions, 306 emitted
bridge wiring/no-live-mutation assertions, 42 source assertions, 454 native
source contracts, 89 status-state assertions, 2498
native status contracts, 115 combat-state assertions and 930 native combat
contracts all pass, plus five actual game presentation hook inspections. Source,
observer, solo-latency and flight suites also pass. These remain headless
transport/physics/Mono checks; live fake-pose hits, HUD placement and FX
continuity still require two-endpoint testing.

Baseline: hash-verified 1.0.85 (`94EDCFADE44059322930873CCDEB1521276AFF056896611DD2E67F269C62590E`). The verifier restricts the production delta and records fresh suite/source/DLL/artifact hashes. Local debug only; no publication, Git push or installed-profile changes.

The combat-state native installation audit uses a disposable game assembly with Character/Player/MonsterAI static initializers omitted because Unity Animator calls are unavailable in the headless host; patched method bodies and field layouts remain native.

Source fixtures execute the actual no-grant protocol and native publication on CLR and Valheim Mono: source-time-zero cutoff, begin/checkpoint/owner/final-state order, unfinished-action deferral, refusal/cancellation, postcommit exceptions without rollback, authenticated source/target/prefab/revision fences, replay and expiry, watch rebinding during native receipt, and exact one-object native serialization. Presentation timing tests cover route RTT sums, cold/expired fallback, five-second cache expiry, stale/foreign/nonfinite replies and session reset without delaying authority. Native audits inspect real Plugin/Observe/attack-end wiring and install/remove actual lifecycle hooks. Protocol authentication is retained; server target-policy approval is absent.

Observer fixtures execute the production descriptor/preview transport and visual bridge: committed owner/data cutoffs, source/new-owner exclusions, capabilities, delayed actor/data arrival, stale/newer state, bounds/expiry/world reset, actual flight mode, flying turn factor, throttled subsequent preview frames, duplicate/foreign-source suppression, local render adoption without native owner/position writes, and finite lifetime. Native audits install the real common transfer/connection/replication/Awake hooks.

Visual fixtures exercise zero initial target lead, progressive latency-sized compensation, smooth monotonic weighting, gradual cached-to-current RTT changes, non-rewinding preview time on lower RTT, frozen recovery windows after native arrival, local-player exclusion, rejection of invalid velocity, target identity changes/loss, ground/flight changes, signed turns, flight-factor easing, finite recovery, render restoration and no world/physics/AI writes. Numerical trajectories remain models, not live Unity observations. Common combat checkpoints restore the target and remaining native search timer before the first AI step, including a regression that confirms it is applied only once.

The shortcut deliberately skips server ping/boss/loading approval. The existing destination guards and subsequent server policy still apply; a loading destination can pause, or the server can later correct the direct target-owner choice. There is no guarantee that a second AI would choose identical movement. Existing five-second checkpoint/event expiry and bounded buffers remain limits during extreme stalls/disconnects.

Immediate complete native state can duplicate a later scheduled record. Spectator descriptors and up-to-20Hz short preview frames add traffic; they do not replace new-owner world updates. Empty queues return immediately, source previews are bounded by active local render tracks, and no extra global mob/player scan is added. No measured CPU/FPS/bandwidth improvement is claimed. All applicable effect/combat/PUP/flight/player-only/grid/config/state-transfer suites are rerun before packaging. Real multiplayer and full third-party patch interactions remain untested.


---

# Historical baseline: active-effect handoff candidate 1.0.85

Baseline is the verified debug build `TooManyLags-1.0.84-mob-continuity` (SHA256 `55A7CA0654601BEB86D7938A85C82E7D82E985785E39AE8801DC8023CA10604B`). Its source and DLL hashes are checked before verification. The production delta is limited to the new MobStatusState codec, MobCombatState integration, the post-commit MobFlightHandoff callback, removed blanket effect vetoes in FgnCombatOwnership/TamedMobHandoff/PlayerSimulation.LocalAgent, and Plugin wiring/version. Movement forecasts, attacks, grid behavior and PUP collision protection retain the baseline implementation.

Active effects no longer hold the old owner until expiration. Checkpoints carry native scalar/struct runtime state and existing network-reference IDs. The destination creates shallow template clones, binds the new actor and restores state without Setup/ResetTime/SetLevel or processed-damage replay. The old manager detaches only after a confirmed native owner write with the expected revision. A refused native handoff leaves source effects intact. Empty snapshots remove stale effects from an earlier ownership epoch. Protocol 2 requires matching server/clients for complete migration.

## Focused validation

The new suite executes the actual production codec on CLR and embedded Valheim Mono. Cases include poison/fire unpaid budgets and tick phase, frost/shield state, initial healing/spawn suppression, repeated and empty handoffs, unloaded FX and repeated unresolved-ID transfers, network-reference ownership, expiry before FX arrival, real destruction before/after adoption, broken harpoons, displaced destination-local FX cleanup, world replacement, different template scalar defaults, malformed/bounded payloads and unsupported custom reference graphs. Reference identity is tested against native Unity template-clone equality so effects on different mobs do not share deferred state.

The actual emitted debug DLL is also audited against installed Valheim status types: every native field is classified; generated accessors and scalar/struct codecs round-trip values; Harmony hooks install/remove; migration methods contain no reward/damage/spawn replay calls. This status-specific native audit needs no rewritten native assemblies. The separate combat-handler hook audit retains its documented disposable static-initializer workaround described below.

Managed common-transport checks cover effects inside the real checkpoint, no source detach before commit, destination restoration before first damage, unchanged native refusal behavior, revision fences and duplicate late packets. They supplement rather than replace the codec suite. Final exact counts, fresh regression suites and artifact/source/DLL hashes are recorded in verification.json.

## Cost and limits

No new global mob polling, effect update stream or network acknowledgement. Cached schemas are built per effect type; value/reference snapshots are made at actual handoffs. No-effects checkpoints add one presence flag. Complete checkpoints are capped at 16 KiB. Deferred live bindings and ID-only cleanup records are bounded and cleared on world replacement/shutdown. Ordinary reference refreshes skip empty collections and reuse existing FX arrays; late cleanup is driven by matching scene arrival, rather than rescanning all cleanup IDs on every world packet. Actual native ZDO deletion events record recent IDs in a bounded 10-second/2,048-entry FIFO to distinguish deletion from loading, including the interval before checkpoint adoption. It uses no periodic scan; capacity overflow warns once per world and evicts the oldest ID.

Arbitrary mod-specific mutable object graphs are not claimed serializable. Source-detectable unsupported references fail visibly before ownership changes; destination templates/schemas must match. Missing destinations and existing five-second transport expiry remain limits. Native effect state tests do not execute live Unity effects, game physics or multiplayer timing. In-game repeated transfers under poison/fire/shields and ordinary target changes remain required. Local package only: no publication, Git push or profile install.

---

# Historical baseline: Mob continuity candidate 1.0.84

Baseline is the final debug-default `TooManyLags-1.0.81-diagnostic-cost` candidate, whose source/DLL receipt is hash-checked before verification. Separate optimistic object-handoff branches are not integrated. Local default build/package remains debug; a publication build requires explicit -Release. No publication, Git push or profile install.

Production delta is limited to MobCombatState, MobFlightHandoff's final cutoff callback, MobVisualCurve/MobHandoffVisual, FgnCombatOwnership's live-target selection and source/server guards, TamedMobHandoff/PlayerSimulation.LocalAgent guards, Plugin lifecycle/config/version and ConfigTemplate wording. The verifier rejects other production changes. Existing gameplay source outside those files remains baseline-identical.

## Presentation checks

The production curve is exercised under CLR and embedded Valheim Mono. Tests retain old root/physics/authority invariants, camera nesting/restoration, native-flight height, correction handling and per-mob timing. Added cases cover braking rather than constant burst velocity, a changed movement direction, ordinary recovery motion, a strict 500ms calculation cap and recovery bounds.

Modeled native ClientSync trajectories include steady motion, early non-pose data changes, ownership regain, an attack during recovery and missing render frames. A separate 8m/s-to-2m/s handoff model tests 50/100/200/500/1200ms RTT. Its recovery stays approximately 75–99% of native movement speed without a catch-up sprint; the 500ms and 1200ms display tracks both expire at the same approximately 1.242s sampled frame. This is a numerical regression scenario, not a measured game performance or universal motion guarantee. The production formula bounds normal prediction+recovery at 0.5+0.74 seconds; frame scheduling can end it on the following sampled frame.

Native visual audit installs/removes actual Harmony hooks and checks emitted gameplay calls remain absent from presentation. It does not run a camera, Animator or physics world.

## Combat checks

Managed tests execute the actual MobCombatState code with declared native/transport seams, on CLR and Valheim Mono. They cover source cutoff, exactly-once next-revision restoration, transient fields/no health replay, first-step movement/turn accumulators separate from pushback, active-effect refusal, early and stale damage/effect requests, local-attacker forwarding, native-prefix suppression, parry/heal routing, revision changes, cycles, duplicates, expiration and world replacement. New fields are not inferred from fabricated IL: native field/signature/Harmony audits inspect the installed Valheim/FGN binaries. Local Character.cs lines 1751-1775 and 1838 demonstrate the independent walking/turn accumulators; UpdatePushback decays the separately restored impulse in the normal first physics step.

A real nested Harmony damage prefix also verifies suppression survives an inner late hit. A disposable negative control with the previous single-scope cleanup fails that assertion; restoring the outer scope passes. The scope is a stack value, with no new per-hit heap allocation. Separate assertions keep sent/delivered deduplication entries out of the server's world-packet work queue while returned, undelivered and expiring events remain tracked.

Some native Character/Player static initializers require Unity internal calls unavailable in an embedded headless Mono host. The combat-state native hook test uses a disposable native-assembly copy omitting only those static initializer bodies; actual hooked instance method bodies, fields and signatures are preserved. Its limits are printed with the results. It is a method-installation/contract test, not execution of live native gameplay.

The source guard prevents transferring arbitrary active StatusEffect objects: copying their numeric timers alone would lose or duplicate spawned effect objects and Setup/ResetTime actions. Current owner AI continues; this is a handoff restriction, not a pause. Checkpoints are negotiated and bounded. Five-second event expiry is a practical limit, not a promise to preserve messages after indefinitely unavailable/disconnected destinations.

Existing combat, PUP readiness, flight, solo network-delay, player-only, config, camera, full grid protocol and state-transfer suites are rerun for the selected debug build. The receipt records exact fresh tests and source/artifact/DLL hashes. The separate normal/debug omission audit is only executed by an explicit release verification, with the continuity file types allowlisted as changed and unrelated native/variant bodies still compared.

## Live test limit

No live Unity multiplayer test was performed. A matching updated server/client is required to test the complete checkpoint protocol. Unknown/mixed endpoints retain native fallback. Arbitrary mod-specific RPCs, private AI state and reproducibility of a second AI simulation are outside this change. Test `tml_ai_test start 100`, `200`, `500` and `800`, plus an unforced multi-player target change, hits/parries/heals and ongoing poison/burning; compare the visual switch on/off.

---

# Historical baseline notes
# Development builds default to debug

Build/verification/package scripts now default to the debug development variant. Only an explicit -Release workflow builds, checks and packages a non-debug publication variant. Existing game C# sources, version and gameplay behavior are unchanged. Both output paths remain separate. Build policy checks inspect the emitted debug DLL, receipt, parameter defaults and invalid flag rejection.

Default verification runs applicable regression suites against the debug DLL; it neither rebuilds nor certifies the non-debug DLL. Release verification additionally executes normal-build omission/error-output checks and marks releaseVerified=true. A development receipt marks debugSystem=true/releaseVerified=false and is rejected by package.ps1 -Release and publish.ps1. The publisher still verifies the packed DLL hash and manifest; ValidateOnly stops before any network or token use. Default package/loose DLL aliases use debug; explicit debug-system ZIP is retained.

This changes local tooling/documentation only; no new changelog gameplay entry, release version, profile install, publication or push. Earlier sections describe the already verified gameplay changes and the former normal-build default as historical context.

---

# Diagnostic-cost candidate 1.0.81

Local successor to the hash-verified 1.0.80 normal/debug build. No publication, profile install or Git push. Historical sections below refer to earlier candidates.

Normal production omits the three FgnTargetBoundary statistic fields and their updates, FgnBossPolicy.Evaluations and both target-event modules' patch-coverage counters. The optional debug variant and standalone managed regression fixtures retain these metrics. No normal-build runtime flag/stub/metric callback is added. Actual intents, holds, expiry/token/owner revisions, boss decisions, queuedCount, attack retries and native-contract validation are preserved.

NativeNode.Requests is now a read-only view of RequestRouter's actual matching queue. The duplicate per-node Int32 field and its enqueue/dequeue/expiry/cancellation updates are removed in both builds. Only explicit status queries/tests call the getter. Routing still uses the same queues, expiry/replies, order, destination readiness and native dispatch. Queue lookup checks object identity, so a reused ZDO ID cannot expose another node's work. No claim of an exact object-size saving: alignment can absorb the removed field.

Warning/error sinks and previously log-gated catch reports are unconditional while the plugin exists. Runtime grid failures and failed native interaction reports no longer depend on EnableLogs. Optional info/status scans, formatting and diagnostic RPC registration remain gated and on demand. No additional error RPC, remote telemetry, polling or network stream is introduced. Unsupported-transport/fallback paths which deliberately catch without reporting remain native behavior; a normal refusal is not converted into an error.

The obsolete PlayerSimulation.Settings.Logs field/read/live assignment is removed: errors use the central always-on sink and optional diagnostics use the existing root flag. EnableLogs remains configurable locally and live. Config descriptions/wiki now state that errors and warnings are always reported.

## Verification

verification.json records each fresh suite. The compiled normal/debug audit asserts metric fields absent only in normal output, solo/latency code still absent, unchanged method bodies exactly match .80, and unrelated normal/debug bodies match. Explicit changed-method allowlist is limited to queue diagnostics, optional-statistic compile boundaries, error logging and config text. Flight checkpoint, visual curve, target/attack handoff decisions and movement implementations remain byte-identical in source to .80 except pure statistics/error reporting.

DiagnosticLoggingAudit copies actual emitted Plugin.Warn/RuntimeFault/RequestFault bodies into a managed harness, replacing only Unity object liveness and BepInEx logger sinks. It runs them with diagnostic logs off/on, checks message content, null/destroyed lifetime safety and verifies .80 as a negative control (old errors are suppressed with logs off). This tests real compiled branches, not a restatement of logging logic. It runs on CLR and embedded Valheim Mono. Metadata/source checks reject lingering active metric fields, the per-node request-count backing field and optional-log gates around error calls.

Both ordinary integration and GridProtocol initial-load suites run, covering actual deferred request queues, native delivery/refusal/expiry/cleanup, inventory, harvest loading and initial readiness. Normal/debug combat/attack/boss/PUP, player-simulation, flight/visual, config, camera and network state-transfer suites also run. The existing PUP Stopwatch budget remains: it limits native preparation work rather than collecting debug statistics.

No live multiplayer or CPU benchmark. Expected saving is small event-driven statistic bookkeeping and one obsolete per-node field; no claim of higher FPS, lower ping or changed handoff timing. Always-on error logging intentionally still costs formatting/output when an error actually occurs.

---

# Flight handoff candidate 1.0.80

Based on hash-verified normal/debug 1.0.79; published=false, installed profiles unchanged. Historical sections below describe earlier candidates and are retained as history.

## Flight checkpoint and native contracts

MobFlightHandoff wraps only the final guarded FGN OnHandOff owner write, plus the existing direct server, tamed-grid and player-simulation source commits. Native vetoes, destination readiness, attack boundaries and late damage forwarding remain. A transient negotiated RPC precedes the final owner record; a server relays client-to-client checkpoints before the later ownership record. No final acknowledgement or added handoff wait is introduced. Legacy peers get native handoffs.

The checkpoint carries runtime flying/ground mode, native random-flight phase, body velocity, movement accumulator/direction/run and turn accumulators. Native circle/wander destinations carry remaining durations, rebased to the recipient clock; actual fresh target choices discard old movement goals. Mismatched goals wait for matching native target adoption instead of overriding targets. Restoring does not call TakeOff/Land, animate attacks, change gravity, unfreeze bodies or bypass PUP. Kinematic bodies retain their native velocity policy.

Apply once after the complete native RPC_ZDOData owner/deserialize pass, or after BaseAI.Awake for a late native instance. Match identity, prefab, source/destination ownership epoch, authenticated connection and negotiated capability. Drop stale/expired/superseded packets. Pending state is capped at 256 IDs, expires after five seconds on packet/instance processing, and clears on world/network replacement or shutdown; weak deferred goals do not retain AI instances. No new per-frame poll, idle AI scan or persistent ZDO key.

Managed wire encoding measures 86 bytes without movement goals, up to 118 with both. Add 12 bytes for the separate object ID plus RPC/package/transport framing per leg. Client-to-client relay uses two legs. These are payload sizes, not measured physical-link traffic.

Native sources: installed assembly_valheim.dll; FiresGhettoNetworking 1.5.72; verified native JotunWitch prefab audit in ../JotunWitchHandoffAudit-2026-10-25. Character.UpdateFlying consumes m_moveDir/m_currentVel; BaseAI.UpdateTakeoffLanding uses runtime m_flying/m_randomFlyTimer. FGN's owner-gain pre-snap retains native position/rotation ownership and is not replaced.

## Presentation

For flying/random-flight actors the native 3D trajectory supplies height. Existing horizontal lead and bounded turn preview remain; fly turn speed uses m_flyTurnSpeed. A separate residual reconciles only excess native Y correction, allowing observed velocity and one physics step of slack, bounded at 25cm and by the existing handoff lifetime. Correction deltas never become velocity; no artificial second vertical arc is generated on a stationary native root. Native root, collision, animation time and gameplay state are unchanged by rendering. Continuous native climb/descent/reversal remains the baseline, rather than damping every valid vertical motion.

## Validation scope

Fresh suites are recorded in verification.json: normal/debug build omission and exact unrelated method comparison to .79; combat/Mono/PUP; debug latency; flight managed/embedded-Mono wire and lifecycle tests; native field delegates/Harmony install/uninstall; visual trajectories/native hooks; player-simulation/grid/inventory/refill/text/loose-item integration; config/camera/state transfer. Test-only logic remains absent from the normal DLL. Changed gameplay methods have explicit audit allowlists plus functional tests; unrelated compiled methods must match .79.

Flight fixtures exercise guarded source commit, server/client relay, timing/revision, native adoption, phase/mode, kinematic holds, duplicate/stale/malformed data, late instances, transport failure and cleanup. Visual fixtures include 50/100/200ms continuous rise/fall/stationary trajectories, correction bounds and finite cleanup, alongside the unchanged horizontal trajectories. Fixtures model transport/native transforms and use yaw math; they are not real Unity animation, collision, full 3D camera or live Steam tests.

Not claimed: visually perfect or lossless transitions for every creature/mod; migration of all private damage/status-effect runtime fields; cross-transport ordering guarantees under arbitrary third-party packet reordering. This change specifically carries flight continuity and removes the reproduced extra vertical excursion. A real JotunWitch 100/200ms solo test remains necessary.

---

# Compile-time debug system 1.0.79

Local candidate based on the hash-verified .78 build. `build.ps1` defaults to **DebugSystem off**. Only `-DebugSystem` defines `TML_DEBUG_SYSTEM`; its DLL is written to `bin/debug-system`, independently of the normal `bin/TooManyLags.dll`. Both variants retain `TML_RELEASE`, optimization and warnings-as-errors.

The normal DLL entirely omits FgnSoloCombatTest, SoloNetworkLatency and their nested state/closure types, Plugin.TestLog, SoloEligible/SoloPrepare/HoldForSolo, the synthetic boundary flag, six test RPCs, capability probes, per-frame test polls and selected-mob PUP tracing/postfix. There is no runtime disabled stub, bool check or empty test callback. EnableLogs and normal owner/status diagnostics are independent and retained. The actual target/attack boundary, handoff protocol, visual bridge, tamed-animal guards and PUP collision preparation/readiness remain.

The optional debug build retains .78's existing test behavior, including start 100/200. Mixed normal/debug endpoints cannot negotiate test capability; normal gameplay uses the unchanged protocol. A build option cannot be toggled by editing a live config: replace the DLL and restart. The two variants share plugin identity/version and must never be installed together.

## Verification and packaging

`test-debug-build.ps1` inspects the emitted assemblies with Mono.Cecil, including all nested types, metadata references, method signatures, fields and instruction operands. It rejects test modules/commands/RPCs/polling/tracing in normal output, checks that only explicitly named test helpers/state disappear, compares every debug method body to verified .78, and compares unchanged normal/debug method bodies. Compiler-generated constant-data identifiers are normalized by array bytes; branch targets are compared by instruction index. The .78 version string alone is normalized to .79.

Combat fixtures/native API audits run in both modes, also on embedded Valheim Mono. PUP fixtures exercise normal collision priority, near/distant actors, native refusals, budget and lifecycle in both modes; only debug fixtures retain selected-mob trace assertions. Debug-only socket/latency hooks and queue tests, normal visual fixtures/native hooks, config migration, camera recovery, grid integration and state-transfer checks run again. Other byte-identical production behavior uses the verified .78 source-hash baseline; the receipt identifies every fresh suite and does not claim a fresh run of all historical tests.

`verification.json` binds both DLL hashes, build modes, production source hashes and current script/test/document hashes. Packaging checks normal and debug mode receipts and each DLL hash; the normal install archive and separate debug-system archive each receive the matching verified DLL. The ordinary publication helper accepts only the normal receipt hash. Source ZIP includes scripts/tests needed to reproduce both modes. Existing profile DLLs and Git remotes are untouched.

No live Unity/Steam session or in-game visual comparison was run for this change. This build switch removes test-system work; it does not remove gameplay guards, improve a network link or resolve the separately documented low-ping player-to-player combat policy question.

## Historical .78 validation (debug build only where test-specific)

# Solo connection latency test 1.0.78

This local candidate starts from the verified 1.0.77 combined build. Production changes are restricted to the new SoloNetworkLatency.cs, the optional FgnSoloCombatTest control/setup path, and plugin version/poll integration. The .77 visual curve, combat/source transaction, actual ownership guards, grid, inventory, loading and PUP implementations are byte-identical. No new persistent config or default change is introduced. Not published or installed into a live profile.

## Scope and timing

`tml_ai_test start 100` means target 100ms round trip, not 100ms in each direction. At setup the client reads the concrete transport RTT; the nonnegative difference to the target is split equally between outbound and inbound ZRpc game-message delay. An existing RTT above the target cannot be reduced. Timing follows the client's monotonic clock and frame pump; frame waits and later physical RTT variation are additional. No cross-machine timestamp comparison is made.

The client retains the original concrete ISocket objects. A reviewed one-call substitution in ZRpc.SendPackage snapshots raw outbound packets before the normal socket/compression chain; the two native receive call sites in ZRpc.Update collect already decoded incoming messages. Delayed packets retain bytes, reader position and FIFO order. Each concrete native send/receive path executes once. The concrete quality postfix applies only to the captured client socket and exposes physical RTT plus injected delay to the existing visual bridge. Other connections stay native. This is application-layer delay, not actual Steam RTT manipulation, bandwidth/loss emulation or a guarantee of exactly 100ms observed latency.

Latency hooks exist only during the explicitly started test and drain. The server requires its native admin check, exactly one player, matching updated endpoint capability, active Server-Side Simulation, a nearby eligible mob and a matching setup acknowledgement before arming any transfer. Repeated/wrong acknowledgements do not arm another request. Basic `start` remains native-timed and compatible with the earlier readiness protocol. Active FGN NetSim on either endpoint refuses setup rather than stacking another hidden delay. Do not enable fgn_netsim during this test.

Normal stop/expiry stops holding messages and drains accepted outgoing/incoming data before later native data; there is no forced owner reset. Disconnect, changed world, changed peer/socket and invalid session clear only stale-session queues. Existing five-minute, second-player and distance/death termination still apply. A 32MiB/8192-message bound aborts artificial holding and drains accepted traffic in order. There are no sleeps, global network setting changes, permanent per-mob queues or extra ownership writes. When unused, latency polling is one null check and no latency Harmony hooks exist.

## Verification

The production network module runs with real Harmony against controlled clocks/sockets on CLR and Valheim Mono. Assertions cover 20ms base + 40ms each direction, 0ms base + 50ms each direction, a higher native RTT, real packet waiting, RPC scratch-buffer mutation, reader position, ordering, one encode/decode per packet, other-socket isolation, ordinary stop/drain, disconnect/world/peer cleanup, timeout, over-limit abort and native restoration. Actual game ZRpc send/receive and Steam quality hooks are installed/JIT-compiled/uninstalled under Valheim Mono; native call-site counts and authority/blocking/global-setting exclusions are audited.

The existing production solo/combat fixture additionally checks capability/admin/single-player admission, delayed setup before first arm, duplicate/stale acknowledgements, setup failure/timeout, stop cleanup, out-of-range targets and both native FGN NetSim conflicts. verify.ps1 rebuilds the real addon, generates/reloads BepInEx config and reruns the relevant latency, combat/Mono/readiness, visual, startup, grid/protocol integration and state-transfer suites. Exact outputs/hashes are in verification.json. Remaining unchanged production/tests retain the hash-verified .77 baseline; no fresh run of every unchanged suite is claimed.

No live Unity world, Steam/PlayFab session, gameplay at a measured 100ms, complete third-party Harmony graph or visual quality comparison was executed. Client/server installation and in-game comparison remain required. This diagnostic does not improve the ownership algorithm or claim to reproduce jitter, loss, congestion or server overload.

## Earlier .77 validation (retained functionality)

# Native-relative visual bridge test 1.0.77

This local candidate starts from the verified 1.0.76 combined build. Only MobHandoffVisual.cs, MobVisualCurve.cs, configuration descriptions and plugin version metadata change in production. All grid, combat-boundary, state-transfer, inventory, tamed/PUP readiness and session-negotiation implementations are byte-identical to 1.0.76. EnableMobHandoffVisualBridge remains true by default; this is not a published release.

## Corrected presentation

- Native root extrapolation is the sole movement baseline, including transform updates between LateUpdate and camera rendering. A short slowing preview adds only motion/turn progress not already supplied natively. Real progress consumes this extra lead; it cannot hold the model behind an already moving root. Additional lead is bounded to 0.25m and 10 degrees.
- Recovery fades only the remaining render offset. No root-position derivative is interpreted as velocity, no spring momentum is injected by correction packets and no independent absolute preview path is pursued. This does not remove an actual native network correction or native owner-gain snap.
- Each handoff captures its own measured socket RTT. Its changed replica revision or RTT deadline starts recovery, using only this handoff's elapsed time. The preview window is 0.04-0.6s; finite recovery is 0.04-0.2s. There is no shared delay history, permanent per-mob timing cache or wait for a missing packet. A new owner/revision invalidates the old track.
- Capture no longer calls SyncNow: the native owner-gain branch of that method can move the root, making a previously captured pose stale. State publication remains in the unchanged native/source handoff path. The presentation module sends no RPCs or ZDO writes.
- Existing collider-free hierarchy checks, attack/use/epoch/world fences, exact render-pose restoration, nested cameras, 32-track bound and disabled-mode teardown remain. Unsupported hierarchies retain native rendering. No additional server simulation or per-frame traffic is introduced.

## Reproduction comparison

The six original trajectory scenarios run against the production module and a port of the actual game's non-kinematic free-root ClientSync math (50Hz physics, 60Hz rendering, simulated packet delivery). This is a deterministic managed model, not a live Unity/network benchmark.

| Scenario | 1.0.76 maximum visual frame step | 1.0.77 maximum visual frame step | Same native frame step |
|---|---:|---:|---:|
| Extrapolation without fresh state | 2.030m | 0.040m | 0.040m |
| Steady movement, 50ms packets | 0.042m | 0.042m | 0.042m |
| Attack during recovery | 0.294m | 0.084m | 0.084m |
| Ownership return during recovery | 0.544m | 0.272m | 0.272m |
| Early non-pose revision | 0.042m | 0.042m | 0.042m |
| One missing render frame | 0.119m | 0.084m | 0.084m |

Maximum preview/native separation across these scenarios drops from 1.99m to under 0.048m. The explicit 0.4m stationary-root correction no longer overshoots to 0.426m or produces backward recovery frames; it remains exactly the native 0.4m correction. A preceding mob's 800ms update wait no longer gives the next 50ms-RTT handoff a 600ms window: its window/recovery stay 50ms and it has ended before the next 500ms test request. Native turns and root corrections arriving after LateUpdate also retain priority.

The trajectory assertions enforce native-frame tolerance, finite completion, bounded offset and expected owner-write counts. Separate production fixtures cover bounds, guards, rendering lifecycle, callback integration and independent timing on CLR and Valheim Mono. The real game/FGN methods are inspected and the actual Harmony source hooks installed/uninstalled under embedded Valheim Mono. verify.ps1 reruns the combined suites and records exact outputs, DLL/source/artifact hashes and the unchanged 1.0.76 baseline in verification.json.

## Limits

No live Unity renderer, Animator, collision world, Steam session or full third-party patch graph was executed. This fixes the three demonstrated presentation defects; it does not promise every mob handoff is visually seamless. Native corrections remain visible, and attack/owner/world invalidation deliberately discards the small visual offset. It does not repair a stopped server AI or missing PUP collision geometry, migrate ongoing attack/root motion, or resolve the separate ordinary low-ping player-to-player FiresGhettoNetworking combat-exclusion path. In-game comparison remains required.

## Historical 1.0.76 implementation and validation (superseded presentation)

# Visual handoff bridge test 1.0.76

This local candidate merges the verified 1.0.73 mob branch with verified 1.0.75 (including 1.0.74 loose-item indexing) on their common 1.0.72 lineage. The existing source directories are preserved. Merge provenance is in visual-76-provenance.json. Changes to AuthorityController and NativeInventoryHandoff belong to those retained fixes; the presentation module adds no grid algorithm, ownership timer or PUP collision override.

## Presentation and traffic

- Server-controlled startup switch EnableMobHandoffVisualBridge, default true in this experimental package. The existing authenticated session endpoint gains negotiated version 2; legacy clients still receive unchanged version-1 flags. A new client on an old server retains gameplay and leaves the bridge off. Retries are bounded to 15 join attempts, never per mob or per frame traffic.
- Only accepted guarded source commits arm a track. Current/unfinished attacks, active use and native ownership/source checks stay in charge. Both FiresGhettoNetworking source commits and player-only LocalAgent commits use the same module. Final SyncNow is sent through the existing native state publication; no new per-handoff packet or ongoing predicted-state stream is introduced.
- The old player's separate Visual subtree must contain no collider, rigidbody or nested network view. Only its rendered pose changes, between camera callbacks; its saved local pose is restored. Simulation/root transforms, hit/event paths and Animator speed are not changed. The actual movement/look intent and native turn-speed fields supply a bounded prediction, not another AI decision.
- Initial timing uses the transport's measured round-trip ping in milliseconds, not ZRpc.GetTimeSinceLastPing (which is not RTT). A failed/unsupported accessor does not affect gameplay. Time from source commit to a changed replica data revision supplies observed end-to-end delay for recovery and later bridges. No client clocks are subtracted and the recipient's RTT is not fabricated.
- Prediction starts at half speed, exponentially slows, and is capped at 1.25m/60 degrees. Velocity-aware critically damped correction joins native movement. A first snapshot seeds correction without treating its positional jump as instantaneous velocity. Attacks/stagger/death, attachment/riding, owner/revision changes, teleports and world changes end the bridge.
- At most 32 local tracks, each at most two seconds; there is no permanent per-mob cache. Empty playback is a count check. No server rendering hooks are installed. With the switch off there are no capture hooks, player-only callbacks or camera subscriptions. Hierarchy/transport sampling occurs at a source handoff; camera subscriptions exist only while tracks remain.

## Verification and limits

The visual production fixtures pass 2,453 assertions on each managed runtime. The actual game/addon/FiresGhettoNetworking audit passes 556 contract/install assertions. Camera tests include nested reflection rendering, exact pose restoration and stopping while a camera is active. Existing source commits in player-only mode verify capture-before-owner-change, completion-after-change, protected refusals and duplicate suppression.

verify.ps1 builds against the installed game APIs, generates and reloads the actual BepInEx config, and runs production visual fixtures on CLR and Valheim Mono, authenticated configuration/startup tests, both ownership modes, combined grid/initial-loading/inventory/tamed guards, combat/PUP readiness and native state/codec audits. verification.json records exact test outputs and artifact/source hashes. The new native audit installs/removes the actual addon's prefix/postfix on FiresGhettoNetworking's real OnHandOff body under Valheim Mono and inspects authority/event/physics call exclusions.

These are headless fixtures and native method/IL checks. No live Unity renderer, Animator, physics world, Steam session or complete third-party Harmony graph was executed. The native Mono host can emit the expected unresolved Unity Time diagnostic; the audit must still explicitly print its completed PASS line. Visual quality and supported visual hierarchies require in-game comparison. The feature deliberately does not migrate attack/root motion, replay gameplay, repair missing collision geometry, fix a stopped server AI or claim a lossless/seamless two-player combat handoff. The previously identified ordinary low-ping player-to-player FGN combat exclusion remains separate and open.

## Earlier validation history
# Local 1.0.73 mob readiness and tamed handoffs

The two new supplied logs reproduce awake server mobs whose native AI invocation is attempted 41 to 164 times while every result remains false and no target evaluation runs. The same state occurs with no target, a player target and a possible nearby non-player target. The Wolf zero-health recovery warning is consistent with a skipped native update, not evidence of a particular collision cell. No log contains the actual PUP preparation result, so this build records it for the selected solo mob rather than claiming PUP is proven to cause every live pause.

Actual PUP 1.0.37 source/IL shows spatial transition/readiness queries, a whole-cell incomplete-support veto and a five-second rebuild retry. The dedicated preparation queue serves at most sixteen bodies with a two-millisecond budget; it is shared across worldwide server bodies. Unknown loaded-session metadata is a separate global prerequisite. New priority preparation calls only the existing native preparation on held owned server mobs within 40 metres of a ready player and does not alter holds, metadata, roots, collider flags or completeness. With compatibility disabled it installs no preparation hooks; the handoff eligibility proof remains conservative.

Mob transfers to the server and initial defaults now use the optional adapter's local canonical-position proof. Server-to-player requests deliberately bypass this additional proof so an unavailable server collider cannot prevent returning simulation to a player. Unchanged server ownership does no new proof work. No general grid area-readiness check or pre-transfer object probe is restored.

Tamed transfers use a capability-negotiated appended grid message and the existing ordered token fence, authoritative owner/revision receipt, native source commit and damage forwarding. Only a scoped `tamed` veto is relaxed after checking the actual source actor; other native reasons remain. Server sources apply the same actor checks and final native motion flush. Older endpoints do not get speculative animal grid writes. Tests cover attacks including an unfinished Attack after InAttack becomes false, riding/following/user state, friendly summons, stale ownership, duplicate wire delivery and exception cleanup.

New-owner no-target bootstrap now requires a successful AI pass unless this owner already published a real report. Thus a freshly adopted actor cannot repeatedly bounce through the server merely because its local target starts null. Existing target adoption and real target loss still use their event path. Weak AI-keyed state resets with the world; no growing event history or per-mob timer is added. The first-pass finalizer does not itself approve an ownership write.

The verification receipt records fresh managed, embedded Valheim Mono and native IL checks. Fixtures exercise production methods and simulated transport, not a live Unity world, PUP proxy queue or Steam connection. Live confirmation of all reported pauses and teleports remains necessary. The ordinary player-to-player native FGN combat gate remains outside this change.

# Earlier local 1.0.72 solo-boundary correction

The supplied live log shows authoritative server ownership with `boundaryReady=False`, no active or unfinished attack, and waits up to 16.2 seconds before preparation. It does not identify the local target or sleeping state, so it cannot prove one specific cause for every pause. Source review found two unnecessary test conditions, reproduced before the fix: a missing player target prevents preparation; native sleeping AI returns successfully before UpdateTarget, leaving the target-evaluation test hook unused.

The correction accepts a completed idle AI operation, preserves the actual target and native sleeping state, checks successful AI completion and keeps Busy/attack/use checks. The readiness payload distinguishes an absent player target from failed source setup. Scope is limited to FgnSoloCombatTest.cs, its HoldForSolo helper and plugin version metadata. All other production files must exactly match the verified 1.0.71 baseline. The ordinary player-to-player FGN combat gate is not changed here.

Regression coverage includes missing and non-player targets, full player/server round trips, sleeping server AI, skipped AI, unfinished attacks and failed source setup. The final verification receipt records fresh managed/embedded-Mono/native audits and the hash-checked unchanged baseline. No fixed live multiplayer run is claimed; install this test build on server and client.

# TooManyLags 1.0.71 combined local test validation

## Merge scope

Three-way integration uses 1.0.64 as common base, the complete 1.0.70 rejected-cell build as current, and the complete 1.0.66 target-boundary/solo branch as the side. Only six production files differ from 1.0.70: FgnCombatOwnership.cs, FgnMobHandoff.cs, FgnTargetEvents.cs, the two new FgnTargetBoundary.cs/FgnSoloCombatTest.cs modules, and Plugin.cs. The loading-gate removal in ServerReady is deliberately retained from 1.0.70. Version conflicts resolve to 1.0.71; there are no remaining merge markers. Provenance and expected source scope are recorded in merge-71-provenance.json and checked by verify.ps1.

All 1.0.70 grid, inventory, static ownership, player-only policy, pending-work and state-transfer sources remain byte-identical. The combat branch retains its bounded same-invocation new-attack barrier, completed-operation reporting, token/revision handling, unfinished-attack veto and admin-controlled one-mob solo test. It does not add animation snapshots, movement extrapolation or additional late-event migration. Solo mode is off until explicitly started, with a 0.5-second delay after observed completion and existing guarded transfer paths.

Verification runs the combined production combat/synthetic-boundary/solo fixtures on managed Harmony and embedded Valheim Mono, actual provider/native IL contracts, companion-policy precedence, collision-readiness regression, inventory, full grid/protocol integration, player-only runtime, field caches, station handoff, native contracts and compact-state transfer checks. Input/output hashes and exact results are in verification.json. Test fixtures are merged rather than replaced, so 1.0.70 loading/refusal expectations and the combat-branch cases both run.

No live Unity world, solo combat, real network or two-player visual continuity test is claimed. No installed profiles, publication or Git remote were changed.

## Retained 1.0.70 validation notes

The 1.0.70 baseline used the complete inspected 1.0.64 test build. Its production scope was AuthorityController.cs, GridHandoffProtocol.cs, StaticBuildings.cs, FgnCombatOwnership.cs, FgnOwnerHandoff.cs, NativeInventoryHandoff.cs, PlayerSimulation/LocalAgent.cs, PlayerSimulation/AuthorityController.cs, PlayerSimulation/Handoffs.cs and plugin version/startup metadata. The independent PUP simulation module is unchanged. Its previously separate mob-test branch is now included as described above.

## Refused grid handoffs

Compared with 1.0.69, only AuthorityController.cs, NativeInventoryHandoff.cs, PlayerSimulation/AuthorityController.cs, PlayerSimulation/Handoffs.cs and version metadata change. Native inventory refusals park the specific object in the existing field-renewal index. In player-only mode, processed grid no-commit/use refusals also wait for new coverage instead of holding a timer or a permanent owner/revision rejection stamp. Hostile Mob attack waiting remains separate.

Tests cover synchronous/asynchronous refusals, both exit-retention modes, mixed successful/refused objects in the same cell, previously certified cells becoming incomplete, stable-idle operation, fresh requests with unchanged owner/revision, ordinary data/router events, unload/reset, and late native receipts/half imports. The cache implementation itself is unchanged: its existing per-member confirmations must all be satisfied before certifying a cell. Permanent server-station overrides retain their intended ownership rule.

This incremental build runs native inventory, full grid/protocol integration, player-only simulation/runtime, field-cache, native contract and compact-state checks. It does not claim to rerun every unchanged suite from earlier builds. Source snapshots which still differ from the native server tuple retain reconciliation; missing replies and half imports are not treated as refusals. No forceful ownership change is added.

## Retained loading-gate regressions

- Loaded doors, pickables, containers, fires and workstations in red coverage transfer without broad readiness queries or destination-loading RPCs. Matching prefab-only roots can be claimed before a server scene instance exists. Settled ownership produces no proof retry work.
- Recipient receipts validate owner/revision/state and the exact RPC instance for actual action delivery, without broad-area readiness. Stale revisions remain rejected; active compatible player sources retain their commit protocol.
- Fixed-source dormancy, field timer parking, red acceleration, running deadlines, static grids, exit retention and zero-peer idle behaviour retain dedicated tests.
- Player-simulation preparation accepts a matching data replica without a live instance; source-use checks and live AI target adoption stay separate.
- Combat and attack-end retries do not run duplicate PUP/native loading queries. Invalid actors/poses, actual attacks, target exclusions and source commits retain their checks.
- Inventory opening, text editing, refill, pickup, routed exactly-once delivery, recipient ordering, compact state, native serialization and optional PUP integration retain dedicated checks.

verify.ps1 performs the warning-as-error build, managed regressions, native game/provider IL audits and headless Valheim Mono checks. Exact results and final artifact/source hashes go into verification.json. Source comparison verifies the ten cumulative production files and the five incremental files above.

Inputs include local FiresGhettoNetworking 1.5.72 and PUP FPS 1.0.37; native audits also exercise older interfaces specified by their scripts. Fixtures do not simulate live packet delay, Unity physics or multiplayer. Operation counts are not live CPU/bandwidth measurements. No publication, installed-profile change or remote Git change is part of this test.

### Visual continuity gaps (2026-10-29)

- Startup: private native motor endpoints become available from current inputs at interval start. AI decisions remain bounded by the live capture horizon; no future AI pass, new recording time, or extra motor step is introduced. Regression sweep covers 20/50/100/500/1000 ms windows and 4/8/16/33 ms render intervals.
- Takeover: blend the current displayed trajectory into the current replicated root using smoothstep. Position and entry velocity remain continuous, including zero initial position error. Direction protection follows replicated/paired authoritative body velocity; root corrections no longer redefine it. Takeover retirement runs after advancing this frame's presentation, with numerical alignment tolerance rather than a 1 cm disappearance allowance.
- Overlap: retain the preceding display offset during new fake playback. Only observed-horizon final takeover corrects it; there is no independent opposing offset decay during playback.
- Model root: run native UpdateGroundTilt inside the existing isolated transaction, sample recorded visual-root locals, and blend them to current native locals at takeover. Model draw and query preparation share scaled model-position math. Managed rotation fixtures cover yaw; pitch/roll and private Unity physics remain live-test requirements.
- Unresolved contract choices: a finite immutable recording cannot guarantee continuous motion through indefinitely late destination updates. Likewise, a displayed mob ahead of a stopped real mob cannot both align and forbid all backward movement without waiting. Existing endpoint-hold and movement-direction rules remain pending explicit user preference; no hidden extrapolated-frontier takeover or extended capture was added.
- No profiles modified; no installation or live multiplayer validation performed.

Full debug verification completed: 19 suites passed. Receipt `verification.json`, DLL SHA256 `4785D38AB6DFC60375737E73D5202C0B3592B83C6A4E87E0664245B54F3B0C58`. Bridge suite: 8,358 trajectory/recording checks, 1,206 emitted wiring/isolation checks, 152 attached-FX checks and 115 native presentation hook checks. No live Unity world or multiplayer playback validation.
User clarified the stationary/ahead case: slow down first; wait when slowing cannot achieve alignment. Backward correction remains forbidden. The remaining finite-buffer question has been re-asked in German; adaptive endpoint playback remains pending that answer.

### Adaptive finite-buffer playback (2026-10-29)

User approved dynamically slowing the remaining immutable buffer, then waiting if needed; backward correction remains forbidden when the real mob stops behind the displayed mob.

- Before advancing presentation, compare remaining recorded time with estimated wait for a matching destination horizon. Prediction is bounded to one destination-local sample interval and also reserves the next clock delivery; stale observations cannot promise unlimited progress. Initial spacing follows the existing 50 ms clock-send interval and cached AI cadence; accepted packets replace it with measured destination-local spacing. This is transport timing, not a new AI tick grid.
- When necessary, critically damp remaining time: x(t) = (x0 + b*t) exp(-omega*t), b = omega*x0 - v0 >= 0, omega = max(2/wait, v0/x0). This preserves incoming displayed velocity, keeps remaining time nonnegative and slows toward the actual fixed endpoint. Normal owner-rate interpolation can still decelerate more strongly. Returning packets restore the normal schedule through the existing continuous rate filter.
- No minimum adaptive playback speed. Extended outages approach a hold; numerical exhaustion holds exactly at the immutable endpoint. Neither recording extension nor future-frontier takeover is allowed. Returning data cannot cause hidden-clock backlog replay.
- New diagnostic fields: bufferSlowFrames and minPlaybackRate. Normal 0.5 playback remains unchanged for healthy matching packet delivery; low-ping cases account for packet cadence exceeding the recording window.
- Fixture sweep covers 20/50/100/500/1000 ms windows, 4/8/16/33 ms frames, normal/late delivery, prolonged outage and resume, healthy nominal rate, entry velocity continuity, immutable endpoint rejection and subdivision of the analytical brake. Focused fixture count: 26,729. No live Unity playback or multiplayer claim.

Adaptive playback full verification: 19 suites PASS, DLL SHA256 C04249806C9C2496D62D1C6286306B4E1ADBA0E78F89FE7931A309C21F9C7541; receipt hash matches. Bridge suite: 26,729 timing/recording assertions and 1,208 emitted wiring/isolation assertions; existing attached-FX and native presentation checks pass. No profiles modified or installed. Live rendering/network validation remains open.

### Short test commands and scoped vanilla comparison (2026-10-29)

- `ai_test [start|stop|status] [RTT_ms] [interval_seconds]`; integer first argument implies start, no nonempty arguments requests an authoritative server toggle. `ai_test_v` / `ai_test_vanilla` use the same parser in vanilla mode. `tml_ai_test` remains an alias. Default native RTT and 0.5 seconds remain; malformed/nonfinite/out-of-range arguments are rejected before control transmission.
- Server decides toggle using actual active state, retaining admin authorization, one-player checks, native attack boundary and five-minute/distance cleanup. Mode flags are announced before the first transfer, including server-first starts and latency setup. The existing arm RPC carries the session flag; revised capability modes 20/21/22 refuse old arm payload endpoints. No additional recurring RPC is added.
- Vanilla comparison is fenced by selected ZDO identity plus current world/network. It skips immediate source-commit shortcut and extra flight/combat/simulation checkpoint callbacks at the existing final owner-write site, preserving exactly one native owner write. Fake and legacy presentation are cancelled for this mob; incoming bridge packets are ignored before decode. Prior matching RNG/target/clock/FX epochs are removed. Unrelated mobs remain unchanged. No changes to latency queues, completion-relative cadence or native damage simulation.
- Optional gate delegates default null and are installed only during an active solo test; stop/reset removes them. Normal builds omit commands and test driver, retain inert null extension points and existing gameplay safeguards.
- Regressions cover aliases, omitted action, empty arguments, toggle, malformed input, current/old capabilities, server-first mode announcement, duplicate mode packets, both transfer directions, custom cadence, artificial-delay handshake, per-mob isolation and complete cleanup. Separate production transfer tests prove native mode sends no checkpoint/forced SyncNow; source tests prove no shortcut/publication; legacy visual tests verify scoped capture skip and render-pose/subscription cleanup.
- This is a native transfer comparison inside the existing FGN test scheduler. Other plugins and original safety guards remain active. No installed profiles changed; live multiplayer/Unity comparison remains open.

Clean snapshot verification exposed a stale-fixture harness defect: test-combat.ps1 compiled a unique CombatRegression-<guid>.dll, while test-combat-mono.ps1 opened CombatRegression.dll. The managed runner now publishes the successfully tested current fixture to that fixed name before Mono execution. Fresh snapshot checks pass 2,194 assertions under both managed CLR and Valheim Mono; no old bin contents are reused. The parallel user-authorized OwnerDebug read-only category/phase API is preserved in this snapshot.

Short command/native comparison final validation: all 19 full debug suites PASS. DLL SHA256 92F1E58490116136D1CD58C92A7442B2E2CE76686863D3739429B6F859CDF0BA. CLR and actual embedded Mono both execute the fresh 2,194-assertion command/handoff fixture. Bridge checks: 26,729 timing/recording, 1,212 emitted wiring/isolation, 115 actual presentation hooks; flight127/source44/legacy-visual production regressions pass too. A source-stable snapshot was used after a parallel authorized OwnerDebug API edit invalidated the initial source fence. No profiles modified or installed; live Unity/multiplayer comparison remains open.



