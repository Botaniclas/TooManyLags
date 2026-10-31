#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using HarmonyLib;
using NativeMobPreviewAudit;
using UnityEngine;

namespace TooManyLags
{
    // Source-cutoff state. Snapshots remain local to the old owner; the network
    // checkpoint serializes the same logical fields for the destination.
    internal sealed class MobBridgeSeed
    {
        internal readonly ShadowSnapshot<MonsterAI> Ai;
        internal readonly ShadowSnapshot<Humanoid> Controls;
        internal readonly UnityEngine.Random.State RandomState;
        internal readonly ZDOID Target;
        internal readonly uint Seed;
        internal readonly double CapturedAt;
        internal readonly float GameTime;
        internal readonly MobBridgeWeaponState Weapons;
        internal readonly bool InAttack;

        internal MobBridgeSeed(NativeSteeringPlan plan, MonsterAI ai, Humanoid actor,
            Character target, uint seed, double now)
        {
            if (plan == null || !ai || !actor || !target) throw new ArgumentNullException("bridge seed");
            if (!MobBridgeTimeline.Finite(now)) throw new ArgumentException("Invalid seed clock.");
            Ai = ShadowSnapshot<MonsterAI>.Create(ai, plan.AiFields, 256);
            Controls = ShadowSnapshot<Humanoid>.Create(actor, plan.ControlFields, 0);
            RandomState = MobBridgeRandom.Create(seed);
            Target = target.GetZDOID();
            Seed = seed == 0u ? 1u : seed;
            CapturedAt = now;
            GameTime = Time.time;
            Weapons = new MobBridgeWeaponState(actor);
            InAttack = actor.InAttack();
        }
    }
}
#endif
