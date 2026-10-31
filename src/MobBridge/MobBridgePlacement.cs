#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using UnityEngine;

namespace TooManyLags
{
    // Cosmetic offsets are not physical simulation results. Sweep the actual
    // movement capsule when applying them; never move a gameplay Rigidbody.
    // Clean native playback never constructs or calls this resolver.
    internal sealed class MobBridgePlacement
    {
        private readonly Character actor;
        private readonly CapsuleCollider capsule;
        private readonly Vector3 localPosition;
        private readonly Quaternion localRotation;
        private readonly int mask, iterations;
        private Collider[] overlaps = new Collider[16];
        private RaycastHit[] hits = new RaycastHit[16];
        private Vector3 a, b;
        private float radius;

        internal MobBridgePlacement(Character actor, int physicsLayer = -1)
        {
            this.actor = actor; capsule = actor.GetCollider();
            localPosition = actor.transform.InverseTransformPoint(capsule.transform.position);
            localRotation = Quaternion.Inverse(actor.transform.rotation) * capsule.transform.rotation;
            int layers = 0;
            int ownerLayer = physicsLayer >= 0 ? physicsLayer : actor.gameObject.layer;
            for (int i = 0; i < 32; ++i)
                if (!Physics.GetIgnoreLayerCollision(ownerLayer, i)) layers |= 1 << i;
            mask = layers;
            Rigidbody body = actor.GetComponent<Rigidbody>();
            iterations = body ? Math.Max(1, body.solverIterations) : 6;
        }

        private bool Blocks(Collider other)
        {
            if (!other || other.isTrigger || other.transform.IsChildOf(actor.transform)) return false;
            Character owner;
            if (MobBridgeHit.TryOwner(other, out owner))
            {
                if (ReferenceEquals(owner, actor)) return false;
                other = MobBridgeHit.NativeCollider(owner, other);
            }
            return other && !Physics.GetIgnoreCollision(capsule, other);
        }

        private Vector3 Shape(Vector3 position, Quaternion rotation)
        {
            Vector3 signedScale = capsule.transform.lossyScale;
            Vector3 scale = signedScale;
            scale = new Vector3(Math.Abs(scale.x), Math.Abs(scale.y), Math.Abs(scale.z));
            Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 2 ? Vector3.forward : Vector3.up;
            float along = capsule.direction == 0 ? scale.x : capsule.direction == 2 ? scale.z : scale.y;
            float across = capsule.direction == 0 ? Math.Max(scale.y, scale.z) :
                capsule.direction == 2 ? Math.Max(scale.x, scale.y) : Math.Max(scale.x, scale.z);
            radius = capsule.radius * across;
            Vector3 colliderPosition = position + rotation * Vector3.Scale(localPosition, actor.transform.lossyScale);
            Quaternion colliderRotation = rotation * localRotation;
            Vector3 center = colliderPosition + colliderRotation * Vector3.Scale(capsule.center, signedScale);
            Vector3 offset = colliderRotation * axis * Math.Max(0f, capsule.height * along * .5f - radius);
            a = center + offset; b = center - offset;
            return colliderPosition;
        }

        private Vector3 Separate(Vector3 position, Quaternion rotation)
        {
            for (int pass = 0; pass < iterations; ++pass)
            {
                Vector3 colliderPosition = Shape(position, rotation);
                int count;
                while (true)
                {
                    count = Physics.OverlapCapsuleNonAlloc(a, b, radius, overlaps, mask, QueryTriggerInteraction.Ignore);
                    if (count < overlaps.Length) break;
                    Array.Resize(ref overlaps, checked(overlaps.Length * 2));
                }
                bool moved = false;
                for (int i = 0; i < count; ++i)
                {
                    Collider other = overlaps[i];
                    overlaps[i] = null;
                    if (!Blocks(other)) continue;
                    Vector3 direction; float distance;
                    if (!Physics.ComputePenetration(capsule, colliderPosition, rotation * localRotation,
                        other, other.transform.position, other.transform.rotation, out direction, out distance) || distance <= 0f) continue;
                    position += direction * distance; moved = true;
                    colliderPosition = Shape(position, rotation);
                }
                if (!moved) break;
            }
            return position;
        }

        internal Vector3 Resolve(Vector3 from, Vector3 desired, Quaternion rotation)
        {
            // Preserve exact no-op behavior, including stationary water/flight.
            if (desired.Equals(from)) return from;
            Vector3 position = Separate(from, rotation), remaining = desired - position;
            float skin = Math.Max(0f, capsule.contactOffset);
            for (int pass = 0; pass < iterations; ++pass)
            {
                float distance = remaining.magnitude;
                if (distance <= 0f) break;
                Vector3 direction = remaining / distance;
                Shape(position, rotation);
                int count;
                while (true)
                {
                    count = Physics.CapsuleCastNonAlloc(a, b, radius, direction, hits, distance + skin, mask, QueryTriggerInteraction.Ignore);
                    if (count < hits.Length) break;
                    Array.Resize(ref hits, checked(hits.Length * 2));
                }
                float closest = Single.PositiveInfinity; Vector3 normal = Vector3.zero;
                for (int i = 0; i < count; ++i)
                {
                    RaycastHit hit = hits[i];
                    if (!Blocks(hit.collider) || Vector3.Dot(direction, hit.normal) >= 0f || hit.distance >= closest) continue;
                    closest = hit.distance; normal = hit.normal;
                }
                if (Single.IsPositiveInfinity(closest)) { position = pass == 0 ? desired : position + remaining; break; }
                float travel = Math.Max(0f, Math.Min(distance, closest - skin));
                position += direction * travel;
                remaining = Vector3.ProjectOnPlane(remaining - direction * travel, normal);
            }
            return Separate(position, rotation);
        }
    }
}
#endif
