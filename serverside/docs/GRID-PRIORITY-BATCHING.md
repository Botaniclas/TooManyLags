# Grid priority and field batching — local candidate

The scheduler processes current red coverage, forward coverage, outer coverage, then vacated cells. Shared red cells remain urgent regardless of destination or configured delay. Server Side also treats fast-movement forward cells as immediate/red. Full-grid mode keeps equal active-cell priority. All due lanes run in the same pass; no transfer quota, new delay, starvation rule or timer-scan optimisation is introduced.

Four reusable lists replace the existing pending-job snapshot. Dense object buckets cache priority once during classification; singleton buckets bypass the cache. Nodes reuse their existing bucket identity, updated on movement and cleared on removal. Cache flags clear before any native/mod callback. Changed-cell delay is evaluated once when needed, and insertion no longer repeats the pending-set lookup.

Final target, NotBefore, owner revisions and use/readiness guards remain evaluated by the existing code. This is scheduler priority, not a promise of network delivery order. No networking hook, client handshake, mob/spawner policy, field-retention rule or config default changes. Existing callbacks, per-object blocked retries and native backend policies are retained.

## Verification

- 986 new priority/batching/guard/callback/exception/unload assertions per edition.
- Baseline-versus-candidate comparison: 1,228,800 per-frame object-state observations per edition across full/directional grids and both exit-retention modes. Actual owners, write counts and NotBefore values produce the identical trace C07A11A493C5875A. Includes movement, headings, overlapping players, blocked jobs, invalidation, receipts and remove/re-add.
- Full edition verification is recorded in verification.json. Controlled fixtures and installed native assemblies are not live multiplayer or FPS measurements.

No publication or installed-profile modification is part of this candidate.