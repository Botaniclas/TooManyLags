#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags
{
    // Append-only recording. Playback never writes or reruns past states. A
    // sealed recording accepts no later target input and never loses its start.
    internal sealed class MobBridgeRecording
    {
        private struct Pose { internal float Time; internal Vector3 Position; internal Quaternion Rotation; }
        private readonly List<Pose> poses = new List<Pose>();
        private readonly double started;
        private readonly float window, rate;
        internal bool Closed { get; private set; }
        internal float End { get; private set; }
        internal double Deadline { get { return started + window; } }
        internal bool AcceptsInput(double now) { return !Closed && now <= Deadline; }
        internal float Horizon(double now) { return rate * (float)Math.Max(0d, Math.Min(window, now - started)); }
        internal MobBridgeRecording(double now, float seconds, float simulationRate)
        {
            if (!MobBridgeTimeline.Finite(now) || !MobBridgeTimeline.Finite(seconds) || seconds <= 0f ||
                !MobBridgeTimeline.Finite(simulationRate) || simulationRate <= 0f) throw new ArgumentException("Invalid native recording window.");
            started = now; window = seconds; rate = simulationRate;
        }
        internal void Add(float time, Vector3 position, Quaternion rotation)
        {
            if (Closed) throw new InvalidOperationException("Native recording is read-only.");
            if (time < 0f || !MobBridgeTimeline.Finite(time) || poses.Count != 0 && time <= End)
                throw new InvalidOperationException("Native recording time must increase.");
            poses.Add(new Pose { Time = time, Position = position, Rotation = rotation }); End = time;
        }
        internal void Close() { Closed = true; }
        internal Vector3 Velocity(float time)
        {
            if (poses.Count < 2 || time < 0f || time > End) return new Vector3();
            int lower = 0, upper = poses.Count - 1;
            while (upper - lower > 1)
            { int middle = (lower + upper) / 2; if (poses[middle].Time < time) lower = middle; else upper = middle; }
            Pose a = poses[lower], b = poses[upper];
            return (b.Position - a.Position) / (b.Time - a.Time);
        }
        internal void Sample(float time, out Vector3 position, out Quaternion rotation)
        {
            if (poses.Count == 0) { position = new Vector3(); rotation = Quaternion.identity; return; }
            int lower = 0, upper = poses.Count - 1;
            if (time <= poses[0].Time) upper = 0;
            else
                while (upper - lower > 1)
                { int middle = (lower + upper) / 2; if (poses[middle].Time < time) lower = middle; else upper = middle; }
            Pose a = poses[lower], b = poses[upper];
            float blend = b.Time <= a.Time ? 0f : Math.Max(0f, Math.Min(1f, (time - a.Time) / (b.Time - a.Time)));
            position = a.Position * (1f - blend) + b.Position * blend;
            rotation = Quaternion.Slerp(a.Rotation, b.Rotation, blend);
        }
    }
}
#endif
