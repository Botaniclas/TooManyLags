# Configuration

## Start here

Install this addon and **[Sarkastic.gg Dedicated Simulation](https://thunderstore.io/c/valheim/p/Sarkastic_gg/Dedicated_Simulation/)** on the dedicated server. Clients need neither mod. The simulation backend must be enabled; this edition has no mode without Server-Side Simulation and does not force or rewrite the dependency's config.

Edit `BepInEx/config/botanic.toomanylags.dedicatedaddon.cfg` **on the server**. Every setting requires a **server restart**. Start with the defaults; the most useful choices are **ownership retention**, **static building sharing** and **coverage for poor connections**.

`Enabled=true` turns this addon on. Set false to leave ownership to the simulation dependency; it does not disable that dependency's simulation.

## 1. Decide where simulation runs

Supported objects use the server outside player fields. An exclusive field can assign them to its player; shared fields use the server, with separate Hostile Mob rules below. Server simulation needs CPU and RAM; client ownership moves part of that work back to players.

**`KeepOwnershipOnCellExit = false`**

- **false:** when a field becomes empty and its retirement delay finishes, managed objects are requested back to the server. Active-use protection can postpone or reject that request.
- **true:** retain the last owner of an empty field while the area remains loaded. Later requests from another player can change it. This avoids automatic return transfers and gradually distributes simulation across clients and server.

This concerns completely vacated fields; an overlapping field that still contains one player's coverage can return to that player. Native region release still applies: retaining ownership does not keep distant areas loaded. Loose drops, fish and vehicles also retain their native-use priority rules.

**`UseGridForStaticBuildings = false`**, under **Simulation Settings**, keeps enabled eligible noninteractive building pieces on the server. Set true to share them through the grid like nearby interactives. There is no separate static-server-default switch in this edition. Turning off the `StaticBuildings` category leaves those pieces to the dependency's normal policy.

## 2. Prepare ownership ahead of movement

**`GridCellSizeMeters = 4`** selects cell size: **4, 6.4, 8 or 16 metres**. **`GridCoveragePattern = Rounded7x7`** selects the available shape; alternatives are `Rounded5x5` and `Square3x3`. Four-metre square 3x3 is automatically treated as rounded 5x5.

**`DirectionalGridMode = true`** uses the character's direction, not the free camera. Leave it enabled initially. Both retention modes keep the full red 3x3, including rear cells. **`EnableOuterCells = false`** leaves yellow cells off; with this default, rounded 5x5 and 7x7 use the same normal red/orange footprint.

**Visual examples of the defaults:** character direction points upwards; each cell is 4 x 4 metres. The four panels show normal coverage, fast movement, poor connection and both adaptations together. White cells make no player-grid request; they are not unloaded cells. Yellow outer cells are disabled by default.

![Server Side default grids: normal coverage has nine immediate red cells and three orange cells with a 0.2-second delay; fast movement makes all twelve immediate. Poor connections expand this to sixteen red plus three orange cells, or nineteen immediate cells when also moving fast.](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/serverside/docs/grid-coverage.png)

| Setting | Default | Purpose |
|---|---|---|
| `InnerTransitionDelaySeconds` | 0 s | Full red 3x3; no deliberate wait for a new field request. |
| `ForwardTransitionDelaySeconds` | 0.2 s | One orange forward row with rounded grids. |
| `OuterTransitionDelaySeconds` | 3 s | Yellow cells when enabled; also the retirement delay after a cell becomes empty. |

The delays can avoid unnecessary ownership changes at the edge. Lower delays or larger coverage can help poor connections and fast movement; they also affect more objects or create more transfers. Zero removes only the configured wait, not network delivery, loading or handoff protection.

**`FastMovementThresholdMetersPerSecond = 4`** makes the orange row immediate at 4 m/s horizontal speed, forming twelve immediate cells in the normal grid (top-right panel). It changes the delay, not the footprint. It returns to the orange delay below **3 m/s** (75% of the threshold). Zero disables this adaptation. This uses native velocity without extra client messages.

With `DirectionalGridMode=false`, the entire selected pattern is active with **`TransitionDelaySeconds = 2`**. Red/orange/yellow delays and the speed boost do not control that full-grid mode. Pattern and cell settings do not change native loading distances.

## 3. Automatic coverage for poor connections

**`LargeGridPingThresholdMs = 200`** expands directional coverage when measured ping reaches 200 ms (bottom-left panel above). Facing upwards, the larger footprint has four red rows of **3 / 5 / 5 / 3** cells, plus an orange row ahead: **16 immediate red + 3 orange cells**.

The same movement threshold applies to the expanded grid. At 4 m/s, its orange row also becomes immediate: **19 immediate cells** (bottom-right panel). Slowing below 3 m/s restores the orange delay without shrinking the grid; ping controls its size separately.

Diagonals use a grid-aligned approximation: twenty red cells plus three orange, rather than sixteen plus three. The grid shrinks below 150 ms with the default threshold. Ping is cached for one second; unavailable telemetry keeps the current coverage size. Zero disables adaptation. Full-grid mode keeps its selected shape.

**`MovementRotationCheckIntervalSeconds = 0.1`** samples character cells, heading and movement up to ten times per second; accepted range is 0.05–5 seconds. Unchanged coverage creates no new field requests. Standing players still count for overlaps, and running field timers can finish without movement. There are no camera-update or camera-prediction settings.

**`ServerOverlapCooldownSeconds = 1`** limits rapid changes after shared cells select the server. During that hold, new grid decisions for the cell are ignored. Expiry alone does not transfer it; fresh relevant movement or heading activity can request it again. Zero disables the hold.

## 4. Hostile Mob ownership

The **`HostileMobs = true`** category covers supported untamed native MonsterAI/Humanoid creatures. Friendly player summons stay excluded. Native tame state switches eligible animals to the separate `TamedAnimals` policy.

- **Exclusive player field:** choose that player, subject to attack and availability checks.
- **Overlapping player fields:** inspect the mob's forward **90-degree cone**, 45 degrees to each side.
- **Outside all fields:** request server ownership with default exit settings.

| Setting | Default | Purpose |
|---|---|---|
| `HostileMobConeRangeMeters` | 8 m | Ordinary Hostile Mob cone range. |
| `BossMobConeRangeMeters` | 10 m | Separate range for creatures with Valheim's native boss flag. |
| `ServerOwnedWhenMultiplePlayersInMobCone` | true | One eligible living cone player gets ownership; zero or multiple players select the server. False selects the nearest eligible cone player instead. |

Distances and angle are three-dimensional, measured from the mob's native position and rotation. The cone is used only for actual shared fields. It **does not identify or change the mob's real attack target**, and this edition has no target-ping fallback rule.

Transfers wait for observed attacks, charging and stagger. A server-owned source checks its real unfinished attack; a player-owned source is observed through native attack triggers and animations. Unknown combat history or an uncompleted looping attack can retain the current owner until new evidence arrives. The latest owner, grid and cone are checked before committing; server takeovers require its area to be ready. Remote observations are delayed and cannot guarantee an atomic attack handoff or identical cooldown timing.

A completed guarded handoff retains the observed idle starting point for the new player owner. Merely having a target or being alerted while chasing does not require that mob to attack once before another transfer is possible. A new attack trigger, an observed attack animation or an external owner revision invalidates that retained observation. Late-loaded and externally reassigned combat histories remain protected.

**`EnableMobAttackForecast = true`** adds an experimental, conservative follow-up-attack check for **player-owned** mobs. It never replaces the existing attack guards or their short idle observation. An already idle server-owned source can still hand off directly without a speculative forecast.

- Standard native ability signals are observed whenever a supported Hostile Mob is loaded, even without a pending handoff. Each recognised ability updates a timestamp; no cooldown is decremented every frame. Profiles include normal/random equipment and the native unarmed fallback. They are shared per prefab, with per-mob timestamps allocated on first recognised use.
- Effective cooldown is shortened by **100 ms plus the source's cached full round-trip latency**. Missing ping, unknown history and ambiguous animation names do not establish a remaining cooldown. Alternative abilities are considered separately; seeing one attack does not put every other attack on cooldown.
- Only a pending handoff that passes the existing attack checks evaluates the forecast. If every modelled ability is still cooling, it does **no geometry query**. Otherwise it checks players first, then other native characters, with **5 degrees extra per side and 0.5 metres extra attack range**, including target radius. Only attackable structures require a physics query. These margins affect attack prediction, not the 8/10-metre ownership cone. Inverted attack angles expand in the opposite direction correctly.
- A possible follow-up attack blocks only that mob. Its own movement or turn, a nearby target moving away, relevant native state changes, load/unload or a new ability event wakes the pending decision. If no ready attack still fits, the handoff can proceed on the next server pass without waiting for the grid interval. Movement never bypasses an actual running attack. Faraway movement, target-only turns, unchanged imports and nonfatal damage do not repeat its geometry. Teleport departures use both old and new positions. There is no stationary geometry poll; the fixed building-query buffer treats saturation as uncertain.

This models possible attacks, not the owner's exact AI decision. It deliberately does not duplicate target selection, line-of-sight raycasts, pathfinding or private global recovery timers. Those unknowns cannot justify a transfer. Owner changes reset inferred cooldowns, and unloading releases per-mob state. The forecast can keep ownership longer during combat; set false to restore the previous attack-guard behavior. A fixed margin and a recent ping remain estimates, not a delivery bound or client acknowledgement.

## Solo combat handoff test

This optional diagnostic tests both handoff directions on your dedicated server with **one player and one selected Hostile Mob**. Clients need no addon.

1. Set the following in the server config and restart:

   ```ini
   [09 - Solo Mob Test]
   EnableMobOwnershipTest = true
   MobOwnershipTestIntervalSeconds = 0.5
   ```

2. Join alone and approach one supported Hostile Mob. The test selects the nearest eligible grid-covered mob within 20 metres, once. Keep other creatures away if you want a particular opponent selected.
3. Fight normally: let it attack repeatedly, block/dodge, move out of range and around its sides. The test alternates desired owners between you and the server. **All normal attack, forecast and readiness guards stay active.** A blocked request keeps its direction; it is not replaced by the opposite request. The interval starts only after a successful transfer.
4. Watch the server BepInEx log: `MOB TEST` records requests, actual completed transfers and a pending status at most every two seconds. `inAttack=True` shows an observed running attack; `forecast=True` shows a possible follow-up blocking the handoff. `observedAttack` is historical, not a current-attack flag; `combat` can stay true while chasing. `idleHandoffObserved` retains the observed idle handoff until new attack/owner evidence invalidates it. These values alone are not failures. Check for interrupted attacks, resets, frozen movement or damage being lost/duplicated during a completed transfer. Optional client-side OwnerDebug can show the owner changes visually.
5. Stop with **`tml_mob_test_stop` in the server's own console**, if available. The test also stops on death, unloading, moving over 64 metres apart, another accepted player, or after five minutes. A stopped test does not select another mob automatically. Disable the setting and restart when finished. To run another test, restart with it enabled.

The test changes only the selected mob's desired owner and never forces an attack-time transfer or an immediate owner reset when stopped. With the setting off, there is no test selection scan, timer or test log. A solo test exercises the handoff path; it does not reproduce two players' simultaneous actions or real network jitter.

## 5. Keep native use protected

Public chests and supported boat/cart cargo use the owner's **native inventory-use check** for remote acquisition. Real opening remains native. A refused or timed-out automatic handoff waits for fresh relevant activity; it does not repeatedly open or poll inventories.

**`RefillOwnershipProtectionSeconds = 1`** blocks automatic transfers after an observed fuel, material, cooking-slot or ammo increase. Each observed refill restarts the window. Consumption does not renew it, and expiry alone does not retry ownership. Remote local refills become visible only when their native update arrives. Zero disables this additional window.

For supported direct-state interactions, a player-to-server request waits **`ClientToServerGracePingMultiplier = 2`** times that player's native round-trip latency, with **`MinimumClientToServerGraceSeconds = 0.1`** as a floor. Incoming source-object updates cancel the takeover; fresh field activity is needed to try again. This does not add a window to server-to-player acquisition or native Pickable completion. A ping estimate is not a delivery deadline; leave these protections at their defaults initially.

**`RespectBackendFireOwnership = true`** respects FireControl's own server policy when that dependency feature is active. Set false to let enabled eligible refill/fireplace objects use the grid. Other fires retain dependency policy. FireControl's server-only fire rules cannot be fully enforced on player-owned sources; see Compatibility before overriding this.

## 6. Object categories and verification

The **Object Categories** section stays at the bottom. Its twenty broad groups can be disabled separately. Disabled or unsupported objects retain native/dependency ownership; they do not necessarily become player-owned.

Idle tamed animals use the grid; riding, following, transport and combat suspend automatic changes. Boats use helm coverage even while steered, but open cargo takes priority. Carts must be idle; pulling, cargo use and pending remote hitch requests protect their owner. Loose drops and fish retain native pickup priority. Signs, portal editors, incinerators, fermenter taps, boss queues and unknown custom controls remain outside the grid.

Use the optional client-side **[BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/)** overlay to inspect ownership; it is not required by this addon.

For diagnostics, enter **`tml_grid_status` in the dedicated server's console**. Two bounded `GRID` reports also appear in BepInEx logs, 10 and 40 seconds after character login. `cachedPingMs=0` means no usable cached ping. This command is read-only and is not available in an unmodded client's console. There is no live `tml_config` command in this edition.

When updating a test installation, replace the DLL and restart. Existing config keys and values remain; newly added keys are generated. No client update is needed.
