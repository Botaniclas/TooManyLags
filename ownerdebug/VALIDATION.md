## OwnerDebug 1.0.12 release preparation, 2026-10-29

Local release only; user requested preparation, not publication. README rewritten
around F10 menu, category selection, live radius, randomized four-player palette,
red/orange optional ai_test phase mode, standalone operation and upgrade migration.
Experiment history removed from release notes; 1.0.11 history recovered from its
existing ZIP without changing that archive. Runtime implementation unchanged
apart from version 1.0.12. TML not edited or rebuilt; no installed profile changes.

Build version: 1.0.12.0; DLL SHA256:
E4DDA8D402E3B97A62089F9475448D44EE666006C2D8428897E8302B8205FF6E.
Automated results recorded in verification.json. Actual graphics and live
multiplayer remain unverified; preparing a release does not imply those tests ran.
Packaging includes matching DLL/README/changelog/manifest/icon and a separate
source ZIP with current docs, validation and receipt. Packaging rejects changed
verified inputs or existing versioned ZIPs; old archives remain unchanged.
## Requested randomized four-player pool, 2026-10-29

CURRENT player pool: purple, green, blue, yellow. The first four player slots
receive these without repetition using a connection-local Fisher-Yates shuffle.
System.Random with fresh Guid entropy plus configured seed; no Unity/game RNG.
Existing owners remain stable until network reset or seed change. Red playback
and orange correction remain fixed and excluded from this first-four pool.
Later eight palette entries remain distinct; twelve colours total. User explicitly
requested yellow despite its closeness to orange. No TML/rendering changes.
## Player palette adjustment, 2026-10-29

Replaces cyan in the first three assignments with saturated magenta. First three are now blue, green, magenta for every seed. Red=fake playback, orange=smoothing unchanged. Twelve palette colours remain unique; cyan moves to the second group. No rendering or TML changes. Managed suite: 2,440 checks PASS. Native results in verification.json; live colour comparison remains open.

## Player/transition palette separation, 2026-10-29

Current colours supersede earlier phase notes: red=fake playback;
orange=final smoothing; old/new owner retain ordinary assigned owner colours.
First three player assignments are saturated blue, green and cyan. ColourSeed
permutes within groups of three, never across the protected first group. Online
roster players receive palette slots before miscellaneous tracked object owners
in Refresh. Assignments remain stable for the connection; later joiners retain
the next slot rather than reshuffling existing players. Server stays white.
No game RNG, TML edits/builds, installations or rendering-path changes.

Tests cover distinct first-three player colours, distance from both phase colours,
seed edge cases (negative/min/max), twelve distinct palette entries and stable
assignments. 2,440 managed assertions pass; native results in verification.json.
Live colour comparison remains untested.
## Native handoff colouring replaces snapshots, 2026-10-29

CURRENT DESIGN supersedes the frozen/snapshot sections below. User reported
oversized snapshot, then absent colouring after scale change. Installed hash
confirmed latest build; no OwnerDebug exception in current client log. Exact
BakeMesh/GPU cause remains unproven; managed mock assumptions did not establish
engine visual correctness. Do not report those earlier changes as verified fixes.

Removed PresentationDraw, all BakeMesh/DrawMesh geometry and scale conversion.
During active TML presentations, PresentationPaint temporarily replaces only the
eligible native renderer material slots after final pre-render callbacks. Native
renderer now draws the visible mob itself in the chosen colour. No duplicate
geometry, independent skinning, pose offsets or transform matrices exist.
Rigid attachments use the same scoped material policy. Untintable effect slots
remain original. Ordinary objects retain the existing flat DrawRenderer overlay.

Typed Camera.FireOnPostRender prefix restores slots before native post-render
listeners. Next-camera preparation, clear, renderer removal and disposal also
restore. Only slots still holding our own materials are restored; unrelated
foreign changes/new slots survive. Original materials are not cloned or mutated.
Arrays/lists and colour materials are reused. No CPU skinning or snapshot meshes.
No TML source or build changes; fixed installed-copy test input remains in use.

Validation: build passes; 2,356 managed checks, 7,465 actual-DLL/native contracts,
41 actual Mono render/menu/phase/bridge binding and restore-hook checks PASS.
Managed tests cover native skin + rigid attachment paint, no duplicate draw,
untintable slots, post-render restoration, foreign material updates, changed slot
count and removal. No Unity world/graphics execution; live visual verification
still required. No profiles installed.
DLL SHA256: 7DF5EB30C5F0F3298EC59CFEF0C8689AF6BB9DF7B7F5C83E01198DAA05DE768D.
## Snapshot scale correction and owner colours, 2026-10-29

User observed roughly 20x oversized snapshot in game. Previous BakeMesh(false)
plus full hierarchy matrix was not a safe imported-rig scale contract. Skins now
BakeMesh(true), storing final size, and draw using TRS(position, rotation, one).
Rigid attachments retain full localToWorldMatrix. No hardcoded size factor or
native transform/mesh mutation. Prior mock assumed false was correct; replaced
with explicit baked-scale/draw-scale combination tests at .05, 20, nonuniform
and negative scales. These are managed contract tests, not engine skinning proof.
Unity API reference: https://docs.unity3d.com/ja/6000.0/ScriptReference/SkinnedMeshRenderer.BakeMesh.html

Old/new owner phases now use their normal assigned owner palette, including
white server. Orange/purple remain playback/correction; legend lists actual
owners and these two phase colours. Tests change owner from player to server
and verify ordinary colours; fake/correction colour remains phase-specific.

Build passes, 2,372 managed checks, 5,395 DLL/native checks and 37 Mono hook/API
checks pass. No TML source/build changes or profile installation. Live Unity
scale and alignment need retesting. Current hash: A811B8081009BC251915F9B30F9C9A1E80188B72EB8BDD151661BF0B85980DF4.
## Frozen handoff overlay, 2026-10-29

OwnerDebug-only fix for displaced colour geometry during temporary TML poses.
The late callback remains necessary, but CommandBuffer.DrawRenderer only retains
an engine renderer reference; it does not own the transient Transform or skin.
The old managed fixture incorrectly assumed renderer data always matched the
current visible hierarchy and did not model cached/native restoration differences.

For active TML bridges only, final-camera submission now captures the live
Transform.localToWorldMatrix and renderer-local posed geometry. Skinned parts
BakeMesh into one reusable owned dynamic mesh (useScale=false); rigid attachments
reuse shared native geometry with an explicit captured matrix. DrawMesh queues
these values. Normal renderer drawing resumes immediately when the bridge ends.
Index width follows the source. Original meshes/materials/transforms untouched.
Snapshot allocation is lazy, retained across handoffs and freed on renderer/root
retirement or debug disposal. CPU skinning only runs on selected visible active
bridge parts while OwnerDebug is enabled; no CPU/GPU timing claim is made.

TML was NOT edited or rebuilt: another worker owns it. OwnerDebug binds the
existing MobBridgeVisual.Contains lookup once as a read-only typed delegate,
with Phase fallback when unavailable. Tests use bin/test-inputs/TooManyLags.dll,
a fixed copy of the installed client DLL, avoiding that worker's changing build.

Build passes; 2,355 managed and 5,342 built-DLL/native assertions pass. Actual
Mono checks cover menu/render hooks, phase API and cached bridge lookup. They do
not execute Unity skinning/GPU rendering. In-game alignment still needs testing.
Current OwnerDebug DLL SHA256:
1BF4DC42FB36402A3AAD5DECE23405F47DA5984299330793DA875ABD37EAD7AF.
No installed profile changes, packages or publication.
## Settings menu and TML test phases, 2026-10-29

F10 now opens settings without toggling colouring. Six independent persisted
category switches default off. Radius text/slider changes apply on next render.
Legacy ShowOwnerColours, InteractiveObjectsOnly and MobsOnly no longer drive it.
The draggable IMGUI menu keeps the mod standalone; EasySpawner's bundled menu
and native input/cursor approach were inspected, without copying its asset bundle
or adding a dependency. Player and PlayerController input, zoom and pause/Escape
handling use small own session hooks; no AI hooks or handoff patch churn added.

Current TML categories come directly from ObjectCatalog through cached typed
optional delegates. Standalone equivalent follows interaction root overrides,
physical-body/successor eligibility, static safety and live tame state. Test mode
is only offered with installed TML and is disabled when its API is missing. It
colours only exact ai_test identity: blue=old preparing owner, green=new owner,
orange=stretched playback including waits, purple=final correction. Category
selection is retained while overridden. No networking or simulation work added.

Validation:
- build.ps1: actual installed Valheim references, warnings as errors.
- test.ps1: 2,346 managed rendering/category/ownership/lifecycle assertions.
- test-native.ps1: 6,937 built DLL/native contracts; 34 actual Mono render/menu
  hook and compiled TML API checks. Real identity/revision changes, playback,
  correction, unrelated ZDO and stopped-test rejection exercised using disposable
  managed native containers. No GameObjects, physics or RPC.
- Test host uses Animator.StringToHash seam for native Player static initialization.
  Expected unresolved Animator/Time notices come from headless JIT; no live engine
  is running. Hook installation and cleanup assertions pass.
- TML full verify.ps1: 19 suites PASS; exact TML hash recorded in its own receipt.

OwnerDebug DLL SHA256:
D2249FEBF485AFE32240FCC34DB59D617AD3923ADF9F0D6202A29A5821C2C404.
No profile installation, packaging or publication. Live menu layout/input, GPU
skinning alignment and networked visual-phase comparison remain untested.
# Unreleased - Final-pose render scheduling (2026-10-29)

OwnerDebug submits its existing native-renderer command buffer after Unity's managed Camera.FireOnPreRender dispatcher has invoked every onPreRender listener. The strongly typed postfix avoids subscription-order dependence with TooManyLags' late handoff bones. Plugin.LateUpdate no longer submits draws. The GPU pass remains BeforeImageEffects; no source materials, renderer bindings, actor transforms, meshes or ownership are changed. No direct dependency on TooManyLags is added.

Secondary cameras return before registry/material work. If the former main camera renders before its replacement, its stale overlay buffer is detached without touching foreign buffers. Repeated rendering refreshes the same active-camera buffer. Hooks exist only during enabled overlay lifetime; teardown preserves other hooks and camera listeners.

Build passed against installed Valheim references. Managed suite: 2,315 assertions, including both presentation-listener registration orders, final-pose submission, camera replacement, secondary-camera fast path, stable-frame scan counts and foreign-resource preservation. Built-DLL/native audit: 4,907 assertions. Actual Unity managed camera dispatcher: 13 install/callback-preservation/cleanup checks passed under Valheim Mono, including repeated production-postfix installation and foreign hooks surviving teardown. The native audit runs independently from a fresh bin/native-tests directory.

DLL SHA256: A4430F892D2FF8943231969356284FFA1CE0ACD979E12EB926D219001E8F3E62. Source/plugin version remains 1.0.11 with Unreleased changes. No profiles installed or changed, no packages or releases produced. The actual Unity dispatcher checks use no camera/world/GPU; live skinned rendering and the reported visible offset still require in-game comparison. The test proves ordering and lifecycle, not that command-buffer timing was the sole GPU-level cause.

---
# BotanicOwnerDebug 1.0.11 - World and physics overlay

All mode now accepts persistent network roots with the native IDestructible interface or their own Rigidbody, including standing trees, rocks/ore and fallen logs. Native distant destructible/physical roots are supported without admitting arbitrary distant proxies. Existing player/embedded-item exclusions, material/visibility filters, interaction/mob modes and unload cleanup remain active. Objects without network ownership cannot display an owner.

Only BuildingVisuals eligibility and Plugin version/config descriptions change at runtime. DebugView, rendering, colour assignment, native hooks and update cadence are unchanged. Capability checks happen on registration; no new recurring hierarchy/material scan, RPC, collider/physics operation or ownership write is added. Enabling All for more visible objects naturally adds overlay draw calls.

All 2,297 production-source rendering/ownership/lifecycle assertions and 4,839 actual-DLL/native-contract assertions passed. Regressions include native/custom destructible roots, distant roots, physical renderer movement, live owner colours, mode filtering, idle scan counts, source materials, unload and player/effect exclusions. Native IL verifies IDestructible on TreeBase, TreeLog, MineRock, MineRock5 and Destructible and confirms no destructive/physics method calls. Final DLL SHA256: `5F12CB8F159A1D8E608DE1FB621CE11B0520D8CB3C5DDD13743B78FBE1A3604B`.

No live Unity/GPU or multiplayer test, installed-profile update, publication or Git push is claimed. Existing config keys/defaults are retained. Earlier results below apply to their named version.

---

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








