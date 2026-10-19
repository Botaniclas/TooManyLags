# FGN performance candidate review

Read-only runtime review, 2026-10-18. References: local FGN 1.5.53 decompilation in `work/KinematicVelocityAudit-2026-10-14/fgn-1.5.53`, Dedicated Simulation source revision recorded in COMPARISON-AND-IDEAS.md, and the current local `work/TooManyLags/src`. This does not establish compatibility with a newer binary or a measured speedup. No runtime code changed.

## 1. Reuse unchanged scene candidate lists: strongest steady-state investigation

FGN ServerAuthorityPatches.CreateDestroyObjects_Prefix collects sector objects, runs both FilterAndDedupeZdos passes, then CreateObjects and RemoveObjects on every permitted pass. Its SssQuietPass only records diagnostics: WasQuiet does not skip any of those operations. Collection has already happened before WasQuiet is called. Multi-peer collection already deduplicates sectors through _cdoVisited; importing another deduplication layer is unnecessary.

Potential saving: cache collection/filter results while the dependency inputs remain unchanged. This could remove repeated work proportional to the number of candidate ZDOs in a settled large base. It does not remove physics, AI, station or networking simulation costs.

Do NOT promote the existing quiet flag to an early return. It compares loaded-zone count rather than zone identities, and does not prove unchanged radius/classic mode, prefab availability, filtering-relevant metadata or external instance changes. Its previous created/removed counts are net count differences, not a complete lifecycle history. CreateObjects can also have pending budget-limited work or previously missing prefabs. A replacement should initially reuse only candidate lists while retaining native creation/removal servicing; invalidate on exact loaded-zone membership, sector membership, simulation-distance/config and relevant prefab/metadata changes, plus world teardown. Bound the cache to the current active set. Hook coverage and cost must be proven before implementation. Measure collect/filter/create/remove separately first using FGN's existing ServerFrameSplit diagnostics.

## 2. Nearest-player object loading: already implemented in FGN's server-budget path

Correction after inspecting the AutoTune namespace as well as ServerAuthorityPatches: FGN 1.5.53 AutoTune.ZoneLoadPatches.ServerCreate already ranks pending near objects with NearestPeerSqr and a bounded KeepNearest heap (100 candidates). It is selected through the CreateObjects prefix when the dedicated-server instantiation budget is positive; the configured default is 8 ms. A search confined to CreateObjectsSorted missed this replacement. The claim that FGN lacks nearest-player loading must not be carried forward.

Dedicated Simulation's single-distance replacement only matters as a comparison with the native path when FGN's server budget is disabled or unavailable. No additional nearest-player patch is recommended. FGN's ValheimCommunityPatchCompat can stand down the simulation/creation paths for a competing scheduler; verify the actual active hooks before measuring.

## 3. Global zone-generation budget: bound spikes, not total work

FGN calls LoadNextZoneAround for each ready peer/test point; each call can successfully poke one zone. Multiple players in distinct new regions can therefore generate/load multiple zones in one update. The fork adds a global successful-spawn budget.

Benefit is lower worst-frame work at the cost of a longer queue, not less generation work. Already loaded zones still need refresh; a budget must not expire their TTL. FGN uses predicted positions and radius compatibility, which must remain intact. Do not copy the fork loop literally: it changes s_nextPeer while deriving subsequent indices from it. For example count=3, initial cursor=0, successful n=0 at index0 sets cursor1; n=1 then selects index2; cursor becomes0; n=2 selects index2 again. Use a fixed pass start and update the persistent cursor after the pass. Adopt only if exploration spikes dominate measured frames; not a large-base idle CPU fix.

## 4. Owner-change events: already present in current TML FGN addon

Plugin.OwnerPatch hooks SetOwnerInternal and OwnerRevision; AuthorityController.OwnerChanged verifies identity/tuple changes and invalidates the affected indexed node. Position and Deserialize events are also hooked. The Server Side edition's event approach is therefore not a missing feature to transplant. Additional parallel hooks would add work and risk conflicting policy writers.

## 5. Refill detection: no polling replacement demonstrated

Current TML RequestRouter already calls NativeNode.Refilled on relevant request paths. Refilled stores a one-second owner/revision-bound timestamp; UseProtected consults it on attempted work. Server Side's accepted-state comparisons distinguish consumption from inserts, but add snapshots and comparisons, including client deserialization observation. They can improve detection semantics in some server-only contracts; they do not demonstrate reduced CPU against the existing event-driven addon. Never replace its client-assisted handoff guarantees with replication-only observation.

## 6. Stale-request routing and loose-item pins: backend-specific, no free port

Server Side's ordered one-object serialization avoids a broad send pass in its own narrow stale-request path. FGN already has StationRouter and the addon has guarded client protocols. A transplant could bypass per-peer policies or remove final-state protection; no measured net saving exists.

Server Side 0.7.1 fixes that edition's permanent loose-item indexing pin. Current FGN addon LooseItemGuard is already consulted only on actual automatic handoffs; it checks native local pickup/eat invocation and local auto-pickup range without polling or a historical requester cache. The server-only pin fix is not evidence of a matching FGN defect or optimisation opportunity.

## Decision

First measure unchanged scene-pass collection/filter cost using existing FGN counters. If material, prototype bounded candidate-list reuse, not a blanket quiet-pass skip. Investigate nearest-player creation separately for startup latency. Consider a fair global zone budget only for observed exploration spikes. Do not replace the send scheduler: FGN already provides due times, per-frame CPU budget, forced/backlog handling and idle backoff. Physics catch-up and worker-count changes are policy tradeoffs, not safe general optimisations.

No benchmark, live-world certification, package, release or performance claim is supplied by this source review.

Follow-up: `FGN-LARGE-BASE-REVIEW-2026-10-18.md` narrows the candidate-list and zone-budget decisions for 10,000+ objects, including current native IL validation and the existing global area-loaded gate.
