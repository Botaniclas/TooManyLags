# Configuration

These settings describe TooManyLags 1.0.44. Earlier releases can use different config sections or protection periods.

## FiresGhettoNetworking and simulation mode

TooManyLags applies required FiresGhettoNetworking settings at startup, including enabling **Server-Side Simulation** on the dedicated server by default. Their saved config values are ignored while TooManyLags is enabled; the config file stays unchanged. Other FiresGhettoNetworking settings and incompatible-mod safety blocks remain active.

**Quick start:** Install both mods on the dedicated server and every client, then play. FiresGhettoNetworking's default settings and Auto-Tune, combined with TooManyLags' defaults and required overrides, are intended to provide an optimal starting setup for almost all dedicated servers without manual configuration. You can still fine-tune all other FiresGhettoNetworking settings yourself; its config is worth a look.

**Server-Side Simulation adds CPU workload to the server.** For a weak or already overloaded server CPU, set `NoServerSideSimulation=true`. Objects and Hostile Mobs then stay player-owned; shared fields prefer the better-connected player. Server-owned buildings, default ownership and permanent station ownership are unavailable. Inventory, refilling and active-use safeguards remain; all clients need the mod for full handoffs. Shared interactions can still depend on the selected player's latency.

## Server configuration

Configure **all gameplay settings on the server** in `BepInEx/config/botanic.toomanylags.fgnaddon.cfg`, including `Enabled`, `EnableAIOwnership` and camera settings. Clients follow the server when connecting; their local copies are ignored. Only `EnableLogs` is local. Config descriptions state their scope; object categories share one note at the bottom.

Start with **Simulation Settings** (interactive, then static objects) and **General**. Other sections have tuned defaults; adjust only when needed.

Simulation settings and `KeepOwnershipOnCellExit` only distribute CPU load between server and clients. They do not affect perceived latency.

**Console:** `tml_config list` lists settings; `tml_config <Key>` reads one; `tml_config <Key> <Value>` saves a change. Only admins can change server settings.

Intervals, delays and mob fallback limits apply immediately. Other settings, including `Enabled` and `EnableAIOwnership`, take effect after a server restart; clients receive the new mode when reconnecting.

Use [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) as an optional client-side debugging tool to see object and mob owners, check ownership transfers and tune your configuration.

**Grid settings:** Defaults should work well. Larger grids are recommended for poor connections, giving handoffs more lead time and covering more objects.

| Feature | Defaults and options |
|---|---|
| Simulation mode | `NoServerSideSimulation=false`: full Server-Side Simulation. Set true for weak server CPUs; transfers stay between players. |
| Interactive defaults | `ServerOwnedInteractivesByDefault=false`: vanilla default; true selects a server default outside player fields. |
| Static defaults/grid | `ServerOwnedStaticBuildingsByDefault=false`: vanilla default; true selects the server. `UseGridForStaticBuildings=false`: optional nearby client sharing. Both require Server-Side Simulation. |
| Grid | 4-metre cells, rounded 7x7. Also 6.4/8/16 metres and square 3x3/rounded 5x5. 6.4 metres divides each vanilla zone into 10x10 cells. 4 metres requires a rounded grid. |
| Direction and delays | Camera direction; overlap 0–45°, default 30 (`CameraDirectionOverlapDegrees`, server-controlled, live console supported). Inner 0s, forward 0.2s, outer 3s. Full-grid mode uses 2s. Missing camera data uses character heading. |
| Server overlap cooldown | `ServerOverlapCooldownSeconds=1`: after an overlapping field switches to the server, keep it there and ignore new grid ownership decisions for 1s. Then the next movement/heading update can request it again; expiry alone causes no transfer. 0 disables. Requires Server-Side Simulation. |
| Handoff protection | Always enabled with Server-Side Simulation; no config switch. A saved `EnableExperimentalInteractionProtection=false` is ignored. Inventory, refill and active-use safeguards remain active. |
| Checks | Movement and camera each default to 0.05s; both support 0.05–5s. Changed camera coverage gets send priority and wakes the next server pass, without another interval wait. Network delivery and protected handoffs can still add delay. |
| Leaving fields | `KeepOwnershipOnCellExit=true`: retain vacated fields, gradually sharing simulation. false returns to server defaults and requires Server-Side Simulation plus at least one interactive/static server default. Native region release still applies. |
| Categories and overrides | 26 interactive/mob groups at the bottom; optional default-mob and permanent-refill server ownership. Overrides default off and require Server-Side Simulation. |
| Hostile Mobs | AI ownership on; server fallback target ping 200ms, other-player distance 6 metres. Without Server-Side Simulation, fallback uses a lower-latency nearby player. |

## Updating from older versions

Use the supplied new config and reapply custom values. Interactive/static defaults move to Simulation Settings; exit retention moves to General. `ServerOwnedStaticBuildings` is renamed `ServerOwnedStaticBuildingsByDefault`. Update server and clients for the optional static grid.
