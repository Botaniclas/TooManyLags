# Object-specific sign and portal editing protection

Status: implemented in the unreleased working tree. Update server and clients; older servers keep their native text UI. Managed fixture and native metadata results are recorded in VALIDATION.md; no live multiplayer claim.

## Flow

1. Intercept native `TextInput.RequestText` only for exact `Sign` and `TeleportWorld` receivers. Their native interaction/access checks have already run. Preserve receiver, topic, character limit and native saving.
2. Arm the actual editor's local source veto before sending an edit intent. The server authenticates the connection, actor, object identity, health, proximity and enabled object category. Concurrent editors cannot replace the first session.
3. Request ownership for this object through the existing indexed grid and source-consenting handoff. Do not force `SetOwner`, probe an inventory or cancel an in-flight commit. The server waits for the existing transaction to settle before granting editing.
4. Open the native UI only after the grant, exact native local ownership revision and required data-state revision arrive. Native sign `ClaimOwnership` is then a no-op; portal saving uses the unchanged native path. Disabled categories and unsupported interfaces remain native.
5. Keep the object's direct cached hold flag until actual position changes by more than 0.1 metres from its edit anchor. This filters idle noise and works inside a cell. Confirming/cancelling text or rotating the camera alone does not release the hold.
6. Reject new automatic ownership work for this object without a retry lease. Release clears the hold and discards its queued grid work. Only a fresh covering movement/heading request can initiate another grid takeover. Existing transactions that have already been sent still follow their ordinary fencing/completion rules.

## Cost and lifetime

- No new Unity component or physics callback. The existing plugin update processes incomplete openings; only clients with an edit session check their actual local movement every 0.05 seconds. Active server locks have no movement polling.
- Ordinary object decisions read cached node fields, not an edit-session dictionary. Client source commits gain a nullable veto only while that client has an edit intent/session.
- An authenticated release can remove only the sender's own session. Using local movement avoids false releases caused by delayed replicated server positions. No cell/world scan finds active editors, and standing server edits create no background work.
- Only actual editing sends begin/grant/release messages. Incomplete openings have an eight-second deadline; active writing has no time limit.
- Limit sessions to 64 per peer and 768 overall. Immutable object IDs, exact data/connection identity and monotonic per-connection tokens reject stale messages, including pooled data reuse. Unloading, destruction, death, external ownership loss, disconnection and world shutdown retire the applicable state.
- A cancelled pending opening cannot overwrite a newer native text window. Moving during an open protected editor also closes that specific native input safely; unrelated text inputs are left alone.
- Both Server-Side Simulation and player-only simulation reuse their existing handoff implementations. This feature neither duplicates a transfer protocol nor writes ownership itself.

## Coverage

Production-module Harmony fixtures cover both modes, signs/portals, delayed ownership receipts, long stationary writing, camera-equivalent unchanged positions, movement within a cell, competing editors, disabled categories, other text windows, old-server fallback, stale packets and object/peer/world cleanup. Controller/source regressions verify rejection without idle retries or late replay. Actual installed Valheim metadata verifies text UI and lifecycle signatures and checks the compiled production veto paths.

The native text-submit hook rechecks the same local owner/identity immediately before saving. Existing ownership/destruction notifications retire server holds; no additional global ZDO ownership/reset Harmony hook is installed. Held player-mode nodes survive loss of shared-region contention and are retired on release if ordinary coverage no longer needs them.
