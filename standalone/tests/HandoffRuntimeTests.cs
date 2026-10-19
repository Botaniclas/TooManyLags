using System;
using System.Collections.Generic;
using TooManyLagsStandalone;
using UnityEngine;
internal static class HandoffRuntimeTests
{
    private static int checks; private static long token;
    private static void Check(bool value,string reason) { checks++; if(!value)throw new Exception(reason); }
    private static void Reset()
    { Time.unscaledTime=0;Time.frameCount++;ZDOMan.instance.Objects.Clear();ZDOMan.Session=11;ZNet.instance.Server=true;ZNet.instance.Reference=new Vector3();ZNet.IsSinglePlayer=false;ZNetScene.instance.Views.Clear();ZNetScene.instance.Ready=true;ZNetScene.instance.Readiness=null;ZNetScene.instance.ReadinessPoints.Clear();ZoneSystem.instance=new ZoneSystem();InventoryGui.instance.Current=null;Plugin.Network.Sent.Clear();Plugin.Network.AdditionalCapable.Clear();Plugin.Network.OnSend=null;Plugin.Controller=new AuthorityController();Plugin.Local=new LocalAgent();IncineratorProtection.Active=true;RequestRouter.Reset();ZRoutedRpc.instance.Delivered.Clear();ZRoutedRpc.instance.Responses.Clear(); }
    private static ZNetView Object(Kinds kind,long id=1)
    { var root=new GameObject { Kind=kind };var view=root.Add<ZNetView>();view.Data=new ZDO { m_uid=new ZDOID(id) };ZDOMan.instance.Objects.Add(view.Data.m_uid,view.Data);ZNetScene.instance.Views.Add(view.Data.m_uid,view);return view; }
    private static ZPackage Ticket(ZDO data,Kinds kind,long n,ZDOID mob=default(ZDOID))
    { var p=new ZPackage();p.Write(n);p.Write(data.m_uid);p.Write(11L);p.Write(22L);p.Write((ushort)0);p.Write((int)kind);p.Write(mob);p.Write(data.DataRevision);p.SetPos(0);return p; }
    private static int Result()
    { var e=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(e.Message==Message.Result,"Expected commit result");e.Data.ReadLong();e.Data.ReadZDOID();return e.Data.ReadInt(); }
    private static void Commit(LocalAgent agent,ZDO data,Kinds kind,long n,ZDOID mob=default(ZDOID)) { agent.Receive(Message.Commit,Ticket(data,kind,n,mob)); }
    private static void InventoryProtection()
    {
        Reset();var view=Object(Kinds.Containers);var box=view.gameObject.Add<Container>();var agent=Plugin.Local;long n=++token;
        InventoryGui.instance.Current=box; // GUI is already visible, m_inUse is not set yet.
        Commit(agent,view.Data,Kinds.Containers,n);Check(Result()==2 && view.Data.Owner==11,"GUI-before-inUse opening race must block");
        InventoryGui.instance.Current=null;box.Used=true;Commit(agent,view.Data,Kinds.Containers,n);Check(Result()==2,"Native local inventory use blocks");
        box.Used=false;view.Data.Set(ZDOVars.s_inUse,1);Commit(agent,view.Data,Kinds.Containers,n);Check(Result()==2,"Replicated inventory use blocks");
        view.Data.Set(ZDOVars.s_inUse,0);agent.Outgoing(view.Data.m_uid,"RPC_RequestOpen".GetStableHashCode());Commit(agent,view.Data,Kinds.Containers,n);Check(Result()==2,"Pending opening request blocks");
        agent.Response(box);Commit(agent,view.Data,Kinds.Containers,n);Check(Result()==1 && view.Data.Owner==22,"Closed idle inventory permits handoff");
        ushort revision=view.Data.OwnerRevision;Commit(agent,view.Data,Kinds.Containers,n);Check(Result()==1 && view.Data.OwnerRevision==revision,"Repeated commit must not transfer twice");
        Reset();view=Object(Kinds.Containers);view.gameObject.Add<Container>();view.gameObject.Add<Container>();Commit(Plugin.Local,view.Data,Kinds.Containers,++token);Check(Result()==3 && view.Data.Owner==11,"Ambiguous multi-inventory roots retain vanilla");
        Reset();view=Object(Kinds.Containers);box=view.gameObject.Add<Container>();box.Loading=true;n=++token;Commit(Plugin.Local,view.Data,Kinds.Containers,n);Check(Result()==2 && view.Data.Owner==11,"Inventory snapshot import blocks source handoff");box.Loading=false;Commit(Plugin.Local,view.Data,Kinds.Containers,n);Check(Result()==1,"Finished inventory import permits source handoff");
        Reset();view=Object(Kinds.Containers);n=++token;var cancel=new ZPackage();cancel.Write(n);cancel.Write(view.Data.m_uid);cancel.SetPos(0);Plugin.Local.Receive(Message.Cancel,cancel);Commit(Plugin.Local,view.Data,Kinds.Containers,n);Check(Result()==0 && view.Data.Owner==11,"Canceled in-flight commit must not transfer");
    }
    private static void TextEditingSourceVeto()
    {
        Reset();var view=Object(Kinds.Signs);long n=++token;
        LocalAgent.TextEditBusy=delegate(ZDO value){return ReferenceEquals(value,view.Data);};
        Commit(Plugin.Local,view.Data,Kinds.Signs,n);Check(Result()==0&&view.Data.Owner==11,"text editing rejects an in-flight commit before its owner write");
        LocalAgent.TextEditBusy=null;Commit(Plugin.Local,view.Data,Kinds.Signs,n);
        Check(Result()==0&&view.Data.Owner==11,"ending text input cannot replay the rejected takeover token");
        Commit(Plugin.Local,view.Data,Kinds.Signs,++token);Check(Result()==1&&view.Data.Owner==22,"fresh guarded request works after text protection ends");
        Reset();view=Object(Kinds.Portals);Plugin.Network.LegacyText.Add(22);
        Check(!Plugin.Controller.Handoffs.Request(view.Data,22,Kinds.Portals,ZDOID.None),"older text recipient cannot bypass native fallback");
        Plugin.Network.LegacyText.Clear();
    }
    private static void RefillAndUse()
    {
        Reset();var view=Object(Kinds.Smelters);Plugin.Local.Refilling(view.Data,"RPC_AddOre".GetStableHashCode());long n=++token;
        Commit(Plugin.Local,view.Data,Kinds.Smelters,n);Check(Result()==2,"Refill lease starts immediately");
        Time.unscaledTime=.75f;Plugin.Local.Refilling(view.Data,"RPC_AddOre".GetStableHashCode());
        Time.unscaledTime=1f;Commit(Plugin.Local,view.Data,Kinds.Smelters,n);Check(Result()==2,"Another refill renews protection beyond the original one-second deadline");
        Time.unscaledTime=1.749f;Commit(Plugin.Local,view.Data,Kinds.Smelters,n);Check(Result()==2,"Renewed refill lease lasts one full second");
        Time.unscaledTime=1.75f;Commit(Plugin.Local,view.Data,Kinds.Smelters,n);Check(Result()==1,"Expired refill protection releases at the new deadline");
        Reset();view=Object(Kinds.TamedAnimals);var animal=view.gameObject.Add<Tameable>();var actor=view.gameObject.Add<Character>();var tameAi=view.gameObject.Add<MonsterAI>();tameAi.Init();actor.Tamed=true;n=++token;
        animal.Rider=true;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==2,"Rider retains ownership");
        animal.Rider=false;animal.m_commandable=true;view.Data.Set(ZDOVars.s_follow,"player");Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==2,"Following retains ownership");
        view.Data.Set(ZDOVars.s_follow,"");animal.Summon=true;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==3,"Magic summon must stay vanilla");
        animal.Summon=false;n=++token;actor.Attack=true;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==2,"Tamed attacks cannot be interrupted by grid transfer");
        actor.Attack=false;tameAi.Target=Object(Kinds.None,2).gameObject.Add<Character>();Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==2,"Fighting tamed animal retains its current AI owner");
        tameAi.Target=null;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==1,"Idle ordinary tamed animal may transfer");
    }
    private static void IncineratorBurnProtection()
    {
        Reset();var view=Object(Kinds.Incinerators);var box=view.gameObject.Add<Container>();var station=view.gameObject.Add<Incinerator>();station.m_container=box;long n=++token;
        Check(IncineratorProtection.Supported,"Native private incinerator state reader binds");
        box.Used=true;Commit(Plugin.Local,view.Data,Kinds.Incinerators,n);Check(Result()==2 && view.Data.Owner==11,"Open incinerator inventory blocks handoff");
        box.Used=false;station.Burning=true;Commit(Plugin.Local,view.Data,Kinds.Incinerators,n);Check(Result()==2 && view.Data.Owner==11,"Native burning state blocks handoff after inventory closes");
        Time.unscaledTime=600;Commit(Plugin.Local,view.Data,Kinds.Incinerators,n);Check(Result()==2,"Long native burn is protected without a guessed timeout");
        foreach(string response in new[] { "RPC_OpenResponse","RPC_StackResponse","RPC_TakeAllResponse" })
        { Check(!IncineratorProtection.Allow(box,22,response),"Remote inventory operation is denied during actual burn: "+response);Check(view.Replies[view.Replies.Count-1]=="22:"+response+":False","Burn rejection uses matching native negative response"); }
        int responses=view.Replies.Count;
        foreach(Type hook in new[] {typeof(IncineratorOpenPatch),typeof(IncineratorStackPatch),typeof(IncineratorTakeAllPatch)})
        {
            var prefix=hook.GetMethod("Prefix",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            Check(!(bool)prefix.Invoke(null,new object[] {box,22L,true}),"Actual burn prefix denies native remote action: "+hook.Name);
            int replies=view.Replies.Count;Check(!(bool)prefix.Invoke(null,new object[] {box,22L,false}) && view.Replies.Count==replies,"Already-cancelled native request cannot emit another response");
        }
        Check(view.Replies.Count==responses+3,"All three actual prefixes emit one rejection apiece");responses=view.Replies.Count;
        Check(IncineratorProtection.Allow(box,11,"RPC_OpenResponse") && view.Replies.Count==responses,"Owner's own burn actions are not intercepted");
        station.Burning=false;Check(IncineratorProtection.Allow(box,22,"RPC_OpenResponse") && view.Replies.Count==responses,"Finished burn allows native open protocol without synthetic reply");
        Commit(Plugin.Local,view.Data,Kinds.Incinerators,n);Check(Result()==1 && view.Data.Owner==22,"Native burn completion permits one handoff");
        Reset();view=Object(Kinds.Incinerators);box=view.gameObject.Add<Container>();station=view.gameObject.Add<Incinerator>();station.m_container=new Container();station.Burning=true;
        Check(IncineratorProtection.Allow(box,22,"RPC_OpenResponse"),"Guard does not capture unrelated inventory on same root");Commit(Plugin.Local,view.Data,Kinds.Incinerators,++token);Check(Result()==3,"Incinerator with mismatched inventory cannot be managed");
        Reset();view=Object(Kinds.Incinerators);box=view.gameObject.Add<Container>();station=view.gameObject.Add<Incinerator>();station.m_container=box;view.gameObject.Add<Container>();Commit(Plugin.Local,view.Data,Kinds.Incinerators,++token);Check(Result()==3,"Incinerator with ambiguous inventories is excluded");
        Reset();view=Object(Kinds.Incinerators);box=view.gameObject.Add<Container>();station=view.gameObject.Add<Incinerator>();station.m_container=box;station.Burning=true;IncineratorProtection.Active=false;Check(IncineratorProtection.Allow(box,22,"RPC_OpenResponse"),"Disabled local feature does not intercept vanilla operations");
    }
    private static void StationaryReadinessGeometry()
    {
        Reset();ZNet.instance.Reference=new Vector3 { x=0,z=0 };ZDOMan.Session=11;
        ZNetScene.instance.Readiness=delegate(Vector3 p) { return p.x==0; };
        var view=Object(Kinds.Doors);view.Data.Point=new Vector3 { x=64 };
        Check(Plugin.Local.Ready(view.Data.m_uid,11,0,0),"Loaded stationary root at footprint edge uses recipient-centered complete area");
        Check(ZNetScene.instance.ReadinessPoints.Count==1 && ZNetScene.instance.ReadinessPoints[0].x==0,"Stationary readiness calls native area readiness at actual recipient reference");
        var second=Object(Kinds.Doors,2);second.Data.Point=new Vector3 {x=128};Check(Plugin.Local.Ready(second.Data.m_uid,11,0,0) && ZNetScene.instance.ReadinessPoints.Count==1,"Stationary roots across adjacent object zones reuse one player-area check per frame");
        ZoneSystem.instance.Loaded=false;Time.frameCount++;Check(!Plugin.Local.Ready(view.Data.m_uid,11,0,0),"Stationary root still requires its own terrain zone loaded");ZoneSystem.instance.Loaded=true;
        view.Data.Point=new Vector3 { x=256 };Time.frameCount++;Check(!Plugin.Local.Ready(view.Data.m_uid,11,0,0),"Distant stationary root outside native active footprint cannot be approved");
        view.Data.Point=new Vector3 { x=64 };view.gameObject.activeInHierarchy=false;Time.frameCount++;Check(!Plugin.Local.Ready(view.Data.m_uid,11,0,0),"Inactive stationary root cannot be approved");view.gameObject.activeInHierarchy=true;
        Reset();ZNetScene.instance.Readiness=delegate(Vector3 p) { return p.x==0; };view=Object(Kinds.Containers);view.Data.Point=new Vector3 { x=64 };var box=view.gameObject.Add<Container>();box.m_privacy=Container.PrivacySetting.Private;
        Check(!Plugin.Local.Ready(view.Data.m_uid,11,0,0) && ZNetScene.instance.ReadinessPoints[0].x==64,"Private inventories retain object-centered native area check");
        Reset();ZNetScene.instance.Readiness=delegate(Vector3 p) { return p.x==0; };view=Object(Kinds.Containers);view.Data.Point=new Vector3 { x=64 };box=view.gameObject.Add<Container>();box.Loading=true;
        Check(!Plugin.Local.Ready(view.Data.m_uid,11,0,0),"Recipient cannot acknowledge a partially loading inventory");box.Loading=false;Time.frameCount++;Check(Plugin.Local.Ready(view.Data.m_uid,11,0,0),"Finished public inventory uses stationary recipient-centered readiness");
        foreach(int variant in new[] {0,1,2,3,4,5})
        {
            Reset();ZNetScene.instance.Readiness=delegate(Vector3 p) { return p.x==0; };view=Object(variant==0?Kinds.TamedAnimals:Kinds.Doors);view.Data.Point=new Vector3 { x=64 };
            if(variant==0)view.gameObject.Add<Character>();if(variant==1)view.gameObject.Add<Vagon>();if(variant==2)view.m_persistent=false;if(variant==3)view.m_distant=true;if(variant==4)view.transform.parent=new Transform();if(variant==5)view.gameObject.Add<Container>().m_rootObjectOverride=new GameObject().Add<ZNetView>();
            Check(!Plugin.Local.Ready(view.Data.m_uid,11,0,0) && ZNetScene.instance.ReadinessPoints[0].x==64,"Dynamic/non-root/non-native object retains full object-centered geometry: "+variant);
        }
    }
    private static void ReceiptAndRouter()
    {
        Reset();var view=Object(Kinds.Doors);var source=Plugin.Local;var receiver=new LocalAgent();var controller=Plugin.Controller;
        controller.Handoffs.Request(view.Data,22,Kinds.Doors,ZDOID.None);controller.Handoffs.Tick(0);
        var prepare=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(prepare.Message==Message.Prepare,"Prepare receiver first");
        ZDOMan.Session=22;receiver.Receive(prepare.Message,prepare.Data);var prepared=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];controller.Handoffs.Receive(22,prepared.Message,prepared.Data);
        ZDOMan.Session=11;Time.unscaledTime=.1f;controller.Handoffs.Tick(.1f);var commit=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(commit.Message==Message.Commit,"Then request source commit");
        source.Receive(commit.Message,commit.Data);var sourceResult=new ZPackage(Plugin.Network.Sent[Plugin.Network.Sent.Count-1].Data.GetArray());Check(Result()==1 && view.Data.Owner==22,"Source commits through native SetOwner");
        controller.Handoffs.Receive(11,Message.Result,sourceResult);
        controller.Handoffs.Observe(view.Data);Check(controller.Handoffs.Committing(view.Data.m_uid),"Coordinator receipt alone must not unlock gameplay");
        var call=new ZRoutedRpc.RoutedRPCData { m_msgID=71,m_senderPeerID=33,m_targetPeerID=11,m_targetZDO=view.Data.m_uid,m_methodHash="UseDoor".GetStableHashCode() };call.m_parameters.Write(123);call.m_parameters.SetPos(0);
        Check(!RequestRouter.Handle(ZRoutedRpc.instance,call,false),"Hold owner RPC until actual receiver activation");call.m_parameters.SetPos(0);call.m_parameters.Write(999);RequestRouter.Flush(.2f);Check(ZRoutedRpc.instance.Delivered.Count==0,"No early delivery");
        ZDOMan.Session=22;Plugin.Local=receiver;receiver.Observe(view.Data);var activated=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(activated.Message==Message.Activated,"Receiver confirms imported owner revision");controller.Handoffs.Receive(22,activated.Message,activated.Data);
        Check(!controller.Handoffs.Committing(view.Data.m_uid),"Matching activation releases gate");ZDOMan.Session=999;RequestRouter.Flush(view.Data.m_uid,.3f);
        Check(ZRoutedRpc.instance.Delivered.Count==1 && ZRoutedRpc.instance.Delivered[0].m_targetPeerID==22 && ZRoutedRpc.instance.Delivered[0].m_senderPeerID==33,"Exactly one delivery with original sender");
        Check(ZRoutedRpc.instance.Delivered[0].m_parameters.ReadInt()==123,"Queued packet must own an immutable payload copy");RequestRouter.Flush(.4f);Check(ZRoutedRpc.instance.Delivered.Count==1,"No repeated dispatch");
        Time.unscaledTime=13f;controller.Handoffs.Tick(13);Check(!controller.Handoffs.AcceptsForward(view.Data.m_uid,11,22,1,"UseDoor".GetStableHashCode()),"Old-source relay receipts expire");
    }
    private static void MobAndUnload()
    {
        Reset();var target=Object(Kinds.None,2).gameObject.Add<Player>();target.Side=Character.Faction.Players;
        var view=Object(Kinds.None);var ai=view.gameObject.Add<MonsterAI>();ai.Init();var actor=view.gameObject.Add<Character>();ai.Target=target;long n=++token;
        actor.Attack=true;Commit(Plugin.Local,view.Data,Kinds.None,n,target.GetZDOID());Check(Result()==2,"Attack must finish before mob transfer");
        actor.Attack=false;actor.Stagger=true;Commit(Plugin.Local,view.Data,Kinds.None,n,target.GetZDOID());Check(Result()==2,"Stagger protection");
        actor.Stagger=false;target.Dead=true;Commit(Plugin.Local,view.Data,Kinds.None,n,target.GetZDOID());Check(Result()==0 && view.Data.Owner==11,"Dead player target invalidates mob handoff");
        // A reloaded AI is a new instance even if the stored ZDO/owner tuple
        // stays unchanged. Its actual None target must replace the old report.
        target.Dead=false;Plugin.Local.SetMobWatch(true);Plugin.Local.TargetChanged(ai);Check(view.Data.GetZDOID(MobPolicy.TargetKey)==target.GetZDOID(),"Initial live player target report");
        var reloadedRoot=new GameObject();var reloadedView=reloadedRoot.Add<ZNetView>();reloadedView.Data=view.Data;reloadedRoot.Add<Character>();var reloadedAi=reloadedRoot.Add<MonsterAI>();reloadedAi.Init();ZNetScene.instance.Views[view.Data.m_uid]=reloadedView;
        Plugin.Local.Instance(reloadedView);Check(view.Data.GetZDOID(MobPolicy.TargetKey).IsNone(),"Reload cannot preserve a stale target publication marker");
        Reset();view=Object(Kinds.Doors);var receiver=new LocalAgent();ZDOMan.Session=22;ZNetScene.instance.Ready=false;receiver.Receive(Message.Prepare,Ticket(view.Data,Kinds.Doors,++token));var msg=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];msg.Data.ReadLong();msg.Data.ReadZDOID();Check(!msg.Data.ReadBool(),"Receiver cannot approve unloaded area");
        // Thousands of traversed handoffs leave no long-lived local leases.
        Reset();for(int i=0;i<2000;i++) { view=Object(Kinds.Smelters,i+1);Plugin.Local.Refilling(view.Data,"RPC_AddOre".GetStableHashCode()); }
        Time.unscaledTime=30;Plugin.Local.Tick(30);Check(!Plugin.Local.HasWork,"Expired station leases are fully released");
        Reset();view=Object(Kinds.Smelters);ZNet.IsSinglePlayer=true;Plugin.Local.Refilling(view.Data,"RPC_AddOre".GetStableHashCode());Plugin.Local.Outgoing(view.Data.m_uid,"RPC_RequestOpen".GetStableHashCode());Check(!Plugin.Local.HasWork,"Single-player sessions cannot accumulate dormant multiplayer leases");
    }
    private sealed class CustomDoor : Door { }
    private static void OptionalClients()
    {
        Reset();var plain=new GameObject();plain.Add<Door>();Check(NativeOwnership.SafePrefab(plain,Kinds.Doors),"Known non-consumptive door supports the native fallback");
        plain.Add<Container>();Check(!NativeOwnership.SafePrefab(plain,Kinds.Doors),"Hidden inventory excludes otherwise simple native prefab");
        Check(!NativeOwnership.SafePrefab(new GameObject(),Kinds.Doors),"A category label alone cannot authorize a native fallback");
        var custom=new GameObject();custom.Add<CustomDoor>();Check(!NativeOwnership.SafePrefab(custom,Kinds.Doors),"Unknown custom action cannot bypass source consent");
        Check(!NativeOwnership.SafePrefab(new GameObject(),Kinds.Smelters) && !NativeOwnership.SafePrefab(new GameObject(),Kinds.TamedAnimals),"Refill stations and animals have no unguarded native source fallback");
        var view=Object(Kinds.Containers);view.Data.Owner=33;
        Check(!Plugin.Controller.Handoffs.Request(view.Data,22,Kinds.Containers,ZDOID.None) && !Plugin.Controller.Handoffs.Pending(view.Data.m_uid),"Vanilla-owned chest stays vanilla without accumulating a handoff job");
        view=Object(Kinds.Smelters,2);view.Data.Owner=33;Check(!Plugin.Controller.Handoffs.Request(view.Data,22,Kinds.Smelters,ZDOID.None),"Vanilla-owned refill station cannot lose unseen insertions");
        view=Object(Kinds.None,3);view.Data.Owner=33;Check(!Plugin.Controller.Handoffs.Request(view.Data,22,Kinds.None,new ZDOID(7)),"Unknown native mob target never authorizes a mob transfer");

        Reset();view=Object(Kinds.Doors);view.Data.Owner=33;var controller=Plugin.Controller;var receiver=new LocalAgent();
        Check(controller.Handoffs.Request(view.Data,22,Kinds.Doors,ZDOID.None),"Vanilla source can hand a simple door to a compatible player");controller.Handoffs.Tick(0);
        var prepare=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(prepare.Message==Message.Prepare && prepare.Uid==22,"Compatible receiver still provides actual area readiness");
        ZDOMan.Session=22;receiver.Receive(prepare.Message,prepare.Data);var prepared=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];controller.Handoffs.Receive(22,prepared.Message,prepared.Data);
        ZDOMan.Session=11;view.Data.Owner=22; // Simulate a native import's owner half.
        controller.Handoffs.Tick(.1f);Check(view.Data.OwnerRevision==0,"Owner-half import cannot authorize a second native owner write");
        view.Data.Owner=33;controller.Handoffs.Tick(.2f);Check(view.Data.Owner==22 && view.Data.OwnerRevision==1,"Host native fallback changes owner once after full identity checks");
        Check(controller.Handoffs.Committing(view.Data.m_uid),"Custom receiver activation remains required after native-source transfer");
        ZDOMan.Session=22;receiver.Observe(view.Data);var active=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];controller.Handoffs.Receive(22,active.Message,active.Data);Check(!controller.Handoffs.Committing(view.Data.m_uid),"Receiver activation releases native-source transition");

        Reset();view=Object(Kinds.Doors);view.Data.Owner=33;controller=Plugin.Controller;controller.DesiredOwner=34;view.Data.Set(ZDOVars.s_user,123);
        Check(controller.Handoffs.Request(view.Data,34,Kinds.Doors,ZDOID.None),"Two vanilla peers use a bounded native transition");controller.Handoffs.Tick(0);Check(view.Data.Owner==33,"Native visible use blocks ownership transfer");
        view.Data.Set(ZDOVars.s_user,0);Time.unscaledTime=.1f;controller.Handoffs.Request(view.Data,34,Kinds.Doors,ZDOID.None);controller.Handoffs.Tick(.1f);Check(view.Data.Owner==34 && view.Data.OwnerRevision==1 && !controller.Handoffs.Pending(view.Data.m_uid),"Native visible use release wakes immediately, without waiting five seconds for a custom acknowledgement");
        Check(Plugin.Network.Sent.Count==0,"All-vanilla transition sends no custom RPC");
        var nativeReceipts=(System.Collections.IDictionary)typeof(Handoffs).GetField("receipts",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(controller.Handoffs);
        Check(nativeReceipts.Count==0,"All-vanilla transition leaves no unnecessary receipt cache");

        Reset();view=Object(Kinds.Containers);var box=view.gameObject.Add<Container>();controller=Plugin.Controller;controller.DesiredOwner=33;InventoryGui.instance.Current=box;
        Check(!controller.Handoffs.Request(view.Data,33,Kinds.Containers,ZDOID.None) && view.Data.Owner==11,"Open capable-source inventory cannot transfer to a receiver lacking activation support");
        InventoryGui.instance.Current=null;Check(!controller.Handoffs.Request(view.Data,33,Kinds.Containers,ZDOID.None),"Closed inventory also retains vanilla when receiver readiness cannot be confirmed");
        view=Object(Kinds.Smelters,2);Check(!controller.Handoffs.Request(view.Data,33,Kinds.Smelters,ZDOID.None),"Refill station cannot lose requests before vanilla receiver activation");
        view=Object(Kinds.TamedAnimals,3);Check(!controller.Handoffs.Request(view.Data,33,Kinds.TamedAnimals,ZDOID.None),"Animal handoffs retain native AI when the receiver lacks local guards");
        Check(Plugin.Network.Sent.Count==0,"Unsupported mixed transfer creates no custom messages or pending job");
        view=Object(Kinds.Doors,4);Check(controller.Handoffs.Request(view.Data,33,Kinds.Doors,ZDOID.None),"Known simple action may transfer from a guarded source to a vanilla receiver");controller.Handoffs.Tick(0);
        var commit=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(commit.Message==Message.Commit && commit.Uid==11,"Native simple-object receiver needs no unavailable prepare RPC");
        Plugin.Local.Receive(commit.Message,commit.Data);var nativeResult=new ZPackage(Plugin.Network.Sent[Plugin.Network.Sent.Count-1].Data.GetArray());Check(Result()==1 && view.Data.Owner==33,"Capable source still runs its actual guard before a simple native-receiver commit");controller.Handoffs.Receive(11,Message.Result,nativeResult);controller.Handoffs.Observe(view.Data);
        Check(!controller.Handoffs.Committing(view.Data.m_uid),"Vanilla simple-object recipient cannot leave a permanent activation barrier");

        Reset();view=Object(Kinds.Doors);view.Data.Owner=33;controller=Plugin.Controller;controller.Handoffs.Request(view.Data,22,Kinds.Doors,ZDOID.None);controller.Handoffs.Tick(0);
        Plugin.Network.AdditionalCapable.Add(33);controller.Handoffs.Tick(.1f);
        Check(view.Data.Owner==33 && !controller.Handoffs.Pending(view.Data.m_uid),"A source handshake cancels its pending native job before an unguarded host commit");
        Check(controller.Handoffs.Request(view.Data,22,Kinds.Doors,ZDOID.None),"Newly capable source restarts through local-consent handoff");

        Reset();controller=Plugin.Controller;controller.DesiredOwner=34;
        for(int i=0;i<4096;i++) { view=Object(Kinds.Doors,i+1);view.Data.Owner=33;Check(controller.Handoffs.Request(view.Data,34,Kinds.Doors,ZDOID.None),"Native handoff capacity accepts bounded active work"); }
        var deferred=Object(Kinds.Doors,4097);deferred.Data.Owner=33;Check(!controller.Handoffs.Request(deferred.Data,34,Kinds.Doors,ZDOID.None),"Capacity saturation defers extra native work");
        controller.Handoffs.Tick(0);Check(controller.Handoffs.Request(deferred.Data,34,Kinds.Doors,ZDOID.None),"Deferred native work can start as soon as capacity is released");controller.Handoffs.Tick(.1f);Check(deferred.Data.Owner==34,"Dense native batch completes its deferred transfer");
    }
    private static Network.Envelope Last(Message message)
    { for(int i=Plugin.Network.Sent.Count-1;i>=0;i--)if(Plugin.Network.Sent[i].Message==message)return Plugin.Network.Sent[i];throw new Exception("Expected "+message); }
    private static ZPackage Copy(ZPackage p) { var copy=new ZPackage(p.GetArray());copy.SetPos(0);return copy; }
    private static void StandActionsAndStatus()
    {
        Reset();var view=Object(Kinds.ItemStands);var stand=view.gameObject.Add<ItemStand>();long n=++token;
        stand.Invoking.Add("UpdateAttach");Commit(Plugin.Local,view.Data,Kinds.ItemStands,n);Check(Result()==2 && view.Data.Owner==11,"Pending stand item attachment blocks the source commit");
        stand.Invoking.Clear();stand.Invoking.Add("UpdateOrientation");Commit(Plugin.Local,view.Data,Kinds.ItemStands,n);Check(Result()==2,"Pending stand orientation remains protected");
        stand.Invoking.Clear();Commit(Plugin.Local,view.Data,Kinds.ItemStands,n);Check(Result()==1,"Finished stand action permits one source handoff");
        Reset();view=Object(Kinds.ItemStands);var armor=view.gameObject.Add<ArmorStand>();armor.Invoking.Add("UpdateAttach");
        Commit(Plugin.Local,view.Data,Kinds.ItemStands,++token);Check(Result()==2,"Armor stand attachment is protected too");
        Reset();view=Object(Kinds.Doors);n=++token;
        Plugin.Local.Receive(Message.Status,Ticket(view.Data,Kinds.Doors,n));Check(Result()==0 && view.Data.Owner==11,"Unknown status settles without executing a commit");
        Commit(Plugin.Local,view.Data,Kinds.Doors,n);Check(Result()==0 && view.Data.Owner==11,"A delayed commit cannot execute after its status fence");
        Commit(Plugin.Local,view.Data,Kinds.Doors,++token);Check(Result()==1 && view.Data.OwnerRevision==1,"A new identified transaction may transfer normally");
        for(int i=0;i<50;i++){Plugin.Local.Receive(Message.Status,Ticket(view.Data,Kinds.Doors,token));Check(Result()==1 && view.Data.OwnerRevision==1,"Status/replayed replies never write ownership twice");}

        Reset();view=Object(Kinds.Doors);var controller=Plugin.Controller;var receiver=new LocalAgent();
        controller.Handoffs.Request(view.Data,22,Kinds.Doors,ZDOID.None);controller.Handoffs.Tick(0);
        ZDOMan.Session=22;var prepare=Last(Message.Prepare);receiver.Receive(prepare.Message,Copy(prepare.Data));var prepared=Last(Message.Prepared);
        ZDOMan.Session=999;controller.Handoffs.Receive(22,prepared.Message,Copy(prepared.Data));controller.Handoffs.Tick(.1f);
        Check(Last(Message.Commit).Uid==11,"Real source alone gets the commit");
        int commits=0;foreach(var sent in Plugin.Network.Sent)if(sent.Message==Message.Commit)commits++;
        Time.unscaledTime=20;controller.Handoffs.Tick(20);
        int laterCommits=0;foreach(var sent in Plugin.Network.Sent)if(sent.Message==Message.Commit)laterCommits++;
        Check(laterCommits==commits && Last(Message.Status).Uid==11 && view.Data.Owner==11,"Lost reply requests status, never a second commit or a timeout steal");
        controller.Handoffs.Cancel(view.Data.m_uid);Check(controller.Handoffs.Committing(view.Data.m_uid),"An already-sent commit stays fenced until its actual result is reconciled");
        ZDOMan.Session=11;var status=Last(Message.Status);Plugin.Local.Receive(status.Message,Copy(status.Data));var result=Last(Message.Result);
        ZDOMan.Session=999;controller.Handoffs.Receive(11,result.Message,Copy(result.Data));
        Check(!controller.Handoffs.Pending(view.Data.m_uid),"Definite unchanged-source outcome clears the pending transaction");
    }
    private static void RecipientFloorAndUnload()
    {
        Reset();var view=Object(Kinds.Doors);view.Data.Owner=22;view.Data.OwnerRevision=1;view.Data.DataRevision=39;
        var controller=Plugin.Controller;controller.RecordRecipient(view.Data,22,1,40);controller.RecordRecipient(view.Data,22,1,38);
        ZDOMan.Session=999;
        var call=new ZRoutedRpc.RoutedRPCData {m_msgID=201,m_senderPeerID=33,m_targetPeerID=22,m_targetZDO=view.Data.m_uid,m_methodHash="UseDoor".GetStableHashCode()};call.m_parameters.Write(77);
        Check(!RequestRouter.Handle(ZRoutedRpc.instance,call,false) && ZRoutedRpc.instance.Delivered.Count==0,"Matching owner revision alone cannot release stale final object data");
        var receiver=new LocalAgent();var request=Last(Message.Ready);ZDOMan.Session=22;receiver.Receive(request.Message,Copy(request.Data));var answer=Last(Message.ReadyResult);
        ZDOMan.Session=999;controller.Recipients.Receive(22,answer.Message,Copy(answer.Data));Check(ZRoutedRpc.instance.Delivered.Count==0,"Lower state revision is explicitly rejected");
        view.Data.DataRevision=40;Time.unscaledTime=.5f;controller.Recipients.Ready(view.Data);request=Last(Message.Ready);
        ZDOMan.Session=22;receiver.Receive(request.Message,Copy(request.Data));answer=Last(Message.ReadyResult);
        ZDOMan.Session=999;controller.Recipients.Receive(22,answer.Message,Copy(answer.Data));
        Check(ZRoutedRpc.instance.Delivered.Count==1 && ZRoutedRpc.instance.Delivered[0].m_parameters.ReadInt()==77,"The final state releases the original interaction exactly once");
        ZDOMan.Session=22;receiver.Unload(view);ZNetScene.instance.Views.Remove(view.Data.m_uid);var lost=Last(Message.ReadyLost);
        ZDOMan.Session=999;controller.Recipients.Receive(22,lost.Message,Copy(lost.Data));Check(!controller.Recipients.Ready(view.Data),"Instance unload invalidates the acknowledged readiness cache");
        ZNet.instance.Server=false;ZDOMan.Session=22;Plugin.Local=receiver;
        var late=new ZRoutedRpc.RoutedRPCData {m_msgID=202,m_senderPeerID=33,m_targetPeerID=22,m_targetZDO=view.Data.m_uid,m_methodHash="UseDoor".GetStableHashCode()};late.m_parameters.Write(88);
        Check(!RequestRouter.Handle(ZRoutedRpc.instance,late,true),"Missing receiver view relays before native execution");var relay=Last(Message.Relay);
        ZNet.instance.Server=true;ZDOMan.Session=999;controller.Recipients.Forget(view.Data.m_uid);RequestRouter.Relayed(22,Copy(relay.Data));
        Check(ZRoutedRpc.instance.Delivered.Count==1,"A stale-instance relay is not bounced back immediately");
        var root=new GameObject {Kind=Kinds.Doors};var reloaded=root.Add<ZNetView>();reloaded.Data=view.Data;ZNetScene.instance.Views[view.Data.m_uid]=reloaded;
        Time.unscaledTime=1;controller.Recipients.Ready(view.Data);request=Last(Message.Ready);
        ZDOMan.Session=22;receiver.Receive(request.Message,Copy(request.Data));answer=Last(Message.ReadyResult);
        ZDOMan.Session=999;controller.Recipients.Receive(22,answer.Message,Copy(answer.Data));
        Check(ZRoutedRpc.instance.Delivered.Count==2 && ZRoutedRpc.instance.Delivered[1].m_parameters.ReadInt()==88,"Reloaded view releases the late action without duplicate execution");
        RequestRouter.Flush(2);Check(ZRoutedRpc.instance.Delivered.Count==2,"No later fallback sweep repeats completed actions");
    }
    private static void DelayedReadiness()
    {
        Reset();var view=Object(Kinds.Doors);view.Data.Owner=22;view.Data.OwnerRevision=1;view.Data.DataRevision=50;
        var controller=Plugin.Controller;controller.RecordRecipient(view.Data,22,1,50);ZDOMan.Session=999;
        var call=new ZRoutedRpc.RoutedRPCData {m_msgID=301,m_senderPeerID=33,m_targetPeerID=22,m_targetZDO=view.Data.m_uid,m_methodHash="UseDoor".GetStableHashCode()};
        RequestRouter.Handle(ZRoutedRpc.instance,call,false);var first=Last(Message.Ready);long original=Copy(first.Data).ReadLong();
        for(int i=1;i<=6;i++)
        {
            Time.unscaledTime=i*.5f;Check(!controller.Recipients.Ready(view.Data),"Unanswered proof remains pending");
            Check(Copy(Last(Message.Ready).Data).ReadLong()==original,"High-latency readiness retries retain the original token");
        }
        var response=new ZPackage();response.Write(original);response.Write(view.Data.m_uid);response.Write(true);response.SetPos(0);
        controller.Recipients.Receive(22,Message.ReadyResult,Copy(response));
        Check(ZRoutedRpc.instance.Delivered.Count==1 && controller.Recipients.Ready(view.Data),"A reply delayed across six retries still releases exactly one interaction");
        controller.RecordRecipient(view.Data,22,1,51);view.Data.DataRevision=51;
        Check(!controller.Recipients.Ready(view.Data),"A higher final-state floor invalidates old readiness");
        long newer=Copy(Last(Message.Ready).Data).ReadLong();Check(newer!=original,"Changed state requirement receives a fresh token");
        controller.Recipients.Receive(22,Message.ReadyResult,Copy(response));Check(!controller.Recipients.Ready(view.Data),"The old ready reply cannot certify the new state floor");
        response=new ZPackage();response.Write(newer);response.Write(view.Data.m_uid);response.Write(true);response.SetPos(0);
        controller.Recipients.Receive(22,Message.ReadyResult,Copy(response));Check(controller.Recipients.Ready(view.Data),"The current proof accepts readiness");
        var lost=new ZPackage();lost.Write(newer);lost.Write(view.Data.m_uid);lost.SetPos(0);controller.Recipients.Receive(22,Message.ReadyLost,lost);
        controller.Recipients.Receive(22,Message.ReadyResult,Copy(response));Check(!controller.Recipients.Ready(view.Data),"Unload retires the token before a stale ready response can restore it");
        Check(Copy(Last(Message.Ready).Data).ReadLong()!=newer,"Reload must acknowledge a new proof identity");
    }
    private static void TargetedQueueCost()
    {
        Reset();var controller=Plugin.Controller;ZDOMan.Session=999;
        for(int i=1;i<=100;i++)
        {
            var view=Object(Kinds.Doors,i);view.Data.Owner=22;view.Data.OwnerRevision=1;view.Data.DataRevision=10;
            controller.RecordRecipient(view.Data,22,1,10);
            RequestRouter.Handle(ZRoutedRpc.instance,new ZRoutedRpc.RoutedRPCData {m_msgID=i,m_senderPeerID=33,m_targetPeerID=22,m_targetZDO=view.Data.m_uid,m_methodHash="UseDoor".GetStableHashCode()},false);
        }
        var probes=new List<Network.Envelope>();foreach(var sent in Plugin.Network.Sent)if(sent.Message==Message.Ready)probes.Add(sent);
        Check(probes.Count==100,"Only waiting real transfers/delivery failures need readiness probes");controller.RecipientLookups=0;
        foreach(var probe in probes)
        {
            var payload=Copy(probe.Data);long n=payload.ReadLong();ZDOID id=payload.ReadZDOID();var response=new ZPackage();response.Write(n);response.Write(id);response.Write(true);response.SetPos(0);
            controller.Recipients.Receive(22,Message.ReadyResult,response);
        }
        Check(controller.RecipientLookups==100 && ZRoutedRpc.instance.Delivered.Count==100,"100 replies visit 100 object queues, not 10000 unrelated queues");
        controller.RecipientLookups=0;for(int i=0;i<1000;i++)RequestRouter.Flush(new ZDOID(i+1000),1);
        Check(controller.RecipientLookups==0,"No waiting action means no recipient-readiness work");
    }
    private static void DenseAndReentrantReceipts()
    {
        Reset();var controller=Plugin.Controller;var source=Plugin.Local;var receiver=new LocalAgent();
        for(int i=1;i<=1000;i++)
        {
            var view=Object(Kinds.Doors,i);Time.unscaledTime=i*.01f;ZDOMan.Session=999;
            Check(controller.Handoffs.Request(view.Data,22,Kinds.Doors,ZDOID.None),"Dense unclicked transfer starts");controller.Handoffs.Tick(Time.unscaledTime);
            var prepare=Last(Message.Prepare);ZDOMan.Session=22;receiver.Receive(prepare.Message,Copy(prepare.Data));var answer=Last(Message.Prepared);
            ZDOMan.Session=999;controller.Handoffs.Receive(22,answer.Message,Copy(answer.Data));controller.Handoffs.Tick(Time.unscaledTime);
            var commit=Last(Message.Commit);ZDOMan.Session=11;source.Receive(commit.Message,Copy(commit.Data));var result=Last(Message.Result);
            ZDOMan.Session=999;controller.Handoffs.Receive(11,result.Message,Copy(result.Data));
            ZDOMan.Session=22;receiver.Observe(view.Data);answer=Last(Message.Activated);
            ZDOMan.Session=999;controller.Handoffs.Receive(22,answer.Message,Copy(answer.Data));
            Check(view.Data.Owner==22 && view.Data.OwnerRevision==1 && !controller.Handoffs.Committing(view.Data.m_uid),"Dense unclicked transfer settles exactly once");
        }
        var proofs=(System.Collections.IDictionary)typeof(RecipientReadiness).GetField("proofs",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(controller.Recipients);
        Check(proofs.Count==0 && controller.RecipientLookups==0,"1000 real handoffs without interactions allocate no recipient proof cache and perform no routing readiness lookups");
        var waiting=Object(Kinds.Doors,1001);controller.Handoffs.Request(waiting.Data,22,Kinds.Doors,ZDOID.None);waiting.Data.SetOwner(22);controller.Handoffs.Observe(waiting.Data);
        controller.AvailabilityLookups=0;Time.unscaledTime=11;controller.Handoffs.Tick(11);
        Check(controller.AvailabilityLookups<=3,"One waiting receiver does not repeatedly check 1000 already-completed receipts");
        Reset();controller=Plugin.Controller;var first=Object(Kinds.Doors,1);var second=Object(Kinds.Doors,2);
        controller.Handoffs.Request(first.Data,22,Kinds.Doors,ZDOID.None);first.Data.SetOwner(22);controller.Handoffs.Observe(first.Data);
        bool nested=false;Plugin.Network.OnSend=delegate(Network.Envelope sent)
        {
            if(nested || sent.Message!=Message.ActivationCheck)return;nested=true;
            controller.Handoffs.Request(second.Data,22,Kinds.Doors,ZDOID.None);second.Data.SetOwner(22);controller.Handoffs.Observe(second.Data);
        };
        Time.unscaledTime=.5f;controller.Handoffs.Tick(.5f);Plugin.Network.OnSend=null;
        Check(nested && controller.Handoffs.Committing(second.Data.m_uid),"Inline listen-host callback may add another receipt without invalidating iteration");
    }
    private static void ShipPassengerOwnership()
    {
        Reset();var view=Object(Kinds.Vehicles);var ship=view.gameObject.Add<Ship>();
        var aboard=Object(Kinds.None,81).gameObject.Add<Player>();ship.Passengers.Add(aboard);
        Commit(Plugin.Local,view.Data,Kinds.Vehicles,++token);Check(Result()==2 && view.Data.Owner==11,"Passengers block a grid transfer to an offboard player that vanilla would undo");
        Time.unscaledTime=.1f;Plugin.Local.Tick(.1f);Check(view.Data.Owner==11,"Occupied ship does not oscillate back to a shore owner on retry");
        var target=Object(Kinds.None,82).gameObject.Add<Player>();target.GetComponent<ZNetView>().Data.Owner=22;ship.Passengers.Add(target);
        Time.unscaledTime=.21f;Plugin.Local.Tick(.21f);Check(Last(Message.Wake).Message==Message.Wake,"Target boarding wakes the pending ship request");
        Commit(Plugin.Local,view.Data,Kinds.Vehicles,++token);Check(Result()==1 && view.Data.Owner==22,"Another onboard player can still receive guarded ship ownership");
        Reset();view=Object(Kinds.Vehicles);ship=view.gameObject.Add<Ship>();aboard=Object(Kinds.None,81).gameObject.Add<Player>();ship.Passengers.Add(aboard);
        Commit(Plugin.Local,view.Data,Kinds.Vehicles,++token);Check(Result()==2,"Occupied ship remains guarded before last passenger exits");
        ship.Passengers.Clear();Time.unscaledTime=.1f;Plugin.Local.Tick(.1f);
        Commit(Plugin.Local,view.Data,Kinds.Vehicles,++token);Check(Result()==1 && view.Data.Owner==22,"Empty ship returns to normal grid transfers");
        Reset();view=Object(Kinds.Vehicles);ship=view.gameObject.Add<Ship>();aboard=Object(Kinds.None,81).gameObject.Add<Player>();aboard.GetComponent<ZNetView>().Data.Owner=0;ship.Passengers.Add(aboard);
        Commit(Plugin.Local,view.Data,Kinds.Vehicles,++token);Check(Result()==1,"Native zero-owner passenger entries do not strand an empty ship");
    }
    private static void NativeDelayedActions()
    {
        Reset();var cartView=Object(Kinds.Vehicles);var cart=cartView.gameObject.Add<Vagon>();cart.Requester=new GameObject().Add<Humanoid>();
        Commit(Plugin.Local,cartView.Data,Kinds.Vehicles,++token);Check(Result()==2 && !cart.InUse() && cartView.Data.Owner==11,"Cart's native pending requester blocks before the attachment joint exists");
        cart.Requester=null;Plugin.Local.Tick(0);Time.unscaledTime=.1f;Plugin.Local.Tick(.1f);Check(Last(Message.Wake).Message==Message.Wake,"Native cart requester completion wakes the waiting handoff");
        Commit(Plugin.Local,cartView.Data,Kinds.Vehicles,++token);Check(Result()==1 && cartView.Data.Owner==22,"Idle cart still permits normal guarded ownership");
        Reset();var mobView=Object(Kinds.TamedAnimals);var actor=mobView.gameObject.Add<Humanoid>();actor.Tamed=true;actor.CurrentAttack=new Attack();var ai=mobView.gameObject.Add<MonsterAI>();ai.Init();
        Commit(Plugin.Local,mobView.Data,Kinds.TamedAnimals,++token);Check(Result()==2 && !actor.InAttack() && mobView.Data.Owner==11,"Live native Attack blocks even before animation reports InAttack");
        actor.CurrentAttack.Done=true;ai.Charging=true;Commit(Plugin.Local,mobView.Data,Kinds.TamedAnimals,++token);Check(Result()==2 && mobView.Data.Owner==11,"Native AI charging remains protected after the prior Attack finishes");
        Plugin.Local.Tick(0);ai.Charging=false;Time.unscaledTime=.1f;Plugin.Local.Tick(.1f);Check(Last(Message.Wake).Message==Message.Wake,"Charge completion wakes without permanent AI polling");
        Commit(Plugin.Local,mobView.Data,Kinds.TamedAnimals,++token);Check(Result()==1 && mobView.Data.Owner==22,"Finished non-charging actor retains normal source-confirmed transfers");
        Reset();var view=Object(Kinds.Fermenters);var fermenter=view.gameObject.Add<Fermenter>();fermenter.Invoking.Add("DelayedTap");long n=++token;
        Commit(Plugin.Local,view.Data,Kinds.Fermenters,n);Check(Result()==2 && view.Data.Owner==11,"Pending native fermenter output retains its source owner");
        Time.unscaledTime=5;Plugin.Local.Tick(5);Check(view.Data.Owner==11,"Elapsed handoff time cannot authorize loss of a queued local output");
        fermenter.Invoking.Clear();Time.unscaledTime=5.11f;Plugin.Local.Tick(5.11f);Check(Last(Message.Wake).Message==Message.Wake,"Native output completion wakes the blocked handoff");
        Commit(Plugin.Local,view.Data,Kinds.Fermenters,++token);Check(Result()==1 && view.Data.Owner==22,"Finished tapping permits the next source-confirmed transfer");
        Reset();view=Object(Kinds.Offerings);var altar=view.gameObject.Add<OfferingBowl>();altar.Invoking.Add("DelayedSpawnBoss");n=++token;
        Commit(Plugin.Local,view.Data,Kinds.Offerings,n);Check(Result()==2 && view.Data.Owner==11,"Queued native boss summon remains on its actual source");
        Time.unscaledTime=1;Plugin.Local.Tick(1);Check(view.Data.Owner==11,"Boss summon is not released by a guessed fixed delay");
        altar.Invoking.Clear();Time.unscaledTime=1.11f;Plugin.Local.Tick(1.11f);Check(Last(Message.Wake).Message==Message.Wake,"Finished native summon wakes without another interaction");
        Commit(Plugin.Local,view.Data,Kinds.Offerings,++token);Check(Result()==1 && view.Data.Owner==22,"Idle altar retains ordinary guarded transfers");
    }
    private static void ReplacedSnapshotJob()
    {
        Reset();var controller=Plugin.Controller;var first=Object(Kinds.Doors,1);var second=Object(Kinds.Doors,2);second.Data.Owner=33;
        Check(controller.Handoffs.Request(first.Data,22,Kinds.Doors,ZDOID.None),"Inline replacement: first job starts");
        Check(controller.Handoffs.Request(second.Data,22,Kinds.Doors,ZDOID.None),"Inline replacement: second job starts");
        bool replaced=false;Plugin.Network.OnSend=delegate(Network.Envelope sent)
        {
            if(replaced || sent.Message!=Message.Prepare)return;replaced=true;
            controller.Handoffs.Cancel(second.Data.m_uid,true);controller.DesiredOwner=34;
            Check(controller.Handoffs.Request(second.Data,34,Kinds.Doors,ZDOID.None),"Listen-host reply replaces a later snapshot job with a new target");
        };
        controller.Handoffs.Tick(0);Plugin.Network.OnSend=null;
        Check(replaced && controller.Handoffs.Pending(second.Data.m_uid) && second.Data.Owner==33,"Retired snapshot cannot cancel or commit the replacement job");
        Time.unscaledTime=.1f;controller.Handoffs.Tick(.1f);
        Check(second.Data.Owner==34 && second.Data.OwnerRevision==1,"Replacement still commits once to its new native target");
    }
    private static void BlockedActionScheduling()
    {
        Reset();var first=Object(Kinds.Smelters,1);var second=Object(Kinds.Smelters,2);var agent=Plugin.Local;
        agent.Refilling(first.Data,"RPC_AddOre".GetStableHashCode());Commit(agent,first.Data,Kinds.Smelters,++token);
        Time.unscaledTime=.4f;agent.Refilling(second.Data,"RPC_AddOre".GetStableHashCode());Commit(agent,second.Data,Kinds.Smelters,++token);agent.Tick(.4f);
        Plugin.Network.Sent.Clear();Time.unscaledTime=.9f;agent.Refilling(first.Data,"RPC_AddOre".GetStableHashCode());agent.Tick(.9f);
        Time.unscaledTime=1f;agent.Tick(1f);Check(Plugin.Network.Sent.Count==0,"Renewed refill cannot wake at the old deadline");
        Time.unscaledTime=1.4f;agent.Tick(1.4f);Check(Plugin.Network.Sent.Count==1 && Last(Message.Wake).Data.ReadLong()==token,"Earliest remaining station wakes independently of another renewed lease");
        Time.unscaledTime=1.91f;agent.Tick(1.91f);Check(Plugin.Network.Sent.Count==2 && first.Data.Owner==11 && second.Data.Owner==11,"Expired leases wake without committing ownership on the timer");

        Reset();var view=Object(Kinds.Containers);var box=view.gameObject.Add<Container>();box.Used=true;agent=Plugin.Local;
        Commit(agent,view.Data,Kinds.Containers,++token);Plugin.Network.Sent.Clear();agent.Tick(0);
        for(int i=1;i<=9;i++) { Time.unscaledTime=i*.1f;agent.Tick(Time.unscaledTime); }
        Check(Plugin.Network.Sent.Count==0 && view.Data.Owner==11,"Open inventory has no timer-triggered handoff or premature wake");
        box.Used=false;agent.Response(box);Check(Last(Message.Wake).Message==Message.Wake && view.Data.Owner==11,"Inventory release still wakes immediately through its event");

        Reset();view=Object(Kinds.TamedAnimals);var actor=view.gameObject.Add<Character>();actor.Tamed=true;actor.Attack=true;agent=Plugin.Local;
        Commit(agent,view.Data,Kinds.TamedAnimals,++token);Plugin.Network.Sent.Clear();agent.Tick(0);
        actor.Attack=false;Time.unscaledTime=.1f;agent.Tick(.1f);Check(Last(Message.Wake).Message==Message.Wake && view.Data.Owner==11,"Attack-end fallback retains its 0.1-second check without a new object update");
        agent.Reset();Check(!agent.HasWork,"World reset retires all scheduled blocked actions");
    }
    public static void Main()
    { try { InventoryProtection();TextEditingSourceVeto();RefillAndUse();IncineratorBurnProtection();StationaryReadinessGeometry();ReceiptAndRouter();MobAndUnload();OptionalClients();StandActionsAndStatus();RecipientFloorAndUnload();DelayedReadiness();TargetedQueueCost();DenseAndReentrantReceipts();ReplacedSnapshotJob();BlockedActionScheduling();NativeDelayedActions();ShipPassengerOwnership();Console.WriteLine("PASS: "+checks+" production handoff/runtime assertions: inventory opening/loading, text source veto/replay fence, renewing one-second refills, native incinerator burn protection, stationary recipient readiness, stand and delayed native actions, status fencing, final-state readiness, delayed replies, unload/relay, per-object queue cost, dense unclicked transfers, listen-host replacement, deadline/event wakeups, optional-client fallback and cache expiry."); } catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; } }
}
