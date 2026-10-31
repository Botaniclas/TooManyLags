using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public class Transform
    {
        private string value;
        private Transform owner;
        public readonly List<Transform> Children = new List<Transform>();
        public static int ChildReads, NameReads;
        public string name { get { ++NameReads; return value; } set { this.value = value; } }
        public Transform parent { get { return owner; } set { if(owner != null) owner.Children.Remove(this); owner = value; if(owner != null) owner.Children.Add(this); } }
        public int childCount { get { return Children.Count; } }
        public Transform GetChild(int index) { ++ChildReads; return Children[index]; }
        public static implicit operator bool(Transform item) { return item != null; }
    }
    public class GameObject
    {
        public readonly Transform transform = new Transform();
        public string Label;
        public bool Destroyed;
        public static implicit operator bool(GameObject item) { return item != null && !item.Destroyed; }
    }
}
internal static class Plugin
{
    internal static readonly List<string> Trace = new List<string>();
    internal static void Warn(string message) { Trace.Add("warn:"+message); }
}
internal sealed class MobBridgeEffectAttachment
{
    internal readonly string Label;
    internal bool Alive = true;
    internal int Depth;
    internal bool ThrowEnter, ThrowExit, ThrowDispose;
    internal Action Entered, Exited;
    internal MobBridgeEffectAttachment(string label) { Label = label; }
    internal void EnterNative()
    {
        Plugin.Trace.Add("enter:"+Label); ++Depth;
        if(Entered != null) Entered();
        if(ThrowEnter) throw new InvalidOperationException("enter failed");
    }
    internal void ExitNative(bool updateAttachment)
    {
        Plugin.Trace.Add("exit:"+Label+":"+updateAttachment);
        if(Depth > 0) --Depth;
        if(Exited != null) Exited();
        if(ThrowExit) throw new InvalidOperationException("exit failed");
    }
    internal void Dispose()
    {
        Plugin.Trace.Add("dispose:"+Label); Alive = false;
        if(ThrowDispose) throw new InvalidOperationException("dispose failed");
    }
}
internal interface IPerformance
{
    string[] Paths(Transform[] bones);
    void Reset(); void Enable(Transform root); void Disable();
    void Bind(GameObject item, MobBridgeEffectAttachment effect);
    object Begin(Transform root, GameObject[] values);
    Exception BeginObserved(Transform root, GameObject[] values, out object state);
    Exception End(Transform root, GameObject[] values, object state, Exception error);
    int Allocations { get; }
    void CheckPool();
}
internal static class PerformanceEquivalenceTests
{
    private static int checks;
    private static void Check(bool valid, string message) { ++checks; if(!valid) throw new Exception(message); }
    private static Transform Bone(string name, Transform parent) { return new Transform { name = name, parent = parent }; }
    private static void Flatten(Transform root, List<Transform> result)
    { result.Add(root); foreach(Transform child in root.Children) Flatten(child,result); }
    private static void CompareRig(Transform root, bool subset)
    {
        var all = new List<Transform>(); Flatten(root,all);
        if(subset) all.RemoveAll(t => t != root && t.Children.Count == 0 && all.IndexOf(t)%3 == 0);
        Transform[] bones = all.ToArray();
        var reference = new ReferencePerformance(); var optimized = new OptimizedPerformance();
        string[] a=reference.Paths(bones), b=optimized.Paths(bones);
        Check(a.Length == b.Length,"rig size changed");
        var unique = new HashSet<string>();
        for(int i=0;i<a.Length;++i) { Check(a[i] == b[i],"rig path changed: "+a[i]+" / "+b[i]); Check(unique.Add(b[i]),"ambiguous rig path"); }
    }
    private static void RigTests()
    {
        string[] names = {"", "Head", "Head", "Hand/left", "3:X:0/", ":", "\u00e4\u4e2d", "FX", "Head"};
        var random = new Random(34799);
        for(int run=0;run<100;++run)
        {
            var root=Bone("model",null); var all=new List<Transform>{root};
            for(int i=0;i<200;++i) all.Add(Bone(names[random.Next(names.Length)],all[run%3==0?0:random.Next(all.Count)]));
            CompareRig(root,false); CompareRig(root,true);
            // Re-read the current hierarchy after FX/equipment insertion,
            // renaming, removal and same-name sibling reorder. No persistent cache.
            Bone("Head",root); Bone("FX/child",root);
            all[1].name="3:X:0/";
            Transform move=root.Children[0]; root.Children.RemoveAt(0); root.Children.Add(move);
            CompareRig(root,false);
            if(all[2].Children.Count == 0) all[2].parent=null;
            CompareRig(root,false);
        }
        var wide=Bone("root",null); for(int i=0;i<256;++i) Bone(i%2==0?"Head":"FX",wide);
        var list=new List<Transform>();Flatten(wide,list);Transform[] values=list.ToArray();
        Transform.ChildReads=0;Transform.NameReads=0;new ReferencePerformance().Paths(values);
        int oldChild=Transform.ChildReads,oldNames=Transform.NameReads;
        Transform.ChildReads=0;Transform.NameReads=0;new OptimizedPerformance().Paths(values);
        Check(Transform.ChildReads == 256 && Transform.ChildReads < oldChild,"rig sibling scan not linear");
        Check(Transform.NameReads == 256 && Transform.NameReads < oldNames,"rig repeated native name reads remain");
        Console.WriteLine("PASS: rig paths identical; 256-sibling fixture GetChild calls "+oldChild+" -> "+Transform.ChildReads+", name reads "+oldNames+" -> "+Transform.NameReads+".");
        var chain=Bone("model",null); Transform last=chain;
        for(int i=0;i<100;++i) last=Bone("bone",last);
        CompareRig(chain,false);list.Clear();Flatten(chain,list);
        Transform.ChildReads=0;new OptimizedPerformance().Paths(list.ToArray());
        Check(Transform.ChildReads == 0,"single-child chains should not scan siblings");
    }
    private static GameObject Item(string label) { return new GameObject { Label = label }; }
    private static void Invoke(IPerformance impl, Transform root, GameObject[] items, Exception error, Action body)
    {
        object scope=impl.Begin(root,items);
        if(body != null) body();
        Check(Object.ReferenceEquals(error,impl.End(root,items,scope,error)),"native exception identity changed");
        impl.CheckPool();
    }
    private static string Scenario(IPerformance impl, int kind)
    {
        impl.Reset();Plugin.Trace.Clear();
        var root=Bone("root",null);var unrelated=Bone("unrelated",null);
        var a=Item("A");var b=Item("B");var c=Item("unbound");var dead=Item("destroyed");dead.Destroyed=true;
        var ea=new MobBridgeEffectAttachment("A");var eb=new MobBridgeEffectAttachment("B");
        var items=new[]{a,b,null,c,dead};
        impl.Bind(a,ea);impl.Bind(b,eb);
        if(kind != 0) impl.Enable(root);
        if(kind == 1) { Invoke(impl,root,null,null,null); }
        else if(kind == 2) { Invoke(impl,unrelated,items,null,null); }
        else if(kind == 3) { eb.Alive=false;Invoke(impl,root,items,null,null); }
        else if(kind == 4) { Invoke(impl,root,new[]{a,a,b},null,null); }
        else if(kind == 5) { Invoke(impl,root,items,new Exception("native failed"),null); }
        else if(kind == 6) { ea.ThrowEnter=true;Invoke(impl,root,items,null,null); }
        else if(kind == 7) { ea.ThrowExit=true;Invoke(impl,root,items,null,null); }
        else if(kind == 8) { Invoke(impl,root,items,null,delegate { ea.Dispose(); impl.Disable(); }); }
        else if(kind == 9)
        {
            Invoke(impl,root,items,null,delegate { Invoke(impl,root,items,null,null); });
        }
        else if(kind == 10)
        {
            bool nested=false;
            ea.Entered=delegate { if(nested)return; nested=true; Invoke(impl,root,new[]{b},null,null); };
            Invoke(impl,root,items,null,null);
        }
        else if(kind == 11)
        {
            bool nested=false;
            ea.Exited=delegate { if(nested)return; nested=true; Invoke(impl,root,new[]{b},null,null); };
            Invoke(impl,root,items,null,null);
        }
        else if(kind == 12)
        {
            var states=new List<object>();
            for(int i=0;i<24;++i) states.Add(impl.Begin(root,items));
            for(int i=states.Count-1;i>=0;--i) impl.End(root,items,states[i],null);
            impl.CheckPool();
            Invoke(impl,root,items,null,null);
        }
        else if(kind == 13)
        {
            // Teardown while native callback is active, then a fresh epoch.
            object state=impl.Begin(root,items); impl.Reset(); impl.End(root,items,state,null);
            impl.Enable(root);impl.Bind(a,ea);impl.Bind(b,eb);Invoke(impl,root,items,null,null);
        }
        else if(kind == 14)
        {
            ea.ThrowExit=true; ea.ThrowDispose=true;
            try { Invoke(impl,root,items,null,null);throw new Exception("expected cleanup failure"); }
            catch(InvalidOperationException error) { Check(error.Message == "dispose failed","cleanup exception changed"); }
            impl.CheckPool();
            ea.ThrowExit=false;ea.ThrowDispose=false;
            Invoke(impl,root,new[]{b},null,null);
        }
        else if(kind == 15)
        {
            // Failed prefix cleanup retains Harmony's original out-state.
            // It must not be pooled/reused before the finalizer retries it.
            ea.ThrowEnter=true;ea.ThrowExit=true;ea.ThrowDispose=true;
            object failed;Exception error=impl.BeginObserved(root,items,out failed);
            Check(error is InvalidOperationException && error.Message == "dispose failed","prefix exception changed");
            Check(failed != null,"failed prefix lost Harmony state");
            impl.CheckPool();Invoke(impl,root,new[]{b},null,null);
            ea.ThrowEnter=false;ea.ThrowExit=false;ea.ThrowDispose=false;
            Check(Object.ReferenceEquals(impl.End(root,null,failed,error),error),"retry exception changed");
        }
        else Invoke(impl,root,items,null,null);
        impl.CheckPool();
        // A throwing Dispose aborts the original loop before B is restored.
        // Preserve that failure behavior rather than hiding it in this audit.
        Check(ea.Depth == 0 && eb.Depth == (kind == 14 ? 1 : 0),"native scope depth changed, scenario "+kind);
        return String.Join("\n",Plugin.Trace.ToArray());
    }
    private static void FxTests()
    {
        for(int i=0;i<16;++i)
        { string a=Scenario(new ReferencePerformance(),i),b=Scenario(new OptimizedPerformance(),i);Check(a == b,"FX callback trace differs, scenario "+i+"\n"+a+"\n--\n"+b); }
        foreach(IPerformance impl in new IPerformance[]{new ReferencePerformance(),new OptimizedPerformance()})
        {
            impl.Reset(); var root=Bone("root",null);var item=Item("A");var effect=new MobBridgeEffectAttachment("A");var values=new[]{item};
            for(int i=0;i<100;++i) Invoke(impl,root,values,null,null);
            Check(impl.Allocations == 0,"idle scope allocated");
            impl.Enable(root);
            for(int i=0;i<100;++i) Invoke(impl,root,values,null,null);
            Check(impl.Allocations == 0,"unbound scope allocated");
            impl.Bind(item,effect);
            for(int i=0;i<1000;++i) Invoke(impl,root,values,null,null);
            Check(impl.Allocations == (impl is OptimizedPerformance ? 1 : 1000),"scope reuse allocation count changed");
            Check(effect.Depth == 0,"steady-state scope depth leaked");impl.CheckPool();
        }
        Console.WriteLine("PASS: native FX callback/exception traces identical, nesting/reentry/teardown covered; 1000 matched calls allocate 1000 -> 1 scope/list pairs; idle allocates zero.");
    }
    public static int Main()
    {
        try { RigTests();FxTests();ClockPublicationTests.Run();Console.WriteLine("PASS: "+checks+" extracted production rig/FX performance equivalence checks; managed Unity seam, no live FPS claim.");return 0; }
        catch(Exception error) { Console.Error.WriteLine(error);return 1; }
    }
}
