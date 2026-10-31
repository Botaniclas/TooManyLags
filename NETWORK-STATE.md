# State-aware ownership traffic — 1.0.63 test

## Chest example

Vanilla/FiresGhettoNetworking continues sending inventory changes. At the actual native send point, TML reads the peer's existing state-revision entry. If it matches the object's current data revision, the state body is omitted; otherwise the original serializer sends the current state together with the latest owner. Initial replication remains complete. Equal prefab names never substitute for equal state.

Ownership-only records retain the native ID/revision/owner/position header with an empty state body. They use the same `ZDOData` RPC, reliable queue, packet batching, compression and receive order as ordinary world updates. No second state channel, per-object ACK, preflight exchange or batch-collection delay is added. Header overhead is retained for native compatibility.

## Receive cost and recovery

One guard is inserted directly after the native object's existing dictionary lookup. For an empty body it checks the existing object, adequate data revision and any known decode failure before native revision/owner mutation. The normal path allocates no certificate, persistent object entry, repair message or timer. Existing native owner callbacks still execute.

A Deserialize exception marks only that object in a weak table; successful decoding or world reset removes its invalid status. A pooled object with a different ID does not inherit the failure. A missing or failed baseline requests full repair, up to 128 IDs per exceptional RPC; overflow immediately starts another bounded chunk. The sender removes only the requested native sent-ledger entries and uses native ForceSend, including FiresGhettoNetworking's existing snapshot invalidation. A bare ForceSend would not override an optimistic current entry.

An equal-revision full retry for a previously failed decode is explicitly decoded before native handling: vanilla otherwise skips it because its revision was already advanced before the failure. A failed compact send invalidates its optimistic peer ledger, requeues affected native records and disables compact output on that connection. It preserves receive/repair capability. Unsupported peers retain native payloads and failure behavior. The receive guard stays installed when sending stops, covering already-queued records.

A stale or still-unreadable full repair is discarded without another automatic request. Recovery cannot become a self-sustaining request/reply loop; subsequent real traffic/activity can retry.

## Other savings and limits

- Stable reference filtering runs only on active native send lists with forced additions. It keeps their first priority slot; the scratch set clears every pass and contains no world history. No ZDO-ID hash lookup is used for this reference filter.
- Small compact `ZDOData` messages can use FiresGhettoNetworking's existing compressor from 128 bytes instead of 512. Its actual saving checks and frame decoder stay unchanged. The threshold remains 512 for unrelated messages and full-only packets. There is no second compression pass or new codec.
- Existing control batching and all inventory, refill, text, pickup, vehicle, collision, physics, source-commit and readiness safeguards remain in place. Their local checks do not justify replacing them with a new reason-dependent protocol. Native/FiresGhettoNetworking handoff results retain their callbacks, tokens, cooldowns and state-floor information; they are not silently discarded.
- No suppression of general ForceSend or delta-cache invalidation. Explicit resynchronisation must still work. Player-only simulation mode is unchanged.

Support is negotiated in the existing greeting. Optional native/compression contracts are checked before installing hooks; a mismatch retains full state without disabling the grid. Update server and clients to use the optimisation. Native managed execution and headless Valheim Mono Harmony installation are tested; combined live third-party patches, multiplayer behavior, real plant/chest sizes and CPU/wire savings still require in-game testing.
