# Two-ping ownership grace: design assessment

2026-10-16. **Design only. No runtime/config/package change.** This revisits a bounded grace period, not a claim that time alone proves a remote owner is idle. Local player execution and both directions of ownership transfer remain requirements.

**Latest refinement:** [Single-harvester review](SINGLE-HARVESTER-REVIEW.md) checks the user's no-simultaneous-users condition, retracts treating an unseen local Pickable completion as a demonstrated error, and narrows the remaining late-request case. It also corrects the blanket action-cancellation rule below for already completed short server-owned actions.

## What the delay would mean

Create a pending job only when an actual managed object's current owner differs from its requested owner. Keep the current owner until the job can complete. Already-local interactions continue through their native local path; do not ask the server for permission before every interaction.

For a server/player transfer, use the relevant player's measured RTT in milliseconds:

`graceSeconds = 2 * pingMs / 1000`

For a player/player transfer, a conservative experimental choice is twice the larger of the source and destination RTTs. That is a heuristic budget, not a protocol completion calculation. Do not use a distant unrelated player's ping or the server's average across every peer.

| Measured RTT | Grace before takeover |
|---|---|
| 25 ms | 50 ms |
| 50 ms | 100 ms |
| 100 ms | 200 ms |
| 200 ms | 400 ms |
| 500 ms | 1000 ms |

The first minimal implementation would start this window at the actual takeover attempt, after ordinary grid selection/timers, so this is **additional ownership delay**. Reusing a grid timer could avoid adding the periods only if the same source, source revision and desired target were already recorded and remained valid during that timer. The current pure field policy does not establish each object's actual source state; do not add early object/world scans merely to count that interval as grace.

## Native ping source

Rechecked against the installed game's metadata/IL, SHA256 `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`:

- `ISocket.GetConnectionQuality(out float, out float, out int ping, out float, out float)` provides the native per-connection metric.
- The installed `ZSteamSocket` implementation reads Steam's `m_nPing` from `GetConnectionRealTimeStatus`. Failed reads return zero; zero must not mean an instantaneous safe transfer.
- `ZNet.GetNetStats` averages peers on the server and is the wrong source for one specific user's handoff.
- `ZRpc.GetTimeSinceLastPing()` returns the age of the last ping reply, not its RTT. Do not use it as the delay.
- Other socket providers need their own validity check; the interface alone does not prove useful RTT telemetry. No new client echo protocol was introduced.

Cache a valid reading briefly per involved connected peer rather than invoking native socket telemetry for every object in the same cell. Prune it on disconnect/session end. For an unavailable metric, leave the ownership decision native/unapplied rather than silently interpreting it as zero or inventing a safety guarantee.

## Pending-job lifetime

One job per object stores the exact ZDO identity, source owner/revision, requested target and deadline. It is not a remembered record for every historical field.

1. Same owner already installed: return before allocation or ping lookup.
2. A real eligible takeover: capture source/revision/target and calculate the deadline once.
3. Repeated coverage with the same still-valid decision does not reset the window. Resetting it on every movement could prevent ownership changes while walking.
4. A still-running protected action/native user grant can veto that object's automatic takeover. An already completed short server-owned action does not require blanket cancellation: preserve and order its final state instead. Never reject the user's action, freeze the whole field, or replay cancelled takeover work merely because time passed.
5. At the deadline, compare the current grid decision, source owner/revision, object identity, readiness and supported native busy state again. A stale decision or active use discards the job.
6. Destruction/unload/disconnect, native ownership changes and native releases invalidate the pending job. Keep-on-exit retention must not turn an obsolete pending job into a later return-to-server operation.
7. A later genuine field request can make a new attempt. Activity ending or a timer expiring after cancellation does not resurrect the old request.

Only server-visible signals can cancel a job. Owner-local actions and text-input start remain invisible to this mechanism. A false replicated use flag still is not a permission grant. This design must not enable inventory, text, stand or other unsupported categories just because a deadline was reached.

## Avoid fast global polling

Native action hooks can cancel jobs when an observed request arrives. A final status read at the deadline handles ongoing supported use. Neither requires checking all objects every 20 ms. Polling pending objects would only be useful for a specifically demonstrated state not covered by those two paths.

The current prototype services policy/object work through the movement check, default 0.1 seconds. Adding a deadline without changing that servicing can postpone completion to the next pass. If an experiment is implemented, expose the next due job time and service only due jobs on an ordinary server frame, independently of the movement interval; do not rebuild player coverage for every deadline.

A 30 FPS server cannot actually perform 50 distinct frame-based checks per second. Never catch up with repeated checks of identical state in one frame. There is no native network round trip in a local dictionary/flag check, but there is still server work.

As a scheduling estimate, 100 requested changes per second and a 100 ms window produce roughly 10 concurrently pending jobs before cancellations. That is not a measured CPU benchmark. State remains bounded by loaded managed objects and connected peers; stable/no-change periods add no takeover jobs.

## What this can and cannot catch

**Useful case:** player harvests at 0 ms, grid requests transfer at 20 ms, the action reaches/is observed by the server at 40 ms. A 100 ms window measured from 20 ms lets that action run at the current server owner before takeover. A completed short action need not cancel the valid takeover; ongoing protected use still can. This matches the user's proposed sequence without unnecessarily pinning a finished action to the server.

**Remaining boundary:** after a request at 20 ms with 50 ms RTT, the deadline is 120 ms. If the old client acts at 110 ms, its update can arrive at 135 ms. The server can finish its idle-looking check at 120 ms before seeing it. Sampling every 20 ms and restarting the timer on every observed action does not observe an update before arrival. The same issue applies to starting a new window after a quiet period.

For Pickable, `RPC_SetPicked` is an existing completion broadcast and may carry a locally completed harvest to the new owner. Bee/sap harvesting uses different quantity/reset behavior. `SetOwner` itself does not request or flush the old client's final state. Assess these components separately rather than replacing their native transaction semantics with a universal timer.

For the narrow server-to-harvesting-sender stale-request case, cancelling a pending transfer when the server receives the request can keep execution at the still-current server. Once transfer has already committed, the request still needs correctly ordered routing/state delivery; the elapsed two-ping window does not repair it. No retry/replay was added.

## Latency decision

Latest refinement: [Direction-specific decision](DIRECTIONAL-HANDOFF-DECISION.md). A-to-server grace explicitly keeps the player as owner and discards a takeover on observed source activity; server-to-A prefers ordered stale-request routing. The user's preference for retaining local harvesting applies to the former direction. This refinement is still research and does not turn an RTT estimate into a final-state acknowledgement.

The window adds **no mandatory round trip to every already-local action**. It delays when a newly requested owner becomes local. Approaching players may not feel this if coverage starts early enough; a standing player gaining an object after another player leaves may retain server round-trip behavior for the extra window. Poorer connections lengthen that period. This cannot be advertised as universally latency-neutral.

Under the requirement to avoid extra perceived latency and avoid unproven guards, leave it unimplemented by default. It is a feasible optional experiment to reduce certain races, not a complete inventory/harvest handoff guarantee. A live comparison would need to demonstrate fewer failed interactions without slower nearby-owner acquisition before adopting it.
