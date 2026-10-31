using System;
using System.Collections.Generic;
using TooManyLags.PlayerSimulation;
using UnityEngine;
internal static class HandoffRuntimeTests
{
    private static int checks; private static long token;
    private static void Check(bool value,string reason) { checks++; if(!value)throw new Exception(reason); }
    private static void Reset()
    { Time.unscaledTime=0;Time.frameCount++;ZDOMan.instance.Objects.Clear();ZDOMan.Session=11;ZNet.instance.Server=true;ZNet.IsSinglePlayer=false;ZNetScene.instance.Views.Clear();ZNetScene.instance.Ready=true;InventoryGui.instance.Current=null;Plugin.Network.Sent.Clear();Plugin.Network.AdditionalCapable.Clear();Plugin.Controller=new AuthorityController();Plugin.Local=new LocalAgent();RequestRouter.Reset();ZRoutedRpc.instance.Delivered.Clear();ZRoutedRpc.instance.Responses.Clear(); }
    private static ZNetView Object(Kinds kind,long id=1)
    { var root=new GameObject { Kind=kind };var view=root.Add<ZNetView>();view.Data=new ZDO { m_uid=new ZDOID(id) };ZDOMan.instance.Objects.Add(view.Data.m_uid,view.Data);ZNetScene.instance.Views.Add(view.Data.m_uid,view);return view; }
    private static ZPackage Ticket(ZDO data,Kinds kind,long n,ZDOID mob=default(ZDOID))
    { var p=new ZPackage();p.Write(n);p.Write(data.m_uid);p.Write(11L);p.Write(22L);p.Write((ushort)0);p.Write((int)kind);p.Write(mob);p.SetPos(0);return p; }
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
        Reset();view=Object(Kinds.Containers);n=++token;var cancel=new ZPackage();cancel.Write(n);cancel.Write(view.Data.m_uid);cancel.SetPos(0);Plugin.Local.Receive(Message.Cancel,cancel);Commit(Plugin.Local,view.Data,Kinds.Containers,n);Check(Result()==0 && view.Data.Owner==11,"Canceled in-flight commit must not transfer");
    }
    private static void RefillAndUse()
    {
        Reset();var view=Object(Kinds.Smelters);Plugin.Local.Refilling(view.Data,"RPC_AddOre".GetStableHashCode());long n=++token;
        Commit(Plugin.Local,view.Data,Kinds.Smelters,n);Check(Result()==2,"Refill lease starts immediately");
        Time.unscaledTime=.75f;Plugin.Local.Refilling(view.Data,"RPC_AddOre".GetStableHashCode());
        Time.unscaledTime=1.74f;Commit(Plugin.Local,view.Data,Kinds.Smelters,n);Check(Result()==2,"Repeated refill renews protection for one second");
        Time.unscaledTime=1.75f;Commit(Plugin.Local,view.Data,Kinds.Smelters,n);Check(Result()==1,"Expired refill protection releases promptly");
        Reset();view=Object(Kinds.TamedAnimals);var animal=view.gameObject.Add<Tameable>();var actor=view.gameObject.Add<Character>();var tameAi=view.gameObject.Add<MonsterAI>();tameAi.Init();actor.Tamed=true;n=++token;
        animal.Rider=true;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==2,"Rider retains ownership");
        animal.Rider=false;animal.m_commandable=true;view.Data.Set(ZDOVars.s_follow,"player");Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==2,"Following retains ownership");
        view.Data.Set(ZDOVars.s_follow,"");animal.Summon=true;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==3,"Magic summon must stay vanilla");
        animal.Summon=false;actor.Attack=true;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==2,"Tamed attacks cannot be interrupted by grid transfer");
        actor.Attack=false;tameAi.Target=Object(Kinds.None,2).gameObject.Add<Character>();Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==2,"Fighting tamed animal retains its current AI owner");
        tameAi.Target=null;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(Result()==1,"Idle ordinary tamed animal may transfer");
    }
    private static void ReceiptAndRouter()
    {
        Reset();var view=Object(Kinds.Doors);var source=Plugin.Local;var receiver=new LocalAgent();var controller=Plugin.Controller;
        controller.Handoffs.Request(view.Data,22,Kinds.Doors,ZDOID.None);controller.Handoffs.Tick(0);
        var prepare=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(prepare.Message==Message.Prepare,"Prepare receiver first");
        ZDOMan.Session=22;receiver.Receive(prepare.Message,prepare.Data);var prepared=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];controller.Handoffs.Receive(22,prepared.Message,prepared.Data);
        ZDOMan.Session=11;Time.unscaledTime=.1f;controller.Handoffs.Tick(.1f);var commit=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(commit.Message==Message.Commit,"Then request source commit");
        source.Receive(commit.Message,commit.Data);Check(Result()==1 && view.Data.Owner==22,"Source commits through native SetOwner");
        controller.Handoffs.Observe(view.Data);Check(controller.Handoffs.Committing(view.Data.m_uid),"Coordinator receipt alone must not unlock gameplay");
        var call=new ZRoutedRpc.RoutedRPCData { m_msgID=71,m_senderPeerID=33,m_targetPeerID=11,m_targetZDO=view.Data.m_uid,m_methodHash="UseDoor".GetStableHashCode() };call.m_parameters.Write(123);call.m_parameters.SetPos(0);
        Check(!RequestRouter.Handle(ZRoutedRpc.instance,call,false),"Hold owner RPC until actual receiver activation");call.m_parameters.SetPos(0);call.m_parameters.Write(999);RequestRouter.Flush(.2f);Check(ZRoutedRpc.instance.Delivered.Count==0,"No early delivery");
        ZDOMan.Session=22;Plugin.Local=receiver;receiver.Observe(view.Data);var activated=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(activated.Message==Message.Activated,"Receiver confirms imported owner revision");controller.Handoffs.Receive(22,activated.Message,activated.Data);
        Check(!controller.Handoffs.Committing(view.Data.m_uid),"Matching activation releases gate");ZDOMan.Session=999;RequestRouter.Flush(.3f);
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
        Reset();view=Object(Kinds.Doors);var receiver=new LocalAgent();ZDOMan.Session=22;ZNetScene.instance.Ready=false;ZNetScene.instance.Views.Clear();receiver.Receive(Message.Prepare,Ticket(view.Data,Kinds.Doors,++token));var msg=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];msg.Data.ReadLong();msg.Data.ReadZDOID();Check(msg.Data.ReadBool(),"Matching state can prepare ownership without a local instance or broad ready area");
        view.Data.SetOwner(22);Plugin.Network.Sent.Clear();receiver.Observe(view.Data);
        Check(view.Data.Owner==22&&Plugin.Network.Sent.Count==0,"Ownership is assigned without prematurely activating an absent RPC receiver");
        ZNetScene.instance.Views.Add(view.Data.m_uid,view);receiver.Instance(view);
        Check(Plugin.Network.Sent.Count==1&&Plugin.Network.Sent[0].Message==Message.Activated,"Native instance creation releases routed actions despite broad area unreadiness");
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
        var prepare=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(prepare.Message==Message.Prepare && prepare.Uid==22,"Compatible receiver confirms the expected source state");
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
        Plugin.Local.Receive(commit.Message,commit.Data);Check(Result()==1 && view.Data.Owner==33,"Capable source still runs its actual guard before a simple native-receiver commit");controller.Handoffs.Observe(view.Data);
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
    private static long StartCoordinator(ZNetView view,Kinds kind,ZDOID mob=default(ZDOID))
    {
        var controller=Plugin.Controller;Check(controller.Handoffs.Request(view.Data,22,kind,mob),"Coordinator starts a guarded request");controller.Handoffs.Tick(0);
        var prepare=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];long value=prepare.Data.ReadLong();prepare.Data.SetPos(0);
        var receiver=new LocalAgent();ZDOMan.Session=22;receiver.Receive(prepare.Message,prepare.Data);
        var ready=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];controller.Handoffs.Receive(22,ready.Message,ready.Data);
        ZDOMan.Session=11;Time.unscaledTime=.1f;controller.Handoffs.Tick(.1f);
        Check(controller.Handoffs.Committing(view.Data.m_uid),"Coordinator source command is in flight: "+kind);return value;
    }
    private static void CoordinatorResult(ZNetView view,long value,int result)
    {
        var packet=new ZPackage();packet.Write(value);packet.Write(view.Data.m_uid);packet.Write(result);packet.SetPos(0);
        Plugin.Controller.Handoffs.Receive(11,Message.Result,packet);
    }
    private static void DiscardedGridRefusals()
    {
        foreach(int result in new[]{0,2,3,4})
        {
            Reset();var view=Object(Kinds.Containers);long value=StartCoordinator(view,Kinds.Containers);CoordinatorResult(view,value,result);
            var controller=Plugin.Controller;
            Check(!controller.Handoffs.Pending(view.Data.m_uid)&&controller.Rejections==1&&controller.Wakes==0&&view.Data.Owner==11,"Processed grid refusal discards without a wake or owner write");
            int messages=Plugin.Network.Sent.Count;
            Time.unscaledTime=20;controller.Handoffs.Tick(20);
            Check(Plugin.Network.Sent.Count==messages&&!controller.Handoffs.Pending(view.Data.m_uid),"No timed replay survives a discarded grid refusal");
            var wake=new ZPackage();wake.Write(value);wake.Write(view.Data.m_uid);wake.SetPos(0);controller.Handoffs.Receive(11,Message.Wake,wake);
            CoordinatorResult(view,value,result);Check(controller.Rejections==1&&controller.Wakes==0,"Late use-end and duplicate refusal cannot revive an old command");
            Check(controller.Handoffs.Request(view.Data,22,Kinds.Containers,ZDOID.None),"Fresh coverage can request unchanged source owner/revision");controller.Handoffs.Tick(20);
            var next=Plugin.Network.Sent[Plugin.Network.Sent.Count-1];Check(next.Message==Message.Prepare&&next.Data.ReadLong()>value,"Fresh grid attempt uses a new token without a veto cooldown");
        }
        Reset();var actor=Object(Kinds.None);actor.gameObject.Add<Character>();var ai=actor.gameObject.Add<MonsterAI>();ai.Init();
        var target=Object(Kinds.None,2).gameObject.Add<Player>();ai.Target=target;
        long attack=StartCoordinator(actor,Kinds.None,target.GetZDOID());CoordinatorResult(actor,attack,2);
        Check(Plugin.Controller.Handoffs.Pending(actor.Data.m_uid)&&Plugin.Controller.Rejections==0,"Hostile Mob attack waiting remains separate from grid refusal policy");
        foreach(bool complete in new[]{false,true})
        {
            Reset();var view=Object(Kinds.Containers);long value=StartCoordinator(view,Kinds.Containers);view.Data.Owner=22;if(complete)view.Data.OwnerRevision=1;
            CoordinatorResult(view,value,2);
            Check(Plugin.Controller.Rejections==0&&Plugin.Controller.Handoffs.Committing(view.Data.m_uid),"Late refusal cannot discard a real native receipt or half import");
        }
    }
    public static void Main()
    { try { CombatRouterSelection();VisualCommitOrdering();DiscardedGridRefusals();LooseItemSource();TextEditingSource();InventoryProtection();RefillAndUse();ReceiptAndRouter();MobAndUnload();OptionalClients();Console.WriteLine("PASS: "+checks+" production handoff/runtime assertions: inventory opening race, refill leases, active use, text editing, cancel/replay, receiver activation, optional-client native fallback, routed payloads and cache expiry."); } catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; } }
    private static void CombatRouterSelection()
    {
        Reset();var target=Object(Kinds.None,2).gameObject.Add<Player>();target.Side=Character.Faction.Players;
        var view=Object(Kinds.None);var ai=view.gameObject.Add<MonsterAI>();ai.Init();view.gameObject.Add<Character>();ai.Target=target;
        Commit(Plugin.Local,view.Data,Kinds.None,++token,target.GetZDOID());Check(Result()==1&&view.Data.Owner==22,"mob source transition establishes real legacy forward receipt");
        ZNet.instance.Server=false;Plugin.Network.Sent.Clear();
        var hit=new ZRoutedRpc.RoutedRPCData{m_msgID=123,m_senderPeerID=33,m_targetPeerID=11,m_targetZDO=view.Data.m_uid,m_methodHash="RPC_Damage".GetStableHashCode()};
        Check(!RequestRouter.Handle(ZRoutedRpc.instance,hit,true)&&Plugin.Network.Sent.Count==1&&Plugin.Network.Sent[0].Message==Message.Forward,"legacy endpoint retains its native receipt relay");
        int probes=0;LocalAgent.CombatForward=delegate(ZDO value){probes++;return ReferenceEquals(value,view.Data);};
        try
        {
            Plugin.Network.Sent.Clear();
            Check(RequestRouter.Handle(ZRoutedRpc.instance,hit,true)&&Plugin.Network.Sent.Count==0&&probes==1,"updated combat route reaches Character handler without competing one-receipt relay");
            LocalAgent.CombatForward=delegate(ZDO value){return false;};
            Check(!RequestRouter.Handle(ZRoutedRpc.instance,hit,true)&&Plugin.Network.Sent.Count==1,"missing or expired new route retains the legacy fallback");
        }
        finally {LocalAgent.CombatForward=null;}
    }
    private static void VisualCommitOrdering()
    {
        Reset();var view=Object(Kinds.TamedAnimals);var tame=view.gameObject.Add<Tameable>();var actor=view.gameObject.Add<Character>();var ai=view.gameObject.Add<MonsterAI>();ai.Init();actor.Tamed=true;
        int captured=0,completed=0;object marker=new object();
        LocalAgent.VisualCapture=delegate(ZDO data,long target){Check(data.Owner==11&&target==22,"capture precedes the sole native ownership write");captured++;return marker;};
        LocalAgent.VisualCommitted=delegate(object value){Check(ReferenceEquals(marker,value)&&view.Data.Owner==22,"presentation starts only after real successful source commit");completed++;};
        try
        {
            tame.Rider=true;long n=++token;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(captured==0&&completed==0,"protected handoff creates no visual bridge");
            tame.Rider=false;Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(captured==1&&completed==1,"idle tamed handoff invokes presentation once");
            Commit(Plugin.Local,view.Data,Kinds.TamedAnimals,n);Check(captured==1&&completed==1,"duplicate transaction cannot restart presentation");
            view=Object(Kinds.Doors,9);Commit(Plugin.Local,view.Data,Kinds.Doors,++token);Check(captured==1&&completed==1,"non-mob handoffs do no presentation work");
        }
        finally {LocalAgent.VisualCapture=null;LocalAgent.VisualCommitted=null;}
    }
    private static void LooseItemSource()
    {
        Reset();var view=Object(Kinds.LooseItems);var drop=view.gameObject.Add<ItemDrop>();drop.Invoking.Add("PickupUpdate");
        Commit(Plugin.Local,view.Data,Kinds.LooseItems,++token);Check(Result()==4&&view.Data.Owner==11,"Native item pickup veto discards automatic transfer");
        drop.Invoking.Clear();Time.unscaledTime=100;Plugin.Local.Tick(100);Check(view.Data.Owner==11,"Pickup completion does not replay rejected transfer");
        Commit(Plugin.Local,view.Data,Kinds.LooseItems,++token);Check(Result()==1&&view.Data.Owner==22,"Fresh idle loose item transaction uses ordinary handoff");
    }
    private static void TextEditingSource()
    {
        Reset(); var view=Object(Kinds.Signs); var data=view.Data;
        try
        {
            LocalAgent.TextEditBusy=delegate(ZDO value) { return ReferenceEquals(value,data); };
            Commit(Plugin.Local,data,Kinds.Signs,++token);
            Check(Result()==0 && data.Owner==11,"active text editing rejects source commit without a retry lease");
            LocalAgent.TextEditBusy=null;Time.unscaledTime=100;Plugin.Local.Tick(100);
            Check(data.Owner==11,"ending text editing never replays discarded commit");
            Commit(Plugin.Local,data,Kinds.Signs,++token);
            Check(Result()==1 && data.Owner==22,"a fresh source transaction works after text editing ends");
        }
        finally { LocalAgent.TextEditBusy=null; }
    }
}
