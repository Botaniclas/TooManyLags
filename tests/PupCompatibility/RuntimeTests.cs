using System;
using System.Reflection;
using TooManyLags.PupCompatibility;
using HarmonyLib;
using UnityEngine;
internal static class RuntimeTests
{
    private static bool ClaimReady(ref bool __result) { __result=true;return false; }
    private static int checks;
    private static void Check(string name,bool value) { checks++;if(!value)throw new Exception(name); }
    private static FakePup World(bool dedicated)
    {
        Statics.Reset();FgnTakeover.Reset();ZNet.instance=new ZNet{Server=dedicated,Dedicated=dedicated};ZNet.IsSinglePlayer=false;
        var pup=new GameObject().Add(new FakePup());Plugin.Native=new NativePup(pup);Statics.Bounds=SupportBounds.Create();return pup;
    }
    private static GameObject Actor(out ZNetView view,out ZSyncTransform sync,out Rigidbody body)
    {
        var root=new GameObject();root.transform.position=new Vector3(10,0,10);
        view=root.Add(new ZNetView());sync=root.Add(new ZSyncTransform());body=root.Add(new Rigidbody());
        sync.Initialize(view,body);
        root.Add(new Character()).Initialize(view);Plugin.Native.Attach(root);return root;
    }
    private static void Takeover()
    {
        var pup=World(false);pup.Cell(0,true);pup.Cell(2,false);
        ZNetView view;ZSyncTransform sync;Rigidbody body;var root=Actor(out view,out sync,out body);
        view.Data.Position=new Vector3(140,0,10);Plugin.Native.ForceStep(Plugin.Native.Existing(root),false);
        Check("ready source is dynamic",!body.isKinematic);
        FgnTakeover.Snapshot snapshot;FgnTakeover.SyncPatch.Prefix(sync,view,out snapshot);
        Check("unloaded canonical target freezes before snap",snapshot.Guard&&body.isKinematic);
        root.transform.position=view.Data.Position;sync.SetPrevious(true);FgnTakeover.SyncPatch.Postfix(snapshot);
        Check("corrected target remains held",body.isKinematic);
        pup.Cell(2,true);Plugin.Native.ForceStep(snapshot.Guard,false);
        Check("completion releases native freeze",!body.isKinematic);
        int before=pup.ExactCalls;
        int collections=GC.CollectionCount(0);
        for(int i=0;i<100000;i++)FgnTakeover.SyncPatch.Prefix(sync,view,out snapshot);
        Check("steady owner invokes no readiness query",pup.ExactCalls==before&&!snapshot.Guard);
        Check("steady owner loop triggers no generation-zero collection",GC.CollectionCount(0)==collections);
        sync.SetPrevious(false);pup.Cell(2,false);bool result=true;
        bool run=FgnTakeover.MobPatch.Prefix(root.GetComponent<Character>(),ref result);
        Check("AI waits even before transform sync",!run&&!result&&body.isKinematic);
        view.Data.Owner=2;Plugin.Native.ForceStep(Plugin.Native.Existing(root),false);
        Check("lost ownership releases own native lock",!body.isKinematic);
        var player=new GameObject();player.Add(new Player());player.Add(new Rigidbody());
        Check("players get no body guard",!Plugin.Native.Attach(player));
        view.Data.Owner=1;sync.SetPrevious(false);
        var entries=new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ZDO,ZNetView>>();
        entries.Add(new System.Collections.Generic.KeyValuePair<ZDO,ZNetView>(view.Data,view));
        FgnTakeover.BeforeEcho(entries);Check("takeover preserves received canonical position",entries.Count==0);
        sync.SetPrevious(true);entries.Add(new System.Collections.Generic.KeyValuePair<ZDO,ZNetView>(view.Data,view));
        FgnTakeover.BeforeEcho(entries);Check("stable owner retains native FGN echo",entries.Count==1);
        sync.SetPrevious(false);root.Add(new Projectile());FgnTakeover.SyncPatch.Prefix(sync,view,out snapshot);
        Check("FGN custom projectile simulation is untouched",!snapshot.Guard);
        FgnTakeover.BeforeEcho(entries);Check("projectile echo is untouched",entries.Count==1);
        var cart=new GameObject();cart.transform.position=new Vector3(10,0,10);
        var cartView=cart.Add(new ZNetView());var cartSync=cart.Add(new ZSyncTransform());var cartBody=cart.Add(new Rigidbody());
        Plugin.Native.Attach(cart);cartView.Data.Position=new Vector3(140,0,10);
        FgnTakeover.SyncPatch.Prefix(cartSync,cartView,out snapshot);
        Check("vehicle terrain-freeze decision is not preempted",!snapshot.Guard&&!cartBody.isKinematic);
        entries.Clear();entries.Add(new System.Collections.Generic.KeyValuePair<ZDO,ZNetView>(cartView.Data,cartView));
        FgnTakeover.BeforeEcho(entries);Check("vehicle owned-position echo stays native",entries.Count==1);
    }
    private static void Structure()
    {
        var pup=World(false);pup.Cell(0,true);
        var root=new GameObject();root.transform.position=new Vector3(10,0,10);var view=root.Add(new ZNetView());
        var wear=root.Add(new WearNTear());wear.Initialize(view);wear.Expected=0;wear.Weather=5;
        pup.Manifest(false);wear.UpdateWear(0);
        Check("missing manifest blocks structural damage",wear.Damage==5&&wear.Updates==0);
        pup.Manifest(true);pup.Cell(0,false);wear.UpdateWear(1);
        Check("incomplete replacement still blocks statics",wear.Damage==10&&wear.Updates==0);
        pup.Cell(0,true);int before=pup.ExactCalls;wear.UpdateWear(2);
        Check("unsupported ready piece collapses normally",wear.Damage==115&&wear.Support==0);
        Check("pending support cache is cleared",wear.Clears==1);
        Check("one exact proof per native wear pass",pup.ExactCalls==before+1);
        wear.Expected=3;wear.UpdateWear(3);Check("real support prevents collapse",wear.Damage==120);
        view.Data.OwnerRevision++;wear.UpdateWear(4);Check("owner revision clears support cache",wear.Clears==2);
        pup.Active(0,false);Check("inactive replacement never certifies ready",!Plugin.Native.ReadyPoint(root.transform.position,8));
        pup.Active(0,true);pup.Transition(0,true);Check("transition blocks proof",!Plugin.Native.ReadyPoint(root.transform.position,8));
        pup.Transition(0,false);Check("finished transition permits proof",Plugin.Native.ReadyPoint(root.transform.position,8));
        pup.Exact=false;Check("missing physical recipe blocks proof",!Plugin.Native.ReadyPoint(root.transform.position,8));
        Check("nonfinite coordinates never certify",!Plugin.Native.ReadyPoint(new Vector3(float.NaN,0,0),8));
        Check("extreme coordinates cannot overflow grid loops",!Plugin.Native.ReadyPoint(new Vector3(float.MaxValue,0,0),8));
        Check("context scan treats extreme coordinates as uncertain",Plugin.Native.Relevant(new Vector3(float.MaxValue,0,0),8));
        Check("extreme modded bounds cannot truncate tiled proof",!Plugin.Native.ReadyBounds(new Bounds(new Vector3(10,0,10),new Vector3(100000000000f,1,1))));
        pup.Exact=true;
        var passField=AccessTools.Field(typeof(Statics),"current");
        passField.SetValue(null,new Statics.Pass{Wear=wear});
        try
        {
            Check("pass initial readiness",Statics.Allow(wear));before=pup.ExactCalls;
            view.Data.OwnerRevision++;Check("same pass rejects obsolete owner revision",Statics.Allow(wear)&&pup.ExactCalls==before+1);
        }
        finally {passField.SetValue(null,default(Statics.Pass));}
    }
    private static void HarmonyPipeline()
    {
        var pup=World(false);pup.Cell(0,true);pup.Cell(2,false);
        ZNetView view;ZSyncTransform sync;Rigidbody body;var root=Actor(out view,out sync,out body);
        view.Data.Position=new Vector3(140,0,10);
        FiresGhettoNetworkMod.EchoFixture.Receive(view);
        Check("patched private FGN echo preserves takeover destination",view.Data.Position.x==140);
        sync.CustomFixedUpdate(0);
        Check("Harmony prefix freezes before actual FGN prefix",FiresGhettoNetworkMod.OwnershipHandoffPatches.SawPreFreeze);
        Check("Harmony postfix rechecks actual corrected position",root.transform.position.x==140&&body.isKinematic);
        sync.SetPrevious(false);root.transform.position=new Vector3(10,0,10);
        Check("patched native AI entry waits before correction",!pup.MoveMob(root.GetComponent<Character>())&&body.isKinematic);
        pup.Cell(2,true);sync.CustomFixedUpdate(0);
        Check("completed canonical destination releases in same handoff",!body.isKinematic);
    }
    private static void Dedicated()
    {
        var pup=World(true);pup.DedicatedCell(0);pup.Exact=false;
        ZNetView view;ZSyncTransform sync;Rigidbody body;var root=Actor(out view,out sync,out body);
        var guard=Plugin.Native.Existing(root);
        Check("dedicated local records require real physics",!Plugin.Native.ForceStep(guard,false)&&body.isKinematic);
        pup.Exact=true;Check("dedicated proxy completion releases",Plugin.Native.ForceStep(guard,false)&&!body.isKinematic);
        pup.RecipeId(0,null);Check("missing dedicated recipe identifier blocks readiness",!Plugin.Native.ReadyPoint(root.transform.position,8));
        pup.RecipeId(0,"a");pup.Restore(0,true);Check("world-save restore cannot certify empty proxy proof",!Plugin.Native.ReadyPoint(root.transform.position,8));
        pup.Restore(0,false);Check("completed restore regains readiness",Plugin.Native.ReadyPoint(root.transform.position,8));
        pup.BodyCell(5,true);Check("native owner body cells also prevent full unloading",pup.Retained(5));
        pup.BodyCell(5,false);Check("unneeded cells become unloadable again",!pup.Retained(5));
        pup=World(false);Check("no PUP cells has fast empty path",Plugin.Native.Empty);
        Check("ordinary unbaked area needs no proxy work",Plugin.Native.ReadyPoint(new Vector3(5,0,5),8)&&pup.ExactCalls==0);
        pup.Manifest(false);Check("empty metadata is not proof before manifest",!Plugin.Native.Empty&&!Plugin.Native.ReadyPoint(new Vector3(5,0,5),8));
    }
    public static void Main()
    {
        World(false);var harmony=new Harmony("botanic.minimal.tests");
        var fgn=new Harmony(Plugin.FgnId);
        foreach(Type t in new[]{typeof(NativeWaitPatch),typeof(NativeRetentionPatch),typeof(Statics.WearPatch),typeof(Statics.SupportPatch),typeof(Statics.PreservePatch),typeof(Statics.RepairPatch)})harmony.CreateClassProcessor(t).Patch();
        fgn.Patch(AccessTools.Method(typeof(ZSyncTransform),"CustomFixedUpdate"),prefix:new HarmonyMethod(AccessTools.Method(typeof(FiresGhettoNetworkMod.OwnershipHandoffPatches),"ZSyncTransform_CustomFixedUpdate_Prefix")));
        FgnTakeover.Install(harmony,typeof(FiresGhettoNetworkMod.OwnershipHandoffPatches).Assembly);
        try { Takeover();Structure();Dedicated();HarmonyPipeline();ModernDedicated();Console.WriteLine("PASS: "+checks+" native-freeze/FGN/statics regression assertions; 100000 steady-owner calls make no readiness requests."); }
        finally { harmony.UnpatchSelf();fgn.UnpatchSelf(); }
    }
    private static void ModernDedicated()
    {
        ZNet.instance=new ZNet{Server=true,Dedicated=true};ZNet.IsSinglePlayer=false;
        var pup=new GameObject().Add(new ModernPup());Plugin.Native=new NativePup(pup);pup.DedicatedCell(0);
        Check("new native dedicated readiness is detected by actual method call",Plugin.Native.NativeDedicatedReadiness);
        Check("new native body-cell retention is detected without version metadata",Plugin.Native.NativeBodyRetention);
        var hooks=new Harmony("botanic.modern.tests");
        hooks.CreateClassProcessor(typeof(NativeWaitPatch)).Patch();
        hooks.CreateClassProcessor(typeof(NativeRetentionPatch)).Patch();
        try
        {
            Check("native retention supplement is omitted when already implemented",Harmony.GetPatchInfo(Plugin.Native.RetainMethod)==null);
            var point=new Vector3(10,0,10);pup.Prepared=false;
            Check("new native incomplete proxy proof cannot be bypassed",!pup.ReplacementReady(point,8)&&pup.ExactCalls==0);
            pup.Prepared=true;int proofs=pup.PreparedCalls;
            Check("new native complete proxy resumes with unchanged supplemental proof",pup.ReplacementReady(point,8));
            Check("body readiness keeps native preparation queue without synchronous Exact",pup.ExactCalls==0&&pup.PreparedCalls==proofs+1);
            pup.RecipeId(0,null);Check("new native readiness retains missing recipe protection",!pup.ReplacementReady(point,8));
            pup.RecipeId(0,"a");pup.Restore(0,true);Check("world-save restore protection survives new native readiness",!pup.ReplacementReady(point,8));
            pup.Restore(0,false);Check("new native readiness resumes after restore completion",pup.ReplacementReady(point,8));
            pup.BodyCell(5,true);Check("new native body retention runs unchanged",pup.Retained(5));
            pup.BodyCell(5,false);Check("new native empty body cell is unloadable",!pup.Retained(5));
            pup.Prepared=false;Check("statics/handoff proof requires modern prepared collision too",!Plugin.Native.ReadyPoint(point,8));
            pup.Prepared=true;Check("statics/handoff resumes with both proofs",Plugin.Native.ReadyPoint(point,8));
            NativePup.ForceWait=true;
            try { Check("FGN owner-change ForceWait still blocks new native guard",!pup.ReplacementReady(point,8)); }
            finally { NativePup.ForceWait=false; }
            var claimed=new HarmonyMethod(typeof(RuntimeTests).GetMethod("ClaimReady",BindingFlags.Static|BindingFlags.NonPublic));
            claimed.priority=Priority.First;hooks.Patch(Plugin.Native.ReadyMethod,prefix:claimed);
            pup.Prepared=false;int before=pup.PreparedCalls;
            Check("foreign prefix skipping native proof cannot release incomplete proxies",!pup.ReplacementReady(point,8)&&pup.PreparedCalls==before+1);
            Check("foreign readiness skip still causes no synchronous body preparation",pup.ExactCalls==2);
        }
        finally { hooks.UnpatchSelf(); }
    }
}
