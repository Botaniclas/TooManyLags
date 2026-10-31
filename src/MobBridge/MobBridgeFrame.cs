#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using UnityEngine;

namespace TooManyLags
{
    // A clock is usable only together with the pose and event sampled with it.
    internal sealed class MobBridgeFrame
    {
        internal long Tick = -1;
        internal float Simulation, AiSimulation, SampleTime, AttackTime = -1f;
        internal bool AttackActive, AttackActiveObserved;
        internal string Attack = "";
        internal Vector3 Position, Velocity;
        internal Quaternion Rotation;
        internal readonly MobBridgePoseHistory History = new MobBridgePoseHistory();

        internal bool Observe(long tick, float simulation, float sampleTime, Vector3 position,
            Quaternion rotation, Vector3 velocity, float attackTime, string attack, float aiSimulation = -1f,
            bool attackActive = false)
        {
            if (aiSimulation < 0f) aiSimulation = simulation;
            if (tick < Tick || tick == Tick && attackActive == AttackActive &&
                (simulation <= Simulation || sampleTime <= SampleTime || aiSimulation != AiSimulation) ||
                Tick >= 0 && (simulation < Simulation || aiSimulation < AiSimulation || sampleTime < SampleTime) ||
                !Valid(simulation, sampleTime, position, rotation, velocity, attackTime, attack, aiSimulation)) return false;
            Tick = tick; Simulation = simulation; AiSimulation = aiSimulation; SampleTime = sampleTime;
            Position = position; Rotation = rotation; Velocity = velocity;
            AttackActive = attackActive; AttackActiveObserved = true;
            if (attackTime >= AttackTime) { AttackTime = attackTime; Attack = attack; }
            History.Add(simulation, position, rotation);
            return true;
        }

        internal static bool Valid(float simulation, float sampleTime, Vector3 position,
            Quaternion rotation, Vector3 velocity, float attackTime, string attack, float aiSimulation = -1f)
        {
            if (aiSimulation < 0f) aiSimulation = simulation;
            return MobBridgeTimeline.Finite(aiSimulation) && aiSimulation >= 0f && MobBridgeTimeline.Finite(simulation) && simulation >= 0f &&
                MobBridgeTimeline.Finite(sampleTime) && sampleTime >= 0f && Finite(position) && Finite(velocity) &&
                Finite(rotation) && MobBridgeTimeline.Finite(attackTime) && attackTime >= -1f &&
                attackTime <= simulation + .00001f && attack != null && attack.Length <= 256;
        }

        internal static bool Finite(Vector3 value)
        {
            return MobBridgeTimeline.Finite(value.x) && MobBridgeTimeline.Finite(value.y) &&
                MobBridgeTimeline.Finite(value.z);
        }
        private static bool Finite(Quaternion value)
        {
            float norm = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return MobBridgeTimeline.Finite(norm) && norm > .0001f;
        }
    }
}
#endif
