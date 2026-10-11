# TooManyLags - FiresGhettoNetworking Addon 1.0.9 release validation

## Mob policy precedence

TooManyLags takes precedence only when EnableAIOwnership is enabled and its selected mob module initializes successfully. Startup runs after plugin Awake, so installation order cannot leave two optional combat transpilers installed. The bridge binds BotanicPupPatch's existing optional combat Stop/Install/Enabled contract without adding a dependency or changing its config. Stop removes only that module; collision, support, cart/animal and streaming guards remain installed. An unsupported contract prevents takeover. Disabled TooManyLags leaves the previous policy untouched; failed normal or player-simulation setup restores it using the original config and network assembly/version. No new polling or gameplay hooks are added for arbitration.

The transaction fixtures pass 40 assertions on both Windows .NET and Valheim's embedded Mono using real Harmony and BepInEx.ConfigFile, including successful, disabled, absent, inactive, failed and partially stopped modules; saved settings; exact single-policy installation; rollback; and preservation of separate collision/streaming Harmony owners. The native audit checks the actual startup IL for suspension before installation, successful-module gating and finally-based rollback. The existing BotanicPupPatch 2.1.0 DLL contract is inspected directly. No BotanicPupPatch rebuild is required.

This is startup arbitration, not a live cross-plugin protocol switch. Server and clients should use this TooManyLags version with consistent AI settings. No live multiplayer test has been performed.

---
# TooManyLags - FiresGhettoNetworking Addon 1.0.8 release validation

`NoServerSideSimulation=false` retains the normal dedicated-server simulation path. With true, startup validates the seven actual FiresGhettoNetworking simulation patch groups before removing only their hooks under FiresGhettoNetworking's Harmony owner. It refuses changes after a network/world object exists. Simulation config and runtime flags are disabled without changing the saved file; unrelated networking and other mods' hooks remain. Station routing remains enabled. The server-owned building/default/refill overrides are not instantiated in this mode.

The player-simulation module is embedded from TooManyLags Standalone 1.0.1, with an isolated namespace/protocol, shared addon settings/camera heading and existing FiresGhettoNetworking connection metrics. The server announces the mode; clients need no matching mode config. Normal-mode clients stop polling the internal endpoint after confirmation; normal-mode servers only answer its connection handshake. No extra latency probe is sent. LinkController's unknown zero-ping status is not treated as a low-latency candidate.

Player-mode overlaps use smoothed latency with a 30 ms advantage margin. Hostile Mobs retain target ownership unless an eligible nearby player has a sufficiently lower latency. A configured zero ping threshold uses the existing 250 ms relative rule. Friendly summons, bosses, active controls, open/pending inventories, ongoing attacks and the 10-second refill protection retain their exclusions/guards. Receiver preparation, owner revision, source consent, activation acknowledgement, stale-request forwarding and bounded handoff queues are retained. An already active external mob policy silently prevents duplicate target management.

Automated validation covers the normal path and the embedded player path: startup on/off and saved-file preservation with real BepInEx config; exact native hooks and contracts against the installed Valheim release and FiresGhettoNetworking 1.5.36/1.5.41; grid/cache/unload behavior; 4,188 production handoff assertions; and 12 mode-negotiation/connection assertions. Actual native interface contracts use the modern metadata host where the Windows desktop CLR cannot load default interface implementations. Fixtures do not simulate live Unity physics or Steam packet ordering.

**No live dedicated-server or multiplayer test has been performed.** Standalone changes alongside this release are documentation only; its simulation code is unchanged.

---
# TooManyLags - FGN Addon 1.0.6: verification

Version 1.0.6 adds startup-only overrides for required FiresGhettoNetworking settings. The grid, AI handoff protocol and ownership protections remain unchanged.

## Required FiresGhettoNetworking session settings

After FGN's Awake and before TML installs its grid or AI policy, FgnSessionSettings replaces only the required static ConfigEntry references with entries in an isolated, non-saving ConfigFile. FGN's own ConfigFile keeps the original entries: later saves and reloads do not persist the overrides. Dedicated servers use SSS and station routing, with broad/selective ownership disabled. Clients only disable those conflicting ownership options. Creature balancing is enabled on both sides when TML AI ownership is configured on; disabling it leaves the existing FGN balancing setting untouched. No config getter patch, event subscription or recurring override scan is installed.

If FGN already installed server simulation, its initializer is not repeated. If it was off, TML checks FGN's dedicated-server detection and scene-scheduler compatibility, checks the ValheimPerformanceOptimizations plugin identity, then invokes FGN's native ApplyServerSideSimulationPatches before setting SimulationActive, matching FGN's own startup order. Existing broad/selective prefixes read the disabled entries and pass through; their startup-only creature/ship ownership mode flags are cleared. Compatibility blocks and missing/uninitialized contracts leave TML inactive rather than announcing readiness. SSS activation produces one startup CPU warning regardless of optional diagnostic logging.

Tests use the actual BepInEx ConfigFile and production setup code to check 52 assertions covering disabled/enabled SSS, one-time initialization, stale modes, unchanged saved entries even after Save/Reload, metadata, client scope, disabled AI and blocked/missing contracts. Native IL/metadata checks verify the initializer, completion order, safety properties, fields and dynamic ownership gates against FGN 1.5.36 and 1.5.41. No live Unity/dedicated-server startup or multiplayer session was performed; server CPU impact depends on the loaded world and owned objects.

Based on TooManyLags 1.0.0 and BotanicServerAuthority 3.7.4. Existing grid geometry, directional timers, inventory consent, refilling protection, summon exclusion, default ownership, regional bypass and unloading cleanup are retained. The original project remains unchanged.

## Camera heading

Clients sample the completed native GameCamera.LateUpdate at the configured cadence. Camera intervals support 0.05–5 seconds with an unchanged 0.1-second default; the server movement cadence retains its 0.1-second minimum. Only changed eight-sector headings are written into the locally owned player's existing ZDO. No camera RPC, server polling cache or new network protocol is needed. The key is session-only. Server consumers validate player ownership; missing, invalid, freefly or vertical camera data falls back to character heading. Destruction clears a valid local heading; disconnect/world replacement cannot write into an old world.

Fixtures cover sector boundaries, stationary camera changes, unchanged samples, freefly, vertical/missing cameras, cleanup, ownership, world changes and the actual controller's directional requests. Native code/delegate inspection verifies supported hooks and that received player rotations do not rely on local SetRotation events. Actual network delivery remains subject to transport/backlog latency.

## Imported AI policy

FgnCombatOwnership, FgnCombatBridge, FgnTargetEvents, FgnAttackRetry, FgnCombatStatus and FieldReader are imported from BotanicPupPatch 2.1.0. Detailed source equivalence and hashes are recorded in tests/combat-import-equivalence.json. Namespace, session keys, RPC names, console command and Harmony IDs are isolated to prevent cross-mod callback replacement. Config branding/default activation, duplicate-policy handling and the narrow friendly-creature exclusion are the explicit integration changes.

Original target reports, revision checks, client consent, attack blocking/retry, FGN candidate scans and transfer limits, tamed/boss/control exclusions, high-ping server leases, damage forwarding and session cleanup remain. Console diagnostic: toomanylags_ai_status. Duplicate active or dormant original AI hooks disable this addon's AI policy; they do not disable the grid or optional collision guards.

The optional PUP adapter retains only the exact collision-readiness proof needed for server mob fallback, including CellReadiness. It does not import physics, support or body guards. Without PUP, exact native scene/area readiness is checked; unsupported present PUP denies server fallback. The copied PUP proof is verified against PUP FPS 1.0.34, including its fingerprint. This is not a dependency or a version lock on the grid/FGN feature. PupPatch has no assembly reference or plugin dependency.

## Friendly magic creatures

FriendlyCreatures.IsFriendly reads the native raw Character faction and excludes real players. Current vanilla magic skeletons, four spiritcaller animals and green roots use Faction.Players; hostile Troll_Summoned uses PlayerSpawned and summoned charred use Demon. Ordinary taming does not alter the raw faction. Native source/assets were inspected read-only; the compact classification evidence is in tests/friendly-summon-classification.json. This is a faction/behavior filter, not a record of who created an object.

The grid checks loaded instances before allocating a node and uses the existing cached actor on later checks. Legacy unsummon-marker protections are retained. AI eligibility rejects friendly prefabs and actual loaded friendly actors before watches, target reports or handoff requests. A scene-scoped, bounded prefab cache avoids repeated component lookups; missing prefabs are not cached and world/scene cleanup releases references. Hostile summons follow the unchanged Hostile Mobs rules. Existing player-only target reporting, boss/control exclusions, attack waits, caps, consent and readiness checks remain.

## Inner-only departure transfers

With KeepOwnershipOnCellExit enabled in directional mode, an overlap ending at one stationary player automatically transfers only that player's inner/red fields. Forward/outer fields retain their settled policy owner and cancel their field timer. A real cell or heading-sector change of the remaining player releases those fields, including unchanged footprint intersections. Full-grid mode and disabled retention keep the previous behavior. Classification uses the field tier, not a floating-point comparison of configured delays.

The pure grid stores one deferred bit per active policy and one refresh bit per existing peer. A counter skips deferred-field lookups entirely while none exist; there is no additional timer, position query, object scan or player-pair search. Timer/guard processing and native in-flight inventory/FGN transactions stay intact. Fixtures cover eight headings, both rounded shapes, all three tiers, unchanged motion, simultaneous update order, reversed delays, readiness, transient overlap, multiple peers and 2000 departure/unload/reuse cycles. Deferred state is removed with active coverage; only the existing capped allocation pool remains.

## Checks and limits

Version 1.0.7 changes the installation wording to explicitly state that required FiresGhettoNetworking settings are automatic. Runtime source and configuration defaults remain the same as 1.0.6 except for the plugin/assembly version and generated config header. The release is rebuilt and checked before packaging.

The 1.0.6 documentation refresh adds the two supplied GitHub videos to the repository README and links their GIF conversions from the Thunderstore README. Both GIFs were decoded frame by frame and visually reviewed: 640x358 pixels, 10 fps, infinite loop, 8.2/13.8 seconds. The original recordings remain available through each GIF's link. Media are hosted in the GitHub repository, outside the install ZIP. The DLL and previous runtime test results are unchanged; metadata/media hashes and archive contents were checked again before publication.

Build uses installed Valheim/BepInEx APIs with warnings treated as errors. Controller/grid, directional geometry, field cache, region bypass, inventory and refill suites run against production logic. AI fixtures run on both CLR and Valheim's embedded Mono; optional readiness and modern runtime bridge fixtures are separate. Native IL/contracts are checked against FiresGhettoNetworking 1.5.36 and 1.5.41. Both ownership transpilers compose in either order, preserving native branches and changing only the intended getter/two calls. Startup validates actual contracts rather than rejecting FGN version numbers.

The actual config binding prefixes execute with installed BepInEx.ConfigFile: 42 settings, nine English sections, reload/save equality. Verification JSON records the final DLL, native dependencies, copied sources and config hashes. Release packaging checks version, DLL hash, manifest, README and config contents.

No live multiplayer, real packet transport, Unity physics session or in-game latency benchmark was performed. Automated checks cannot guarantee immediate delivery or compatibility with arbitrary future/third-party changes. The new filter covers native summons and the current faction of loaded actors. Unloaded actors use their prefab's current faction; arbitrary modded per-instance faction changes while unloaded cannot be inferred from native ZDO data. Modded friendly creatures using another faction are not identified by this predicate; existing summon safeguards still apply. Local PUP collision/stability protection remains separate.
