# Dedicated Simulation frame/worker policies versus FGN + TML

Read-only review, 2026-10-18. No runtime settings, installed profiles, production code or packages changed. Scope: inspected Dedicated Simulation source revision from COMPARISON-AND-IDEAS.md and released 1.18.0 binary; actual FGN 1.5.65 binary and selected decompilation; TML's current local source. No host-specific timing measurements were taken.

## Worker cap

The fork calls LimitJobWorkers once in Awake, even before attempting Core patches. It reads JobsUtility.JobWorkerCount and sets it only when the configured positive limit is lower than the current count. Source default: eight; zero leaves the setting alone. Actual released binary references both the getter and setter. The source's reported 23-to-four-worker idle CPU reduction is the author's measurement on one host, not our reproduction or a prediction for a busy large base.

FGN 1.5.65's external member references include Time.maximumDeltaTime's getter but no direct JobWorkerCount setter or maximumDeltaTime setter. No equivalent worker-cap policy was found in the inspected code. A very narrow optional dedicated-server worker cap is therefore a distinct candidate, unlike replacing FGN's existing scheduler.

Unity documents reducing available job workers as a possible way to reduce CPU load on shared/virtual hosts. It does not make Valheim's main-thread game logic parallel. Fewer workers can lower idle/coordination cost while also slowing useful parallel jobs. Total process CPU can improve with no main-thread frame-time gain. An eight-worker limit has no effect if the current count is already eight or lower. Actual host CPU quota, existing command-line worker settings and other tuning mods must be checked before selecting a value.

Suggested experiment: record actual worker count, process CPU, server frame percentiles and terrain/object loading time with the current value versus a conservative lower value. Keep this optional; no universal four/eight-worker performance guarantee or default global override follows from the code review. The setter is a one-time policy operation and requires no recurring object scan. It controls Unity job workers, not every OS, Steam or .NET worker thread.

Primary reference: [Unity JobWorkerCount](https://docs.unity.com/en-us/engine/6000.6/script-reference/unity/jobs/lowlevel/unsafe/jobsutility/jobworkercount).

## World-update cadence

The fork's SendZDOToPeers2 prefix keeps a bounded owed-send accumulator and rotating cursor. Source default SendIntervalMs=100. It does not create CPU capacity: at low frame rates it serves at most one send per peer per frame. A nominal interval cannot be guaranteed under arbitrary load.

FGN 1.5.65 already has SendScheduler: per-peer deadlines, configured target rate, a per-frame CPU budget, rotating priority after exhaustion, backlog/force-send handling, link-capacity checks, idle backoff and a competing-scheduler check. Its setting Send To Every Player Each Frame defaults on; the name does not mean unconditional transmission every frame regardless of deadlines and budget.

Recommendation: retain FGN's scheduler. Importing the fork's scheduler is redundant and could disable useful link/budget protection. Higher player counts still increase total sending cost; neither design guarantees a fixed interval when the server or transport is saturated.

## Physics catch-up

The fork's LimitPhysicsCatchUp sets Time.maximumDeltaTime, clamped to at least one fixedDeltaTime. Source default MaxCatchUpMs=100; zero leaves the existing setting. It does not change fixedDeltaTime. At a 20 ms fixed timestep, a 100 ms limit corresponds to roughly five fixed steps instead of ten if the previous limit was 200 ms. This caps simulated elapsed time, not a measured 100 ms CPU-time budget; one expensive step can still take longer than expected.

This can contain a catch-up feedback loop after a long stall. It does not remove the original stall or accelerate the same amount of simulation. Unity's deltaTime/time and fixed-step simulation advance less than real time when the cap is exceeded. Valheim's fixed-loop creature/synchronisation work is affected as well as physics.

FGN's optional WorldClock correction adds unscaledDeltaTime-deltaTime to m_netTime. That advances the shared world clock; it does not execute omitted physics or AI steps. Combining a tighter cap with this correction changes the relationship between wall time, world time and actually executed simulation. PUP readiness is a separate mechanism and must remain active; changing this cap is not a collision fix.

Recommendation: do not silently adopt the fork's 100 ms default under a no-behavior-loss requirement. Consider an explicit overload tradeoff only after measuring fixed-step counts, time spent there, lost simulation time and shared-object behavior. A long frame caused by instantiation, GC or another subsystem is not proof that physics catch-up dominates.

Primary reference: [Unity time handling, including maximumDeltaTime and fixed updates](https://docs.unity.com/en-us/engine/6000.0/manual/scripting/managing-time-and-frame-rate/time-handling-variations).

## Zone budget and diagnostics

The fork budgets successful local/ghost zone creation globally at its approximately 0.1-second zone cadence. It can distribute join/exploration/root-loading spikes; it does not reduce the persistent cost of 10,000 already-instantiated building objects. Its cursor/refresh caveats and FGN's all-peer area-ready dependency remain as documented in FGN-LARGE-BASE-REVIEW-2026-10-18.md. Do not copy the loop literally or assume the same cadence in FGN's postfix loader.

Periodic measurement is useful and is not itself a CPU optimisation. The fork reports frame histograms/percentiles, fixed-step counts, sends, zone work and coarse subsystem timing. Not all engine physics/GC time is individually attributed. Its StatsIntervalMinutes=0 stops reports/frame aggregation, but timing hooks still execute if the broader Performance feature is enabled by sending or FPS settings; it is not universally zero-cost when logging is off.

FGN already has ServerHealth, ServerFrameProfile and ServerFrameSplit diagnostics for frame stalls, networking, zone/scene work and collection/filter/creation/removal phases. Reuse those before adding a second profiler. A focused optional fixed-step-count/cost diagnostic would be more useful than copying the entire report system.

The fork also raises its configured server frame target to 60 by default. This is separate from the quoted worker/catch-up ideas. Faster main-loop opportunities can reduce reaction wait with spare capacity, but increase unthrottled per-frame work; this is not a free CPU-saving policy.

## Priority for this project

1. Measure current subsystem costs with existing FGN diagnostics; retain the proposed scene-filter-cache investigation for base-size-dependent recurring work.
2. Optional worker-cap A/B test on a many-core/shared host: strongest additional CPU-saving candidate in this set, with load/parallel-job regression checks.
3. Keep FGN's existing send scheduler and avoid duplicate diagnostics.
4. Reserve catch-up limits for an explicit overload tradeoff, and global zone budgets for demonstrated loading spikes. Neither qualifies as guaranteed behavior-preserving work reduction.
