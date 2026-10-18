namespace TooManyLags
{
    internal static class ConfigTemplate
    {
        internal const string Text = @"## Settings file was created by plugin TooManyLags - FiresGhettoNetworking Addon v@VERSION@
## Plugin GUID: botanic.toomanylags.fgnaddon


[00 - General]

## Server only; restart required. true (default): automatically apply the required FiresGhettoNetworking session settings and start/stop its Server-Side Simulation for an easy quick start.
## false: configure FiresGhettoNetworking on the server and clients yourself for the TML features you use. TML ownership patches and handoff safeguards remain active. Clients follow this server setting.
AutoConfigureFiresGhettoNetworking = true


## Enable TooManyLags for this server session. Clients follow the server; their local value is ignored.
Enabled = true


[01 - Simulation Settings]
##Simulation settings and exit retention only distribute CPU load between server and clients. They do not affect perceived latency
## Server only; restart required. Server defaults and static simulation require Server-Side Simulation.


## Recommended for dedicated servers with a weak or already overloaded CPU. true: disable FiresGhettoNetworking Server-Side Simulation and keep simulation on players; shared fields prefer the player with lower latency. Functionality is limited: no server ownership, and server-owned building/default/refill options are ignored. false: Server-Side Simulation mode.
NoServerSideSimulation = false


## true: server default for managed interactions/idle tamed animals, shifts some CPU load from the client to the server; false: vanilla default.
ServerOwnedInteractivesByDefault = false


## true: server default for eligible non-interactive building pieces, shifts some CPU load from the client to the server;
## false: vanilla default. UseGridForStaticBuildings lets nearby clients share their simulation.
ServerOwnedStaticBuildingsByDefault = false


## true: Include eligible static building pieces in the existing grid; requires ServerOwnedStaticBuildingsByDefault=true. Overlaps use the server; empty fields follow KeepOwnershipOnCellExit.
UseGridForStaticBuildings = false


## true: keep vacated fields, gradually sharing CPU load between clients/server.
## false: exit cells return to server defaults; requires Server-Side Simulation and an interactive/static server default. false also keeps the full directional footprint. Native region release still applies.
KeepOwnershipOnCellExit = true



[02 - Default Ownership and Overrides]

## Keep enabled refill stations server-owned, even inside player fields: smelters, cooking/fire stations, fermenters, shield generators and turrets. false: grid rules with 1 second of protection, renewed by each observed refill. Ignored with NoServerSideSimulation=true.
ServerOwnedRefillStations = false


## Give idle tamed animals a server default outside player fields, even when the interactive default is off. Hostile Mobs without a player target use guarded server handoffs (requires EnableAIOwnership); player targets keep normal target ownership. Friendly magic summons, bosses and active riding/following stay excluded. Requires Server-Side Simulation; can increase server CPU usage.
ServerOwnedMobsByDefault = false



[03 - AI Mob Ownership]

##  Enable target-based Hostile Mob ownership. Clients follow the server; their local value is ignored. Attacks block transfers until attack end.
EnableAIOwnership = true


## Server only; restart required. On a boss player-target change, use server ownership if at least two living players are within BossPlayerRadius. Otherwise bosses follow normal Hostile Mob target rules. Requires EnableAIOwnership and Server-Side Simulation for server ownership.
BossAlwaysServerOwned = true


## Player distance from the boss in metres for BossAlwaysServerOwned. Evaluated on player-target acquisition, change or loss, not movement.
# Default value: 30
# Acceptable value range: From 1 to 100
BossPlayerRadius = 30


## live console supported. Target ping limit in milliseconds for server fallback with Server-Side Simulation, or a lower-latency nearby player with NoServerSideSimulation=true. Requires another eligible FiresGhettoNetworking player within ServerFallbackRadius. 0: target ping must exceed the best other candidate by 250 ms.
# Default value: 200
# Acceptable value range: From 0 to 2000
ServerFallbackPingMs = 200


## Server only; live console supported. Distance in metres to another eligible FiresGhettoNetworking player for high-ping fallback. With Server-Side Simulation the server takes over; otherwise a lower-latency nearby player. The candidate must have the mob in its active area.
# Default value: 6
# Acceptable value range: From 1 to 100
ServerFallbackRadius = 6



[04 - Grid Settings]
##Defaults should work well.

## Larger grids are recommended for poor connections: they give handoffs more lead time, covering more objects. Cell size in metres: 4, 6.4, 8 or 16; 4 requires a rounded grid. Does not change vanilla/PUP loading distances.
# Default value: 4
# Acceptable values: 4, 6.4, 8, 16
GridCellSizeMeters = 4


## Base shape: Square3x3 is square; Rounded5x5/Rounded7x7 are rounded. With KeepOwnershipOnCellExit=true, directional mode omits central and far rear fields; false keeps the complete shape. Overlaps use the server, or a lower-latency player with NoServerSideSimulation=true.
# Default value: Rounded7x7
# Acceptable values: Rounded5x5, Square3x3, Rounded7x7
GridCoveragePattern = Rounded7x7


## live console supported. Check interval in seconds for movement and fallback heading. Received camera-coverage changes request an immediate pass. Only changed coverage requests fields; existing timers/handoffs also progress while standing.
# Default value: 0.1
# Acceptable value range: From 0.05 to 5
MovementRotationCheckIntervalSeconds = 0.1



[05 - Directional Grid Mode]

## true: camera-directed grid with the timers below and the overlap angle in section 07. false: full grid from section 06. Missing camera data uses character heading. Stationary players still count.
DirectionalGridMode = true


## live console supported. Red: six inner fields with KeepOwnershipOnCellExit=true; a full 3x3 centre with false. Delay in seconds for new requests; 0 allowed. Handoff protection remains active.
# Default value: 0
# Acceptable value range: From 0 to 30
InnerTransitionDelaySeconds = 0


## live console supported. Orange: one extra three-cell row ahead of red. Active only with 4-metre cells and Rounded5x5/Rounded7x7. Delay in seconds for new requests; 0 allowed. The farthest 7x7 row stays yellow.
# Default value: 0.2
# Acceptable value range: From 0 to 30
ForwardTransitionDelaySeconds = 0.2


## live console supported. Yellow: remaining active fields. Delay in seconds for new requests. With KeepOwnershipOnCellExit=true, white rear fields make no request; false covers the full shape. Overlaps use the earliest still-valid request deadline.
# Default value: 3
# Acceptable value range: From 0 to 30
OuterTransitionDelaySeconds = 3


## live console supported. After a directional-grid overlap assigns a field to the server, ignore new grid ownership decisions for this many seconds. Expiry alone causes no transfer; the next movement/heading update can request the field again. 0 disables. Empty fields/native release and usage protection remain unchanged. Ignored without Server-Side Simulation. New cooldowns use the updated value.
# Default value: 1
# Acceptable value range: From 0 to 30
ServerOverlapCooldownSeconds = 1



[06 - Full Grid Mode]

## live console supported. Only with DirectionalGridMode=false: one delay in seconds for new full-grid requests. Active use and safe handoffs may require more time.
# Default value: 2
# Acceptable value range: From 1 to 30
TransitionDelaySeconds = 2



[07 - Camera Heading]

## live console supported. Camera check interval in seconds, sent to all clients. Client value is ignored. 0.05 allows up to 20 checks per second; only coverage changes are sent. Server grid timing is in section 04.
# Default value: 0.05
# Acceptable value range: From 0.05 to 5
CameraHeadingCheckIntervalSeconds = 0.05


## live console supported. Base angular overlap of adjacent directions, sent to clients. 0: one base direction; 30: default; 45: two. Rotation prediction can add one direction. Character-heading fallback uses the same angle.
# Default value: 30
# Acceptable value range: From 0 to 45
CameraDirectionOverlapDegrees = 30


## live console supported. Predict camera rotation this many seconds ahead. Adds at most one adjacent direction; new fields use the inner/red delay. Keeps current coverage and handoff protection. 0 disables. Requires updated clients; character fallback is unchanged.
# Default value: 0.1
# Acceptable value range: From 0 to 0.5
CameraRotationPredictionSeconds = 0.1



[08 - PUP FPS Compatibility]

## Local to this server/client; restart required. Built-in PUP FPS collision, building-stability and FiresGhettoNetworking handoff safeguards. Automatically inactive without the required mods or when a separate BotanicPupPatch supplies the guards. Inactive mode installs no guard hooks and does no runtime work. AI ownership remains controlled by section 03.
EnablePupCompatibility = true



[09 - Logs]

## Local to this server/client. Live console: tml_config EnableLogs true/false. Enable diagnostic messages and status commands. Off skips diagnostic work; startup failures are always reported.
EnableLogs = false



[10 - Object Categories]

## All categories below: server only; restart required. true: managed by TooManyLags; false: vanilla/FiresGhettoNetworking. Open inventories, refilling, riding, following and other active use remain protected.

## Doors and gates.
Doors = true

## Seats and barber stations.
Seats = true

## Beds.
Beds = true

## Crafting stations and upgrades.
Workstations = true

## Smelters, kilns, windmills and similar stations.
Smelters = true

## Cooking stations.
CookingStations = true

## Fireplaces and refillable light sources.
Fireplaces = true

## Beehives.
Beehives = true

## Sap collectors.
SapCollectors = true

## Wards.
Wards = true

## Cartography tables.
MapTables = true

## Wisp fountains.
Wisplights = true

## Idle tamed animals; friendly magic summons excluded.
TamedAnimals = true

## Public chests and inventories.
Containers = true

## Item and armour stands.
ItemStands = true

## Signs.
Signs = true

## Portals.
Portals = true

## Fermenters.
Fermenters = true

## Incinerators; guarded clients required. Burning/open inventories discard automatic transfers; new covering player activity retries.
Incinerators = true

## Traps and turrets.
Defenses = true

## Shield generators.
ShieldGenerators = true

## Pickable objects and feasts; excludes loose item drops.
Pickables = true

## Loose item drops. Leave enabled; disabling is normally not useful. false keeps native ownership. Native pickup takes priority over automatic grid transfers.
LooseItems = true

## Offering altars.
Offerings = true

## Idle carts and vehicle controls; FiresGhettoNetworking-simulated ships excluded.
Vehicles = true

## Other compatible modded interactables.
OtherInteractables = true

## Existing network roots of dungeon entrances and Vegvisir; server default only, local objects without a ZDO have no owner.
WorldMarkers = true";
    }
}
