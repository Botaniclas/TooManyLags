# BotanicOwnerDebug 1.0.10 - Loose item overlay

Loose ItemDrop network roots are included in both All and Mobs + interactive objects. Mobs only still excludes them. The overlay stays independent of server ownership mods and category switches. Embedded/held item visuals, players, effect materials and independent network children retain their exclusions. Owner changes and pickup/unload use the existing changed-object and renderer registries; no new timer, scan loop or gameplay hook is added.

Production-source fixtures pass 2,167 rendering/ownership/lifecycle assertions, including bare item roots, dynamic owner colour, all filter modes, pickup removal, embedded item visuals and item effects. Actual built-DLL/native metadata checks pass 4,810 assertions, including unchanged read-only native access and no renderer/mesh/ownership mutation. Build and checks pass against the installed Valheim assemblies. Live Unity/GPU and multiplayer item rendering remain unverified. Release checks completed before publication.

---

# BotanicOwnerDebug 1.0.8 - Validation

## Release scope

F10 now cycles **Off -> Mobs only -> Mobs + interactive objects -> All buildings and mobs -> Off**. Existing saved display settings remain supported; `MobsOnly` takes precedence over `InteractiveObjectsOnly`, and `ShowOwnerColours=false` always means off. Starting from off selects mobs only, regardless of saved filters.

All overlay labels, fallback owner names and configuration descriptions are English. Actual player names are preserved. The README includes the two existing ownership GIFs, identifies the TooManyLags grid shown in them, and documents the new controls. Hosted GIFs add no game resources or runtime work.

Rendering, ownership hooks, object classification and filter registries retain the 1.0.7 behavior. This release changes no gameplay, RPCs, ownership assignment, colliders, original material bindings or transforms.

## Classification and rendering

Interactive objects have an `Interactable` component belonging to their network object, including owned children and `Container.m_rootObjectOverride`. Independent nested network objects do not make a static parent interactive. Persistent interactive roots without `Piece` or `WearNTear` are supported. Detection never invokes `Interact` or `UseItem`, and does not mirror another mod's server categories or configuration. All NPC mobs are included; players are excluded.

Three cached renderer registries support all objects, interactive objects plus mobs, and mobs only. Switching active modes reuses existing hooks, camera buffers and original renderer references. It does not rescan models or materials. Drawing iterates only the selected registry. Hidden objects still receive targeted ownership updates so returning to all mode shows current colours. Unload, world changes and disposal clear the registries.

The overlay uses `CommandBuffer.DrawRenderer` with existing native renderer references and material slots. There are no raw mesh draws, mesh copies, pose bakes or custom transform matrices. Shared `Hidden/Internal-Colored` materials are keyed by owner and culling mode. Original materials and property blocks are never overwritten or cloned.

The additional pass runs at `BeforeImageEffects`, with `ZTest Always` and `ZWrite Off`, preserving colours through other objects. Native activity, camera, layer, distance, frustum and renderer visibility restrictions remain active. Disabled PUP FPS original renderers are not reactivated. Players, loose items, clusters, particles, effects, transparent and cutout surfaces remain excluded. Native property blocks can affect the replacement pass; automated tests do not prove GPU appearance.

## Updates and cost

Owner and replicated owner-revision events mark tracked IDs for an owner read at the next debug draw; they do not rescan models or materials. Supported equipment events trigger targeted model reconciliation. NPC models, validity and the player legend retain their existing one-second reconciliation. Online players appear even without owning a tracked object. Missing character IDs show an explicit `owner ID loading` row, replaced when the native ID arrives; duplicate names remain distinct by session owner ID.

While active, selected renderers require visibility checks and an additional draw pass. The overlay costs CPU/GPU time; no measured performance improvement is claimed. Buildings do not receive periodic hierarchy, material or owner scans. Unknown dynamic building renderers added by other mods may require a complete off/on cycle. When off, overlay resources and hooks are disposed; the plugin still checks its toggle and enabled state.

## Automated checks

Build with `./build.ps1`: C# 5, AnyCPU, optimized, warnings as errors, against actual local Valheim, Unity, BepInEx and Harmony assemblies. Run `./test.ps1` and `./test-native.ps1`. Exact assertion counts and input/output hashes are recorded in `verification.json` after execution.

The managed suite covers repeated four-state cycles, saved-filter precedence, interaction and container classification, nested roots, filtered rendering, ownership events, roster joins and pending IDs, unload/reload, camera and resource cleanup. The native audit checks the built DLL, actual Valheim contracts, tested cycle use, and absence of gameplay/material/ownership mutations.

The existing large-registry regression retains 1,000 static renderers and one interactive renderer: interactive mode draws one, mobs-only draws none, and all mode restores 1,001 original references without repeated classification or material work.

## Remaining live checks

This release has not been tested in the running Unity game or live multiplayer. GPU appearance, network delivery delays, roster join timing and CPU/GPU costs need an in-game check.

Replace the client DLL and restart. The legend should show 1.0.8. With menus closed, press F10 through mobs only, mobs plus interactive objects, all buildings and mobs, then off. Confirm doors, containers, beehives and NPCs appear in the appropriate modes; static floors and walls appear only in all mode. Check ownership changes, pending player IDs, animation, object size, colours through walls, and unchanged original appearance after switching off. Compare FPS in the same view with the overlay on and off.

## Reference APIs

- [CommandBuffer.DrawRenderer](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Rendering.CommandBuffer.DrawRenderer.html)
- [CameraEvent.BeforeImageEffects](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Rendering.CameraEvent.BeforeImageEffects.html)
- [Renderer.isPartOfStaticBatch](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Renderer-isPartOfStaticBatch.html)

## 1.0.9 - fixed owner palette
Six base hues and six intermediate variants replace UID-derived random hues. Twelve first-seen owner IDs receive unique slots, preserved across overlay toggles and owner changes. A new network connection resets slots; more than twelve historical IDs repeat the palette. ColourSeed rotates the six-colour order; assignment order is local, not guaranteed identical between clients. Server white and unowned dark grey remain unchanged. No ownership or rendering changes.

Validation: optimized production build; 2,160 runtime assertions (including twelve unique colours and stable repeated lookup); 4,768 actual-DLL/native contract assertions. Live GPU colour appearance has not been tested.
