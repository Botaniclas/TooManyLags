#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using UnityEngine;

namespace TooManyLags
{
    internal static class MobBridgeHitFilter
    {
        internal static bool Accepts(HitData hit, Collider native, Collider proxy)
        {
            if (hit == null || hit.m_hitCollider == null) return true;
            if (ReferenceEquals(hit.m_hitCollider, proxy)) return true;
            return !ReferenceEquals(hit.m_hitCollider, native);
        }
    }
}
#endif
