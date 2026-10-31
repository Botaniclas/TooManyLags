using System;
using UnityEngine;

namespace TooManyLags
{
    // Presentation only. No AI, animation clock, physics, ZDO or event writes.
    internal static class MobVisualCurve
    {
        internal static float Window(float rtt)
        { return Math.Max(.04f, Math.Min(.5f, Finite(rtt) ? rtt : 0f)); }
        internal static float Recovery(float seconds)
        { return .04f + 1.4f * Window(seconds); }
        internal static float Recovery(float seconds, float lead, float speed)
        {
            float duration = Recovery(seconds);
            // Give a small residual time to disappear as mild slowing, rather
            // than stop-and-go. Still bounded by the same 500ms latency budget.
            return speed > .1f ? Math.Min(Recovery(.5f), Math.Max(duration, 6f * Math.Max(0f, lead) / speed)) : duration;
        }
        internal static float PredictionTime(float elapsed, float window)
        { return .5f * window * (1f - (float)Math.Exp(-Math.Max(0f, elapsed) / window)); }
        // Integrate a read-only locomotion forecast in slowed simulation time.
        // A brief dash/root-motion velocity brakes towards the actual movement
        // intent instead of becoming the velocity of the whole bridge.
        internal static Vector3 Displacement(Vector3 velocity, Vector3 intent, float seconds)
        {
            float t = Math.Max(0f, seconds);
            float momentum = (1f - (float)Math.Exp(-12f * t)) / 12f;
            return intent * t + (velocity - intent) * momentum;
        }
        internal static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        internal static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        internal static bool Finite(Quaternion value)
        { return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w); }

        internal static float FutureWeight(float elapsed, float duration)
        { float t = Math.Max(0f, Math.Min(1f, elapsed / Math.Max(.04f, duration))); return t * t * (3f - 2f * t); }
        internal static Vector3 ProgressiveTargetLead(Vector3 velocity, float horizon, float weight)
        {
            if (!Finite(velocity) || velocity.sqrMagnitude > 400f) return new Vector3();
            velocity.y = 0f;
            return velocity * Math.Max(0f, Math.Min(.5f, horizon)) * Math.Max(0f, Math.Min(1f, weight));
        }
        internal static float Yaw(Quaternion from, Quaternion to)
        {
            Vector3 a = from * new Vector3(0f, 0f, 1f), b = to * new Vector3(0f, 0f, 1f);
            return (float)(Math.Atan2(a.z * b.x - a.x * b.z, a.x * b.x + a.z * b.z) * (180.0 / Math.PI));
        }
        internal static float ConsumeTurn(float offset, float nativeStep)
        {
            return offset * nativeStep > 0f ? Math.Sign(offset) * Math.Max(0f, Math.Abs(offset) - Math.Abs(nativeStep)) : offset;
        }
        internal static float TurnLead(Quaternion origin, Quaternion root, Quaternion aim, float distance)
        {
            float yaw = Yaw(origin, aim);
            return ConsumeTurn(Math.Sign(yaw) * Math.Min(Math.Abs(yaw), Math.Min(10f, Math.Max(0f, distance))), Yaw(origin, root));
        }
        internal static float ReconcileTurn(float offset, float nativeStep, float speed, float dt)
        {
            float excess = Math.Max(0f, Math.Abs(nativeStep) - Math.Max(0f, speed) * Math.Max(0f, dt) - .5f);
            return ConsumeTurn(offset, Math.Sign(nativeStep) * excess);
        }
        internal static Quaternion TurnOffset(float degrees)
        { return Quaternion.AngleAxis(degrees, new Vector3(0f, 1f, 0f)); }
        internal static float BoundTurn(float offset, Quaternion root, Quaternion aim)
        {
            float remaining = Yaw(root, aim);
            return offset * remaining > 0f ? Math.Sign(offset) * Math.Min(Math.Abs(offset), Math.Abs(remaining)) : 0f;
        }
        internal static float FlightTurn(float speed, float factor, float desired, float seconds)
        {
            // Exact integral of native MoveTowards(factor, desired, dt) at one
            // factor per second, evaluated only in slowed presentation time.
            float t = Math.Max(0f, seconds), start = Math.Max(0f, Math.Min(1f, factor));
            desired = Math.Max(0f, Math.Min(1f, desired));
            float ramp = Math.Min(t, Math.Abs(desired - start));
            return Math.Max(0f, speed) * (start * ramp + .5f * Math.Sign(desired - start) * ramp * ramp + desired * (t - ramp));
        }

        // One bounded offset on top of native movement, not another world path.
        internal static Vector3 Lead(Vector3 prediction, Vector3 nativeProgress)
        {
            return Vector3.ClampMagnitude(Consume(prediction, nativeProgress), .25f);
        }
        internal static Vector3 Consume(Vector3 offset, Vector3 nativeProgress)
        {
            float length = offset.sqrMagnitude;
            if (length < 1e-10f) return new Vector3();
            float progress = (nativeProgress.x * offset.x + nativeProgress.y * offset.y + nativeProgress.z * offset.z) / length;
            return offset * Math.Max(0f, 1f - Math.Max(0f, progress));
        }
        internal static Vector3 Reconcile(Vector3 offset, Vector3 step, float speed, float dt)
        {
            // Ordinary native locomotion should keep moving on screen. Consume
            // only excess correction immediately; ease the remaining lead away.
            float distance = step.magnitude;
            if (distance < .00001f) return offset;
            float excess = Math.Max(0f, distance - Math.Max(0f, speed) * Math.Max(0f, dt) - .03f);
            return Consume(offset, step * (excess / distance));
        }
        internal static float Fade(float elapsed, float seconds)
        {
            float t = Math.Max(0f, Math.Min(1f, elapsed / seconds));
            return 1f - t * t * (3f - 2f * t);
        }
        // Smooth only excess native correction, never infer velocity from it.
        // One physics step of slack preserves ordinary flight/root motion.
        internal static float HeightCorrection(float offset, float step, float speed, float dt)
        {
            if (offset * step > 0f) offset = Math.Sign(offset) * Math.Max(0f, Math.Abs(offset) - Math.Abs(step));
            float excess = Math.Max(0f, Math.Abs(step) - (speed * (Math.Max(0f, dt) + .02f) + .03f));
            return Math.Max(-.25f, Math.Min(.25f, offset - Math.Sign(step) * excess));
        }
    }
}
