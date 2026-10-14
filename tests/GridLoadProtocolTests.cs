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
    private static ZPackage GridClientReply(ZNetPeer serverPeer,ZPackage query,bool areaReady)
    {
        bool dedicated=ZNet.instance.Dedicated;long session=ZDOMan.SessionID;var previous=ZNet.instance.ServerPeer;
        var oldReady=ZNetScene.instance.Ready;var replies=GridCapture(serverPeer);
        try
        {
            ZNet.instance.Dedicated=false;ZDOMan.SessionID=1;ZNet.instance.ServerPeer=serverPeer;
            ZNetScene.instance.Ready=delegate{return areaReady;};Time.frameCount++;
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
    private static void DestinationLoadProtocol()
    {
        int initial=checks;Plugin.Warnings.Clear();
        Fresh(Kinds.Doors|Kinds.Containers,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true,checkIntervalSeconds:.05f);
        var first=Peer(1);var second=Peer(2);var serverPeer=new ZNetPeer{Uid=99,m_server=true};ZNet.instance.Peers.Add(serverPeer);
        var door=Object(typeof(Door),1,1,99);door.DataRevision=3;var view=ZNetScene.instance.FindInstance(door);
        GridStart();
        try
        {
            GridHello(first,true);GridHello(second,true);var sent=GridCapture(first);
            ZNetScene.instance.Ready=delegate{return false;};Tick(0);MovePeer(second,1024);Tick(.1f);
            Check("server-area gap queries only destination and retains owner",sent.Count==1&&door.GetOwner()==99);
            long token=GridToken(sent[0]);
            GridReceive(first,GridReply(door,token+1,true));Tick(.2f);
            GridReceive(second,GridReply(door,token,true));Tick(.3f);
            Check("wrong nonce and wrong authenticated peer cannot authorize",door.GetOwner()==99&&sent.Count==1);
            Tick(.7f);Check("slow response remains valid across retries",GridToken(sent[1])==token);
            Check("client-area gap rejects pre-owner proof",!GridReplyAvailable(GridClientReply(serverPeer,sent[1],false)));
            door.DataRevision=2;
            Check("recipient state below captured minimum cannot prove ready",!GridReplyAvailable(GridClientReply(serverPeer,sent[1],true)));
            door.DataRevision=4;
            Check("current data can satisfy captured minimum without restarting token",GridReplyAvailable(GridClientReply(serverPeer,sent[1],true)));
            view.gameObject.activeInHierarchy=false;
            Check("inactive exact object cannot prove ready",!GridReplyAvailable(GridClientReply(serverPeer,sent[1],true)));view.gameObject.activeInHierarchy=true;
            Vector3 pos=door.Position;door.Position=new Vector3(2,0,1);
            Check("moved object cannot answer old position proof",!GridReplyAvailable(GridClientReply(serverPeer,sent[1],true)));door.Position=pos;
            ushort rev=door.OwnerRevision;door.OwnerRevision++;
            Check("changed owner revision cannot answer old proof",!GridReplyAvailable(GridClientReply(serverPeer,sent[1],true)));door.OwnerRevision=rev;
            door.SetOwnerInternal(1);
            Check("pre-owner proof requires authenticated server ownership",!GridReplyAvailable(GridClientReply(serverPeer,sent[1],true)));door.SetOwnerInternal(99);
            GridReceive(first,GridClientReply(serverPeer,sent[1],true));
            // The retained request wakes the next frame; it never writes inside the RPC.
            Check("proof reply alone makes no owner write",door.GetOwner()==99);
            Time.unscaledTime=.71f;controller.Poll(.71f);
            Check("valid destination world proof resumes exact static handoff",door.GetOwner()==1&&GridCacheCount()==0);
            int calls=sent.Count;for(int i=0;i<1000;i++)Tick(1+i*.05f);
            Check("settled object creates no repeated load queries",sent.Count==calls);
            uint minimum;Check("post-write delivery still requires new recipient revision",controller.TryRecipientState(door,1,out minimum));
            Check("old proof cannot satisfy post-owner readiness",!TooManyLags.GridHandoffs.Protocol.Ready(ZDOMan.instance,1,door));
            GridReceive(first,GridReply(door,token,true));
            Check("old pre-owner nonce does not unlock newer delivery proof",!TooManyLags.GridHandoffs.Protocol.Ready(ZDOMan.instance,1,door));
            controller.Unregister(view);Check("unload removes all per-object proof state",GridCacheCount()==0);
        }
        finally {TooManyLags.GridHandoffs.Protocol.Stop();}

        // Existing/legacy clients retain the complete old area guard, without
        // sending a new message they cannot understand or requiring a version.
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        first=Peer(1);door=Object(typeof(Door),1,1,99);GridStart();
        try
        {
            GridHello(first,false);var sent=GridCapture(first);ZNetScene.instance.Ready=delegate{return false;};Tick(0);Tick(1);
            Check("legacy client keeps area guard without new RPC",door.GetOwner()==99&&sent.Count==0);
            ZNetScene.instance.Ready=delegate{return true;};Tick(2);
            Check("normal ready path creates no load-proof work",door.GetOwner()==1&&sent.Count==0&&GridCacheCount()==0);
        }
        finally {TooManyLags.GridHandoffs.Protocol.Stop();}

        // The alternative proof does not apply to source-client commits,
        // physics actors, local open use, or a server destination.
        Fresh(Kinds.Doors|Kinds.Containers|Kinds.TamedAnimals|Kinds.Vehicles,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        first=Peer(1);second=Peer(2,64);door=Object(typeof(Door),1,1,2);
        var chest=Object(typeof(Container),2,1,99);var chestView=ZNetScene.instance.FindInstance(chest);chestView.GetComponent<Container>().InUse=true;
        var moving=Object(typeof(Door),1,2,99);var movingView=ZNetScene.instance.FindInstance(moving);
        controller.Unregister(movingView);movingView.gameObject.Add(new Rigidbody());controller.Register(moving,movingView);
        var animal=Object(typeof(Character),2,2,99);animal.Tamed=true;controller.Unregister(ZNetScene.instance.FindInstance(animal));controller.Register(animal,ZNetScene.instance.FindInstance(animal));
        GridStart();
        try
        {
            GridHello(first,true);GridHello(second,true);var sent=GridCapture(first);ZNetScene.instance.Ready=delegate{return false;};Tick(0);Tick(1);
            Check("source client, rigidbody, tame and local open chest keep original guard",door.GetOwner()==2&&moving.GetOwner()==99&&animal.GetOwner()==99&&chest.GetOwner()==99&&sent.Count==0);
            chestView.GetComponent<Container>().InUse=false;Tick(2);
            Check("closing chest enables only its pending load query",sent.Count==1&&chest.GetOwner()==99);
            long token=GridToken(sent[0]);chest.OwnerRevision++;Tick(3);
            GridReceive(first,GridReply(chest,token,true));Tick(3.1f);
            Check("old server revision cannot unlock changed snapshot",chest.GetOwner()==99);
            MovePeer(first,4);Tick(3.2f);var last=sent[sent.Count-1];long nextToken=GridToken(last);
            GridReceive(first,GridPacket(8,delegate(ZPackage p){p.Write(chest.m_uid);p.Write(nextToken);}));Tick(3.3f);
            Check("load-loss notification leaves transfer guarded",chest.GetOwner()==99);
            controller.Unregister(chestView);Check("unload also removes outstanding pre-owner proof",GridCacheCount()==0);
            GridReceive(first,GridReply(chest,nextToken,true));Check("reply cannot recreate unloaded object proof",GridCacheCount()==0);
            controller.Register(chest,chestView);Tick(3.4f);
            ZNet.instance.Disconnect(first);Check("disconnect retires destination proof cache",GridCacheCount()==0);
            GridReceive(first,GridReply(chest,nextToken,true));Check("removed transport cannot restore proof",GridCacheCount()==0);
        }
        finally {TooManyLags.GridHandoffs.Protocol.Stop();}
        Fresh(Kinds.Doors,defaultInteractives:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        first=Peer(1);second=Peer(2);door=Object(typeof(Door),1,1,1);GridStart();
        try
        {
            GridHello(first,true);GridHello(second,true);var a=GridCapture(first);var b=GridCapture(second);
            ZNetScene.instance.Ready=delegate{return false;};Tick(0);Tick(1);
            Check("server destination retains its own full area check",door.GetOwner()==1&&a.Count==0&&b.Count==0);
        }
        finally {TooManyLags.GridHandoffs.Protocol.Stop();}
        Check("valid protocol exercise logs no rejection",Plugin.Warnings.Count==0);
        Check("stop clears destination hook and caches",AuthorityController.DestinationLoaded==null&&GridCacheCount()==0);
        DestinationStateTraffic();
        DestinationGrantCancellation();
        NormalDestinationWork();
        DeferredStartupProof();
        WaitingDestinationWork();
        Console.WriteLine("PASS: "+(checks-initial)+" actual grid-protocol/controller assertions: authenticated object/world proof, stable retries, normal/legacy zero-RPC path, open-use/physics guards and lifecycle cleanup.");
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
