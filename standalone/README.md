# TooManyLags - Standalone

Keep nearby interactions local and stop an arbitrary distant owner from slowing down your adventures. TooManyLags prepares nearby ownership before you interact and gives Hostile Mobs smarter ownership, without requiring FiresGhettoNetworking or Server-Side Simulation. This lightweight edition suits locally hosted multiplayer and servers with little spare CPU.

## Choose your version

| Your setup | Recommended edition and mode |
|---|---|
| Dedicated server, strong CPU | **[TooManyLags - FiresGhettoNetworking Addon](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_FGN_Addon/)** with **Server-Side Simulation**. **Most-tested mode.** |
| Dedicated server, weak CPU, with FiresGhettoNetworking | **[TooManyLags - FiresGhettoNetworking Addon](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_FGN_Addon/)** with `NoServerSideSimulation=true`. |
| Dedicated server, weak CPU, without FiresGhettoNetworking | **This mod**. |
| Locally hosted multiplayer | **This mod**. |

For dedicated servers, the **FiresGhettoNetworking Addon is highly recommended instead of this edition**.

## Quick install

1. Install **BepInExPack Valheim** and put `TooManyLagsStandalone.dll` in `BepInEx/plugins/TooManyLagsStandalone/` on the host/server.
2. Install the same version on clients for camera direction and full supported handoffs, then restart and play.

**Clients are optional:** players without the mod can still join, using character direction and a restricted set of simple interactions. Inventories, refill stations, signs/portals, vehicles, tamed animals and Hostile Mob target handoffs need compatible modded clients on both sides of the transfer. Only BepInEx is required.

## See it in action

These FiresGhettoNetworking Addon recordings show the shared directional grid using the optional [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) overlay. Standalone uses the same grid, but keeps simulation on players rather than moving shared objects to the server.

**Ownership from above**

[![Ownership from above](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-1.gif)](https://github.com/user-attachments/assets/4a445578-1edf-4abb-8eea-f28183878d3c)

**Ownership while moving**

[![Ownership while moving](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-2.gif)](https://github.com/user-attachments/assets/8555a985-7696-4cc6-865a-5115c3695f72)

## Configuration

Start with the defaults. Configure gameplay **on the host/server**; modded clients follow its settings. Find configuration, admin commands and compatibility details in the **[Thunderstore Wiki](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_Standalone/wiki/)**.

## How it works and what to expect

Your nearby grid prepares interactive objects for local use. Where players' fields overlap, a suitable player with a better measured connection takes ownership; a small margin and overlap hold avoid needless switching. Hostile Mobs normally follow their current player target's ownership. A high-ping target can use a better-connected player near the mob instead, without changing the attack target.

**Locally owned interactions avoid another player's connection delay.** Shared objects and shared combat can still depend on the selected player's connection: this edition never moves simulation to a dedicated server. Open inventories, refilling, burning and other supported active use protect ongoing actions before a handoff.

Native sign and portal-name editing keeps the edited object's owner until the player moves. Ownership is confirmed before the text window opens; other objects keep their normal grid behavior.

Friendly magic summons and bosses retain vanilla ownership. Static walls/floors and loose item drops also remain outside the grid. This edition has no PUP FPS integration. Do not combine it with TooManyLags - FiresGhettoNetworking Addon.

## Report bugs and ideas

Real-world testing is still limited. Please share bugs and ideas through [GitHub Issues](https://github.com/Botaniclas/TooManyLags/issues).
