# OwnerDebug 1.0.12 release preparation

## Current state, 2026-10-29

Local release preparation requested; no publication, push or profile installation.
Version 1.0.12 in Plugin assembly/BepInPlugin, manifest, thunderstore.toml,
package default and native audit. Outputs are under ../../outputs; package.ps1
validates DLL/source hashes and refuses existing versioned ZIPs. Run parent
organize-outputs.ps1 after packaging, scoped to BotanicOwnerDebug.
Current exact hashes and automated counts: verification.json.

## Behaviour

- F10 toggles settings, never enables colouring. Six independently saved
  categories start off; radius field/slider applies next render, 10–1000m.
- Truly interactive, fixed static, destruction physics, tamed, wild, loose.
  TML Category delegate uses its canonical policy; standalone equivalent included.
- Player pool: purple, green, blue, yellow, shuffled once without replacement
  for first four assignments with System.Random + Guid; gameplay RNG untouched.
  Existing assignments stay stable per connection/seed. Twelve colours total.
- Online roster receives slots before miscellaneous object owners at Refresh.
- Server white; unowned dark grey; unknown blue-grey.
- Optional TML ai_test mode overrides categories for the exact selected mob only:
  ordinary owner colours before/after transfer, red playback including waits,
  orange final correction. Missing TML hides it; missing API disables it.
- Menu hooks block native Player/PlayerController input, zoom, cursor capture and
  pause-menu Escape conflict while open. Only own hooks removed on teardown.

## Rendering / integration

Final Camera.FireOnPreRender postfix queues normal flat DrawRenderer overlay.
Active TML handoffs instead temporarily paint eligible native material slots;
Camera.FireOnPostRender prefix restores before other post-render listeners.
No BakeMesh, DrawMesh, duplicate geometry, pose offsets or native transform writes.
Restoration also occurs before next camera, removal, clear and disposal; foreign
slot changes/new slots survive. Material assets/property blocks remain untouched.
No repeated handoff hook installation. PUP/native visibility restrictions retained.

TmlDebug caches read-only optional Category/Phase delegates and existing private
MobBridgeVisual.Contains binding. TML source/build belongs to another worker:
DO NOT edit or rebuild it without coordinating after that worker finishes.
Tests use fixed bin/test-inputs/TooManyLags.dll copied earlier from installed client.
No TML DLL is included in this release.

## Validation and limits

Run build.ps1, test.ps1, test-native.ps1 -TmlAssembly <compatible DLL>.
Actual Mono tests install native render/menu/restore hooks and bind compiled TML;
Animator hash seam supports Player static initialization. Headless engine-call
notices are expected; no Unity world/graphics execution is claimed.
Live menu layout/input, GPU handoff alignment and phase colours still need testing.
Earlier snapshot experiments produced wrong size then missing colouring; removed.
Historical investigation remains in VALIDATION.md, not the release changelog.

## Files

Plugin.cs: menu/config/input hooks. DebugView.cs: registry/filters/native draws.
OwnerColour.cs: player shuffle + phase palette. PresentationPaint.cs: scoped slots.
DebugCategories.cs: standalone classification. TmlDebug.cs: optional binding.
README.md: final user instructions, upgrade note and optional API requirements.
CHANGELOG.md: final 1.0.12 behaviour; 1.0.11/1.0.10 history preserved.
