#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using UnityEngine;

namespace TooManyLags
{
    // Post-takeover correction only. Real simulation owns both clocks and root
    // motion immediately; the displayed trajectory joins real motion through
    // a zero-slope endpoint blend, with observed movement direction protection.
    internal sealed class MobBridgeTakeover
    {
        private const float MaximumSettleSeconds = .5f;
        private readonly double started;
        private readonly float duration;
        private readonly Vector3 fakePosition, fakeVelocity;
        private readonly Quaternion rotationOffset;
        private double last;
        private Vector3 lastRealPosition, lastDisplayedPosition;
        private Vector3 movementDirection;
        private Vector3 previousDisplayedPosition;
        private double previousSample;
        private OwnershipCorrection ownership;
        private readonly OwnershipCorrection motion;
        private sealed class OwnershipCorrection
        {
            internal Vector3 Position, Velocity, Direction;
            private readonly double started;
            private readonly float duration;
            private readonly Quaternion rotationOffset;
            private readonly Vector3 correction, relativeVelocity;
            private bool allowReverse;
            private double last;
            internal OwnershipCorrection(double now, double sampleTime, float seconds, Vector3 position, Vector3 velocity,
                Quaternion displayedRotation, Quaternion realRotation, Vector3 direction, Vector3 root, Vector3 realVelocity,
                bool reverse = false)
            {
                started = now; last = sampleTime; duration = seconds; Position = position; Velocity = velocity; Direction = direction;
                correction = position - root; relativeVelocity = velocity - realVelocity;
                allowReverse = reverse;
                rotationOffset = Quaternion.Inverse(realRotation) * displayedRotation;
            }
            internal float Remaining(double now) { return (float)Math.Max(0d, started + duration - now); }
            internal float Progress(double now)
            { float t = duration <= 0f ? 1f : Math.Max(0f, Math.Min(1f, (float)(now - started) / duration)); return t * t * (3f - 2f * t); }
            internal bool Finished(double now) { return now >= started + duration - .000001d; }
            internal bool Active(double now) { return now >= started && !Finished(now); }
            internal Quaternion Rotation(Quaternion root, double now)
            { return root * Quaternion.Slerp(Quaternion.identity, rotationOffset, 1f - Progress(now)); }
            internal void AdvancePosition(Vector3 root, double now, Vector3? native)
            {
                if (now <= last) return;
                float dt = (float)(now - last);
                Vector3 realVelocity = native.HasValue && Finite(native.Value) ? native.Value : Vector3.zero;
                bool nativeReversal = allowReverse;
                allowReverse = false;
                if (realVelocity.sqrMagnitude > .000001f)
                {
                    Vector3 nextDirection = realVelocity.normalized;
                    nativeReversal = nativeReversal || Direction.sqrMagnitude > .000001f &&
                        Vector3.Dot(Direction, nextDirection) < -.000001f;
                    Direction = nextDirection;
                }
                if (nativeReversal)
                {
                    // Preserve the displayed tangent for the first frame of a
                    // genuine native reversal; the curve can turn next frame.
                    Position += Velocity * dt;
                    last = now;
                    allowReverse = false;
                    return;
                }
                // Fixed correction-space curve. It starts with the fake
                // trajectory and reaches the live root at the capped deadline.
                float t = duration <= 0f ? 1f : Math.Max(0f, Math.Min(1f, (float)(now - started) / duration));
                float t2 = t * t, t3 = t2 * t;
                Vector3 nextCorrection = correction * (2f * t3 - 3f * t2 + 1f) +
                    relativeVelocity * (duration * (t3 - 2f * t2 + t));
                Vector3 nextRelativeVelocity = correction * ((6f * t2 - 6f * t) / Math.Max(duration, .000001f)) +
                    relativeVelocity * (3f * t2 - 4f * t + 1f);
                Vector3 next = root + nextCorrection;
                Vector3 nextVelocity = realVelocity + nextRelativeVelocity;
                // A genuine native reversal may smoothly brake the preceding
                // visible motion. Otherwise do not pull against actual travel;
                // when the successor stops, retain position and wait.
                if (Direction.sqrMagnitude > .000001f && !nativeReversal)
                {
                    Vector3 step = next - Position;
                    float backward = step.x * Direction.x + step.y * Direction.y + step.z * Direction.z;
                    if (backward < 0f)
                    {
                        next -= Direction * backward;
                        float reverseVelocity = nextVelocity.x * Direction.x + nextVelocity.y * Direction.y + nextVelocity.z * Direction.z;
                        if (reverseVelocity < 0f) nextVelocity -= Direction * reverseVelocity;
                    }
                }
                Position = next; Velocity = nextVelocity; last = now;
            }
        }

        internal MobBridgeTakeover(double now, float seconds, Vector3 fakePosition, Quaternion fakeRotation,
            Vector3 realPosition, Quaternion realRotation, Vector3 realVelocity = default(Vector3),
            Vector3? displayedVelocity = null)
        {
            if (!MobBridgeTimeline.Finite(now) || !MobBridgeTimeline.Finite(seconds) || seconds < 0f)
                throw new ArgumentException("Invalid takeover window.");
            if (!Finite(fakePosition) || !Finite(fakeRotation) ||
                !Finite(realPosition) || !Finite(realRotation))
                throw new ArgumentException("Invalid takeover pose.");
            started = last = now;
            duration = seconds;
            this.fakePosition = fakePosition;
            fakeVelocity = displayedVelocity ?? realVelocity * MobBridgeTimeline.FakeRate;
            if (!Finite(fakeVelocity)) throw new ArgumentException("Invalid displayed velocity.");
            rotationOffset = Quaternion.Inverse(realRotation) * fakeRotation;
            lastRealPosition = realPosition;
            lastDisplayedPosition = fakePosition;
            previousDisplayedPosition = fakePosition; previousSample = now;
            // A replica can retain its root for several frames after the
            // observed owner frontier permits takeover. Seed from that same
            // owner's velocity: waiting for a root delta leaves the first
            // correction frames free to pull a moving mob backwards.
            if (Finite(realVelocity) && realVelocity.sqrMagnitude > .000001f)
                movementDirection = realVelocity.normalized;
            if (displayedVelocity.HasValue)
                motion = new OwnershipCorrection(now, now, seconds, fakePosition, fakeVelocity,
                    fakeRotation, realRotation, movementDirection.sqrMagnitude > .000001f ? movementDirection : fakeVelocity.normalized,
                    realPosition, realVelocity);
        }

        internal static float SettleForLatency(float latency)
        {
            // The fake simulation is stretched across 2*latency. The final blend
            // is exactly one quarter of that stretch: latency / 2.
            return Math.Max(.001f, latency * .5f);
        }

        internal static float SettleForPlayback(float playbackSeconds, float distance, float yawAngle,
            float movementSpeed, float turnSpeed)
        {
            // Final takeover now uses half the actual stretched playback.
            return SettleForPose(playbackSeconds, distance, yawAngle, movementSpeed, turnSpeed, Double.PositiveInfinity);
        }

        internal static float SettleForPlaybackCapped(float playbackSeconds, float distance, float yawAngle,
            float movementSpeed, float turnSpeed, float ownerReadySeconds = 0f, float minimumSeconds = 0f)
        {
            // The simulation horizon remains the normal duration. A large
            // residual may use one additional horizon of correction, bounded
            // to half a second. The owner-ready floor covers the next native
            // simulation/publish interval when latency itself is very short.
            float simulation = SettleForLatency(playbackSeconds);
            float maximum = Math.Min(MaximumSettleSeconds, simulation * 2f);
            float duration = Math.Max(simulation,
                SettleForPlayback(playbackSeconds, distance, yawAngle, movementSpeed, turnSpeed));
            if (MobBridgeTimeline.Finite(ownerReadySeconds) && ownerReadySeconds > 0f)
                duration = Math.Max(duration, ownerReadySeconds);
            duration = Math.Min(maximum, duration);
            // A sub-frame correction is evaluated as one render frame. Keep it
            // alive long enough to render at least one interpolated frame.
            return float.IsNaN(minimumSeconds) || float.IsInfinity(minimumSeconds) || minimumSeconds < 0f ?
                duration : Math.Max(duration, minimumSeconds);
        }

        internal static float SettleForPose(float latency, float distance, float yawAngle,
            float movementSpeed, float turnSpeed, double availableTime)
        {
            float duration = SettleForLatency(latency);
            float speed = MobBridgeTimeline.Finite(movementSpeed) ? Math.Max(.1f, movementSpeed) : .1f;
            float turn = MobBridgeTimeline.Finite(turnSpeed) ? Math.Max(30f, turnSpeed) : 30f;
            if (MobBridgeTimeline.Finite(distance))
                duration = Math.Max(duration, 1.5f * Math.Abs(distance) / speed);
            if (MobBridgeTimeline.Finite(yawAngle))
                duration = Math.Max(duration, 1.5f * Math.Abs(yawAngle) / turn);
            // Completion is geometric, not an expiring wall-time budget.
            // Base timing already has a numerical 1ms epsilon. A second
            // fixed-physics-step floor stretches clean low-latency joins even
            // when position/rotation need no additional correction.
            return duration;
        }

        internal bool Active(double now)
        {
            if (ownership != null) return ownership.Active(now);
            return MobBridgeTimeline.Finite(now) && now >= started && now < started + duration;
        }

        internal bool Finished(double now)
        {
            if (ownership != null) return ownership.Finished(now);
            return !MobBridgeTimeline.Finite(now) || now >= started + duration - .000001d;
        }

        internal bool Settled(double now, Vector3 realRoot)
        {
            return Finished(now) && (realRoot - lastDisplayedPosition).sqrMagnitude <= .000000000001f;
        }

        // Tick advances once; HUD, hitbox and camera reads never consume the
        // correction again or replace the previous observed root position.
        internal Vector3 Position { get { return lastDisplayedPosition; } }
        internal void ConstrainPosition(Vector3 position)
        {
            lastDisplayedPosition = position;
            if (ownership != null)
            { ownership.Position = position; ownership.Velocity = DisplayedVelocity; }
            else if (motion != null)
            { motion.Position = position; motion.Velocity = DisplayedVelocity; }
        }
        internal float Remaining(double now)
        { return ownership == null ? (float)Math.Max(0d, started + duration - now) : ownership.Remaining(now); }
        internal float OwnershipWeight(double now)
        { return ownership == null ? 1f : ownership.Progress(now); }
        internal Vector3 DisplayedVelocity
        {
            get
            {
                double dt = last - previousSample;
                return dt > .000001d ? (lastDisplayedPosition - previousDisplayedPosition) / (float)dt :
                    ownership == null ? fakeVelocity : ownership.Velocity;
            }
        }
        internal void RebaseOwnership(double now, float seconds, Quaternion displayedRotation,
            Vector3 realPosition, Quaternion realRotation, Vector3 realVelocity)
        {
            Vector3 direction = ownership == null ? movementDirection : ownership.Direction;
            Vector3 velocity = DisplayedVelocity;
            if (!Finite(realPosition) || !Finite(realRotation) || !Finite(realVelocity) || !Finite(displayedRotation) ||
                !MobBridgeTimeline.Finite(now) || now < last || !MobBridgeTimeline.Finite(seconds) || seconds < 0f)
                throw new ArgumentException("Invalid ownership correction.");
            // A stopped successor must not turn an outstanding forward offset
            // into reverse travel. Keep the prior direction until real motion
            // establishes a new one; geometric settlement can wait if needed.
            bool reverse = direction.sqrMagnitude > .000001f && realVelocity.sqrMagnitude > .000001f &&
                Vector3.Dot(direction, realVelocity) < 0f;
            if (realVelocity.sqrMagnitude > .000001f) direction = realVelocity.normalized;
            else if (direction.sqrMagnitude <= .000001f) direction = velocity.normalized;
            ownership = new OwnershipCorrection(now, last, seconds, lastDisplayedPosition, velocity, displayedRotation, realRotation, direction,
                realPosition, realVelocity, reverse);
        }

        internal void AdvancePosition(Vector3 realRoot, double now, Vector3? authoritativeVelocity = null)
        {
            if (!Finite(realRoot) || !MobBridgeTimeline.Finite(now) || now < last) return;
            if (now > last) { previousSample = last; previousDisplayedPosition = lastDisplayedPosition; }
            last = now;
            if (ownership != null)
            {
                ownership.AdvancePosition(realRoot, now, authoritativeVelocity);
                lastDisplayedPosition = ownership.Position; lastRealPosition = realRoot;
                return;
            }
            if (motion != null)
            {
                motion.AdvancePosition(realRoot, now, authoritativeVelocity);
                lastDisplayedPosition = motion.Position; lastRealPosition = realRoot;
                return;
            }
            float progress = Progress(now);
            // Continue the displayed trajectory at entry. Smoothstep has zero
            // slope at both ends: initial velocity stays fake, final velocity
            // becomes real, even when both initial positions already match.
            Vector3 continuation = fakePosition + fakeVelocity * (float)Math.Max(0d, now - started);
            Vector3 result = continuation * (1f - progress) + realRoot * progress;
            Vector3 rootDelta = realRoot - lastRealPosition;
            if (authoritativeVelocity.HasValue)
            {
                Vector3 velocity = authoritativeVelocity.Value;
                if (Finite(velocity) && velocity.sqrMagnitude > .000001f)
                    movementDirection = velocity.normalized;
            }
            else if (rootDelta.sqrMagnitude > .000001f)
                movementDirection = rootDelta.normalized;
            // Replicas retain their root between physics/network updates.
            // An unchanged root must not disable the established direction.
            if (movementDirection.sqrMagnitude > .000001f)
            {
                Vector3 direction = movementDirection;
                Vector3 delta = result - lastDisplayedPosition;
                float backward = delta.x * direction.x + delta.y * direction.y + delta.z * direction.z;
                if (backward < 0f) result -= direction * backward;
            }
            lastRealPosition = realRoot;
            lastDisplayedPosition = result;
        }

        internal Quaternion Rotation(Quaternion realRoot, double now)
        {
            if (ownership != null) return ownership.Rotation(realRoot, now);
            Quaternion correction = Quaternion.Slerp(Quaternion.identity, rotationOffset,
                1f - Progress(now));
            return realRoot * correction;
        }
        internal float AnimationWeight(double now) { return Progress(now); }
        internal float AnimationTime(float simulation, double now)
        {
            float elapsed = (float)Math.Max(0d, now - started);
            if (duration <= 0f) return simulation + elapsed;
            float ramp = Math.Min(elapsed, duration);
            return simulation + MobBridgeTimeline.FakeRate * ramp +
                (1f - MobBridgeTimeline.FakeRate) * ramp * ramp / (2f * duration) + Math.Max(0f, elapsed - duration);
        }

        private float Progress(double now)
        {
            if (!MobBridgeTimeline.Finite(now) || now <= started) return 0f;
            now = Math.Max(last, now);
            if (duration <= 0f || now >= started + duration - .000001d) return 1f;
            float t = (float)((now - started) / duration);
            // Monotone, zero-slope endpoints. No overshoot and no restart after
            // duplicate render callbacks or repeated camera passes.
            return t * t * (3f - 2f * t);
        }

        private static bool Finite(Vector3 value)
        {
            return MobBridgeTimeline.Finite(value.x) && MobBridgeTimeline.Finite(value.y) &&
                MobBridgeTimeline.Finite(value.z);
        }

        private static bool Finite(Quaternion value)
        {
            return MobBridgeTimeline.Finite(value.x) && MobBridgeTimeline.Finite(value.y) &&
                MobBridgeTimeline.Finite(value.z) && MobBridgeTimeline.Finite(value.w);
        }
    }
}
#endif
