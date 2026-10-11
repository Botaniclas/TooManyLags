using System;
using System.Collections.Generic;
using TooManyLags.PlayerSimulation;
using UnityEngine;
internal static class NetworkModeTests
{
    private static int checks;
    private static void Check(bool yes, string message) { checks++; if(!yes) throw new Exception(message); }
    private static void Receive(ZNetPeer peer, Message message, Action<ZPackage> write)
    { ZPackage p=new ZPackage();p.Write((int)message);write(p);ZPackage batch=new ZPackage();batch.Write(1);batch.Write(p);batch.SetPos(0);peer.m_rpc.Handler(peer.m_rpc,batch); }
    public static void Main()
    {
        try
        {
            NativeLatency.Bind(typeof(FiresGhettoNetworkMod.ConnectionEcho).Assembly);
            ZNet.instance=new ZNet { Server=true,Dedicated=true };ZDOMan.Session=999;
            Plugin.ServerMode=true;Plugin.Network=new Network();Plugin.Local=new LocalAgent();Plugin.Controller=new AuthorityController();
            var peer=new ZNetPeer { m_uid=22,m_characterID=new ZDOID(2) };ZNet.instance.Peers.Add(peer);Plugin.Network.Register(peer);
            Check(Plugin.Network.Present(22) && !Plugin.Network.Capable(22),"FiresGhettoNetworking-only client has no advanced handoff capability");
            Plugin.Network.Poll(0);Check(Plugin.Network.Latency(22)==70 && peer.m_rpc.Sent.Count==0,"Existing FiresGhettoNetworking RTT reused without ping RPCs");
            FiresGhettoNetworkMod.FgnVersionGate.Matched=false;
            Check(!Plugin.Network.Present(22),"Unmatched networking peer cannot receive ownership");
            FiresGhettoNetworkMod.FgnVersionGate.Matched=true;
            Receive(peer,Message.Hello,delegate(ZPackage p){p.Write(Network.Protocol);p.Write(true);});
            Check(Plugin.Network.Capable(22) && Plugin.Network.MobCapable(22),"Current client handshake enables guarded handoffs");
            ZPackage welcome=new ZPackage(peer.m_rpc.Sent[0].GetArray());welcome.ReadInt();ZPackage item=new ZPackage();welcome.ReadPackage(ref item);
            Check((Message)item.ReadInt()==Message.Welcome && item.ReadInt()==Network.Protocol && item.ReadBool() && item.ReadBool(),"Server announces player-simulation mode");
            Check(!Plugin.Network.Capable(999) && !Plugin.Network.Present(999),"Dedicated server cannot be selected as a simulated owner");
            ZNet.instance.Server=false;ZNet.instance.ServerPeer=peer;Plugin.Network=new Network();Plugin.Network.Register(peer);Plugin.Local=null;Plugin.Activations=0;
            Check(Plugin.Network.NeedsPoll,"Client handshake initially pending");
            Receive(peer,Message.Welcome,delegate(ZPackage p){p.Write(Network.Protocol);p.Write(true);p.Write(false);});
            Check(!Plugin.Network.Confirmed && !Plugin.Network.NeedsPoll && Plugin.Activations==0 && Plugin.Local==null,"Normal server mode leaves extra runtime dormant");
            Receive(peer,Message.Policy,delegate(ZPackage p){p.Write(true);});Check(Plugin.Local==null,"Normal server mode cannot activate mob monitoring through a policy packet");
            Receive(peer,Message.Welcome,delegate(ZPackage p){p.Write(Network.Protocol);p.Write(true);p.Write(true);});
            Check(Plugin.Network.Confirmed && Plugin.Network.NeedsPoll && Plugin.Activations==1 && Plugin.Local.MobsEnabled,"Server mode activates full local protection independent of client config");
            var other=new ZNetPeer { m_uid=33 };ZNet.instance.Peers.Add(other);Plugin.Network.Register(other);
            Receive(other,Message.Welcome,delegate(ZPackage p){p.Write(Network.Protocol);p.Write(true);p.Write(false);});
            Check(Plugin.Network.Confirmed,"Another peer cannot override the server's mode");
            Plugin.Network.Reset();Check(!Plugin.Network.Confirmed && Plugin.Network.NeedsPoll,"Disconnect clears confirmation; next server can select a new mode");
            Console.WriteLine("PASS: "+checks+" player-simulation network assertions: mode negotiation, dormant normal mode, existing RTT, dedicated-server exclusion and peer origin.");
        }
        catch(Exception error) { Console.Error.WriteLine(error);Environment.Exit(1); }
    }
}
namespace FiresGhettoNetworkMod
{
    internal static class ConnectionEcho { public static bool TryRtt(ZNetPeer peer,out float value) { value=70;return true; } }
    internal static class LinkController { public static int PingMs(ZNetPeer peer) { return 90; } }
    internal static class FgnVersionGate { internal static bool Matched=true;public static bool IsMatched(ZNetPeer peer) { return Matched; } }
}
