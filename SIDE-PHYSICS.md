# PhysicsObjects in the TooManyLags 1.0.61 test build

Local experimental build based on `TooManyLags-1.0.60-loose-initial`. Preserves its decorative-vine support and early loose-item/egg ownership, and includes bounded ownership-message batching with failure isolation. The main project, earlier candidates and installed profiles were not changed. Plugin version: 1.0.61; not published.

## Behavior

- New server category: `PhysicsObjects = true`, under Object Categories. It uses `ServerOwnedInteractivesByDefault` and the existing grid/exit rules. Requires Server-Side Simulation and this updated build on the server and participating clients. Player-only simulation mode is unchanged.
- Includes simple persistent, synchronized world physics props and stationary objects whose explicit destruction successor becomes such a prop: for example, standing trees and falling logs. Ordinary loot tables and visual effects do not qualify an object. Projectiles are explicitly excluded. Existing inventories, animals, vehicles and other actual interactions retain their own categories and guards.
- Initial assignment of existing unowned objects uses an already settled, available grid owner, or the configured interactive default. Freshly spawned objects keep native creator ownership so creation and the spawn impulse execute together; no special spawn retarget is added.
- Later automatic transfers of non-interactable physics bodies require the **current owner's** Rigidbody to be sleeping. A server replica's sleep state cannot authorize a player-owned body's transfer.
- A genuine vacant-field return to the server with `KeepOwnershipOnCellExit=false` and the interactive server default enabled bypasses sleep. Overlap-driven server acquisition still requires sleep. Never-covered new objects are not treated as exit returns.
- A moving-body veto discards only that object's request. Fresh movement/heading coverage can retry; merely stopping the body does not start another attempt. Other objects in the cell continue normally.

Only single-body roots with native position, rotation and body-velocity synchronization qualify. Jointed/multiple-body hierarchies, independent child network roots and insufficiently synchronized props keep native handling. With `PhysicsObjects=false`, sources/props remain native rather than silently entering the static category.

## Performance

Classification and component references are cached with weak object lifetime. No periodic sleep scan, per-prop timer, second velocity serializer or separate scene scan is added. Existing native motion synchronization carries velocity through a real handoff. One small prefix on the existing native transform tick checks ownership gain and synchronizes a qualifying body once; it respects existing kinematic collision holds. Stable-owner ticks use an early cached native ownership check.

The regression fixture checks that 100 unchanged passes after a moving-body veto add no sleep queries, hierarchy scans or owner writes. Enabling the category naturally adds the work of indexing and actually transferring its eligible objects; no live CPU timings are claimed.

## Test installation

1. Use a test session/world copy. Replace the existing `TooManyLags.dll` on server and clients with the test DLL; do not install a second copy alongside it.
2. Keep your existing config. The new category is added automatically; the supplied config is only a defaults example.
3. Leave Server-Side Simulation enabled. Test a standing tree, its falling log, an awake/sleeping log, player overlap, and a vacant-field server return. Check that ordinary drops and projectiles keep their separate behavior.

Automated checks passed; live Unity/PhysX and multiplayer testing has not been performed. Existing collision/readiness/use guards remain active, so a sleep bypass is not a bypass of every handoff prerequisite. See `VALIDATION.md` for the exact scope and DLL hash.

`merge-provenance.json` records the 1.0.60 baseline and the reviewed side-build import. The three overlapping runtime hunks preserve both sets of changes; the current source archive contains the combined implementation.
