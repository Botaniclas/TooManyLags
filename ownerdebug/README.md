# BotanicOwnerDebug

An optional client-side ownership visualizer for Valheim. Colours buildings, dropped items and NPC mobs by their current network owner, with a live player legend. Useful for inspecting ownership distribution and transfers in multiplayer bases.

## What it's for

Use BotanicOwnerDebug to identify object and mob owners, debug ownership transfers and configure ownership mods. Watch who owns nearby objects as players move around, then check whether your settings assign them to the intended player or server.

## How it works

Press **F10** during gameplay, with menus closed, to cycle:

**Mobs only → Mobs + interactive objects → All objects and mobs → Off**

The overlay starts off by default. The first press selects **Mobs only**.

- **White:** server. **Dark grey:** unowned. **Bright colours:** players. **Blue-grey:** unknown owner.
- Colours follow ownership changes and remain visible through other objects. The legend refreshes every second; network delivery can delay new player information.
- The interactive filter includes dropped items and hides non-interactive pieces such as walls and floors. Both wild and tamed mobs remain visible.
- Players, effects, transparent surfaces and PUP FPS clusters are excluded.

The overlay is local and leaves ownership, original materials and colliders unchanged. Active overlays add rendering work; turn them off after inspection.

## Ownership in action

These examples show BotanicOwnerDebug displaying ownership while [TooManyLags - FiresGhettoNetworking Addon](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_FGN_Addon/) manages the ownership grid. White objects belong to the server; purple objects belong to the player in these recordings.

**Ownership from above**

[![Ownership from above](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-1.gif)](https://github.com/user-attachments/assets/4a445578-1edf-4abb-8eea-f28183878d3c)

**Ownership while moving**

[![Ownership while moving](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-2.gif)](https://github.com/user-attachments/assets/8555a985-7696-4cc6-865a-5115c3695f72)

## Configuration

Edit `BepInEx/config/botanic.owner.debug.cfg`, then restart the game. F10 updates the display and filter settings automatically.

| Feature | Defaults and options |
|---|---|
| Display | `ShowOwnerColours`: off. `ToggleKey`: F10. First press shows mobs only. |
| Filters | `InteractiveObjectsOnly` and `MobsOnly`; mobs-only takes priority. |
| Legend | `ShowLegend`: on. Displays owner colours and player names. |
| Range | `MaximumDistance`: 200 metres from the camera. |
| Colours | `ColourStrength`: 1. `ColourSeed`: 1337; six distinct fixed colours, then six variants; stable per connection. The seed rotates the palette. |
| Diagnostics | `EnableLogs`: off. |

## Requirements and installation

1. Install **BepInEx on your client**. Copy `BotanicOwnerDebug.dll` into `BepInEx/plugins/BotanicOwnerDebug/`.
2. Start the game to generate the config. Press **F10** to show mobs, again to include interactive objects and dropped items, again for all objects and mobs, then again to turn the overlay off.

No server installation is needed.
