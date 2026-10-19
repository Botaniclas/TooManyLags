# Compatibility

Install TooManyLags on the dedicated server and every client for its full features. The CommunityPatch integration below runs exclusively on the dedicated server.

## How the mods fit together

| Mod | Role | Required? |
|---|---|---|
| **[FiresGhettoNetworking](https://thunderstore.io/c/valheim/p/VerdantsAscent/FiresGhettoNetworking/)** | Provides the networking and simulation foundation. TooManyLags applies required session settings automatically unless AutoConfigureFiresGhettoNetworking=false. Server-Side Simulation is enabled by default and can increase server CPU usage. | Required for this addon. |
| **TooManyLags** | Distributes nearby object ownership through its grid and gives Hostile Mobs target-based ownership. With PUP FPS installed, it also includes the collision and building-stability safeguards previously supplied by BotanicPupPatch. | Install on server and clients. |
| **[PUP FPS](https://thunderstore.io/c/valheim/p/PUP82/PUP_FPS/)** | Combines eligible objects into clusters and supplies their replacement colliders. It controls which objects are combined and when its collisions are prepared. | Optional. TooManyLags works without it. |
| **[ValheimCommunityPatch](https://thunderstore.io/c/valheim/p/MidnightMods/ValheimCommunityPatch/)** | Supplies additional fixes and optimisations. With automatic Server-Side Simulation setup, TooManyLags coordinates its conflicting server loading policies; client optimisations remain unchanged. | Optional. Requires supported fix-control and scene interfaces; see below. |

FiresGhettoNetworking's Server-Side Simulation loads the world around players on the server. PUP FPS optimizes clustered objects; it does not replace the ownership system. TooManyLags coordinates ownership and, when PUP FPS is present, protects building support and physics during cluster loading and ownership changes. It keeps PUP's native collider preparation and body guards in charge.

For weak dedicated-server CPUs, `NoServerSideSimulation=true` keeps simulation on players. FiresGhettoNetworking is still required; server-owned defaults are unavailable. See **Configuration** for the other edition and mode choices.

## ValheimCommunityPatch compatibility

**Keep CommunityPatch installed while using FiresGhettoNetworking's Server-Side Simulation.** TooManyLags coordinates their conflicting loading policies automatically with the default setup. This integration has been checked with **ValheimCommunityPatch 0.37.0 and FiresGhettoNetworking 1.5.65**.

On the dedicated server, use:

- `AutoConfigureFiresGhettoNetworking = true`
- `NoServerSideSimulation = false`

At server startup, **before the world loads**, four overlapping CommunityPatch loading/unloading optimisations stand down through its official API. FiresGhettoNetworking then manages loading around every player. **The object-unload crash guard keeps its setting.** Other fixes and independent networking compatibility decisions retain their normal behaviour.

**Server-only change:** client-side CommunityPatch optimisations and saved config files are unchanged. Existing clients do not need a TooManyLags update specifically for this fix; update the server and restart it. The integration adds no per-frame or physics polling.

CommunityPatch needs its fix-control API, introduced in 0.35.0, and the supported scene interfaces. An older or changed interface, an unknown conflicting scene patch, or a refused handover keeps the simulation safety block active. Manual setup and the mode without Server-Side Simulation do not activate this integration.

FiresGhettoNetworking loads first, so an initial log message saying simulation stayed off can appear. The later **"TooManyLags coordinated CommunityPatch scene scheduling"** and simulation-enabled messages confirm the completed setup. The handover lasts for that server session only; restarting without TooManyLags restores CommunityPatch's normal startup behaviour.

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
