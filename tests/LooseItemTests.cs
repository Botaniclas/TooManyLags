using System;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static void LooseItems()
    {
        try
        {
            LooseInitialOwnership();
            LooseLoadingGrid();
            Fresh(Kinds.LooseItems, defaultInteractives:true, nativeSolo:true, checkIntervalSeconds:.05f);
            var first=Peer(1); AuthorityController.LooseItemClient=delegate {return true;};
            var item=Object(typeof(ItemDrop),1,1,99); var view=ZNetScene.instance.FindInstance(item); var drop=view.GetComponent<ItemDrop>();
            // Native ItemDrop.Awake disables its optional placement WearNTear.
            view.GetComponent<WearNTear>().isActiveAndEnabled=false;view.GetComponent<WearNTear>().enabled=false;
            Tick(0);
            Check("loose items have their own category",ObjectCatalog.Classify(view.gameObject)==Kinds.LooseItems);
            Check("loose items retain native owner requests",controller.RequestNode(item.m_uid,"RPC_RequestOwn".GetStableHashCode())!=null);
            Check("loose items never use stationary early-loading bypass",!ObjectCatalog.Stationary(view.gameObject,Kinds.LooseItems));
            Tick(2.1f);Check("existing single-player field owns loose item",item.GetOwner()==1);
            var second=Peer(2);Tick(2.2f);Tick(4.4f);Check("existing overlapping field owns loose item on server",item.GetOwner()==99);
            MovePeer(second,80);Tick(5);Tick(7.1f);Check("loose items use normal overlap departure",item.GetOwner()==1);
            controller.PickupBlocked(item,1,item.OwnerRevision);int attempts=controller.ScheduledAttempts;
            for(int i=0;i<50;i++)Tick(8+i*.1f);
            Check("pickup refusal has no background retry",controller.ScheduledAttempts==attempts);
            MovePeer(first,8);Tick(14);Tick(16.1f);Check("fresh covering movement resumes loose item management",!controller.RequestNode(item.m_uid,"RPC_RequestOwn".GetStableHashCode()).WaitingForRequest);
            item.Position=new Vector3(40,0,0);view.transform.position=item.Position;controller.Position(item);Tick(19);
            Check("moving loose item retains normal KeepOwnershipOnCellExit semantics",item.GetOwner()==1);
            MovePeer(second,40);Tick(20);Tick(22.1f);Check("item cell index follows movement into another player's field",item.GetOwner()==2);

            Fresh(Kinds.LooseItems,defaultInteractives:true,nativeSolo:true);Peer(1);AuthorityController.LooseItemClient=delegate {return false;};
            var legacy=Object(typeof(ItemDrop),1,1,1);Tick(0);Tick(2.1f);Peer(2);Tick(3);Tick(5.1f);
            Check("legacy pickup client retains native item ownership",legacy.GetOwner()==1);
            Fresh(Kinds.Doors,defaultInteractives:true,nativeSolo:true);Peer(1);var disabled=Object(typeof(ItemDrop),1,1,99);Tick(0);Tick(2.1f);
            Check("disabled loose category has no indexed owner request",disabled.GetOwner()==99&&controller.RequestNode(disabled.m_uid,"RPC_RequestOwn".GetStableHashCode())==null);

            Player.m_localPlayer=null;drop.Invoking.Add("PickupUpdate");Check("native manual pickup protects without a timer",LooseItemGuard.Busy(drop));
            drop.Invoking.Clear();drop.Invoking.Add("EatUpdate");Check("native eating protects without a timer",LooseItemGuard.Busy(drop));drop.Invoking.Clear();
            var actor=new GameObject().Add(new Player());Player.m_localPlayer=actor;Player.m_enableAutoPickup=true;
            drop.transform.position=new Vector3(0,1,0);Check("native automatic collection protects during source handoff",LooseItemGuard.Busy(drop));
            actor.Teleporting=true;Check("teleporting actor does not pin item",!LooseItemGuard.Busy(drop));actor.Teleporting=false;
            Player.m_enableAutoPickup=false;Check("disabled automatic pickup does not pin nearby items",!LooseItemGuard.Busy(drop));Player.m_enableAutoPickup=true;
            drop.m_autoPickup=false;Check("freshly player-dropped item retains native pickup delay",!LooseItemGuard.Busy(drop));drop.m_autoPickup=true;
            drop.Placed=true;Check("placed item does not enter automatic collection guard",!LooseItemGuard.Busy(drop));drop.Placed=false;
            drop.transform.position=new Vector3(50,1,0);Check("distant source player does not block item transfer",!LooseItemGuard.Busy(drop));
        }
        finally {AuthorityController.LooseItemClient=null;Player.m_localPlayer=null;Player.m_enableAutoPickup=true;}
    }
    private static ZDO LooseInitialTemplate(string name,float x=40,long owner=0)
    {
        var data=Object(typeof(ItemDrop),x,1,owner);var view=ZNetScene.instance.FindInstance(data);
        controller.Unregister(view);view.gameObject.name=name;view.gameObject.Add(new Rigidbody());view.gameObject.Add(new ZSyncTransform());
        ZNetScene.instance.Views.Remove(data);return data;
    }
    private static void LooseLoadingGrid()
    {
        var priorInitial=AuthorityController.InitialClient;
        var priorLoose=AuthorityController.LooseItemClient;
        try
        {
            AuthorityController.InitialClient=delegate{return true;};
            AuthorityController.LooseItemClient=delegate{return true;};
            foreach(bool retain in new[]{false,true}) foreach(string prefab in new[]{"Wood","ChickenEgg","AsksvinEgg"})
            {
                Fresh(Kinds.LooseItems,defaultInteractives:true,keepOnExit:retain,cellSize:4,
                    pattern:GridCoveragePattern.Rounded7x7,directional:true,checkIntervalSeconds:.05f);
                var player=Peer(1);Tick(0);
                var data=LooseInitialTemplate(prefab,1,99);
                var root=ZNetScene.instance.Prefabs[data.Hash];var view=root.GetComponent<ZNetView>();
                ZNetScene.instance.Ready=delegate{return false;};
                controller.OwnerChanged(data);
                Check("server-owned loose item has a grid node before server construction: "+prefab,controller.InitialObject(data));
                var node=controller.RequestNode(data.m_uid,"RPC_RequestOwn".GetStableHashCode());
                int queries=ZNetScene.instance.AreaQueries;
                Tick(.051f);
                Check("red grid claims loose item without waiting for server construction",data.GetOwner()==1&&data.Writes==1);
                Check("early loose item claim adds no loading query or source round trip",ZNetScene.instance.AreaQueries==queries&&player.m_rpc.Calls==0);
                Check("early loose item stays outside stationary/root commit rules",!ObjectCatalog.Stationary(root,Kinds.LooseItems)&&!ObjectCatalog.InitialRoot(root,Kinds.LooseItems)&&controller.CommitKind(data)==Kinds.None);
                int attempts=controller.ScheduledAttempts;
                for(int i=1;i<=50;i++)Tick(.051f+i*.051f);
                Check("settled pre-instance loose item has no repeated ownership work",controller.ScheduledAttempts==attempts&&data.Writes==1);
                ZNetScene.instance.Views.Add(data,view);data.Created=true;controller.Register(data,view);Tick(3);
                Check("late loose item attachment preserves the original grid node",!controller.InitialObject(data)&&ReferenceEquals(node,controller.RequestNode(data.m_uid,"RPC_RequestOwn".GetStableHashCode()))&&data.GetOwner()==1&&data.Writes==1);
            }

            Fresh(Kinds.LooseItems,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
            var first=Peer(1);var second=Peer(2);Tick(0);
            var shared=LooseInitialTemplate("Wood",1,99);controller.OwnerChanged(shared);Tick(.1f);
            Check("early loose item preserves server overlap",shared.GetOwner()==99&&shared.Writes==0);
            MovePeer(second,64);Tick(.2f);
            Check("overlap departure can claim loose item before server construction",shared.GetOwner()==1);

            Fresh(Kinds.LooseItems,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
            first=Peer(1);Tick(0);var legacy=LooseInitialTemplate("Wood",1,99);controller.OwnerChanged(legacy);
            AuthorityController.LooseItemClient=delegate{return false;};Tick(.1f);
            Check("early loose item keeps legacy pickup clients excluded",legacy.GetOwner()==99);
            AuthorityController.LooseItemClient=delegate{return true;};controller.CapabilityChanged(1);Tick(.2f);
            Check("pickup capability wake resumes the early loose item claim",legacy.GetOwner()==1);
            MovePeer(first,1024);Tick(.3f);
            Check("leaving native active coverage removes the pre-instance loose item node",!controller.InitialObject(legacy)&&controller.RequestNode(legacy.m_uid,"RPC_RequestOwn".GetStableHashCode())==null);

            Fresh(Kinds.LooseItems,defaultInteractives:true);Peer(1);Tick(0);
            var hybrid=LooseInitialTemplate("Wood",1,99);ZNetScene.instance.Prefabs[hybrid.Hash].Add(new Container());controller.OwnerChanged(hybrid);Tick(.1f);
            Check("modded item inventory hybrid retains its live inventory path",!controller.InitialObject(hybrid)&&hybrid.GetOwner()==99);

            Fresh(Kinds.LooseItems,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
            Peer(1);Tick(0);var queued=LooseInitialTemplate("Wood",1,99);controller.OwnerChanged(queued);
            Check("native pickup request waits only while its current server handler is absent",controller.Router.TryHandle(ZRoutedRpc.instance,Request(queued,"RPC_RequestOwn",99),false)&&ZRoutedRpc.instance.Remote.Count==0);
            Tick(.1f);ZDOMan.instance.MarkSent(1,queued);controller.Router.Flush();
            Check("queued native pickup follows the early grid owner without waiting for a server instance",queued.GetOwner()==1&&ZRoutedRpc.instance.Remote.Count==1&&ZRoutedRpc.instance.Remote[0].m_targetPeerID==1);
        }
        finally {AuthorityController.InitialClient=priorInitial;AuthorityController.LooseItemClient=priorLoose;}
    }
    private static void LooseInitialOwnership()
    {
        var patcher=new Harmony("botanic.authority.test.loose-initial");
        patcher.Patch(AccessTools.Method(typeof(ZDOMan),"ReleaseNearbyZDOS"),transpiler:new HarmonyMethod(AccessTools.Method(typeof(OwnershipPatch),"Transpiler")));
        try
        {
            foreach(string prefab in new[]{"Wood","ChickenEgg","AsksvinEgg"})
            {
                Fresh(Kinds.LooseItems,defaultInteractives:true);var player=Peer(1);
                var data=LooseInitialTemplate(prefab);var root=ZNetScene.instance.Prefabs[data.Hash];
                ZNetScene.instance.Ready=delegate{return false;};Tick(0);int queries=ZNetScene.instance.AreaQueries;
                ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);
                Check("native first loose-item assignment selects server: "+prefab,data.GetOwner()==99&&data.Writes==1);
                Check("initial item assignment adds no loading query or source RPC",ZNetScene.instance.AreaQueries==queries&&player.m_rpc.Calls==0);
                Check("egg/drop keeps non-stationary physics path",ObjectCatalog.Classify(root)==Kinds.LooseItems&&!ObjectCatalog.Stationary(root,Kinds.LooseItems)&&!ObjectCatalog.InitialRoot(root,Kinds.LooseItems));
                controller.OwnerChanged(data);
                Check("dynamic items do not create stationary pre-instance jobs",!controller.InitialObject(data)&&controller.RequestNode(data.m_uid,"RPC_RequestOwn".GetStableHashCode())==null);
                int scans=GameObject.Scans,lookups=GameObject.Lookups;
                for(int i=0;i<50;i++)Check("native default reuses prefab cache",controller.NativeOwnerTarget(data,1)==99);
                Check("cached item default adds no component scans",GameObject.Scans==scans&&GameObject.Lookups==lookups);
                for(int i=1;i<6;i++){Tick(i);ZDOMan.instance.ReleaseNearbyZDOS(new Vector3(512,0,512),99);ZDOMan.instance.ReleaseNearbyZDOS(player.Position,1);}
                Check("native default retains owner without repeated writes or initial jobs",data.GetOwner()==99&&data.Writes==1&&!controller.InitialObject(data));
                ZNetScene.instance.Ready=delegate{return true;};Tick(6);Check("readiness does not cause another initial owner write",data.GetOwner()==99&&data.Writes==1);
                ZNet.instance.Peers.Clear();Tick(7);ZDOMan.instance.ReleaseNearbyZDOS(new Vector3(512,0,512),99);
                Check("native release still clears unloaded loose items",data.GetOwner()==0);
            }
            Fresh(Kinds.LooseItems,defaultInteractives:true);var first=Peer(1);var second=Peer(2,64);Tick(0);
            var pending=LooseInitialTemplate("Wood",1);ZDOMan.instance.ReleaseNearbyZDOS(second.Position,2);
            Cell cell;Cell.At(1,1,out cell);Check("pending item field receives default without advancing timer",pending.GetOwner()==99&&Index().Grid.Owner(cell)==0);
            Tick(2);var settled=LooseInitialTemplate("ChickenEgg",1);ZNetScene.instance.Ready=delegate{return false;};
            ZDOMan.instance.ReleaseNearbyZDOS(second.Position,2);Check("settled item grid overrides native scanning player",settled.GetOwner()==1&&settled.Writes==1);
            Fresh(Kinds.LooseItems,defaultInteractives:true);first=Peer(1);Peer(2);Tick(0);Tick(2);
            var shared=LooseInitialTemplate("Wood",1);ZDOMan.instance.ReleaseNearbyZDOS(first.Position,1);Check("native item overlap selects server",shared.GetOwner()==99);
            Fresh(Kinds.LooseItems,defaultInteractives:true);first=Peer(1);var old=Peer(7,64);var active=LooseInitialTemplate("Wood",40,7);Tick(0);
            ZDOMan.instance.ReleaseNearbyZDOS(first.Position,1);Check("native active item owner is not stolen",active.GetOwner()==7&&active.Writes==0);
            MovePeer(old,512);Tick(1);ZDOMan.instance.ReleaseNearbyZDOS(first.Position,1);Check("departed item owner permits native server assignment",active.GetOwner()==99&&active.Writes==1);
            foreach(int mode in new[]{0,1,2})
            {
                Fresh(mode==1?Kinds.Doors:Kinds.LooseItems,defaultInteractives:mode==1,defaultMobs:mode==2);first=Peer(1);
                var item=LooseInitialTemplate("ChickenEgg");Tick(0);ZDOMan.instance.ReleaseNearbyZDOS(first.Position,1);
                Check("disabled item/default or mob-only default retains vanilla: "+mode,item.GetOwner()==1);
            }
            Fresh(Kinds.LooseItems,defaultInteractives:true);first=Peer(1);var distant=LooseInitialTemplate("Wood");
            ZNetScene.instance.Prefabs[distant.Hash].GetComponent<ZNetView>().m_distant=true;
            var temporary=LooseInitialTemplate("Wood");ZNetScene.instance.Prefabs[temporary.Hash].GetComponent<ZNetView>().m_persistent=false;
            var baked=LooseInitialTemplate("Wood");baked.Parked=true;Tick(0);ZDOMan.instance.ReleaseNearbyZDOS(first.Position,1);
            Check("distant/nonpersistent item templates and baked roots stay native",distant.GetOwner()==1&&temporary.GetOwner()==1&&baked.GetOwner()==1);
        }
        finally {patcher.UnpatchSelf();}
    }
#if GRID_PROTOCOL
    private static void LooseItemProtocol()
    {
        Fresh(Kinds.LooseItems,defaultInteractives:true);Peer(1);
        var host=new ZNetPeer {Uid=99,m_server=true};ZNet.instance.Peers.Add(host);
        var data=Object(typeof(ItemDrop),1,1,1);var drop=ZNetScene.instance.FindInstance(data).GetComponent<ItemDrop>();
        bool dedicated=ZNet.instance.Dedicated;long session=ZDOMan.SessionID;var oldHost=ZNet.instance.ServerPeer;
        GridStart();
        try
        {
            ZNet.instance.Dedicated=false;ZDOMan.SessionID=1;ZNet.instance.ServerPeer=host;
            GridReceive(host,GridPacket(1,delegate(ZPackage p){p.Write(true);p.Write(false);p.Write(false);p.Write(true);}));
            Check("new protocol advertises item protection",TooManyLags.GridHandoffs.Protocol.SupportsLooseItems(host));
            int commits=0;typeof(TooManyLags.GridHandoffs.Protocol).GetField("nativeCommit",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).SetValue(null,(Action<ZRpc,ZDOID,long>)delegate{commits++;});
            Action<long> commit=delegate(long token){GridReceive(host,GridPacket(2,delegate(ZPackage p){p.Write(token);p.Write(data.m_uid);p.Write(2L);p.Write((uint)data.OwnerRevision);}));};
            drop.Invoking.Add("PickupUpdate");commit(1);Check("normal simulation source pickup veto",commits==0);
            drop.Invoking.Clear();commit(1);Check("same rejected transaction never replays",commits==0);
            commit(2);Check("fresh ordinary item transaction works",commits==1);
            drop.Invoking.Add("PickupUpdate");drop.RequestOwn(7);Check("native explicit pickup ownership is not intercepted",data.GetOwner()==7);
        }
        finally {ZNet.instance.Dedicated=dedicated;ZDOMan.SessionID=session;ZNet.instance.ServerPeer=oldHost;TooManyLags.GridHandoffs.Protocol.Stop();}
    }
#endif
}
