# Standalone validation

## Release 1.1.2

This candidate preserves the 1.1.0 grid, optional-client policy and handoff protocol. No new ownership writer, dependency, server simulation, global object scan or physics hook is added.

- A reusable handoff snapshot now checks the exact job identity, not just its object ID. The new regression fails with the old check: an inline listen-host callback replaces a later job, and the retired snapshot cancels that replacement. The candidate preserves the replacement and commits once to its new target.
- Local blocked-action scans run only when the earliest timed check is due. Event-only inventory/user vetoes keep their existing event wakeups; the existing two-second retirement sweep remains. Timed checks retain their 0.1-second cadence. Repeated refills reuse their existing lease while renewing the full one second; no expiry grants ownership.
- Expiry selectors no longer capture the frame time. This removes the compiler-generated per-Tick closure allocation, including calls that return at the cadence gate. The production IL audit checks this; no live allocation/CPU benchmark is claimed.
- Cached native Fermenter and OfferingBowl references veto a requested source handoff while `DelayedTap` or `DelayedSpawnBoss` is scheduled. Their private pending output/spawn is not contained in a ZDO snapshot. The native callback names and scheduling sites are checked against the installed game IL. No new hooks or permanent action observers are needed.
- Native cart attachment is protected while the source's `m_useRequester` is pending, before a joint exists. Humanoid `m_currentAttack`/`Attack.IsDone` and native AI charging cover the pre-animation attack gap. Direct field readers bind once; these checks run only for pending source handoffs. Cheap action-state vetoes precede animator queries.
- Occupied ships reject a requested grid destination on shore, matching native `Ship.UpdateOwner`. Any valid passenger can remain eligible; empty ships still transfer. The passenger field binds once and the cached ship reference is checked only during a pending source handoff, using its existing timed wakeup. No native ship ownership method is patched.
- The handoff and network fixture suites now run on both CLR and Valheim's embedded Mono, in addition to the existing text/config Mono checks. They do not start a world or real networking.
- A new suite combines production `MobPolicy`, `LocalAgent` and `Handoffs`. Dedicated coordination is forbidden to read live scene instances; the listen-host case uses a real player destination. Both paths cover actual source target publication, attack veto, re-preparation, a single owner commit and recipient activation, plus ping fallback and native exclusions. Transport, Unity and target instrumentation boundaries remain fixtures; actual native target stores are independently audited below.

The no-client capability assessment is in `docs/SERVER-ONLY-GUARD-AUDIT-2026-10-19.md` and the public compatibility Wiki. No synthetic native inventory bridge or broad new unmodified-client fallback is enabled. Matching modded owners retain the full supported handoff path on dedicated and local hosts; missing client state is not replaced with a guessed delay.

The full-client path review and its evidence boundaries are recorded in `docs/FULL-CLIENT-INTEGRATION-AUDIT-2026-10-19.md`.

## Retained features

Standalone retains the portable grid and guarded player-to-player ownership features from FiresGhettoNetworking Addon 1.0.45, plus the local Addon's native sign/portal text protection adapted to the independent protocol and listen hosts. It has no FiresGhettoNetworking/PUP assembly dependency, dedicated-server simulation or fake inventory-open probe. Friendly summons, bosses, static pieces and loose item drops retain native ownership.

The build treats warnings as errors. `verify.ps1` runs nine groups: grid/policy/cache, camera, handoffs/network, mob integration, session/live configuration, text-edit lifecycle/controller, actual native contracts, production BepInEx config generation and config migration. `verification.json` records the checked DLL, native assembly, exact sources and package metadata. `package.ps1` checks those hashes before packaging. Publication is separate.

## Text-edit protection

- The native TextInput request is intercepted only for exact native Sign and TeleportWorld receivers. Custom receivers and earlier Harmony vetoes retain their original behavior. Native access checks, receiver, topic, character limit and saving remain intact.
- The local source veto exists before the begin request leaves the editor. The host requests the actual editor through the existing guarded handoff, then waits for ownership, pending commits and final-state activation receipts. The native UI opens only after the matching grant and local owner revision arrive. No direct owner writer is added.
- Only the edited object is pinned. Confirmation, closing the UI and elapsed time do not release it; actual movement does. Small position noise is tolerated. Other objects in the same cell remain independently managed.
- During the lock, automatic takeovers are dismissed rather than queued for later. Release alone creates no takeover. A fresh covering grid request enables normal decisions again. The per-object wait is removed at native-region retirement, node removal or shutdown; unshared completed edits retain no unreachable node.
- Local hosts use the same checks with synchronous local messages. Remote editing works on dedicated servers. Disconnection, connection replacement, death, teleport, object/view removal, ownership loss and world reset clean up the corresponding lock. Incomplete openings time out; a late grant cannot open an abandoned editor.
- The base ownership protocol stays at 3. An optional trailing text capability is exchanged through the existing channel. Earlier protocol-3 clients retain their other guards but keep native sign/portal ownership; unmodified clients remain joinable. Both ownership endpoints need the text capability for managed sign/portal transfers.

The text suites pass 118 production lifecycle/Harmony assertions and 14 real controller/grid assertions, on both CLR and the game's embedded Mono. Unity, transports and handoff completion are controlled fixtures. Cases include dedicated/listen host, ownership-receipt ordering, delayed/fenced requests, replacement connections/character instances, death/unload, native fallback and zero repeated network/handoff work during 10,000 stationary iterations.

## Existing protections and regressions

The production handoff suite passes 6,445 assertions on CLR and embedded Mono. Source use checks precede owner writes; a text-busy source rejects an in-flight commit, and releasing the text guard never replays that token. Inventory GUI-before-in-use, pending opening, loading, renewed one-second refills, incinerator burning, riders/following/active users, attack completion, late delivery, source snapshots and recipient state receipts remain covered. New cases cover callback-job replacement, independent timed/event wakeups, renewed deadlines, cart attachment requests, occupied ship passengers, pre-animation Attack state, charging, native tapping/summoning completion and reset. A fresh token can transfer after the veto ends.

The production mob integration suite passes 40 assertions on both CLR and embedded Mono. The dedicated coordinator has no live mob scene. The suite uses real policy, source guard, transaction and readiness code with controlled external boundaries; it is not a real multiplayer transport test.

Network tests pass 52 boundary assertions on CLR and embedded Mono, and session/config tests pass 56 assertions. They cover local/dedicated coordination, optional clients, authoritative session/camera settings, new and absent text capability tails, disabled sessions, admin identity, malformed packets, bounded queues and disconnect cleanup. Camera checks retain 11,017 receipt/cadence/lifecycle assertions and 26 actual publisher/controller cases.

Actual game metadata/IL checks cover 165 assertions and all 28 exact game Harmony targets, including TextInput.RequestText and its private queued-receiver contract. Native ship passenger ownership and the new source guard are checked without adding a game hook. MonsterAI target instrumentation still covers 14 stores across five actual native methods. The owner-write audit still permits only the existing source commit and narrow native fallback paths.

The fractional-cell and directional/cache suites retain their independent references, 6.4-metre boundaries, eight directions, overlap-only cooldown, timer preservation and native-region retirement tests. Stable 10,000-object/1,000-cycle coverage adds no repeated object jobs. The text renewal callback is null when no edited objects await fresh requests.

## Runtime cost and lifecycle

There is one additional game hook, on native text entry. Existing native import, destruction and view-unload hooks provide lifecycle events. The existing network channel carries the optional feature; no extra permanent connection endpoint is installed.

Without editing, text callbacks are null and no text object scan or timer runs. While an opening is pending, only those openings are checked. A granted remote edit has no server polling loop: player positions already read by the controller and relevant native imports release the lock. The client samples movement while it holds edits, skipping unchanged position lists. Per-object state is bounded, indexed and removed with its connection/object/world. No new physics loop is added.

The existing grid remains indexed by loaded native regions; coarse eligibility and unchanged coverage avoid repeated object work. Dynamic objects and private inventories keep object-centred recipient readiness; stationary objects use the receiving client's loaded footprint. No server scene substitutes for client readiness, and no PUP-specific collision readiness is claimed.

## Config and scope

The 45-setting config uses the shared feature layout with categories last. Existing supported values migrate at startup; an original `.cfg.pre-layout.bak` is saved once. Fresh movement/fallback checks default to 0.1 seconds and camera checks to 0.05. Real BepInEx/Harmony migration has 25 assertions, including a separate embedded-Mono run with normal path bootstrap. Config reformatting occurs only at startup and explicit saves.

These checks do not start a live Unity world, simulate real PhysX or measure network/CPU/FPS performance. Live multiplayer testing is still required. Unknown custom mod actions and direct competing ownership writers are outside the certified contracts.
