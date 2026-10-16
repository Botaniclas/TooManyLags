# Compatibility

## Installation and optional clients

Only **BepInExPack Valheim** is required. Install Standalone on the dedicated server or the player hosting a local multiplayer world. Install the same version on clients for camera direction and full supported handoffs. Players without it can still join.

The host/server cannot see every local inventory, action or creature target. Standalone therefore retains a narrower native fallback instead of assuming those actions are safe.

| Feature | Without the client mod | With compatible modded owner clients |
|---|---|---|
| Directional grid | Replicated character rotation | Camera direction, overlap and turn prediction |
| Player latency selection | Native transport ping | Smoothed connection measurements |
| Standard doors, seats, beds, workstations and wards | Restricted fixed-object fallback | Guarded ownership handoffs |
| Signs and portals | Vanilla editing and ownership if either endpoint lacks the text guard | Ownership confirmed before native editing; edited object protected until movement |
| Inventories and incinerators | Vanilla ownership if either handoff endpoint lacks the mod | Inventory/opening and burning checks |
| Refill stations | Vanilla ownership if either endpoint lacks the mod | Renewing 1-second refill protection |
| Vehicles and idle tamed animals | Vanilla ownership if either endpoint lacks the mod | Active drivers, riders, following and relevant combat protected |
| Hostile Mob target ownership | Vanilla ownership if either endpoint lacks the mod | Target owner, with nearby lower-ping fallback |

“Both endpoints” means the old and new owner. An incompatible older modded client also uses the restricted fallback until updated. A local listen-host can be an owner; a dedicated server coordinates decisions without taking over simulation.

## Supported interactions

The mod groups supported native interaction components into broad configurable categories. Vanilla access checks remain active. Disabled categories retain vanilla handling. Friendly magic summons, bosses, static walls/floors and loose item drops are excluded from ownership management.

Transfers between modded owners preserve active-use checks and require the receiving object and area to be loaded. Open/opening inventories block transfers; refilling renews its short guard. Incinerators additionally stay with their owner while burning. Ordinary supported late owner requests retain their original sender and payload when routed to a new owner.

Loaded stationary objects use the receiving client's own loaded footprint, including neighbouring zones. Dynamic objects, animals and private inventories retain their stricter object-centred area checks; the host's loading state never substitutes for the receiver's.

Native sign/portal text protection works on dedicated servers and local hosts. It protects only the edited object, including after text confirmation or closing the editor, until actual player movement. Waiting automatic grid requests are discarded; release alone does not replay them. Teleport, death, disconnect, object unloading and world shutdown clean up the lock. Incomplete openings time out without opening a delayed editor later.

Unknown mod-specific actions and unreplicated local states cannot be certified automatically. Custom text receivers retain their original UI behavior. Test custom interactables separately and disable their category if necessary.

## Other ownership mods

Do not combine this edition with **TooManyLags - FiresGhettoNetworking Addon**. Use that edition for a dedicated server running FiresGhettoNetworking. Other mods that force ownership can override this grid and cause competing transfers.

Standalone has no PUP FPS integration or embedded collision guards. Its player-owned ownership handoffs are separate from any clustering mod's collision behavior.

## Updating

Update the host/server and modded clients together. Existing configs are automatically reorganized at startup, preserving supported values and keeping the original once as `.cfg.pre-layout.bak`. Older clients remain joinable through the restricted fallback; they do not gain the latest guards until updated.
