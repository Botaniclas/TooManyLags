#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;

namespace TooManyLags
{
    // Route delay, expected delivery and recording capacity are distinct.
    // Estimates may budget playback; only received progress may allow takeover.
    internal sealed class MobBridgeTiming
    {
        internal const float ReserveFactor = 1.2f;
        internal readonly float Route, FirstArrival, ExpectedMatch, RequiredWindow, RecordingWindow, Horizon, PhysicsStep, ReceiverFrame, Startup;
        private readonly float rate, senderFrame, firstReady, firstProgress;

        private MobBridgeTiming(float route, float first, float match, float rate, float step, float receiverFrame,
            float senderFrame, float firstReady, float firstProgress, float startup)
        {
            Route = route; FirstArrival = first; ExpectedMatch = match;
            // A sub-step-only recording skips the native controller pass.
            // Retain a full native motor interval even when matching can be
            // observed earlier; unused recorded motion is never forced onscreen.
            RequiredWindow = Math.Max(MobBridgeTimeline.FakeRate * match, step / rate);
            RecordingWindow = RequiredWindow * ReserveFactor;
            Horizon = RecordingWindow * rate; PhysicsStep = step;
            ReceiverFrame = receiverFrame;
            Startup = startup;
            this.rate = rate; this.senderFrame = senderFrame; this.firstReady = firstReady; this.firstProgress = firstProgress;
            if (!Positive(RecordingWindow) || !Positive(Horizon)) throw new ArgumentException("Invalid bridge recording budget.");
        }

        internal static void DefaultReady(float rate, float step, float frame, out float ready, out float progress)
        {
            if (!Positive(rate) || !Positive(step) || !Positive(frame)) throw new ArgumentException("Invalid bridge peer timing.");
            ready = (float)RoundUp((double)step / rate, frame);
            progress = Math.Max(step, (float)(Math.Floor((double)ready * rate / step + .000001d) * step));
        }

        internal static MobBridgeTiming Create(float route, float rate, float step, float senderFrame,
            float firstReady, float firstProgress, float receiverFrame, float startup = 0f)
        {
            if (!Positive(route) || !Positive(rate) || !Positive(step) || !Positive(senderFrame) ||
                !Positive(firstReady) || !Positive(firstProgress) || !Positive(receiverFrame) ||
                !MobBridgeTimeline.Finite(startup) || startup < 0f)
                throw new ArgumentException("Invalid bridge arrival prediction.");
            float delivery = route + startup;
            float first = (float)RoundUp((double)delivery + firstReady, receiverFrame);
            float match = Match(delivery, rate, step, senderFrame, firstReady, firstProgress, receiverFrame, 0f);
            // The publisher's fine tail is tied to the reserved horizon. Refine
            // the ordinary-cadence bound to that exact event schedule; a larger
            // budget must not manufacture an unnecessary minimum display time.
            for (int pass = 0; pass < 16; ++pass)
            {
                float horizon = Math.Max(match * MobBridgeTimeline.FakeRate * rate, step) * ReserveFactor;
                float next = Match(delivery, rate, step, senderFrame, firstReady, firstProgress, receiverFrame, horizon);
                if (next >= match - .0000001f) break;
                match = next;
            }
            return new MobBridgeTiming(route, first, match, rate, step, receiverFrame, senderFrame, firstReady, firstProgress, startup);
        }

        internal float MatchForRoute(float route)
        {
            if (!Positive(route)) throw new ArgumentException("Invalid updated route.");
            return Match(route, rate, PhysicsStep, senderFrame, firstReady, firstProgress, ReceiverFrame, Horizon);
        }

        private static float Match(float route, float rate, float step, float senderFrame,
            float firstReady, float firstProgress, float receiverFrame, float horizon)
        {
            double candidate = firstProgress;
            double phase = Math.Min(0d, firstReady - (double)firstProgress / rate);
            double period = RoundUp(MobBridgeClockCadence.Interval, senderFrame);
            double firstPeriodic = firstReady >= MobBridgeClockCadence.Interval ? firstReady : period;
            double crossing = horizon > 0f ? RoundUp(horizon / rate + phase, senderFrame) : 0d;
            double lastFine = horizon > 0f ? Math.Floor((crossing - phase) * rate / step + .000001d) * step : 0d;
            // Each iteration jumps to the required actual frontier, rather
            // than scanning physics ticks. Ordinary routes converge in a few
            // jumps, including batched physics at low frame rates.
            for (int attempt = 0; attempt < 32; ++attempt)
            {
                double published = candidate <= firstProgress ? firstReady :
                    firstPeriodic + RoundUp(Math.Max(0d, candidate / rate + phase - firstPeriodic), period);
                if (horizon > 0f && candidate > firstProgress && candidate <= lastFine + .0000001d &&
                    horizon - candidate < MobBridgeClockCadence.Interval)
                    published = Math.Min(published, Math.Max(firstReady, RoundUp(candidate / rate + phase, senderFrame)));
                double progressed = candidate <= firstProgress ? firstProgress :
                    Math.Max(firstProgress, Math.Floor((published - phase) * rate / step + .000001d) * step);
                double arrival = RoundUp(route + published, receiverFrame);
                double needed = MobBridgeTimeline.FakeRate * rate * arrival;
                if (progressed + .0000001d >= needed)
                    return (float)arrival;
                candidate = firstProgress + RoundUp(needed - firstProgress, step);
                if (!MobBridgeTimeline.Finite(candidate) || !MobBridgeTimeline.Finite(arrival)) break;
            }
            throw new NotSupportedException("Bridge arrival prediction could not reach a matching frontier.");
        }

        private static double RoundUp(double value, double quantum)
        { return value <= 0d ? 0d : Math.Ceiling(value / quantum - .000001d) * quantum; }
        internal static bool Positive(float value)
        { return MobBridgeTimeline.Finite(value) && value > 0f; }
    }
}
#endif
