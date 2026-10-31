#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using UnityEngine;

namespace TooManyLags
{
    // Supplies a forward target prediction for the destination's matching
    // simulation tick. Both local and remote targets are projected ahead.
    internal sealed class MobBridgeTargetTimeline
    {
        private const float VelocityResponse = .08f;
        private const float IntentResponse = .12f;
        private readonly Character target;
        private float latency, route, ownerGameRate, sourceGameRate;
        private readonly double started;
        private readonly bool localTarget;
        private Vector3 velocity, intentVelocity, localPosition, localVelocity;
        private Vector3 observedPosition;
        private Quaternion observedRotation;
        private float localSimulation;
        private double lastObserved;

        internal MobBridgeTargetTimeline(Character target, double now, float latencySeconds, float ownerGameRate = 1f, float sourceGameRate = 1f)
        {
            if (!target) throw new ArgumentNullException("target");
            this.target = target; started = lastObserved = now;
            latency = route = Math.Max(.001f, latencySeconds);
            ObserveRates(ownerGameRate, sourceGameRate);
            localTarget = ReferenceEquals(target, Player.m_localPlayer);
            velocity = target.GetVelocity();
            if (!Finite(velocity)) velocity = new Vector3();
            intentVelocity = localVelocity = velocity;
            localPosition = target.transform.position;
            observedPosition = localPosition; observedRotation = target.transform.rotation;
        }

        internal void ObserveRates(float ownerRate, float sourceRate)
        {
            ownerGameRate = MobBridgeTimeline.Finite(ownerRate) ? Math.Max(.001f, ownerRate) : 1f;
            sourceGameRate = MobBridgeTimeline.Finite(sourceRate) ? Math.Max(.001f, sourceRate) : 1f;
        }

        internal void ObserveRoute(float seconds)
        {
            if (MobBridgeTimeline.Finite(seconds) && seconds > 0f) route = seconds;
        }

        internal void Observe(double now)
        {
            if (now < lastObserved) return;
            float dt = (float)Math.Max(.001d, now - lastObserved);
            lastObserved = now;
            observedPosition = target.transform.position; observedRotation = target.transform.rotation;
            latency += (route - latency) * (1f - (float)Math.Exp(-dt / VelocityResponse));
            Vector3 measured = target.GetVelocity();
            if (!Finite(measured)) measured = new Vector3();
            float blend = 1f - (float)Math.Exp(-dt / VelocityResponse);
            velocity += (measured - velocity) * blend;
            Vector3 wanted = measured;
            Vector3 move = target.GetMoveDir();
            Vector3 planarMove = new Vector3(move.x, 0f, move.z);
            if (Finite(move) && planarMove.sqrMagnitude > .0001f)
            {
                Vector3 planarVelocity = new Vector3(measured.x, 0f, measured.z);
                wanted = planarMove.normalized * planarVelocity.magnitude;
                wanted.y = measured.y;
            }
            float intentBlend = 1f - (float)Math.Exp(-dt / IntentResponse);
            intentVelocity += (wanted - intentVelocity) * intentBlend;
        }

        internal void Prepare(float simulationTime, out Vector3 lead, out Vector3 predictedVelocity,
            out Quaternion rotation)
        {
            lead = new Vector3();
            predictedVelocity = velocity;
            rotation = observedRotation;
            if (localTarget)
            {
                // In the server test, this player's input reaches the new owner
                // later. Predict forward from the cutoff, not live-position +
                // elapsed fake time (which counts its movement twice).
                float step = Math.Max(0f, simulationTime - localSimulation) * sourceGameRate / ownerGameRate;
                float decayLocal = (float)Math.Exp(-step / IntentResponse);
                localPosition += localVelocity * IntentResponse * (1f - decayLocal) +
                    intentVelocity * (step - IntentResponse * (1f - decayLocal));
                localVelocity = intentVelocity + (localVelocity - intentVelocity) * decayLocal;
                localSimulation = Math.Max(localSimulation, simulationTime);
                lead = localPosition - target.transform.position;
                predictedVelocity = localVelocity;
                return;
            }
            float horizon = Math.Max(0f, ownerGameRate * (latency - (float)(lastObserved - started)) + simulationTime);
            // Integrate velocity turning exponentially from the measured
            // direction toward the target's current move intent. This reaches
            // a fast reversal without projecting the old direction for the
            // entire horizon or snapping the predicted point.
            float decay = (float)Math.Exp(-horizon / IntentResponse);
            lead = observedPosition - target.transform.position + velocity * IntentResponse * (1f - decay) +
                intentVelocity * (horizon - IntentResponse * (1f - decay));
            predictedVelocity = intentVelocity + (velocity - intentVelocity) * decay;
        }

        private static bool Finite(Vector3 value)
        {
            return MobBridgeTimeline.Finite(value.x) && MobBridgeTimeline.Finite(value.y) &&
                MobBridgeTimeline.Finite(value.z);
        }
    }
}
#endif
