# Changelog

**1.0.29**
```
- Enables experimental interaction protection by default for new configs. Existing saved values remain unchanged.
- Fixes interactions waiting indefinitely when recipient-readiness replies take longer than 0.5 seconds. Retries keep the pending request identity; changed ownership, state and unloading still invalidate readiness. Server-side fix; existing client protocol unchanged.
```

**1.0.28**
```
- Adds BossAlwaysServerOwned (default on): boss player-target changes choose the server with at least two living players within BossPlayerRadius (default 30 metres); otherwise bosses use normal Hostile Mob rules. Server ownership requires Server-Side Simulation. Unchanged targets reuse their decision without movement or radius polling.
- Bosses can use guarded target ownership when the option is off. Only identified TooManyLags handoffs bypass FiresGhettoNetworking's boss exclusion; attack, stagger, control and loading safeguards remain. Update server and clients together.
```

**1.0.27**
```
- Replaces outdated Hostile Mob handoffs with the latest target after the pending reply, without the six-second rejection lock. Attacks still block transfers; attack end checks the current target again. Ping fallback and other native safeguards remain active.
- Identifies mob requests and waits for actual owner replication before another transfer, preventing late replies from affecting newer handoffs. Update server and clients together; older peers retain native FiresGhettoNetworking ownership.
```

**1.0.26**
```
- With ServerOwnedInteractivesByDefault=true, gives eligible stationary interactives their server owner during vanilla assignment, avoiding an initial player-to-server handoff. Settled grid fields take priority; native active-owner checks and region release remain in place. Server-side change only.
- Adds ServerOwnedMobsByDefault (off): idle tamed animals use server ownership outside player fields, and Hostile Mobs without a player target use guarded server ownership, including with one player online. Player targets and active-use/loading safeguards remain in place. Requires Server-Side Simulation; Hostile Mobs also require EnableAIOwnership.
```

**1.0.24**
```
- Fixes the object ownership grid failing to start when newer FiresGhettoNetworking versions already patch Pickable.RPC_Pick. Preserves its existing server-safe effect actor; harvesting, inventory safeguards and grid timing are unchanged.
```

**1.0.23**
```
- Makes Enabled and EnableAIOwnership server-controlled; clients ignore their local values, and only logging remains local. Both switches require a server restart; update server and clients together.
- Restores guarded interactive handoffs after delayed join negotiation or stale owner replication. The extra 1.0.15 handoff protocol is now experimental and opt-in (EnableExperimentalInteractionProtection=false); existing inventory, refill, active-use and overlap safeguards remain enabled, and the player-simulation mode is unchanged.
- Adds 6.4-metre cells and live tuning of camera overlap (0–45°, default 30°) and turn prediction (default 0.1s, 0 disables); predicted cells use the inner/red delay. KeepOwnershipOnCellExit=false includes rear fields with a red 3x3 centre, and outdated positions after teleportation no longer block grid transfers; changing cell size requires a server restart.
```

**1.0.17**
```
- Restores camera-directed grid updates when initial server camera settings are missed; clients retry until synchronization succeeds.
- Fixes ownership handoffs leaving door and harvesting interactions waiting. Verifies source results and recipient readiness, with per-object processing and extra checks limited to actual transfers or delivery failures. Preserves inventory/refill and pending item-stand protection. Update server and clients together.
- Adds ServerOverlapCooldownSeconds (default 1s, 0 disables) to reduce rapid ownership changes when nearby players turn their cameras. Shared fields stay server-owned during the cooldown; a later movement or direction update can request another change. Supports live tuning.
```

**1.0.12**
```
- Adds admin-only in-game tuning of ownership check intervals, transition delays and Hostile Mob fallback thresholds without restarting. Preserves pending handoffs and usage safeguards. Other settings can be saved for the next restart, with pending and active values shown in the console.
- Camera timing is controlled by the server; camera and movement checks default to 0.05s. Directions cover +/-37.5 degrees with 30 degrees of overlap. Changed directions get native send priority and wake the next server pass without another interval wait; duplicate field work is avoided.
- Moves object categories to the final config section, 10 - Object Categories. Use the supplied config and reapply custom category values; saved check intervals are retained.
```

**1.0.9**
```
- Fixes Hostile Mob ownership being disabled by another installed target-ownership feature. Keeps collision protection active and restores the previous target policy if startup fails.
```

**1.0.8**
```
- Adds NoServerSideSimulation for weak dedicated servers: overlapping fields and high-ping Hostile Mobs prefer a better-connected player. Server ownership is disabled in this mode.
```

**1.0.6**
```
- Automatically applies required FiresGhettoNetworking settings, including Server-Side Simulation, without editing the saved config. Server CPU usage may increase.
```

**1.0.4**
```
- With KeepOwnershipOnCellExit enabled in directional mode, only inner/red fields transfer after an overlap ends. Other fields wait for the remaining player's next cell or direction-sector change.
```

**1.0.3**
```
- CameraHeadingCheckIntervalSeconds now accepts 0.05–5 seconds, allowing up to 20 camera-direction checks per second. The default remains 0.1 seconds.
```
