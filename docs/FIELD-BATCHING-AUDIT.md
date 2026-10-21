# Field batching candidate

Baseline: the local 1.0.53 priority test build. The two 1.0.50 grid optimisations remain reverted. This change does not add a future-job sleep gate, policy-pool reuse, a transfer budget or a new network protocol.

## Shared field work

- The existing loaded-object bucket caches its scheduling priority only while a pending-work snapshot or forced-send prefix is classified. Interleaved objects in one field reuse that result. A singleton bypasses caching because no other object can amortise its setup.
- Changed-field scheduling calculates its common directional wait at most once, lazily. One HashSet.Add replaces a Contains followed by Add when queuing a newly pending node.
- Object attachment records its existing bucket reference; movement updates it and removal clears it. There is no additional coordinate-to-object index or historical field dictionary.

## Individual work that remains necessary

An actual owner change still writes each object's native ZDO. Inventory usage, text editing, refill leases, actor state, owner revision and authenticated recipient readiness can differ inside one field. The final apply step therefore retains all object-specific checks and reads the current field owner. It does not reuse a target captured before a native/mod callback.

The priority cache is cleared before any object Apply callback. A separate try/finally clears forced-send classification state. Scratch lists clear their references after each batch and reuse capacity. No cached priority survives into another pass; unloaded fields are removed from the existing object index.

## Checks and measurements

`test-grid-scheduling.ps1` covers dense and interleaved jobs, both exit policies and simulation modes, singleton fields, changed priorities, movement, blocked guards, future deadlines, native exceptions and unload cleanup. Full-controller tests cover packet batches, exact unrelated slots, zero extra writes/RPCs, world isolation and teardown.

`audit-field-batch.ps1 -BaselineProject <1.0.53 source folder>` compares 40,000 deterministic event steps per simulation mode, including twelve players, add/remove/move, owner replies, changing delays, callback-created work, clock rollback and both retention modes. Owners, writes, object-call counts, deadlines and lifecycle are compared byte-for-byte.

`benchmark-field-batch.ps1 -BaselineProject <1.0.53 source folder>` builds both schedulers into the same executable, with production diagnostic counters removed. It alternates measurement order and reports nine-sample medians. On this Windows CLR run, 4,096 pending jobs across 1–64 fields took about 8–22% less scheduling time. For 4,096 singleton fields, overhead was about 3–5%, roughly 0.005–0.011 ms per pass. The singleton bypass avoids the original, rejected 22–24% sparse-case regression. Apply counts and traces match for all timed workloads.

These are managed scheduler measurements, not server frame-time, Unity/Mono, multiplayer latency or FPS measurements. Native object guards and transport can dominate actual transfer time. Detailed result files and source copies are produced under bin/field-batch-benchmark and bin/field-batch-audit when running the scripts.
