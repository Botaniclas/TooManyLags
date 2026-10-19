# Server Side guards in Standalone without guest client mods

Source assessment, 2026-10-19. Compared Standalone 1.1.0 with TooManyLags - Server Side 0.13.1. **No gameplay code or released package was changed.** The host/coordinator has Standalone; guests may be unmodified. No FiresGhettoNetworking, Dedicated Simulation, new remote RPC handler or client DLL is assumed.

Installed game assembly SHA256: `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`.

## Result

An expanded native fallback is feasible, especially for ordinary public chests and known immediate controls. The Server Side code is not a drop-in replacement: it has live server instances and a real server-simulation destination, whereas Standalone coordinates player-to-player transfers and normally leaves a dedicated server out of ownership.

Keep the existing source-confirmed path whenever both endpoints support it. Add separate, direction-specific native policies rather than changing every nonmodded peer to `Capable=true` or broadening one global allowlist. Native permission and replicated state provide useful protection, but are not equivalent to the full source/recipient protocol.

## What transfers

| Guard or feature | Feasibility without guest mods | Conditions and limits |
|---|---|---|
| Original-request forwarding, ownership revision checks and targeted state sends | Strong direct candidate. Standalone already forwards selected stale owner requests. | Reuse raw ZDO/peer/RPC identities and the Server Side packet validation/snapshot approach. Exclude responses, broadcasts and completed destructive requests; avoid duplicates and replay after a timeout. A queued send does not prove the remote scene is ready. |
| Cancellable transfer window | Applicable to pending player A to player B transfers. | Reuse native socket RTT and source-object receive events. A source update cancels only its matching pending source/revision. No sleeping thread, permanent object scan or automatic retry after expiry. RTT multiples are estimates, not delivery bounds or source permission. |
| Observed one-second refill hold | Feasible as additional protection using raw replicated data. | Cache native prefab fuel/queue/ammo/slot layout, seed current values on registration, and inspect actual incoming changes. Renew on positive fill changes, not consumption. Own-owner local inserts bypass routed requests and become visible only when replicated; this is weaker than LocalAgent's source-side protection. |
| Ordinary public stationary containers | Most valuable new native transaction candidate. | Ask the actual source through native RequestOpen, receive its real source-to-coordinator commit, then delegate to the chosen player. Requires a new native transaction path; current Server Side implementation assumes a live Container object. Opening/loading timing and recipient readiness remain limited. See below. |
| Pickables, collectors and similar immediate controls | Candidate for an extended, explicitly bounded native fallback. | Exact native controls only, with state-aware completion, original-request routing and applicable source-update cancellation. The source can execute locally without an observable routed request; do not claim an atomic reservation or enable all consumptive/custom controls by classification alone. Source-side headless pick-effect patches are unnecessary when only players execute picks. |
| Idle tamed-animal identification | Native tame/user/follow/transport flags can be reused. | Identification is not an action lock. Do not transfer during indicated combat, riding, following or transport. Source-local pending use, attack completion and receiving physics readiness are not fully visible. Guard parity is not established by these flags. |
| ItemDrop pickup protection | Native request/owner events and coverage-contribution lifetime can be adapted. | Preserve failed-pickup/requester priority and native pickup completion. Server Side currently uses live node/physics readiness; Standalone's blanket exclusion cannot simply be removed. No temporary headless physics owner. |
| Boats and carts | Native user/joint/request tracking can improve additional vetoes. | Server Side's cargo acquisition can temporarily make the server the vehicle owner. That leg requires simulation and loaded physics; it is unsuitable as-is for Standalone. Purely local pending cart attachment remains invisible. Keep native use priority. |
| Hostile Mob cooldown observation | Raw native attack trigger observation and cached prefab metadata are technically possible. | This does not supply attack-end observation or the actual target. Do not add all-mob tracking if no usable native handoff policy can consume it. |
| Hostile Mob attack-end/forecast/idle-handoff guards | Cannot preserve Server Side parity across arbitrary unmodified remote owners with a direct copy. | The existing guard reads Humanoid.InAttack, actual server Attack completion, charge/stagger and live character/target geometry. Native animation triggers do not publish an authoritative animation-end tag. Standalone does not load every peer's scene. A cone can choose a desired owner from replicated poses, but does not solve the safe commit. |
| Exact target-based Hostile Mob ownership | Still requires owner reports, or a changed policy. | Native BaseAI.SetTargetInfo stores target presence, not the target's ZDO identity. A nearest-player/cone policy is an approximation and changes semantics. A listen host can inspect its own owned mobs; that does not reveal a remote owner's private AI. |
| Signs/portal editing until movement | Not supplied by Server Side either. | Editing begins locally before submission. Server Side excludes the identity from the grid. Keep native handling for unmodified endpoints; forwarding a later tag submission is not an editing lock. |
| Incinerator, delayed tapping/offering and other private coroutines | No complete new Server Side guard to copy. | Server Side keeps unsupported/deferred controls native. Incinerator.isInUse is private; lever return precedes conversion. Broadcast/timer estimates do not certify the source coroutine has completed. Ordinary chest permission does not protect burning. |
| Remote scene, inventory and collider readiness | Full parity unavailable without a report from that client. | Native active area, connection readiness, RequestZDO and RTT are not acknowledgements of a loaded live instance. Larger grids provide lead time, not proof. |

## Public chest bridge: viable design, not a read-only query

Native Container.RPC_RequestOpen checks the source's actual local IsInUse, wagon use and privacy/access. On success it requests a state send, **changes ownership to the caller**, and sends RPC_OpenResponse. Public access accepts the zero profile used by the headless bridge; private/custom containers must stay excluded. The replicated `inUse` value is useful for a cheap early refusal, but zero does not prove no opening is pending.

A Standalone adaptation can work with ZDO identities and cached exact-native prefab contracts, without creating a server-side chest instance:

1. Restrict to ordinary public stationary chests; reject vehicles, incinerators, graves, private inventories, unknown controls and moving roots.
2. Keep one real pending native transaction per object/source revision. Send the native opening request as the coordinator, preserving authentic sender identity.
3. Intercept that bridge's reply in raw server routing. This must also happen on listen hosts, where a normal positive response would otherwise open the host's inventory GUI. Never generate a positive opening response for the eventual guest.
4. Wait for the exact native source-to-coordinator owner/revision receipt and its state import. Preserve half-imports and late replies; a timeout is not permission, and a granted commit cannot be rolled back blindly.
5. Delegate to the still-valid grid player, send the real state/owner to relevant peers and route genuine waiting interactions to the current owner. Handle changed targets, disconnects and unloading; never strand a dedicated server as a persistent nonsimulating owner.

For a stationary chest the interim coordinator owner need not run creature/vehicle physics. It still adds an ownership hop and requires explicit cleanup and reply correlation. Native RequestOpen does not reserve the opening GUI phase, pending take-all transaction or recipient loading, so this remains native-level protection rather than equality with the full client-assisted guard. It needs dedicated and listen-host tests before enabling.

## Important additional opportunity: asymmetric endpoints

Current fallback restrictions treat an unmodified source and an unmodified receiver alike. They are different problems:

- **Modded source, unmodified receiver:** the real source can retain its existing inventory, refill, active-use and attack checks and make the commit. Only receiver readiness becomes the weaker native active-area path. Handoffs already has NativeTarget preparation/activation handling for simple objects. Carefully extending selected categories here is more promising than discarding source guards. Further ownership decisions after the receiver becomes an unmodified source still need the native fallback.
- **Unmodified source, modded receiver:** destination readiness is available, but source permission is missing. Native public-container acquisition can supply some of it; a ready receiver alone is insufficient.
- **Both unmodified:** use only the explicit native contracts above, with their real limitations.

This is especially useful for a listen host: its installed Standalone instance already counts as a capable local owner. The host's own loaded/source-owned state must never be used as a substitute for a remote source's private state. Mob target reporting gates require separate changes; permitting a native target endpoint does not create continuing target reports from that guest.

## Minimal implementation order

1. Preserve the current full client-assisted protocol. Add a separate native policy choice per object and transfer direction, with existing source/revision/peer checks and native zero-owner releases.
2. Extend native routing/snapshot handling and event-driven state guards for a small exact-native set. Reuse current interest/sector retirement; allocate source windows only for actual pending transfers and fill history only for eligible indexed stations.
3. Prototype public stationary chests and the modded-source/native-receiver path, with separate dedicated/listen-host cases. Exercise simultaneous opening, source-local inventory actions, late/missing/duplicate replies, half owner imports, native claim races, target departure and unload.
4. Expand known immediate controls after stale-request, harvest-completion and source-local race tests. Leave delayed private actions, mobile transit and combat transfers out until their missing evidence is solved.

This adds work during real handoffs and observed eligible updates, not a new per-physics all-object scan. The chest bridge adds native requests/replies and potentially two owner changes; no live CPU or latency saving is claimed. Copying a shadow AI or animator for all remote mobs would add work without certifying the actual owner's decision and is not the recommended way to expand this lightweight fallback.

## Source evidence

- Standalone: `src/AuthorityController.cs` (registration and both-endpoint gates), `NativeOwnership.cs` (current narrow native set), `Handoffs.cs` (NativeSource/NativeTarget phases), `RecipientReadiness.cs`, `RequestRouter.cs`, `NativeLatency.cs`, `LocalAgent.cs`, `MobPolicy.cs`, `TextEditingProtection.cs`.
- Server Side 0.13.1: `../TooManyLagsDedicatedAddon/src/NativeInventoryHandoff.cs`, `InventoryOwnership.cs`, `NativeFlow.cs`, `Handoffs.cs`, `RefillProtection.cs`, `DynamicObjects.cs`, `ShipOwnership.cs`, `CartOwnership.cs`, `MobOwnership.cs`, `MobAttackForecast.cs` and `docs/SERVER-ONLY-SCOPE.md`.
- Native reference: `../InteractionTypesAudit-2026-10-14/sources/Container.cs`, `Incinerator.cs`, `Fermenter.cs`, `Sign.cs`, `ShipControlls.cs`, `Vagon.cs`; `../HostileMobServerAudit-2026-10-18/ZNetScene.cs`, `Character.cs`, `Humanoid.cs`, `BaseAI.cs`, `ZSyncAnimation.cs`.
- Earlier assessment: `../StandaloneServerOnlyAudit-2026-10-15/REPORT.md`. The new Server Side implementation now provides a concrete native-container transaction and event-driven reference, but does not remove the source/recipient observability boundaries identified there.

Source-level feasibility only. No new native fallback, live vanilla-client transaction, transport ordering or multiplayer behavior was tested in this assessment. No version bump, publication or push.
