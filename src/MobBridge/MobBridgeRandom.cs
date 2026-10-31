#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using UnityEngine;

namespace TooManyLags
{
    // Random state is isolated per handoff. The handoff seed is enough for both
    // endpoints to reproduce the same MobBridge decision sequence.
    internal static class MobBridgeRandom
    {
        internal static uint NewSeed()
        {
            uint seed = (uint)Environment.TickCount;
            seed ^= (uint)(Time.realtimeSinceStartup * 1000f);
            seed = seed * 1664525u + 1013904223u;
            return seed == 0u ? 1u : seed;
        }

        internal static UnityEngine.Random.State Create(uint seed)
        {
            UnityEngine.Random.State original = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(unchecked((int)seed));
                return UnityEngine.Random.state;
            }
            finally { UnityEngine.Random.state = original; }
        }
    }
}
#endif
