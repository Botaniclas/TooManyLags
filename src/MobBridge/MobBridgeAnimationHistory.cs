#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags
{
    // Cosmetic bone weighting only. Root motion and gameplay clocks are not
    // affected. Native animation remains live at both recording boundaries.
    internal static class MobBridgeAnimationBlend
    {
        internal static float NativeWeight(float elapsed, float sample, float horizon, float takeoverWeight, float attackWeight)
        {
            if (horizon <= 0f) return 1f;
            // Each fade spans one quarter of the normal 2L presentation.
            // Entry uses wall time; exit uses the consumed recording horizon.
            float entry = Smooth(elapsed / (horizon * .5f));
            float remaining = Smooth((horizon - sample) / (horizon * .25f));
            // Ordinary locomotion keeps the current native pose throughout.
            // Only a captured attack needs private skeletal playback.
            float fake = entry * remaining * Smooth(attackWeight);
            return 1f - fake * (1f - Math.Max(0f, Math.Min(1f, takeoverWeight)));
        }
        private static float Smooth(float value)
        { float t = Math.Max(0f, Math.Min(1f, value)); return t * t * (3f - 2f * t); }
    }

    // Pose data only. Sampling cannot run animation events or change a recording.
    internal sealed class MobBridgeAnimationHistory
    {
        private sealed class Frame
        { internal float Time; internal Vector3[] Positions, Scales; internal Quaternion[] Rotations; internal bool Attack;
          internal Vector3 RootPosition; internal Quaternion RootRotation; }
        private readonly List<Frame> frames = new List<Frame>();
        private Vector3[] lastScales;
        private readonly int count;
        internal bool Closed { get; private set; }
        internal float End { get; private set; }
        internal int Count { get { return frames.Count; } }
        internal float TimeAt(int index) { return frames[index].Time; }
        internal MobBridgeAnimationHistory(int bones) { if (bones < 1) throw new ArgumentException("Empty animation rig."); count = bones; }
        internal void Add(float time, Vector3[] positions, Quaternion[] rotations, Vector3[] scales, bool attack = false)
        {
            if (Closed || !MobBridgeTimeline.Finite(time) || time < 0f || frames.Count > 0 && time <= End ||
                positions.Length != count || rotations.Length != count || scales.Length != count)
                throw new InvalidOperationException("Invalid or sealed animation recording.");
            // Frames and exported poses are immutable. Unchanged scales can
            // share the previous owned snapshot; input scratch arrays never do.
            bool same = lastScales != null;
            for (int i = 0; same && i < count; ++i) same = scales[i].Equals(lastScales[i]);
            if (!same) lastScales = (Vector3[])scales.Clone();
            frames.Add(new Frame { Time = time, Positions = (Vector3[])positions.Clone(),
                Rotations = (Quaternion[])rotations.Clone(), Scales = lastScales, Attack = attack,
                RootPosition = positions[0], RootRotation = rotations[0] }); End = time;
        }
        internal void AddRoot(float time, Vector3 position, Quaternion rotation)
        {
            if (Closed || frames.Count == 0 || time <= End || !MobBridgeTimeline.Finite(time))
                throw new InvalidOperationException("Invalid received model pose.");
            frames.Add(new Frame { Time = time, RootPosition = position, RootRotation = rotation }); End = time;
        }
        internal MobBridgeObserverPacket.Sample Export(int index)
        {
            Frame frame = frames[index];
            var sample = new MobBridgeObserverPacket.Sample { Time = frame.Time, ModelPosition = frame.RootPosition,
                ModelRotation = frame.RootRotation, Attack = frame.Attack };
            // Ordinary locomotion is native-bone passthrough on source and
            // recipient. Only predicted attacks need a skeletal payload.
            if (index < 2 || frame.Attack || index > 0 && frames[index - 1].Attack ||
                index + 1 < frames.Count && frames[index + 1].Attack)
            {
                sample.Positions = frame.Positions;
                sample.Rotations = frame.Rotations;
                sample.Scales = frame.Scales;
            }
            return sample;
        }
        internal void Close() { Closed = true; }
        internal void SampleRoot(float time, out Vector3 position, out Quaternion rotation)
        {
            if (frames.Count == 0) throw new InvalidOperationException("Empty model recording.");
            int lo, hi; float t;
            Interval(time, out lo, out hi, out t);
            position = frames[lo].RootPosition * (1f - t) + frames[hi].RootPosition * t;
            rotation = Quaternion.Slerp(frames[lo].RootRotation, frames[hi].RootRotation, t);
        }
        internal float AttackWeight(float time)
        {
            if (frames.Count == 0) return 0f;
            int lo, hi; float t;
            Interval(time, out lo, out hi, out t);
            return (frames[lo].Attack ? 1f : 0f) * (1f - t) + (frames[hi].Attack ? 1f : 0f) * t;
        }
        private void Interval(float time, out int lo, out int hi, out float t)
        {
            lo = 0; hi = frames.Count - 1;
            if (time <= frames[0].Time) hi = 0;
            else while (hi - lo > 1)
            { int middle = (lo + hi) / 2; if (frames[middle].Time < time) lo = middle; else hi = middle; }
            Frame a = frames[lo], b = frames[hi];
            t = b.Time <= a.Time ? 0f : Math.Max(0f, Math.Min(1f, (time - a.Time) / (b.Time - a.Time)));
        }
        internal void Sample(float time, Vector3[] positions, Quaternion[] rotations, Vector3[] scales)
        {
            if (frames.Count == 0 || positions.Length != count || rotations.Length != count || scales.Length != count)
                throw new InvalidOperationException("Invalid animation output.");
            int lo, hi; float t;
            Interval(time, out lo, out hi, out t);
            Frame a = frames[lo], b = frames[hi];
            Frame ap = a.Positions == null ? frames[0] : a, bp = b.Positions == null ? frames[0] : b;
            for (int i = 0; i < count; ++i)
            {
                positions[i] = ap.Positions[i] * (1f - t) + bp.Positions[i] * t;
                rotations[i] = Quaternion.Slerp(ap.Rotations[i], bp.Rotations[i], t);
                scales[i] = ap.Scales[i] * (1f - t) + bp.Scales[i] * t;
            }
            positions[0] = a.RootPosition * (1f - t) + b.RootPosition * t;
            rotations[0] = Quaternion.Slerp(a.RootRotation, b.RootRotation, t);
        }
    }
}
#endif
