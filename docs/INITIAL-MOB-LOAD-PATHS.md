# Initial mob ownership: 1.0.56 test

Based on the verified local 1.0.55 test build. No grid, camera, priority, packet, interaction, attack or collision protocol is changed.

## Confirmed gaps

- The 1.0.55 initial Hostile Mob selector rejected `Created` data and any existing server instance. Native `ZNetScene.AddInstance` sets `Created` when registering a local instance; it does not prove that the ZDO has an owner. A regression reproduces the unwanted player assignment for an otherwise eligible unowned created mob.
- Native `ZNetView.Awake` can construct a new server-origin ZDO already owned by the server. That path does not pass through `ReleaseNearbyZDOS`'s first-unowned assignment. Without an initial lease, the later native player-area pass could select a player before the normal default policy reclaimed it.
- Native `SpawnSystem.UpdateSpawning` requires a local player and zone ownership. Its `Spawn` instantiates locally, and `CreateNewZDO` initially uses the creating process's session ID. A client-origin spawn therefore genuinely starts on its creator before the server receives it. A server-only patch cannot prevent that first local owner. Initial spawn routing/initialisation is deliberately not replaced.

## Narrow correction

One prefix at native server `ZNetScene.CreateObject` entry seeds only eligible **unowned** mobs with their configured server default. Idle tamed animals retain category/use/follow/summon checks and settled player-field precedence. The Hostile Mob policy retains its mode, native loading-area, health, boss/friendly, possession and persistent-body checks. Ordinary non-creature objects do not perform the mob peer-area scan.

Native creation, terrain/type gates, ordering, `m_initZDO`, Awake and PUP body safeguards still run. No actor is instantiated by the patch. An already created **unowned** mob is also eligible at the existing native assignment wrapper; existing nonzero owners remain excluded.

Newborn server-origin Hostile Mobs use the existing AddInstance event to retain the initial owner without another owner write. This requires a matching current-world object, current server UID origin, owner revision zero, an eligible persistent synchronized body and a ready native loading peer. The existing initial lease, deferred AI observation, balance pass and unload/reset cleanup handle adoption and release. The patch adds no periodic loop, ownership RPC, spawn request or client protocol.

Client-origin spawns and pre-existing player-owned actors retain guarded handoffs, including attack/readiness checks. A brief creator owner can still be observed for those spawns. No claim is made that every observed player-to-server transition has been eliminated.

## Checks

- Baseline reproduction fails on the old created-instance filter; corrected production path passes.
- 1,925 combat assertions pass on CLR and embedded Valheim Mono, covering construction, created/unowned data, server-origin retention, zero extra writes, existing owners, exclusions, adoption and cleanup.
- 29,905 full controller/grid/router assertions pass, including initial tamed defaults and settled player/overlap fields.
- Actual native IL confirms local spawn origin and the native init/Created semantics. Full verification and final DLL hash are recorded in `VALIDATION.md` and `verification.json`.

Server-only test update; all 55 config values and existing clients are retained. No live Unity/PhysX/multiplayer test or release publication is claimed.
