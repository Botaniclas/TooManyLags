# Report bugs and ideas

Real-world testing is still limited. Please share bugs and ideas through [GitHub Issues](https://github.com/Botaniclas/TooManyLags/issues). Include **TooManyLags - Server Side** and the version so the report is assigned to the right edition.

Before reporting a bug, test on a separate copy of your server and world:

1. Run only BepInEx and **Sarkastic.gg Dedicated Simulation**, without this addon or other server mods. Use vanilla clients where possible. Repeat the steps that caused the problem. If it still happens, report it to the [simulation project](https://github.com/MistrCech/valheim-serverside/issues) or the relevant project rather than here.
2. Add **TooManyLags - Server Side** to that isolated setup and repeat the same steps. If the problem appears only with this addon, open an issue with a detailed description, steps to reproduce, game/mod versions, relevant settings and server BepInEx logs. Note any client mods that remained installed.

For an ownership issue, include the object or creature name, expected and actual owner, player positions, whether someone had just logged in or teleported, and whether an inventory, vehicle or attack was active. The read-only **`tml_grid_status`** command in the **dedicated server's console** adds a snapshot to the server log. The optional [BotanicOwnerDebug](https://thunderstore.io/c/valheim/p/Botaniclas/BotanicOwnerDebug/) client overlay can help illustrate the result.

Thanks for helping improve the mod. Have fun, and enjoy smoother adventures together!
