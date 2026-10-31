#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using UnityEngine;

namespace TooManyLags
{
    internal static class MobBridgePresentationPoint
    {
        internal static Vector3 ModelPosition(Vector3 root, Quaternion rotation, Vector3 local, Vector3 scale)
        {
            return root + rotation * new Vector3(local.x * scale.x, local.y * scale.y, local.z * scale.z);
        }
        // Mirrors Character.GetTopPoint exactly, but with the fake presentation
        // root instead of the replicated gameplay root.
        internal static Vector3 TopPoint(Vector3 root, Quaternion rootRotation,
            Quaternion visualLocalRotation, Vector3 colliderCenter, float colliderHeight,
            Vector3 lossyScale)
        {
            Vector3 center = new Vector3(
                colliderCenter.x * lossyScale.x,
                colliderCenter.y * lossyScale.y,
                colliderCenter.z * lossyScale.z);
            return root + rootRotation * center +
                (rootRotation * visualLocalRotation) * Vector3.up * (colliderHeight * .5f);
        }
        internal static float OwnerGainWeight(float progress)
        {
            float t = MobBridgeTimeline.Finite(progress) ? Math.Max(0f, Math.Min(1f, progress)) : 1f;
            return t * t * (3f - 2f * t);
        }
        internal static Vector3 OwnerGainPosition(Vector3 native, Vector3 offset, float progress)
        {
            return native + offset * (1f - OwnerGainWeight(progress));
        }
        internal static Quaternion OwnerGainRotation(Quaternion native, Quaternion offset, float progress)
        {
            return Quaternion.Slerp(Quaternion.identity, offset, 1f - OwnerGainWeight(progress)) * native;
        }
    }
}
#endif
