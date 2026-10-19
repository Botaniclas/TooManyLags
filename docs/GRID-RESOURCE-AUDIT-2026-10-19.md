# Grid resource audit — 1.0.49

Reviewed source: `fb71a241cd08ea8ede6a270110b68385dd83c49b` (2026-10-19).
Scope: the FiresGhettoNetworking Addon's grid, loaded-object index, initial-load tracking, readiness state, request routing, camera publication and embedded player-simulation mode. Separate Standalone and Server Side projects were not changed.

**This was an audit. Production source, configs, DLLs and published packages are unchanged.** The pooling experiment below exists only under ignored `bin/grid-resource-audit/`.

## Memory lifetime

No accumulating history of visited grid cells was found in the reviewed paths or managed lifecycle tests:

- Vacated field policies are retired immediately with exit retention, or after their existing grace period with ownership return enabled. The latter is temporary state, not an unlimited world-history cache.
- Completed ownership certificates are limited to loaded object buckets. Removing the last object forgets its certificate and bucket.
- Unregister, data destruction, scene destruction and world reset remove indexed nodes, field-wait records and readiness entries. Initial objects without a server instance are also pruned when loaded-area coverage changes. Disconnect removes transport-specific readiness state.
- The policy reuse pool is capped at 1,024 entries. Its retained objects contain selection state, not Unity objects or ZDO references. Collection capacities may retain their previous high-water mark after entries are removed; that alone is not a leak.
- Camera publication retains only the current player/world, resets on session loss and sends only changed coverage. It does not keep per-rotation history. Sampling and the server's player/cadence checks still have a small ongoing cost.

An additional harness compiled the unchanged production `AuthorityGrid.cs` with 12 players, all 56 coverage masks and 10,000 travel steps per exit mode. Only current objects were kept loaded. Results:

| Keep ownership on exit | Peak field policies | Peak loaded certificates | Managed heap after step 5,000 / 10,000 | Final entries after unload/departure |
|---|---:|---:|---:|---:|
| true | 420 | 12 | 372,512 / 372,512 bytes | 0 |
| false | 444 | 0 | 290,128 / 290,128 bytes | 0 |

In separate weak-reference checks, all 2,000 removed nodes per mode were collected while the grid itself remained alive, including nodes that had been blocked and pending. These are managed fixture heap samples, not the grid's exclusive memory size, Unity native memory, process working set or a live server RAM diagnosis.

## Conditional request-queue growth

The main server-simulation `RequestRouter` has an open growth case. `Hold` queues requests without a per-object or global limit. Inventory requests and early uninstantiated picks have a five-second expiry; other action types do not have a generic expiry. If recipient readiness never completes while the object stays loaded, further actions can accumulate.

An isolated harness used the real compiled controller/router fixture, held a valid door recipient unready, and submitted 5,000 `UseDoor` requests. After a simulated hour all 5,000 requests remained queued. Unregistering the object removed every request and its queue.

This is **conditional queue growth**, not evidence that visiting more regions leaks field caches. Without additional requests the queue does not grow. It nevertheless prevents claiming a universal bound for all memory associated with grid handoffs. Arbitrarily expiring, dropping or merging every action is unsafe: some queued actions carry distinct state or follow local material consumption. Any limit needs action-aware completion or rejection rules rather than silently dropping gameplay requests.

## Avoidable allocations: ownership return mode

`AuthorityGrid.Commit` returns an empty policy to its existing pool only when `discardVacant` is true, which corresponds to `KeepOwnershipOnCellExit=true`. With return enabled, retired policies are removed correctly but are repeatedly allocated again as players travel.

For the identical 10,000-step return-enabled travel workload:

- Current production: **2,361,704 policy allocations**; no accumulated live-cell history.
- Isolated one-condition pooling candidate: **888 policy allocations**, with the same peak active-policy count and final cleanup.
- The existing 1,024-entry cap remains. The candidate's retained managed fixture heap was 391,208 bytes versus 290,128 bytes for the production return-enabled case: it trades a small bounded pool for fewer short-lived allocations.

The candidate also passed the existing 124,064 grid/index assertions, 76,845 field-cache assertions and 60,009 additional resource/lifecycle checks. It changes neither timers nor ownership decisions. It has not been copied into production or measured on a live Valheim server. The embedded player-simulation grid contains the same pool restriction and would need its own corresponding change and regression coverage if this optimisation is adopted.

## Pending work: repeated traversal

Stable loaded objects already have no recurring object jobs: the existing 10,000-object / 1,000-cycle regression passed again.

There are two different remaining costs:

1. **Jobs not yet due.** `AuthorityWorld.Tick` copies and traverses the pending set before skipping nodes whose `NotBefore` is still in the future. In a 1,000-node outer-field fixture there were 59,000 pending-entry visits across 59 polls, with zero `Apply` calls before the deadline. A wakeable earliest-deadline gate could skip this traversal. It must also handle accelerated fields, clock rollback, received handoff data and `LoadReady` shortening `NotBefore`; a timer-only early return would be incomplete.
2. **Actually blocked jobs.** Some busy, uncontrollable or unresolved transfers return `Pending` and are retried each grid pass. A deliberately blocked 1,000-node fixture produced 100,000 `Apply` calls in 100 passes. Real node guards differ in cost; this is an operation count, not server CPU time. Some refusals already use `waitingFieldRequests` and are retried only on a genuine field request. Moving every other refusal to that mechanism would alter behavior: inventory closing, rider/follow state, readiness and in-flight native transactions can progress without a player cell or heading change.

The least invasive next change is extending the existing capped policy pool to return-enabled mode. A future-only pending-work gate is a separate, more involved change. Blanket throttling of busy nodes, removing readiness checks, expiring arbitrary interactions or rebuilding caches periodically is not justified by these results.

## Checks run

- `test-field-cache.ps1`: 76,845 assertions.
- `test-regions.ps1`: 475,943 assertions.
- `test-grid-load.ps1`: grid/index, complete controller/router and actual grid-protocol fixtures; initial-load, harvest, unload, disconnect, inventory and readiness checks passed.
- `test-player-simulation.ps1`: all seven suites passed, including 1,274,775 directional-grid assertions and handoff/network lifecycle checks.
- Additional resource, weak-reference, blocked-work, pooling-candidate and request-queue experiments under `bin/grid-resource-audit/`.

No live multiplayer, packet-loss soak test, Unity native-memory profile or full-process RAM attribution was performed. No CPU percentage or in-game speedup is claimed. The released DLL remains SHA256 `872F0BF2D3295D44378B8951D79CB986F3D2A604A794751556C8EBFA7F49D04D`.
