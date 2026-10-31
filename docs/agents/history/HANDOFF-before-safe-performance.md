# TooManyLags Mob Bridge Rebuild

## Current: performance/sub50ms candidate, 2026-10-29

- Active copy TooManyLags-2.0.0-performance, from verified low-latency50F850C6...
  Earlier source/builds and prepared release ZIPs unchanged. User authorizes
  implementing performance opportunities and checking below50ms; no publication.
- Full native pose fast path avoids discarded History.Sample/blending while
  retaining Apply's late native snapshots, all roots/IK/attachments/partial blends.
  Extracted actual-method differential fixture compares verified prior source.
- Server DecodeForRelay validates full payload but omits dense bone arrays;
  relay forwards same raw bytes. Decoder acceptance differential/mutation tests,
  large-batch byte identity/allocation checks; client full decoder unchanged.
- Animation history shares unchanged owned scale arrays, clones actual changes.
  All pose samples/precision retained; no pooling/recycling lifetime risk.
- Sub20ms CaptureClock primes existing partial endpoint at startup if no earlier
  AI decision due; no future decisions/full controller pass/double integration.
  SettleForPose20ms fixed floor removed; half-playback/geometry and1ms epsilon stay.
- No AI/physics cadence reduction, new logs/polls or wire changes. Sub50ms tests
  retain observed-only join and finite-buffer waits. Live Wolf/Troll retest open.
- verify.ps1 -Release includes new animation differential suite (26 suites).
  See verification.json for final hashes/status. No installs/packages/push.

## Prior: low-latency playback candidate, 2026-10-29

- Active independent copy: TooManyLags-2.0.0-low-latency. Prepared release source
  and outputs2.0.0 remain unchanged. User reports Wolf near-stop early in50/100ms.
- Fixed initial SendClock not updating its send timestamp, followed by immediate
  duplicate zero-progress AI clock. Sender scheduling now shared value state;
  first positive physical progress and attack events remain immediate.
- Receiver accepts early packet poses/frontiers but sub50ms startup/event gaps
  cannot replace ordinary interval/rate-filter response. Slow cadence still adapts.
- Observed buffer planner now accounts for current integration frame when using
  packet age measured at frame end. Sufficient healthy buffers keep0.5x; short
  buffers do not exhaust one frame before a known upcoming packet. Unknown
  outage damping unchanged. No new traffic/polling/global work/logging.
- Native AI/body/animation, fixed recording, target/RNG, observed match and final
  smoothing unchanged. New managed cadence/50/100ms/early-event tests pass;
  release/debug/native checks required via verify.ps1 -Release. Live Wolf retest
  open. Do not overwrite prepared root release ZIPs as part of candidate testing.
- See verification.json for final hashes/pass status, VALIDATION for evidence.

## Prior: 2.0.0 release preparation, 2026-10-29

- Active independent copy: TooManyLags-2.0.0-release. Baseline debug-idle E953BF9A...
  unchanged. User authorizes preparation and English user-facing changelog,
  not publication/install/push. Final German; work English/Caveman.
- Metadata2.0.0 synchronized. New changelog uses Markdown and concise outcomes;
  this release has a longer overview. Actual config key PhysicsObjects. Avoid
  universal zero-teleport/all-AI guarantees; historical entries preserved.
- TML_MOB_BRIDGE enables gameplay bridge in both modes. TML_DEBUG_SYSTEM retains
  solo commands, injected latency and selected-mob tracing only. Audits verify
  release keeps native bridge/isolation while omitting test machinery.
- User observed overlong transition at ai_test0/50. Native FGN policy RTT uses
  unknown400ms sentinel. MobBridgeLatency selects measured echo/link or connected
  socket RTT; active injected delay wins over stale echo/cache. Unknown-route
  fallback100ms retained. No new polling/packets. No playback-ratio policy change.
- Real AI/clock50ms quantization and actual pose residuals can still lengthen
  low-ping transitions. Exact live frame not traced; in-game retest open.
- verify.ps1 -Release includes release/debug and full release native bridge
  checks. See verification.json for final status/hashes. Package after verified
  receipt. No profile changes, installation, publication or push.

## Prior: debug idle/error-only logs, 2026-10-29

- Active copy: TooManyLags-1.0.94-debug-idle. Previous owner-gain-continuity
  A63F3777... unchanged. No install/package/push.
- Default compiler excludes TML_BRIDGE_DIAGNOSTICS: no routine TestLog/Report
  callers, cost builders/timers, per-frame divergence stats, root-bone/camera
  probes or diagnostic sends. Legacy diagnostic RPC empty sink retained.
- Pure counters guarded by !TML_RELEASE || TML_TEST_METRICS (standalone fixtures
  retain them; shipping debug does not). PUP AfterMob first checks solo Active.
- EnableLogs gates on-demand console requests only. Real Warn/Report errors
  unconditional; test console feedback preserved. Buffer overflow and missing
  visual attack trigger now use real warning sink.
- Required pre-handoff rate cache and real bridge traffic preserved. Cheap idle
  null/count guards remain. No claim of literal zero CPU or measured FPS gain.
- BuildPolicy includes DebugIdleAudit of emitted nested method IL and negative
  control. See current verification.json for final suite results and hashes.

## Prior: rare ownership-overlap continuity, 2026-10-29

- Active copy: TooManyLags-1.0.94-owner-gain-continuity. Prior fully verified
  observer-recording copy7447FA... remains unchanged. No installs/packages/push.
- User: normal path performant; extra correction work only for exceptional
  renewed ownership while an existing bridge/correction remains active.
- Visual.Valid checks active correction owner revision; unchanged epochs use
  original path. Exceptional ownership state allocated lazily. Local gain waits
  for actual m_wasOwner=true via existing Effects.AfterSync finalizer; callback
  null normally. No new Harmony patch/per-mob scan/network traffic/private AI.
- Rare Takeover ownership layer preserves last pose/velocity, uses finite
  receding Hermite steps, lets real reversal brake smoothly, waits behind stopped
  native root. Original animation clocks/weights retained; model-local tilt starts
  from displayed pose. Shared HUD/hit/FX remain on same presented pose.
- Original protocol identity remains intact for matching/finish messages; separate
  CorrectionRevision tracks presentation epoch. No native root/physics/state writes.
- Full final verifier:19 suites PASS, timing29846, emitted isolation1708,
  native presentation122, native weapon502. Source/DLL receipt must match.
  SHA256:A63F3777AB8ACA88F5FD5A6A570684EF520DAE1B4F0801D97D0EE4C4CCEF0110.
- Watcher removal uses ReferenceEquals for destroyed Unity references. Headless
  key comparer avoids unavailable engine instance-ID hashing; native field,
  finalizer and cleanup bodies execute from actual addon/game assemblies.
- User's exact live jump lacks per-frame positions. Managed1.547m/10ms boundary
  reproduction and live overlap motivate the fix; no claim of live confirmation.
- Details: VALIDATION.md; chat workspace work/owner-gain-rewind-issue.md and
  work/owner-gain-full-verify.log. No broader server->player bridge added.

## Prior snapshot: recorded third-party observers, 2026-10-29

- Active independent copy: TooManyLags-1.0.94-observer-recording. Do not overwrite
  the concurrently used handoff-sim working folder or its OwnerDebug changes.
- User: old-owner display/timebase unchanged. Observers reuse its exact native
  recording, locally delayed/adaptive, matching observed horizon then correction
  with half actual playback duration. No observer simulation or world-state writes.
- New MobBridgeObserver/Packet: negotiated reliable source stream, server epoch
  authentication, native active-area relevance, no endpoint/solo streaming,
  first endpoints promptly then 50ms batches, ordered independent pose deltas,
  seals, bounded pending state, disconnect cleanup and observer-only clock demand.
- Visual/Animation: shared presentation Track with receiving adapter instead of
  Body/Simulation; source-relative rig paths tolerate differing FX/equipment;
  locomotion native, predicted attack bones exported from existing source history.
- Legacy observer suppression scoped to actual recorded epoch or vanilla mode;
  server-origin/unsupported paths retain old fallback. ai_test solo guard unchanged.
- Final full debug verifier: 19 suites PASS. DLL/source hashes match receipt.
  SHA256: 7447FA316BCAD7C1ACF1C84570D16BBAF1D2567F410E8E504FBDCF84948FECCD.
  Recorded observer22853 CLR/Mono assertions; native observer880 contracts;
  existing timing26729. Native clock relay failures isolated/retried independently.
  Observer/source presentation capacity separate; exact source epoch/token presence.
  No installation/publication/packages/push. Live three-client Unity test open.
- Details/limitations: docs/observer-recording.md and VALIDATION.md.

## Short commands and vanilla comparison, 2026-10-29

- Verified immutable integration snapshot: TooManyLags-1.0.94-ai-test-verified-20261029.
- `ai_test [start|stop|status] [RTT_ms] [interval_seconds]`; numeric first argument means start, empty arguments toggle on server. Defaults 0 ms/.5 s. Legacy `tml_ai_test` alias retained.
- `ai_test_v` / `ai_test_vanilla`: same parsing, cadence, fake network latency and safety guards; selected mob bypasses source shortcut, flight/combat/AI checkpoints and both fake/legacy visual bridges. Starting native mode cancels prior matching bridge epochs, hooks clear on stop/reset. Other mobs unaffected.
- Revised capability20/21/22 and bool arm payload require updated debug client/server. Session mode is announced before first transfer, including server-first and latency setup.
- Parallel OwnerDebug read-only API/DebugPhase additions preserved. No source edits during final successful verifier.
- Full debug verifier: 19 suites PASS, SHA256 92F1E58490116136D1CD58C92A7442B2E2CE76686863D3739429B6F859CDF0BA. Fresh CLR and Mono command/handoff fixture2194; bridge timing26729/wiring1212; native presentation115.
- Harness defect fixed in test-combat.ps1: freshly successful GUID fixture copied to CombatRegression.dll before Mono run. Earlier Mono fixture2112 was stale; current full run executes2194.
- No installs/packages/publication/push. Live Unity comparison open. Final DLL in this snapshot's bin/debug-system, not concurrently edited shared project's bin.

## Visual continuity gaps, 2026-10-29

- Five of six audited gaps addressed: causal native motor endpoint priming, displayed-velocity takeover blend, constant overlapping display offset, authoritative-velocity direction guard, recorded native model-local tilt and scaled model presentation. Retirement now checks current-frame geometry.
- Latest adaptive-buffer full verifier: 19 suites PASS, SHA256 C04249806C9C2496D62D1C6286306B4E1ADBA0E78F89FE7931A309C21F9C7541. Bridge: 26,729 timing/recording and 1,208 emitted wiring/isolation checks. No profile installs or live Unity playback test.
- Sixth gap now implemented after explicit user approval: slow remaining immutable playback dynamically, then wait if needed. Timeline.ManageBuffer plans speed from observed progress, bounded scheduling-only prediction and next packet spacing. Critically damped remaining-time integration preserves entry velocity and cannot cross the fixed endpoint. Returning packets recover via continuous owner-rate scheduling. No extra capture or predicted-frontier takeover.
- User explicitly chose slowing, then waiting if necessary, over backward correction when fake is ahead of stopped real mob. Current guard retained.
- Selected-file baseline backup and focused/full logs live in next-chat workspace work/before-six-gap-fixes and work/six-gap-*. See work/six-gap-status.md there.

## Current task: post-collider rough animation/smoothing regression

- User confirms FPS spikes/rotation flicker fixed; now roughness/brief pauses,
  perhaps animation-only and near start, head rotation appears to reset.
- Comparison boundary: immediately before first collider/Troll admission through
  current build, including its nearby predecessors. Do not blame older systems
  without comparing or redesign/undo working fixes.
- Exact method comparison vs before-multi-hitboxes: Advance, BeginTakeover,
  ComputePose, CurrentPose unchanged; new path is early native bone blending
  in UpdatePresentation then late application. Existing holds predate regression.
- Fix: Prepare caches fake sample/time/weight only. Apply samples/blends latest
  native locals at camera boundary from values already saved for restore.
  Blended hitboxes refresh from that same late saved-native pose. No second
  simulation, animation evaluation or clock advance, no early native bone scan.
- Emitted checks: no early bone locals, late native read/blend, hitbox refresh.
- Troll prefab enables native head rotation/foot IK; private rig lacks native
  CharacterAnimEvent. Skin refresh after collider fix can expose this incomplete
  procedural-pose parity. Pure-fake start symptom remains live-unproven; no
  head skip, speed change or large IK rewrite made without evidence.
- Latest combined full verifier/hash: verification.json. Profiles untouched.

## Prior task: new flicker log + constructor/pose lookup optimization

- Attachment 3d424a4c-d767-4e3a-9170-a02126523122: all 5 model draw gaps 0 deg;
  bone draw gaps 8.238 / 3.463 / 22.479 / 10.150 / 39.661 deg. Actor takeover
  yaw 0.0-0.6; transitionLayers=0. Difference proven; writer/math cause not proven.
- Keep root placement early (preCull), apply/save bones once at preRender
  after culling, restore after draw. New maxApplyBoneGapDeg checks immediate
  post-write mismatch; read-only MeasureDraw retains pre/post draw diagnostics.
  No extra full-rig application, graphics toggle or rotation clamp. Live pending.
- Live snapshotCreate first98.743ms, warm39.068-50.807ms. Ground remap tiny;
  expensive ground-copy hypothesis not supported by this log.
- ShadowSnapshot caches constructor delegates by field type. Per-request field
  validation and fresh actor slots/values retained. No actors in static cache.
- 100 native snapshots warm: old134-139ms, new7.610-14.087ms, firstnew34.127ms.
  Headless microbenchmark, not live frame speed. Actor independence and latest
  value/duplicate-field rejection/failure restoration native checks added.
- PoseHistory binary lower-bound over same ring; 1520 exact differential cases
  cover repeats/tiny spans/endpoints/wrap, same interpolation/capacity.
- Combined verifier result/hash: verification.json. No profile installation.

## Prior task: persistent Troll flicker follow-up

- User confirms club fixed; rotation/apparent double model persists around
  takeover. Motion Blur off, no TAA/upscaling mod. Do not disable graphics
  features or cap rotation as an unproven fix.
- Fake rig has no renderers. Actual installed Troll prefab has Body+Hair using
  same root bone, one LOD level. No proof about runtime mod-added renderers.
- New read-only pre/post camera diagnostics compare intended cached vs actual
  visual/root-bone rotations: maxDrawModelGapDeg, maxDrawBoneGapDeg, drawSamples
  in existing operation=presentation report. Subscribed only with active tracks,
  no per-sample strings/logs, removed on unsubscribe. Need live test/log.
- New live logs show snapshot phase ~42-46 ms each transfer. Headless snapshot
  Create warm benchmark only ~1.34-1.39 ms each (100x loop); not enough to
  attribute the full spike. New phase splits snapshotCreate/Callbacks/Install/
  Ground distinguish snapshot allocation from ground collider copy/cooking.
- Earlier performance pass completed 19 suites before these diagnostics;
  latest combined receipt/hash must come from final verification.json.

## Prior task: further active-preview performance optimizations

- User requested worthwhile optimizations preserving function. No target-input
  semantic changes made; previous local-target integrated-motion branch remains.
- Native motor/event/hash paths use exact cached FieldAccess delegates, replacing
  reflective GetValue/SetValue and string lookup. Setup reflection remains.
- Typed per-actor callback slots capture latest delegates, null during isolation,
  restore in existing finally chain. Native tests cover all callback fields with
  non-null, changed and null values; scalar/vector/quaternion fields compared.
- Real native field benchmark: 50k read/add/write pairs, reflection 20.677 /
  22.327 / 28.619 ms, typed 0.358 / 0.445 / 0.552 ms, identical float result.
  Dispatch microbenchmark only; no live frame/FPS inference.
- NativeWorld.Mirror direct dictionary replaces linear scan. Observe caches
  stable source Transform refs and reads target pose once per observation.
- Animation.Prepare returns after sampling/model setup at realWeight==0,
  skipping irrelevant real-bone reads. Nonzero blend behavior unchanged.
- No scenes/rigs pooled, no simulation-rate/collision/RNG shortcuts. Latest
  full verification receipt/hash: verification.json. Profiles untouched.

## Prior task: handoff cost investigation + Troll rotation report

- Measured former native+presentation installs at 52.853/70.568 ms and removes
  at 52.187/56.929 ms under embedded Mono, without world or third-party patches.
  Test/destination hook costs are separate. Benchmark tools/benchmark-mob-bridge-hooks.ps1.
- Hooks warm on bridge world connection, retain across epochs, remove at
  shutdown/feature stop. Test hooks retain until test stop/reset. Empty epoch
  callbacks bypass actor/clock lookups; normal dispatch overhead is not zero.
- Retained edge benchmark: first 2.280 ms, then 0.040/0.031 ms with reflection.
- Cache native state/delegate field metadata; skip only exactly unchanged
  environment collider poses/enable writes. No simplified simulation added.
- New debug MobBridgeCost reports deferred setup/checkpoint/capture/disposal
  timing, shape/bone counts. Full receipt/hash in verification.json.
- User located Troll flicker at start of takeover smoothing; club briefly floats
  beside hand. Both first seen after multi-collider admission.
- Fixed mismatched presentation clocks: UpdatePresentation prepares bone blend
  once; camera readers apply cached values. FollowHit no longer applies/restores
  whole visible skeleton. TryPose composes only requested cached local TRS chains.
- MobBridgeSkin enables per-render skin-matrix refresh during bridge only,
  restores original flags on Stop/RemoveAt, includes inactive LOD renderers.
  Official Unity API requires it for multiple skinned poses per update. No mesh
  baking, per-frame renderer scans or additional animation evaluation.
- 40 hitbox/skin fixtures; emitted audits check shared sample, no reader clock,
  math-only queries and cleanup. Visual symptom resolution remains live-unproven.
- Private Animator drops active transitions during current-state seed; separate
  parity limit left unchanged, not proven culprit for reported takeover flicker.
  Keep transition/root-gap/angular-step diagnostics for next live test.
- No game profiles modified. Preserve earlier native recording and visual
  attack experiment; no pooling or renderer redesign without measured need.

## Prior task: multi-collider preview admission

- User explicitly requested generic multi-collider support after Troll preview
  rejection. Blanket visual collider/Rigidbody guard removed.
- MobBridgeHit copies all mob-owned capsule/sphere/box/mesh shapes; excludes
  nested Character/ZNetView entities. Hidden shapes disabled only after complete
  construction, restored once; returning owner retires proxies immediately.
- FollowHit samples shared render bone pose and restores source rig in finally.
  Weak-spot lookup maps proxy to native collider only for its owning Character.
- 34 managed hitbox regression checks added, plus native weak-spot Harmony
  hook install/remove and emitted pose/restoration audits. Full result/hash:
  verification.json. Live Unity execution remains untested; profiles untouched.
- Keep scope small: no pooling, performance redesign or extra attack features.

## Prior task: visual-only preview attacks

- Full debug verifier complete: 19 suites PASS. SHA256:
  9CD2736BB66A1471925967D6F4CBCAC7A399439925A835F61DC2FDD9FB163B61.

- Newest user request: continue fake capture through attacks; no attack barrier.
  Earlier no-animation-recording instruction is superseded for this trial.
- Private native Animator + manual PlayableGraph on transform-only rig. No
  gameplay components cloned. Pose frames share capture time and seal read-only.
- Native variant/chain selection, private attack tags/age/factors/root motion and
  jump/flight movement continue. Hit callbacks never invoke real attack logic;
  no preview damage, ammunition consumption, projectiles or gameplay sends.
- Native clip callbacks have private-only receiver. RandomIdle/AnimSetTrigger/
  StateController have scoped handlers; unknown custom state behaviours reject
  preview. Explicit declared 3-argument overloads required on Unity 6.
- Camera pose override restores source bones. At takeover, pose time continues
  without restart and accelerates toward 1x while blending to actual animation.
- Protocol unchanged. Debug build; no installation/package/publication. Live
  Unity graph/render/physics/multiplayer remains untested. See VALIDATION.md and
  verification.json for actual completed checks and latest DLL hash.
- Prior source/DLL backup: task workspace/work/before-visual-attacks.

## Previous native producer task (historical)


- User explicitly excluded animation-state buffering. Only position/rotation.
- MobBridgeBody now wraps MobBridgeNativeActor: original Character native motor,
  liquid/contact methods and separate Unity PhysicsScene Rigidbody integration.
  The obsolete MobBridgeCollision runtime and its custom-contact tests are removed.
- Append-only MobBridgeRecording captures live input over initial measured L,
  then seals read-only. Native fixed-step capture and independent half-speed replay;
  final physics remainder avoids dropping e.g. 50ms -> 40ms of movement.
- Existing AI/target prediction/attack barrier/hitbox/HUD/observed matching and final
  correction retained. An ahead attack never jumps replay to the future barrier.
- Transient native step restores actual fields/root/body/callbacks/global RNG;
  private collider ground references prevent impulses reaching real bodies.
  Native Character.transform yaw is transferred into private body before Simulate.
- Full debug verifier: 19 suites passed, receipt 2026-10-28 21:13:54 local.
  SHA256: 7AE07E5AFC6BFA6B41C7466E527565BCB85DDBCF30EE83E5E3FD9E33D5B16639.
  Unity world/live execution remains untested.
- No install, package or publish. Actual repo path is the .94 handoff-sim tree.
- Details and native-physics limits: latest section at top of VALIDATION.md.

## Earlier work (superseded motor/contact notes retained as history)


- Short-handoff clock fix: only the first frame prorates Unity delta against
  handoff wall time. Later frames consume native delta once, avoiding min(delta,
  callbackGap) losses from variable LateUpdate phase. A 120-frame/50ms-window
  fixture alternates 6ms callback phase at steady 16ms native frames.
- Terrain start stalls confirmed in current Player.log: 11/153 early samples
  have zero terrain-allowed displacement with active steering. Contact solving
  now resolves initial overlap with ComputePenetration, retains controller input,
  sweeps remaining tangent motion, and uses full native geometry/contact margin
  and solver iterations. Flat-foot ground snap removed; walking gravity enabled.
- Contact regressions cover initial-overlap opposing normals, 400 slope steps,
  corners, real walls and takeoff. Early motion diagnostics include recovery
  distance and contact-normal Y; actual Unity contact execution remains untested.
- Latest follow-up: protocol 4/checkpoint 3; copied native SelectBestAttack and
  EquipBestWeapon, isolated candidate buffers/equipment/cooldowns, and full
  weapon cooldown/order/selection transfer. Field schema remains 62 fields.
- Near-horizon playback holds removed. Fake own attack state and body attack
  factors no longer follow later replicated animations; attack age follows
  physical simulation time. Attack barriers remain intentional.
- Latest log: 29 starts, 3 attack pauses, one at fake zero; two explicit wrong
  attack choices. Other start freezes lack body-motion evidence. Two early
  bridge motion samples now report steering, velocity, displacement and contact.
- Native weapon differential checks: 478 pass with declared engine seams.
  Consult verification.json for the latest completed full verification/hash.
- Base: verified `1.0.86` source archive.
- Working branch: `work/TooManyLags-1.0.94-handoff-sim`.
- Target version: `1.0.94` local debug candidate.
- Live validation: not started.
- Published or pushed: no.
- Focused production/native suites: pass for the status/FX/hit pass.
- Full debug verifier: pass. The earlier `UnauthorizedAccessException` was the
  execution sandbox denying Windows `File.Replace`, not a game/profile lock.
- Release bridge omission audit: pass.
- Debug DLL SHA256: authoritative value in `verification.json`.

## Objective

Rebuild the old-owner combat handoff bridge rather than extending the
`.93` preview. The old owner runs a deterministic simulation with the new owner
as target. It advances continuously at `0.5x`, and blends into the destination
simulation when both simulation clocks reach the same frontier.

For latency `L`:

- `t=0`: source owner relinquishes, fake simulation starts.
- `t=L`: first destination state defines real time zero.
- fake time at `t=L`: `0.5L`.
- both frontiers meet at `t=2L`, simulation time `L`.
- no hold, no fake-time rewind and no immediate absolute root assignment.

Incoming destination updates are not continuously applied to the running fake
simulation. During `L..2L` they provide the real clock and may identify large
divergence. Any residual position/yaw after takeover receives one short,
monotone settle phase while real simulation already owns gameplay.

## Invariants

- New owner is the target.
- Fake simulation uses the same native decision logic and deterministic random
  sequence as the destination.
- Only real simulation owns gameplay, damage, effects and ZDO writes. The fake
  sees a cloned, transaction-local status timeline without ticking gameplay.
- Old-owner damage and real hits remain valid. A bridge-only hitbox follows the
  visible fake pose; hidden replicated-root hits are rejected.
- Vanilla `EnemyHud` anchors to a fake `GetTopPoint` only during HUD updates.
  The actor root stays authoritative/replicated; no whole-root or collider
  presentation mutation leaks into gameplay.
- Target movement, including later jumps, is represented in simulation time.
- No backward root or animation jump from packet arrival.
- No arbitrary rotation clamp.
- Unsupported native state falls back without blocking ownership.

## Current Foundation

- `src/MobBridge/MobBridgeTimeline.cs`: continuous fake/real clock and common
  frontier, including monotone CPU-rate adaptation.
- `src/MobBridge/MobBridgeTakeover.cs`: monotone post-takeover position/yaw
  residual.
- `src/MobBridge/MobBridgeRateTracker.cs`: bounded rolling destination rate
  estimator.
- `src/MobBridge/MobBridgeCheckpoint.cs` and `MobBridgeFieldCodec.cs`: complete
  audited AI/control snapshot, target, seed and schema transfer.
- `src/MobBridge/Native/*`: reused low-level native transaction/audit engine.
- `src/MobBridge/MobBridgeProtocol.cs`: event-driven state/clock RPCs and
  transient AI/random hooks.
- `src/MobBridge/MobBridgeVisual.cs`: active old-owner simulation and render
  takeover.
- `src/MobBridge/MobBridgeBody.cs`: scene-query locomotion projection.
- `src/MobBridge/MobBridgeHit.cs` and `MobBridgeHitFilter.cs`: visible-pose
  melee/projectile hitbox and authoritative-root hit rejection.
- `tests/MobBridge/*`: timing, rate, DLL ordering and release-omission audits.

## Performance Boundary

- Default startup installs no `MonsterAI.UpdateAI` hook.
- A checkpoint queues an owner-data apply hook only while pending.
- Destination random and simulation-clock work installs the AI hook only after
  a bridge checkpoint is applied.
- Hooks, per-handoff state and random epochs are removed when the bridge ends.
- One global count check runs from `Plugin.LateUpdate`; idle tracks do no mob or
  AI work.
- Existing five-second RTT timing cache supplies only the initial bridge length.
  A separate two-second per-player simulation-rate cache is refreshed every
  `500 ms` and is read before handoff. Active bridge clocks override it after
  the first sample.

## Verified State

- `verify.ps1` passes all inherited `.86` suites plus the new mob-bridge
  fixtures, emitted DLL audit and field-schema audit.
- Portable bridge state covers 44 audited AI fields, 13 control fields and 5
  movement fields; schema hash `955594408`.
- Destination CPU-rate estimates use at most eight samples per active handoff.
- AI and owner-data hooks are absent in the idle state and install only after a
  bridge checkpoint is queued/applied.
- Release build omits all `MobBridge*` and `NativeMobPreviewAudit.*` types.
- Native availability no longer depends on an exact `assembly_valheim.dll`
  file hash. The complete method/field/adapter contract audit remains the gate;
  platform/patcher-modified assemblies with matching contracts are accepted.
- Live multiplayer, Unity physics, Animator and transport timing remain
  untested.
- `tml_ai_test start [RTT_ms] [interval_seconds]` is built in; interval defaults
  to `0.5s` and accepts `0.1–60s`. Its parser/combat regression passes.
- Latest timing change waits for an authoritative destination clock and uses a
  `L + 250 ms` fallback instead of taking over at nominal `L`.
- Takeover settle duration is fixed from the display stretch: `latency / 2`,
  exactly one quarter of `2 * latency`.
- Final blending interpolates the recorded fake pose directly to the current
  real pose. Root corrections are therefore revealed only by blend progress,
  never applied at full size on the first takeover frame.
- Fake AI runs on simulation time. Fixed body poses are interpolation samples,
  never direct render poses.
- Target input is always projected forward. Local targets use simulation time
  as horizon; remote destination targets use `2L - simulationTime`. No target
  position history is replayed into the AI.
- Target prediction turns measured velocity toward `GetMoveDir`, so rapid
  reversals arc monotonically instead of continuing in the stale direction.
  The collider center follows that same predicted root and rotation.
- The transferred vanilla target-search timer is decremented by fake
  `UpdateAI`; the production handoff target remains pinned while the new owner
  is valid.
- Combat parity uses destination-relative native timestamps, predicted native
  game time, native `CanUseAttack`, attack age, weapon cooldown, weapon-review
  cadence and flight/water/lava state. Bridge protocol 2 requires matching
  updated endpoints.
- Fake and real random events execute the same Unity `Random.Range` and
  `Random.value` calls from the shared per-handoff RNG state.
- Destination target and RNG locks count native simulation `dt` and expire
  after exactly `L`; vanilla behavior resumes on the next AI tick.
- Fake freezes at the first attack decision. The old owner waits for the
  destination's 50 ms clock frontier, then takes over without replaying any
  fake movement after that attack point.
- Normal takeover requires the destination's latest observed simulation
  frontier, not old-owner wall time or an extrapolated future tick. The cached
  rate remains diagnostic/fallback context only.
- Latency improvement never accelerates fake simulation toward a larger lead.
  If observed destination already passed fake, takeover follows immediately;
  otherwise real simulation catches monotonically.
- Target-lock Harmony parameters match native `UpdateTarget` names exactly, and
  AI hook installation is transactional.
- Zero-second presentation timing is rejected; injected solo-test RTT seeds the
  initial bridge window.
- Fake body locomotion follows AI takeoff/landing state instead of retaining
  its initial ground/flying mode.
- Fake ground locomotion follows native Walk/Run priority and `CanMove`; large
  takeover residuals settle no faster than configured mob speed.
- Fake collision slides along the contact plane and includes native character
  collision layers.
- Takeover correction never moves opposite current real-root motion; it waits
  until the real root catches a displayed lead.
- Fake water locomotion follows native swim timer, speed, acceleration, turn
  and buoyancy targets instead of ground gravity.
- Post-takeover correction decays fake/real residual offsets around the moving
  real root, preserving forward root velocity and avoiding bridge-induced
  backward display motion.
- Active status effects are captured before the source relinquishes its live
  manager. The fake temporarily swaps in cloned effects for AI/body reads,
  advances only effect time and speed modifiers, and never pays DoT damage or
  replays setup/start FX on the old owner.
- The fake-pose `hitbox` collider replaces the hidden replicated collider only
  while the bridge is active. Proxy hits keep native `HitData` (including its
  status hash) and late damage routing; the original collider is restored on
  takeover.
- Artificial-latency start/stop/reset invalidates the pre-handoff presentation
  timing cache on both endpoints. New tests cannot inherit the previous test's
  per-destination RTT and take over a fake pose too early.
- Vanilla nameplates project the scaled fake collider center and configured
  visual-top height through `EnemyHud`. The HUD scope hook is installed only
  while a bridge track is active and is removed with the track.
- Latency is not capped at 500 ms; the measured route value controls the bridge
  up to the debug test's 1000 ms RTT limit. Internal `0.05 s` native AI calls
  are game cadence only and do not quantize phase boundaries.
- Timeline evidence:
  `docs/agents/issues/mob-bridge-timeline.md`.

## Latest dynamic-window follow-up

Protocol 3 separates elapsed body/game time from native AI dt/pass count and adds paired owner clock/pose/attack and source matching/visual-end
messages. The old comparison snapshot remains untouched. Native fake cooldowns
no longer add route delay to rebased ages; fake cadence is continuous and live
progress is driven once per Unity frame. Root-motion residue is portable; fake
knockback remains omitted. Disabled-capsule bounds must not be read by fake AI.
The hit proxy has independent kinematic physics and scoped FindHitObject routing;
HUD/hit/camera share a cached pose. Unsettled correction is not time-expired.
Source completion is asynchronous and fences epochs; it must be installed on
both endpoints. Matching-time pose diagnostics are distinct from native-root
presentation offset. Future Animator/root-motion and live Unity parity remain
unverified; consult VALIDATION.md and the fresh verification receipt.

## Attached FX presentation (2026-10-29, current)

MobBridgeEffects + MobBridgeEffectAttachment keep actual visual FX on temporary
display anchors, including captured status FX references, new/late network FX,
bone parents/constraints, child lights/audio and particle emission. No FX copies
or simulation/events replay. Physical/gameplay roots are excluded. Native FX
sync callbacks restore logical poses in scoped finalizers; anchors prepare before
native particle/constraint updates via one preserved PlayerLoop insertion.
Status Shadow.VisualFxIds survives source SEMan relinquishment. Destination FX
metadata is owner/epoch gated until source's final visual completion (not only
matching completion). Session hooks warm on server/client; no whole-scene scan
or per-AI scans, idle paths avoid Unity lookups. Source actor remains native;
existing half-playback final smoothing and native locomotion policy unchanged.
Both endpoints need this build for new destination FX metadata. See VALIDATION.md
for exact admission/coverage and unverified live particle/audio behavior.

## Half-playback correction trial (2026-10-29, previous)

User asks to try final smoothing at half stretched playback instead of quarter.
BeginTakeover -> SettleForPlayback receives full elapsed presentation duration;
base correction is 0.5 * that duration. Larger residual extension/completion
unchanged. Inherited-pose startup timing and native locomotion/attack fade policy
unchanged. This does not fix the separate source gaps listed in task workspace
work/further-visual-causes.md. See VALIDATION.md and refreshed receipt.

## Continuous playback-rate follow-up (2026-10-29, previous)

User confirms animation improved; occasional movement jerks remain. Timeline
owner-rate samples now set target + measured accepted sample interval, without
immediately overwriting playbackRate. Integrate analytically advances rate and
fake time; MatchHorizon must not snap it to target. No changed native physics,
recording pose interpolation, takeover frontier or animation policy. Summary
diagnostics now include bufferStarvedFrames/endpointHeldFrames/maxPlaybackRateStep/
maxFrameMs. Startup first motor interval and sealed endpoint waiting are still
possible; latest log has background dungeon/world work and actual collisions.
Do not claim this fixes every live jerk. See VALIDATION.md and receipt.

## Continuous native locomotion (2026-10-29, previous)

User accepts mismatched animation/root speeds to preserve smooth animation.
Visual.UpdatePresentation now uses MobBridgeAnimationBlend.NativeWeight and
recorded AttackWeight metadata: native skeleton for all ordinary locomotion,
buffered bones only for visual attacks with smooth entry/exit and endpoint fade.
No additional graph evaluation or buffer extension; position timeline unchanged.
Private IK/transition gaps remain only in predicted-attack contribution. Native
idle/clip holds are still possible; do not promise all poses always move. Full
details/tests in VALIDATION.md. Keep previous FPS/club/render fixes.

## Further animation-start audit (2026-10-29)

Keep the FPS/club/late-render fixes. Initial takeover direction now comes from
the accepted destination frame velocity, closing backward correction before
the first nonzero replica-root delta (reproduced 1.000 -> 0.896 in 20ms before
fix). Existing later direction tracking and stationary correction unchanged.
See VALIDATION.md for regression scope and remaining limits.

Animation-start gaps remain separate: t0 is native visible IK pose; later poses
are private graph without CharacterAnimEvent look-at/foot IK. Active Animator
transition state is counted but not copied. Latest client log has 60 setups,
zero active transitions, zero measured draw-bone gaps. Full rig playback slows
the run cycle since the visual-attack experiment; no new speed change made.
Sealed-buffer endpoint and no-backwards correction holds still exist. Do not
claim these older design constraints prove the recent startup symptom.

## Next Steps

- 2026-10-28 log follow-up: zero-time attack release now requires `ClockSeen`
  plus the observed frontier; elapsed wall time no longer releases attacks.
  Takeover position is updated explicitly from `Advance`, and HUD/hitbox/camera
  consumers only read it. Established direction remains valid across unchanged
  replica-root samples. Existing track expiry remains; metre-scale residual
  causes and live Unity behavior are not resolved by these two fixes.

1. Run the full verifier after closing Valheim, then package/install the debug
   DLL on both endpoints.
2. Add end-to-end protocol fixtures using modeled RPC order.
3. Add two-player target-change, fake-pose hit, status, knockback,
   random-choice and repeated handoff
   live tests.
4. Compare 50/100/200/500/1000 ms live behavior.
