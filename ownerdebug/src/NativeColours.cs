using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BotanicOwnerDebug
{
    // A few shared, owned flat materials. Never assigned to a game renderer.
    internal sealed class NativeColours
    {
        private struct Key : IEquatable<Key>
        {
            internal OwnerKey Owner; internal int Cull;
            public bool Equals(Key other) { return Owner.Equals(other.Owner) && Cull == other.Cull; }
            public override bool Equals(object other) { return other is Key && Equals((Key)other); }
            public override int GetHashCode() { return Owner.GetHashCode() ^ Cull * 397; }
        }
        private sealed class Paint { internal Material Material; internal int Frame; }
        private readonly Dictionary<Key, Paint> cache = new Dictionary<Key, Paint>();
        private readonly List<Key> retired = new List<Key>();
        private Shader shader; private int seed, frame; private float strength; private bool configured;
        private static readonly int colour = Shader.PropertyToID("_Color");
        internal void Configure(int nextSeed, float nextStrength)
        {
            frame++; nextStrength = Mathf.Clamp01(nextStrength);
            if (configured && seed == nextSeed && strength == nextStrength) return;
            seed = nextSeed; strength = nextStrength; configured = true;
            foreach (var pair in cache) SetColour(pair.Key.Owner, pair.Value.Material);
        }
        private void SetColour(OwnerKey owner, Material material)
        {
            Rgb rgb = OwnerColour.Get(owner, seed);
            material.SetColor(colour, new Color(rgb.R, rgb.G, rgb.B, strength));
        }
        internal Material Get(OwnerKey owner, int cull)
        {
            Key key = new Key { Owner = owner, Cull = cull }; Paint paint;
            if (!cache.TryGetValue(key, out paint))
            {
                if (!shader) shader = Shader.Find("Hidden/Internal-Colored");
                if (!shader || !shader.isSupported) throw new NotSupportedException("PUP-style flat shader unavailable.");
                Material material = new Material(shader) { name = "BotanicOwnerDebugPaint", hideFlags = HideFlags.HideAndDontSave, enableInstancing = false };
                paint = new Paint { Material = material }; cache.Add(key, paint);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0); material.SetInt("_ZTest", (int)CompareFunction.Always); // Ownership overlay remains visible through other objects.
                material.SetInt("_Cull", cull); SetColour(owner, material);
            }
            paint.Frame = frame; return paint.Material;
        }
        internal void Sweep()
        {
            retired.Clear(); foreach (var pair in cache) if (pair.Value.Frame < frame - 2) retired.Add(pair.Key);
            foreach (Key key in retired) { UnityEngine.Object.Destroy(cache[key].Material); cache.Remove(key); } retired.Clear();
        }
        internal void Clear()
        {
            foreach (Paint paint in cache.Values) if (paint.Material) UnityEngine.Object.Destroy(paint.Material);
            cache.Clear(); retired.Clear(); shader = null;
        }
    }
}
