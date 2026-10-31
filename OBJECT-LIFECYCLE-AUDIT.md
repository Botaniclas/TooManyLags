# Object lifecycle and send timing: 1.0.64 test build

## Classification and handoffs

| Type | Actual policy and limits |
|---|---|
| Interactable roots | Native interactables are grouped by their components, with compatibility fallbacks. The interactive default and grid apply; inventories, refills, text editing and active use keep their existing guards. |
| Fixed sources of physical successors | Standing trees/destructibles with explicit native successors leading to an eligible physical body use the PhysicsObjects category, but follow interactive default/grid rules. The current fixed source is not a body and has no sleep prerequisite. Loot tables and cosmetic effects alone do not qualify it. |
| Static roots | Eligible fixed building and world roots, including rocks/ore and sources that only drop ordinary loot, use the static default/optional static grid. Actual interactables, physical successors, bodies, creatures, projectiles and spawners do not fall through to static handling. |
| Physical props | Only simple persistent roots with one Rigidbody, native position/rotation/body-velocity sync, no joints and no independent child network root qualify. Characters, ordinary item drops, projectiles and vehicles are excluded. Normal grid transfers require sleep at the actual owner. A genuine vacant-field return to the server bypasses sleep; an overlap-driven server acquisition does not. |
| Loose items and eggs | Separate category; interactive default/grid and native pickup priority. No sleep gate is added. Native item/transform physics continues; this fix does not freeze or zero velocities. Native ownership gain restores saved linear/angular velocity when the prefab enables m_syncBodyVelocity. TML does not currently enforce that flag or add a final motion snapshot for every ItemDrop, so exact momentum continuity for every item/prefab is not established by this source audit. |
| Idle tamed animals | Grid-managed through saved s_tamed state, with the interactive default and optional additional mob default. Riding, attachment, following, friendly summons and other use/exclusion checks remain. Movement reindexes them only when crossing a fine-cell boundary, after the existing cheap position-bound check. |
| Hostile Mobs | Separate target-based ownership system, combat handoff guards, ping fallback and optional server default. Not moved into the ordinary object grid by this fix. |

An existing unowned eligible object's first native assignment selects its configured server/grid owner instead of first assigning a player and then migrating it. It does not require a sleeping body: no existing body simulation is being migrated. Existing active owners remain protected. Freshly created physical objects keep native creator ownership so spawning and the initial impulse remain together; they are not silently treated as existing-world initial loads.

## Pending work, standby and unloading

- Unowned distant fixed sources outside every active player area now use the same dormancy rule as distant static scenery. They remain indexed for later native ownership events or covering player fields. They are not reclassified as static objects.
- Unsettled initial field-timer work uses the current field deadline. A red acceleration or a committed/replaced field supersedes that deadline, without another full timer. No global future-work gate, retry backoff or per-pass work limit is introduced.
- Loaded objects blocked only by broad server-area readiness are parked in a per-zone set. Each unresolved zone is checked once per controller pass; the first ready pass resumes its members. Native owner changes, movement, new field work and refused requests retire/replace stale wait entries. Missing-instance and destination-proof waits keep their distinct paths.
- Parking preserves an established owner's native release protection. It does not mean a completed handoff or permission to bypass use/collision/loading safeguards.
- Removing a node also removes its parked membership. Empty wait buckets disappear; world reset clears all buckets. No persistent history of unloaded world positions is added.
- Settled indexed objects have no pending-object scans. This is not zero total overhead: player movement/heading sampling, native event hooks, real motion, active handoffs, unresolved zones and the existing cheap transform ownership-gain hook still need work. A moving-body veto is not periodically polled to discover that the body stopped; fresh relevant coverage may retry it. Future field-timer jobs still undergo the existing cheap queue/deadline checks, but skip their full Apply policy; this build deliberately does not restore the previously reverted global future-work gate.

The tests count Apply invocations and shared native readiness queries. They do not show that every captured Pending outcome has disappeared, or establish live CPU/latency measurements.

## Existing world-update timing

The actual flow is already:

1. Native ZDOMan.Update calls ReleaseZDOS.
2. TML's ReleaseZDOS prefix checks its cadence and processes changed/due ownership work.
3. Native Update calls SendZDOToPeers2; the FiresGhettoNetworking 1.5.72 scheduler controls per-peer sends when active.
4. Native Update processes destruction and statistics.

With the FiresGhettoNetworking scheduler active, its per-peer due/queued-work and send-budget decisions remain authoritative. Forced ownership work is visible to that scheduler, but connection room, its budget or a foreign scheduler may still defer sending. TML does not create a second independent world-update loop or delay urgent red work until a later send slot. Source-owner handoffs arriving asynchronously can of course complete in a later native pass.

At serialization time, StateTransfer reads the recipient's existing native sent DataRevision before the native sender advances its ledger. Matching compatible peers receive the native ownership envelope with no repeated state body; changed, initial, missing or unsupported state keeps the original native serializer. Forced/ordinary duplicate entries are removed. An owner change still needs an ownership record, even when its full state body is omitted. Control/usage/readiness handoff messages are separate and remain when required. No blanket claim of ACK-free handoffs or receiver delivery acknowledgement is made: the native sent ledger relies on the ordered transport, with bounded exceptional repair if state is missing or failed.

There is no benefit here from moving the entire grid after world sending: that can miss the current native send opportunity. Checking state freshness belongs to serialization, where the actual recipient and latest revision are known, as already implemented. This build does not change that network protocol.

## Evidence and limits

Reviewed production files: AuthorityController.cs, AuthorityGrid.cs, ObjectCatalog.cs, PhysicsObjects.cs, StaticBuildings.cs, LooseItemGuard.cs, OwnershipCadence.cs, GridHandoffProtocol.cs and NetworkSavings/StateTransfer.cs. The actual installed native ZDOMan/ZSyncTransform and FiresGhettoNetworking 1.5.72 SendScheduler/CreatureOwnership were inspected; temporary decompilations are in bin/type-audit.

All 30 existing verification groups pass, including new Pending-work cases, physics source/body distinctions, native send/receive state audits, initial assignment, guards and lifecycle cleanup. See verification.json and VALIDATION.md. Managed fixtures and native IL/headless checks do not replace live Unity/PhysX and multiplayer tests; no such live test was performed.
