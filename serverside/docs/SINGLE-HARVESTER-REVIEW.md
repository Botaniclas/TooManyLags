# Revised limits under the single-harvester constraints

2026-10-16. **Assessment only; no runtime, config or package change.** This is the current refinement of the broader warnings in `TWO-PING-HANDOFF-WINDOW.md` and `SERVER-ONLY-HANDOFF-ALTERNATIVES.md`.

Fixed assumptions supplied by the user:

- An ownership transfer does not coincide with two different players interacting with the same object.
- The interactor uses only their locally owned object or an object they see as server-owned.
- A joining player is not counted as an interactor while initial login input is unavailable.

Follow-up: [Quick-interaction routing](QUICK-INTERACTION-ROUTING.md) checks additional native action contracts and the distinct bee/sap quantity-replication issue. Its broader classification does not turn this Pickable-only model into a general handoff proof.

The cases below use **one interactor throughout**, including the failure case. No simultaneous second harvester is needed or assumed.

## What was actually checked

`work/ServerSideHandoffAudit-2026-10-16/SingleHarvesterAudit.cs` passed **15 installed-game IL contract checks and 61 offline event-model checks**. The game assembly is SHA256 `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`.

The IL checks verify the model's specific premises: Pickable's owner rejection, picked-completion dispatch, local completion before remote routing, completion setting the local picked flag before testing ownership, Steam reliable flag 8, queued message copies, and the differing bee/sap handlers. They also verify that SetOwner does not synchronously send the final state and native ZDO sending is separately scheduled/budgeted.

The event model assumes loaded copies, a live native FIFO Steam connection, no other owner writer, fixed 25 ms delivery in each direction and atomic short native handlers. It does **not** simulate Unity scene lifecycle, real item instantiation, every ZDO revision conflict, crossplay, arbitrary component extensions or a live packet capture. Passing it is not a general gameplay safety proof.

Steam's reference defines flag 8 as reliable and guarantees ordering for reliable messages on the same lane. Opposite directions have no shared order. Valheim's inspected native path copies and enqueues each message and processes received RPCs in order. This evidence supports the native Steam case, not every networking modification:

- https://partner.steamgames.com/doc/api/steamnetworkingtypes#message_sending_flags
- https://partner.steamgames.com/doc/api/ISteamNetworkingSockets#ReceiveMessagesOnConnection

## Corrected conclusions

### Locally completed Pickable harvest crossing player-to-server transfer

The native completion message matters. `Pickable.RPC_Pick` creates the drops and broadcasts `RPC_SetPicked(true)`. The broadcast handles its local endpoint first; `SetPicked` updates `m_picked` before its owner test. When a loaded server has just become owner, the later completion can set its picked state and write the normal persistent state there.

In the model, 55 action times around the ownership change, including after the server decision but before the client learns it, all produce one harvest and the picked state at both endpoints. There is no second interactor harvesting stale server state in this model.

**Correction:** 'The server cannot see the last local action yet' is an information gap, not by itself evidence that this Pickable harvest is lost, duplicated, or needs a new client guard. The completion channel supplies native recovery. The earlier broad wording overstated this as a demonstrated gameplay problem. Full ZDO/scene corner cases remain untested, so do not promote this limited result to a guarantee for every harvestable type.

### A server-addressed Pickable request crossing server-to-player transfer

This remains a concrete permitted failure path:

| Time | Single-user sequence |
|---|---|
| 20 ms | Server starts a transfer request to player A. Two times 50 ms RTT gives a deadline of 120 ms. |
| 110 ms | A still sees server ownership and sends the native pick request to the server. |
| 120 ms | Server installs A as owner. |
| 135 ms | The server receives the request. Native `Pickable.RPC_Pick` rejects execution because the server is no longer owner. |

This late request is rejected in the model even with cancellation for server-observed actions during the wait. A 200 ms grace with an action 10 ms before its later deadline has the same outcome. Those checks validate the event-order counterexample; they are not a claim that a live failure has already been reproduced in this prototype, which does not manage Pickables yet.

An original action already sent to the server can be forwarded to A without a new acknowledgement round trip. In the model that repairs the case when A has received ownership. It still fails if the native owner-state send is budget-delayed and the forwarded command gets there first. Explicitly ordering current owner/state before that command repairs this **narrow loaded-object model**. A production implementation would have to preserve the real native serializer, revisions, transport and dependency policy; no such send patch was implemented.

### Bee/sap extraction is not the Pickable rejection case

Both inspected `RPC_Extract` methods lack Pickable's IsOwner rejection. A late server-addressed extraction is therefore not proven lost merely because the server no longer owns its ZDO. Their direct quantity reset and native state synchronization need separate evaluation. Bee extraction has no completion RPC; sap's update-effects broadcast is not a complete quantity snapshot.

**Correction:** do not apply the Pickable failure trace or a universal replay/forward rule to bee/sap extraction automatically. This audit neither reproduces nor excludes every quantity/revision race for them.

### An early completed short server action need not cancel the takeover

The previous proposal cancelled a pending takeover on any observed relevant action. That is too broad for a synchronous short action already completed under server ownership. It can leave an object with the server until another field request, even though the server has the finished authoritative state and the desired handoff is still valid.

The corrected design distinction is:

- A still-running protected action or actual native user grant can veto/invalidate the automatic takeover according to its exact contract.
- A short completed server-owned harvest needs its final state preserved and delivered in order. Do not discard its pending takeover solely because the completed action was observed.
- A stale grid target, changed source/revision, unload or native release still invalidates obsolete work; never replay it after cancellation.

Regular 20 ms world-object scans are not required by any of these paths.

## Latency and adoption

Two times 50 ms RTT means 100 ms of additional takeover grace in the simple design. It is a delay in reaching the new owner, **not 100 ms added to every local interaction**. With early coverage, much of it can pass before use. No live evidence establishes that a normal player would feel a slowdown, and no live evidence establishes universal latency neutrality either.

The precise outcome is narrower than the earlier blanket rejection: **two-ping grace can catch the user's early-action sequence and be an inexpensive optional experiment. It does not on its own address late server-targeted Pickable requests.** The useful next candidate is an ordered native state/request route for that exact case; local harvesting does not need to be sent to the server first. Inventories, private text input and long transactions keep their separate requirements.
