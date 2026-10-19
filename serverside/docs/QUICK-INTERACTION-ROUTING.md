# Quick interactions: where forwarding applies

2026-10-16. Research only. No runtime, config or package change. This extends [the single-harvester review](SINGLE-HARVESTER-REVIEW.md); it does not enable harvesting in the current five-group prototype.

## Fixed scope

Use the user's conditions: one interactor during an ownership transition; the interactor sees either their own ownership or server ownership; initial-login input is unavailable. Do not use a simultaneous second harvester as the counterexample. Inventories, editing and long-running work are outside this short-action assessment.

Inspected installed game: SHA256 `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`, MVID `65d2b4f8-e94e-4805-8a80-1451498a82d7`. `work/ServerSideHandoffAudit-2026-10-16/QuickInteractionAudit.cs` passed 47 actual-assembly contract assertions and 3 small receive-rule countermodels. The IL dump is `quick-contracts.txt`. Countermodels are not a live reproduction or a full replica of Unity/network scheduling.

## The narrow route remains useful

When a genuine owner-directed request arrives at the server addressed to its previous owner:

1. Resolve the exact live ZDO and supported component/request contract.
2. If the server is still owner, execute the original native request normally.
3. If ownership has moved to the requesting client, order the current native owner/state send before forwarding the original request to that client. Preserve sender, payload and message identity; suppress original execution.
4. Never execute on the old server copy first and then forward. Do not forward a response, broadcast, completed result or generic unknown method.

This is forwarding an unexecuted original request, not retrying a possibly executed operation. Already-local use stays local. No routine two-ping grace or new acknowledgement round trip is required by this narrow candidate. A rare forwarded action does travel back from server to client, and a required state send can wait for native scheduling/backpressure; do not describe that path as zero-latency.

`SetOwner` does not synchronously send. `ForceSendZDO` only queues native sending, so calling it and immediately forwarding is insufficient. A production adapter must prove that the exact object was actually submitted to the same connection before the request. Per-peer sync metadata alone is not an acknowledgement: native receiving also updates it. Native Steam reliable messages on the same lane are ordered; this does not prove crossplay or arbitrary transport-mod behavior. Official transport reference: https://partner.steamgames.com/doc/api/ISteamNetworkingSockets#ReceiveMessagesOnConnection.

One candidate is rare pending-request bookkeeping tied to the real native send boundary. It should exist only while an actually stale request awaits its necessary state send, be removed on destruction/disconnect/session end, and revalidate changed ownership before dispatch. It is not implemented. Avoid serializing every world object twice, world scans, unlimited retry queues and a generic handoff timer.

## Short does not identify a protocol

| Native action | Server-to-client stale request | Player-local completion crossing takeover |
|---|---|---|
| `Pickable.RPC_Pick`: berries, crops and many world pickups | Good first adapter candidate. Native handler rejects non-owner execution; route before that rejection. Preserve the bonus argument and completion broadcast. | Native `RPC_SetPicked` updates the local picked flag before its owner test. A loaded newly owning server can persist that completion. This supports the user's local-harvest case; previous conditional model passed. Not a universal game/scene safety proof. |
| `PickableItem.RPC_Pick` (`Pick` on the wire) | Similar route shape, but native local `m_picked` and pending destruction must be respected. A completed drop followed by queued destruction must not be mistaken for an unexecuted request. | Native destruction is queued while owner and the destroy receiver does not reject merely because ownership changed. This is a different completion contract from `Pickable`. Dependency ItemLedger/destruction behavior must remain intact. |
| `Beehive.RPC_Extract`, `SapCollector.RPC_Extract` | Can intercept and route before native execution even though these handlers lack an owner guard. Do not rely on the old owner rejecting them. Correct current quantity/state is needed. | No authoritative picked-style completion. Local extraction spawns drops and resets a ZDO quantity; later whole-object replication can conflict with the new owner's writes. Routing alone cannot fix this direction. |
| Door `UseDoor` | Owner-addressed toggle is a routing candidate, but the native handler itself lacks `IsOwner`; it also consults local animation state. Keep original key/access checks and execute once. | No picked-style completion. Do not promote Pickable's result to every door or key-consuming variant. |
| Ward `ToggleEnabled` / `TogglePermitted`, bed claimant `SetOwner`, map-table `MapData` | Explicit owner-directed operations with native owner checks. Good type-specific candidates for ordered routing. Bed claimant is not the network owner. | Ordinary direct ZDO state writes; no generic completion/merge guarantee. Optional GuardianStones policy must take precedence. |
| Feast `RPC_TryEat` | Route only the original try-eat. The owner decrements servings and sends `RPC_EatConfirmation` to the original user. | Effects broadcast is not an authoritative serving-count completion. Never replay the confirmation or substitute the server as sender. |
| Archery-target arrow return | `RPC_DropArrows` delegates to owner-checked quantity/reset work. Routing candidate after actual state ordering. | Resets quantities directly; no Pickable completion. Projectile hits are separate events outside the fixed interaction-radius assumptions. |
| Refill inserts and fire toggles | Many are immediate owner-checked RPCs; preserving original arguments can fix stale destinations. Local inventory removal before the insert, native capacity and backend ledger policies remain relevant. | Direct ZDO quantity writes plus autonomous production/consumption. Not proven safe merely because each handler is synchronous. |
| Chair, workstation UI, Vegvisir, dungeon entry and similar local actions | No owner request to reroute for the inspected native actions. | Do not add an ownership RPC or transaction guard solely because an object implements `Interactable`. |
| Trap activation, fish pickup, loose-item pickup, stands | Not a generic short command: native grants, delayed owner actions or pickup completion extend beyond the initial click. | Preserve native policies; not covered by the narrow adapter. |
| `Switch` / `ToggleSwitch` and unknown mod components | Dispatch arbitrary host callbacks. A shared RPC name or short `Interact` method does not identify action semantics. | Classify the real host component; unknown callbacks need an explicit adapter. |

Thus ordered forwarding can repair the stale destination part of several synchronous RPC contracts, but it is not a universal quick-interaction handoff protocol.

## Specific beehive answer

### The player already owns it locally

`Beehive.Interact` invokes `RPC_Extract` on the locally recorded owner. If that owner is the same client, native `ZRoutedRpc` dispatches locally: no extraction request reaches the server. The client spawns honey and `ResetLevel` writes `s_level=0`. Native ZDO syncing later carries that state.

The server becoming owner does not prevent it from receiving a later client update, but receipt does not guarantee applying its quantity. Installed `RPC_ZDOData` uses whole-object `DataRevision`:

- If incoming data revision is not greater, it skips the payload; it may update only a newer ownership revision.
- If incoming data revision is greater, it imports data and the packet's ownership fields together.

A valid conditional trace needs only one harvester: both endpoints start at revision 10 with four honey. The old client harvests and creates revision 11 with quantity zero. The newly owning server's normal `UpdateBees` writes time/product bookkeeping before that packet arrives, reaching revision 12. Revision 11 is then skipped, leaving the server's honey quantity unchanged. Alternatively, a newer client packet can carry old ownership back as well as its quantity. The three countermodels exercise exactly these native receive rules. This establishes a protocol gap; occurrence/frequency under the real fork, scene and frame order have not been measured.

The same audit found the analogous quantity-reset/time-bookkeeping structure in sap collectors. The sap effects RPC does not apply the quantity reset.

### The player still sees the server as owner

Now a real `RPC_Extract` reaches the server. If still server-owned, it completes natively and resets the authoritative quantity there. If ownership has already moved to the harvesting sender, intercept before execution and use the ordered route above. This is the direction forwarding can cover. Never execute on the old server copy and then also forward.

## Decision

Prefer a narrow ordered-route experiment for native `Pickable` first; no blanket ownership delay or repeated object polling. Do not claim all harvesting is covered, and do not add bee/sap/refill rebalancing on the strength of that result. Their player-to-server final-state contract needs a separate minimal solution or a deliberately narrower ownership policy. No such policy or guard was changed here.

The follow-up [direction-specific decision](DIRECTIONAL-HANDOFF-DECISION.md) selects cancellable A-to-server pending jobs only for contracts without verified native completion, alongside ordered server-to-A request routing. It explicitly retains A during grace and discards the takeover on source activity, while recording the unresolved strict-guarantee boundary and the native RequestZDO alternative.
