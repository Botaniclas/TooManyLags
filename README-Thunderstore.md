# TooManyLags - FiresGhettoNetworking Addon

Leave connection lag behind. TooManyLags brings nearby interactions to your own client and gives Hostile Mobs smarter ownership, aiming to avoid almost all connection-related lag. Go beyond Server-Side Simulation: install, play, and never look back.

## Choose your version

| Your setup | Recommended edition and mode |
|---|---|
| Dedicated server, strong CPU | This addon with **Server-Side Simulation** (default). **Most-tested mode.** |
| Dedicated server, weak CPU, with FiresGhettoNetworking | This addon with `NoServerSideSimulation=true`. |
| Dedicated server, weak CPU, without FiresGhettoNetworking | **[TooManyLags - Standalone](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_Standalone/)**. |
| Locally hosted multiplayer | **[TooManyLags - Standalone](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_Standalone/)**. |

## Quick install

1. Install **BepInEx, [FiresGhettoNetworking](https://thunderstore.io/c/valheim/p/VerdantsAscent/FiresGhettoNetworking/) and the same TooManyLags version on the dedicated server and every client**. For manual installation, put `TooManyLags.dll` in `BepInEx/plugins/TooManyLags/`.
2. Start the server and play. TooManyLags automatically applies the required FiresGhettoNetworking session settings. Both mods' defaults, with FiresGhettoNetworking Auto-Tune, are the recommended starting point; fine-tuning remains available.

**Server-Side Simulation is enabled by default and increases server CPU usage.** For a weak server CPU, use the mode recommended above.

**Optional PUP FPS support is built in.** With PUP FPS installed, TooManyLags includes BotanicPupPatch's collision and building-stability guards. Set `EnablePupCompatibility=false` locally and restart to disable them (default true). Missing dependencies or a separate BotanicPupPatch automatically disable the embedded module, with no guard hooks or runtime work.

## See it in action

White objects belong to the server; purple objects belong to the player in these recordings. Colours are shown with the optional [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) tool.

**Ownership from above**

[![Ownership from above](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-1.gif)](https://github.com/user-attachments/assets/4a445578-1edf-4abb-8eea-f28183878d3c)

**Ownership while moving**

[![Ownership while moving](https://raw.githubusercontent.com/Botaniclas/TooManyLags/main/docs/grid-demo-2.gif)](https://github.com/user-attachments/assets/8555a985-7696-4cc6-865a-5115c3695f72)

## Configuration

Defaults should work well. Find configuration, admin commands and compatibility details in the **[Thunderstore Wiki](https://thunderstore.io/c/valheim/p/Botaniclas/TooManyLags_FGN_Addon/wiki)**. Gameplay settings are configured on the server.

## How it works and what to expect

With the default Server-Side Simulation mode, nearby objects run locally before you interact with them. Shared fields use the server, and Hostile Mobs normally follow their player target's ownership.

**Outside the two cases below, the goal is to eliminate all connection-related lag from supported interactions, making multiplayer feel like single-player.**

Your own **very poor connection** can still be felt in these situations:

1. **Close to other players:** overlapping fields run on the server, so interactions can reflect your own latency.
2. **Fighting the same mob together:** when it targets someone else, its simulation runs elsewhere and your connection can delay what you see. If an ordinary Hostile Mob targets you, it normally runs locally; above the configured ping limit (**200 ms** by default), another eligible player within **6 metres of the mob** triggers server fallback once loading and handoff safeguards allow it. Both values are configurable. Bosses have separate shared-combat rules in the Wiki.

With a very poor connection, give other players space when fighting the same enemy.

## Report bugs and ideas

Real-world testing is still limited. Please share bugs and ideas through [GitHub Issues](https://github.com/Botaniclas/TooManyLags/issues).
