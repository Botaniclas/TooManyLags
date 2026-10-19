# Changelog

**0.13.1**
```
- Fixes Hostile Mobs waiting for their first attack before changing owner again after a completed guarded handoff. The previously observed idle handoff is retained while chasing; new attacks and external owner revisions invalidate it. Existing attack, loop, forecast and readiness safeguards remain active.
```

**0.13.0 — local beta**
```
- Adds a disabled-by-default solo combat test: one nearby Hostile Mob alternates guarded ownership requests between the only player and server. Requests keep their direction until completed; death, departure, another player or a five-minute limit stops the test. Server logs distinguish requests, completed handoffs and forecast waits.
- Adds an optional attack forecast for player-owned Hostile Mob handoffs. Observed cooldowns are shortened by 100 ms plus cached round-trip latency; ready attacks use 5 degrees extra per side and 0.5 metres extra reach. Existing attack guards remain; blocked predictions wait for relevant events instead of repeating geometry and can retain ownership longer during combat.
- Requires Sarkastic.gg Dedicated Simulation 1.18.0 or newer, now declared as a package dependency. Only the dedicated server needs this addon; clients remain vanilla.
```

**0.10.1 — local prototype**
```
- Shared Hostile Mob fields now select the server when multiple eligible players are in the forward cone. ServerOwnedWhenMultiplePlayersInMobCone defaults to true; false restores nearest-player selection.
- Adds BossMobConeRangeMeters, default 10 metres, using Valheim's native boss flag. Other Hostile Mobs retain their separate 8-metre default. Existing attack guards apply to both.
```

**0.10.0 — local prototype**
```
- Adds Hostile Mobs to the grid: solo fields select their player; overlapping fields select the nearest living player in the mob's forward 90-degree cone, or the server. Cone range defaults to 8 metres and is configurable; the category can be disabled separately.
- Waits for attack completion before mob transfers, protecting charging, stagger and observed immediate follow-up attacks. Completed handoffs send ownership directly to both involved players. Friendly summons and existing tamed-animal safeguards remain unchanged.
```

**0.9.0 — local prototype**
```
- Adds idle native carts to the ownership grid, with native cargo and pulling checks before automatic handoffs.
- Protects remote hitch requests before attachment completes; attachment, native refusal or leaving the footprint releases that request protection. Carts can be disabled separately.
```

**0.8.0 — local prototype**
```
- Adds native boats to the ownership grid: solo nearby players own them; overlapping fields use the server, including while steering.
- Protects cargo with native inventory checks and keeps steering requests working across handoffs. Blocked shared-boat takeovers retry on the existing boat timer; the Ships category can restore normal backend ownership.
```

**0.7.1 — local prototype**
```
- Fixes loose drops permanently leaving the ownership grid after player assignment or a failed pickup attempt.
- Resumes grid handoffs after the owner/requester leaves, including teleport or disconnect, while retaining nearby pickup priority and configured exit retention.
```

**0.7.0 — local prototype**
```
- Adds automatic grid ownership for public stationary chests, including nearby-player acquisition without opening them.
- Keeps native open-inventory checks when taking a connected player's chest and routes stale opening requests to the current owner.
```

**0.6.3 — local prototype**
```
- Fixes the entire grid stopping on dedicated Steam servers because ping measurement entered the Steam client API.
- Keeps the grid running when connection telemetry is unavailable, caches failed measurements and preserves existing protected handoff rules.
```

**0.6.2 — local diagnostic prototype**
```
- Adds bounded server grid status reports and a read-only server console command to locate runtime ownership failures.
- Fixes supported public chests with a matching native network-root override being incorrectly excluded.
```

**0.6.1 — local prototype**
```
- Fixes startup incorrectly rejecting Dedicated Simulation's active Core patch and disabling the entire grid.
- Correctly recognizes the backend's separate FireControl feature, preserving its default fire ownership policy.
```

**0.6.0 — local prototype**
```
- Adds server returns for public stationary chests: overlapping fields use native open-inventory protection; empty fields return directly.
- Keeps real opening and player ownership grants native, without inventory polling or automatic retries after refusal.
```

**0.5.0 — local prototype**
```
- Automatically expands directional coverage for poor connections, with an adjustable ping threshold and hysteresis.
- Adds one-second ownership protection after observed refilling; repeated inserts refresh it, while consumption and cooking do not.
- Covers the native shield generator fuel and attack requests during ownership changes.
- Retires remaining field state after the last player disconnects.
```

**0.4.0 — local prototype**
```
- Adds dynamic grid ownership for idle tamed animals, preserving native riding, following, combat and transport ownership.
- Adds early nearby-player ownership for loose items and fish while keeping native pickup and fishing priority; client-owned pickups are never forcibly reclaimed by the grid.
- Adds optional grid sharing for static building pieces; their default remains server ownership.
- Repairs settled ownership decisions after external owner changes, without periodically scanning objects or overriding native zero-owner releases.
```

**0.3.1 — local prototype**
```
- Adds an option to let the grid manage eligible FireControl fireplaces without competing server takeovers; the native backend policy remains the default.
```

**0.3.0 — local prototype**
```
- Defaults to server simulation outside player fields, with ownership retention disabled.
- Keeps the rear red cells in both retention modes and makes yellow outer cells optional, disabled by default.
- Makes the orange forward row immediate during fast movement, with separate activation and release speeds to prevent jitter.
- Preserves the shared-field server cooldown during movement and direction changes.
```

**0.2.0 — local prototype**
```
- Adds native harvesting, collectors, immediate refill controls, feasts, map tables and archery targets to the ownership grid.
- Keeps purely local controls such as seats and crafting menus outside the grid, with a server default.
- Delays supported player-to-server takeovers briefly; incoming source-object updates cancel that takeover and retain local ownership.
- Sends current object state before forwarding a stale owner request. Completed pickups are not replayed.
- Keeps signs, portal editing, inventories, vehicles and creatures on their native ownership paths.
```

**0.1.0 — local prototype**
```
- Adds an experimental server-only character-directed ownership grid for native simple stationary interactions, using Sarkastic.gg Dedicated Simulation for shared fields and world simulation.
- Preserves native inventory, refill, text-entry, vehicle and creature ownership paths; unloaded objects and inactive worlds do not retain a growing cell cache.
```
