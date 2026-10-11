# TooManyLags - FiresGhettoNetworking Addon

A FiresGhettoNetworking extension designed to make nearby interactions feel almost latency-free: locally owned objects are processed directly on your client, without waiting for another player's connection. In shared areas, the server takes over so each player primarily feels their own latency instead of someone else's lag. Includes ownership improvements for Hostile Mobs.

## Forced FiresGhettoNetworking settings

TooManyLags applies required FiresGhettoNetworking settings at startup, including enabling **Server-Side Simulation** on the dedicated server by default. Their saved config values are ignored while TooManyLags is enabled; the config file stays unchanged. Other FiresGhettoNetworking settings and incompatible-mod safety blocks remain active.

**Quick start:** Install both mods on the dedicated server and every client, then play. FiresGhettoNetworking's default settings and Auto-Tune, combined with TooManyLags' defaults and required overrides, are intended to provide an optimal starting setup for almost all dedicated servers without manual configuration. You can still fine-tune all other FiresGhettoNetworking settings yourself; its config is worth a look.

**Server-Side Simulation adds CPU workload to the server.** For a weak or already overloaded server CPU, set `NoServerSideSimulation=true`. Objects and Hostile Mobs then stay player-owned; shared fields prefer the better-connected player. Server-owned buildings, default ownership and permanent station ownership are unavailable. Inventory, refilling and active-use safeguards remain; all clients need the mod for full handoffs. Shared interactions can still depend on the selected player's latency.

| Your setup | Recommended edition and mode |
|---|---|
| Dedicated server, strong CPU | This addon with **Server-Side Simulation** (default). |
| Dedicated server, weak CPU, with FiresGhettoNetworking | This addon with `NoServerSideSimulation=true`. |
| Dedicated server, weak CPU, without FiresGhettoNetworking | **[TooManyLags - Standalone](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_Standalone/)**. |
| Locally hosted multiplayer | **[TooManyLags - Standalone](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_Standalone/)**. |

## Why it helps

Valheim distributes simulation between object owners. A door or creature owned by another player can feel delayed when that player's connection is slow, even if yours is fast.

TooManyLags assigns nearby interactive objects to you, allowing supported interactions to run locally without a network round trip. Where players' fields overlap, the server becomes the owner. If your connection is poor while you are close to other players, you can still feel that delay when using server-owned objects; other players' interactions no longer depend on your client processing those objects.

Hostile Mobs prefer their current player target as owner. A high-ping target can use the server when another eligible player is nearby. The aim is to keep each player's experience responsive and prevent one slow connection from becoming everyone's lag.

This shares client workload more fairly, especially in large bases. Server ownership can increase **server CPU usage**. Server overload and protected ownership transitions can still cause delays; this is not a guarantee of zero lag.

### Help find problems

Real-world testing is still limited. Please share bugs and ideas through [GitHub Issues](https://github.com/Botaniclas/TooManyLags/issues).

Before reporting a bug, test on a separate copy of your server and world:

1. Run only BepInEx and FiresGhettoNetworking, without this mod or other mods. Repeat the steps that caused the problem. If it still happens, please report it to the relevant project rather than here.
2. Add this mod to that isolated setup and repeat the same steps. If the problem appears only with this mod, open an issue with a detailed description, steps to reproduce, mod versions, relevant settings and logs.

Thanks for helping improve the mod. Have fun, and enjoy smoother adventures together!

## How it works

- A camera-directed grid covers nearby interactive objects and idle tamed animals. One player's fields go to that player; overlapping fields go to the server. Stationary players still count.
- Cell or camera-sector changes request updates. Transfer delays, completed-field caching and unloading cleanup avoid repeated work. Isolated players use vanilla ownership by default.
- Hostile Mobs use AI target ownership with attack protection, consent, retries and safe server fallback. Hostile summons follow the same rules. Friendly magic summons, including skeletons, spirit animals and roots, stay outside both the grid and mob ownership policy. Ordinary tamed animals still use the grid; bosses and active controls retain their safeguards.
- Open inventories, active users and refilling remain protected. Local PUP collision/stability guards remain a separate feature.

## Ownership grid in action

These examples use [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) to show ownership: white objects belong to the server; purple objects belong to the player in these recordings. Movement and camera-sector changes update nearby grid cells using their configured delays.

**Ownership from above**

[![Ownership from above](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-1.gif)](https://github.com/user-attachments/assets/4a445578-1edf-4abb-8eea-f28183878d3c)

**Ownership while moving**

[![Ownership while moving](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-2.gif)](https://github.com/user-attachments/assets/8555a985-7696-4cc6-865a-5115c3695f72)

## Configuration

Configure ownership rules, the grid, delays, ping limits and **camera check timing on the server** in `BepInEx/config/botanic.toomanylags.fgnaddon.cfg`. The server sends camera timing to all clients. Client copies of these settings are ignored. `Enabled` and `EnableAIOwnership` must be enabled consistently on the server and clients; `EnableLogs` affects only the installation where it is changed. Each config entry states its scope.

**Console:** `tml_config list` lists settings; `tml_config <Key>` reads one; `tml_config <Key> <Value>` saves a change. Only admins can change server settings.

Intervals, delays and mob fallback limits apply immediately. Other server settings take effect after restart. `Enabled` and `EnableAIOwnership` require client restarts too.

Use [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) as an optional client-side debugging tool to see object and mob owners, check ownership transfers and tune your configuration.

| Feature | Defaults and options |
|---|---|
| Simulation mode | `NoServerSideSimulation=false`: full Server-Side Simulation. Set true for weak server CPUs; transfers stay between players. |
| Grid | 4-metre cells, rounded 7x7. Also 8/16 metres and square 3x3/rounded 5x5. 4 metres requires a rounded grid. |
| Direction and delays | Camera direction; inner 0s, forward 0.2s, outer 3s. Full-grid mode uses 2s. Missing camera data uses character heading. |
| Checks | Movement and camera each default to 0.1s. Camera checks support 0.05–5s, allowing up to 20 checks per second. Unchanged sectors create no new field requests or camera writes. Network delivery can add delay. |
| Leaving fields | Keep ownership: on. In directional mode, when only one player remains, red fields transfer automatically; forward/outer fields keep their owner until that player's next cell or heading-sector change. Fully vacated fields retain ownership. Native release still applies. |
| Categories and overrides | 24 object groups; separate static, default-interactive and permanent-refill server ownership. All three overrides default off and are ignored with NoServerSideSimulation=true. |
| Hostile Mobs | AI ownership on; server fallback target ping 200ms, other-player distance 6 metres. Without Server-Side Simulation, fallback uses a lower-latency nearby player. |

## Requirements and installation

1. Install **BepInEx, FiresGhettoNetworking and the same mod version on the dedicated server and every client**. Copy `TooManyLags.dll` into `BepInEx/plugins/TooManyLags/`.
2. Copy the supplied config into `BepInEx/config/`, or let first startup generate it. Keep `EnableAIOwnership` consistent across server and clients.
3. Restart. **TooManyLags automatically applies all required FiresGhettoNetworking settings at startup; no manual FiresGhettoNetworking configuration is needed.** These overrides apply to the current session only. Your saved FiresGhettoNetworking config and other FiresGhettoNetworking settings remain unchanged.

**PUP FPS is optional.**

The imported PUP readiness check supports the verified PUP FPS 1.0.34 build. Other PUP builds block AI server fallback; target-player ownership still works. Without PUP, native loading readiness is checked instead.

**BotanicPupPatch is recommended when combining PUP FPS with FiresGhettoNetworking.** It adds compatibility safeguards for cluster collisions and building stability during loading and ownership changes. Install it on the server and all clients; it is optional for TooManyLags.
