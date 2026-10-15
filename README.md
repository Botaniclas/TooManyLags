# TooManyLags - FiresGhettoNetworking Addon

A FiresGhettoNetworking extension designed to make nearby interactions feel almost latency-free: locally owned objects are processed directly on your client, without waiting for another player's connection. In shared areas, the server takes over so each player primarily feels their own latency instead of someone else's lag. Includes ownership improvements for Hostile Mobs.

## Forced FiresGhettoNetworking settings

TooManyLags applies required FiresGhettoNetworking settings at startup, including enabling **Server-Side Simulation** on the dedicated server by default. Their saved config values are ignored while TooManyLags is enabled; the config file stays unchanged. Other FiresGhettoNetworking settings and incompatible-mod safety blocks remain active.

**Quick start:** Install both mods on the dedicated server and every client, then play. FiresGhettoNetworking's default settings and Auto-Tune, combined with TooManyLags' defaults and required overrides, are intended to provide an optimal starting setup for almost all dedicated servers without manual configuration. You can still fine-tune all other FiresGhettoNetworking settings yourself; its config is worth a look.

**Server-Side Simulation adds CPU workload to the server.** For a weak or already overloaded server CPU, set `NoServerSideSimulation=true`. Objects and Hostile Mobs then stay player-owned; shared fields prefer the better-connected player. Server-owned buildings, default ownership and permanent station ownership are unavailable. Inventory, refilling and active-use safeguards remain; all clients need the mod for full handoffs. Shared interactions can still depend on the selected player's latency.

| Your setup | Recommended edition and mode |
|---|---|
| Dedicated server, strong CPU | This addon with **Server-Side Simulation** (default). **Most-tested mode.** |
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

- With `ServerOwnedInteractivesByDefault=true`, eligible new or abandoned stationary interactives receive their initial server owner during vanilla assignment, including while grid timers are pending. Settled grid fields take priority; permanent refill ownership stays server-owned. Active owners keep the existing guarded handoff path; vehicles and animals keep their existing loading safeguards.
- A camera-directed grid covers nearby interactive objects and idle tamed animals. One player's fields go to that player; overlapping fields go to the server. Stationary players still count. KeepOwnershipOnCellExit=true keeps the rear cutout; false covers the full shape with a red 3x3 centre and the orange row ahead.
- `CameraDirectionOverlapDegrees` sets base angular overlap between adjacent directions: **30° by default**, adjustable from **0–45°** on the server. At 0°, one base direction is active; at 45°, two. Clients receive the server's angle; character-heading fallback uses it too. Changed coverage triggers the next server pass immediately; unchanged coverage does no field work. Isolated players use vanilla ownership by default.
- `CameraRotationPredictionSeconds` prepares fields in your turning direction **0.1 seconds ahead** by default; **0 disables**, range 0–0.5. Faster turns can add one direction, up to three in total, while keeping your current coverage. Newly added fields use the **inner/red delay** (default **0 seconds**); existing fields keep their delays. Brief reversals are damped; usage protection stays active. Set it on the server; updated clients receive it automatically.
- Hostile Mobs use AI target ownership with attack protection, consent, retries and safe server fallback. Outdated handoffs retry the latest target without a six-second lock; attacks finish before the target is checked again. Update server and clients together. Hostile summons follow the same rules. Friendly magic summons, including skeletons, spirit animals and roots, stay outside both the grid and mob ownership policy. Ordinary tamed animals still use the grid; active controls retain their safeguards.
- In Server-Side Simulation mode, `BossAlwaysServerOwned` (on): at a boss player-target change, two living players within `BossPlayerRadius` (30 metres) select server ownership. Otherwise bosses use normal Hostile Mob rules. Turning it off always uses those normal rules. Requires AI ownership; server ownership requires Server-Side Simulation. Radius decisions wait for the next target change; movement alone does not rescan. Attacks finish before transfers. With `NoServerSideSimulation=true`, bosses retain the existing vanilla exclusion.
- Optional `ServerOwnedMobsByDefault` (off) gives idle tamed animals a server default outside player fields and puts Hostile Mobs without a player target on the server, even with one player online. Loading and usage safeguards still apply; field-exit retention stays unchanged. Hostile Mobs require `EnableAIOwnership`. Friendly summons and bosses remain excluded. This can increase server CPU usage.
- Incinerators follow the grid with updated, guarded clients. Burning/effects and open inventories reject automatic transfers; completion alone never replays them. A fresh covering player movement/heading update can request ownership again. Older clients keep native incinerator ownership. The permanent-refill option does not pin incinerators. Local PUP collision/stability guards remain a separate feature.
- With `ServerOwnedInteractivesByDefault=true`, existing persistent network objects for dungeon entrances and Vegvisir receive a server default, independently of the grid (`WorldMarkers`). Local entrances/stones without a ZDO have no assignable owner; their interactions remain unchanged.
- Fixed doors/gates with animated kinematic child bodies support early server defaults and guarded player handoffs. Moving roots and dynamic/synchronized physics remain excluded from this exception.
- Open inventories, active users and refilling remain protected.
- With server-default ownership enabled, managed stationary interactions (including harvestables, torches, doors and public chests) can enter your grid before their server instance loads. This includes already server-owned objects and loaded neighbours across native zone boundaries. Clients confirm the actual object, its terrain zone and their ready local area; field delays and active-use safeguards still apply. Update server and clients to 1.0.41 or later for the complete startup fix. Vehicles and animals keep their physics-loading safeguards.
- If overlapping fields request an unready server, the current available player keeps ownership. Loading completion alone does not replay that takeover; a fresh covering movement/heading update can request it again.
- Updated clients can confirm local loading for server-owned stationary objects when server-area loading is incomplete. Older clients retain the original loading check.
- Loading retries stop requesting object data once the client confirms its replica. Missing startup data/instances receive an end-of-frame recheck; successful replies wake the handoff immediately. Configured field delays and all use safeguards remain active.

## Ownership grid in action

These examples use [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) to show ownership: white objects belong to the server; purple objects belong to the player in these recordings. Movement and camera-sector changes update nearby grid cells using their configured delays.

**Ownership from above**

https://github.com/user-attachments/assets/4a445578-1edf-4abb-8eea-f28183878d3c

**Ownership while moving**

https://github.com/user-attachments/assets/8555a985-7696-4cc6-865a-5115c3695f72

## Configuration

Configure **all gameplay settings on the server** in `BepInEx/config/botanic.toomanylags.fgnaddon.cfg`, including `Enabled`, `EnableAIOwnership` and camera settings. Clients follow the server when connecting; their local copies are ignored. Only `EnableLogs` is local. Config descriptions state their scope; object categories share one note at the bottom.

**Console:** `tml_config list` lists settings; `tml_config <Key>` reads one; `tml_config <Key> <Value>` saves a change. Only admins can change server settings.

**Diagnostics:** With Server-Side Simulation, admins can use `tml_config owner_status` for a one-time ownership/loading snapshot in the console and server's `BepInEx/LogOutput.log`, even with `EnableLogs=false`. Add part of a prefab name, e.g. `tml_config owner_status torch`, to inspect that object type. Shows missing instances and native creation/zone readiness. No background checks; existing clients support the command.

Intervals, delays and mob fallback limits apply immediately. Other settings, including `Enabled` and `EnableAIOwnership`, take effect after a server restart; clients receive the new mode when reconnecting.

Use [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) as an optional client-side debugging tool to see object and mob owners, check ownership transfers and tune your configuration.

| Feature | Defaults and options |
|---|---|
| Simulation mode | `NoServerSideSimulation=false`: full Server-Side Simulation. Set true for weak server CPUs; transfers stay between players. |
| Grid | 4-metre cells, rounded 7x7. Also 6.4/8/16 metres and square 3x3/rounded 5x5. 6.4 metres divides each vanilla zone into 10x10 cells. 4 metres requires a rounded grid. |
| Direction and delays | Camera direction; overlap 0–45°, default 30 (`CameraDirectionOverlapDegrees`, server-controlled, live console supported). Inner 0s, forward 0.2s, outer 3s. Full-grid mode uses 2s. Missing camera data uses character heading. |
| Server overlap cooldown | `ServerOverlapCooldownSeconds=1`: after an overlapping field switches to the server, keep it there and ignore new grid ownership decisions for 1s. Then the next movement/heading update can request it again; expiry alone causes no transfer. 0 disables. Requires Server-Side Simulation. |
| Handoff protection | Always enabled with Server-Side Simulation; no config switch. A saved `EnableExperimentalInteractionProtection=false` is ignored. Inventory, refill and active-use safeguards remain active. |
| Checks | Movement and camera each default to 0.05s; both support 0.05–5s. Changed camera coverage gets send priority and wakes the next server pass, without another interval wait. Network delivery and protected handoffs can still add delay. |
| Leaving fields | Keep ownership: on. In directional mode, when only one player remains, red fields transfer automatically; forward/outer fields keep their owner until that player's next cell or heading-sector change. Fully vacated fields retain ownership. Native release still applies. |
| Categories and overrides | 26 object groups; separate static, default-interactive, default-mob and permanent-refill server ownership. These overrides default off and are ignored with NoServerSideSimulation=true. |
| Hostile Mobs | AI ownership on; server fallback target ping 200ms, other-player distance 6 metres. Without Server-Side Simulation, fallback uses a lower-latency nearby player. |

## Requirements and installation

1. Install **BepInEx, FiresGhettoNetworking and the same mod version on the dedicated server and every client**. Copy `TooManyLags.dll` into `BepInEx/plugins/TooManyLags/`.
   Update clients together with the server. Clients without handoff support keep native interactive ownership.
2. Configure the server using the supplied config in `BepInEx/config/`, or let first startup generate it. Client gameplay settings need no adjustment.
3. Restart. **TooManyLags automatically applies all required FiresGhettoNetworking settings at startup; no manual FiresGhettoNetworking configuration is needed.** These overrides apply to the current session only. Your saved FiresGhettoNetworking config and other FiresGhettoNetworking settings remain unchanged.

**Updating:** Object categories now use `10 - Object Categories`. Use the supplied config and reapply your custom values. Existing configs retain their saved check intervals; set both to `0.05` to use the new defaults. Update all clients too.

**PUP FPS is optional.**

Optional PUP collision readiness is checked through its supported interfaces, without a version lock. PUP FPS 1.0.34 and 1.0.36 have been verified; unfinished collision replacements block AI server takeover. An incompatible interface disables that takeover safely. Without PUP, native loading readiness is checked instead.

**BotanicPupPatch is recommended when combining PUP FPS with FiresGhettoNetworking.** It adds compatibility safeguards for cluster collisions and building stability during loading and ownership changes. Install it on the server and all clients; it is optional for TooManyLags.
