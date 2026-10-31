using System;
using System.Collections.Generic;
using TooManyLags;
using UnityEngine;
using UnityEngine.Animations;

namespace UnityEngine
{
    public partial class Transform
    {
        internal static bool FailNextParent;
        public Vector3 InverseTransformPoint(Vector3 value) { return Quaternion.Inverse(rotation) * (value - position); }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component
        { return gameObject.GetComponentsInChildren<T>(inactive); }
        public void SetParent(Transform value, bool keepWorld)
        {
            if (FailNextParent) { FailNextParent = false; throw new InvalidOperationException("injected parent failure"); }
            Vector3 p = position; Quaternion q = rotation;
            if (parent != null) parent.Children.Remove(this);
            parent = value; if (parent != null) parent.Children.Add(this);
            if (keepWorld) { position = p; rotation = q; }
        }
    }
}
namespace UnityEngine.Animations
{
    public struct ConstraintSource { public Transform sourceTransform; public float weight; }
    public class ParentConstraint : Component
    {
        public readonly List<ConstraintSource> Sources = new List<ConstraintSource>();
        public Vector3 Offset;
        public int sourceCount { get { return Sources.Count; } }
        public ConstraintSource GetSource(int index) { return Sources[index]; }
        public void SetSource(int index, ConstraintSource source) { Sources[index] = source; }
    }
}

internal static class BridgeEffectAttachmentTests
{
    private static int checks;
    private static void Check(bool value, string message) { ++checks; if (!value) throw new Exception(message); }
    private static void Near(Vector3 a, Vector3 b, string message) { Check((a - b).sqrMagnitude < .0000001f, message); }
    private static Transform Root(float x)
    { var go = new GameObject("test"); go.transform.position = new Vector3(x, 0f, 0f); return go.transform; }

    private static void ParentAndNativeScope()
    {
        Transform mob = Root(10f), anchor = Root(20f), fx = Root(10f), child = Root(10f);
        fx.SetParent(mob, false); fx.localPosition = new Vector3(0f, 2f, 0f);
        child.SetParent(fx, false); child.localPosition = new Vector3(0f, 0f, 3f);
        var visualComponent = child.gameObject.Add<SkinnedMeshRenderer>();
        fx.localScale = new Vector3(2f, 3f, 4f);
        var binding = new MobBridgeEffectAttachment(fx, mob, anchor, fx.localPosition, fx.localRotation, fx.localScale);
        Check(fx.parent == anchor, "FX did not attach to presentation anchor");
        Near(fx.position, new Vector3(20f, 2f, 0f), "FX offset not preserved at presented mob");
        Near(child.position, new Vector3(20f, 2f, 3f), "nested visual hierarchy lost attachment");
        Check(child.gameObject.GetComponent<SkinnedMeshRenderer>() == visualComponent, "effect was cloned or restarted");
        Near(mob.position, new Vector3(10f, 0f, 0f), "presentation moved actual mob root");
        Near(fx.localScale, new Vector3(2f, 3f, 4f), "presentation changed local FX scale");
        anchor.position = new Vector3(25f, 0f, 0f);
        Near(fx.position, new Vector3(25f, 2f, 0f), "effect emitter did not follow anchor between camera callbacks");

        binding.EnterNative();
        Check(fx.parent == mob, "network callback sees presentation parent");
        Near(fx.position, new Vector3(10f, 2f, 0f), "network callback sees fake position");
        binding.EnterNative(); fx.localPosition = new Vector3(0f, 4f, 0f);
        binding.ExitNative();
        Check(fx.parent == mob, "nested callback restores presentation prematurely");
        binding.ExitNative();
        Check(fx.parent == anchor, "native callback failed to restore presentation");
        Near(fx.localPosition, new Vector3(0f, 2f, 0f), "network interpolation overwrote visual attachment offset");
        binding.Dispose();
        Check(fx.parent == mob, "teardown did not restore original parent");
        Near(fx.localPosition, new Vector3(0f, 4f, 0f), "teardown lost native network update");
        fx.localPosition = new Vector3(0f, 9f, 0f); binding.Dispose();
        Near(fx.localPosition, new Vector3(0f, 9f, 0f), "duplicate teardown overwrote later native changes");
    }
    private static void WorldRootAndConstraints()
    {
        Transform mob = Root(10f), anchor = Root(30f), fx = Root(15f), unrelated = Root(99f);
        ParentConstraint constraint = fx.gameObject.Add<ParentConstraint>();
        constraint.Sources.Add(new ConstraintSource { sourceTransform = mob, weight = .35f });
        constraint.Sources.Add(new ConstraintSource { sourceTransform = unrelated, weight = .65f });
        constraint.Offset = new Vector3(0f, 2f, 0f);
        var binding = new MobBridgeEffectAttachment(fx, mob, anchor, new Vector3(5f, 0f, 0f),
            Quaternion.AngleAxis(30f, Vector3.up), new Vector3(1f, 1f, 1f));
        Near(fx.position, new Vector3(35f, 0f, 0f), "remote world-rooted FX not mapped to presented attachment");
        Check(constraint.GetSource(0).sourceTransform == anchor && constraint.GetSource(0).weight == .35f,
            "follow constraint source/weight not preserved");
        Check(constraint.GetSource(1).sourceTransform == unrelated && constraint.GetSource(1).weight == .65f,
            "unrelated constraint source changed");
        Near(constraint.Offset, new Vector3(0f, 2f, 0f), "constraint offset changed");
        for (int frame = 0; frame < 60; ++frame)
        {
            binding.EnterNative();
            Check(fx.parent == null && constraint.GetSource(0).sourceTransform == mob,
                "world FX native scope has fake parent/constraint");
            fx.position = new Vector3(15f + frame * .01f, 0f, 0f);
            binding.ExitNative();
            Near(fx.position, new Vector3(35f, 0f, 0f), "delayed FX world updates pulled flames away from displayed mob");
        }
        binding.Dispose();
        Check(fx.parent == null && constraint.GetSource(0).sourceTransform == mob, "world effect attachment not restored");
        Near(fx.position, new Vector3(15.59f, 0f, 0f), "last native FX position lost at disposal");
        Check(constraint.GetSource(1).sourceTransform == unrelated, "unrelated constraint changed at disposal");
    }
    private static void FailureAndScopeTeardown()
    {
        Transform mob = Root(10f), anchor = Root(20f), fx = Root(15f);
        Transform.FailNextParent = true; bool failed = false;
        try { new MobBridgeEffectAttachment(fx, mob, anchor, new Vector3(5f, 0f, 0f), Quaternion.identity, new Vector3(1f, 1f, 1f)); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && fx.parent == null, "partial attachment did not roll back original parent");
        Near(fx.position, new Vector3(15f, 0f, 0f), "partial attachment changed native position");
        var binding = new MobBridgeEffectAttachment(fx, mob, anchor, new Vector3(5f, 0f, 0f), Quaternion.identity, new Vector3(1f, 1f, 1f));
        binding.EnterNative(); fx.position = new Vector3(17f, 0f, 0f); binding.Dispose(); binding.ExitNative();
        Check(fx.parent == null && !binding.Alive, "teardown in native callback reattached disposed effect");
        Near(fx.position, new Vector3(17f, 0f, 0f), "teardown in native callback overwrote current native pose");
        var detached = new MobBridgeEffectAttachment(fx, mob, anchor, new Vector3(5f, 0f, 0f), Quaternion.identity, new Vector3(1f, 1f, 1f));
        fx.SetParent(null, true); Vector3 released = fx.position;
        Check(!detached.Alive, "external effect detachment remains controlled by preview");
        detached.Dispose();
        Check(fx.parent == null, "cleanup reparented an effect detached by vanilla");
        Near(fx.position, released, "cleanup snapped externally released effect to native root");
        var continuous = new MobBridgeEffectAttachment(fx, mob, anchor, new Vector3(5f, 0f, 0f), Quaternion.identity, new Vector3(1f, 1f, 1f));
        continuous.EnterNative(); fx.position = new Vector3(10f, 3f, 0f); continuous.ExitNative(true);
        Near(fx.position, new Vector3(20f, 3f, 0f), "continuous native FX offset update is discarded or left on hidden root");
        continuous.EnterNative(); fx.SetParent(mob, true); Vector3 reparented = fx.position; continuous.ExitNative();
        Check(!continuous.Alive && fx.parent == mob, "native attachment change was overwritten by presentation");
        Near(fx.position, reparented, "native attachment change snapped back to preview");
    }
    public static int Main()
    {
        try { ParentAndNativeScope(); WorldRootAndConstraints(); FailureAndScopeTeardown();
            Console.WriteLine("PASS: " + checks + " attached-FX parent/constraint/native-scope/cleanup checks (managed scene seam)."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
