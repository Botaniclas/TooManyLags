#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using NativeMobPreviewAudit;
using UnityEngine;

namespace TooManyLags
{
    // Pose producer only. Locomotion and contacts come from native Character
    // methods and Unity's private-scene Rigidbody simulation, not a motor copy.
    internal sealed class MobBridgeBody : IDisposable
    {
        private readonly MobBridgeNativeActor recording;
        private readonly Character target;
        private Vector3 targetLead;
        private Quaternion targetRotation;
        private bool disposed;
        internal Vector3 Position, Velocity;
        internal readonly int PhysicsLayer;
        internal Quaternion Rotation;
        internal bool Swimming { get; private set; }
        internal float DesiredSpeed { get; private set; }
        internal bool CanMove { get; private set; }
        internal string ContactName { get { return recording.ContactName; } }
        internal MobBridgeAnimation Animation { get { return recording.Animation; } }
        internal bool Flying { get { return recording.Flying; } }
        internal bool Staggering { get { return recording.Staggering; } }

        internal MobBridgeBody(Character actor, Character target, Vector3 origin, Quaternion rotation,
            Vector3 velocity, float seconds, MobStatusState.Shadow status, MobBridgeCost cost)
        {
            this.target = target; targetRotation = target.transform.rotation;
            Position = origin; Rotation = rotation; Velocity = velocity;
            recording = new MobBridgeNativeActor((Humanoid)actor, origin, rotation, velocity, seconds, status, cost);
            PhysicsLayer = recording.Body.gameObject.layer;
            try { recording.Observe(target); }
            catch { recording.Dispose(); throw; }
            Swimming = actor.IsSwimming(); CanMove = actor.CanMove();
        }
        internal void TargetInput(Vector3 lead, Quaternion rotation) { targetLead = lead; targetRotation = rotation; }
        internal void ObserveInputs() { if (!disposed) recording.Observe(target); }
        internal bool Move(SteeringFrame command, float time, float dt, bool controller)
        {
            if (disposed) return false;
            recording.Step(command, time, dt, controller, target, targetLead, targetRotation);
            Position = recording.Body.position; Rotation = recording.Body.rotation; Velocity = recording.Body.linearVelocity;
            Swimming = recording.Swimming; CanMove = recording.CanMove;
            DesiredSpeed = recording.ControlVelocity.magnitude;
            return Finite(Position) && Finite(Velocity);
        }
        private static bool Finite(Vector3 value)
        { return MobBridgeTimeline.Finite(value.x) && MobBridgeTimeline.Finite(value.y) && MobBridgeTimeline.Finite(value.z); }
        public void Dispose() { if (disposed) return; disposed = true; recording.Dispose(); }
    }
}
#endif
