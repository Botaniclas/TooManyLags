#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags
{
    // Render callbacks temporarily override bone transforms more than once per
    // frame. Unity must not reuse matrices calculated for the restored live rig.
    internal sealed class MobBridgeSkin : IDisposable
    {
        private sealed class Entry { internal SkinnedMeshRenderer Renderer; internal bool Original; }
        private readonly List<Entry> entries = new List<Entry>();
        private bool disposed;
        internal int Count { get { return entries.Count; } }
        internal int Changed { get; private set; }
        internal MobBridgeSkin(GameObject visual)
        {
            try
            {
                foreach (SkinnedMeshRenderer renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    bool original = renderer.forceMatrixRecalculationPerRender;
                    entries.Add(new Entry { Renderer = renderer, Original = original });
                    if (!original) { renderer.forceMatrixRecalculationPerRender = true; ++Changed; }
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (Entry entry in entries)
                if (entry.Renderer && !entry.Original) entry.Renderer.forceMatrixRecalculationPerRender = false;
            entries.Clear();
        }
    }
}
#endif
