# Recorded handoffs for third-party observers

Old-owner presentation and recording duration keep their current behavior. The
observer receives that recording rather than predicting the mob independently.
No source target inputs, RNG choices, attacks or collision results are recalculated
for another viewer. Recordings remain append-only and become read-only when sealed.

## Timing

The descriptor carries the actual existing source capture window and initial
destination simulation rate. The observer starts when a usable interval and the
committed owner epoch are available. Its playback clock starts locally; its real
frontier is derived from the destination's clocks as those reach this observer.
Observer ping is not added again to an already delayed pose or used to change
the source capture window.

For an idealized **100ms recorded simulation interval**:

| Observer wall time | Available information | Local display |
| --- | --- | --- |
| 70ms | Source cutoff / first usable recorded interval | Begin fake playback |
| 170ms | First real state at simulation zero | Continue fake playback |
| 270ms | Observed real state at simulation 100ms | Match horizon; begin correction |
| 370ms | Nominal 100ms correction elapsed | Native display if aligned |

This is the accepted 200ms playback / 100ms correction example. It assumes
continuous data, rates of one and ideal arrivals. Real capture/send/frame spacing,
adaptive remaining-buffer slowdown and residual-size correction affect the exact
wall times. The implementation does **not** change the old owner's current
roundtrip-based capture-window calculation to force this illustrative 100ms value.

Missing incoming samples slow only unconsumed playback. The display cursor cannot
pass the received endpoint, rewind or consume a predicted future frontier.
Destination progress can guide scheduling, but takeover needs an accepted real
clock at or beyond displayed simulation time. If slowdown cannot cover a gap,
the display waits, following the existing user decision. Final correction reuses
the shared position/rotation/model-tilt/velocity transition and current-frame
settlement checks.

## Scope and transport

- Debug bridge only; compatible updated server, source, destination and observers.
- Source exports existing native root/model samples, plus exact skeletal samples
  around visual predicted attacks. Locomotion retains native bone animation.
- Stable relative rig paths map bones without requiring identical local FX trees.
  Unknown attachment transforms retain their local native pose.
- Reliable presentation RPCs carry epoch/token/sequence; no fake ZDO writes or
  health, damage, status, AI, physics or ownership changes on receiving observers.
- Server selects ready compatible observers in vanilla's active area at start,
  excluding both owners. Later packets check relevance again. No global fanout
  and no source stream when the connected-client capability count precludes any
  observer. Interest changes are announced during connection negotiation.
- First endpoints prime interpolation promptly. Subsequent batches target 50ms,
  like native ZDO send rounds; actual packet/frame spacing may be longer. Each
  skeletal batch decodes independently using exact value deltas within that batch.
- Limits bound pending streams, samples and packet size. Only pending unloaded
  observers expire on the initial timeout; an active correction is not removed
  on that timeout. Epoch changes, unload/teardown and disconnects retire references.
- Observer clock demand lasts until the endpoint is actually sent. It does not
  keep source RNG or target locks alive after old-owner matching completes.
- Clock relay failures cannot escape into native AI. The latest failed frontier
  retries independently of further destination clocks; no extra simulation runs.
- Source and observer presentation capacity are separate. Passive observers cannot
  fill the old-owner recording slots. When the last observer finishes or leaves
  range, source export stops while local old-owner recording/display continue.

Server-origin handoffs have no client old-owner recording and retain the existing
legacy observer path. Unsupported recordings do likewise. A peer entering range
after the initial descriptor is not backfilled into that recording. These paths
continue using native/legacy presentation rather than running a second fake AI.

`ai_test_v`/`ai_test_vanilla` bypass the source stream. `ai_test` retains its solo
safety guard; live three-client validation requires actual target-driven handoffs.
No installed profile is modified by the build or verifier.
