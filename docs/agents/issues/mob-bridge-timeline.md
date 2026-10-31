# Mob bridge 50 ms / 500 ms timeline

## Clock model

```text
fake(t) = 0.5 * t                         before the real clock starts
real(t) = max(0, t - realStart)
```

The first destination simulation-clock sample defines `realStart`. The common
frontier is:

```text
0.5 * t = t - realStart
t = 2 * realStart
```

If the first sample arrives at nominal latency `L`, takeover is at `2L` with
both simulations at time `L`.

The handoff phase is never quantized to 50 ms. `L` can be any measured latency,
and the final partial simulation interval is integrated exactly up to `L`.
The native game currently invokes `BaseAI.UpdateAI` with a `0.05 s` delta from
`MonoUpdaters.FixedUpdate`; the fake runner matches that native decision cadence
only to reproduce the real simulation. Decision cadence is therefore an
implementation detail of the game, not the length of the fake phase.
The previous 500 ms bridge cap is removed; measured RTT up to the debug test's
1000 ms ceiling controls the phase duration.

## 50 ms

Assumed first clock sample at `50 ms`:

| Wall time | Fake sim | Real sim | State |
|---:|---:|---:|---|
| 0 ms | 0 ms | inactive | source pose, bridge starts |
| 20 ms | 10 ms | inactive | first fixed movement step |
| 40 ms | 20 ms | inactive | second fixed step |
| 50 ms | 25 ms | 0 ms | first destination clock sample, blend starts |
| 75 ms | 37.5 ms | 25 ms | continuous old-owner motion |
| 100 ms | 50 ms | 50 ms | equal simulation frontier |
| + takeover window 25 ms | 50 ms | 50 ms | residual position/yaw settles |

The 50 ms bridge contains only `50 ms` of authored simulation while being shown
over `100 ms`. It must still move continuously. A stationary/oscillating result
therefore indicates a movement/decision problem, not a reason to add a hold:

- `DoAttack` decision caused the speculative movement hold;
- ground query rejected the fake step;
- target replica alternated between old/new positions;
- no destination clock sample arrived and the fallback takeover fired early;
- root correction after takeover exceeded the old fixed smoothing duration.

The bridge now logs `bridge start`, `bridge clock`, and `bridge takeover` lines
with fake time, offset, yaw, settle duration, max divergence and clock status.

## 500 ms

| Wall time | Fake sim | Real sim | State |
|---:|---:|---:|---|
| 0 ms | 0 ms | inactive | source pose, bridge starts |
| 250 ms | 125 ms | inactive | fake remains continuous |
| 500 ms | 250 ms | 0 ms | first destination clock sample |
| 750 ms | 375 ms | 250 ms | divergence exposed |
| 1000 ms | 500 ms | 500 ms | equal simulation frontier |
| + takeover window 250 ms | 500 ms | 500 ms | residual position/yaw settles |

At 500 ms, fake and real simulation each run for `500 ms` before alignment.
Larger path, target-input and collision divergence is expected. An early
takeover based only on nominal latency can use an old destination root; the
subsequent authoritative root update then appears as a backward teleport.

## Corrections applied

- Takeover no longer starts at nominal `L`.
- It waits for the first destination simulation-clock sample.
- A missing-clock fallback starts only after `L + 250 ms`.
- If a real clock sample arrives after the fallback but before takeover, the
  real epoch is corrected to that sample without rewinding fake time.
- Takeover duration is exactly one quarter of the stretched display:
  `2L / 4 = L / 2`.
- There is no pose blending during the running fake phase. The final blend is
  the only position/rotation correction toward the real simulation.
- The final blend stores the complete fake pose and interpolates it directly to
  the current real root. A root correction arriving after the fake phase cannot
  become a full backward jump on the first transition frame.
- Partial fixed simulation steps are integrated; the 50 ms case is no longer
  truncated to two complete 20 ms steps.
- Fake AI is evaluated on fake simulation time, not wall time.
- Fixed simulation poses are recorded separately from rendering. Every render
  samples interpolated position and rotation between the surrounding fixed
  poses, so a 20 ms steering step can never become a one-frame visual jump.
- Movement direction and speed still use native acceleration, braking and turn
  rates inside the fixed step.
- Every client publishes a low-frequency global simulation-rate sample. The
  source reads the destination player's recent sample before ownership changes,
  so fake playback starts with the current rate rather than learning it halfway
  through the bridge.
