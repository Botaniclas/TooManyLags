using System;
using System.Reflection;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static void StaticGridCoverage()
    {
        int start=checks;
        Fresh(Kinds.Doors,statics:true,nativeSolo:true);Peer(1);
        var wall=Object(null,1,1,99);Tick(0);Tick(3);
        Check("static grid is opt-in; server-only statics stay on server",wall.GetOwner()==99&&controller.RequestNode(wall.m_uid,"RPC_Repair".GetStableHashCode())==null);
        foreach(bool keep in new[]{true,false})
        {
            Fresh(Kinds.Doors,statics:true,staticGrid:true,nativeSolo:true,keepOnExit:keep);
            var first=Peer(1);wall=Object(null,1,1,99);var outside=Object(null,100,1,99);Tick(0);Tick(2.1f);
            Check("a solo player receives nearby static grid objects without PUP",wall.GetOwner()==1&&outside.GetOwner()==99);
            MovePeer(first,48);Tick(3);Tick(6);
            Check("static grid exit follows selected CPU retention policy",wall.GetOwner()==(keep?1:99));
        }
        Fresh(Kinds.Doors,statics:true,staticGrid:true,nativeSolo:true);
        var a=Peer(1);var b=Peer(2,48);var left=Object(null,1,1,99);var right=Object(null,49,1,99);var middle=Object(null,25,1,99);Tick(0);Tick(2.1f);
        Check("separated players share static simulation in one native region",left.GetOwner()==1&&right.GetOwner()==2&&middle.GetOwner()==99);
        MovePeer(b,0);Tick(3);Tick(6);
        Check("overlapping static fields use server ownership",left.GetOwner()==99);
        MovePeer(a,48);Tick(7);Tick(10);
        Check("a remaining player can receive a static field after overlap",left.GetOwner()==2);
        int attempts=controller.ScheduledAttempts,scans=GameObject.Scans,lookups=GameObject.Lookups;
        for(int i=0;i<1000;i++)Tick(11+i*.05f);
        Check("settled static grid adds no idle object jobs or hierarchy work",attempts==controller.ScheduledAttempts&&scans==GameObject.Scans&&lookups==GameObject.Lookups);

        Fresh(Kinds.Doors,defaultInteractives:true,staticGrid:true,nativeSolo:true);Peer(1);
        wall=Object(null,1,1,7);var far=Object(null,100,1,7);var door=Object(typeof(Door),100,2,99);Tick(0);Tick(3);
        Check("static and interactive server defaults are independent",wall.GetOwner()==1&&far.GetOwner()==7&&door.GetOwner()==99);
        Check("interactive default does not rewrite a native static assignment",controller.NativeOwnerTarget(far,7)==7);

        Fresh(Kinds.Doors,statics:true,staticGrid:true,nativeSolo:true);Peer(1);
        var baked=Object(null);baked.Parked=true;var physics=Object(null);ZNetScene.instance.FindInstance(physics).gameObject.Add(new Rigidbody());
        var actor=Object(typeof(Character));Tick(0);Tick(3);
        Check("baked PUP originals remain outside static grid",baked.GetOwner()==7&&baked.Writes==0&&controller.Persistent(baked));
        Check("static grid excludes dynamic bodies and creatures",physics.GetOwner()==7&&actor.GetOwner()==7);
        baked.Parked=false;var restored=ZNetScene.instance.FindInstance(baked);controller.Register(baked,restored);Tick(4);Tick(7);
        Check("a restored ordinary piece can reenter the same static grid",baked.GetOwner()==1);
#if GRID_PROTOCOL
        StaticInitialProof();
#endif
        Console.WriteLine("PASS: "+(checks-start)+" optional static-grid assertions: per-type defaults, solo/shared regions, overlap, retention, no idle rescans, PUP exclusions/restoration and no PUP dependency.");
    }
#if GRID_PROTOCOL
    private static void StaticInitialProof()
    {
        Fresh(Kinds.None,statics:true,staticGrid:true,nativeSolo:true,cellSize:4,pattern:GridCoveragePattern.Rounded7x7,directional:true);
        var peer=Peer(1);var server=new ZNetPeer{Uid=99,m_server=true};ZNet.instance.Peers.Add(server);GridStart();
        try
        {
            HarvestHello(peer);Tick(0);var sent=GridCapture(peer);
            var template=new GameObject{name="static_initial",activeInHierarchy=false};template.Add(new ZNetView());template.Add(new Piece());template.Add(new WearNTear());
            var data=new ZDO{m_uid=new ZDOID{ID=next++},Hash=100+next,Position=new Vector3(1,0,1)};
            ZDOMan.instance.Objects.Add(data);ZNetScene.instance.Prefabs.Add(data.Hash,template);
            Check("native static assignment respects a settled nearby grid",controller.NativeOwnerTarget(data,1)==1);
            data.Position=new Vector3(100,0,1);
            Check("native static server default applies outside the grid before instantiation",controller.NativeOwnerTarget(data,1)==99);
            data.Position=new Vector3(1,0,1);
            data.SetOwner(99);controller.OwnerChanged(data);ZNetScene.instance.Ready=delegate{return false;};Tick(.1f);
            Check("static early grid uses existing recipient proof",controller.InitialObject(data)&&sent.Count==1);
            GridReceive(peer,InteractionProof(server,data,sent[0]));Tick(.2f);
            Check("a proven loaded static recipient transfers before server scene loading",data.GetOwner()==1&&!data.Created&&!ZNetScene.instance.FindInstance(data));
            var live=InteractionView(data);data.Created=true;ZNetScene.instance.Views.Add(data,live);controller.Register(data,live);int writes=data.Writes;Tick(.3f);
            Check("late static registration preserves settled owner",data.GetOwner()==1&&data.Writes==writes&&!controller.InitialObject(data));
            long session=ZDOMan.SessionID;bool dedicated=ZNet.instance.Dedicated;var previousServer=ZNet.instance.ServerPeer;
            try
            {
                ZDOMan.SessionID=1;ZNet.instance.Dedicated=false;ZNet.instance.ServerPeer=server;data.SetOwner(2);
                var receive=typeof(TooManyLags.GridHandoffs.Protocol).GetMethod("ReceiveRequest",BindingFlags.Static|BindingFlags.NonPublic);
                var request=new ZRoutedRpc.RoutedRPCData{m_targetPeerID=1,m_targetZDO=data.m_uid,m_senderPeerID=1,m_methodHash="RPC_Repair".GetStableHashCode(),m_parameters=new ZPackage()};
                var relayed=GridCapture(server);
                GridReceive(server,GridPacket(1,delegate(ZPackage p){p.Write(true);}));
                Check("older server without static-grid selection keeps native static RPCs",(bool)receive.Invoke(null,new object[]{request})&&relayed.Count==0);
                GridReceive(server,GridPacket(1,delegate(ZPackage p){p.Write(true);p.Write(false);}));
                Check("disabled optional static grid keeps native static RPCs",(bool)receive.Invoke(null,new object[]{request})&&relayed.Count==0);
                GridReceive(server,GridPacket(1,delegate(ZPackage p){p.Write(true);p.Write(true);}));
                Check("selected static grid relays stale-owner repair instead of dropping it",!(bool)receive.Invoke(null,new object[]{request})&&relayed.Count==1);
            }
            finally{ZDOMan.SessionID=session;ZNet.instance.Dedicated=dedicated;ZNet.instance.ServerPeer=previousServer;}
        }
        finally{TooManyLags.GridHandoffs.Protocol.Stop();}
    }
#endif
}
