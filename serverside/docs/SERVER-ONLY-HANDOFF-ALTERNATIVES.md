# Server-only handoff alternatives

Assessment: 2026-10-16. **Research only: none of the alternatives below expands the prototype's runtime categories or replaces its guards.**

The useful alternative is often to avoid a risky automatic handoff, rather than invent a server copy of a private client lock. This can preserve ordinary interaction while giving up some proactive grid rebalancing. It is not feature parity with the client-assisted edition.

## Evidence and limits

- Installed `assembly_valheim.dll`: SHA256 `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`, MVID `65d2b4f8-e94e-4805-8a80-1451498a82d7`.
- `work/ServerSideHandoffAudit-2026-10-16/InspectContracts.cs`: **53 passing assertions** against that actual assembly. `native-contracts.txt` records the inspected IL; this is static evidence, not a multiplayer test.
- Decompiled explanations: `work/InteractionTypesAudit-2026-10-14/sources/`; older snapshots are not assumed to have identical signatures. For example, current installed `ItemStand.RPC_SetVisualItem` differs from the earlier snapshot. Any implementation must bind the actual supported contract.
- Dependency source: MistrCech/Sarkastic.gg Dedicated Simulation commit `e31165bbe9a81b63c1e213965c8e699af15e1907`. Released 1.18.0 DLL and source are separate baselines; new integration patches would require a released-binary audit as well.

`ZRoutedRpc.InvokeRoutedRPC` executes a call addressed to the current local peer without passing through the dedicated server. Consequently, routed traffic is useful evidence of an action, but absence of routed traffic is not proof that the owner is idle.

`ForceSendZDO` requests a native send. It does not acknowledge a loaded scene, inventory, collider, or completed client action. A transport ping and a newer data revision are not substitutes for those acknowledgements.

## Follow-up: preserve local harvesting and free transfer in both directions

The current requirement rejects permanent server ownership, native-only retention, or a routine extra interaction round trip as a solution for harvesting. Those policies remain documented alternatives for other requirements; they do not satisfy this one. **No new harvesting or delay guard is implemented.**

A concrete two-ping grace design, native telemetry source, timer lifecycle, cost and adoption limits are assessed separately in [Two-ping ownership grace](TWO-PING-HANDOFF-WINDOW.md). It adds no routine interaction round trip, but does delay actual owner acquisition and remains incomplete protection.

The [single-harvester refinement](SINGLE-HARVESTER-REVIEW.md) is the latest assessment under the user's fixed conditions. It distinguishes Pickable's native completion recovery from a proven lost-request path and corrects blanket cancellation of already completed short server actions.

Server-side forwarding of an already stale harvest request can improve delivery without an extra acknowledgement round trip: the request already travelled to the server, and forwarding returns it to its harvesting sender. However, the receiving client must get the appropriate owner/state before executing it. `ForceSendZDO` only queues work; `SendZDOs` can skip a send because of queue limits or omit an individual object because of budget. Simply forwarding after scheduling a force-send does not establish that ordering. A correctly ordered targeted send would address this narrow direction, not finish the full handoff protocol.

The harder direction is player to server. A native client can finish a local action while the server has not yet seen it, including after the server decided to transfer ownership but before that decision reaches the client. Pickable broadcasts its picked state, whereas beehive extraction resets quantity without an equivalent picked-completion message; sap collector's effects message is not an inventory/state acknowledgement. The server cannot assume the same completion contract for all three. No generic delayed replay or receive-path ownership rewrite was adopted.

### Why ping plus a safety timer and periodic checks do not close the gap

Assume an ideal, fixed 100 ms one-way delivery delay; no loss or jitter is needed for this counterexample. Let the server plan the actual transfer for time 1000 ms, after any chosen preliminary waiting period:

| Time | Event |
|---|---|
| 950 ms | The old client still owns the object locally and starts/finishes an interaction. It sends the relevant update. |
| 1000 ms | Even an immediate server check cannot see that update yet. The server executes its scheduled ownership change. |
| 1050 ms | The action's update reaches the server, after the change. |

Sampling every 200 ms does not change the causal ordering. Waiting another interval or ping moves the deadline, but an unmodified owner can start another action immediately before the new deadline. Renewing the timer on every **observed** action still leaves the most recent unobserved action. The same argument holds even if one grants a hypothetical perfect busy flag; polling cannot observe its future arrival.

An exact cutoff requires the old owner to stop beginning relevant local actions, complete/flush its existing action state, and acknowledge that cutoff before the new authoritative owner runs. The inspected native client has no generic prepare/flush/ack procedure for all harvestable components. A client-assisted protocol can supply it, at the cost of handoff preparation/delivery time; it need not route every already-local interaction through the server. A fixed server-only timer cannot reproduce it.

Decision under the requested latency constraint: **reject the blanket ping/safety delay and do not claim full safety for a harvest forwarding patch alone.** Keep the current runtime unchanged. Server-local busy checks remain inexpensive and valid for actions actually owned by the server, but are not proof of idle remote state. No live latency benchmark or gameplay reproduction is claimed by this reasoning.

### Follow-up: ownership coverage is larger than interaction reach

Treat the user's stated initial-login input restriction as a constraint: do not count the joining player as an interactor while that restriction holds. The normal-play counterexample does not need a login or two simultaneous harvesters.

Player A remains within interaction reach of an object. Player B is outside interaction reach but at the edge of its larger ownership footprint. B entering or leaving coverage changes the object from exclusive to shared ownership or back. A can interact during that change while B never interacts with the object. A large footprint gives takeover lead time on an approach, but it does not establish action completion when an overlap begins/ends. Cell/heading changes and due field timers also need consideration; the current grid is not a pure distance-triggered, fixed-radius region policy.

Current source evidence: `AuthorityGrid.MoveContribution` changes contributions for cell/heading changes. `Evaluate` defers an ending shared field only outside `FieldTier.Inner` when the remaining peer has not refreshed; inner fields can return to the remaining player. `Controller.Apply` then changes the actual object owner. The real configured footprint controls the distance; do not claim a universal 20-metre radius or use the class's 5-metre interaction initializer as the user's actual configured reach.

Thus the approach-distance margin makes settled, single-user local interactions a favorable case, but does not prove all quick interactions are automatically safe. Remaining cases are a request addressed before the change and final local action state still travelling during it. These are protocol-level possibilities, not a claimed live reproduction. Inventories and long-running actions still need their separate handling. No blanket ping timer, fast global object polling, or runtime change was introduced.

## Practical alternatives

### 1. Retain the native user once a sensitive object belongs to a client

For inventories, stands and other private client transactions, do not proactively take an object away from a valid active client. Native open/control requests can still transfer it to a real new user. The fork's normal abandoned-owner/disconnect rules still apply.

This needs no per-object timer or permanent lease dictionary: the current owner and native active-area eligibility already provide the decision. Apply it only to those sensitive groups, not every simple door or ward.

A server-owned object can still be prepared for an approaching player when its actual server-side state is idle and no native grant is pending. **After handing it to an unmodified client, treat its private state as unknown.** Do not reclaim it merely because the server's replicated `inUse` is false.

Tradeoff: an overlapping grid may have to leave a chest with a player. Its normal native opening remains available; the exact shared-server grid behavior is lost for that object. A server-to-client prefetch still needs separate testing of state delivery and requests already in flight; it is not automatically enabled by this policy.

### 2. Keep RPC-based stations on a stable server owner

Smelters, cooking/fire stations, shield generators, turrets, harvestables and similar controls can send their normal native requests to a server owner. If the server remains the owner, no grid takeover can invalidate an insert halfway through delivery. A one-second safety timer is then unnecessary for the ownership race itself.

Prefer the fork's initial/abandoned assignment path. A newly created client-owned station cannot safely be reclaimed just because its prefab is eligible: a client may already have removed material and sent an insert. Retain the active native owner until a verified safe transition, or leave that object native.

This preserves native interaction paths, not zero round-trip latency. It also does not solve native full-queue or simultaneous-insert behavior. Local headless-player dependencies, custom station code, and the fork's optional ownership policies must be checked before enabling each adapter.

### 3. Use exact local completion state when the server really owns the action

There is no need to guess a duration if the authoritative action runs on the server:

- `Incinerator.isInUse` and its real coroutine/`StopAOE` lifecycle, plus the attached container's use/grant state.
- Fermenter `IsInvoking("DelayedTap")` and `m_delayedTapItem`: the output is deferred and stored locally after contents are cleared.
- Offering bowl `IsBossSpawnQueued()` / `IsInvoking("DelayedSpawnBoss")`.

Check these only on an actual attempted handoff; discard a blocked automatic request. Completion does not schedule an old takeover. A later real field request can try again.

These checks are authoritative **only for an action started under server ownership**. Merely becoming the owner does not import the previous client's coroutine or queued output. On a remote owner, the server's matching component does not run that owner's coroutine. Lever-return broadcasts and elapsed timers can reduce risk but cannot prove the remote transaction or inventory transfer finished.

### 4. Conservative interaction-reach protection

An optional middle ground is to forbid automatic takeovers while a sensitive object could be used by any nearby player, even without observing the interaction. This also catches owner-local actions before a server RPC exists.

The cheaper broad gate can use small, omnidirectional protected cell masks built with the existing player-position pass. It must cover the whole cell and the object's interaction geometry; it cannot reuse a directional footprint that deliberately omits rear cells. A possible nearby result may conservatively reject the takeover, or trigger a narrow per-object check only at that actual takeover.

Important limitations:

- Native `Player.m_maxInteractDistance` has a **5-metre class initializer**, not a reliable universal 4-metre radius. Prefabs/mods can override it; hover offsets, eye position and child colliders matter.
- Server positions arrive late. Teleport, disconnect and an action already in flight remain relevant after a player disappears from the small mask. A fixed one-second grace is not a bound on arbitrary packet delay.
- Player presence is not a transaction-completion signal. Text input can remain open for a long time; modded reach may be unknown.

Therefore this is a conservative, testable optimisation candidate, not an exact replacement for client guards. If a strictly native-level guarantee is needed, use native ownership retention instead. Do not introduce this extra mask as the first implementation: it has more bookkeeping and can suppress the desired grid behavior over most of a small footprint.

### 5. Narrow request routing and real native grants

For supported single owner-directed commands, forwarding an already stale request at server arrival is useful. Preserve its sender, payload and message identity, suppress the original route, and execute the native handler exactly once. Do not reroute responses, broadcasts or arbitrary mod RPCs as owner requests.

A short per-object hold renewed by observed requests can reduce subsequent races. It cannot cover delivery after an arbitrary delay, nor owner-local requests invisible to the server. It must not be described as a complete guard or used to justify automatic inventory takeovers.

For native ownership grants (`Container.RPC_RequestOpen`, stand/cart `RPC_RequestOwn`, ship control), give the real user's procedure priority over automatic field changes. Do not synthesize fake users, open requests or successful replies as permission probes. Keep permission checks and dependency policy vetoes intact.

## Object coverage

These rows cover the current FiresGhettoNetworking edition's categories, including static buildings and the unreleased loose-item addition. They propose handling for this server-only backend, not changes to the other edition.

| Group | Relevant action / risk | Best server-only direction |
|---|---|---|
| Doors / gates | `UseDoor`; stale owner-directed requests can disappear. Some doors consume a key before the request. | Keep the simple grid; narrow native request routing. A request hold is risk reduction only; do not replay a possibly executed action. Key-consuming variants merit stable/native ownership until their adapter is tested. |
| Seats / barber | Player attachment and local UI, without an owner RPC in the inspected `Chair.Interact`. | No inventory-style takeover guard solely for sitting. Account separately for composite inventories/vehicles and attached-player constraints. |
| Beds | Local rest/attachment plus native `SetOwner` for the bed's character claim. Bed claimant is not the network owner. | Preserve native claim checks; do not confuse claimant with the peer owning its ZDO. |
| Workstations / extensions | Crafting UI is local in inspected `CraftingStation.Interact`; a crafting action does not itself require changing the station owner. | No universal crafting-duration lock. Inspect added inventory/refill/custom components separately. |
| Wards | Native `ToggleEnabled` / `TogglePermitted`, with real user permissions. | Simple command routing; keep native and dependency permissions. |
| Smelters / kilns / windmills | Client removes a material before `RPC_AddOre` / `RPC_AddFuel`. | Prefer stable server ownership or native retention; an observed-RPC timer cannot guard an unseen local insert. |
| Cooking stations | Local item removal, slot operations, deferred cooking and native output effects. | Stable server ownership; retain the dependency's headless `SpawnItem` fix. |
| Fireplaces / torches | Local fuel removal, owner-directed fuel/state requests. | Stable server/native owner. Optional fork FireControl already claims selected fire sources: do not fight its policy. |
| Fermenters | Client input removal and deferred output with private `m_delayedTapItem`. | Stable server; if ever handing away a server-owned one, wait for actual `DelayedTap` completion. |
| Shield generators | Fuel removed before the owner RPC; native timed attack/effect operations. | Stable server or retain native owner; exact local active-action state before any server-side release. |
| Defenses | Turret ammo removal before RPC. Trap state changes can themselves transfer ownership to the acting player. | Split turret refilling from trap-native control. Respect trap grants instead of immediately reversing them with the grid. |
| Beehives / sap collectors | `RPC_Extract` creates drops and resets stored quantity. Inspected handlers lack their own `IsOwner` rejection. | Stable server is strongest. A stale request must not execute on an old copy and be forwarded again. Do not assume all native handlers reject non-owner execution. |
| Pickables / harvestables | Owner handles pick, creates drops, broadcasts the picked state. | Stable server/native owner, or tested simple-command adapter with final state handling. Scene readiness and source final-state receipt remain separate issues. |
| Feasts | Owner decrements servings, then sends native eat confirmation to the user. | Stable server; the native confirmation must not be replayed or reinterpreted as a handoff acknowledgement. |
| Containers / tombstones | Open/stack grants, local GUI opening, `m_loading`, TakeAll and final inventory writes. | Native user priority and no proactive takeover from an active unmodified client. `inUse=false` alone is insufficient. |
| Item / armour stands | `RPC_RequestOwn`, queued local item, `UpdateAttach` consumes it after becoming owner. | Honor native ownership and retain that owner. A queue timer on the server does not describe the client's queued attachment. |
| Incinerators | Container transactions plus private timed burning coroutine. | Native inventory retention; local `isInUse` is useful only while the server owns the actual burn. No generic safe active-client takeover. |
| Signs | Edit start is local. On submission `ClaimOwnership` and direct ZDO text writes. | Leave text/ownership native. Forcing server ownership back can discard the submitted text. Do not add a fake text RPC. |
| Portals | Edit start is local; submission uses `RPC_SetTag`. Teleporting itself runs on the player. | Stable server tag handler can avoid ownership churn without knowing edit duration. Respect optional PortalLock; arbitrary grid rebalancing during local editing is not fully guarded. |
| Map tables | Reading is local; writing sends native `MapData`. | Stable server writer or simple adapter; reading does not need a long ownership lock. |
| Wisplights | `WispSpawner` is Hoverable, not a native Interactable; spawning is timed owner work. | No interaction guard for the spawner itself. Treat spawned wisps separately. |
| Offerings / altars | Material consumption, native response and a private delayed boss spawn. | Stable server; preserve pending spawn state. Never infer completion from disappearance of offering material. |
| Vehicles | Cart/ship real request/control grants and attached inventories. | Keep the fork/native driver and inventory policies. No generic automatic idle-grid takeover while a remote client's private use state is unknown. |
| Tamed animals | Taming flag is replicated; riding/following/control transitions are separate. | Native/fork control retention. The tamed flag does not prove idle or collision readiness. |
| World markers | Inspected dungeon `Teleport.Interact` and `Vegvisir.Interact` do not ask their object's network owner. | No ownership interaction guard needed for those native actions; preserve separate global-key/location permissions. |
| Other interactables | Interface does not declare transactions, duration, queueing, or owner requirements. | Recognize standard components, then apply the strictest supported adapter on that ZNetView. Unknown/private custom actions stay native. |
| Static buildings | No interaction transaction; stability/physics and PUP readiness are different issues. | Do not add inventory/text guards. This audit does not enable a static or PUP handoff system. |
| Loose items | `RPC_RequestOwn`, client `PickupUpdate`/eating/auto-pickup before final save/destruction. | Native pickup has priority; retain an active client's owner. No speculative grid reclaim while pickup completion is unknown. |

## Why the chest permission probe is not the solution

`RPC_RequestOpen` is a real open/ownership transaction. Its owner checks local `IsInUse` and access, requests a ZDO send, assigns the requester as owner, and sends the response. It is not a read-only permission query.

`RPC_OpenResponse` opens the GUI. `InventoryGui.Show` does not immediately call `SetInUse`; a subsequent `UpdateContainer` does. The grant therefore has a window where the inventory is being opened while both a local and a replicated use flag can still be false. TakeAll adds a different path: the response causes the receiving client to claim ownership and move items. A server cannot turn any false use flag or a reply timeout into proof that all of this finished.

Intercepting a server-addressed response avoids accidentally opening a server/listen-host GUI, but it does not eliminate those missing client states. Such a bridge adds an ownership hop and a transaction state machine. It is a worse first choice than preserving the real user's native grant.

## Minimal implementation order and cost

1. Keep the current small grid and existing supported request forwarding. Split purely local UI actions from owner-sensitive commands; do not add guards to every `Interactable` callback.
2. Add explicitly classified **stable server/native** groups first: refill stations and simple harvest operations. They expand managed coverage without adding repeated automatic ownership swaps. Validate initial client-created objects and policy modules before exposing the modes.
3. Enable native-user retention for containers, stands and loose drops only with an explicit description that shared grid rebalancing is reduced. Never override real native user grants.
4. Add exact local coroutine/Invoke vetoes only where server-to-player handoffs are actually permitted. Gate them on a real requested owner change; no per-frame action scan.
5. Keep proximity masks and any fake-open bridge experimental research. They are not necessary for stable server/native policies.

No new object/physics-tick polling is needed for these first steps. Cached prefab adapters, an owner comparison and a small type-specific veto run on an actual attempted transfer. Already-correct owners exit first. Native user operations run only when the user performs them. Existing field changes and native load/unload events maintain indexes.

Blocked automatic requests should be discarded or wait for fresh field activity; completion must not replay them. If a future adapter stores pending native grants, key them by the exact ZDO and owner revision, remove them on destruction/unload/session end, bound them per object, and never let timeout mean 'safe to steal'. A dictionary key per historical world cell is unnecessary.

Stable server policies remove automatic handoff traffic for those groups, but increase server simulation and retain the player's network round trip. This is a deliberate latency/CPU tradeoff. No live performance or packet-race benchmark has been run; static contract assertions do not establish equivalent safety to a cooperating client.
