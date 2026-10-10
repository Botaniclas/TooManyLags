# Changelog

**1.0.6**
```
- Applies required FiresGhettoNetworking settings for the current session without changing its saved config: dedicated-server simulation and station routing on, broad/selective server ownership off, creature balancing on when AI ownership is enabled.
- Starts previously disabled server simulation through FGN's native initializer, retaining its incompatible-mod safety blocks. Adds a prominent warning about the extra server CPU demand.
```

**1.0.5**
```
- Adds the GitHub project link to the package metadata. Ownership behavior and configuration defaults are unchanged.
```

**1.0.4**
```
- With KeepOwnershipOnCellExit enabled in directional mode, only inner/red fields transfer automatically when one player remains after an overlap ends.
- Forward/outer fields retain their settled owner until the remaining player's next cell or heading-sector change, without repeated field jobs while stationary. Existing handoff protections remain active.
- Adds a simple 256x256 icon showing LAGS crossed out with a red X.
```

**1.0.3**
```
- CameraHeadingCheckIntervalSeconds now accepts 0.05–5 seconds, allowing up to 20 camera-direction checks per second. The default remains 0.1 seconds.
- Faster camera checks still send updates only when the direction sector changes. Server grid checks keep their existing cadence; network delivery can still delay ownership updates.
```
