# Standalone validation

## Local candidate 1.1.0

Standalone retains the portable grid and guarded player-to-player ownership features from FiresGhettoNetworking Addon 1.0.45, plus the local Addon's native sign/portal text protection adapted to the independent protocol and listen hosts. It has no FiresGhettoNetworking/PUP assembly dependency, dedicated-server simulation or fake inventory-open probe. Friendly summons, bosses, static pieces and loose item drops retain native ownership.

The build treats warnings as errors. `verify.ps1` runs eight groups: grid/policy/cache, camera, handoffs/network, session/live configuration, text-edit lifecycle/controller, actual native contracts, production BepInEx config generation and config migration. `verification.json` records the checked DLL, native assembly, exact sources and package metadata. `package.ps1` checks those hashes before packaging. Publication is separate.

## Text-edit protection

- The native TextInput request is intercepted only for exact native Sign and TeleportWorld receivers. Custom receivers and earlier Harmony vetoes retain their original behavior. Native access checks, receiver, topic, character limit and saving remain intact.
- The local source veto exists before the begin request leaves the editor. The host requests the actual editor through the existing guarded handoff, then waits for ownership, pending commits and final-state activation receipts. The native UI opens only after the matching grant and local owner revision arrive. No direct owner writer is added.
- Only the edited object is pinned. Confirmation, closing the UI and elapsed time do not release it; actual movement does. Small position noise is tolerated. Other objects in the same cell remain independently managed.
- During the lock, automatic takeovers are dismissed rather than queued for later. Release alone creates no takeover. A fresh covering grid request enables normal decisions again. The per-object wait is removed at native-region retirement, node removal or shutdown; unshared completed edits retain no unreachable node.
- Local hosts use the same checks with synchronous local messages. Remote editing works on dedicated servers. Disconnection, connection replacement, death, teleport, object/view removal, ownership loss and world reset clean up the corresponding lock. Incomplete openings time out; a late grant cannot open an abandoned editor.
- The base ownership protocol stays at 3. An optional trailing text capability is exchanged through the existing channel. Earlier protocol-3 clients retain their other guards but keep native sign/portal ownership; unmodified clients remain joinable. Both ownership endpoints need the text capability for managed sign/portal transfers.

The text suites pass 118 production lifecycle/Harmony assertions and 14 real controller/grid assertions, on both CLR and the game's embedded Mono. Unity, transports and handoff completion are controlled fixtures. Cases include dedicated/listen host, ownership-receipt ordering, delayed/fenced requests, replacement connections/character instances, death/unload, native fallback and zero repeated network/handoff work during 10,000 stationary iterations.

## Existing protections and regressions

The production handoff suite passes 6,397 assertions. Source use checks precede owner writes; a text-busy source rejects an in-flight commit, and releasing the text guard never replays that token. Inventory GUI-before-in-use, pending opening, loading, renewed one-second refills, incinerator burning, riders/following/active users, attack completion, late delivery, source snapshots and recipient state receipts remain covered. A fresh token can transfer after the veto ends.

Network tests pass 52 boundary assertions and session/config tests pass 56 assertions. They cover local/dedicated coordination, optional clients, authoritative session/camera settings, new and absent text capability tails, disabled sessions, admin identity, malformed packets, bounded queues and disconnect cleanup. Camera checks retain 11,017 receipt/cadence/lifecycle assertions and 26 actual publisher/controller cases.

Actual game metadata/IL checks cover 147 assertions and all 28 exact game Harmony targets, including TextInput.RequestText and its private queued-receiver contract. MonsterAI target instrumentation still covers 14 stores across five actual native methods. The owner-write audit still permits only the existing source commit and narrow native fallback paths.

The fractional-cell and directional/cache suites retain their independent references, 6.4-metre boundaries, eight directions, overlap-only cooldown, timer preservation and native-region retirement tests. Stable 10,000-object/1,000-cycle coverage adds no repeated object jobs. The text renewal callback is null when no edited objects await fresh requests.

## Runtime cost and lifecycle

There is one additional game hook, on native text entry. Existing native import, destruction and view-unload hooks provide lifecycle events. The existing network channel carries the optional feature; no extra permanent connection endpoint is installed.

Without editing, text callbacks are null and no text object scan or timer runs. While an opening is pending, only those openings are checked. A granted remote edit has no server polling loop: player positions already read by the controller and relevant native imports release the lock. The client samples movement while it holds edits, skipping unchanged position lists. Per-object state is bounded, indexed and removed with its connection/object/world. No new physics loop is added.

The existing grid remains indexed by loaded native regions; coarse eligibility and unchanged coverage avoid repeated object work. Dynamic objects and private inventories keep object-centred recipient readiness; stationary objects use the receiving client's loaded footprint. No server scene substitutes for client readiness, and no PUP-specific collision readiness is claimed.

## Config and scope

The 45-setting config uses the shared feature layout with categories last. Existing supported values migrate at startup; an original `.cfg.pre-layout.bak` is saved once. Fresh movement/fallback checks default to 0.1 seconds and camera checks to 0.05. Real BepInEx/Harmony migration has 25 assertions, including a separate embedded-Mono run with normal path bootstrap. Config reformatting occurs only at startup and explicit saves.

These checks do not start a live Unity world, simulate real PhysX or measure network/CPU/FPS performance. Live multiplayer testing is still required. Unknown custom mod actions and direct competing ownership writers are outside the certified contracts.