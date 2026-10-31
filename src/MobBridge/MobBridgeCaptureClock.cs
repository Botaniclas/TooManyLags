#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;

namespace TooManyLags
{
    // Native motor ticks keep their fixed cadence independently of rendering
    // and AI. Only the final physics integration may have a partial duration.
    internal sealed class MobBridgeCaptureClock
    {
        private readonly double fixedStep;
        private readonly double recordingEnd;
        private double bodyTime, decisionTime;
        internal MobBridgeCaptureClock(float nativeFixedStep, float recordingEnd = 0f)
        {
            if (!MobBridgeTimeline.Finite(nativeFixedStep) || nativeFixedStep <= 0f)
                throw new ArgumentException("Invalid native physics cadence.");
            fixedStep = nativeFixedStep;
            if (!MobBridgeTimeline.Finite(recordingEnd) || recordingEnd < 0f)
                throw new ArgumentException("Invalid recording endpoint.");
            this.recordingEnd = recordingEnd;
        }
        internal bool Take(float horizon, bool final, float aiInterval,
            out float time, out float dt, out bool decision, out bool controller)
        {
            double body = bodyTime + fixedStep;
            decision = decisionTime < body - .0000001d;
            double next = decision ? decisionTime : body;
            controller = !decision;
            dt = 0f;
            // A native motor step computes its upcoming interval from current
            // inputs. Make that endpoint available for interpolation immediately;
            // never speculate another AI decision or integrate past sealed L.
            if (!decision && recordingEnd > 0d && bodyTime < recordingEnd - .0000001d &&
                bodyTime <= horizon + .0000001d)
            {
                // A sub-fixed-step recording needs an endpoint too. Otherwise
                // a 5/10ms buffer contains only t0 until its capture deadline,
                // so its initial slowed presentation cannot move at all.
                double endpoint = Math.Min(body, recordingEnd);
                controller = endpoint == body;
                time = (float)endpoint; dt = (float)(endpoint - bodyTime); bodyTime = endpoint;
                return true;
            }
            if (next > horizon + .0000001d)
            {
                if (!final || horizon <= bodyTime + .0000001d)
                { time = 0f; return false; }
                next = horizon; decision = controller = false;
            }
            time = (float)next;
            if (decision) decisionTime += aiInterval;
            else { dt = (float)(next - bodyTime); bodyTime = next; }
            return true;
        }
    }
}
#endif
