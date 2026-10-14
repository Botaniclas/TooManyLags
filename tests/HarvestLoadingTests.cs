using System;
using System.Collections.Generic;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static ZDO UncreatedHarvest(string name, float x = 1, float z = 1)
    {
        var prefab = new GameObject { name = name, activeInHierarchy = false };
        prefab.Add(new ZNetView()); prefab.Add(new Pickable());
        var data = new ZDO { m_uid = new ZDOID { ID = next++ }, Hash = 100 + next, Position = new Vector3(x, 0, z) };
        ZDOMan.instance.Objects.Add(data); ZNetScene.instance.Prefabs.Add(data.Hash, prefab);
        data.SetOwner(99); data.Writes = 0; controller.OwnerChanged(data);
        return data;
    }
    private static ZNetView HarvestView(ZDO data)
    {
        var root = new GameObject { name = ZNetScene.instance.GetPrefab(data.GetPrefab()).name };
        root.transform.position = data.GetPosition(); var view = root.Add(new ZNetView { Data = data });
        root.Add(new Pickable()); return view;
    }
    private static void HarvestHello(ZNetPeer peer)
    { GridReceive(peer, GridPacket(0, delegate(ZPackage p) { p.Write(5); })); }
    private static ZPackage HarvestProof(ZNetPeer server, ZDO data, ZPackage query, bool areaReady, Action<GameObject> alter = null)
    {
        var view = HarvestView(data); if (alter != null) alter(view.gameObject);
        ZNetScene.instance.Views.Add(data, view);
        try { return GridClientReply(server, query, areaReady); }
        finally { ZNetScene.instance.Views.Remove(data); }
    }
    private static void HarvestLoading()
    {
        int start = checks;
        Fresh(Kinds.Pickables | Kinds.Doors | Kinds.TamedAnimals, defaultInteractives:true, cellSize:4,
            pattern:GridCoveragePattern.Rounded7x7, directional:true, checkIntervalSeconds:.05f);
        Tick(0); var peer = Peer(1); var server = new ZNetPeer { Uid=99, m_server=true }; ZNet.instance.Peers.Add(server);
        GridStart();
        try
        {
            HarvestHello(peer); var sent = GridCapture(peer); ZNetScene.instance.Ready = delegate { return false; };
            foreach (string name in new[] { "Pickable_Branch", "Pickable_Log", "BlueberryBush", "RaspberryBush", "Pickable_Carrot", "Pickable_Onion" })
            {
                var data = UncreatedHarvest(name);
                Check("first server assignment registers fixed harvest without a server instance",controller.InitialObject(data));
                Tick(Time.unscaledTime + .1f); var query = sent[sent.Count-1]; long token = GridToken(query);
                Check("absent server instance leaves owner protected until proof",data.GetOwner()==99 && !data.Created && !ZNetScene.instance.FindInstance(data));
                var noArea = HarvestProof(server,data,query,false);
                Check("proximity without recipient area readiness cannot authorize harvest transfer",!GridReplyAvailable(noArea));
                GridReceive(peer,noArea); Tick(Time.unscaledTime+.1f);
                Check("negative harvest proof preserves server owner",data.GetOwner()==99);
                Tick(Time.unscaledTime+.6f); query=sent[sent.Count-1];
                Check("negative reply retires its nonce before a fresh proof",GridToken(query)!=token);
                var yesArea = HarvestProof(server,data,query,true); Check("actual loaded harvest instance can prove ready",GridReplyAvailable(yesArea));
                GridReceive(peer,yesArea); Tick(Time.unscaledTime+.1f);
                Check("harvest grid transfers before server instantiation",data.GetOwner()==1 && !data.Created && controller.InitialObject(data));
                var node=controller.RequestNode(data.m_uid,"RPC_Pick".GetStableHashCode());
                Check("early handoff keeps exact recipient state requirement",node!=null && node.Recipient.Matches(1,data.OwnerRevision));
                var late = HarvestView(data); data.Created=true; ZNetScene.instance.Views.Add(data,late); controller.Register(data,late);
                var attached = controller.RequestNode(data.m_uid,"RPC_Pick".GetStableHashCode());
                Check("native server registration adopts same node and preserves recipient state",ReferenceEquals(node,attached) && ReferenceEquals(node.View,late) && !controller.InitialObject(data) && node.Recipient.Matches(1,data.OwnerRevision));
                int writes=data.Writes; Tick(Time.unscaledTime+.1f);
                Check("server catch-up does not repeat ownership transfer",data.GetOwner()==1 && data.Writes==writes);
                controller.Unregister(late);
            }
        }
        finally { TooManyLags.GridHandoffs.Protocol.Stop(); }

        // Old clients get no unsupported proof message; recipient instance
        // structure and actual collider-area readiness remain mandatory.
        foreach (string veto in new[] { "legacy", "body", "inventory", "attached" })
        {
            Fresh(Kinds.Pickables,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
            Tick(0); peer=Peer(1); server=new ZNetPeer {Uid=99,m_server=true};ZNet.instance.Peers.Add(server);GridStart();
            try
            {
                if(veto=="legacy")GridHello(peer,true);else HarvestHello(peer);
                var sent=GridCapture(peer);var data=UncreatedHarvest("RaspberryBush");
                ZNetScene.instance.Ready=delegate{return false;};Tick(.1f);
                if(veto=="legacy")Check("old proof capability does not authorize instance-less takeover",sent.Count==0 && data.GetOwner()==99);
                else
                {
                    var reply=HarvestProof(server,data,sent[0],true,delegate(GameObject root)
                    {
                        if(veto=="body")root.Add(new Rigidbody());
                        if(veto=="inventory")root.Add(new Container());
                        if(veto=="wear")root.Add(new WearNTear());
                        if(veto=="plant")root.Add(new Plant());
                        if(veto=="attached")root.transform.parent=new GameObject().transform;
                        if(veto=="other interaction")root.Add(new CustomInteractable());
                    });
                    Check("recipient structural veto: "+veto,!GridReplyAvailable(reply));GridReceive(peer,reply);Tick(.2f);
                    Check("unsafe recipient keeps server ownership",data.GetOwner()==99);
                }
            }
            finally{TooManyLags.GridHandoffs.Protocol.Stop();}
        }

        HarvestQueuedRequests();
        SharedServerLoadRequests();
        GeneralInitialLoading();
        HarvestCleanupAndScope();
        Console.WriteLine("PASS: "+(checks-start)+" early-harvest assertions: loaded recipient only, unchanged native creation, exact-once queued pick, same-node adoption, bounded lifecycle and legacy/physics/inventory exclusions.");
    }
    private static void HarvestQueuedRequests()
    {
        Fresh(Kinds.Pickables,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        Tick(0);var peer=Peer(1);GridStart();
        try
        {
            HarvestHello(peer);var sent=GridCapture(peer);var data=UncreatedHarvest("Pickable_Carrot");
            var request=Request(data,"RPC_Pick",99);request.m_parameters.Write(7);
            Check("pick arriving before server handler exists is held once",controller.Router.TryHandle(ZRoutedRpc.instance,request,true));
            var node=controller.RequestNode(data.m_uid,"RPC_Pick".GetStableHashCode());
            Tick(.1f);Check("held pick does not run without server instance or client ownership",node.Requests==1 && ZRoutedRpc.instance.Local.Count==0 && ZRoutedRpc.instance.Remote.Count==0);
            GridReceive(peer,GridReply(data,GridToken(sent[0]),true,true));Tick(.2f);
            Check("owner changes but queued pick waits for exact recipient revision",data.GetOwner()==1 && node.Requests==1 && ZRoutedRpc.instance.Remote.Count==0);
            ZDOMan.instance.MarkSent(1,data);controller.Router.Flush();
            var last=sent[sent.Count-1];last.SetPos(0);Check("recipient readiness query follows real owner packet",last.ReadInt()==5);
            last.ReadZDOID();long token=last.ReadLong();GridReceive(peer,GridReply(data,token,true));controller.Router.Flush();
            Check("queued pick delivered exactly once after recipient ready",node.Requests==0 && ZRoutedRpc.instance.Remote.Count==1 && ZRoutedRpc.instance.Local.Count==0);
            var delivered=ZRoutedRpc.instance.Remote[0];delivered.m_parameters.SetPos(0);
            Check("native harvest sender and bonus survive early routing",delivered.m_senderPeerID==1 && delivered.m_methodHash==request.m_methodHash && delivered.m_parameters.ReadInt()==7);
            controller.Router.Flush();Tick(.3f);Check("later flush cannot repeat harvest",ZRoutedRpc.instance.Remote.Count==1);
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}

        // Shared ownership stays on the server. If its instance catches up
        // first, the original request and native handler are retained.
        Fresh(Kinds.Pickables,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        Tick(0);peer=Peer(1);Peer(2);GridStart();
        try
        {
            var data=UncreatedHarvest("BlueberryBush");var node=controller.RequestNode(data.m_uid,"RPC_Pick".GetStableHashCode());
            var request=Request(data,"RPC_Pick",99);controller.Router.TryHandle(ZRoutedRpc.instance,request,true);Tick(.1f);
            Check("overlapping field does not use a player shortcut",data.GetOwner()==99 && node.Requests==1 && ZRoutedRpc.instance.Local.Count==0);
            var view=HarvestView(data);data.Created=true;ZNetScene.instance.Views.Add(data,view);controller.Register(data,view);
            Check("AddInstance does not dispatch before Pickable Awake registers handler",ZRoutedRpc.instance.Local.Count==0);
            controller.Router.Flush();Check("shared queued pick uses real server handler once it exists",ZRoutedRpc.instance.Local.Count==1 && node.Requests==0 && ReferenceEquals(node.View,view));
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}

        Fresh(Kinds.Pickables,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        Tick(0);Peer(1);Peer(2);GridStart();
        try
        {
            var data=UncreatedHarvest("RaspberryBush");var request=Request(data,"RPC_Pick",99);
            controller.Router.TryHandle(ZRoutedRpc.instance,request,true);Tick(.1f);Tick(5.1f);
            var node=controller.RequestNode(data.m_uid,"RPC_Pick".GetStableHashCode());
            Check("unanswered startup pick expires without a late harvest",node.Requests==0 && ZRoutedRpc.instance.Local.Count==0);
            var view=HarvestView(data);data.Created=true;ZNetScene.instance.Views.Add(data,view);controller.Register(data,view);controller.Router.Flush();
            Check("late server creation cannot replay an expired startup click",ZRoutedRpc.instance.Local.Count==0);
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
    }
    private static void HarvestCleanupAndScope()
    {
        Fresh(Kinds.Pickables|Kinds.Doors|Kinds.TamedAnimals,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        Tick(0);var peer=Peer(1);GridStart();
        try
        {
            HarvestHello(peer);var data=UncreatedHarvest("Pickable_Turnip");Tick(.1f);
            var node=controller.RequestNode(data.m_uid,"RPC_Pick".GetStableHashCode());
            controller.DataDestroyed(data);Check("destroying uncreated harvest retires field, proof and node",node.Removed && !controller.InitialObject(data) && GridCacheCount()==0);
            data=UncreatedHarvest("BlueberryBush");Tick(.2f);node=controller.RequestNode(data.m_uid,"RPC_Pick".GetStableHashCode());
            MovePeer(peer,1024);Tick(.3f);
            Check("teleport out removes uninstantiated harvest state without waiting for unload",node.Removed && !controller.InitialObject(data) && GridCacheCount()==0);
            MovePeer(peer,0);data=UncreatedHarvest("RaspberryBush");ZNet.instance.Peers.Clear();Tick(.4f);
            Check("disconnect before first prune also retires pending harvest state",!controller.InitialObject(data));
            peer=Peer(1);data=UncreatedHarvest("Pickable_Carrot");Tick(.5f);controller.Reset();
            Check("world reset releases uninstantiated harvest references",!controller.InitialObject(data) && GridCacheCount()==0);
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}

        Fresh(Kinds.Pickables,defaultInteractives:false);Tick(0);Peer(1);GridStart();
        try{var data=UncreatedHarvest("BlueberryBush");Check("early harvest path requires server-default mode",!controller.InitialObject(data));}
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
        Fresh(Kinds.Doors,defaultInteractives:true);Tick(0);Peer(1);GridStart();
        try{var data=UncreatedHarvest("BlueberryBush");Check("disabled pickable category is respected before server instantiation",!controller.InitialObject(data));}
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
    }
    private static void SharedServerLoadRequests()
    {
        foreach (Type type in new[] { typeof(Pickable), typeof(Door), typeof(Container), typeof(CraftingStation) })
        {
            Fresh(Kinds.Pickables|Kinds.Doors|Kinds.Containers|Kinds.Workstations,defaultInteractives:true,
                cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
            var player=Peer(1);var other=Peer(2,64);var data=Object(type,1,1,1);
            Tick(0);ZNetScene.instance.Ready=delegate{return false;};MovePeer(other,0);Tick(.1f);
            var node=controller.RequestNode(data.m_uid,(type==typeof(Door)?"UseDoor":type==typeof(Container)?"RPC_RequestOpen":type==typeof(Pickable)?"RPC_Pick":"RPC_Damage").GetStableHashCode());
            Check("unready shared destination retains player owner: "+type.Name,data.GetOwner()==1 && node.WaitingForRequest);
            int jobs=controller.ScheduledAttempts;ZNetScene.instance.Ready=delegate{return true;};Tick(1);Tick(10);
            Check("server becoming ready does not replay discarded overlap request",data.GetOwner()==1 && controller.ScheduledAttempts==jobs);
            MovePeer(player,4);Tick(11);
            Check("fresh covering movement can request ready shared server: "+type.Name+" "+node.OwnerStatus(),!node.WaitingForRequest && (data.GetOwner()==99 || controller.TransferPending(data)));
        }
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        Peer(1);var departing=Peer(2,64);var door=Object(typeof(Door),1,1,1);Tick(0);
        ZNetScene.instance.Ready=delegate{return false;};MovePeer(departing,0);Tick(.1f);
        var held=controller.RequestNode(door.m_uid,"UseDoor".GetStableHashCode());Check("overlap readiness rejection waits sparsely",held.WaitingForRequest);
        MovePeer(departing,64);Tick(.2f);Tick(3);
        Check("discarded server request does not block new exclusive player policy",door.GetOwner()==1 && !held.WaitingForRequest);
    }
}
