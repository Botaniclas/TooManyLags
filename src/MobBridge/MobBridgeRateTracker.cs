#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;

namespace TooManyLags
{
    // Small rolling window over destination-local sample time. It estimates how
    // much simulation time the destination completes per wall second without
    // depending on either endpoint's absolute clock.
    internal sealed class MobBridgeRateTracker
    {
        private const int Capacity = 8;
        private readonly float[] simulation = new float[Capacity];
        private readonly float[] sample = new float[Capacity];
        private int count, next;
        private float estimate;

        internal MobBridgeRateTracker(float initial = 1f)
        {
            estimate = MobBridgeTimeline.Finite(initial) ?
                Math.Max(.1f, Math.Min(1.5f, initial)) : 1f;
        }
        internal float Estimate { get { return estimate; } }
        internal int Count { get { return count; } }

        internal float Frontier(float simulationTime, double sampledAt, double now, float limit)
        {
            if (!MobBridgeTimeline.Finite(simulationTime) || !MobBridgeTimeline.Finite(sampledAt) ||
                !MobBridgeTimeline.Finite(now) || !MobBridgeTimeline.Finite(limit) ||
                now < sampledAt || limit < 0f) return 0f;
            float elapsed = (float)Math.Max(0d, now - sampledAt);
            return Math.Min(limit, simulationTime + elapsed * estimate);
        }

        internal bool Observe(float simulationTime, float sampleTime)
        {
            if (!MobBridgeTimeline.Finite(simulationTime) || !MobBridgeTimeline.Finite(sampleTime) ||
                simulationTime < 0f || sampleTime < 0f) return false;
            if (count != 0)
            {
                int last = (next + Capacity - 1) % Capacity;
                if (sampleTime <= sample[last] + .00001f || simulationTime < simulation[last] - .00001f)
                    return false;
            }
            simulation[next] = simulationTime;
            sample[next] = sampleTime;
            next = (next + 1) % Capacity;
            if (count < Capacity) count++;
            if (count < 2) return true;
            int oldest = (next + Capacity - count) % Capacity;
            int newest = (next + Capacity - 1) % Capacity;
            float ds = sample[newest] - sample[oldest];
            if (ds < .04f) return true;
            float ratio = (simulation[newest] - simulation[oldest]) / ds;
            if (!MobBridgeTimeline.Finite(ratio)) return false;
            ratio = Math.Max(.1f, Math.Min(1.5f, ratio));
            estimate += (ratio - estimate) * .35f;
            return true;
        }
    }
}
#endif
