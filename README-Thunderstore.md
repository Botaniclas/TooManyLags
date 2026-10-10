# TooManyLags - FGN Addon

A FiresGhettoNetworking extension designed to make nearby interactions feel almost latency-free: locally owned objects are processed directly on your client, without waiting for another player's connection. In shared areas, the server takes over so each player primarily feels their own latency instead of someone else's lag. Includes ownership improvements for Hostile Mobs.

## Forced FiresGhettoNetworking settings

TooManyLags applies required FiresGhettoNetworking settings at startup, including enabling **Server-Side Simulation** on the dedicated server. Their saved config values are ignored while TooManyLags is enabled; the config file stays unchanged. Other FGN settings and incompatible-mod safety blocks remain active.

**If Server-Side Simulation was previously off, enabling it adds CPU workload to the server.**

## Why it helps

Valheim distributes simulation between object owners. A door or creature owned by another player can feel delayed when that player's connection is slow, even if yours is fast.

TooManyLags assigns nearby interactive objects to you, allowing supported interactions to run locally without a network round trip. Where players' fields overlap, the server becomes the owner. If your connection is poor while you are close to other players, you can still feel that delay when using server-owned objects; other players' interactions no longer depend on your client processing those objects.

Hostile Mobs prefer their current player target as owner. A high-ping target can use the server when another eligible player is nearby. The aim is to keep each player's experience responsive and prevent one slow connection from becoming everyone's lag.

This shares client workload more fairly, especially in large bases. Server ownership can increase **server CPU usage**. Server overload and protected ownership transitions can still cause delays; this is not a guarantee of zero lag.

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

Edit `BepInEx/config/botanic.toomanylags.fgnaddon.cfg`; restart after changes. Sections explain each feature briefly.

Use [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) as an optional client-side debugging tool to see object and mob owners, check ownership transfers and tune your configuration.

| Feature | Defaults and options |
|---|---|
| Grid | 4-metre cells, rounded 7x7. Also 8/16 metres and square 3x3/rounded 5x5. 4 metres requires a rounded grid. |
| Direction and delays | Camera direction; inner 0s, forward 0.2s, outer 3s. Full-grid mode uses 2s. Missing camera data uses character heading. |
| Checks | Movement and camera each default to 0.1s. Camera checks support 0.05–5s, allowing up to 20 checks per second. Unchanged sectors create no new field requests or camera writes. Network delivery can add delay. |
| Leaving fields | Keep ownership: on. In directional mode, when only one player remains, red fields transfer automatically; forward/outer fields keep their owner until that player's next cell or heading-sector change. Fully vacated fields retain ownership. Native release still applies. |
| Categories and overrides | 24 object groups; separate static, default-interactive and permanent-refill server ownership. All three overrides default off. |
| Hostile Mobs | AI ownership on; server fallback target ping 200ms, other-player distance 6 metres. Requires FiresGhettoNetworking creature balancing. |

## Requirements and installation

1. Install **BepInEx, FiresGhettoNetworking and the same mod version on the dedicated server and every client**. Copy `TooManyLags.dll` into `BepInEx/plugins/TooManyLags/`.
2. Copy the supplied config into `BepInEx/config/`, or let first startup generate it. Keep `EnableAIOwnership` consistent across server and clients.
3. Restart. **TooManyLags automatically applies all required FiresGhettoNetworking settings at startup; no manual FGN configuration is needed.** These overrides apply to the current session only. Your saved FGN config and other FGN settings remain unchanged.

**PUP FPS is optional.**

The imported PUP readiness check supports the verified PUP FPS 1.0.34 build. Other PUP builds block AI server fallback; target-player ownership still works. Without PUP, native loading readiness is checked instead.
