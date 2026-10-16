using System;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static void LooseItems()
    {
        try
        {
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
