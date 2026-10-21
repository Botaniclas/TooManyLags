# Fixed mob spawner defaults

Local 1.0.57 candidate based on the verified 1.0.56 test build.

`ServerOwnedMobsByDefault=true` now covers fixed persistent roots with a native `SpawnArea` or `CreatureSpawner`. It is independent of the tamed-animal category and interactive/static defaults. It requires the normal dedicated Server-Side Simulation controller; the default remains false. It does not place spawners in the player grid or change the target ownership of their creatures.

## Ownership path

The existing pre-construction event seeds an unowned eligible spawner with the server owner. The existing native ownership target selector also chooses the server, and its persistence hook retains the owner while a ready player loads the native area. New server-created spawners keep their native server owner without another write. Native release resumes when no ready player loads the area.

An existing active client owner is not stolen. Native active-owner predicates run before the selector; after ordinary native release/reassignment the new target is the server. This avoids interrupting a client spawn and its spawned-ZDO linkage. A server update/restart naturally applies the configured default as world data is loaded; this is not a live forced-takeover mode.

Spawner classification is cached by prefab, not world position or object ID. Missing prefabs are retried. SpawnArea must use the matching parent network root; CreatureSpawner must have that same view on its own GameObject. Another child ZDO does not make an entire location eligible. Moving bodies, creatures, item drops, interactive/inventory roots, distant/nonpersistent roots and SpawnSystem are excluded. The cache is part of the controller's scene-scoped catalog and is never consulted when the mob default is off.

## Native spawning retained

- SpawnArea retains its native two-second update, owner/active-area/player-distance checks, spawn interval, population limits and spawn-point selection.
- CreatureSpawner retains its native owner, day/night, keys, player-base, noise/range, group and respawn/linkage checks.
- FiresGhettoNetworking's dedicated OutsideActiveArea query checks loaded player areas rather than just the server's own reference position. Native IL audits verify this for the tested providers.
- Native destruction of nests/piles has no direct local-player dependency; no damage or loot implementation is replaced.
- SpawnSystem is deliberately excluded: native UpdateSpawning requires Player.m_localPlayer. General ambient spawns and other client-created spawns retain their existing paths.

No spawn method is called/replaced by the addon. No new callback, timer, periodic scan, ownership RPC, physics hook or per-world-object queue is added. Stable native passes reuse prefab/area caches and add no grid jobs, component scans, readiness queries or repeated owner writes for these spawners. Population counts use the normal world view of the owner running the native spawner.

## Verification scope

Production-controller tests pass 31,059 assertions, including both fixed types, the setting on/off, inner/outer fields, disabled animal categories, unrelated defaults, existing owners, native departure, cached classification, late prefabs, network-root boundaries and preserved position/revision guards. All 29 full verification groups pass; native combat/spawner IL audits pass 190 contracts for FiresGhettoNetworking 1.5.36 and 192 for the newer tested providers. The separate forced-prefix audit passes on the final DLL. Compiled combat policy differs from 1.0.56 only in its shared config-description string. Detailed evidence is recorded in VALIDATION.md and verification.json. No live Unity/PhysX/multiplayer spawn test is claimed.
