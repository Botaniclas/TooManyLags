# Changelog

**Unreleased**
```
- Restores camera-directed grid updates when the initial server camera settings are missed. Clients request the required interval until it arrives; retries stop after successful synchronization.
- Fixes rejected or uncertain grid handoffs blocking door and harvesting requests. Confirms source results and recipient readiness, relaying late requests before execution without timeout-based steals or repeated commits. Processes replies per object; the experimental gate checks actual transfers and delivery failures, storing metadata directly in the existing loaded-object entry. Requires updated clients for automatic interactive transfers; older clients retain native ownership. Keeps inventory/refill protection and guards pending item-stand attachments.
- Adds ServerOverlapCooldownSeconds (default 1s, 0 disables): overlapping directional fields assigned to the server temporarily ignore new grid ownership decisions. After expiry, only a new movement/heading update may request a change; there is no automatic player return. Supports live server tuning and preserves existing usage safeguards.
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
