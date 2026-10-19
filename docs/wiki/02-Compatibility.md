# Compatibility

This page describes TooManyLags 1.0.45. Install matching TooManyLags versions on the dedicated server and every client.

## How the mods fit together

| Mod | Role | Required? |
|---|---|---|
| **[FiresGhettoNetworking](https://thunderstore.io/c/valheim/p/VerdantsAscent/FiresGhettoNetworking/)** | Provides the networking and simulation foundation. TooManyLags applies required session settings automatically unless AutoConfigureFiresGhettoNetworking=false. Server-Side Simulation is enabled by default and can increase server CPU usage. | Required for this addon. |
| **TooManyLags** | Distributes nearby object ownership through its grid and gives Hostile Mobs target-based ownership. With PUP FPS installed, it also includes the collision and building-stability safeguards previously supplied by BotanicPupPatch. | Install on server and clients. |
| **[PUP FPS](https://thunderstore.io/c/valheim/p/PUP82/PUP_FPS/)** | Combines eligible objects into clusters and supplies their replacement colliders. It controls which objects are combined and when its collisions are prepared. | Optional. TooManyLags works without it. |

FiresGhettoNetworking's Server-Side Simulation loads the world around players on the server. PUP FPS optimizes clustered objects; it does not replace the ownership system. TooManyLags coordinates ownership and, when PUP FPS is present, protects building support and physics during cluster loading and ownership changes. It keeps PUP's native collider preparation and body guards in charge.

For weak dedicated-server CPUs, `NoServerSideSimulation=true` keeps simulation on players. FiresGhettoNetworking is still required; server-owned defaults are unavailable. See **Configuration** for the other edition and mode choices.

## ValheimCommunityPatch

With `AutoConfigureFiresGhettoNetworking=true` and `NoServerSideSimulation=false`, TooManyLags coordinates supported CommunityPatch scene fixes at dedicated-server startup. Four overlapping loading/unloading optimisations stand down through CommunityPatch's official API so FiresGhettoNetworking can simulate all player areas. The object-unload crash guard retains its setting. Saved configs and client-side CommunityPatch settings are unchanged.

This integration is checked with CommunityPatch 0.37.0 and FiresGhettoNetworking 1.5.65. CommunityPatch needs its fix-control API, introduced in 0.35.0; incompatible or unknown scene prefixes retain the simulation safety block. Other networking compatibility decisions remain in effect. Manual setup and the mode without Server-Side Simulation do not activate this integration. No new client version is required specifically for this startup fix.

## Built-in PUP safeguards

`[08 - PUP FPS Compatibility] EnablePupCompatibility = true` enables the integrated guards automatically when their required mods and interfaces are available. Install PUP FPS on the server and clients that use it; use matching versions.

- Protects building stability while required cluster collisions load.
- Keeps required collision protection for carts, items, animals and other native PUP-guarded physics objects, including dedicated-server loading.
- Adds FiresGhettoNetworking ownership-change and streaming compatibility.

The switch is **local on each server/client** and requires a restart. With it off, or with a dependency missing, the embedded module installs no hooks, starts no timers and performs no runtime checks. Missing dependencies already disable it automatically, so turning it off manually is unnecessary in that case. TooManyLags' ordinary ownership features retain their normal costs.

**A separate BotanicPupPatch installation is no longer needed.** If one is detected, the entire embedded guard module stays off and the separate patch remains responsible for collision/stability protection. There are no duplicate embedded guards. The existing TooManyLags AI ownership feature remains controlled by `EnableAIOwnership`.

Required interfaces are checked at startup rather than locked to a PUP version. PUP FPS 1.0.34 and 1.0.36 are covered by the native contract checks. An incompatible interface disables the affected supplement and reports a startup warning. Real-world testing remains limited; this is not a guarantee for every future update or mod combination.

## Signs and portal names

TooManyLags prepares the editor's ownership before opening a managed sign or portal's native text window. Only that object's ownership is protected until the player moves, even after confirming or closing the window. Camera turns do not release it. Rejected grid takeovers are discarded; ending the protection does not replay them.

Update both server and clients for this protection. Disabled object categories and unrelated text interfaces retain their native behavior. The same protection works without Server-Side Simulation.
