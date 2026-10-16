using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using TooManyLagsStandalone;
using UnityEngine;
internal static class NetworkBoundaryTests
{
    private static int checks;
    private static void Check(bool value,string message) { checks++;if(!value)throw new Exception(message); }
    private static void Incoming(ZNetPeer peer,Message message,Action<ZPackage> payload)
    { var p=new ZPackage();p.Write((int)message);payload(p);var batch=new ZPackage();batch.Write(1);batch.Write(p);batch.SetPos(0);peer.m_rpc.Handler(peer.m_rpc,batch); }
    private static object Connection(Network n,ZNetPeer p)
    { var entries=(System.Collections.IDictionary)typeof(Network).GetField("connections",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(n);return entries[p.m_rpc]; }
    private static bool? LatestPolicy(ZNetPeer peer)
    {
        bool? result=null;
        foreach(ZPackage sent in peer.m_rpc.Sent) { var batch=new ZPackage(sent.GetArray());int count=batch.ReadInt();for(int i=0;i<count;i++) { var part=new ZPackage();batch.ReadPackage(ref part);if((Message)part.ReadInt()==Message.Policy)result=part.ReadBool(); } }
        return result;
    }
    public static void Main()
    {
        try
        {
            ZDOMan.Session=11;ZNet.instance=new ZNet { Server=true,Dedicated=false };Plugin.Local=new LocalAgent();Plugin.Network=new Network();Plugin.Controller=new AuthorityController();
            var root=new GameObject();var view=root.Add<ZNetView>();view.Data=new ZDO { m_uid=new ZDOID(1) };Player.m_localPlayer=root.Add<Player>();ZDOMan.instance.Objects[view.Data.m_uid]=view.Data;
            var peer=new ZNetPeer { m_uid=22,m_characterID=new ZDOID(2) };ZDOMan.instance.Objects[peer.m_characterID]=new ZDO { m_uid=peer.m_characterID,Owner=22 };ZNet.instance.Peers.Add(peer);Plugin.Network.Register(peer);
            Check(!Plugin.Network.Capable(22) && Plugin.Network.Present(22),"Vanilla remote client participates without supporting custom handoffs");Check(Plugin.Network.Capable(11) && Plugin.Network.Latency(11)==0,"Listen host is an eligible zero-hop player");
            var vanillaPlayers=new List<PlayerPeer>();Plugin.Network.Players(vanillaPlayers);Check(vanillaPlayers.Count==2,"Vanilla player joins the directional grid before any handshake");
            ZDO vanilla=ZDOMan.instance.GetZDO(peer.m_characterID);vanilla.Rotation=new Quaternion { eulerAngles=new Vector3 { y=90 } };vanilla.Set(CameraHeading.Key,6);
            var cameraWake=new CameraWake();
            Check(CameraHeading.Read(vanilla,22,false,cameraWake)==2 && cameraWake.Count==0,"Vanilla direction ignores stale camera data without tracking a camera subscription");
            Check(CameraHeading.Read(vanilla,22,true,cameraWake)==6 && cameraWake.Count==1,"Compatible client camera coverage overrides character rotation");
            vanilla.Set(CameraHeading.Key,8);Check(CameraHeading.Read(vanilla,22,true)==8,"Adjacent camera masks are valid coverage, not a missing sector");
            vanilla.Set(CameraHeading.Key,16);Check(CameraHeading.Read(vanilla,22,true)==16,"Predictive three-direction coverage is accepted");
            vanilla.Set(CameraHeading.Key,32);Check(CameraHeading.Read(vanilla,22,true)==32,"Red forecast coverage is accepted");
            vanilla.Set(CameraHeading.Key,56);Check(CameraHeading.Read(vanilla,22,true)==2,"Invalid/missing camera coverage restores character rotation");
            vanilla.Rotation=new Quaternion { eulerAngles=new Vector3 { y=20 } };Check(CameraHeading.Read(vanilla,22,false)==8,"Unmodified clients get the same overlap from body rotation");
            vanilla.Owner=33;vanilla.Set(CameraHeading.Key,6);Check(CameraHeading.Read(vanilla,22,true)==8,"Foreign-owned player data cannot supply a camera advisory");vanilla.Owner=22;
            var socket=new ZSteamSocket { Ping=70 };peer.m_rpc.Socket=socket;Time.unscaledTime=0;Plugin.Network.Poll(0);
            Check(Plugin.Network.Latency(22)==70 && peer.m_rpc.Sent.Count==0,"Vanilla RTT uses transport telemetry without sending an unknown RPC");
            Plugin.Network.Send(22,Message.Cancel,delegate(ZPackage p) { p.Write(1); });Plugin.Network.FlushAll();Check(peer.m_rpc.Sent.Count==0,"Custom transfer messages never reach vanilla clients");
            socket.Quality=0;Time.unscaledTime=14;int oldRank=Plugin.Network.RankVersion;Plugin.Network.Poll(14);
            Check(Single.IsInfinity(Plugin.Network.Latency(22)) && Plugin.Network.RankVersion>oldRank,"Expired vanilla RTT invalidates overlap ranks instead of becoming zero ping");
            ZNet.instance.Dedicated=true;Steamworks.SteamGameServerNetworkingSockets.Ping=110;
            Check(NativeLatency.Read(peer.m_rpc)==110,"Dedicated Steam telemetry uses the game-server interface");
            Steamworks.SteamGameServerNetworkingSockets.Quality=0;Check(Single.IsInfinity(NativeLatency.Read(peer.m_rpc)),"Unavailable dedicated metrics never imply zero latency");
            socket.Quality=1;Steamworks.SteamGameServerNetworkingSockets.Throw=true;Check(NativeLatency.Read(peer.m_rpc)==70,"Unavailable Steam game-server API falls back to valid native socket telemetry");Steamworks.SteamGameServerNetworkingSockets.Throw=false;
            Steamworks.SteamGameServerNetworkingSockets.Quality=1;socket.Connected=false;Check(Single.IsInfinity(NativeLatency.Read(peer.m_rpc)),"Disconnected socket cannot supply latency");socket.Connected=true;
            ZNet.instance.Dedicated=false;socket.Quality=1;socket.Throw=true;Check(Single.IsInfinity(NativeLatency.Read(peer.m_rpc)),"Optional telemetry failure preserves ownership operation");socket.Throw=false;Time.unscaledTime=0;
            Incoming(peer,Message.Hello,delegate(ZPackage p) { p.Write(Network.Protocol+1); });Check(!Plugin.Network.Capable(22),"Unknown protocol must retain vanilla");
            Incoming(peer,Message.Hello,delegate(ZPackage p) { p.Write(2);p.Write(true); });Check(!Plugin.Network.Capable(22),"Previous protocol remains connected using native-safe fallback");
            Incoming(peer,Message.Hello,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(false); });Check(Plugin.Network.Capable(22) && !Plugin.Network.MobCapable(22),"Client can opt out of mobs while retaining grid handoffs");
            Incoming(peer,Message.Hello,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(true); });Check(Plugin.Network.MobCapable(22),"Matching client advertises mob adoption support");
            Check(Plugin.Controller.CapabilityUpdates==1,"Only a new capability revisits previously excluded objects; repeated hello does not rescan");
            Plugin.Options.CameraOverlap=45;
            Incoming(peer,Message.Hello,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(true); });
            var cameraConnection=Connection(Plugin.Network,peer);
            Check((bool)cameraConnection.GetType().GetField("SupportsCameraConfig",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(cameraConnection),"Camera-angle negotiation does not change the ownership protocol");
            var lastBatch=new ZPackage(peer.m_rpc.Sent[peer.m_rpc.Sent.Count-1].GetArray());int replies=lastBatch.ReadInt();bool angleSent=false;
            for(int i=0;i<replies;i++) { var part=new ZPackage();lastBatch.ReadPackage(ref part);if((Message)part.ReadInt()!=Message.Welcome)continue;Check(part.ReadInt()==3 && part.ReadBool() && part.ReadBool(),"Welcome declares current protocol and server enable/mob flags");Check(part.ReadSingle()==.05f,"Welcome declares authoritative camera interval");angleSent=part.ReadSingle()==45f;Check(part.ReadSingle()==.1f && part.ReadBool() && part.GetPos()==part.Size(),"Welcome appends only the optional text-edit capability after prediction"); }
            Check(angleSent,"Negotiated welcome contains authoritative host angle");Plugin.Options.CameraOverlap=30;
            Incoming(peer,Message.Hello,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(true); });
            Check((bool)Connection(Plugin.Network,peer).GetType().GetField("SupportsCameraPrediction",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Connection(Plugin.Network,peer)),"new capability negotiates prediction without changing the ownership protocol");
            lastBatch=new ZPackage(peer.m_rpc.Sent[peer.m_rpc.Sent.Count-1].GetArray());replies=lastBatch.ReadInt();bool predictionSent=false;
            for(int i=0;i<replies;i++) { var part=new ZPackage();lastBatch.ReadPackage(ref part);if((Message)part.ReadInt()!=Message.Welcome)continue;part.ReadInt();part.ReadBool();part.ReadBool();part.ReadSingle();part.ReadSingle();predictionSent=part.ReadSingle()==.1f; }
            Check(predictionSent,"only predictive clients receive authoritative prediction time");
            var players=new List<PlayerPeer>();Plugin.Network.Players(players);Check(players.Count==2,"Listen host plus compatible client appear");
            view.Data.Set(ZDOVars.s_health,0);Plugin.Network.Players(players);Check(players.Count==1 && players[0].Uid==22,"Dead host cannot win ownership");view.Data.Set(ZDOVars.s_health,100);
            Time.unscaledTime=0;Plugin.Network.Poll(0);object connection=Connection(Plugin.Network,peer);var stamp=connection.GetType().GetField("Stamp",BindingFlags.NonPublic|BindingFlags.Instance);long first=(long)stamp.GetValue(connection);
            Plugin.Controller.MobPair=false;Plugin.Network.Poll(1);Check(LatestPolicy(peer)==false,"One capable mob owner does not enable unusable target reporting beside vanilla clients");
            Plugin.Controller.MobPair=true;Plugin.Network.Poll(1.1f);Check(LatestPolicy(peer)==true,"Mob reporting re-enables once a second suitable client is available");
            Time.unscaledTime=3;Plugin.Network.Poll(3);Check((long)stamp.GetValue(connection)==first,"A slow pending ping must not be overwritten every interval");
            long synthetic=Stopwatch.GetTimestamp()-(long)(Stopwatch.Frequency*.25);stamp.SetValue(connection,synthetic);Incoming(peer,Message.Pong,delegate(ZPackage p) { p.Write(synthetic); });Check(Plugin.Network.Latency(22)>=240 && Plugin.Network.Latency(22)<500,"Smoothed RTT uses matched monotonic echo");
            Time.unscaledTime=14;Check(Single.IsInfinity(Plugin.Network.Latency(22)),"Stale RTT becomes unknown");
            ZNet.instance.Dedicated=true;Player.m_localPlayer=null;Check(!Plugin.Network.Capable(11),"Dedicated coordinator is never a simulation recipient");Plugin.Network.Players(players);Check(players.Count==1 && players[0].Uid==22,"Dedicated host lists only player clients");
            ZNet.instance.Peers.Clear();Plugin.Network.Poll(15);Check(!Plugin.Network.Capable(22),"Disconnected peer state removed");
            // A remote client must not be able to command the listen host's source agent.
            ZNet.instance.Dedicated=false;ZNet.instance.Peers.Add(peer);Plugin.Network.Register(peer);Incoming(peer,Message.Hello,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(true); });
            var doorRoot=new GameObject { Kind=Kinds.Doors };var door=doorRoot.Add<ZNetView>();door.Data=new ZDO { m_uid=new ZDOID(4) };ZDOMan.instance.Objects[door.Data.m_uid]=door.Data;ZNetScene.instance.Views[door.Data.m_uid]=door;
            Incoming(peer,Message.Commit,delegate(ZPackage p) { p.Write(45L);p.Write(door.Data.m_uid);p.Write(11L);p.Write(22L);p.Write((ushort)0);p.Write((int)Kinds.Doors);p.Write(ZDOID.None); });Check(door.Data.Owner==11,"Only coordinator-origin commands may commit host ownership");
            ZNet.instance.Server=false;ZNet.instance.ServerPeer=peer;Plugin.Network.Reset();Plugin.Network.Register(peer);Check(!Plugin.Network.Confirmed,"Client begins unconfirmed");
            Incoming(peer,Message.Welcome,delegate(ZPackage p) { p.Write(2);p.Write(false); });Check(!Plugin.Network.Confirmed,"Old server protocol cannot enable the new local guard session");
            Incoming(peer,Message.Welcome,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(true);p.Write(false);p.Write(.05f);p.Write(30f);p.Write(0f); });Check(Plugin.Network.Confirmed && Plugin.SessionEnabled && !Plugin.Local.MobsEnabled,"Server session and mob toggle applied to client");
            Check(Plugin.CameraOverlap==30f,"Current welcome applies camera defaults");
            Incoming(peer,Message.Welcome,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(true);p.Write(false);p.Write(.1f);p.Write(42.5f);p.Write(0f); });
            Check(Plugin.CameraOverlap==42.5f,"Client uses host angle instead of its local config");
            Check(Plugin.CameraPrediction==0f && Plugin.CameraInterval==.1f,"Host may explicitly disable prediction and set camera interval");
            Incoming(peer,Message.Welcome,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(true);p.Write(false);p.Write(.1f);p.Write(42.5f);p.Write(.2f); });
            Check(Plugin.CameraPrediction==.2f,"client uses the host prediction time");
            Incoming(peer,Message.Welcome,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(true);p.Write(false);p.Write(.1f);p.Write(42.5f);p.Write(.6f); });
            Check(Plugin.CameraPrediction==.2f,"invalid prediction time cannot replace the current setting");
            Incoming(peer,Message.Welcome,delegate(ZPackage p) { p.Write(Network.Protocol);p.Write(true);p.Write(false);p.Write(.1f);p.Write(46f);p.Write(.2f); });
            Check(Plugin.CameraOverlap==42.5f,"Invalid host angle cannot replace the current setting");
            var stranger=new ZNetPeer { m_uid=33 };ZNet.instance.Peers.Add(stranger);Plugin.Network.Register(stranger);Incoming(stranger,Message.Commit,delegate(ZPackage p) { p.Write(46L);p.Write(door.Data.m_uid);p.Write(11L);p.Write(22L);p.Write((ushort)0);p.Write((int)Kinds.Doors);p.Write(ZDOID.None); });Check(door.Data.Owner==11,"Client rejects commands from another player");
            Console.WriteLine("PASS: "+checks+" production network boundary assertions: listen/dedicated host, handshake, RTT expiry, slow echoes, disconnects and command-origin checks.");
        }
        catch(Exception error) { Console.Error.WriteLine(error);Environment.ExitCode=1; }
    }
}
