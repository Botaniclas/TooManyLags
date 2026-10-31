# Ownership versus simulation guards

## Local mob readiness in 1.0.73

The broad area-loaded gates below remain removed. A narrower exception now applies only to taking mob ownership onto a PUP server: its canonical position needs PUP's existing local collision/recipe/restore proof. A running client keeps ownership if that proof is unavailable; server-to-player returns do not require it. Initial server mob defaults use the same local proof. Unknown PUP session metadata is not treated as an empty, safe world.

Existing held server mobs within 40 metres of a ready player get priority through native `ZoneBodyGuard.PrepareDedicatedCollision`. Native preparation throttling remains; extra preparation has a shared two-millisecond frame budget (a single native operation can exceed it). This does not change collider completeness, release a hold, bypass AI/fixed-update guards or preload every worldwide region. A remote missing collider is not itself a global refusal; the native queue and session initialization are shared resources.

Idle tamed animals use identified source-owner grid commits. Their source checks unfinished/current attacks, stagger, riding/following, summoned/friendly status and active use, then flushes its normal movement state. FGN's blanket `tamed` refusal is relaxed only inside that authenticated commit; other native vetoes and late-damage forwarding remain. Both endpoints need the new animal capability. Server-owned animals receive the same local busy checks before leaving the server. This is a safe idle handoff, not full continuation of a running attack animation.

New-owner bootstrap `None` reports wait for a successful native AI operation; valid target adoption and subsequent real target loss retain their event path. The solo test reports the effective PUP collision-call result alongside its AI result. These observations distinguish the remaining live causes without forcing an unsafe ownership reset.

## Removed

- Broad `ZNetScene.IsAreaReady` prerequisites before grid, legacy static-default and managed combat ownership changes.
- Per-object destination-loading queries before grid writes, including their retry storage, zone wait buckets and readiness wake scans. Startup no longer constructs the unused ownership-only PUP readiness adapter.
- Broad-area requirements on recipient state receipts and player-simulation preparation. Ordinary object preparation uses the matching ZDO owner/revision; actual AI target adoption still needs its live actor.

`owner_status` still calls `IsAreaReady` once to report the diagnostic value. That call does not grant or block ownership.

## Kept independently

- **Native/PUP simulation:** collision preparation, body freezing, AI collision checks and building-support checks remain at their simulation entry points. The optional PUP compatibility sources are unchanged from 1.0.64, including the narrow FiresGhettoNetworking pre-snap integration.
- **Object use:** open/opening inventories, text editing, incineration, refill, pickup, occupied controls/riding/following and actual attacks retain their protections. These concern an action in progress, not general region-loading permission.
- **Identity/state:** object identity, current owner, owner revision and received state must agree. Active source owners still commit through the existing protocol, avoiding stale replies, lost edits and duplicate actions. Compact ownership-only updates and full-state fallback are unchanged.
- **Routed actions:** an exceptionally delayed action can wait for the recipient's matching data after an owner change. It needs its exact active RPC instance, but no unrelated neighbouring instances. This does not delay ownership assignment. This receipt is lazy: ownership changes without such an action send no receipt query.
- **Physics transfers:** existing sleep/motion policy remains for actual body handoffs; its vacant-field-return exception remains. Native initial assignment adds no sleep test.

## Scope

This build removes loading prerequisites. It does not redesign every remaining use check around a new direction policy. Container/source-commit logic and long-use protection remain as in 1.0.64. The optional PUP simulation module is retained because its immediate FiresGhettoNetworking position-snap integration is independent from the removed ownership gate.

The 1.0.65/1.0.66 mob-test branch is included in 1.0.71. It adds the bounded new-attack barrier, immediate completed-AI/damage reporting, unfinished-Attack veto and explicit solo test, without restoring the removed broad ownership-loading gates. Native/PUP simulation guards remain independent. No live multiplayer test, publication or installed-profile change has been performed.

## Inventory request timing in 1.0.68

Unanswered native inventory ownership requests expire after two seconds and may retry during the same existing policy evaluation. Explicit denials and send exceptions retain a two-second backoff. A grant without its matching owner receipt still cannot authorize an onward write or an unsafe resend; partial owner/revision imports remain protected. This changes request timing, not how long an actually open inventory remains protected.

## Refused ordinary grid handoffs in 1.0.69

A known native source veto or identified processed no-commit with matching current state discards the automatic request for that object. It releases its transaction and waits in the existing cell-renewal index; no six-second retry, status probe or repeated object scan remains for that refusal. Fresh covering-player movement/heading can re-arm it, including when the requested cell owner stays the same. Ordinary data/router events and elapsed time alone do not re-arm it. Unload/reset clears the waiting entry. Missing replies, partial imports and differing source snapshots remain protected until the actual transaction is resolved. Inventory's separate two-second timing is unchanged.

## Inventory refusals and incomplete fields in 1.0.70

Explicit native inventory refusals now follow the same fresh-coverage rule, replacing their former two-second retry pause. Only the refused object's request is discarded. The cell's existing completion certificate is invalidated and cannot be restored until every member satisfies its configured ownership rule. Successfully owned neighbours require no new owner write. The grid/cache algorithm is unchanged. Unanswered inventory requests retain a two-second response window with immediate retry; uncertain send errors retain two seconds. Delayed refusals cannot discard an actual native receipt or half import.

Without Server-Side Simulation, processed grid refusals also park until fresh covering movement/heading. The old indefinite owner/revision rejection lock is removed. Actual native receipts, activation barriers and half imports remain protected; Hostile Mob attack-end waiting is unchanged. The server-side change uses the existing client protocol.
# Optional old-owner mob presentation (1.0.77)

`EnableMobHandoffVisualBridge` controls rendering after an already successful source commit. It adds no ownership wait, authority change, attack continuation or replacement for gameplay safeguards. Both the native FiresGhettoNetworking source commit and the player-only source transaction can arm it; rejected and duplicate commits do not. Server-originated transitions have no old-player bridge.

The presentation module does not flush or modify native state; handoff publication remains native. Only a separate collider/body/network-free visual subtree is offset during camera rendering and restored afterward. The network root, gameplay transforms, animation clocks and damage/event paths remain native. Actual movement/look intent supplies a bounded additive lead on the current native root. Real native progress consumes it; a fresh replica revision or the current RTT deadline starts a finite monotone fade. Root corrections are never differentiated into velocity and no timing history is shared across mobs. Teleports, attacks, stale owner epochs and session changes terminate presentation. The bridge has a bounded local active list and lifetime; no persistent per-creature cache or new per-handoff RPC is added.

