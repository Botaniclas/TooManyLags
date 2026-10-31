using System;
using System.Collections.Generic;
using UnityEngine;
using TooManyLags;
using Object = System.Object;
internal static class TimingCacheChecks
{
    @@PEER@@
    private const string RateRpc="rate";
    private const double Lifetime=5;
    private const double ClockInterval=.05d;
    private static bool Started=true;
    private static ZNet network;
    private static float localRate=1,localAiRate=1,lastRateSimulation;
    private static double lastRateWall,nextRateAt;
    private static float localReady,localFirstProgress,localReadyRate,localReadyStep,localStartup;
    private static double localReadyAt;
    private static readonly Dictionary<long,PeerRate> rates=new Dictionary<long,PeerRate>();
    private static readonly PeerRate localTimingCache=new PeerRate();
    private static bool Trusted(ZNetPeer peer,ZRpc rpc){return peer!=null&&Object.ReferenceEquals(peer.m_rpc,rpc);}
    private static bool Capable(ZNetPeer peer){return peer!=null;}
    private static void Connected(ZNetPeer peer){}
    @@FRAME@@
    @@LOCAL@@
    @@FOR@@
    @@POLL@@
    @@READ@@
    private static int checks;
    private static void Check(bool valid,string message){++checks;if(!valid)throw new Exception(message);}
    private static ZPackage Packet(long source,float step,float ready)
    {
        var p=new ZPackage();p.Write(source);p.Write(1f);p.Write(1f);p.Write(step);p.Write(.008f);p.Write(ready);p.Write(.02f);p.Write(.03f);
        return new ZPackage(p.GetArray());
    }
    internal static void Run()
    {
        Time.realtimeSinceStartupAsDouble=10;Time.time=100;Time.smoothDeltaTime=.008f;
        network=new ZNet();var server=new ZNetPeer{m_uid=10,m_server=true};network.Peers.Add(server);
        PollRate();Check(server.m_rpc.Sends==1&&server.m_rpc.Last.Size()==36,"timing extended RPC format/cadence wrong");
        for(int i=1;i<=50;i++){Time.realtimeSinceStartupAsDouble=10+i*.004d;PollRate();}
        Check(server.m_rpc.Sends==1,"readiness metadata added per-frame sends");
        Check(Object.ReferenceEquals(LocalTiming(10.2),LocalTiming(10.3)),"local cache allocates a new entry per lookup");
        localReady=.07f;localFirstProgress=.06f;localReadyRate=1;localReadyStep=.02f;localReadyAt=10;localStartup=.04f;
        PeerRate fresh=LocalTiming(11);Check(fresh.Ready==.07f&&fresh.FirstProgress==.06f&&fresh.Startup==.04f,"fresh measured first-step/application readiness ignored");
        PeerRate stale=LocalTiming(13);Check(stale.Ready<.03f&&stale.FirstProgress==.02f&&stale.Startup==.05f,"expired readiness did not use current native/frame fallback");
        localRate=.7f;Time.realtimeSinceStartupAsDouble=13;
        PeerRate unknown=TimingFor(99);Check(unknown.Ratio==1&&LocalTiming(13).Ratio==.7f,"fallback mutated local cached CPU rate");
        localRate=1;network.Server=true;rates.Clear();var owner=new ZNetPeer{m_uid=2};var observer=new ZNetPeer{m_uid=3};network.Peers.Add(owner);network.Peers.Add(observer);
        ReceiveRate(owner,owner.m_rpc,Packet(2,.02f,.024f));
        Check(rates.Count==1&&rates[2].Step==.02f&&rates[2].Ready==.024f&&rates[2].Startup==.03f&&observer.m_rpc.Sends==1,"valid peer cache not stored/relayed");
        Check(Object.ReferenceEquals(TimingFor(2),rates[2]),"known-peer lookup copied/recomputed cached metadata");
        ReceiveRate(owner,owner.m_rpc,Packet(3,.02f,.024f));Check(rates.Count==1,"peer spoofed another timing cache");
        ReceiveRate(owner,owner.m_rpc,Packet(2,float.NaN,.024f));Check(rates[2].Step==.02f,"nonfinite native step replaced cache");
        ReceiveRate(owner,owner.m_rpc,Packet(2,.02f,0));Check(rates[2].Ready==.024f,"invalid readiness replaced cache");
        var old=new ZPackage();old.Write(2L);old.Write(1f);old.Write(1f);
        ReceiveRate(owner,owner.m_rpc,new ZPackage(old.GetArray()));Check(rates.Count==1,"old timing format accepted");
        Time.realtimeSinceStartupAsDouble=16;Check(!Object.ReferenceEquals(TimingFor(2),rates[2]),"expired peer cache used at handoff");
        network.Server=false;rates.Clear();ReceiveRate(server,server.m_rpc,Packet(2,.02f,.024f));
        Check(rates.Count==1,"server-relayed peer cache rejected");
        ReceiveRate(owner,owner.m_rpc,Packet(3,.02f,.024f));Check(rates.Count==1,"client accepted nonserver timing relay");
        Console.WriteLine("PASS: "+checks+" extracted production peer timing cache/RPC checks; existing0.5s broadcast, allocation-free local/cache-hit reads, expiry and trust checks.");
    }
}
