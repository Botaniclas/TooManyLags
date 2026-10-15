# Troubleshooting

## What this mod addresses

TooManyLags targets connection-related delay caused by object and creature ownership. It does not fix low FPS, an overloaded server, broken mod interactions or an unstable internet connection.

The README's two latency cases describe the default mode with Server-Side Simulation. Without it, shared objects stay on a selected player, so that player's latency can also matter.

## Check ownership

Use [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) on your client to see object and mob owners and tune ownership settings.

**Diagnostics:** With Server-Side Simulation, admins can use `tml_config owner_status` for a one-time ownership/loading snapshot in the console and server's `BepInEx/LogOutput.log`, even with `EnableLogs=false`. Add part of a prefab name, e.g. `tml_config owner_status torch`, to inspect that object type. Shows missing instances and native creation/zone readiness. No background checks; existing clients support the command.

## Report bugs and ideas

Real-world testing is still limited. Please share bugs and ideas through [GitHub Issues](https://github.com/Botaniclas/TooManyLags/issues).

Before reporting a bug, test on a separate copy of your server and world:

1. Run only BepInEx and FiresGhettoNetworking, without this mod or other mods. Repeat the steps that caused the problem. If it still happens, please report it to the relevant project rather than here.
2. Add this mod to that isolated setup and repeat the same steps. If the problem appears only with this mod, open an issue with a detailed description, steps to reproduce, mod versions, relevant settings and logs.

Thanks for helping improve the mod. Have fun, and enjoy smoother adventures together!
