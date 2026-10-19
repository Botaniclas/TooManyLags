# Source and dependency notices

The pure grid policy is adapted from Botaniclas TooManyLags - Standalone 1.1.0 under this project's MIT license. Namespace adaptation and an explicit null initializer do not change the geometric policy.

This addon interoperates with **Sarkastic.gg Dedicated Simulation**, MistrCech's fork of ddormer/valheim-serverside. The dependency DLL and its bundled game code are not redistributed in these packages. Obtain it from its upstream release. The current source reference is recorded in `docs/COMPARISON-AND-IDEAS.md`; its own-code MIT license is included in `docs/UPSTREAM-LICENSE.txt` for attribution and future permitted source adaptations.

BepInEx, Harmony, Unity and Valheim are external dependencies. Their assemblies are used locally for compilation/tests and are not included in the install or source ZIPs. Test fixtures model their interfaces; managed and metadata tests do not start a Unity world.
