# Changelog

**1.1.2**
```
- Fixes a listen-host reply cancelling a replacement ownership request. Source consent, final-state receipts and receiving-client readiness remain required.
- Protects pending cart attachment, attacks before animation startup, charging, fermenter tapping and queued boss summons. Occupied ships reject grid transfers to players on shore, avoiding competing vanilla owner changes. Update modded owner clients for these additional checks.
- Reduces repeated work while handoffs wait for use events or action deadlines, and reuses refill guards during repeated inserts without shortening protection.
```

**1.1.0**
```
- Rebuilds Standalone with the current directional grid: host-controlled camera overlap and turn prediction, 6.4-metre cells, and a short hold only for overlapping player fields. Unmodified clients retain character direction and restricted simple-object handoffs.
- Fixes unresolved interaction handoffs and stale-receiver delivery. Refills renew a 1-second guard; incinerators protect inventory use and burning. Native signs/portals confirm ownership before editing and protect only that object until movement; discarded takeovers require fresh grid activity. Update host/server and clients for full guards; older clients keep native sign/portal ownership.
- Adds host/admin live configuration and reorganizes old configs automatically while preserving values and an original backup. The host controls gameplay/camera settings; only logging stays local. Fresh movement checks default to 0.1 seconds; existing values stay unchanged. No FiresGhettoNetworking dependency or Server-Side Simulation is added.
```

**1.0.2**
```
- Speeds up camera-driven ownership: adjacent directions overlap by 30 degrees, changed coverage gets native send priority, and the next host/server pass reacts without another interval wait. Includes immediate local-host camera notification while preserving usage safeguards and optional-client character rotation.
- Camera and movement checks default to 0.05 seconds; both allow 0.05–5 seconds. Existing configs retain their saved intervals. Update modded clients to use the new camera coverage.
```

**1.0.1**
```
- Makes client installation optional: clients with the mod use camera direction; unmodified clients use character rotation.
- Allows ownership transfers for supported simple interactions with unmodified clients. Inventories, refill stations, vehicles and animals retain vanilla ownership unless both owner clients have the mod.
- Uses existing connection latency measurements for unmodified clients.
```

**1.0.0**
```
- Adds player-owned interaction and creature simulation for local multiplayer and dedicated servers without FiresGhettoNetworking. Requires installation on the host/server and participating clients.
- Adds a camera-directed ownership grid that selects better-connected players in overlapping fields.
- Adds Hostile Mob target ownership and grid management for idle tamed animals. Friendly summons and bosses keep vanilla ownership.
- Protects open inventories, active use and refill stations during ownership transfers, including ten seconds after refilling.
```
