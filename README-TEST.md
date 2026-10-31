# TooManyLags 2.0.0 development test package

This package includes the same native handoff bridge as the normal release, plus
admin test commands, artificial latency and bridge diagnostics. Install the same
version and variant on the dedicated server and every test client; one DLL per
endpoint. It is not the normal publication package.

## Solo combat test

Connect as server admin, be the only connected player, and approach a loaded
Hostile Mob. Server-Side Simulation, Hostile Mob ownership and the visual bridge
must be enabled. Test sessions stop after five minutes.

```text
ai_test                    # Toggle start/stop with native latency
ai_test 100                # Target 100 ms round-trip latency
ai_test start 200 1.5       # Target 200 ms, ownership requests every 1.5 s
ai_test status
ai_test stop
ai_test_v 200 1.5           # Native handoff comparison with same latency
ai_test_vanilla stop
```

Targets range from 0 to 1000 ms; intervals from 0.1 to 60 seconds. Existing
transport latency is included and cannot be reduced by the test. Frame scheduling
and jitter can increase observed delivery time. Artificial latency delays actual
game RPC traffic on this connection, not merely the displayed ping. Do not combine
with `fgn_netsim`. Stopping clears test/handoff presentation and restores transport.

## What to compare

- Low latency (0/20/50 ms) and higher latency (100/500/1000 ms).
- Player-to-server and server-to-player movement, turns and retirement smoothing.
- Locomotion/attack continuity, including a matching attack already in progress.
- Seeker takeoff/landing, swimming, Troll equipment, damage/status effects and
  mob-attached fire or other continuous effects.
- Real target changes with a second player and a nearby third-player observer.

Only observed matching new-owner progress permits takeover. Normal playback is
slower than real simulation; delayed updates may require further slowing or
waiting. Predicted attacks are visual only. No zero-pause or exact prediction
of arbitrary player movement is promised.

Debug builds include test/bridge summaries, scoped measurements and client reports
relayed to the debug server even with EnableLogs=false. Active diagnostics cost
CPU/log output. Normal releases compile routine diagnostics/test commands out,
while keeping the native bridge, errors/warnings and explicit command responses.

## Local build workflow

```powershell
.\build.ps1                 # Debug DLL: bin/debug-system/TooManyLags.dll
.\verify.ps1 -Release       # Verify both release and debug variants
.\package.ps1 -Release      # Normal release plus separate debug/test ZIP
.\publish.ps1 -ValidateOnly # Validate only; no upload
```

This release preparation does not install or publish anything. See README.md for
normal installation, CHANGELOG.md for changes and verification.json for exact
build hashes and test results. Automated fixtures do not establish live Unity
rendering, physics or multiplayer behaviour.
