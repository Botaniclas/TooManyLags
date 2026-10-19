# Hostile Mob observation without a client mod

Read-only review, 2026-10-18. No runtime, config, dependency or package changes.

Inspected the installed `assembly_valheim.dll` directly with ILSpy, SHA256 `94998885B103D6E5F5480123EAEE0A093E82AF2DDEEEE37536C15BF555F24F3A`. Decompiled evidence is in sibling workspace `HostileMobServerAudit-2026-10-18`. Also inspected released Dedicated Simulation 1.18.0, SHA256 `51D838EA1EDF8ED27E7F0BEA6FE39D9761B5A1F71DB599BB2EE196B83A325650`, rather than relying only on current upstream source.

| Information | What a server can observe while a client owns the mob |
|---|---|
| Exact current target | `MonsterAI.GetTargetCreature` returns its local `m_targetCreature`. `BaseAI.UpdateAI` returns false for non-owners, so the server's AI does not follow the owner's target decisions. `BaseAI.SetTargetInfo(ZDOID)` discards the identity and writes only Boolean `ZDOVars.s_haveTargetHash`. Reflection cannot recover a client-local reference; a surviving server reference can be stale. |
| Target exists | `BaseAI.HaveTarget` reads that replicated Boolean. It says neither which creature nor whether the target is a player. MonsterAI's separate static building target is not identified by this field. |
| Alerted | `ZDOVars.s_alert` is replicated. Non-owner `BaseAI.UpdateAI` copies it to `m_alerted`; reading the ZDO directly avoids depending on that local update. Alerted does not mean an attack is currently running. |
| Attack start | `Attack.Start` uses its configured attack-animation name, including random/chain suffixes, with `ZSyncAnimation.SetTrigger`. That method broadcasts the native `SetTrigger` RPC. Its routed envelope identifies the mob ZDO; the string is an animation trigger, with no target identity. A server-side route observer can see the existing packet without an extra client protocol or recurring mob poll. Not every trigger means an attack: names must be matched to the actual attack definitions. |
| Attack in progress | `Humanoid.InAttack` checks current/next local Animator state tags through Character's frame cache. ZSyncAnimation delivers triggers and parameters to local copies; this is a local animation reconstruction, not a replicated authoritative attack-active flag or acknowledgement. Trigger receipt and local playback can differ in timing; short events are not a durable state snapshot for late-loaded copies. Do not promise exact owner-side start/end or safe mid-attack ownership migration from this alone. |
| Targeted-player notification | `MonsterAI.UpdateTarget` calls target Player.OnTargeted. Its RPC has two booleans, a target-player ZDO and the sending peer ID, but no attacking-mob ZDO. Several mobs can share that peer. Correlating it with a nearby attack trigger cannot prove the mob-to-target relationship. |

Released Dedicated Simulation exposes a Humanoid.UpdateAttack prefix clearing an invalid local attack object; it adds no exact target replication protocol. No target-query RPC was found in native BaseAI/MonsterAI registration. Server ownership permits reading that server's own live AI target; after delegation to a client, subsequent target changes are again private to that owner.

Conclusion: native alert/target-presence and attack-start events can support an event-driven combat heuristic. They cannot reproduce exact target-based ownership across multiple client-owned mobs. Exact ongoing target ownership still needs owner cooperation or retaining server AI ownership. This review does not establish headless Animator playback accuracy, live multiplayer handoff safety or measured performance gains.

## Native pauses as a handoff opportunity

Follow-up inspection confirms MonsterAI.UpdateAI checks both `Character.GetTimeSinceLastAttack() >= m_minAttackInterval` and per-weapon `Time.time - item.m_lastAttackTime > m_aiAttackInterval`. Humanoid.UpdateAttack resets its private `m_timeSinceLastAttack` while InAttack and increments it outside the animation. These are different clocks: the minimum interval can describe recovery after the attack animation, while the per-weapon interval uses the weapon's start timestamp. Humanoid.CustomFixedUpdate services UpdateAttack only on the owner. Neither owner-side clock is a replicated exact remaining-cooldown value.

A verified positive recovery period could provide a practical opportunity to transfer early after attack completion, rather than imposing a fixed wait after every ownership request. Its remaining duration must exceed uncertainty in observing the end plus delivery/application of the owner change and a margin. A merely typical 0.5–1 second pause, or RTT alone, is not proof of that bound; late animation playback and network stalls can move the attempted handoff into the next attack. No universal positive minimum across vanilla/modded prefabs or live timing bound was verified. No pause-based runtime change was installed.

## Follow-up: practical server-only grid design

The native asset inspection script and `native-mob-asset-contracts.json` in the sibling audit directory read actual installed prefabs and referenced default/random weapons from bundle `c4210710`. All referenced items in this sample resolved. Examples:

| Prefab | Global recovery after attack animation | Individual attack intervals, counted from their starts |
|---|---|---|
| Lox | 2 seconds | Bite 4 seconds; stomp 12 seconds |
| Asksvin | 2 seconds | Bite/headbutt 8 seconds; pounce 4 seconds |
| Eikthyr | 2 seconds | Antler 5 seconds; charge 25 seconds; stomp 40 seconds |
| Greydwarf | 0 seconds | Melee 2 seconds; throw 8 seconds |
| Wolf | 0 seconds | Three separate attacks, each 5 seconds |
| Troll | 0 seconds | Separate punch, slam, throw and log attacks, 4–20 seconds |

Zero global recovery does not mean zero observed pauses. It means no universal post-animation minimum can be inferred from MonsterAI.m_minAttackInterval. Humanoid.EquipBestWeapon considers independently ready weapons; the last-used weapon's interval is not a global block on another ready attack. Do not treat the listed per-weapon values as guaranteed remaining recovery.

Further verified constraints:

- Humanoid.OnAttackTrigger checks local ownership and requires its private m_currentAttack before executing damage or projectile work. ZSyncAnimation.RPC_SetTrigger merely sets an Animator trigger and does not construct that Attack object. Transferring a visible animation is not transferring its attack execution.
- Attack.Update carries private projectile-burst timers, attachment and completion state. Attack.Stop normally marks completion locally; only looping attacks explicitly send attack_abort. There is no universal native attack-end acknowledgement to wait for.
- Owner-side cooldown state is not transferred. Repeated reassignments cannot be claimed to preserve exact cooldown timing merely because the network changes happen between visible animations.
- The native broadcast executes immediately on its sending client and arrives later at the server. A server-observed animation end can already lag the owner's recovery window. A fixed grace or recent RTT value is not a bound on this uncertainty or on owner-change delivery.
- Native MonsterAI.RPC_Sleep/RPC_Wakeup update only non-owner copies. They cannot be used as a hidden command/acknowledgement barrier to suspend a remote owner's AI. Forged stagger/freeze/animation commands would change gameplay and are not an acceptable substitute.

Direction-specific candidate, not implemented:

1. Keep grid decisions separate from permission to execute a mob transfer. Only the affected mob waits; the cell and other objects continue normally. Revalidate current owner/revision, current desired grid owner and loaded destination before committing.
2. With a server source, check the real Attack lifecycle, not just InAttack: no current unfinished Attack, no pending attack-animation transition, charging or stagger. Commit on the server thread before allowing its next AI attack. This closes source-side overlap locally, but does not migrate private state to the destination or guarantee identical combat timing.
3. With a client source, native animation triggers can arm a per-mob observation guard. A positively known recovery window might justify an experimental early-gap handoff for verified attack sets and a conservative network budget. Unknown animation history, changed/unresolved attacks, inconsistent playback or an insufficient window must veto rather than expire into a forced transfer. This remains an observation-based heuristic, not source permission.
4. Conservative server-only policy: keep the current owner during observed combat for cases that cannot provide such a window; resume grid changes after combat. This reduces repeated exposure but delayed combat-start flags are still not an atomic idle certificate. Exact grid changes throughout combat require cooperating owner-side permission; native clients offer no equivalent of Container.RequestOpen's source-side busy check for mobs.

Scheduling can remain bounded to pending mob handoffs: use existing mobile cell/owner/unload events and a cheap hash-filtered native SetTrigger observer, with due-only checks for waiting nodes. No new source-to-client protocol, all-mob physics polling or historical unloaded-world cache is needed for the observational variant. One pending intent per mob must be replaced/cancelled on newer decisions, native release or unload. No CPU benchmark, live attack migration or multiplayer test was performed; none of these candidates is a shipped safeguard.
