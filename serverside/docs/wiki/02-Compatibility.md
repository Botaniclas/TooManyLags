# Compatibility

## Required setup

| Component | Role | Where to install |
|---|---|---|
| BepInExPack Valheim | Plugin loader. | Dedicated server. |
| [Sarkastic.gg Dedicated Simulation](https://thunderstore.io/c/valheim/p/Sarkastic_gg/Dedicated_Simulation/) | MistrCech's simulation fork; loads and simulates the world around players. Its core must be active. | Dedicated server. |
| TooManyLags - Server Side | Shares supported ownership through a character-directed grid and Hostile Mob cones. | Dedicated server only. |

**Players can keep vanilla clients.** No addon handshake, camera report, custom client RPC or mandatory client DLL is introduced. This edition requires a dedicated server; it is not the local-host Standalone edition.

The declared minimum Dedicated Simulation package is **1.18.0**. Native metadata and startup contracts are checked against **1.18.0 and 1.23.2** for this beta preparation. Those checks do not constitute live multiplayer validation of every dependency feature or future release. Startup checks the required interfaces and installed patches rather than hard-locking a version number; if those contracts fail, the addon stops and leaves native/dependency simulation in place. Check the BepInEx log for warnings.

## Choose one ownership system

Do not combine this edition with **FiresGhettoNetworking**, another **TooManyLags** edition, or **BotanicServerAuthority/ServerBuildings**. They can assign conflicting owners. The original Serverside Simulations plugin shares the fork's identity but is not a substitute for the supported dependency.

For dedicated servers whose players can install client mods, **[TooManyLags - FiresGhettoNetworking Addon](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_FGN_Addon/)** remains the most-tested option. For local hosting or a setup without server simulation, use **[TooManyLags - Standalone](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_Standalone/)** instead.

## Dependency features and other mods

The addon selectively integrates with the dependency's existing assignment and boat paths for managed objects. It does not globally replace every ownership write. Unmanaged objects and disabled categories retain the dependency's policies.

- **FireControl:** protected by default with `RespectBackendFireOwnership=true`. Allowing grid-owned fire sources cannot preserve server-only fire enforcement on those player-owned sources.
- **Signs and portal names:** stay outside the grid. Their native submission and dependency permissions remain responsible for editing; no editor-start or move-to-unlock client protocol is added.
- **Inventories:** supported public stationary chests and native boat/cart cargo use real native owner checks. Private chests, graves, incinerators and custom inventory transactions stay native.
- **Vehicles:** supported boat/cargo identity and native helm controls are required. Boat and inventory share one owner. Native carts must have matching pulling/cargo links. Unsupported or custom vehicles stay native.
- **Modded controls and AI:** only supported native component contracts qualify. Unknown interaction callbacks, custom AI subclasses, stands and deferred actions are not automatically opted into grid ownership.

No blanket compatibility claim is made for every permission, anti-cheat or networking mod. Compare the dependency alone against the same setup with this addon when testing a problem.

## PUP FPS and collision replacements

**This edition has no PUP integration or collider-readiness guards.** Native server area readiness does not prove that another mod has finished restoring its replacement colliders on a client or server. Pairing it with PUP FPS or similar clustering mods is not validated by this beta; do not assume the safeguards of the FiresGhettoNetworking Addon are included.

## What server-only protection can observe

Automatic inventory transfers use native permission and matching ownership state; the addon does not open a fake player GUI. Nevertheless, the short opening phase before the source sets its local in-use flag is not a separate acknowledged lock. Purely local pending cart attachments and other unpublished client actions have similar limits.

Immediate native requests addressed to a previous owner can follow the current owner after a targeted state update. Completed picks are not replayed, and source updates cancel affected grace-window transfers. This is not a universal transaction protocol for arbitrary modded actions. Door input can still be refused during its native animation; this edition adds no dedicated door-animation lock.

For Hostile Mobs, server sources can inspect their actual attack object. Player sources expose only delayed native triggers and animation observations; private target, attack execution and cooldown state are not fully replicated or migrated. Unclear history can keep an old owner longer, and a new remote attack can begin before the server learns of it. **There is no atomic client-confirmed combat handoff or guarantee of identical combat timing.**

`EnableMobAttackForecast=true` additionally tracks observed ability timestamps and checks enlarged attack areas only for actual player-source handoffs. It can veto a likely follow-up attack but cannot reconstruct unseen abilities or exact private cooldowns. Unknown alternatives stay ready; source changes discard old assumptions. This is a small model using existing signals and native physics queries, not an additional simulated creature. The 100-ms margin and cached latency adjustment do not turn it into an authoritative lock. See Configuration to disable the extra forecast independently.

## Performance and testing

Server simulation uses server CPU and RAM; assigning nearby objects to clients distributes part of that work. Larger coverage can touch more objects. Native load/unload and object-state events maintain the index; unchanged coverage does not repeat object work. Cone decisions are cached, and only pending mob handoffs receive short due checks. No new all-mob physics polling loop is added.

Managed fixtures and installed-game metadata/startup checks cover the implementation. Live combat, sailing, simultaneous inventory use, hostile network conditions and combinations with other mods still need testing. Test a copy of the world before adopting the beta on a main server.
