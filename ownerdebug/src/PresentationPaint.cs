using System;
using System.Collections.Generic;
using UnityEngine;

namespace BotanicOwnerDebug
{
    // Use the very same native skin draw during a transient bridge pose.
    // No second geometry, renderer matrix, scale convention or skinning pass.
    internal sealed class PresentationPaint : IDisposable
    {
        private readonly Renderer renderer;
        private readonly List<Material> current = new List<Material>();
        private Material[] original, painted;
        private bool applied;
        internal PresentationPaint(Renderer renderer) { this.renderer = renderer; }
        internal void Begin()
        {
            Restore();
            current.Clear(); renderer.GetSharedMaterials(current);
            if (original == null || original.Length != current.Count)
            { original = new Material[current.Count]; painted = new Material[current.Count]; }
            current.CopyTo(original); current.CopyTo(painted);
        }
        internal void Slot(int index, Material colour)
        { if (index >= 0 && index < painted.Length) painted[index] = colour; }
        internal void Apply() { applied = true; renderer.sharedMaterials = painted; }
        internal void Restore()
        {
            if (!applied) return;
            applied = false;
            if (!renderer) return;
            current.Clear(); renderer.GetSharedMaterials(current);
            bool changed = false;
            // Restore only slots still containing our paint. A foreign update
            // during the camera draw must survive cleanup, including new slots.
            for (int i = 0; i < current.Count && i < painted.Length; i++)
                if (!ReferenceEquals(original[i], painted[i]) && ReferenceEquals(current[i], painted[i]))
                { current[i] = original[i]; changed = true; }
            if (changed)
            {
                if (current.Count == original.Length) { current.CopyTo(original); renderer.sharedMaterials = original; }
                else renderer.sharedMaterials = current.ToArray();
            }
            current.Clear(); Array.Clear(original, 0, original.Length); Array.Clear(painted, 0, painted.Length);
        }
        public void Dispose() { Restore(); current.Clear(); original = painted = null; }
    }
}
