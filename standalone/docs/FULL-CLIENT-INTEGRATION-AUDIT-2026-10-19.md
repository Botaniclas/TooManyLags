# Standalone full-client integration audit

Scope: matching Standalone installs on the host/server and both owner clients, without FiresGhettoNetworking or Server-Side Simulation. This is source review and automated verification, not a live Unity multiplayer certification.

## Coordination without simulation

The dedicated coordinator reads replicated ZDOs, prefab metadata and connection information. Source-owner clients inspect their actual active actions; receiving clients prove their own object/area readiness. No server-owned intermediate simulation is introduced. The dedicated session is excluded as an ownership destination. A local listen host is a valid player destination and supplies its own client half.

| Feature | Integrated path and retained behavior | Evidence |
|---|---|---|
| Directional ownership grid | Indexed native regions, movement/heading changes, camera overlap/prediction, inner/forward/outer delays and overlap-only hold. No camera data uses character heading. | Production grid/camera fixtures, controller/publisher tests and region/cache retirement cases. |
| Source-confirmed transfers | Recipient preparation, source-local use veto, final owner/state receipts and recipient activation. Inline listen-host replies cannot cancel a replacement job through an old snapshot. | 6,445 production handoff assertions on CLR and game Mono; exact job-identity regression. |
| Inventories and delayed actions | Opening/loading/open inventories, incinerator burning, one-second renewed refills, pending cart attachment, native fermenter/boss-altar callbacks and active use block source commits. | Runtime fixtures plus installed-game private-field/callback IL checks. No fake chest opening or server-scene substitute. |
| Sign and portal text | Actual editor receives ownership before the native GUI opens; only that object remains pinned until movement. Discarded takeovers need fresh grid activity. | 118 lifecycle/Harmony and 14 real controller assertions, each on CLR and game Mono. |
| Ships, carts and tamed animals | Native controls, passenger ownership, inventories, pending cart use, riding/following and relevant combat remain protected. Empty ships can transfer. | Pending-source runtime tests and native vehicle method/field review. |
| Hostile Mob target ownership | Source reports actual target; target player normally owns it. A nearby better-connected player can replace a high-ping owner. Source Attack/charging veto and recipient target adoption remain active. | 40 production policy/source/transaction assertions on CLR and game Mono; dedicated fixture rejects live scene reads. Actual target instrumentation audited separately. |
| Settings and transport boundaries | Host settings govern clients; admins control live settings. Existing capability checks, optional-client restrictions, sender identity and bounded cleanup remain. | 52 network and 56 session/config assertions; native socket/API signatures. |
| Lifecycle and cost | Disconnect, object/region retirement and shutdown remove state. Stationary coverage avoids repeated object work; action checks run for pending transfers. | Grid/cache, text and handoff cleanup regressions; production IL rejects per-Tick captured-time allocation. No measured CPU/FPS claim. |

## Confirmed ship conflict and minimal fix

Vanilla `Ship.UpdateOwner` transfers a ship back to a passenger if its local owner is on shore. A grid handoff to a shore player could therefore be undone repeatedly. `ShipTransferGuard` reads the source's existing passenger list and rejects that destination while valid passengers remain aboard. Boarding the target, or the last passenger leaving, allows a new prepared transfer. No additional Harmony hook, permanent passenger observer or server simulation is needed.

The cached guard runs only while a ship transfer is pending, using the existing 0.1-second retry cadence. It does not take over native rudder/driver behavior.

## Native exceptions and limits

Friendly summons, bosses, static walls/floors and loose item drops intentionally retain native ownership in this edition. Missing/incompatible client capabilities keep their documented narrower fallback. Unknown mod-specific private actions and competing direct owner writers are not certified automatically.

The native audit checks 165 assertions, 28 exact Harmony targets, private field/delegate signatures and 14 actual MonsterAI target stores across five methods. This validates the installed assembly contract, not live Harmony installation, real transport ordering, Unity/PhysX behavior or custom-mod compatibility. Real multiplayer testing on both hosting modes remains necessary.
