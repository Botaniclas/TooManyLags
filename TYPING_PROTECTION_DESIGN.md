# Object-specific sign and portal editing protection

Status: source-verified design only; not implemented in 1.0.17.

## Required behavior

- Lock only the edited object's ownership, not its cell or nearby objects.
- Keep the lock until the editing player actually moves. Closing or confirming the text box alone does not release it.
- Discard fresh ownership requests while locked. Movement removes the lock without replaying those requests or creating a return job.
- Destruction, unloading, disconnect and world reset remove the applicable lock. An offline owner must not strand the object.
- Preserve native access/privilege checks, text limits and text-writing behavior.

## Actual vanilla behavior

Both Sign.Interact and TeleportWorld.Interact call TextInput.RequestText locally. The server sees neither start event. TeleportWorld.Interact can even return true after denying private-area access, so its return value is not sufficient proof that editing began. Hook the actual RequestText call for these two verified TextReceiver types after their native interaction checks.

Sign.SetText calls ZNetView.ClaimOwnership before directly writing text/author fields. TeleportWorld.SetText sends RPC_SetTag; its handler writes only on the owner. Locking every sign ownership change without preparing its editor would interfere with native saving. A sign editor must therefore own the sign before the editing lock/UI is established; the subsequent native ClaimOwnership then remains a no-op. A portal may keep its existing owner.

## Minimal safe event path

1. A client starting native text editing registers an object-specific edit intent. If it already owns a sign, the local source guard can immediately reject new source commits without delaying the UI. Otherwise sign ownership must be prepared safely before opening the UI. A server-owned portal likewise needs a server-confirmed lock before editing begins if no ownership race is to remain.
2. The server validates the connected requester, real loaded object/type, actor identity/proximity and current owner tuple. One edit session stores the editor and anchor position on the existing object entry. Repeated requests from the same session do not allocate new entries or refresh a timer. Other editors receive an explicit busy response.
3. Every new TML ownership decision for this object checks that flag and drops the attempted change. No field-wide suppression occurs. Already sent commits cannot be treated as cancelled: they must be resolved/fenced before granting an edit lock. Inventory/source protections remain active.
4. Use the existing player movement path while edit sessions exist. Release on genuine movement, not camera rotation, a cell boundary or idle physics jitter. Native replicated position can be checked using already-read player coordinates; a local movement event can release the source guard immediately. No dedicated Update/FixedUpdate scan or per-object distance polling is needed. One player's movement clears that player's edit sessions, without scheduling an ownership update.
5. Cleanup follows existing instance, connection and world lifecycle events. Reject stale replies using connection/session identity. Late begin/release messages must not affect a replacement object or a newer edit session.

Ignoring the server's start acknowledgement in the server-owned case leaves the same in-flight ordering hole as a one-second lease. A normal interaction packet does not carry a text-edit begin event or the current owner/data revision. Optional/unmodified clients cannot provide that event, so strict text-edit exclusion cannot be silently promised for them.

Player-simulation mode needs the same local edit-source veto and object-specific coordinator rejection. It must not rely on Server-Side Simulation or bypass its existing handoff fencing. The shared edit state should be reused rather than installing competing text/UI hooks.

## Performance expectation

Only actual text-edit sessions send start/grant and movement-release messages. No text-edit cache work exists per ordinary ownership change beyond checking a flag on the object already being managed. Use the existing player/connection index; do not scan cells, objects, renderers or all text inputs. Marking an attempted transfer as simply rejected must remove its ownership job; returning Pending would unnecessarily retry it while the player writes. Source transactions already in flight remain a separate, necessary completion path.

Required tests: two-player editing in shared fields; server-owned portal begin races; sign ownership preparation and native saving; long stationary typing; movement within a cell; camera turns without movement; concurrent editors; denied access; destruction/unload/disconnect; late session messages; and both simulation modes. No such live tests have been performed.
