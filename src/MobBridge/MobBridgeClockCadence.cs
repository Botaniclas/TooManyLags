#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;

namespace TooManyLags
{
    // Clock scheduling belongs to the whole epoch, including its initial
    // checkpoint clock. Event-triggered packets do not establish a new faster
    // periodic cadence at the receiver.
    internal struct MobBridgeClockCadence
    {
        internal const double Interval = .05d;
        private bool sent;
        private double last, periodic;
        private float lastAttack;
        private float lastSimulation;
        private long lastTick;
        private bool positive;
        private bool horizonSent;
        internal bool Due(double now, float attackTime, float simulation, float horizon = 0f, long tick = 0)
        {
            return MobBridgeTimeline.Finite(now) && (!sent ||
                now - periodic >= Interval - .0000001d && (simulation > lastSimulation || tick > lastTick) ||
                attackTime > lastAttack || !positive && simulation > 0f ||
                !horizonSent && horizon > 0f && simulation > lastSimulation && horizon - simulation < Interval);
        }
        internal void Sent(double now, float attackTime, float simulation, float horizon = 0f, long tick = 0)
        {
            if (!MobBridgeTimeline.Finite(now) || sent && now < last) return;
            if (!sent || now - periodic >= Interval - .0000001d) periodic = now;
            sent = true; last = now; lastAttack = attackTime;
            positive |= simulation > 0f;
            horizonSent |= horizon > 0f && simulation >= horizon;
            lastSimulation = simulation; lastTick = tick;
        }
        internal static float InitialInterval(float aiRate)
        {
            // Publication runs in LateUpdate independently of AI callbacks.
            // CPU/game slowdown changes reported progress, not the send budget.
            return (float)Interval;
        }
        internal static float ObserveInterval(float previous, float measured)
        {
            // Sub-period gaps come from initial/event clocks. Accept their
            // actual poses/frontiers, without predicting another such packet.
            return MobBridgeTimeline.Finite(measured) && measured >= (float)Interval ? measured : previous;
        }
    }
}
#endif
