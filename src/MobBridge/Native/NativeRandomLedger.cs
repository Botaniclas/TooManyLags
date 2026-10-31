using System;
using UnityEngine;

namespace TooManyLags
{
    // Bounded values sampled from the source's next RNG stream. Sampling saves
    // and restores the global state, so the old owner consumes nothing early.
    internal sealed class NativeRandomLedger
    {
        internal const int Capacity = 16;
        private readonly float[] values;
        private int index;

        private NativeRandomLedger(float[] samples)
        {
            values = new float[Capacity];
            int count = Math.Min(Capacity, samples == null ? 0 : samples.Length);
            for (int i = 0; i < count; ++i)
            {
                float value = samples[i];
                values[i] = !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value < 1f ? value : .5f;
            }
        }

        internal static NativeRandomLedger Capture()
        {
            float[] samples = new float[Capacity];
            UnityEngine.Random.State state = UnityEngine.Random.state;
            try
            {
                for (int i = 0; i < Capacity; ++i) samples[i] = UnityEngine.Random.value;
            }
            finally { UnityEngine.Random.state = state; }
            return new NativeRandomLedger(samples);
        }

        internal static NativeRandomLedger Decode(float[] samples)
        {
            return samples != null && samples.Length == Capacity ? new NativeRandomLedger(samples) : null;
        }

        internal float[] Encode()
        {
            float[] result = new float[Capacity];
            Array.Copy(values, result, Capacity);
            return result;
        }

        internal bool TryTake(out float value)
        {
            if (index >= Capacity) { value = 0f; return false; }
            value = values[index++];
            return true;
        }
    }
}
