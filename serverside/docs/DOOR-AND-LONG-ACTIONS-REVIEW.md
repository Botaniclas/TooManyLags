# Door and long-action review

2026-10-16 audit of the local 0.3.1 candidate, with the 0.4.0 implementation follow-up below. The original audit was read-only; it did not include a runtime patch.

Inspected installed game SHA256: `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`. Dependency: released Dedicated Simulation 1.18.0. The native source snapshots match that installed game. `work/ServerSideHandoffAudit-2026-10-16/DoorGridAudit.cs` passed 10 native-IL and controlled-controller assertions. This is not a live Unity animation or network reproduction.

## Doors and gates

Native `Door.CanInteract` checks the local Animator for the `open` or `closed` tag. Both `Interact` and `RPC_UseDoor` can reject input while the local animation is between those states. `UpdateState` normally runs every 0.2 seconds and reads the replicated `s_state`; the RPC also invokes it after a successful write.

The door handler does not claim/set network ownership or install an inventory/user lock. An animation by itself therefore does not explain a permanent ownership lock. Our generic `Catalog.Idle` checks `s_user`, `s_inUse` and attachment state, not door animation. Ordered object-state delivery before a stale forwarded command does not force a recipient's Animator to finish or to have consumed the state already. A click rejected by the native animation test is not queued for later replay by this addon.

A permanent headless Animator failure, or the exact earlier TooManyLags symptom where only one user could operate a door, was not reproduced. Native animation evaluation/culling and live packet order still need a world test; do not infer either from the controller fixtures.

## Confirmed ownership-cache gap

The controlled fixture executes the production controller:

1. A loaded door is correctly assigned to nearby player A.
2. An external owner write changes its actual owner to connected player B.
3. The source-data notification is called outside any pending grace job.
4. Stationary checks and real heading changes leave the object's actual owner B while its grid cell still selects A.
5. Leaving and re-entering the footprint can recover assignment.

`SourceData` only cancels current grace jobs. There is no general invalidation when an already-settled object's native owner changes. `AuthorityWorld` schedules its objects after a cell-owner change, registration or a fresh wait-set renewal. A heading change with the same cell recipient does not reschedule a settled node with no wait entry. This preserves the intended low steady-state cost but misses owner drift.

Native `RPC_ZDOData` can import ownership together with a newer data payload; the earlier `QuickInteractionAudit` inspected that receive rule. That is a possible external writer, not proof that it caused the user's old incident. A second mod or native ownership request can also change ownership. The fixture injects the writer explicitly and does not simulate real packet arrival.

The smallest candidate is to invalidate only affected managed nodes on an actual native ownership change, reusing the existing scheduling/use/grace checks. A periodic world or all-door scan is unnecessary. No such repair was installed in the audited 0.3.1 build. This gap affects other managed categories too. It is separate from the claim that door animation itself permanently locks ownership or that all other users necessarily lose access.

### Implemented in 0.4.0

`ZDO.SetOwnerInternal` now queues only an affected loaded managed node after an actual owner change. The next controller pass reuses normal grid/use/grace checks; own addon writes do not recursively invalidate. The dynamic-object regression fixture verifies repair for a settled door/static building without player movement, preserves native zero-owner release, and resumes after native reactivation. No periodic scan or dedicated animation patch is introduced. The original ten-assertion audit remains evidence of the earlier gap, not a live reproduction or a test of this fix.

## Long actions still outside dynamic ownership

| Action | Why an automatic takeover needs a separate contract | Current handling |
|---|---|---|
| Chest/container/tombstone inventory | Native opening/grant, inventory response and local GUI use form a transaction; replicated in-use state alone does not prove it has finished. | Whole identity excluded; native/dependency ownership. |
| Sign and portal text entry | Opening the local editor has no server-visible start; sign saving claims ownership, portal saving submits `RPC_SetTag`. | Whole identity excluded; no new addon takeover while typing. |
| Incinerator burn | Private `isInUse` plus a coroutine and an inventory; the delayed continuation checks ownership. | Excluded. |
| Fermenter tapping | The native owner keeps delayed output in private fields before `DelayedTap`; the content is already cleared in the ZDO. | Excluded, including its refill side. |
| Offering/boss ritual | A local delayed spawn and private spawn point; inventory removal and confirmation use separate messages. | Excluded. |
| Item/armour stand attachment or delayed powers | Ownership grants, queued attachment/output or delayed activation extend beyond the initial click. | Excluded. |
| Vehicle/mount control | Driver/rider/follow state and moving physics require their own policies. | Vehicles stay native. In 0.4.0 only idle tamed animals enter the grid; riding requests withdraw them, native grants retain priority, and an observed dismount can reactivate them. |

Repeated refilling remains a series of immediate inserts, not a single server-observable long transaction. Supported inserts use the current cancellable source grace and stale-request routing. Those mechanisms are bounded mitigations, not a generic acknowledged final-state protocol.

Crafting menus, seats and barber use run on the player and do not require a grid handoff guard solely because the UI/pose lasts longer. Bed network ownership is also distinct from its saved player claimant; sleeping attaches the player locally. Ordinary serialized smelter/cooking production is different from a private deferred tap/burn/spawn callback.
