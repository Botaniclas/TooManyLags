# Ownership control batching — TooManyLags 1.0.61 test

This section documents the inherited 1.0.61 change. The 1.0.63 test additionally implements [state-aware native payloads and stable duplicate filtering](NETWORK-STATE.md); statements below about unchanged native state traffic describe 1.0.61.

Combined with the PhysicsObjects extension on the 1.0.60 baseline, including its loose-item/egg initial defaults. Includes the reviewed 2026-10-22 failure-isolation correction. The main project, earlier candidates and installed mods remain unchanged. Local test version 1.0.61; not published.

## What changes

Large grid passes combine small handoff requests and replies per recipient. Chunks contain at most 32 records and 4 KiB; full chunks leave immediately and remaining records leave at each priority-lane boundary and before the operation ends. A red tail therefore cannot wait behind a large yellow/exit block. A single record keeps its original message format. There is no extra interval, quota or cross-frame collection window.

The same grouping applies to replies to an incoming batch and to the existing same-frame deferred load-proof pass. Red/forward/outer/exit scheduling is unchanged, and each recipient sees the original command order. There is no grouping by prefab identity: two plants can have different growth, harvest and ownership state even when they look identical.

Every record retains the original object ID, token, owner/revision and state checks. Inventory, refill, text, collision and physics sleeping guards still execute individually. One blocked object does not reject the other records. Oversized, truncated, unnegotiated or nested bundles are rejected; replayed commit tokens remain fenced.

The existing grid already combines overlapping player footprints and holds one pending job per indexed object. This change does not introduce an additional delay to suppress real policy changes and does not discard a sent transaction.

## Failure isolation

The 2026-10-22 correction contains each queued flush within its own connection. If building an envelope fails before any transport call, the original records use the existing individual message format in their original order. A transport exception does not trigger a resend: the packet may already have left the process, so existing token/status reconciliation remains responsible. An individual fallback send failure cannot prevent later records or other recipients from being attempted. Buffers and queue flags retire at the end of the operation; later operations can immediately queue the connection again.

The successful batching path has no additional record copy, timer, idle scan, RPC or persistent cache. The fallback array is allocated only for an envelope construction failure or a queued connection's capability downgrade. Diagnostic formatting remains behind the existing logging setting. Grid decisions, wire IDs, record bounds and individual handoff safeguards are unchanged.

## Expected benefit and limits

The controller/protocol fixture completes 100 source handoffs with a simulated native commit, using four TML request and four TML result RPC envelopes rather than 100 of each. It also covers 100 initial-load requests and 100 deferred replies using four envelopes in each direction. Native FiresGhettoNetworking acknowledgements and object-state replication are not included in that count and are not removed. This reduces repeated TML framing and dispatch work. It is not a measured server-upload or CPU improvement: actual transport fragmentation/coalescing and ordinary world traffic are outside the fixture.

Necessary native object-state replication remains unchanged. In particular, a normal fully loaded server-to-player transfer already needs no extra control handshake; batching cannot reduce the object's native state payload. This build does not replace the ZDO serializer, alter FiresGhettoNetworking's delta-cache invalidation or disable another mod's network patches.

Source inspection found that FiresGhettoNetworking 1.5.65 invalidates its per-peer delta snapshot on ForceSendZDO. Suppressing that generally would also suppress deliberate resynchronization. It has therefore not been changed. Repeated ForceSend marks inside one native send interval are already coalesced by the native set and are not automatically duplicate wire packets.

## Compatibility and testing

Install the test DLL in place of the existing TooManyLags DLL on the server and clients to use batching. Existing configs remain valid; no new config setting is required. Older peers continue receiving individual messages through capability negotiation. Player-only simulation is unchanged. The PhysicsObjects changes from the preceding side build are included; see `SIDE-PHYSICS.md` for their prerequisites.

Test with a disposable world/session and compare the same routes and object groups. A live network/bandwidth or Unity multiplayer test has not been performed. The tests cover actual controller dispatch, native contract binding, malformed envelopes, native-use vetoes, token replay, legacy fallback, shutdown cleanup and deferred recipient proofs. See `VALIDATION.md` for this build's hash and exact results.

The matching source archive contains the combined 1.0.61 implementation. `merge-provenance.json` records the imported side changes and preserved 1.0.60 baseline. Duplicate ZDO filtering, altered status resynchronization and player-specific send priorities are not part of this build.
