#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags
{
    // Presentation data only. Never deserialize these samples into a ZDO.
    internal sealed class MobBridgeObserverPacket
    {
        internal const int MaxBones = 1024, MaxFrames = 128, MaxBytes = 65536;
        internal byte Kind;
        internal long Source, Destination;
        internal uint Revision, Token, Sequence;
        internal int Prefab, Bones;
        internal string[] Paths;
        internal float Window, Rate, AiRate, End;
        internal float RecordingWindow, FirstArrival, PhysicsStep, ExpectedMatch, Startup;
        internal int PhysicsLayer = -1;
        internal float RecordingSeconds { get { return RecordingWindow > 0f ? RecordingWindow : Window; } }
        internal float ExpectedArrival { get { return FirstArrival > 0f ? FirstArrival : Window; } }
        internal float NativeStep { get { return PhysicsStep > 0f ? PhysicsStep : .02f; } }
        internal float MatchArrival { get { return ExpectedMatch > 0f ? ExpectedMatch : RecordingSeconds * 2f; } }
        internal Vector3 Origin, ModelPosition;
        internal Quaternion Rotation, ModelRotation;
        internal ZDOID[] Effects = new ZDOID[0];
        internal readonly List<Sample> Samples = new List<Sample>();
        internal sealed class Sample
        {
            internal float Time;
            internal Vector3 Position, ModelPosition;
            internal Quaternion Rotation, ModelRotation;
            internal bool Attack;
            internal Vector3[] Positions, Scales;
            internal Quaternion[] Rotations;
        }
        internal bool Same(long source, long destination, uint revision, uint token)
        { return Source == source && Destination == destination && Revision == revision && Token == token; }
        internal ZPackage Encode()
        {
            ZPackage p = new ZPackage();
            p.Write(Kind); p.Write(Source); p.Write(Destination); p.Write(Revision); p.Write(Token); p.Write(Sequence);
            if (Kind == 0)
            {
                p.Write(Prefab); p.Write(Window); p.Write(RecordingSeconds); p.Write(ExpectedArrival); p.Write(NativeStep);
                p.Write(MatchArrival); p.Write(Startup); p.Write(PhysicsLayer);
                p.Write(Rate); p.Write(AiRate); p.Write(Origin); p.Write(Rotation);
                p.Write(ModelPosition); p.Write(ModelRotation);
                p.Write(Bones);
                foreach (string path in Paths) p.Write(path);
                p.Write(Effects.Length);
                foreach (ZDOID effect in Effects) p.Write(effect);
            }
            else if (Kind == 1)
            {
                p.Write(Samples.Count);
                Sample baseline = null;
                foreach (Sample frame in Samples)
                {
                    p.Write(frame.Time); p.Write(frame.Position); p.Write(frame.Rotation);
                    p.Write(frame.ModelPosition); p.Write(frame.ModelRotation); p.Write(frame.Attack);
                    int count = frame.Positions == null ? 0 : frame.Positions.Length;
                    p.Write(count);
                    for (int i = 0; i < count; ++i)
                    {
                        byte mask = 0;
                        if (baseline == null || !frame.Positions[i].Equals(baseline.Positions[i])) mask |= 1;
                        if (baseline == null || !frame.Rotations[i].Equals(baseline.Rotations[i])) mask |= 2;
                        if (baseline == null || !frame.Scales[i].Equals(baseline.Scales[i])) mask |= 4;
                        p.Write(mask);
                        if ((mask & 1) != 0) p.Write(frame.Positions[i]);
                        if ((mask & 2) != 0) p.Write(frame.Rotations[i]);
                        if ((mask & 4) != 0) p.Write(frame.Scales[i]);
                    }
                    if (count != 0) baseline = frame;
                }
            }
            else if (Kind == 2 || Kind == 3) p.Write(End);
            if (p.Size() > MaxBytes) throw new InvalidOperationException("Observer sample batch exceeds transport budget.");
            return p;
        }
        internal static MobBridgeObserverPacket Decode(ZPackage p)
        { return DecodeCore(p, true); }
        internal static MobBridgeObserverPacket DecodeForRelay(ZPackage p)
        { return DecodeCore(p, false); }
        private static MobBridgeObserverPacket DecodeCore(ZPackage p, bool materialize)
        {
            if (p == null || p.Size() > MaxBytes) throw new ArgumentException("Invalid observer packet size.");
            var value = new MobBridgeObserverPacket { Kind = p.ReadByte(), Source = p.ReadLong(), Destination = p.ReadLong(),
                Revision = p.ReadUInt(), Token = p.ReadUInt(), Sequence = p.ReadUInt() };
            if (value.Kind > 5 || value.Source == 0 || value.Destination == 0 || value.Source == value.Destination ||
                value.Revision >= UInt16.MaxValue || value.Token == 0) throw new ArgumentException("Invalid observer epoch.");
            if (value.Kind == 0)
            {
                value.Prefab = p.ReadInt(); value.Window = p.ReadSingle();
                value.RecordingWindow = p.ReadSingle(); value.FirstArrival = p.ReadSingle(); value.PhysicsStep = p.ReadSingle();
                value.ExpectedMatch = p.ReadSingle(); value.Startup = p.ReadSingle(); value.PhysicsLayer = p.ReadInt();
                value.Rate = p.ReadSingle(); value.AiRate = p.ReadSingle();
                value.Origin = p.ReadVector3(); value.Rotation = p.ReadQuaternion();
                value.ModelPosition = p.ReadVector3(); value.ModelRotation = p.ReadQuaternion();
                value.Bones = p.ReadInt();
                if (value.Bones < 1 || value.Bones > MaxBones) throw new ArgumentException("Invalid observer rig size.");
                value.Paths = new string[value.Bones];
                var paths = new HashSet<string>();
                for (int i = 0; i < value.Bones; ++i)
                {
                    string path = p.ReadString();
                    if (path.Length > 4096 || !paths.Add(path) || i == 0 && path.Length != 0 || i > 0 && path.Length == 0)
                        throw new ArgumentException("Invalid observer rig path.");
                    value.Paths[i] = path;
                }
                int effects = p.ReadInt();
                if (!Positive(value.Window) || !Positive(value.RecordingWindow) || !Positive(value.FirstArrival) || value.FirstArrival < value.Window ||
                    !Positive(value.PhysicsStep) || !Positive(value.ExpectedMatch) || value.ExpectedMatch < value.FirstArrival ||
                    !MobBridgeTimeline.Finite(value.Startup) || value.Startup < 0f || value.Startup > 5f ||
                    value.PhysicsLayer < -1 || value.PhysicsLayer > 31 ||
                    !Positive(value.Rate) || !Positive(value.RecordingWindow * value.Rate) || !Positive(value.AiRate) || !Finite(value.Origin) ||
                    !Finite(value.Rotation) || !Finite(value.ModelPosition) || !Finite(value.ModelRotation) ||
                    value.Bones < 1 || value.Bones > MaxBones || effects < 0 || effects > 256)
                    throw new ArgumentException("Invalid observer descriptor.");
                value.Effects = new ZDOID[effects];
                for (int i = 0; i < effects; ++i) value.Effects[i] = p.ReadZDOID();
            }
            else if (value.Kind == 1)
            {
                int count = p.ReadInt();
                if (count < 1 || count > MaxFrames) throw new ArgumentException("Invalid observer sample count.");
                Sample baseline = null; int baselineBones = 0; float previous = -1f;
                for (int f = 0; f < count; ++f)
                {
                    Sample frame = new Sample { Time = p.ReadSingle(), Position = p.ReadVector3(), Rotation = p.ReadQuaternion(),
                        ModelPosition = p.ReadVector3(), ModelRotation = p.ReadQuaternion(), Attack = p.ReadBool() };
                    if (!MobBridgeTimeline.Finite(frame.Time) || frame.Time < 0f || frame.Time <= previous || !Finite(frame.Position) ||
                        !Finite(frame.Rotation) || !Finite(frame.ModelPosition) || !Finite(frame.ModelRotation))
                        throw new ArgumentException("Invalid observer sample.");
                    previous = frame.Time;
                    int bones = p.ReadInt();
                    if (bones < 0 || bones > MaxBones || baseline != null && bones != 0 && baselineBones != bones)
                        throw new ArgumentException("Invalid observer pose size.");
                    if (bones > 0)
                    {
                        if (materialize)
                        { frame.Positions = new Vector3[bones]; frame.Rotations = new Quaternion[bones]; frame.Scales = new Vector3[bones]; }
                        for (int i = 0; i < bones; ++i)
                        {
                            byte mask = p.ReadByte();
                            if (mask > 7 || baseline == null && mask != 7) throw new ArgumentException("Invalid observer pose delta.");
                            if (materialize)
                            {
                                frame.Positions[i] = (mask & 1) != 0 ? p.ReadVector3() : baseline.Positions[i];
                                frame.Rotations[i] = (mask & 2) != 0 ? p.ReadQuaternion() : baseline.Rotations[i];
                                frame.Scales[i] = (mask & 4) != 0 ? p.ReadVector3() : baseline.Scales[i];
                                if (!Finite(frame.Positions[i]) || !Finite(frame.Rotations[i]) || !Finite(frame.Scales[i]))
                                    throw new ArgumentException("Invalid observer bone pose.");
                            }
                            else
                            {
                                // Inherited values were validated when their
                                // baseline was read. Validate every present
                                // value without allocating unused dense rigs.
                                if ((mask & 1) != 0 && !Finite(p.ReadVector3()) ||
                                    (mask & 2) != 0 && !Finite(p.ReadQuaternion()) ||
                                    (mask & 4) != 0 && !Finite(p.ReadVector3()))
                                    throw new ArgumentException("Invalid observer bone pose.");
                            }
                        }
                        baseline = frame; baselineBones = bones;
                    }
                    value.Samples.Add(frame);
                }
            }
            else if (value.Kind == 2 || value.Kind == 3)
            { value.End = p.ReadSingle(); if (!MobBridgeTimeline.Finite(value.End) || value.End < 0f) throw new ArgumentException("Invalid observer endpoint."); }
            if (p.GetPos() != p.Size()) throw new ArgumentException("Trailing observer packet data.");
            return value;
        }
        private static bool Positive(float value) { return MobBridgeTimeline.Finite(value) && value > 0f; }
        private static bool Finite(Vector3 value)
        { return MobBridgeTimeline.Finite(value.x) && MobBridgeTimeline.Finite(value.y) && MobBridgeTimeline.Finite(value.z); }
        private static bool Finite(Quaternion value)
        { float norm = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
          return MobBridgeTimeline.Finite(norm) && norm > .000001f; }
    }
}
#endif
