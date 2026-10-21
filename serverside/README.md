# TooManyLags - Server Side

**Less connection lag, no client mod.** A multiplayer ownership optimisation addon. Nearby supported interactions run on your own client; shared fields use the server. Hostile Mobs use grid and forward-cone ownership to bring their simulation closer to the action.

Currently built on **[Sarkastic.gg Dedicated Simulation](https://thunderstore.io/c/valheim/p/Sarkastic_gg/Dedicated_Simulation/)**. Compatibility with as many server-side simulation mods as possible is planned. Please suggest the mods you'd like supported through **[GitHub Issues](https://github.com/Botaniclas/TooManyLags/issues)** to help guide future integrations. Thank you!

**Beta:** real-world testing is limited. Server-only attack and handoff observations have limits; this edition does not promise the same safeguards or features as the client-assisted addon.

## Choose your version

| Your setup | Recommended edition and mode |
|---|---|
| Dedicated server; players should need no client mod | **This mod** with **Sarkastic.gg Dedicated Simulation**. Beta. |
| Dedicated server, strong CPU; client mods are welcome | **[TooManyLags - FiresGhettoNetworking Addon](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_FGN_Addon/)** with Server-Side Simulation. **Most-tested option.** |
| Dedicated server, weak CPU, with FiresGhettoNetworking | The FiresGhettoNetworking Addon with `NoServerSideSimulation=true`. |
| Dedicated server, weak CPU, without a simulation backend | **[TooManyLags - Standalone](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_Standalone/)**. |
| Locally hosted multiplayer | **[TooManyLags - Standalone](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_Standalone/)**. |

## Quick install

1. Install **BepInEx, Sarkastic.gg Dedicated Simulation and this mod on the dedicated server only**. Players need neither addon nor simulation mod.
2. For manual installation, put `TooManyLagsServerSide.dll` in `BepInEx/plugins/TooManyLagsServerSide/`, alongside the separately installed dependency.
3. Start the server. Begin with the defaults; edit `BepInEx/config/botanic.toomanylags.dedicatedaddon.cfg` and restart when changing settings.

**Server-Side Simulation needs server CPU and RAM.** This addon shares some work with nearby clients; it is not a mode for running without server simulation. Do not combine it with FiresGhettoNetworking or another TooManyLags edition.

## See the grid concept

White objects belong to the server; purple objects belong to a player. Recorded with the FiresGhettoNetworking Addon and the optional [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) overlay. **Server Side uses character direction and a different default footprint; these recordings illustrate the ownership concept.**

**Ownership from above**

[![Ownership from above](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-1.gif)](https://github.com/user-attachments/assets/4a445578-1edf-4abb-8eea-f28183878d3c)

**Ownership while moving**

[![Ownership while moving](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-2.gif)](https://github.com/user-attachments/assets/8555a985-7696-4cc6-865a-5115c3695f72)

## Example settings

White means the server; colours identify players. These animations use Server Side's actual default grid: full rear 3x3, three forward cells, yellow cells off and character direction. Fast movement makes the forward row immediate. Both retention modes use the same footprint and player routes.

| Default: return on exit — CPU: mostly server | Retain ownership — CPU: shared |
|---|---|
| `KeepOwnershipOnCellExit=false` | `KeepOwnershipOnCellExit=true` |
| [![Server-default ownership with returns: vacated cells return to the server; nearby players own their current work areas.](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/serverside/docs/ownership-examples/compact/return-on-exit.gif)](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/serverside/docs/ownership-examples/return-on-exit.gif) | [![Server-default ownership with retention: players leave ownership trails; unvisited and shared cells stay server-owned.](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/serverside/docs/ownership-examples/compact/retained.gif)](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/serverside/docs/ownership-examples/retained.gif) |

Click a preview for the full-size animation. CPU notes refer to simulation of managed objects. Idle, ready objects are illustrated; loading and active-use safeguards can delay real handoffs.

## Configuration

Use the **[Thunderstore Wiki](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_Server_Side/wiki)** for configuration, supported interactions and compatibility. **All settings are server-side and require a server restart.** Vanilla clients need no configuration.

For an optional solo combat ownership stress test, see **Solo combat handoff test** on the Configuration Wiki page. It defaults off, runs on one selected mob and keeps normal safeguards active.

## How it works and what to expect

Vanilla can let a distant player simulate objects you are trying to use. This addon prepares supported objects through small nearby fields: exclusive fields select their player, overlapping fields select the server, and vacant fields return to the server by default. Public chests, boats, idle carts, harvestables, loose drops and idle tamed animals have their own native-use rules.

Hostile Mobs follow exclusive player fields. In shared fields, one eligible player in the mob's forward cone takes ownership; zero or multiple players select the server by default. Normal cone range is **8 metres**, bosses **10 metres**. Observed attacks delay transfers; the cone does not read or change the actual attack target. An optional ability forecast adds a conservative check for possible follow-up attacks before player-owned mobs change owner; combat can retain their owner longer.

**The goal is local responsiveness wherever supported ownership can be prepared in time.** Shared/server-owned objects still depend on your connection, and loading, protected use or remote attack observations can delay a handoff. Signs, portal editing, deferred actions and unsupported modded controls retain their native/dependency handling. This edition has no client-camera data or PUP collider guards.

## Report bugs and ideas

Real-world testing is still limited. Please share bugs and ideas through [GitHub Issues](https://github.com/Botaniclas/TooManyLags/issues). Include **Server Side** and the version; the Wiki explains how to isolate a reproducible issue.
