# Shared static-object policy

Static policy inherited from 1.0.59. No separate world-object switch: the existing `ServerOwnedStaticBuildingsByDefault` and `UseGridForStaticBuildings` settings cover eligible buildings and stationary world roots together. In this side candidate, stationary sources of qualified physical successors move to the interactive `PhysicsObjects` category instead; see `../SIDE-PHYSICS.md`. Ordinary loot does not cause that reclassification. Existing static defaults and saved values are unchanged.

## Confirmed exclusion

The supplied wall diagnostics show a loaded server instance, ready area and `baked=False`, but `initialEligible=False` and no controller node. The former filter allowed only Piece, WearNTear and ZNetView directly on the root. It excluded even native woodwall's SimpleMeshCombine child scripts. The local native prefab audit also shows standing Beech1/FirTree roots with `m_distant=true`, and PineTree/rock4_copper with visual scripts. These are structural exclusions, independent of loading time.

## Changed scope

- Fixed WearNTear roots may contain additional visual/mod scripts. Piece is not required: the native decorative `vines` has fixed WearNTear and a RandomPieceRotation child, but no Piece. The same classification works whether PlantEverything adds build-menu components on a client or is absent on the server. Harvestable vines retain the Pickables category.
- Native MineRock, MineRock5 and permanent Destructible roots without qualified physical successors can use the same static defaults/grid without a Piece or WearNTear. TreeBase roots with physical logs use PhysicsObjects instead.
- Fixed distant roots are allowed through this static classification or the separate stationary PhysicsObjects-source classification; existing active-area, position, recipient and readiness checks remain. Ordinary distant interactions and creatures are not enabled.
- Unowned distant scenery beyond every active simulation area creates no repeated object job. It stays indexed for future fields; the ordinary native first-owner event also wakes it when the area becomes active.
- Rigidbody hierarchies, TreeLog, ZSyncTransform, creatures/AI, interactions, inventories, fixed/ambient spawners, timed destructibles and foreign child network roots remain outside this policy. Mob spawners retain their separate mob-default policy. PUP-baked objects retain their existing exclusions.

The existing pre-construction event seeds only eligible **unowned** static roots with the server default. It does not steal a nonzero owner and respects already-settled player fields. Native owner assignment and existing guarded takeovers handle the other cases. No terrain generation, collision creation, support calculation or destruction method is replaced.

## Native state and compatibility

TreeBase, MineRock and Destructible damage read health from the ZDO. MineRock5 reloads and saves its per-area health during DamageArea. Its visual broadcasts, and native tree growth/shake broadcasts, remain native. MineRock's owner request is named `Hit`; it joins the existing static request router, while `Hide` remains a broadcast. FiresGhettoNetworking's dedicated no-profile guard for Game.IncrementPlayerStat remains in place.

Update clients and server for the expanded classification, early destination proofs and rock-hit routing. The static path adds no protocol packet or mandatory third-party dependency; the separate PhysicsObjects extension adds a capability-gated source sleep check. With the static options off, static ownership is not selected by this feature. Server-Side Simulation remains required; player-only simulation is unchanged.

## Cost and validation scope

Uses existing prefab caches, controller registration, native assignment hook, pre-construction event, field jobs and unload/reset cleanup. No new periodic scan, timer, physics callback or RPC. Larger enabled scope naturally manages more objects; enabling the static grid can therefore create additional real ownership transfers.

Regression fixtures cover visual child scripts, all four world roots, native distant trees, defaults on/off, grid on/off, exit retention/return, unowned construction, settled-player priority, active owners, PUP exclusions, unsafe roots, client proof and stale native requests. Native IL checks verify damage-owner gates, serialized health and MineRock request/broadcast registration. Automated checks do not replace live Unity, physics or multiplayer testing.
