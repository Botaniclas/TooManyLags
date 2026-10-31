using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private const string GridRpc = "TooManyLags_GridHandoff_1";
    private static ZPackage GridPacket(int message, Action<ZPackage> write)
    { var p=new ZPackage();p.Write(message);if(write!=null)write(p);return new ZPackage(p.GetArray()); }
    private static void GridReceive(ZNetPeer peer,ZPackage packet)
    { peer.m_rpc.Receive(GridRpc,new ZPackage(packet.GetArray())); }
    private static List<ZPackage> GridCapture(ZNetPeer peer)
    {
        var list=new List<ZPackage>();peer.m_rpc.OnInvoke=delegate(string method,object[] args)
        { if(method==GridRpc)list.Add(new ZPackage(((ZPackage)args[0]).GetArray())); };return list;
    }
    private static long GridToken(ZPackage p)
    { p.SetPos(0);Check("load query uses appended message id",p.ReadInt()==9);p.ReadZDOID();return p.ReadLong(); }
    private static ZPackage GridReply(ZDO data,long token,bool available,bool? replica=null)
    { return GridPacket(6,delegate(ZPackage p){p.Write(data.m_uid);p.Write(token);p.Write(available);if(replica.HasValue)p.Write(replica.Value);}); }
    private static int GridCacheCount()
    { return ((IDictionary)typeof(TooManyLags.GridHandoffs.Protocol).GetField("ready",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Count; }
    private static ZPackage GridClientReply(ZNetPeer serverPeer,ZPackage query,bool areaReady,Func<Vector3,bool> regionReady=null)
    {
        bool dedicated=ZNet.instance.Dedicated;long session=ZDOMan.SessionID;var previous=ZNet.instance.ServerPeer;
        var oldReady=ZNetScene.instance.Ready;var replies=GridCapture(serverPeer);
        try
        {
            ZNet.instance.Dedicated=false;ZDOMan.SessionID=1;ZNet.instance.ServerPeer=serverPeer;
            ZNetScene.instance.Ready=regionReady??delegate{return areaReady;};Time.frameCount++;
            GridReceive(serverPeer,query);TooManyLags.GridHandoffs.Protocol.FlushDeferred();Check("authenticated server receives one client proof",replies.Count==1);return replies[0];
        }
        finally { ZNet.instance.Dedicated=dedicated;ZDOMan.SessionID=session;ZNet.instance.ServerPeer=previous;ZNetScene.instance.Ready=oldReady; }
    }
    private static bool GridReplyAvailable(ZPackage packet)
    { packet.SetPos(0);Check("load proof shares readiness reply",packet.ReadInt()==6);packet.ReadZDOID();packet.ReadLong();return packet.ReadBool(); }
    private static void GridStart()
    { TooManyLags.GridHandoffs.Protocol.Start(typeof(FiresGhettoNetworkMod.FiresGhettoNetworkMod).Assembly); }
    private static void GridHello(ZNetPeer peer,bool modern)
    { GridReceive(peer,GridPacket(0,modern?(Action<ZPackage>)delegate(ZPackage p){p.Write(1);}:null)); }
    private static void RefillProtocolLease()
    {
        Fresh(Kinds.Smelters,defaultInteractives:true);Peer(1);
        var serverPeer=new ZNetPeer{Uid=99,m_server=true};ZNet.instance.Peers.Add(serverPeer);
        var data=Object(typeof(Smelter),1,1,1);
        bool dedicated=ZNet.instance.Dedicated;long session=ZDOMan.SessionID;var previous=ZNet.instance.ServerPeer;
        GridStart();
        try
        {
            ZNet.instance.Dedicated=false;ZDOMan.SessionID=1;ZNet.instance.ServerPeer=serverPeer;
            GridReceive(serverPeer,GridPacket(1,delegate(ZPackage p){p.Write(true);}));
            int commits=0;
            typeof(TooManyLags.GridHandoffs.Protocol).GetField("nativeCommit",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,
                (Action<ZRpc,ZDOID,long>)delegate{commits++;});
            var receive=typeof(TooManyLags.GridHandoffs.Protocol).GetMethod("ReceiveRequest",BindingFlags.Static|BindingFlags.NonPublic);
            var request=new ZRoutedRpc.RoutedRPCData{m_targetPeerID=1,m_targetZDO=data.m_uid,m_senderPeerID=1,m_methodHash="RPC_AddOre".GetStableHashCode(),m_parameters=new ZPackage()};
            Action<long> commit=delegate(long token){GridReceive(serverPeer,GridPacket(2,delegate(ZPackage p){p.Write(token);p.Write(data.m_uid);p.Write(2L);p.Write((uint)data.OwnerRevision);}));};
            Check("owned refill RPC stays on the native client path",(bool)receive.Invoke(null,new object[]{request}));
            Time.unscaledTime=.5f;commit(1);Check("client refill lease blocks a source handoff",commits==0);
            Time.unscaledTime=.75f;receive.Invoke(null,new object[]{request});
            Time.unscaledTime=1.74f;commit(2);Check("another client refill renews the full one-second hold",commits==0);
            request.m_methodHash="RPC_Repair".GetStableHashCode();receive.Invoke(null,new object[]{request});
            Time.unscaledTime=1.75f;commit(3);Check("client refill hold expires at one second and unrelated RPCs do not extend it",commits==1);
        }
        finally
        {
            ZNet.instance.Dedicated=dedicated;ZDOMan.SessionID=session;ZNet.instance.ServerPeer=previous;
            TooManyLags.GridHandoffs.Protocol.Stop();
        }
    }
    private static void DestinationLoadProtocol()
    {
        int before=checks;
        foreach(Type type in new[]{typeof(Door),typeof(Pickable),typeof(Container),typeof(Fireplace),typeof(CraftingStation)})
        {
            Fresh(Kinds.Doors|Kinds.Pickables|Kinds.Containers|Kinds.Fireplaces|Kinds.Workstations,
                defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true,keepOnExit:false);
            var peer=Peer(1);GridStart();
            try
            {
                HarvestHello(peer);var sent=GridCapture(peer);
                ZNetScene.instance.Ready=delegate{throw new Exception("Ownership must not query broad readiness.");};
                var data=Object(type,1,1,99);Tick(0);
                Check("loaded red object transfers without a loading query: "+type.Name,data.GetOwner()==1&&sent.Count==0&&ZNetScene.instance.AreaQueries==0);
                var absent=UncreatedInteraction(type);Tick(.1f);
                Check("missing server instance does not delay initial player acquisition: "+type.Name,absent.GetOwner()==1&&!absent.Created&&!ZNetScene.instance.FindInstance(absent)&&sent.Count==0);
                int attempts=controller.ScheduledAttempts;
                for(int i=1;i<=50;i++)Tick(.1f+i*.05f);
                Check("settled initial ownership has no proof retries: "+type.Name,controller.ScheduledAttempts==attempts&&sent.Count==0&&ZNetScene.instance.AreaQueries==0);
            }
            finally{TooManyLags.GridHandoffs.Protocol.Stop();}
        }
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var source=Peer(7);var nearby=Peer(1);GridStart();
        try
        {
            HarvestHello(source);HarvestHello(nearby);var messages=GridCapture(source);var active=Object(typeof(Door),1,1,7);
            ZNetScene.instance.Ready=delegate{throw new Exception("Source handoff must not query broad readiness.");};Tick(0);
            Check("active player source is not forcibly stolen when a loading gate disappears",active.GetOwner()==7&&active.Writes==0&&messages.Count==1);
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var target=Peer(1);var server=new ZNetPeer{Uid=99,m_server=true};ZNet.instance.Peers.Add(server);GridStart();
        try
        {
            HarvestHello(target);var sent=GridCapture(target);var data=Object(typeof(Door),1,1,99);
            ZNetScene.instance.Ready=delegate{throw new Exception("State receipt must not query broad readiness.");};Tick(0);
            Check("ownership finishes before any state-receipt request",data.GetOwner()==1&&sent.Count==0);
            Check("exceptional recipient requirement remains a separate state check",!RequestRouter.RecipientReady(ZDOMan.instance,1,data)&&sent.Count==1);
            var view=ZNetScene.instance.FindInstance(data);ZNetScene.instance.Views.Remove(data);
            var reply=GridClientReply(server,sent[0],false);
            Check("routed action still needs its exact RPC instance, not a broad ready area",!GridReplyAvailable(reply)&&ZNetScene.instance.AreaQueries==0);
            ZNetScene.instance.Views.Add(data,view);reply=GridClientReply(server,sent[0],false);
            Check("exact live receiver is sufficient despite unrelated missing neighbours",GridReplyAvailable(reply)&&ZNetScene.instance.AreaQueries==0);
            GridReceive(target,reply);
            Check("matching receipt releases routed use without broad load wait",RequestRouter.RecipientReady(ZDOMan.instance,1,data));
            data.OwnerRevision++;
            var stale=GridClientReply(server,sent[0],false);
            Check("stale owner revision still cannot certify state",!GridReplyAvailable(stale));
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
        Check("stop clears initial capability and state receipts",AuthorityController.InitialClient==null&&GridCacheCount()==0);
        Console.WriteLine("PASS: "+(checks-before)+" ownership-without-load-gates assertions: loaded roots, absent server instances, zero readiness scans/probe RPCs, state-only receipts and stale-revision rejection.");
    }
    private static void DestinationStateTraffic()
    {
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var peer=Peer(1);var data=new Dictionary<ZDOID,ZDO>();
        for(int i=0;i<100;i++){var item=Object(typeof(Door),1,1,99);data.Add(item.m_uid,item);}
        GridStart();
        try
        {
            GridHello(peer,true);var sent=GridCapture(peer);ZNetScene.instance.Ready=delegate{return false;};Tick(0);
            int forced=ZDOMan.instance.Forced;
            Check("100 blocked objects request initial state exactly once each",forced==100&&sent.Count==100);
            int offset=0;
            for(int i=0;i<10;i++)
            {
                int end=sent.Count;
                for(int n=offset;n<end;n++)
                {
                    var p=sent[n];p.SetPos(0);Check("blocked object query remains load-only",p.ReadInt()==9);
                    var id=p.ReadZDOID();long token=p.ReadLong();GridReceive(peer,GridReply(data[id],token,false,true));
                }
                offset=end;Tick((i+1)*.55f);
                foreach(var item in data.Values)Check("known replica without local area never grants owner",item.GetOwner()==99);
            }
            Console.WriteLine("LOAD RETRY WORK: 100 objects, 10 negative-ready reply cycles; initial state requests="+forced+", additional state requests="+(ZDOMan.instance.Forced-forced)+", proof queries="+sent.Count+".");
            Check("confirmed replica avoids repeated full-state requests",ZDOMan.instance.Forced==forced&&sent.Count==1100);
            var last=sent[sent.Count-1];last.SetPos(0);last.ReadInt();var lastId=last.ReadZDOID();long lastToken=last.ReadLong();
            GridReceive(peer,GridReply(data[lastId],lastToken,false,false));
            Check("lost replica immediately requests resynchronization",ZDOMan.instance.Forced==forced+1);
            foreach(var item in data.Values)controller.Unregister(ZNetScene.instance.FindInstance(item));
            Check("all waiting state entries disappear on unload",GridCacheCount()==0);
        }
        finally {TooManyLags.GridHandoffs.Protocol.Stop();}
    }
    private static void DestinationGrantCancellation()
    {
        foreach(string reason in new[]{"overlap","local open","opening lease","server ready","native opening"})
        {
            Fresh(Kinds.Doors|Kinds.Containers,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
            var first=Peer(1);var other=Peer(2,64);bool inventory=reason!="overlap"&&reason!="server ready";
            var data=Object(inventory?typeof(Container):typeof(Door),1,1,99);var view=ZNetScene.instance.FindInstance(data);
            GridStart();
            try
            {
                GridHello(first,true);GridHello(other,true);var sent=GridCapture(first);ZNetScene.instance.Ready=delegate{return false;};Tick(0);
                long token=GridToken(sent[0]);GridReceive(first,GridReply(data,token,true,true));
                Check("positive grant waits for controller",data.GetOwner()==99&&GridCacheCount()==1);
                if(reason=="overlap")MovePeer(other,0);
                else if(reason=="local open")view.GetComponent<Container>().InUse=true;
                else if(reason=="opening lease")controller.RequestNode(data.m_uid,"RPC_RequestOpen".GetStableHashCode()).Refilled("RPC_RequestOpen".GetStableHashCode());
                else if(reason=="server ready")ZNetScene.instance.Ready=delegate{return true;};
                else {data.SetOwner(1);controller.OwnerChanged(data);}
                Tick(.1f);
                Check("superseded or blocked grant is retired: "+reason,GridCacheCount()==0);
                if(reason=="server ready"||reason=="native opening")Check("native or complete-server path remains usable",data.GetOwner()==1);
                else
                {
                    Check("no transfer during overlap or inventory use",data.GetOwner()==99);
                    if(reason=="overlap")MovePeer(other,64);
                    else if(reason=="local open")view.GetComponent<Container>().InUse=false;
                    Tick(reason=="opening lease"?5.2f:.2f);
                    Check("later takeover requests fresh load proof",data.GetOwner()==99&&sent.Count==2&&GridToken(sent[1])!=token);
                    GridReceive(first,GridReply(data,token,true,true));Tick(5.3f);
                    Check("old cancelled positive reply cannot restore permission",data.GetOwner()==99);
                }
            }
            finally {TooManyLags.GridHandoffs.Protocol.Stop();}
        }
    }
    private static void NormalDestinationWork()
    {
        Fresh(Kinds.Doors|Kinds.Containers,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var peer=Peer(1);var objects=new List<ZDO>();var nodes=new List<AuthorityController.NativeNode>();
        for(int i=0;i<100;i++)objects.Add(Object(i<50?typeof(Door):typeof(Container),1,1,99));
        GridStart();
        try
        {
            GridHello(peer,true);var sent=GridCapture(peer);Tick(0);
            for(int i=0;i<objects.Count;i++)nodes.Add(controller.RequestNode(objects[i].m_uid,(i<50?"UseDoor":"RPC_RequestOpen").GetStableHashCode()));
            GameObject.Scans=0;int forced=ZDOMan.instance.Forced;
            for(int cycle=0;cycle<100;cycle++)for(int i=0;i<objects.Count;i++)
            {
                objects[i].SetOwnerInternal(99);objects[i].OwnerRevision++;
                Check("ordinary loaded server-to-player assignment completes",nodes[i].Apply(1)==ApplyResult.Done&&objects[i].GetOwner()==1);
            }
            Check("10000 normal transfers have no load queries or readiness entries",sent.Count==0&&GridCacheCount()==0);
            Check("normal transfers retain one existing catalog scan and two native sends",GameObject.Scans==10000&&ZDOMan.instance.Forced-forced==20000);
            Console.WriteLine("NORMAL WORK: 10000 loaded-object transfers; extra load queries=0; ready cache entries=0; catalog scans=10000 (existing path); native sends=20000 (existing path).");
        }
        finally {TooManyLags.GridHandoffs.Protocol.Stop();}
    }
    private static void DeferredStartupProof()
    {
        foreach(bool missingView in new[]{false,true})
        {
            Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true,checkIntervalSeconds:5);
            var target=Peer(1);var server=new ZNetPeer{Uid=99,m_server=true};ZNet.instance.Peers.Add(server);
            var data=Object(typeof(Door),1,1,99);var view=ZNetScene.instance.FindInstance(data);GridStart();
            try
            {
                GridHello(target,true);var sent=GridCapture(target);ZNetScene.instance.Ready=delegate{return false;};Tick(0);
                var query=sent[0];bool dedicated=ZNet.instance.Dedicated;long session=ZDOMan.SessionID;
                ZPackage reply=null;var replies=GridCapture(server);
                try
                {
                    ZNet.instance.Dedicated=false;ZDOMan.SessionID=1;ZNet.instance.ServerPeer=server;Time.frameCount++;
                    ZNetScene.instance.Ready=delegate{return true;};
                    if(missingView)ZNetScene.instance.Views.Remove(data);else data.SetOwnerInternal(1);
                    GridReceive(server,query);Check("incomplete initial snapshot waits for end of frame",replies.Count==0);
                    if(missingView)ZNetScene.instance.Views.Add(data,view);else data.SetOwnerInternal(99);
                    TooManyLags.GridHandoffs.Protocol.FlushDeferred();
                    Check("state or view arrival produces one same-frame response",replies.Count==1&&GridReplyAvailable(replies[0]));reply=replies[0];
                    TooManyLags.GridHandoffs.Protocol.FlushDeferred();Check("deferred queue drains without repeated work",replies.Count==1);
                }
                finally {ZNet.instance.Dedicated=dedicated;ZDOMan.SessionID=session;ZNet.instance.ServerPeer=null;ZNetScene.instance.Ready=delegate{return false;};}
                GridReceive(target,reply);Time.unscaledTime=.01f;controller.Poll(.01f);
                Check("positive startup proof bypasses long polling interval",data.GetOwner()==1&&sent.Count==1);
            }
            finally {TooManyLags.GridHandoffs.Protocol.Stop();}
        }
    }
    private static void WaitingDestinationWork()
    {
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var peer=Peer(1);var objects=new Dictionary<ZDOID,ZDO>();
        for(int i=0;i<100;i++){var data=Object(typeof(Door),1,1,99);objects.Add(data.m_uid,data);}
        GridStart();
        try
        {
            GridHello(peer,true);var sent=GridCapture(peer);ZNetScene.instance.Ready=delegate{return false;};Tick(0);
            foreach(var q in sent){q.SetPos(0);q.ReadInt();var id=q.ReadZDOID();long token=q.ReadLong();GridReceive(peer,GridReply(objects[id],token,false,true));}
            int peers=ZNet.instance.PeerQueries;
            for(int i=1;i<25;i++)Tick(i*.02f);
            Check("unchanged waiting objects skip peer/protocol work before retry",ZNet.instance.PeerQueries==peers&&sent.Count==100);
            Tick(.5f);Check("standing clients retain bounded retry progress",sent.Count==200);
            ZNetScene.instance.Ready=delegate{return true;};Tick(.51f);
            foreach(var data in objects.Values)Check("server-area completion releases waiting work without extra delay",data.GetOwner()==1);
            Check("area completion sends no additional proof queries",sent.Count==200&&GridCacheCount()==0);
            Console.WriteLine("WAIT WORK: 100 objects / 24 unchanged polls; extra peer checks=0; proof queries before due=0; server-ready completion resumes at next poll.");
        }
        finally {TooManyLags.GridHandoffs.Protocol.Stop();}
    }
}
