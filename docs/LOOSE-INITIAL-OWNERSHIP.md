# Loose-item initial ownership

With Server-Side Simulation, LooseItems=true and ServerOwnedInteractivesByDefault=true, the existing ReleaseNearbyZDOS owner selector now includes persistent, non-distant ItemDrop templates. Eggs use this category too; the mob default does not control them. No prefab names are matched.

Vanilla still decides when an unowned object, or an object whose former owner no longer loads its area, needs an owner. Only the target of that existing write changes: server outside a settled player field, the available settled player inside one, and server for overlaps or pending field timers. Existing active owners are not stolen. Vanilla region release and native pickup ownership requests remain unchanged.

The existing per-prefab cache is reused. There is no additional hook, polling loop, RPC, timer or per-drop initial loading record. Dynamic drops remain excluded from the stationary pre-instance protocol; their normal loaded-node registration, physics readiness, stacking and pickup safeguards continue unchanged. Native initial assignment does not activate a Rigidbody or alter native creation/type ordering.

This is not a client spawn interception: a newly dropped or laid item created by a client still originates with that creating session, as native CreateNewZDO initializes its owner there. It subsequently follows the existing grid and native owner-departure rules. A still-active creator or pickup owner is not overridden by an initial-default repair.

The focused production-transpiler/controller fixtures cover ordinary items and both egg names, pending and settled fields, overlaps, active/departed owners, category/default opt-outs, native release, non-stationary classification, cache reuse and absence of pre-instance jobs. Automated network/Unity doubles are not live physics or multiplayer testing. This fix requires only a server update from 1.0.59; no client protocol changes are introduced.
