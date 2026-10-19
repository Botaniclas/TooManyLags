# Direction-specific short-action decision

2026-10-16. Latest design decision under the user's fixed single-interactor conditions. Research only; no production changes, new category support, config or release.

Implementation follow-up: [local 0.2.0 candidate](HANDOFF-IMPLEMENTATION.md). The implemented rare route uses a targeted native one-object snapshot instead of a separate pending request queue. This document records the earlier decision and remaining guarantee boundary.

## Requirements used

- A player interacts only with an object they see as locally owned or server-owned.
- A second player entering ownership coverage cannot also harvest the object during the transition considered here. Do not require a two-interactor counterexample.
- For player A to server, keeping A as owner during a pending handoff preserves the fastest local interaction path. If source activity arrives during that pending period, discard the requested takeover completely. Do not merely extend a timer, run the old request later, or start an automatic takeover when activity stops.
- For server to A, prefer ordered routing of an already stale, unexecuted native request rather than a routine grace period on all handoffs.
- Synchronous handler duration is not a completion contract. Inventories, editing, native grants and deferred actions remain separate.

## Selected policy

| Action contract | A to server | Server to A | Reason/cost |
|---|---|---|---|
| Pure local use: chairs, crafting UI, Vegvisir/dungeon entry | No new interaction guard | No new interaction guard | Inspected action has no owner-directed command to protect. Existing object simulation policies are separate. |
| Native `Pickable` picked completion | Use native completion; do not add a grace job solely for this harvest | Ordered stale-request routing | Best first timer-free harvesting adapter. Keep bonus argument, broadcast, actual state ordering and loaded-instance assumptions. Conditional model supports completion crossing takeover; not a universal game-version guarantee. |
| Native `PickableItem` drop plus destruction | Preserve native destruction completion; a terminal/pending-destroy object must not be treated as an unexecuted harvest | Ordered routing only before execution; respect local picked and pending destruction state | Separate contract from `Pickable`. No blanket timer as the first fix, and no retry of an already completed drop. Validate dependency ItemLedger/native destroy handling before enabling. |
| Bee/sap extraction and other immediate quantity mutations without an authoritative completion message | Cancellable pending takeover, retaining A as owner | Ordered stale-request routing before native execution | Retains local harvesting and avoids routine delay on acquisition by A. Source state arriving during grace cancels the takeover. This is bounded mitigation, not a final-state proof. |
| Other immediate owner commands with only ordinary ZDO writes: doors/ward toggles, bed claimant, map write, feast serving count, archery-arrow quantities | Conservatively use the same cancellable path until that component has a verified timer-free completion contract | Type-specific ordered routing; responses/broadcasts excluded | Do not label native replication alone a completion acknowledgement. Already-local actions still need no RPC to the server first. Exact component contracts and dependency policy take precedence. |
| Immediate refill insert/toggle portion | Cancellable source takeover for ordinary direct state mutations, if enabling dynamic rebalancing | Ordered insert/toggle routing preserving arguments and user; no routine server-to-A grace | Inventory removal/capacity and backend ledger behavior still apply. A cooking/fermenter output or offering spawn that is actually deferred is not included just because its start click is short. |
| Inventories, text, stands, vehicles, native pickup/control grants, delayed tap/burn/spawn | Keep separate existing/native retention policies | Keep separate existing/native policies | Do not expand short-action routing into long transaction protection. Unknown mod callbacks remain unclassified. |

This selects the smallest shared mechanisms without pretending every native action has the same protocol. The conservative direct-write group can be narrowed only when a component-specific completion contract is established. Do not add an arbitrary timer to every loaded object.

## Cancellable pending takeover, precisely

1. Return immediately when actual owner already equals the desired owner. No job, ping read or activity tracking.
2. For a supported actual A-to-server attempt, keep owner A and store one pending job: exact identity, A, source ownership revision, current target/decision and deadline. Reuse a brief per-peer RTT sample if testing two-RTT grace; do not sample the socket for every object.
3. Process incoming native events normally. If relevant source activity arrives for that pending object, delete the job; do not reject the interaction or roll back its result.
4. When due, validate identity, source ownership, current target and native/backend vetoes once, then apply or discard. Repeated identical coverage must not restart the pending window.
5. Destruction, unload, native release/ownership change, disconnect and world shutdown remove obsolete jobs. Cancelled completion never schedules itself again; a fresh field request may make a new attempt.

For bee/sap, a positive-to-zero quantity change is useful evidence but not an exact event log: the server's baseline may already be zero while the client produced and harvested between snapshots. Native packets can coalesce writes. During a short pending handoff, conservatively cancelling on any newly applied authoritative source-object data avoids claiming that an unchanged final quantity proves no harvest. This can cancel on ordinary production/other updates too; it trades fewer automatic takeovers for preserved locality. Do not parse every incoming packet twice or inspect all fields every physics tick.

Use a shared due scheduler, not one coroutine/Unity repeating timer per object. An empty pending set needs no object servicing. Native receive hooks can exit on a zero pending count, otherwise look up only the actual deserialized object. Events carry source context from the native receive operation; a payload from another peer is not the source owner's completion. Existing field/unload events maintain scope. No added 20 ms world poll or per-frame proximity scan.

At 100 actual timed takeover attempts/second and a 0.1-second grace, roughly 10 jobs are concurrently pending before cancellations. This is a scheduling estimate, not measured CPU performance. Main ongoing work remains real object changes and native networking; memory does not grow with historical world cells.

## Ordered request route

Only the stale exception takes the extra server-to-client trip. Preserve the original user, message, arguments and exact object. Deliver actual current owner/state before the command through the native ordered connection, suppress original execution, and never retry a possibly completed operation. Ordinary local interactions remain local. Ordinary server-owned use executes natively if ownership has not changed; its already completed action does not require routine cancellation of a valid server-to-client handoff.

Neither `SetOwner` nor `ForceSendZDO` establishes actual submission of the state packet. The real send boundary must be verified; a forced-list removal alone also fails as proof, because `AddForceSendZdos` removes entries that `ShouldSend` rejects without sending them. The route is still a candidate, not an implemented guarantee.

## Additional native alternative checked

`ZDOMan` registers routed `RequestZDO` on both endpoints. It queues a force-send request but does not synchronously flush an object or acknowledge an action cutoff. `AddForceSendZdos` still calls `peer.ShouldSend`. If the peer is already recorded as knowing the same data and ownership revisions, the request can produce no ZDO response at all. Received ZDO data also updates that record. Therefore a targeted native request is useful for synchronization but is not a reliable final-state handshake or a universal substitute for pending grace.

Requesting a snapshot before takeover still allows the old client to act after that snapshot. Pausing server production only removes one revision writer, not the missing client cutoff/final-state proof. Stable server ownership avoids transitions but loses local execution; retaining native client ownership avoids reclamation but gives up shared-server balancing. None is an equivalent guaranteed replacement for all requested behavior. Do not spoof data revisions, send fake inventory opens or add global ownership writers to force an acknowledgement.

## Guarantee boundary

The cancellable design catches source activity that reaches the server before its deadline, with no additional latency on already-local harvesting. It cannot certify an action update that has not arrived by the deadline. Native two-RTT telemetry is not an upper bound on delivery plus client send scheduling. The same single actor can act near a deadline; no second harvester is needed to establish that limit.

Thus the selected combination is the practical low-overhead experiment, not a claim of guaranteed transitions for all immediate quantity actions. A strict general guarantee while preserving arbitrary bidirectional takeovers would need a real old-owner cutoff/final-state acknowledgement, or a narrower ownership policy. No native universal handshake was established by this audit, and no extra client requirement was introduced.

Evidence: `DirectionalDecisionAudit.cs` checks the installed-game request/filter contracts and the cancel/discard semantics offline. Native Steam ordering reference: https://partner.steamgames.com/doc/api/ISteamNetworkingSockets#ReceiveMessagesOnConnection. No live multiplayer or measured CPU/latency result is claimed.
