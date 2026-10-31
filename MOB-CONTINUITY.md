# Source-initiated handoff: local 1.0.86 candidate

Based on verified 1.0.85. Local debug build only; no publication, profile installation or Git push.

## Immediate source cutoff

There is no per-mob permission cache or server policy approval. At the completed AI/damage boundary, or attack-end retry, the current owner selects the actual player target's owner. An active attack still completes before capture. The source sends an authenticated Begin notification, existing flight/combat/effect checkpoints, writes the native owner and immediately sends a complete native ZDOData record. The server checks the actual source connection, object/prefab/epoch and next owner record, then relays the native final record to the destination. These are protocol/identity checks, not a ping/boss/loading decision. No return request or final acknowledgement delays the source.

The new owner keeps native simulation/physics guards. Skipping readiness approval can therefore expose a destination's loading pause. Server ping/boss policy still runs subsequently and can correct the initial target-owner choice. Player-only mode retains its existing ownership path. Older source/server endpoints retain the existing path; all destinations need this version for the new checkpoint protocol.

Final native pose is flushed for both ground and flying actors. Native serializers/compression remain in effect. Immediate publication is one object; it never drains unrelated forced updates. Ordinary native scheduling remains a fallback and may later duplicate the record. Postcommit send failures never roll back owner revisions, replay effects or resume the old authority.

## Shared presentation

The initial 74-byte descriptor identifies a committed handoff and its final native data revision. The server fans it out only to nearby capable spectators, excluding source and destination. Receivers wait for the matching owner/data epoch; newer native state suppresses stale descriptors.

While its visual bridge still awaits real new-owner motion, the old owner may send a separate 40-byte position/yaw preview at most every 50ms. The server validates the original source, epoch, monotonic frame and short lifetime, then relays only to spectators. These frames never enter ZDOData, set owners, run AI/effects or move a collider. The new owner does not receive them. Spectators softly interpolate a bounded render offset on top of their native root; genuine new-owner data starts local recovery and retires preview input. Tracks/queues are bounded and clear on world replacement/shutdown. Missing frames fall back to local prediction and finite fade.

This is additional temporary traffic, even when its cadence resembles ordinary world updates. No claim of zero cost or measured bandwidth savings. There is no preview stream with the visual option disabled or without an active source bridge.

## Gradual target compensation and native continuity

Capture begins with the observed target position: zero future compensation. During preview/recovery a smooth 0-to-1 weight progressively adds remote native velocity times the bounded route delay. A presentation-only timing reply uses half the source RTT plus half the destination RTT for the target horizon; the complete return path uses their RTT sum. A five-second, session-local cache per destination starts later bridges from that measurement without extra polling. Cold or expired entries use the measured source-server RTT as an estimate. Authority never waits for timing. Fresh timing blends into the preview window and target horizon over a short local easing period; a lower RTT cannot rewind the preview, restart its lifetime or extend recovery after native state arrives. The preview uses at most 500ms. Incoming player-position changes are filtered and yaw goals turn continuously; real player transforms never change. Implausible velocities are rejected and local/owned players are never extrapolated.

The source's target and remaining MonsterAI.m_updateTargetTimer are carried in the common checkpoint and applied before the first new-owner AI pass. Native searches normally recur after two seconds with players nearby, six otherwise. Copying the remaining timer prevents the new replica's unrelated/random timer from immediately searching again. This is not a new blanket target lock: ordinary death/enemy/lava/forgetting rules remain.

Grounded Seekers no longer use the species random-flight flag as an active-flight test. The real handoff also carries Character.currentRotSpeedFactor, used by native flying rotation. Signed yaw/recovery and analytic native turn-factor easing avoid the reproduced brief reverse. Existing active-effect migration and late hit/effect/heal routing remain unchanged; only the real owner ticks effects.

The preview remains a locomotion approximation, not an execution of MonsterAI/PhysX. Collision, random decisions, new attacks and genuinely different target inputs can still require correction. Live two-player target changes with a third spectator, Seeker flight/landing and combat/effect tests remain necessary.


---

# Historical baseline: local 1.0.85 candidate

Based on the hash-verified 1.0.84 continuity candidate. This version replaces the blanket active-effect hold with runtime-state migration. The independent optimistic object-handoff/inventory branches are not merged. No installed profiles or published packages are changed.

## Presentation

The previous bridge extrapolated a captured velocity; recovery was capped at 200ms. The new forecast analytically integrates velocity towards the known movement intent, so a short dash brakes instead of persisting. A fresh target invalidates the previous movement intent: the forecast brakes and faces the new target rather than assuming the old path remains valid.

This is a local, read-only locomotion approximation, **not a second execution of MonsterAI or PhysX**. Native AI can choose different paths, attacks and random actions, and local world inputs need not match. Running it twice could duplicate gameplay events. The bridge touches only a collider-free visual subtree while cameras render; roots, physics, authority, animation clocks and damage stay native.

Each handoff uses its own RTT, clamped to 40–500ms. Higher actual RTT cannot extend the prediction window. Recovery grows with that window and the small remaining lead, capped at 740ms; the ordinary total is at most 1.24s. A short frame stall cannot extend the prediction deadline. A new attack, death, owner change, unsupported model or teleport ends the bridge.

Normal native movement continues during recovery. The remaining lead eases away as slightly slower motion rather than being consumed into a stop or a catch-up sprint. Excess position corrections are not interpreted as velocity. A bounded forecast cannot hide every real correction or an arbitrary pause/lost update. The render lead remains capped at 25cm. Flight retains native height and its existing transient flight checkpoint.

## Trigger and policy

The source observes the real target assignment and sends at the end of the same AI/damage invocation. There is no extra grid/two-second scan wait on that path. The server still selects the eligible owner and sends its authenticated commit back to the source; no unilateral client ownership or additional final acknowledgement is introduced.

TML's reported live player target always uses its guarded Request transaction, including at ordinary ping. That path bypasses FiresGhettoNetworking's recent-hit, combat/ownership-hold and rejection-cooldown waits. Unmanaged/native fallback retains its own policy. TML's attack-end/stagger/active-use checks, candidate eligibility, boss/high-ping decisions, source revision validation and per-owner/global caps remain.

## Combat cutoff

Updated endpoints send one transient checkpoint immediately before the final source owner write. It preserves residual push force, partial stagger, remaining backstab protection and the last HitData for attribution. Native walking/swimming acceleration and turn-animation accumulators add 20 bytes; push remains separate, never added twice. Native Character.UpdateWalking uses these accumulators independently of the body velocity restored by ZSyncTransform. Health remains native ZDO state. A processed hit is never replayed. The checkpoint is applied once at the matching next owner revision, before new-owner fixed updates or owner-directed combat handlers.

Checkpoint v5 also carries the source cutoff root position, rotation and velocity.
They are applied once at owner gain. Pose equality is not allowed to skip the
velocity field: the first paired owner clock must carry the source tangent so
final smoothing does not converge to a stopped server replica. Checkpoint
versions 1-4 fail closed before native state decode.

Native `ZSyncTransform.OwnerSync` can run after checkpoint apply and overwrite
the root/velocity from the delayed ZDO. When `m_wasOwner` is still false, keep
one bounded owner-pose copy and restore it in the post-`OwnerSync` finalizer,
immediately before any visual ownership rebase. The copy is consumed once and
does not run per frame.

When no fake track exists, owner gain keeps the authoritative root correction
but offsets only the visual subtree from the pre-owner replica pose to zero.
The offset follows the moving local root and never pauses or rewrites local AI,
physics or hit ownership. Its first sample is applied synchronously at owner
gain, not deferred to the next camera callback.

For every non-initial owner revision, bridge source/checkpoint/fake/observer
paths accept any live Character target. The destination no longer has to equal
the target. Initial owner creation is excluded. Existing policy deciding
whether to request an ownership transfer remains unchanged.

Final smoothing follows the live native root every frame while its stored
relative position/rotation correction decays. Its normal window is the fake
simulation horizon. A measured owner-ready floor covers the next physics step,
owner update interval and render/send frame when latency is short. Large residual
may use up to twice the simulation horizon, capped at `500 ms`; one render frame
is always retained.

| Situation | Treatment |
|---|---|
| Damage already processed | Native health plus transient combat checkpoint; no damage replay. |
| Late unprocessed damage | Bounded, deduplicated forwarding to the current owner. |
| Separate late status-effect request | Forward once; native new-owner handler adds/resets it. |
| Already active status effect | Transfer elapsed time, budgets and tick phase; do not restart or replay it. |
| Late parry/stagger or heal | Preserve native arguments and execute once at the current owner. Broadcast presentation remains native. |

Effect state travels in the existing transient checkpoint. The source remains responsible until the native owner write succeeds, then detaches its local effect manager without calling Stop/OnDestroy. The destination clones the matching ObjectDB template and restores fields before its first owner update or combat handler. Setup, ResetTime, SetLevel, initial healing, spawns and processed damage are not repeated. Poison/fire retain unpaid damage and tick phase; shields retain consumed protection; other native scalar/struct state follows the same schema.

Existing network FX, harpoon references and the demister ball are identified by ZDO ID and adopted instead of recreated. Missing scene instances resolve through existing object arrival/world-update events. Repeated handoffs retain unresolved IDs; expired effects retain bounded cleanup records for late FX. Native destruction notifications distinguish genuinely deleted references from ordinary unloading, including deletion before checkpoint adoption. Broken harpoons still terminate immediately. Distinct destination-local FX are retired without invoking effect lifecycle rewards; incoming or foreign-owned network FX are preserved. Finder's local beacon can rebind through native behavior. No attempt is made to serialize an arbitrary custom object graph: a changed unsupported reference, missing template or incompatible schema is reported rather than silently dropped. Source-detectable unsupported state refuses the owner write. Matching effect templates and updated endpoints are required.

The new protocol negotiates capabilities, validates source/object/prefab/revisions/world, bounds queues and expires stale routes after five seconds. A returned event cannot bounce on an unchanged ownership revision. Unsupported/mixed endpoints retain native fallback, so complete transient protection requires updating server and clients together. RPC expiry, missing endpoints, arbitrary modded combat RPCs and unrelated AI-private state are outside a guarantee of lossless migration.

## Cost and validation scope

Visual work exists only for up to 32 active old-owner tracks and sends no new packets. Combat/effect state is sent at a real handoff; late-event envelopes are used only for the rare stale-request case. Effectless handoffs add one presence flag. Effects add their bounded field/reference payload, within the complete checkpoint's 16 KiB limit; no per-tick effect stream or extra acknowledgement is added. Cached field delegates avoid rediscovering schemas on every handoff. No steady full-world effect scan or duplicate gameplay simulation is added. Unresolved references and cleanup are capped, with empty fast paths and world-reset cleanup. Real ZDO deletions add a small FIFO record (10 seconds, at most 2,048 IDs); expiration is handled on deletion/reference access, without a timer loop. Existing FX arrays are reused while resolving late references. Normal damage without pending handoffs takes the count-check fast path. Sent/delivered event records remain for deduplication but are not scanned on each world packet; only unresolved server events re-enter that work queue. Debug test commands remain compile-time optional for explicit publication builds.

Validation executes the production forecast, effect codec and routing code in managed fixtures, tests ordered/delayed/repeated ownership events, and checks native field/method/Harmony contracts on the installed Valheim/FGN assemblies. Native status-field delegates and codecs run under Valheim Mono without rewriting native assemblies. Numerical trajectories and fixture effects do not execute a live Unity world. Actual multiplayer motion, effect visuals, collision and visual quality require the in-game comparison test.
