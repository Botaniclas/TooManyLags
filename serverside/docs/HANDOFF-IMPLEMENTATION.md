# Handoff implementation - local 0.5.0 candidate

The server-only runtime now implements the direction-specific experimental design. This supersedes the earlier proposal to wait for a forced native send with a separate request queue.

## One-object ordered routing

`NativeSnapshot.Send` constructs Valheim's native `ZDOData` envelope for exactly one supported object. The payload is produced by the real `ZDO.Serialize`; ownership/data revisions, position and identity remain native. It sends through the recipient's existing `ZRpc`, then forwards the original command through that same connection. Original sender, message identity and arguments are preserved; original stale delivery is suppressed.

There is no background request queue, retry/replay, full-region `SendZDOs` pass, force-send acknowledgement assumption or persistent send-history cache. Only the stale exception adds an object snapshot. The normal native sync pass can later send a redundant snapshot because its bookkeeping was not forged; this is preferable to adding private peer-cache mutations for the rare path.

A startup IL contract check validates the actual native receiver's typed read sequence and the PickableItem completion field. Unsupported contracts disable only this addon. This is a structural check, not a version-number gate.

Native Steam reliable messages on the same lane preserve sending order. This supports state-before-request in the inspected native path; a loaded scene is still required for native object RPC dispatch. It is not an acknowledgement from an unmodified client and does not certify arbitrary crossplay/network modifications.

## Cancellable source takeovers

Only actual player-to-server changes for direct-state groups create a pending job. The old owner remains installed. The shared indexed heap removes cancelled entries immediately, without stale timer tombstones or one coroutine per object. Native per-peer RTT is cached for one second; an invalid/zero telemetry reading does not permit takeover when a ping-based window is requested.

`RPC_ZDOData` supplies the source context only while pending work exists. A `ZDO.Deserialize` postfix cancels a matching pending source transfer after normal data processing. This intentionally accepts any applied source-object update rather than trying to reconstruct an exact honey/insert event from potentially coalesced snapshots. Ordinary production updates can therefore cancel a pending takeover too.

Cancellation deletes the timer, dismisses old scheduled work and places that object in the existing per-cell fresh-activity wait set. Time passing or activity ending does not requeue it. A new relevant movement/heading request can try again. Deadline servicing revalidates object identity, ownership revision, current grid target and native guards once; it is independent of the movement interval.

No player-to-server grace is added solely for the verified Pickable picked-completion contract or the PickableItem destruction contract. Already completed pickups are not forwarded for execution again. These claims are scoped to the inspected native contracts and remain separate from live scene/packet validation.

## Local controls and texts

Seats, crafting-menu controls and local markers are not added to grid buckets. Within an active player area, their initial server-side registration selects a server default. Native region release is retained; no periodic ownership-enforcement loop is installed.

Signs and TeleportWorld portals remain completely unclassified, even on mixed identities. Sign submission claims ownership locally before writing text/author fields; the addon does not reclaim that identity. Portal submission resolves its native owner and calls `RPC_SetTag`; the addon introduces no grid transfer during editing. No editor-start event or idle permission is inferred, and no synthetic text RPC/open/lock is used.

Containers, item/armour stands, vehicles, deferred output/burning/spawning and unknown controls remain outside the new short-action path. Native/fork permissions and actual user grants remain authoritative.

## Mobile objects and native priority

Cold prefab classification admits ordinary tameable MonsterAI animals, including native saddle controls. Only the replicated `s_tamed` flag activates grid management. `s_user`, follow name, attachment/parent connection, alert and target-present state suspend it. Friendly magic summons with the player faction are excluded. The server does not query private target pointers on another owner: vanilla replicates only target presence, not its identity.

Position changes enqueue only indexed mobile nodes that crossed a grid cell. State setters and completed deserialization compare cached eligibility flags. Actual native owner changes invalidate only the affected loaded managed node. Work is deduplicated until the next player/controller pass; no ownership write runs midway through packet deserialization. Zero-owner region release withdraws that node until native reactivation. This also closes the settled owner-drift gap found in the door audit.

Riding requests immediately withdraw the animal from grid work. Native `RequestRespons` messages keep their recipient, parameters and ownership grant. Parameters are inspected only for a matching pending animal/requester pair; other replies are left unparsed. Outstanding requesters are tracked only for an actual control request, so one rejected rider cannot clear another rider's accepted grant. An observed user-to-zero transition resumes grid participation. If grant completion cannot be observed, native handling remains until a clear release or unload; there is no guessed grant deadline.

Loose items and fish allow early server-to-client delegation, but client ownership is then left to vanilla. Local pickup can start without a routed request; forcing a later grid transfer would race native pickup. A real `RPC_RequestOwn`, `RequestPickup` or fishing action similarly pins the loaded object even while the owner is still the server. The fish `Pickup` response and rider `RequestRespons` are never retargeted as object-owner requests. Fish/ItemDrop mixed identities use one pickup policy. Pins disappear with unload/destruction, while native pickup grants, auto-stacking and fishing remain active.

Only actual mobile player-to-server attempts additionally call native `ZNetScene.IsAreaReady`; results are cached per native zone for the current pass. A blocked attempt waits for fresh relevant grid/mobile activity, not a repeated area scan. This proves native instances exist, not PUP/client collider readiness.

## Static buildings

Eligible stationary noninteractive Piece/WearNTear identities use a server default. With `UseGridForStaticBuildings=false`, they have no grid bucket: registration and actual foreign ownership changes create only one affected-object assignment job through the existing protection/scheduler. Enabling the setting opts them into normal grid decisions. Native zero-owner releases remain intact. Source updates can cancel protected return to the server; a later relevant event is required to retry.

## Backend policy boundaries

The dependency's PortalLock and GuardianStones serialization policies are not bypassed: portals, stands and boss stones are excluded from management/routing. By default, an active FireControl ownership postfix supplies its real `IsSource` predicate at cold prefab classification; these sources stay native.

`RespectBackendFireOwnership=false` disables that catalog exclusion and replaces only the `IsSource` call inside FireControl's `TakeOver` pass with a narrow filter. The original predicate still runs; actual Fire objects remain native. Only enabled, fully eligible refill/fireplace prefabs are left to the existing grid and grace logic. Raw fires, unsupported/mixed controls, disabled refill categories and inactive addon worlds preserve the original predicate result. No second owner write, network request, world scan or background enforcement loop is added. Harmony rollback restores the original dependency method. A changed call-site contract rejects the optional patch. Discovery is at startup; restart after either mod's feature-mode changes.

This override deliberately allows client simulation: a server-only fire-control module cannot enforce all of its ignition/spark rules on a player-owned source. Its other patches are kept installed, but their reach does not extend to unmodified clients.

The targeted payload uses native `ZDO.Serialize` without the dependency's per-peer `SendZDOs` wrappers. This is acceptable for the specifically inspected eligible components; unknown future per-peer serializers require another compatibility review, not a universal compatibility claim.

## Grid timing and movement

The default is server simulation outside active player fields, with `KeepOwnershipOnCellExit=false`. The server-only controller explicitly selects the full rear shape in both retention modes. The red 3x3 and orange forward row remain; yellow cells are filtered when the masks are built unless enabled. With yellow disabled, the two rounded patterns both have twelve active cells per heading. Selecting a rounded size still determines the optional outer coverage.

The existing player pass reads native `s_velHash`, compares horizontal squared speed, and selects an already-built fast mask. Activation is 4 m/s and release below 3 m/s by default. Only forward cells acquire a zero-delay tier; other timers, request ages and overlap membership remain intact. No velocity history, movement coroutine or additional RPC is added. Uniform mode, an unexpanded square 3x3, an already-zero forward delay or a disabled boost skip the velocity read.

Adaptive directional coverage selects a further prebuilt mask when native RTT reaches `LargeGridPingThresholdMs` (default 200). The cached telemetry is shared with takeover grace, read at most once per second per valid socket. A 75% release threshold avoids size jitter; unknown telemetry retains the active size. Cardinal masks have sixteen red cells in rows 3/5/5/3 and three orange ahead. Diagonal masks union neighboring cardinal red coverage, fill its forward tip and add the next three-cell diagonal row: twenty red plus three orange. Optional yellow stays available. Speed can promote the large orange row too; this includes an expanded square-pattern user, whereas an unexpanded square has no forward row. Full-grid mode stays explicit and does not query adaptive ping.

## Observed refill protection

Only eligible enabled refill nodes allocate a native state snapshot. Fuel, queue and ammo counts plus empty/nonempty cooking slots are captured at registration. Normal completed native deserialization compares only those fields. A local server refill handler refreshes its baseline immediately before execution and checks actual changes afterward, avoiding false negatives after unobserved server-side production. The seven handlers are Smelter add-ore/add-fuel, CookingStation add-item/add-fuel, Fireplace add-fuel, ShieldGenerator add-fuel and Turret add-ammo. There is no global numeric setter patch; automatic ShieldGenerator set-fuel does not receive a new observer hook.

An observed increase sets `Until = now + RefillOwnershipProtectionSeconds` (default one second), cancels that object's pending grace, dismisses pending grid work and waits for fresh relevant activity. Another real observed increase restarts the window. Actual ownership mismatches test that timestamp before transfer; already-correct owners exit first. Expiry does not schedule or replay anything. Fuel consumption, queue removal, ammo firing and occupied-slot cooking conversions update the snapshot without renewing the lock. Disabled protection allocates no snapshot and skips native handler capture.

This is observed activity, not an old-client cutoff: remote local inserts become visible only after native transmission, and a coalesced remove/consume plus insert can hide the change. Existing source-data grace remains active. Native disconnect/release policy is not blocked by this addon. Whole-inventory and private deferred-action identities remain excluded; their exact native boundaries are documented in `NATIVE-ACTION-GUARDS-2026-10-17.md`.

Unchanged speed mode does not renew field requests. A threshold crossing uses the same incremental mask delta as a cell/heading change; currently pending forward requests can mature in that pass. Native-use and grace checks remain in `Apply`. Shared server-owned cells also remain subject to overlap cooldown; the dedicated server is a valid incumbent even though it is not a player-list entry. Expiry alone does not request an ownership change.

## Remaining evidence gap

An RTT-derived grace window catches source updates received before its deadline, not arbitrary late local work. It does not establish an old-owner cutoff/final-state acknowledgement. This limitation is explicit; no finished client-assisted handoff protocol was recreated. The candidate needs multiplayer tests of the two relevant directions with one interactor, native pickup completion, lost/late transport, initial loading and dependency feature combinations. Inventories and text editor start were not silently broadened into the protocol.

Official ordering reference: https://partner.steamgames.com/doc/api/ISteamNetworkingSockets#ReceiveMessagesOnConnection.
