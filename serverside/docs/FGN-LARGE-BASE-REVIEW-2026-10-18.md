# FGN: large-base scene lists and zone budget

Source/IL review, 2026-10-18. No game/mod runtime changes, installation, config changes, publishing or live performance measurements.

## Evidence and scope

- FGN reference: locally decompiled 1.5.53, including `FiresGhettoNetworkMod/ServerAuthorityPatches.cs`, `SssQuietPass.cs`, `FgnHooks.cs`, `SectorChangeTracker.cs`, `ServerFrameSplit.cs`, and `FiresGhettoNetworkMod.AutoTune/ZoneLoadPatches.cs`. This is a version-scoped review, not a statement about the newest upstream release or the user's active server configuration.
- Native game: installed `assembly_valheim.dll` SHA256 `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`. Nineteen IL contracts verified in `audits/LargeBaseNativeAudit.ps1`; output and selected native IL are beside it. This audit does not run Unity or validate an optimisation.
- Dedicated Simulation: the reference source revision recorded in COMPARISON-AND-IDEAS.md. Its global budget is a design comparison, not code to copy blindly.

## The relevant work in a settled base

FGN's `Server Object Pass Hz` default is 10. The native scene update and actual frame cadence can further limit realised passes. Its positive `Server Instantiation Budget Ms` default is 8. The following counts assume an illustrative 10 realised passes/second, 10,000 eligible near candidates, 10,000 already-created instances, no distant candidates and no current creation backlog:

| Stage | Existing work per second | Saved by reusing collected/filtered lists? |
|---|---:|---|
| Collect sector references | Approximately 100,000 copied references, plus sector enumeration | Yes, on a cache hit |
| Filter validity/prefab/loaded-zone membership | Approximately 100,000 candidate tests | Yes, on a filtered-list cache hit |
| Creation path examines Created flags | Approximately 100,000 candidates | No; retained initially |
| Removal marks retained near objects | Approximately 100,000 earmark writes | No; retained initially |
| Removal examines existing instances | Approximately 100,000 instances | No; retained initially |

These are work counts, not equal-cost CPU units, timing measurements or a promised percentage. Bulk reference copies can be cheap compared with per-object dictionary/Unity checks. At 20,000 candidates the candidate-dependent counts double. Five overlapping players do not multiply a shared 10,000-object base by five: FGN already visits each native sector once per collection through its visited set. Five separate 10,000-object areas have a much larger combined active set.

This optimisation concerns the dedicated server's scene management. It does not remove rendering, client physics, AI, WearNTear, light/effect updates or ZDO networking work. A server CPU reduction does not establish a direct client-FPS improvement.

## Candidate-list reuse: useful, with specific invalidation

The list contains references to live ZDOs, not frozen gameplay snapshots. Owner, health, fuel and quantity changes normally do not change list membership. Neither do camera rotation, TML's small ownership-cell changes, or ordinary movement within the same native sector when the predicted loading centre stays in the same zone. Created flags and readiness continue to be read by existing creation/removal logic.

Invalidate only the input that changed:

| Change | Relevant action |
|---|---|
| Peer readiness, join/leave, teleport, predicted centre crosses a zone, simulation-distance/classic/radius/test-point change | Recompute the required near/far sector coverage. Preserve near precedence over far and input ordering initially. |
| ZDO added/removed/moved between native sectors | Dirty the affected active sectors/union. Ignore unrelated areas for the base's cache. |
| Zone root loaded/unloaded | Update actual near-zone membership, not merely the number of loaded zones. |
| Prefab, validity, object identity or Distant metadata changes | Invalidate the relevant candidate/filter membership. Network Deserialize writes some metadata directly: hooking SetPrefab alone is insufficient. |
| Prefab registry availability changes | Recheck previously excluded/missing prefabs. Unknown mod registration paths need a supported hook or a fallback to the original path. |
| Portals/chunk import/reset/pool reuse/world shutdown | Respect native portal storage and complete the lifecycle coverage; release cached references on teardown. |
| Owner/fuel/health change only; Created or type-readiness changes | No membership invalidation is inherently required for a candidate-only cache; keep normal creation/removal and type-readiness servicing. |

Do not reuse FGN's broad networking change counter without narrowing it. `SectorChangeTracker` marks ordinary data revisions, owner revisions and Deserialize activity as well as structural changes. Mobs, stations and ownership updates would then repeatedly invalidate a cache that did not need rebuilding.

Do not use the global SssQuietPass flag as a production early return. It is diagnostic, is evaluated after collection has already occurred, uses loaded-zone count, and reacts to any sector addition/removal in the world. An unrelated exploring player can defeat it. Native pending creation and external instance events also remain relevant even after one zero-creation pass.

### Smallest worthwhile design

Start with private, reusable active candidate lists and an active-sector membership signature. Coalesce relevant changes until the next existing scene pass. A stable base can then reuse the lists across many passes while creation/removal still run at the original cadence. Cached references must not survive ZDO reset/removal into an unrelated pooled identity. Detect unsupported input mutators/integration contracts and use FGN's original path instead of assuming an idle cache is safe.

If one busy active sector or a distant player makes the combined list rebuild frequently, measure a second step: filtered lists cached per active native sector. Then only dirty-sector filtering repeats, while aggregate copies may still remain O(N). Avoid complex incremental swap/removal structures initially; they risk changing creation order, near/far precedence and mod compatibility. The profitable choice depends on rebuild frequency, not only object count.

Keep cache state bounded to current active sectors and clear abandoned references on coverage exit/world teardown. Existing FGN scratch lists already retain reusable capacity; a design that reuses these concepts needs no copies of entire ZDOs. Ordinary data updates must not allocate cache entries or enqueue work.

## Global zone budget: different work and a possible loading regression

`PokeLocalZone` first checks the loaded-zone dictionary. If present, it resets TTL and returns false. Otherwise it calls SpawnZone. A successful SpawnZone may load a terrain/zone root for a previously generated base (`Client` mode) or generate a new zone's locations/vegetation (`Full` mode). These roots are distinct from the scene pass that instantiates 10,000 saved building ZDOs.

FGN calls LoadNextZoneAround for each ready peer/test point from an Update postfix, with no local 0.1-second throttle in that function. The original ZoneSystem.Update's early-return cadence does not by itself throttle a postfix. Therefore do not directly import the fork's one-zone-per-0.1-second policy or describe FGN's extra loader as already running only ten times per second. The actual frame rate and other patches remain relevant.

In a fully loaded base, the spawn budget is unused. The work left is loaded-zone probing/TTL refresh, which a successful-spawn budget does not remove. Distinct ready players can repeat much of this probing. A separate possible optimisation is to reuse a deduplicated required-zone set and refresh already-loaded zones once per shared pass; that needs no 10,000-object scan but is a smaller target than candidate filtering. Prediction, the original native loader and ghost generation must remain accounted for.

At login/portal arrival, a global zone budget can limit concurrent root creation and reduce worst-frame contention. It cannot split one expensive SpawnZone into several frames, stop native/background terrain work, or cap saved-building object creation. A count limit bounds successful calls rather than their duration or failed-readiness probe cost.

**Important interaction:** FGN's IsActiveAreaLoaded prefix checks needed near-zone roots across every ready peer. Native near creation and FGN's budgeted ServerCreate both consult that global predicate. Slowing the last peer's root loading can therefore hold up new near-object instantiation elsewhere too, including objects near players already in a populated base. Existing simulated objects are not stopped by this predicate. Do not advertise a tighter zone cap as an unconditional smoother/faster login.

For scale only: a classic radius-3 footprint has 49 zone roots. With one successful root per loader pass, completion requires at least 49 successful passes if all were missing; this is a queue bound, not a prediction of actual loading time. The same footprint still has 49 roots whether it holds 1,000 or 10,000 building objects. Concurrent players with overlapping footprints share many roots; separate areas add work. Lowering loader cadence in addition to adding a cap can greatly lengthen the queue.

If a budget is introduced later, refresh all still-required loaded zones independently of the spawn budget (TTL is natively four seconds), prioritise actual player centres/collision-critical roots, preserve prediction and ghost behavior, and use a fixed-start fair cursor. A low-FPS server with twelve players must not lose zone refresh simply because it ran out of spawn allowance. The fork's changing cursor inside its index loop is not a suitable implementation.

## Recommendation for 10,000+ objects

1. Prioritise candidate-list reuse. It targets recurring base-size-dependent work and need not add ownership, interaction or physics delay.
2. Do not add a restrictive global zone budget merely because a base has many objects. Consider it only if measurements show zone-root loading/generation spikes during join/teleport/exploration.
3. Treat deduplicated loaded-zone probing as a separate, smaller possible optimisation, not as a generation budget.
4. Preserve FGN's existing object-instantiation budget. Correction to the earlier review: AutoTune.ZoneLoadPatches.ServerCreate already selects nearest-peer candidates when that budget is enabled; no duplicate nearest-player patch is needed.

## Measurements needed before a production patch

FGN already records collect/filter/create/remove averages and worst durations in ServerFrameSplit when its debug reporting is enabled. ServerFrameProfile also measures the broader ZoneSystem update, but this includes the native update and postfix, so it is not a pure SpawnZone timer. No suitable measured large-base phase timings were found in the inspected pasted logs.

Compare the same world/settings with one and several players: settled/idle; moving within the base; continuous building and drop/mob activity; one player teleports away while others stay; simultaneous arrival into the base. Record actual scene-pass frequency, candidate counts, phase time, cache rebuild causes/hit rate, and allocations. Distinguish generated terrain roots, reloaded roots and saved ZDO instances. A prototype must differentially match native lists after spawn/removal/sector crossing, portal/chunk changes, equal-count zone swaps, metadata changes and unload/reload. Pending creation must drain at the original rate and protected ownership must remain untouched.

Net upper bound for this first cache stage is the measured collect+filter cost on cache hits, minus invalidation/rebuild overhead. No honest millisecond, whole-server percentage or client-FPS promise can be inferred from an object count and CPU clock alone.
