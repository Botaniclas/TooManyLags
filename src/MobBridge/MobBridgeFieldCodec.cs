#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NativeMobPreviewAudit;
using UnityEngine;

namespace TooManyLags
{
    // Deterministic schema for the audited MonsterAI/Character state. Field
    // order is sorted, so source and destination encode the same value stream.
    internal static class MobBridgeFieldCodec
    {
        private static NativeSteeringPlan cachedPlan;
        private static FieldInfo[] cachedFields;
        private static uint cachedHash;

        internal static FieldInfo[] Fields(NativeSteeringPlan plan)
        {
            if (ReferenceEquals(plan, cachedPlan) && cachedFields != null) return cachedFields;
            List<FieldInfo> fields = new List<FieldInfo>();
            fields.AddRange(plan.AiFields);
            fields.AddRange(plan.ControlFields);
            foreach (string name in new[] { "m_currentVel", "m_currentTurnVel", "m_currentTurnVelChange", "currentRotSpeedFactor", "m_rootMotion" })
            {
                FieldInfo field = AccessTools.Field(typeof(Character), name);
                if (field == null) throw new NotSupportedException("Missing bridge character state: " + name);
                if (!fields.Contains(field)) fields.Add(field);
            }
            fields.Sort(delegate(FieldInfo a, FieldInfo b)
            {
                int compare = StringComparer.Ordinal.Compare(a.DeclaringType.FullName, b.DeclaringType.FullName);
                return compare != 0 ? compare : StringComparer.Ordinal.Compare(a.Name, b.Name);
            });
            foreach (FieldInfo field in fields)
                if (!Supported(field.FieldType))
                    throw new NotSupportedException("Unsupported bridge state: " + field.DeclaringType.Name + "." + field.Name +
                        " (" + field.FieldType.FullName + ")");
            uint hash = 2166136261u;
            foreach (FieldInfo field in fields)
            {
                Mix(ref hash, field.DeclaringType.FullName);
                Mix(ref hash, ".");
                Mix(ref hash, field.Name);
                Mix(ref hash, ":");
                Mix(ref hash, field.FieldType.FullName);
                Mix(ref hash, ";");
            }
            cachedPlan = plan; cachedFields = fields.ToArray(); cachedHash = hash;
            return cachedFields;
        }

        internal static uint Hash(NativeSteeringPlan plan) { Fields(plan); return cachedHash; }

        internal static void Write(ZPackage packet, NativeSteeringPlan plan, MonsterAI ai, Humanoid actor,
            float sourceTime)
        {
            FieldInfo[] fields = Fields(plan);
            packet.Write((short)fields.Length);
            foreach (FieldInfo field in fields)
            {
                object target = Owner(field, ai, actor);
                object value = field.GetValue(target);
                if (field.Name == "m_targetCreature")
                    packet.Write(new ZDOID());
                else if (field.Name == "m_targetStatic")
                    packet.Write(value != null);
                else if (field.Name == "m_nearFireArea")
                    packet.Write(value != null);
                else WriteField(packet, field, value, sourceTime);
            }
            MobBridgeWeaponState.Write(packet, actor, sourceTime);
        }

        internal static void Read(ZPackage packet, NativeSteeringPlan plan, MonsterAI ai, Humanoid actor,
            Character destinationTarget, float sourceTime)
        {
            FieldInfo[] fields = Fields(plan);
            short count = packet.ReadShort();
            if (count != fields.Length) throw new InvalidOperationException("Bridge state schema count changed.");
            foreach (FieldInfo field in fields)
            {
                object target = Owner(field, ai, actor);
                if (field.Name == "m_targetCreature")
                {
                    ZDOID ignored = packet.ReadZDOID();
                    if (!ignored.IsNone()) throw new InvalidOperationException("Bridge target header mismatch.");
                    field.SetValue(target, destinationTarget);
                }
                else if (field.Name == "m_targetStatic")
                {
                    if (packet.ReadBool()) throw new NotSupportedException("Static-target bridge state is unsupported.");
                    field.SetValue(target, null);
                }
                else if (field.Name == "m_nearFireArea")
                {
                    bool present = packet.ReadBool();
                    field.SetValue(target, present
                        ? EffectArea.IsPointInsideArea(actor.transform.position, EffectArea.Type.Fire, 3f)
                        : null);
                }
                else field.SetValue(target, ReadField(packet, field, sourceTime));
            }
            MobBridgeWeaponState.Read(packet, actor, Time.time);
        }

        private static void WriteField(ZPackage packet, FieldInfo field, object value, float sourceTime)
        {
            if (!AbsoluteClock(field.Name)) { WriteValue(packet, value, field.FieldType); return; }
            float clock = (float)value;
            bool initialized = clock > 0f && Finite(clock);
            packet.Write(initialized);
            packet.Write(initialized ? sourceTime - clock : clock);
        }

        private static object ReadField(ZPackage packet, FieldInfo field, float sourceTime)
        {
            if (!AbsoluteClock(field.Name)) return ReadValue(packet, field.FieldType);
            bool relative = packet.ReadBool();
            float value = packet.ReadSingle();
            if (!Finite(value)) throw new InvalidOperationException("Invalid bridge clock state.");
            return relative ? Time.time - value : value;
        }

        private static bool AbsoluteClock(string name)
        {
            return name == "m_lastDespawnInDayCheck" || name == "m_lastEventCreatureCheck" ||
                name == "m_lastMoveToWaterUpdate" || name == "m_patrolPointUpdateTime" ||
                name == "m_fleeTargetUpdateTime" || name == "m_nearFireTime" ||
                name == "aroundPointUpdateTime" || name == "m_lastFindPathTime" ||
                name == "m_lastFlee";
        }

        private static bool Finite(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value);
        }

        private static object Owner(FieldInfo field, MonsterAI ai, Humanoid actor)
        {
            return field.DeclaringType == typeof(MonsterAI) || field.DeclaringType == typeof(BaseAI)
                ? (object)ai : actor;
        }

        private static bool Supported(Type type)
        {
            return type == typeof(bool) || type == typeof(float) || type == typeof(Vector3) ||
                type == typeof(Quaternion) || type == typeof(string) || type == typeof(List<Vector3>) ||
                type == typeof(Character) || type == typeof(StaticTarget) || type == typeof(EffectArea);
        }

        private static void WriteValue(ZPackage packet, object value, Type type)
        {
            if (type == typeof(bool)) { packet.Write((bool)value); return; }
            if (type == typeof(float)) { packet.Write((float)value); return; }
            if (type == typeof(Vector3)) { packet.Write((Vector3)value); return; }
            if (type == typeof(Quaternion))
            {
                Quaternion value4 = (Quaternion)value;
                packet.Write(value4.x); packet.Write(value4.y); packet.Write(value4.z); packet.Write(value4.w); return;
            }
            if (type == typeof(string)) { packet.Write((string)value ?? ""); return; }
            if (type == typeof(List<Vector3>))
            {
                List<Vector3> path = (List<Vector3>)value ?? new List<Vector3>();
                if (path.Count > 256) throw new NotSupportedException("Bridge path exceeds 256 points.");
                packet.Write((short)path.Count);
                foreach (Vector3 point in path) packet.Write(point);
                return;
            }
            throw new NotSupportedException("Unsupported bridge value: " + type.FullName);
        }

        private static object ReadValue(ZPackage packet, Type type)
        {
            if (type == typeof(bool)) return packet.ReadBool();
            if (type == typeof(float)) return packet.ReadSingle();
            if (type == typeof(Vector3)) return packet.ReadVector3();
            if (type == typeof(Quaternion)) return new Quaternion(packet.ReadSingle(), packet.ReadSingle(), packet.ReadSingle(), packet.ReadSingle());
            if (type == typeof(string)) return packet.ReadString();
            if (type == typeof(List<Vector3>))
            {
                short count = packet.ReadShort();
                if (count < 0 || count > 256) throw new InvalidOperationException("Invalid bridge path length.");
                List<Vector3> path = new List<Vector3>(count);
                for (int i = 0; i < count; ++i) path.Add(packet.ReadVector3());
                return path;
            }
            throw new NotSupportedException("Unsupported bridge value: " + type.FullName);
        }

        private static void Mix(ref uint hash, string value)
        {
            if (value == null) return;
            for (int i = 0; i < value.Length; ++i) { hash ^= value[i]; hash *= 16777619u; }
        }
    }
}
#endif
