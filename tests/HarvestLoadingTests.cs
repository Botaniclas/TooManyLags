using System;
using System.Collections.Generic;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static ZDO UncreatedHarvest(string name, float x = 1, float z = 1, bool notify = true)
    {
        var prefab = new GameObject { name = name, activeInHierarchy = false };
        prefab.Add(new ZNetView()); prefab.Add(new Pickable());
        var data = new ZDO { m_uid = new ZDOID { ID = next++ }, Hash = 100 + next, Position = new Vector3(x, 0, z) };
        ZDOMan.instance.Objects.Add(data); ZNetScene.instance.Prefabs.Add(data.Hash, prefab);
        data.SetOwner(99); data.Writes = 0; if (notify) controller.OwnerChanged(data);
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
    private static ZPackage HarvestProof(ZNetPeer server, ZDO data, ZPackage query, bool areaReady, Action<GameObject> alter = null,Func<Vector3,bool> regionReady=null)
    {
        var view = HarvestView(data); if (alter != null) alter(view.gameObject);
        ZNetScene.instance.Views.Add(data, view);
        try { return GridClientReply(server, query, areaReady,regionReady); }
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
        RetainedHarvestRegistration();
        ClientSectorBorderProof();
        ClientSectorBorderWork();
        Console.WriteLine("PASS: "+(checks-start)+" early-harvest assertions: loaded recipient only, unchanged native creation, exact-once queued pick, same-node adoption, bounded lifecycle and legacy/physics/inventory exclusions.");
    }
    private static void ClientSectorBorderProof()
    {
        Fresh(Kinds.Pickables,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var peer=Peer(1,31,31);var server=new ZNetPeer{Uid=99,m_server=true};ZNet.instance.Peers.Add(server);GridStart();
        try
        {
            HarvestHello(peer);Tick(0);var sent=GridCapture(peer);int step=0;
            Func<Vector3,bool> playerReady=delegate(Vector3 point){return ZoneSystem.GetZone(point).Equals(ZoneSystem.GetZone(peer.Position));};
            foreach(Vector3 position in new[]{new Vector3(33,0,31),new Vector3(31,0,33),new Vector3(33,0,33)})
            {
            float now=.1f+step++;var data=UncreatedHarvest("BlueberryBush",position.x,position.z);
            ZNetScene.instance.Ready=delegate{return false;};Tick(now);var query=sent[sent.Count-1];
            var reply=HarvestProof(server,data,query,true,null,playerReady);
            Check("loaded neighbouring-sector harvest uses ready player footprint instead of missing outer sectors",GridReplyAvailable(reply));
            var wrong=HarvestProof(server,data,query,true,null,delegate(Vector3 point){return !playerReady(point);});
            Check("ready object-centred area cannot bypass an unready player footprint",!GridReplyAvailable(wrong));
            ZoneSystem.instance.Loaded=false;wrong=HarvestProof(server,data,query,true,null,playerReady);ZoneSystem.instance.Loaded=true;
            Check("ready player footprint never bypasses the harvest's unloaded terrain zone",!GridReplyAvailable(wrong));
            MovePeer(peer,1024,1024);wrong=HarvestProof(server,data,query,true);MovePeer(peer,31,31);
            Check("loaded old harvest outside current native reference area cannot prove ready",!GridReplyAvailable(wrong));
            GridReceive(peer,reply);Tick(now+.1f);Check("neighbouring-sector harvest transfers while server is unready",data.GetOwner()==1&&!data.Created);
            Check("post-owner startup delivery still requires current native state",!TooManyLags.GridHandoffs.Protocol.Ready(ZDOMan.instance,1,data));
            query=sent[sent.Count-1];reply=HarvestProof(server,data,query,true,null,playerReady);
            Check("post-owner delivery uses the same loaded local footprint across a sector boundary",GridReplyAvailable(reply));
            GridReceive(peer,reply);Check("queued interaction can finish without loading outer client sectors",TooManyLags.GridHandoffs.Protocol.Ready(ZDOMan.instance,1,data));
            }
            foreach(Type type in new[]{typeof(Vagon),typeof(Character),typeof(Pickable)})
            {
                var data=Object(type,33,31,1);var view=ZNetScene.instance.FindInstance(data);
                if(type==typeof(Pickable))view.gameObject.Add(new Rigidbody());
                var query=GridPacket(5,delegate(ZPackage p){p.Write(data.m_uid);p.Write(9000L);p.Write(1L);p.Write((uint)data.OwnerRevision);p.Write(data.DataRevision);});
                Check("dynamic physics still requires object-centred readiness: "+type.Name,!GridReplyAvailable(GridClientReply(server,query,true,playerReady)));
                Check("dynamic physics keeps its original object-ready result: "+type.Name,GridReplyAvailable(GridClientReply(server,query,true,delegate(Vector3 point){return !playerReady(point);})));
            }
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
    }
    private static void ClientSectorBorderWork()
    {
        Fresh(Kinds.Pickables,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var peer=Peer(1,31,31);var server=new ZNetPeer{Uid=99,m_server=true};ZNet.instance.Peers.Add(server);GridStart();
        var temporary=new List<ZDO>();
        try
        {
            HarvestHello(peer);Tick(0);var sent=GridCapture(peer);
            foreach(Vector3 p in new[]{new Vector3(33,0,31),new Vector3(31,0,33),new Vector3(33,0,33)})temporary.Add(UncreatedHarvest("RaspberryBush",p.x,p.z));
            ZNetScene.instance.Ready=delegate{return false;};Tick(.1f);
            foreach(var data in temporary)ZNetScene.instance.Views.Add(data,HarvestView(data));
            var replies=GridCapture(server);
            try
            {
                ZNet.instance.Dedicated=false;ZNet.instance.ServerPeer=server;ZDOMan.SessionID=1;Time.frameCount++;
                ZNetScene.instance.Ready=delegate(Vector3 p){return ZoneSystem.GetZone(p).Equals(ZoneSystem.GetZone(peer.Position));};
                int scans=ZNetScene.instance.AreaQueries;
                foreach(var query in sent)GridReceive(server,query);
                Check("three neighbouring object sectors share one native player-area scan",ZNetScene.instance.AreaQueries-scans==1);
                Check("shared player-area result still proves every actual object",replies.Count==3&&replies.TrueForAll(GridReplyAvailable));
            }
            finally
            {
                foreach(var data in temporary)ZNetScene.instance.Views.Remove(data);
                ZNet.instance.Dedicated=true;ZNet.instance.ServerPeer=null;ZDOMan.SessionID=99;ZNetScene.instance.Ready=delegate{return false;};
            }
            foreach(var reply in replies)GridReceive(peer,reply);Tick(.2f);
            Check("shared area proof does not replace individual object and state requirements",temporary.TrueForAll(delegate(ZDO data){return data.GetOwner()==1&&!data.Created;}));
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
    }
    private static void RetainedHarvestRegistration()
    {
        var patcher=new Harmony("botanic.authority.test.retained-harvest");
        patcher.Patch(AccessTools.Method(typeof(ZDOMan),"ReleaseNearbyZDOS"),transpiler:new HarmonyMethod(AccessTools.Method(typeof(OwnershipPatch),"Transpiler")));
        try
        {
            foreach(string reason in new[]{"ordinary owner event","existing server owner","owner before reference ready","return after pruning"})
            {
                Fresh(Kinds.Pickables,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
                var peer=Peer(1,31,31);var server=new ZNetPeer{Uid=99,m_server=true};ZNet.instance.Peers.Add(server);GridStart();
                try
                {
                    HarvestHello(peer);Tick(0);var objects=new List<ZDO>();
                    if(reason=="owner before reference ready"){peer.Ready=false;server.Ready=false;}
                    bool notify=reason!="existing server owner";
                    objects.Add(UncreatedHarvest("Pickable_Branch",29,31,notify));
                    objects.Add(UncreatedHarvest("BlueberryBush",33,31,notify));
                    objects.Add(UncreatedHarvest("RaspberryBush",31,33,notify));
                    objects.Add(UncreatedHarvest("Pickable_Carrot",33,33,notify));
                    peer.Ready=true;server.Ready=true;
                    if(reason=="return after pruning")
                    {
                        MovePeer(peer,1024,1024);server.Ready=false;Tick(.01f);server.Ready=true;MovePeer(peer,31,31);Tick(.02f);
                    }
                    if(reason=="owner before reference ready")Tick(.05f);
                    var adjacent=UncreatedHarvest("Pickable_Log",30,31);
                    Check("adjacent ordinary owner event is registered",controller.InitialObject(adjacent));
                    Check("regression covers the login sector plus three neighbouring sectors",!ZoneSystem.GetZone(objects[0].GetPosition()).Equals(ZoneSystem.GetZone(objects[3].GetPosition())));
                    ZNetScene.instance.Ready=delegate{return false;};var sent=GridCapture(peer);
                    ZDOMan.instance.ReleaseNearbyZDOS(peer.Position,1);
                    foreach(var data in objects)Check("native owner retention also indexes missing harvest: "+reason,controller.InitialObject(data));
                    Check("native retention does not write ownership or send a handoff",objects.TrueForAll(delegate(ZDO d){return d.GetOwner()==99&&d.Writes==0;})&&sent.Count==0);
                    Tick(.1f);Check("all four sectors request destination loading",sent.Count==objects.Count+1);
                    foreach(var query in new List<ZPackage>(sent))
                    {
                        query.SetPos(0);query.ReadInt();var data=ZDOMan.instance.GetZDO(query.ReadZDOID());
                        GridReceive(peer,HarvestProof(server,data,query,true));
                    }
                    Tick(.2f);
                    foreach(var data in objects)Check("loaded client can claim retained harvest across sector borders: "+reason,data.GetOwner()==1&&!data.Created);
                    int jobs=controller.ScheduledAttempts,scans=GameObject.Scans,packets=sent.Count;
                    for(int i=0;i<25;i++){ZDOMan.instance.ReleaseNearbyZDOS(peer.Position,1);Tick(1+i);}
                    Check("later native passes do not reindex or query completed harvest",jobs==controller.ScheduledAttempts&&scans==GameObject.Scans&&packets==sent.Count);
                    Console.WriteLine("PASS: harvest across login sector and three neighbours; "+reason+".");
                }
                finally{TooManyLags.GridHandoffs.Protocol.Stop();}
            }
        }
        finally{patcher.UnpatchSelf();}
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
