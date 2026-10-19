# Scene-list cache: TML, FiresGhettoNetworking and PUP

Research review, 2026-10-18. No production/runtime code, installed profile, gameplay config or release package changed. The cache below is an isolated differential model, not a working Unity plugin.

## Decision

The opportunity is real at the algorithmic level, but there is no cross-pass cache to port from the inspected Dedicated Simulation fork. Its Core.CreateDestroyObjects_Patch reuses vanilla list buffers, clears them every pass, recollects each ready peer's objects and removes duplicates. FGN already reuses buffers and deduplicates sectors. Repeating that buffer optimisation would not add a useful feature.

For TML's FGN edition, the preferred first production experiment is narrower than the complete list-cache model: **retain FGN's collection and reuse only unchanged filtered results**. This preserves FGN's live prediction updates, source bookkeeping, sector queries, diagnostics and list ordering. It avoids rewriting the whole scene prefix or moving FGN's initial Clear calls around. Bulk collection copies remain, but repeated per-object filtering can be eliminated on hits. Expand into raw collection caching only if measured collection cost justifies the additional integration surface.

The proposed module is server-only, separate from the ownership grid and optional PUP guard module. It requires FGN's active dedicated-server simulation path; it must stand down when that path is absent, disabled or refused by a competing scene scheduler. PUP is not a required dependency of the cache itself. Neither built-in nor external BotanicPupPatch ownership of collision guards changes.

## Verified inputs

- Installed Valheim native DLL SHA256 `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`.
- Actual cached FGN release DLLs: 1.5.53 and 1.5.65. The 1.5.65 DLL hash is `E43F8E7B7614D0C6B7C8437CD235D837851955A41ACE1238858EC5FCEF75B2CB`. Its inspected ServerAuthorityPatches still collects/filters each served pass and SssQuietPass remains diagnostic. The AutoTune module still reads live Created/type readiness and nearest-player distance.
- Actual cached PUP FPS 1.0.36 DLL, plus matching reference decompilation. No assertion about newer PUP releases or the versions running on the user's server is implied.
- Current local TML FGN Addon source identifies itself as 1.0.47. Its ownership layer excludes baked originals independently; its optional PUP module maintains live collision/support guards and defers to an external PupPatch.
- Dedicated Simulation source revision is recorded in COMPARISON-AND-IDEAS.md. This review does not treat source comments as proof of a released binary's runtime behavior.

## Findings that rule out naive caches

| Shortcut | Failure or unnecessary work | Required handling |
|---|---|---|
| Cache only objects with Created=false | PUP deliberately sets Created=true for a suppressed original even without a live original instance, then releases it to false later. A cached pending-only list misses restoration. | Cache candidate membership, read Created on every normal creation pass. |
| Omit baked originals because TML ignores their ownership | Ownership eligibility is not native scene eligibility. Save/restore and PUP's own transitions still need their normal scene treatment. | Preserve FGN's original filter; do not add ownership/category/baked predicates. |
| Treat cached scene membership as collision readiness | PUP builds colliders asynchronously; Created and loaded-zone membership do not establish proxy completeness. | Never cache, invent or bypass physics/support/recipient readiness in this module. |
| Invalidate from ZDO.GetPosition during AddToSector | Native InternalSetPosition calls SetSector before writing m_position. The add callback can still see the old position. | Use the actual SectorIndex callback argument for structural invalidation. |
| Watch only coordinate changes | PUP can call its bound SetRestoreSector to repair indexing without changing the coordinate. | Watch actual sector membership operations too. |
| Use PUP cell coordinates as native-sector keys | PUP uses floor(x/64); native zones use floor((x+32)/64). At x=40 their keys already differ. | Keep native sector keys; translate bounds only if a separate cluster event is ever used. |
| Use normal HashSet<ZDO>/Dictionary<ZDO,...> for persistent identity | Native ZDO.GetHashCode uses m_uid; Reset/pooling changes the key hash. Separate wrappers can also compare equal by UID. | Use stable reference identity for cached wrappers, retain/validate identity snapshots where needed, clear before pool reuse. |
| Clear dirty state after CreateObjects/RemoveObjects | Awake, spawn, destruction or PUP callbacks can invalidate inputs while consumers run. Clearing afterward loses the next rebuild. | Commit a generation captured before building; later events remain dirty. |
| Track only loaded-zone/prefab counts | A remove+add can keep the count unchanged but change an eligible object. | Compare the relevant membership answers, not collection counts alone. |
| Invalidate on every owner/data revision | Normal AI, fuel, health, camera reports and grid transfers can constantly invalidate an unchanged membership list. | Invalidate only actual filter or membership changes. |
| Reuse a list that another patch edited | A same-count replacement can change contents. | Preserve private list ownership; check mutation versions/contracts and use the original path after a detected edit. |

## Proposed cache boundary

### Preserve the existing path

Do not skip CreateDestroyObjects wholesale. Keep FGN's original collection, time/instance budgets, creation, removal, orphan handling and every native/Harmony callback. Native removal marks candidates with a frame-derived byte; those marks are not persistent cache state. A previous pass that created nothing is not proof that future creation is unnecessary: zones/types/prefabs can become ready without changing Created immediately.

The cache services the existing FGN scene pass, not the TML movement timer. A new object, imported prefab or PUP restore must be visible even when all players stand still. No new gameplay wait period, heartbeat RPC or physical-tick object scan is required.

### Cheap validation instead of checking every object

1. Keep the exact ordered FGN source/coverage identity, near/far mode and actual simulation distance. Preserve near precedence, duplicated-source bookkeeping, radius/classic behavior and test points. Continue prediction sampling and diagnostics normally.
2. Check loaded membership for the distinct position-zone keys used by near filtering. This is normally a small set. Identical final membership after unload/reload is sufficient for candidate filtering; readiness remains live elsewhere.
3. Check HasPrefab for each distinct relevant prefab hash from the raw candidates, including currently missing prefabs. The installed native HasPrefab only tests dictionary key membership. This observes late registration and direct dictionary edits without extra registration hooks. A replacement under the same hash leaves membership unchanged; creation still obtains the current prefab normally.
4. Relevant native structural events dirty only the active sector set. Coalesce them until the next original pass. Do not rebuild from each event or invalidate a base because another player spawned something in a separate area.
5. Capture actual eligibility changes on metadata imports/setters and reset/initialisation. Deserialize writes m_prefab directly, so a SetPrefab-only hook is insufficient. Distant changes can introduce a previously absent far candidate. Portal storage and position-only portal changes need their own accounting; AddToSector alone is not a complete hook inventory.
6. Reuse buffers and once-bound field delegates in production. No reflection, LINQ, allocation of per-import state objects or full-world snapshot in the cache-hit path. The model uses reflection to check list versions for test simplicity and is not a production performance implementation.

FGN's native collection callbacks can supply active-sector membership instead of duplicating its geometry algorithm. This needs a narrowly scoped collector context so networking scans cannot accidentally replace scene membership. No raw-collector short circuit should be installed before proving its prediction, list-clear and bookkeeping contracts.

Metadata checks can occur on existing native import/position notifications, but the extra reads/lookups are not free. Ordinary owner changes alone must neither invalidate this cache nor call a collision guard. Optimisation value must include these notification costs, not only cache-hit counts.

## TML and PUP interaction matrix

| Situation | Candidate-list work | Existing behavior that remains live |
|---|---|---|
| Player moves inside a native zone or rotates camera | Usually reuse filtered results | TML small-cell ownership, heading updates and handoff protocol |
| Door/chest used; owner switches | Usually reuse results | Inventory/user state, source/destination handoff protection and ownership revisions |
| Item/mob spawns or crosses a native sector | Relevant invalidation; one next-pass rebuild | Native pickup/AI/physics and FGN owner policy |
| Original suppressed by a guest-side PUP cluster | No need to freeze Created in a cached snapshot | PUP's live Created flag and normal creation suppression |
| Dedicated bake parks objects outside the world | Remove the affected native candidates after the next normal collection | PUP retains its cluster/proxy state and original ZDOs |
| Save restores coordinates then reparks | Coalesce genuine sector changes; do not force an instance or clear baked metadata | Original save/restore ordering and PUP guards |
| Restore clears Created without moving membership | Reuse candidates and service creation normally | Readiness and PUP's own CreateObject/AddInstance hooks |
| Actor changes owner near a cluster | Candidate cache is not a readiness authority | FGN takeover plus PUP's local collision gate |
| Different player teleports/disconnects | Re-evaluate actual FGN coverage | TML field retirement and native ownership/release rules |
| Last peer leaves / world unloads / simulation turns off | Drop candidate references, subscriptions and the cached context | No retained world history or waiting for another player movement |

With dedicated PUP parking, many static ZDOs can already be outside the server's active sectors. The cache benefit then depends on remaining active candidates, not the base's visible building count. With client-side suppression, list sizes can differ again. Measure FGN near/distant counts instead of assuming every displayed instance is still processed here.

## Validation completed

`audits/RunSceneCacheAudit.ps1` reproduces the checks and writes `scene-cache-verification.json` with input hashes and outputs:

- **4,095 differential/model assertions** passed on .NET Framework and the game's embedded Mono. Eight deterministic histories start with 10,000 objects and apply 250 mixed changes each. Both full candidate-list reuse and the narrower filter-only seam are covered.
- Directed cases cover native add-before-position order, PUP sector-zero parking, 10,000-object park/restore batches, prefab availability/metadata completion, equal-count zone swaps, portal storage, far precedence, reset/pool reuse, external same-count list edits, dirty-during-build events, empty coverage, world replacement and pending readiness.
- **33 native/release/reference contract assertions** cover the installed game, actual FGN 1.5.53/1.5.65 and PUP 1.0.36 binaries plus the relevant fork/TML source contracts.
- Existing TML optional-PUP guard baseline: **24 lifecycle/module assertions and 63 guard assertions**, on .NET and Mono. The latter includes 100,000 steady-owner calls without readiness requests. This is baseline verification; the cache is not integrated into those guards.
- In the illustrative model with 10,000 candidates, four relevant zones and 100 prefab hashes, 1,000 stable full-cache hits perform 4,000 zone and 100,000 prefab-membership checks instead of 10,000,000 repeated object filter visits. The full model also avoids reference recollection. The recommended filter-only seam retains collection copies and avoids the repeated filter visits only. These are operation counts, not measured runtime savings.

## Remaining proof before shipping

The model assumes the structural/metadata notifications are actually delivered. It is not a test of Harmony ordering, Unity object destruction, terrain/collider jobs, packet scheduling or a production cache implementation. Unknown mods can directly edit backing storage and bypass ordinary methods; a cheap universal detector for arbitrary writes is not established. List mutation checks alone cannot detect every native-store edit.

A temporary verification mode should compute original and cached candidates side-by-side, compare **ordered reference/identity sequences**, and use the original result on a mismatch while disabling cache reuse. Such a mode intentionally pays original cost during validation; it is not the production performance mode. Installed competing scene schedulers must retain their existing FGN/TML safety block, rather than being overridden.

Required live cases: multiple clients join/leave/teleport; bake/restore/save/repark; late mod-prefab registration; large structure destruction; moving drops/tames; an occupied inventory and concurrent ownership handoff; unloaded/reloaded areas; PUP present with built-in guards versus external PupPatch; PUP absent. Verify candidate equivalence, readiness timing, absence of unintended object destruction, cache bounds and native-creation backlog progress.

Measure collect/filter/create/remove cost, rebuild causes, active-candidate count and allocations on the same large world before and after. The filter-only approach's maximum benefit is the measured filter cost on hits minus its validation/notification work. No frame-rate percentage, universal compatibility guarantee or zero-risk release claim follows from this research alone.
