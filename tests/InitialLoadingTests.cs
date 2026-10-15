using System;
using System.Collections.Generic;
using System.Reflection;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static ZDO UncreatedInteraction(Type type)
    {
        var root=new GameObject {name=type.Name+"_initial",activeInHierarchy=false};
        root.Add(new ZNetView());root.Add((Component)Activator.CreateInstance(type));
        if(type==typeof(Smelter)||type==typeof(StationExtension)||type==typeof(MapTable)||type==typeof(WispSpawner))root.Add(new Switch());
        if(type==typeof(Incinerator))root.GetComponent<Incinerator>().m_container=root.Add(new Container());
        var data=new ZDO {m_uid=new ZDOID {ID=next++},Hash=100+next,Position=new Vector3(1,0,1)};
        ZDOMan.instance.Objects.Add(data);ZNetScene.instance.Prefabs.Add(data.Hash,root);data.SetOwner(99);data.Writes=0;controller.OwnerChanged(data);return data;
    }
    private static ZNetView InteractionView(ZDO data)
    {
        var template=ZNetScene.instance.GetPrefab(data.GetPrefab());var root=new GameObject {name=template.name};root.transform.position=data.GetPosition();
        var view=root.Add(new ZNetView {Data=data});
        foreach(Component component in template.Components)if(!(component is ZNetView))root.Add((Component)Activator.CreateInstance(component.GetType()));
        var incinerator=root.GetComponent<Incinerator>();if(incinerator)incinerator.m_container=root.GetComponent<Container>();return view;
    }
    private static ZPackage InteractionProof(ZNetPeer server,ZDO data,ZPackage query,long recipient=1)
    {
        var view=InteractionView(data);ZNetScene.instance.Views.Add(data,view);
        long session=ZDOMan.SessionID;bool dedicated=ZNet.instance.Dedicated;var oldServer=ZNet.instance.ServerPeer;
        var oldArea=ZNetScene.instance.Ready;var replies=GridCapture(server);
        try
        {
            ZDOMan.SessionID=recipient;ZNet.instance.Dedicated=false;ZNet.instance.ServerPeer=server;
            ZNetScene.instance.Ready=delegate{return true;};Time.frameCount++;
            GridReceive(server,query);TooManyLags.GridHandoffs.Protocol.FlushDeferred();
            Check("initial object reply uses actual recipient view",replies.Count==1);return replies[0];
        }
        finally{ZNetScene.instance.Views.Remove(data);ZDOMan.SessionID=session;ZNet.instance.Dedicated=dedicated;ZNet.instance.ServerPeer=oldServer;ZNetScene.instance.Ready=oldArea;}
    }
    private static void GeneralInitialLoading()
    {
        int start=checks;
        foreach(Type type in new[] {typeof(Door),typeof(Chair),typeof(Barber),typeof(Bed),typeof(Container),typeof(CraftingStation),typeof(StationExtension),typeof(Smelter),typeof(CookingStation),typeof(Fireplace),typeof(Beehive),typeof(SapCollector),typeof(PrivateArea),typeof(MapTable),typeof(WispSpawner),typeof(Sign),typeof(TeleportWorld),typeof(Fermenter),typeof(ItemStand),typeof(ArmorStand),typeof(Incinerator),typeof(Trap),typeof(Turret),typeof(ShieldGenerator),typeof(Pickable),typeof(PickableItem),typeof(Feast),typeof(OfferingBowl),typeof(CustomInteractable)})
        {
            Fresh((Kinds)((1<<26)-1),defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
            var peer=Peer(1);var server=new ZNetPeer {Uid=99,m_server=true};ZNet.instance.Peers.Add(server);GridStart();
            try
            {
                GridReceive(peer,GridPacket(0,delegate(ZPackage p){p.Write(7);}));Tick(0);var sent=GridCapture(peer);
                var data=UncreatedInteraction(type);ZNetScene.instance.Ready=delegate{return false;};Tick(.1f);
                Check("all eligible interaction groups enter grid before server instance: "+type.Name,controller.InitialObject(data)&&sent.Count==1);
                GridReceive(peer,InteractionProof(server,data,sent[0]));Tick(.2f);
                Check("loaded client receives initial interaction without server-area readiness: "+type.Name,data.GetOwner()==1&&!data.Created&&!ZNetScene.instance.FindInstance(data));
                uint state;Check("initial source uses existing exact recipient state gate",controller.TryRecipientState(data,1,out state));
                if(type==typeof(Incinerator))
                {
                    var source=InteractionView(data);var station=source.GetComponent<Incinerator>();
                    var guard=typeof(TooManyLags.GridHandoffs.Protocol).GetMethod("InitialCommit",BindingFlags.Static|BindingFlags.NonPublic);
                    station.Burning(true);Check("actual incinerator burn vetoes a source commit before server instantiation",!(bool)guard.Invoke(null,new object[]{source,Kinds.Incinerators}));
                    station.Burning(false);Check("idle incinerator can commit through the same stationary path",(bool)guard.Invoke(null,new object[]{source,Kinds.Incinerators}));
                }
                var view=InteractionView(data);data.Created=true;ZNetScene.instance.Views.Add(data,view);controller.Register(data,view);int writes=data.Writes;Tick(.3f);
                Check("late real instance hydrates existing policy without a return transfer",data.GetOwner()==1&&data.Writes==writes&&!controller.InitialObject(data));
            }
            finally{TooManyLags.GridHandoffs.Protocol.Stop();}
        }
        InitialLoadedSourceGuards(false);
        InitialLoadedSourceGuards(true);
        InitialQueuedUse();
        InitialQuietWork();
        InitialInventoryInspectionWork();
        Console.WriteLine("PASS: "+(checks-start)+" general initial-object assertions: every eligible category, real client view, existing ownership tuple, source inventory/use protection and no idle object rescans.");
    }
    private static void InitialInventoryInspectionWork()
    {
        var commit=typeof(TooManyLags.GridHandoffs.Protocol).GetMethod("InitialCommit",BindingFlags.Static|BindingFlags.NonPublic);
        Fresh(Kinds.Containers,defaultInteractives:true);
        GridStart();
        try
        {
            for(int i=0;i<100;i++)
            {
                var root=new GameObject();var view=root.Add(new ZNetView());
                var child=new GameObject();child.transform.parent=root.transform;root.Children.Add(child);
                var inventory=child.Add(new Container());
                Check("child inventory accepts an idle initial commit",(bool)commit.Invoke(null,new object[]{view,Kinds.Containers}));
                Check("initial inspection traverses the inventory hierarchy once",root.ContainerHierarchyReads==1&&child.ContainerHierarchyReads==1);
                int scans=GameObject.Scans;
                inventory.InUse=true;Check("cached child inventory still blocks an open inventory",!(bool)commit.Invoke(null,new object[]{view,Kinds.Containers}));inventory.InUse=false;
                inventory.Loading(true);Check("cached child inventory still blocks local inventory loading",!(bool)commit.Invoke(null,new object[]{view,Kinds.Containers}));inventory.Loading(false);
                inventory.m_privacy=Container.PrivacySetting.Private;Check("cached child inventory still checks current privacy",!(bool)commit.Invoke(null,new object[]{view,Kinds.Containers}));inventory.m_privacy=Container.PrivacySetting.Public;
                Check("cached child inventory becomes usable after use ends",(bool)commit.Invoke(null,new object[]{view,Kinds.Containers}));
                Check("warm inventory guards require no hierarchy rescans",GameObject.Scans==scans);
            }
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
    }
    private static void InitialLoadedSourceGuards(bool attachDuringCommit)
    {
        Fresh(Kinds.Containers,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var first=Peer(1);var other=Peer(2,64);var server=new ZNetPeer {Uid=99,m_server=true};ZNet.instance.Peers.Add(server);GridStart();
        try
        {
            HarvestHello(first);HarvestHello(other);Tick(0);var data=UncreatedInteraction(typeof(Container));
            ZNetScene.instance.Ready=delegate{return false;};var sent=GridCapture(first);Tick(.1f);
            GridReceive(first,InteractionProof(server,data,sent[0]));Tick(.2f);Check("first inventory claim avoids a pointless RequestOpen round trip",data.GetOwner()==1);
            MovePeer(first,64);MovePeer(other,0);var toOther=GridCapture(other);Tick(.3f);
            Check("second player can request the same object before server creation",toOther.Count==1&&data.GetOwner()==1);
            GridReceive(other,InteractionProof(server,data,toOther[0],2));var toFirst=GridCapture(first);Tick(.4f);
            Check("player-owned inventory requires its real source commit",toFirst.Count==1&&data.GetOwner()==1);
            var commit=toFirst[0];commit.SetPos(0);Check("source guard is attached to identified commit",commit.ReadInt()==2);long token=commit.ReadLong();commit.ReadZDOID();Check("source commit targets current grid player",commit.ReadLong()==2);commit.ReadUInt();Check("inventory guard capability is explicit",commit.ReadInt()==(int)Kinds.Containers);
            var actual=InteractionView(data);var inventory=actual.GetComponent<Container>();
            var guard=typeof(TooManyLags.GridHandoffs.Protocol).GetMethod("InitialCommit",BindingFlags.Static|BindingFlags.NonPublic);
            inventory.InUse=true;Check("actual open source inventory refuses proxy handoff",!(bool)guard.Invoke(null,new object[]{actual,Kinds.Containers}));
            inventory.InUse=false;inventory.Loading(true);Check("actual source inventory load refuses proxy handoff",!(bool)guard.Invoke(null,new object[]{actual,Kinds.Containers}));
            inventory.Loading(false);Check("idle actual source can use native FGN ownership commit",(bool)guard.Invoke(null,new object[]{actual,Kinds.Containers}));
            if(attachDuringCommit)
            {
                var serverView=InteractionView(data);data.Created=true;ZNetScene.instance.Views.Add(data,serverView);controller.Register(data,serverView);
                Check("real server instance preserves an in-flight source transaction",controller.TransferPending(data)&&!controller.InitialObject(data));
            }
            int queries=toOther.Count,commits=toFirst.Count;
            for(int i=0;i<10;i++)Tick(.41f+i*.005f);
            Check("pending source commit never repeats destination proof or ownership command",toOther.Count==queries&&toFirst.Count==commits);
            data.SetOwner(2);controller.OwnerChanged(data);
            GridReceive(first,GridPacket(4,delegate(ZPackage p){p.Write(token);p.Write(data.m_uid);p.Write(true);p.Write(true);p.Write(2L);p.Write((uint)data.OwnerRevision);p.Write(data.DataRevision);}));
            Check("committed inventory still requires its actual recipient-state proof",!RequestRouter.RecipientReady(ZDOMan.instance,2,data));
            var query=toOther[toOther.Count-1];query.SetPos(0);Check("post-commit inventory proof uses existing ready message",query.ReadInt()==5);query.ReadZDOID();long readyToken=query.ReadLong();
            GridReceive(other,GridReply(data,readyToken,true));queries=toOther.Count;Tick(.5f);
            Check("identified inventory receipt survives optional server creation",data.GetOwner()==2&&controller.InitialObject(data)==!attachDuringCommit&&!controller.TransferPending(data));
            Check("settling source commit preserves an already confirmed delivery proof",RequestRouter.RecipientReady(ZDOMan.instance,2,data)&&toOther.Count==queries);
            if(attachDuringCommit)
            {
                var node=controller.RequestNode(data.m_uid,"RPC_RequestOpen".GetStableHashCode());
                Check("server creation does not restart a completed native inventory acquisition",node.Managed&&node.ManagedOwner==2);
            }
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
    }
    private static void InitialQueuedUse()
    {
        foreach(Type type in new[]{typeof(Container),typeof(Smelter)})
        {
            Fresh(Kinds.Containers|Kinds.Smelters,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
            var peer=Peer(1);var server=new ZNetPeer {Uid=99,m_server=true};ZNet.instance.Peers.Add(server);GridStart();
            try
            {
                HarvestHello(peer);Tick(0);var sent=GridCapture(peer);var data=UncreatedInteraction(type);
                string method=type==typeof(Container)?"RPC_RequestOpen":"RPC_AddFuel";
                var request=Request(data,method,99);var node=controller.RequestNode(data.m_uid,method.GetStableHashCode());
                Check("pre-instance interaction is queued without a missing server handler",controller.Router.TryHandle(ZRoutedRpc.instance,request,true)&&node.Requests==1);
                Check("unexecuted server request cannot create a first-claim use delay",!node.UseProtected());
                ZNetScene.instance.Ready=delegate{return false;};Tick(.1f);
                GridReceive(peer,InteractionProof(server,data,sent[0]));Tick(.2f);
                Check("queued opening or refill does not stall the first loaded player claim",data.GetOwner()==1&&node.Requests==1);
            }
            finally{TooManyLags.GridHandoffs.Protocol.Stop();}
        }
    }
    private static void InitialQuietWork()
    {
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var peer=Peer(1);GridStart();
        try
        {
            HarvestHello(peer);Tick(0);var sent=GridCapture(peer);var data=new List<ZDO>();
            for(int i=0;i<100;i++)data.Add(UncreatedInteraction(typeof(Door)));
            ZNetScene.instance.Ready=delegate{return false;};Tick(.1f);Check("initial queries concern only requested initial objects",sent.Count==100);
            for(int i=0;i<data.Count;i++)GridReceive(peer,GridReply(data[i],GridToken(sent[i]),true,true));Tick(.2f);
            int attempts=controller.ScheduledAttempts,scans=GameObject.Scans,lookups=GameObject.Lookups,packets=sent.Count;
            for(int i=0;i<1000;i++)Tick(1+i*.05f);
            Check("100 initial objects in 1000 standing updates cause zero extra object jobs or hierarchy scans",attempts==controller.ScheduledAttempts&&scans==GameObject.Scans&&lookups==GameObject.Lookups&&packets==sent.Count);
            MovePeer(peer,1024);Tick(60);
            foreach(var item in data)Check("unloaded initial object entry is released",!controller.InitialObject(item));
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
    }
}
