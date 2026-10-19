# CommunityPatch scene integration

Prepared for TooManyLags 1.0.49. Checked against the actual ValheimCommunityPatch 0.37.0 and FiresGhettoNetworking 1.5.65 DLLs, including the installed `Botanicserver` profile. No installed files are modified by building or testing this integration.

## Scope and startup

The existing automatic-backend-setup option controls this integration. It runs during the enabled dedicated server's `Awake`, before a network world or scene exists, only when Server-Side Simulation is requested and no separate incompatible scene scheduler is detected. Clients, local hosts, manual setup and the no-simulation mode do not call it. An explicit soft dependency makes CommunityPatch load first; there is no hard DLL/package dependency.

All known scene-prefix signatures, their original target methods, the fix-control API, fix states and disable permissions are checked before asking CommunityPatch to change anything. Its official API makes four performance policies inert for the session: SceneIdleSkipPatch, SpawnQueueCachePatch, ZoneDiffRemovalPatch and RemoveSweepPacingPatch. Saved config entries are untouched. Calls are made from the normal Unity main-thread startup.

CommunityPatch keeps its now-inert Harmony methods installed. The integration therefore rewrites only calls to FGN's prefix-presence helper inside `ValheimCommunityPatchCompat.Detect`. This recompiles the detector even if its small helper was already JIT-inlined during FGN's earlier startup. The native helper still runs; only known CommunityPatch prefixes on their exact reviewed scene targets stop counting as conflicting schedulers. Terrain detection and independent ZDO serialization/network compatibility decisions keep their existing behaviour.

`RemoveObjectsNrePatch` is retained, including its original setting and orphan recovery. Its prefix stamps the near/distant ZDO lists supplied by FGN and performs guarded vanilla removal; it does not discover a streamed ring around the headless server reference position. The production-binary audit checks its signature, fast/recovery calls and absence of ZNet/ZoneSystem ring-discovery calls.

FGN's own normal simulation initializer is then invoked by the existing session setup. On backends that provide `ZSyncHookGate.Refresh`, startup also refreshes that native gate after changing simulation state, matching FGN's own `Awake` completion order. This attaches the native simulation transform-rescue hook even when transform limiting and remote-motion timestamps are both disabled; stopping an already active simulation refreshes the same gate without removing hooks needed by those other settings. The integration does not declare readiness by setting a flag, replace scene creation/removal itself, patch ownership decisions, or add interaction/collision guards.

## Failure behaviour and cost

Unknown or misplaced CommunityPatch prefixes remain blocking. Any later call to FGN's detector also checks newly installed prefixes against their exact allowed targets; the bridge does not add polling for mods that change patches after startup. Missing/failed APIs, denied stand-downs, invalid states and changed bindings do not grant compatibility. All four permissions are preflighted; if a later API call nevertheless refuses, earlier successful stand-downs cannot be rolled back: CommunityPatch defines them as session-long. The normal FGN safety block remains intact in that case, and a restart restores CommunityPatch's startup configuration.

The only additional installed hook is on FGN's compatibility detector, which is used at startup. There is no new frame, physics, object, region or player polling. The retained state is a fixed three-target method map and one native predicate delegate, independent of world size. Enabling FGN simulation and giving up overlapping CommunityPatch server scheduling optimisations can still change server CPU usage; this is distinct from the bridge's own startup work.

## Validation limits

`test-communitypatch.ps1` runs the production bridge with real Harmony in managed fixtures and checks the actual dependency DLL contracts. The fixtures cover original safety blocking, native creation/removal after stand-down, retained crash recovery and unrelated hooks, repeated detection, API failures, live-world/client rejection, cleanup, and unknown late prefixes. The complete project verification also covers existing ownership, inventory, combat, loading, camera, config and optional PUP contracts.

These checks do not start a Unity world or certify every combination of CommunityPatch's other fixes. Live multiplayer checks remain necessary: login, two players far apart, portals, object load/unload, interaction/physics, and reconnect. An initial FGN log line saying simulation stayed off is expected because FGN loads first; the later TooManyLags coordination and simulation-enabled messages describe the completed setup.
