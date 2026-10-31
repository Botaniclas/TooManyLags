#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using NativeMobPreviewAudit;
using UnityEngine;

namespace TooManyLags
{
    // Bounded, versioned state sent before ownership changes. Combat/effects
    // remain in their existing .86 checkpoints; this packet covers AI decisions,
    // movement/look controls and the isolated random epoch.
    internal sealed class MobBridgeCheckpoint
    {
        private const byte Version = 5;
        private const int MaxBytes = 16384;
        internal readonly ZDOID Target;
        internal readonly uint Seed;
        internal readonly uint Schema;
        internal readonly float Latency;
        internal readonly float FirstArrival, RecordingWindow, Horizon, PhysicsStep;
        internal readonly float SourceTime;
        internal readonly Vector3 Origin;
        internal readonly Quaternion Rotation;
        internal readonly Vector3 Velocity;
        internal readonly byte[] Payload;

        private MobBridgeCheckpoint(ZDOID target, uint seed, uint schema, float latency, float sourceTime,
            Vector3 origin, Quaternion rotation, Vector3 velocity, byte[] payload,
            float first, float recording, float horizon, float physicsStep)
        {
            Target = target; Seed = seed; Schema = schema; Latency = latency; SourceTime = sourceTime;
            Origin = origin; Rotation = rotation; Velocity = velocity;
            Payload = payload;
            FirstArrival = first; RecordingWindow = recording; Horizon = horizon; PhysicsStep = physicsStep;
        }

        internal static MobBridgeCheckpoint Capture(NativeSteeringPlan plan, MonsterAI ai,
            Humanoid actor, Character target, uint seed, float latency, MobBridgeTiming timing = null)
        {
            ZNetView view = actor.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !target || target.IsDead())
                throw new NotSupportedException("Bridge checkpoint target/view unavailable.");
            ZPackage packet = new ZPackage();
            packet.Write(Version);
            packet.Write(target.GetZDOID());
            packet.Write(seed);
            packet.Write(MobBridgeFieldCodec.Hash(plan));
            packet.Write(Mathf.Max(.001f, latency));
            float recording = timing == null ? latency : timing.RecordingWindow;
            float first = timing == null ? latency : timing.FirstArrival;
            float horizon = timing == null ? latency : timing.Horizon;
            float step = timing == null ? Time.fixedDeltaTime : timing.PhysicsStep;
            packet.Write(first); packet.Write(recording); packet.Write(horizon); packet.Write(step);
            float sourceTime = Time.time;
            Vector3 origin = actor.transform.position;
            Quaternion rotation = actor.transform.rotation;
            Vector3 velocity = actor.GetVelocity();
            packet.Write(sourceTime);
            packet.Write(origin); packet.Write(rotation); packet.Write(velocity);
            MobBridgeFieldCodec.Write(packet, plan, ai, actor, sourceTime);
            byte[] payload = packet.GetArray();
            if (payload.Length > MaxBytes) throw new NotSupportedException("Bridge checkpoint exceeds size limit.");
            return new MobBridgeCheckpoint(target.GetZDOID(), seed, MobBridgeFieldCodec.Hash(plan),
                Mathf.Max(.001f, latency), sourceTime, origin, rotation, velocity, payload,
                first, recording, horizon, step);
        }

        internal static MobBridgeCheckpoint Decode(ZPackage packet)
        {
            if (packet == null || packet.Size() > MaxBytes || packet.Size() < 12) throw new InvalidOperationException("Invalid bridge checkpoint.");
            if (packet.ReadByte() != Version) throw new InvalidOperationException("Unsupported bridge checkpoint version.");
            ZDOID target = packet.ReadZDOID();
            uint seed = packet.ReadUInt();
            uint schema = packet.ReadUInt();
            float latency = packet.ReadSingle();
            float first = packet.ReadSingle(), recording = packet.ReadSingle(), horizon = packet.ReadSingle(), step = packet.ReadSingle();
            float sourceTime = packet.ReadSingle();
            Vector3 origin = packet.ReadVector3();
            Quaternion rotation = packet.ReadQuaternion();
            Vector3 velocity = packet.ReadVector3();
            if (!MobBridgeTimeline.Finite(latency) || latency <= 0f)
                throw new InvalidOperationException("Invalid bridge latency.");
            if (!MobBridgeTiming.Positive(first) || first < latency || !MobBridgeTiming.Positive(recording) ||
                !MobBridgeTiming.Positive(horizon) || !MobBridgeTiming.Positive(step) ||
                horizon < recording * .099999f || horizon > recording * 1.500001f)
                throw new InvalidOperationException("Invalid bridge recording timing.");
            if (!MobBridgeTimeline.Finite(sourceTime) || sourceTime < 0f)
                throw new InvalidOperationException("Invalid bridge game time.");
            if (!Finite(origin) || !Finite(rotation) || !Finite(velocity))
                throw new InvalidOperationException("Invalid bridge source pose.");
            if (packet.GetPos() >= packet.Size()) throw new InvalidOperationException("Truncated bridge checkpoint.");
            return new MobBridgeCheckpoint(target, seed, schema, latency, sourceTime, origin, rotation, velocity,
                packet.GetArray(), first, recording, horizon, step);
        }

        internal void Apply(NativeSteeringPlan plan, MonsterAI ai, Humanoid actor, Character target)
        {
            if (MobBridgeFieldCodec.Hash(plan) != Schema) throw new InvalidOperationException("Bridge state schema mismatch.");
            if (!target || target.GetZDOID() != Target) throw new InvalidOperationException("Bridge target changed.");
            ZPackage packet = new ZPackage(Payload);
            packet.ReadByte(); packet.ReadZDOID(); packet.ReadUInt(); packet.ReadUInt(); packet.ReadSingle();
            packet.ReadSingle(); packet.ReadSingle(); packet.ReadSingle(); packet.ReadSingle();
            packet.ReadSingle();
            packet.ReadVector3(); packet.ReadQuaternion(); packet.ReadVector3();
            MobBridgeFieldCodec.Read(packet, plan, ai, actor, target, SourceTime);
            if (packet.GetPos() != packet.Size()) throw new InvalidOperationException("Trailing bridge state.");
        }
        private static bool Finite(Quaternion value)
        {
            float norm = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return MobBridgeTimeline.Finite(norm) && norm > .0001f;
        }
        private static bool Finite(Vector3 value)
        {
            return MobBridgeTimeline.Finite(value.x) && MobBridgeTimeline.Finite(value.y) &&
                MobBridgeTimeline.Finite(value.z);
        }
    }
}
#endif
