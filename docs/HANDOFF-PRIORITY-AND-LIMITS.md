# Handoff priority and existing limits

Test build 1.0.53 starts from the 1.0.52 comparison source. The reverted 1.0.50 field-pool and future-job-skip changes remain reverted. No early-mob candidate or packet-catchup prototype is included.

## Priority

Each scheduled pass snapshots pending nodes into four reused lists: current inner/red coverage, forward/orange coverage, outer/yellow coverage, then no current coverage. The live contribution counts determine the tier; configuring equal transition delays does not merge priorities. Shared red cells remain red priority even when their destination is the server.

All due lists run in that same pass, without a new count/time budget. Existing object deadlines, field timers, overlap cooldown, readiness, inventory, use and collision guards remain in control. A blocked red node does not stop another node, and lower lists cannot starve. Native callbacks can remove or create jobs without invalidating the snapshot; newly queued jobs retain next-pass semantics.

Vanilla's `AddForceSendZdos` prepends forced IDs and can invert enqueue order. The dedicated simulation path therefore stably reorders recognised TML slots in the prefix that this native method just inserted. Unrecognised forced updates keep their exact slots, and the normal suffix is untouched. No packet is added, removed or duplicated. A replacement that skips the original method is left alone. This common native method is also called by the inspected ValheimTune 0.7.9 dirty-set path.

Priority adds no new RPC, distance check, world scan or timer. Scratch collections release object references after each pass and reuse their high-water capacity. The ownership queue and send-prefix partition are linear, without comparison sorting. Native transfer callbacks and responses are not deferred by a return budget.

## Limits found in the inspected source

- TML's interactive grid has no global objects-per-second or objects-per-pass limit. Every due pending node can attempt its existing handoff during a pass.
- Current test builds remove the old broad-area and pre-transfer loading gates. An actual forwarded interaction still needs its matching recipient state and RPC instance; this does not gate the preceding ownership assignment.
- Unanswered native inventory requests have a 2-second response window and can then retry immediately. Uncertain send errors retain a 2-second delay. In 1.0.70, confirmed inventory and ordinary grid refusals discard that object's request until fresh covering-player movement/heading in both simulation modes. Partial cells are not certified as completely owned. Unanswered commits and native-state reconciliation retain their safeguards. These are per-object safety states, not a general throughput quota.
- FiresGhettoNetworking 1.5.65's own creature balancing has a 2-second pass, up to 16 handoffs per pass and up to 6 per source owner. This is not an interactive-grid quota; direct TML object handoffs do not pass through `BalanceCreaturesNearPlayers`.
- Native ZDO replication has a per-peer send window and skips sending when the socket queue has insufficient room. The inspected unmodified game uses 10,240 bytes and a 2,048-byte minimum headroom. Networking mods can replace these values.
- FiresGhettoNetworking 1.5.65's enabled send scheduler defaults to a 4 ms frame budget. It defers to a recognised replacement such as ValheimTune. The inspected ValheimTune build has configurable round/send-window and receive limits. These are transport/scheduling limits, not an ownership-write counter; actual server settings determine which are active.

Dense areas can require many object snapshots and safety exchanges. Source inspection identifies plausible delays but does not prove which mechanism caused the reported 0.5–2+ seconds in a live session. Prioritisation cannot bypass a missing snapshot, protected usage, unavailable destination or saturated connection.
