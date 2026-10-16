namespace TooManyLagsStandalone
{
    internal static class ConfigTemplate
    {
        internal const string Text = @"## Settings file was created by plugin TooManyLags - Standalone v@VERSION@
## Plugin GUID: botanic.toomanylags.standalone

[00 - General]

## Host/server settings control gameplay, camera and mobs. Only EnableLogs is local. Clients are optional with reduced features. Restart unless marked live.
## Enable TooManyLags for this session. Clients follow the host/server.
Enabled = true


[01 - Simulation Settings]

## Simulation stays on players; there is no server simulation mode.
## true: retain vacated fields. Only inner fields update automatically when one of several players leaves; outer fields wait for movement/rotation.
## false: keep the full directional footprint with a red 3x3 centre. Native region ownership release remains active in both modes.
KeepOwnershipOnCellExit = true


[02 - Latency Selection]

## Host/server; restart required. Smoothed ping sampling interval in seconds. Unmodded clients use native connection telemetry without an extra request.
# Acceptable value range: From 0.5 to 10
PingMeasurementIntervalSeconds = 2

## Host/server; restart required. Required ping improvement in milliseconds before replacing a candidate; avoids jitter-driven switches.
# Acceptable value range: From 0 to 500
OwnershipPingMarginMs = 30


[03 - AI Mob Ownership]

## Host/server; restart required. Target-based Hostile Mob ownership requires compatible source/target clients. Friendly player summons and bosses stay vanilla; idle tamed animals use the grid.
EnableAIOwnership = true

## Live console supported. Above this target ping in milliseconds, prefer a better-connected eligible player close to the Hostile Mob. Does not change its attack target.
# Acceptable value range: From 50 to 2000
HighTargetPingMs = 200

## Live console supported. Maximum distance in metres from the mob for a better-ping alternative owner.
# Acceptable value range: From 1 to 20
NearbyAlternativeOwnerRadiusMeters = 6


[04 - Grid Settings]
## Defaults should work well. Larger grids prepare ownership earlier for poor connections.

## Cell size in metres: 4, 6.4, 8 or 16. 6.4 divides each vanilla zone into 10x10 cells. 4 requires a rounded grid. Does not change loading distances.
# Acceptable values: 4, 6.4, 8, 16
GridCellSizeMeters = 4

## Square3x3 is square; Rounded5x5/Rounded7x7 are rounded. Directional mode omits rear fields with KeepOwnershipOnCellExit=true; false keeps the complete shape. Overlaps prefer the best eligible player ping.
# Acceptable values: Square3x3, Rounded5x5, Rounded7x7
GridCoveragePattern = Rounded7x7

## Live console supported. Movement/fallback-heading check interval in seconds. Only changed coverage creates requests; received camera changes wake the next server pass directly. Existing handoffs progress while standing.
# Acceptable value range: From 0.05 to 5
MovementRotationCheckIntervalSeconds = 0.1


[05 - Directional Grid Mode]

## Recommended: leave enabled. Camera-directed grid with the timers below and camera settings in section 07. Missing camera data uses character heading. false uses the full grid from section 06. Stationary players still count.
DirectionalGridMode = true

## Live console supported. Red: six inner cells; full 3x3 with KeepOwnershipOnCellExit=false. Delay in seconds; 0 allowed. Safe handoffs can still wait.
# Acceptable value range: From 0 to 30
InnerTransitionDelaySeconds = 0

## Live console supported. Orange: one extra three-cell row ahead of red, with 4-metre cells and rounded coverage. Delay in seconds; 0 allowed.
# Acceptable value range: From 0 to 30
ForwardTransitionDelaySeconds = 0.2

## Live console supported. Yellow: remaining active cells. Delay in seconds. Avoids unnecessary edge handoffs; lower delays can help fast movement on poor connections.
# Acceptable value range: From 0 to 30
OuterTransitionDelaySeconds = 3

## Live console supported. Hold an overlap ownership decision for this many seconds. Exclusive fields have no hold; expiry alone causes no transfer. 0 disables.
# Acceptable value range: From 0 to 30
OverlapOwnershipCooldownSeconds = 1


[06 - Full Grid Mode]

## Live console supported. Only with DirectionalGridMode=false: uniform delay in seconds for new full-grid requests.
# Acceptable value range: From 0 to 30
TransitionDelaySeconds = 2


[07 - Camera Heading]

## Live console supported; sent to clients. Camera sample interval in seconds. 0.05 allows up to 20 checks per second; only changed coverage is published. Without a client mod, character heading is used.
# Acceptable value range: From 0.05 to 5
CameraHeadingCheckIntervalSeconds = 0.05

## Live console supported; sent to clients. Angular overlap between adjacent directions. 30 is recommended; 0 disables base overlap. Prediction can add one further direction.
# Acceptable value range: From 0 to 45
CameraDirectionOverlapDegrees = 30

## Live console supported; sent to clients. Predict camera rotation this many seconds ahead. At most one extra direction uses the inner/red delay. 0 disables. Requires a compatible client.
# Acceptable value range: From 0 to 0.5
CameraRotationPredictionSeconds = 0.1


[09 - Logs]

## Local only; live console supported. Extra diagnostics. Startup errors are always reported.
EnableLogs = false


[10 - Object Categories]

## All categories below: host/server; restart required. true: managed by TooManyLags; false: vanilla. Active use and safe handoffs remain protected. Without compatible clients, only native-safe categories transfer.

## Doors and gates.
Doors = true

## Seats and barber stations.
Seats = true

## Beds.
Beds = true

## Crafting stations and upgrades.
Workstations = true

## Smelters, kilns, windmills and similar refill stations. Each observed insert renews a one-second hold.
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

## Idle tamed animals; riding, following and friendly player summons retain native control.
TamedAnimals = true

## Chests and inventories; open inventories and pending requests block transfers.
Containers = true

## Item and armour stands.
ItemStands = true

## Signs; native editing retains ownership until player movement.
Signs = true

## Portals and dungeon entrances; native portal-name editing retains ownership until movement.
Portals = true

## Fermenters.
Fermenters = true

## Incinerators; open inventories, pending requests and burning block transfers.
Incinerators = true

## Traps and turrets.
Defenses = true

## Shield generators.
ShieldGenerators = true

## Pickable objects and feasts; excludes loose item drops.
Pickables = true

## Offering altars.
Offerings = true

## Carts, boats and vehicle controls; active drivers retain control.
Vehicles = true

## Other compatible modded interactables; custom local actions cannot be inferred.
OtherInteractables = true";
    }
}
