# Mob bridge simulation rebuild

## Model

```text
fakeRate = 0.5
fake(t) = min(L, 0.5 * t)
real(t) = clamp(t - realStart, 0, L)
```

If real starts at `L`:

| Time | Fake | Real | Display |
|---:|---:|---:|---|
| 0 ms | 0 ms | inactive | fake |
| 50 ms | 25 ms | inactive | fake |
| 100 ms | 50 ms | 0 ms | blend starts |
| 150 ms | 75 ms | 50 ms | continuous blend |
| 200 ms | 100 ms | 100 ms | real takeover |

## Decisions

- Fake never pauses and never rewinds.
- Fake moves continuously at base `0.5x`.
- Destination clock heartbeats report simulation progress and destination-local
  sample time.
- A bounded rolling window estimates CPU slowdown. Fake wall-time rate scales as
  `0.5 * realRate`, never by changing fixed simulation `dt`.
- A two-second per-player rate cache is refreshed at 500 ms intervals before
  handoff, so fake starts at the destination's current effective rate.
- Destination updates do not continuously rewrite fake input.
- Large divergence is recorded for takeover correction.
- Post-takeover position and yaw settle monotonically to the real pose.
- Random state is seeded once per handoff and shared by fake and real.
- Fake and real execute the same Unity `Random.Range`/`Random.value` calls from
  the shared state; fake ranged values are not remapped through another formula.
- Fake attack gating copies native `CanUseAttack`, consumes the attack-animation
  random choice, resets attack age and advances a shadow weapon cooldown. Real
  simulation still owns damage, projectiles and animation events.
- First fake attack request freezes simulation and display at that decision.
  No later fake movement is applied; takeover waits for the destination clock
  to reach the same `0.05 s` simulation frontier.
- Destination target and RNG locks are measured in simulation `dt`. Both end
  after `L` and return to vanilla target/random behavior on the next AI tick.
- The Harmony target-lock prefix must use the native out-parameter names
  `canHearTarget` and `canSeeTarget`; AI hook installation succeeds atomically
  for both `UpdateAI` and `UpdateTarget`.
- Presentation timing rejects zero/near-zero samples. In solo latency tests the
  configured injected RTT seeds the initial fake window until the normal cache
  is populated.
- `MobBridgeBody` follows `SteeringFrame.Flying`; takeoff and landing switch
  ground/fly speed selection and gravity behavior at the fake AI decision.
- Ground locomotion applies native Walk-before-Run priority, `CheckRun`
  conditions and `CanMove`; flying movement applies status modifiers to turn
  speed only.
- Takeover residual duration is physical: displacement and yaw need no more
  than configured mob movement/turn rates, with the smoothstep 1.5x peak
  accounted for.
- Fake capsule movement includes character/vehicle layers and slides along
  contact normals; it no longer zeroes all velocity at first obstruction.
- Takeover rejects residual movement opposite the current real-root direction;
  the track waits for the real root to catch rather than displaying a rewind.
- Fake body implements the native swim timer/depth threshold, swim speed,
  acceleration, turn speed, status modifiers and water-level vertical target.
- RNG override is scoped to the matching `MonsterAI.UpdateAI` call and restored
  in its finalizer; it does not replace the global random source persistently.

## Open Implementation Work

- The portable checkpoint carries 44 audited AI fields and 13 control fields.
- Native fake AI execution and side-effect suppression are implemented.
- Destination random-scope and simulation-clock hooks are transient and active
  only after a real handoff.
- Target input uses the live replica transform, including its delayed target
  jump.
- Both local and remote target paths project forward. Measured velocity turns
  exponentially toward the current target `GetMoveDir`, and that intended
  velocity is integrated across the prediction horizon. Remote targets use
  `2L - simulationTime`; local targets project from bridge start over
  simulation time.
- The native target collider center is stored in target-local space and
  transformed by the predicted target root and rotation.
- The vanilla `m_updateTargetTimer` is transferred with the AI checkpoint and
  decremented by fake `UpdateAI`. The known handoff target remains pinned during
  the short bridge, which matches the production rule that the new owner is
  also the target.
- `m_updateWeaponTimer`, `m_timeSinceLastAttack`, current-weapon cooldown,
  flight/water/lava state and absolute native timers are transferred or
  normalized to destination-relative values.
- Copied native `Time.time` reads use the destination-predicted game clock, not
  the old owner's wall clock.
- Normal takeover completion uses an estimated destination frontier from the
  latest clock sample plus measured destination rate, not old-owner wall time.
- Post-takeover correction decays a relative pose offset around the moving real
  root. Absolute fake-to-real pose interpolation is forbidden because it can
  move backward while the real root advances.

## Cache Policy

- RTT timing cache: existing five-second session-local estimate for initial `L`.
- Bridge clock state: per active `ZDOID + bridge token + owner revision`.
- CPU rate samples: at most eight samples in memory.
- Pre-handoff player rates: per-player, two-second TTL, overwritten only by a
  newer monotonic owner/session sample.
- No global simulation-tick cache and no reuse across owner epochs.
