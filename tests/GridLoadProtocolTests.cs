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
    private static ZPackage GridReply(ZDO data,long token,bool available)
    { return GridPacket(6,delegate(ZPackage p){p.Write(data.m_uid);p.Write(token);p.Write(available);}); }
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
            GridReceive(serverPeer,query);Check("authenticated server receives one client proof",replies.Count==1);return replies[0];
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
        Console.WriteLine("PASS: "+(checks-initial)+" actual grid-protocol/controller assertions: authenticated object/world proof, stable retries, normal/legacy zero-RPC path, open-use/physics guards and lifecycle cleanup.");
    }
}
