#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using UnityEngine;

namespace TooManyLags
{
    // Fixed-step poses with render-time interpolation. This keeps visible motion
    // continuous even when simulation and render cadences differ.
    internal sealed class MobBridgePoseHistory
    {
        private const int Capacity = 64;
        private readonly float[] times = new float[Capacity];
        private readonly Vector3[] positions = new Vector3[Capacity];
        private readonly Quaternion[] rotations = new Quaternion[Capacity];
        private int count, start;

        internal int Count { get { return count; } }

        internal void Add(float time, Vector3 position, Quaternion rotation)
        {
            if (!MobBridgeTimeline.Finite(time) || time < 0f) return;
            if (count != 0 && time < times[Index(count - 1)]) return;
            int write;
            if (count < Capacity)
            {
                write = Index(count);
                count++;
            }
            else
            {
                write = start;
                start = (start + 1) % Capacity;
            }
            times[write] = time; positions[write] = position; rotations[write] = rotation;
        }

        internal void Sample(float time, out Vector3 position, out Quaternion rotation)
        {
            if (count == 0)
            {
                position = new Vector3(); rotation = Quaternion.identity; return;
            }
            if (count == 1 || time <= times[Index(0)])
            {
                position = positions[Index(0)]; rotation = rotations[Index(0)]; return;
            }
            // First sample at or after time. Preserve the earliest matching
            // duplicate timestamp and the existing tiny-span blend rule.
            int lower = 1, upper = count;
            while (lower < upper)
            {
                int middle = (lower + upper) / 2;
                if (times[Index(middle)] < time) lower = middle + 1;
                else upper = middle;
            }
            if (lower < count)
            {
                int current = Index(lower), previous = Index(lower - 1);
                float span = times[current] - times[previous];
                float blend = span <= .00001f ? 1f : Math.Max(0f, Math.Min(1f, (time - times[previous]) / span));
                position = positions[previous] * (1f - blend) + positions[current] * blend;
                rotation = Quaternion.Slerp(rotations[previous], rotations[current], blend);
                return;
            }
            position = positions[Index(count - 1)]; rotation = rotations[Index(count - 1)];
        }

        private int Index(int offset) { return (start + offset) % Capacity; }
    }
}
#endif
