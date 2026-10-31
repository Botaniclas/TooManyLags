#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using UnityEngine;
using UnityEngine.Animations;

namespace TooManyLags
{
    // Owns only a visual effect's temporary parent/constraint binding. The
    // original object, particle systems, timers and network identity survive.
    internal sealed class MobBridgeEffectAttachment : IDisposable
    {
        private sealed class Constraint
        { internal ParentConstraint Value; internal int Index; internal ConstraintSource Native, Presented; }
        private readonly Transform root, source, anchor;
        private Transform nativeParent;
        private Vector3 nativePosition, nativeScale, shownPosition, shownScale;
        private Quaternion nativeRotation, shownRotation;
        private readonly System.Collections.Generic.List<Constraint> constraints = new System.Collections.Generic.List<Constraint>();
        private int depth;
        private bool disposed, attached;
        internal Transform Root { get { return root; } }
        internal Transform Source { get { return source; } }
        internal bool Alive { get { return !disposed && root && source && anchor && (!attached || depth > 0 || root.parent == anchor); } }

        internal MobBridgeEffectAttachment(Transform effect, Transform originalSource, Transform presentationAnchor,
            Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            root = effect; source = originalSource; anchor = presentationAnchor;
            CaptureNative();
            shownPosition = localPosition; shownRotation = localRotation; shownScale = localScale;
            foreach (ParentConstraint value in effect.GetComponentsInChildren<ParentConstraint>(true))
                for (int i = 0; i < value.sourceCount; ++i)
                {
                    ConstraintSource native = value.GetSource(i);
                    if (native.sourceTransform != source) continue;
                    ConstraintSource presented = native; presented.sourceTransform = anchor;
                    constraints.Add(new Constraint { Value = value, Index = i, Native = native, Presented = presented });
                }
            try { Attach(); }
            catch { Dispose(); throw; }
        }
        private void CaptureNative()
        {
            nativeParent = root.parent; nativePosition = root.localPosition;
            nativeRotation = root.localRotation; nativeScale = root.localScale;
        }
        private void RestoreConstraints()
        {
            foreach (Constraint item in constraints)
                if (item.Value && item.Index < item.Value.sourceCount &&
                    item.Value.GetSource(item.Index).sourceTransform == anchor)
                {
                    ConstraintSource original = item.Native;
                    if (!original.sourceTransform) original.sourceTransform = null;
                    item.Value.SetSource(item.Index, original);
                }
        }
        private void RestoreNative()
        {
            RestoreConstraints();
            if (!root) return;
            if (!System.Object.ReferenceEquals(nativeParent, null) && !nativeParent)
            { root.SetParent(null, true); return; }
            root.SetParent(nativeParent ? nativeParent : null, false);
            root.localPosition = nativePosition; root.localRotation = nativeRotation; root.localScale = nativeScale;
        }
        private void Attach()
        {
            if (disposed || !root || !source || !anchor) return;
            root.SetParent(anchor, false);
            attached = true;
            root.localPosition = shownPosition; root.localRotation = shownRotation; root.localScale = shownScale;
            foreach (Constraint item in constraints)
                if (item.Value && item.Index < item.Value.sourceCount &&
                    item.Value.GetSource(item.Index).sourceTransform == source)
                    item.Value.SetSource(item.Index, item.Presented);
        }
        internal void EnterNative()
        {
            if (!Alive || depth++ != 0) return;
            // Keep visual script changes, but never feed a display offset back
            // into ZSyncTransform's interpolation or owner serialization.
            shownPosition = root.localPosition; shownRotation = root.localRotation; shownScale = root.localScale;
            RestoreNative();
        }
        internal void ExitNative(bool updateAttachment = false)
        {
            if (depth <= 0 || --depth != 0 || disposed || !root || !source || !anchor) return;
            Transform previousParent = nativeParent;
            CaptureNative();
            if (nativeParent != previousParent) { Dispose(); return; }
            if (updateAttachment)
            {
                shownPosition = source.InverseTransformPoint(root.position);
                shownRotation = Quaternion.Inverse(source.rotation) * root.rotation;
            }
            Attach();
        }
        public void Dispose()
        {
            if (disposed) return;
            // If teardown occurs in a native scope, that scope already has the
            // current logical pose. Do not overwrite a newer native update.
            if (depth == 0 && root && root.parent == anchor) RestoreNative();
            else RestoreConstraints();
            disposed = true; constraints.Clear();
        }
    }
}
#endif
