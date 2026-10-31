using UnityEngine;

namespace TooManyLags
{
    // Optional client diagnostics. No ownership, simulation, RPC or config writes.
    public static class OwnerDebugApi
    {
        // Bits: interactive, fixed static, destruction physics, tame, wild, loose.
        public static int Category(ZNetView view)
        {
            if (!view || !view.IsValid()) return 0;
            Character actor = view.GetComponent<Character>();
            if (actor) return actor is Player || view.m_distant ? 0 : actor.IsTamed() ? 8 : 16;
            Kinds kind = ObjectCatalog.Classify(view.gameObject);
            if (kind == Kinds.None || kind == Kinds.WorldMarkers) return 0;
            if (kind == Kinds.LooseItems) return 32;
            if (kind == Kinds.StaticBuildings) return 2;
            if (kind == Kinds.PhysicsObjects) return 4;
            return 1;
        }

        // -1: outside ai_test; 0: old owner; 1: new owner; 2: playback; 3: correction.
        public static int Phase(ZDO data)
        {
#if TML_DEBUG_SYSTEM
            int phase = FgnSoloCombatTest.DebugPhase(data);
            if (phase < 0) return -1;
            int visual = MobBridgeVisual.DebugPhase(data);
            return visual >= 0 ? visual : phase;
#else
            return -1;
#endif
        }
    }
}
