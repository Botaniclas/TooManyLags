#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;

namespace TooManyLags
{
    // Freezes the old-owner simulation at the first attack decision. The
    // destination reaches the same simulation frontier first; only then may
    // the visual bridge consume its newer owner state.
    internal sealed class MobBridgeAttackGate
    {
        private float simulation;
        private long decisionTick;
        private bool held;

        internal MobBridgeAttackGate(double now, float latency)
        {
            if (!MobBridgeTimeline.Finite(now) || !MobBridgeTimeline.Finite(latency) || latency <= 0f)
                throw new ArgumentOutOfRangeException("attack gate");
        }

        internal bool Held { get { return held; } }
        internal float Simulation { get { return simulation; } }

        internal void Capture(float value, long tick)
        {
            if (held || !MobBridgeTimeline.Finite(value) || value < 0f) return;
            simulation = value; decisionTick = tick;
            held = true;
        }

        internal bool Released(float destinationSimulation, double now, bool destinationObserved, float destinationAttackTime, long destinationTick)
        {
            if (!held || !destinationObserved || !MobBridgeTimeline.Finite(now)) return false;
            bool reached = MobBridgeTimeline.Finite(destinationSimulation) &&
                destinationSimulation + .00001f >= simulation && destinationTick >= decisionTick &&
                MobBridgeTimeline.Finite(destinationAttackTime) && destinationAttackTime >= 0f &&
                destinationSimulation + .00001f >= destinationAttackTime;
            // Missing clocks and elapsed wall time cannot prove that even a
            // zero-time attack decision has reached the destination.
            if (!reached) return false;
            held = false;
            return true;
        }
    }
}
#endif
