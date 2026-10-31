using System;
using System.Collections.Generic;
using UnityEngine;
using MobBridgeEffects = @@NAME@@Performance;

internal sealed class @@NAME@@Performance : IPerformance
{
    private static readonly Dictionary<int, bool> active = new Dictionary<int, bool>();
    private static readonly Dictionary<Transform, @@NAME@@Performance> roots = new Dictionary<Transform, @@NAME@@Performance>();
    private static readonly Dictionary<Transform, MobBridgeEffectAttachment> bindings = new Dictionary<Transform, MobBridgeEffectAttachment>();
    private static int allocations;
    private readonly bool optimized = "@@NAME@@" == "Optimized";
    private static NativeFxScope Allocated(NativeFxScope value) { ++allocations; return value; }
    @@SCOPE@@
    @@RIG@@
    @@BEFORE@@
    @@AFTER@@
    public string[] Paths(Transform[] bones) { return RigPaths(bones); }
    public int Allocations { get { return allocations; } }
    public void Reset()
    { active.Clear(); roots.Clear(); bindings.Clear(); allocations = 0; @@CLEAR@@ }
    public void Enable(Transform root) { active[1] = true; roots[root] = this; }
    public void Disable() { active.Clear(); roots.Clear(); bindings.Clear(); }
    public void Bind(GameObject item, MobBridgeEffectAttachment effect) { bindings[item.transform] = effect; }
    public object Begin(Transform root, GameObject[] values)
    { NativeFxScope state; BeforeContinuous(root, values, out state); return state; }
    public Exception BeginObserved(Transform root, GameObject[] values, out object result)
    {
        NativeFxScope state = null;
        try { BeforeContinuous(root, values, out state); return null; }
        catch (Exception error) { return error; }
        finally { result = state; }
    }
    public Exception End(Transform root, GameObject[] values, object state, Exception error)
    { return AfterContinuous(root, values, (NativeFxScope)state, error); }
    private void Existing(GameObject item, Transform root)
    { Plugin.Trace.Add("existing:"+(item == null ? "null" : item.Label)); }
    public void CheckPool()
    {
        if (!optimized) return;
        var type = typeof(NativeFxScope);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        int expected = (int)type.GetField("retained", flags).GetValue(null), count = 0;
        object item = type.GetField("spare", flags).GetValue(null);
        var seen = new HashSet<object>();
        while(item != null)
        {
            if(!seen.Add(item)) throw new Exception("pool cycle / double return");
            if(((NativeFxScope)item).Values.Count != 0) throw new Exception("pooled Unity reference retained");
            item = type.GetField("next", flags).GetValue(item); ++count;
        }
        if(count != expected || count > 8) throw new Exception("unbounded or inconsistent pool");
    }
}
