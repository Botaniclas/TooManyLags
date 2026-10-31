using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static class MobObserverTests
{
    private const string Cap="TooManyLags_ObserverCapability",Transition="TooManyLags_ObserverTransition";
    private static int checks;private static uint sequence;
    private sealed class Sent {internal string Method;internal object[] Args;}
    private static readonly Dictionary<ZRpc,List<Sent>> sends=new Dictionary<ZRpc,List<Sent>>();
    private static void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
    private static IDictionary Pending{get{return (IDictionary)typeof(MobObserverHandoff).GetField("pending",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);}}
    private static IList Tracks{get{return (IList)typeof(MobHandoffVisual).GetField("tracks",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);}}
    private static ZDO Data(Humanoid actor){return actor.GetComponent<ZNetView>().Data;}
    private static Humanoid Mob(long owner,uint revision=7)
    {
        GameObject root=new GameObject();Humanoid actor=root.Add<Humanoid>();actor.Visual=new GameObject();actor.Visual.transform.parent=root.transform;
        root.transform.Children.Add(actor.Visual.transform);actor.Velocity=new Vector3(2,0,0);actor.MoveDir=new Vector3(1,0,0);
        ZNetView view=root.Add<ZNetView>();view.Data=new ZDO{m_uid=new ZDOID{UserID=100,ID=++sequence},Owner=owner,OwnerRevision=revision};
        root.Add<BaseAI>();ZDOMan.instance.All.Add(view.Data.m_uid,view.Data);ZNetScene.instance.All.Add(view.Data,view);return actor;
    }
    private static ZNetPeer Peer(long uid,bool capable=true,bool server=false)
    {
        ZNetPeer peer=new ZNetPeer{m_uid=uid,m_server=server};List<Sent> list=new List<Sent>();sends[peer.m_rpc]=list;
        peer.m_rpc.Send=delegate(string method,object[] args)
        {
            object[] copy=(object[])args.Clone();for(int i=0;i<copy.Length;i++)if(copy[i] is ZPackage)copy[i]=new ZPackage(((ZPackage)copy[i]).Bytes());
            list.Add(new Sent{Method=method,Args=copy});
        };
        ZNet.instance.Peers.Add(uid,peer);if(server)ZNet.instance.Peer=peer;
        if(Harmony.GetPatchInfo(typeof(MobFlightHandoff).GetMethod("Transfer",BindingFlags.Static|BindingFlags.NonPublic))!=null)
        {ZNet.instance.OnNewConnection(peer);if(capable)peer.m_rpc.Deliver(Cap,-1);}
        return peer;
    }
    private static void Start(bool server=false,long local=3,bool enabled=true)
    {
        MobObserverHandoff.Stop();MobHandoffVisual.Stop();sends.Clear();ZDOMan.SessionID=local;
        ZNet.instance=new ZNet{Server=server};ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();
        Time.time=0;Time.realtimeSinceStartupAsDouble=0;Application.isBatchMode=false;Plugin.Warnings=0;
        MobFlightHandoff.Reject=MobFlightHandoff.Throw=MobFlightHandoff.ChangeAgain=false;
        MobFlightHandoff.AdvanceData=0;
        if(!server)Peer(10,true,true);
        MobHandoffVisual.Start(Assembly.GetExecutingAssembly(),enabled);MobObserverHandoff.Start(enabled);
        if(!server&&enabled)ZNet.instance.Peer.m_rpc.Deliver(Cap,-1);
        foreach(List<Sent> list in sends.Values)list.Clear();
    }
    private static ZPackage Packet(ZDO data,long source=1,long destination=2,uint revision=7,int version=1,float window=.1f,byte flags=0,uint? cutoff=null,float rotationFactor=.3f)
    {
        ZPackage p=new ZPackage();p.Write((byte)version);p.Write(source);p.Write(destination);p.Write(revision);p.Write(cutoff??data.DataRevision);p.Write(data.GetPrefab());
        p.Write(ZDOID.None);p.Write(new Vector3(2,0,0));p.Write(new Vector3(1,0,0));p.Write(flags);p.Write(window);p.Write(rotationFactor);return new ZPackage(p.Bytes());
    }
    private static void Receive(ZNetPeer peer,ZDO data,ZPackage packet){peer.m_rpc.Deliver(Transition,data.m_uid,packet);}
    private static int Count(ZNetPeer peer){int count=0;foreach(Sent s in sends[peer.m_rpc])if(s.Method==Transition)count++;return count;}
    private static Sent Last(ZNetPeer peer){List<Sent> all=sends[peer.m_rpc];for(int i=all.Count-1;i>=0;i--)if(all[i].Method==Transition)return all[i];return null;}
    private static void Pump(double time){Time.realtimeSinceStartupAsDouble=time;MobObserverHandoff.Tick();}
    public static int Main()
    {
        try{Run();PreviewStream();Console.WriteLine("PASS: "+checks+" production observer transport assertions; native-shaped RPC/epoch/visual fixtures, not real network or Unity rendering.");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{MobObserverHandoff.Stop();MobHandoffVisual.Stop();}
    }
    private static void Run()
    {
        Start(false,1,false);Humanoid actor=Mob(1);MobFlightHandoff.Transfer(Data(actor),2);
        Check(Count(ZNet.instance.Peer)==0&&Pending.Count==0,"disabled bridge sends nothing and retains no pending references");
        Check(Harmony.GetPatchInfo(typeof(MobFlightHandoff).GetMethod("Transfer",BindingFlags.Static|BindingFlags.NonPublic))==null,"disabled bridge installs no common commit hook");

        Start(false,1);actor=Mob(1);ZDO data=Data(actor);ZNetPeer server=ZNet.instance.Peer;
        MobFlightHandoff.Reject=true;MobFlightHandoff.Transfer(data,2);Check(Count(server)==0&&data.OwnerWrites==0,"refused native handoff emits no descriptor");
        MobFlightHandoff.Reject=false;MobFlightHandoff.Throw=true;try{MobFlightHandoff.Transfer(data,2);}catch(InvalidOperationException){}
        Check(Count(server)==0&&data.OwnerWrites==0,"failed checkpoint before native commit emits no descriptor");
        MobFlightHandoff.Throw=false;actor.GetComponent<BaseAI>().Fresh=true;
        Humanoid target=Mob(9);actor.GetComponent<BaseAI>().Target=target;
        Vector3 root=actor.transform.position;MobFlightHandoff.Transfer(data,2);
        Check(data.GetOwner()==2&&data.OwnerWrites==1&&Count(server)==1,"source emits exactly once after actual native commit");
        Sent descriptor=Last(server);Check(((ZPackage)descriptor.Args[1]).Size()==74,"descriptor payload is 74 bytes, plus outer ID and native RPC framing");
        Check(actor.transform.position.sqrMagnitude==root.sqrMagnitude&&data.DataRevision==0,"descriptor capture never writes root, physics, native data or extra owner changes");
        ZPackage parsed=new ZPackage(((ZPackage)descriptor.Args[1]).Bytes());parsed.ReadByte();parsed.ReadLong();parsed.ReadLong();parsed.ReadUInt();parsed.ReadUInt();parsed.ReadInt();
        Check(parsed.ReadZDOID()==target.GetZDOID(),"fresh real target is captured for read-only spectator turning");
        MobFlightHandoff.Transfer(data,2);Check(Count(server)==1,"same-owner native call cannot emit a transition");

        Start(false,1);actor=Mob(1);server=ZNet.instance.Peer;MobFlightHandoff.ChangeAgain=true;MobFlightHandoff.Transfer(Data(actor),2);
        Check(Count(server)==0,"a newer ownership epoch at return suppresses an obsolete descriptor");
        Start(false,1);actor=Mob(1);server=ZNet.instance.Peer;server.Ready=false;MobFlightHandoff.Transfer(Data(actor),2);
        Check(Count(server)==0,"unready server has no optional traffic");
        Start(false,1);actor=Mob(1);server=ZNet.instance.Peer;server.m_socket.Fail=true;MobFlightHandoff.Transfer(Data(actor),2);
        Check(Count(server)==1&&Plugin.Warnings==0,"unsupported latency access falls back without affecting gameplay or reporting an error");
        Start(false,1);actor=Mob(1);server=ZNet.instance.Peer;actor.GetComponent<BaseAI>().m_randomFly=true;MobFlightHandoff.AdvanceData=2;MobFlightHandoff.Transfer(Data(actor),2);
        parsed=new ZPackage(((ZPackage)Last(server).Args[1]).Bytes());parsed.ReadByte();parsed.ReadLong();parsed.ReadLong();parsed.ReadUInt();
        Check(parsed.ReadUInt()==2,"source descriptor captures final native cutoff after common-transfer SyncNow rather than prefix state");
        parsed.ReadInt();parsed.ReadZDOID();parsed.ReadVector3();parsed.ReadVector3();Check(parsed.ReadByte()==0,"grounded random-flight species remain ground presentation until native flying flag is set");
        Start(false,1);actor=Mob(1);server=ZNet.instance.Peer;actor.Flying=true;actor.currentRotSpeedFactor=.47f;MobFlightHandoff.Transfer(Data(actor),2);
        parsed=new ZPackage(((ZPackage)Last(server).Args[1]).Bytes());parsed.ReadByte();parsed.ReadLong();parsed.ReadLong();parsed.ReadUInt();parsed.ReadUInt();parsed.ReadInt();parsed.ReadZDOID();parsed.ReadVector3();parsed.ReadVector3();parsed.ReadByte();parsed.ReadSingle();
        Check(Math.Abs(parsed.ReadSingle()-.47f)<.00001f,"descriptor carries actual source flight rotation accumulator");

        Start(true,10);ZNetPeer a=Peer(1),b=Peer(2),c=Peer(3),old=Peer(4,false),far=Peer(5);far.Reference=new Vector3(2000,0,0);
        actor=Mob(1);data=Data(actor);Receive(a,data,Packet(data));
        Check(Pending.Count==1&&Count(c)==0,"server buffers authenticated descriptor while source native record has not committed");
        data.SetOwner(2);ZDOMan.instance.RPC_ZDOData(delegate{});
        Check(Pending.Count==0&&Count(c)==1&&Count(a)==0&&Count(b)==0,"server relays only after matching canonical epoch, excluding source and new owner");
        Check(Count(old)==0&&Count(far)==0,"absent capability and out-of-area peers get no transition traffic");
        Receive(a,data,Packet(data));Check(Count(c)==1&&Pending.Count==0,"duplicate committed source descriptor cannot relay twice");
        Check(data.OwnerWrites==1&&Tracks.Count==0,"server relay never simulates, changes owner or creates render tracks");

        Start(true,10);a=Peer(1);c=Peer(3);actor=Mob(1);data=Data(actor);data.SetOwner(2);Receive(a,data,Packet(data));
        Check(Count(c)==1&&Pending.Count==0,"descriptor arriving after native ownership record is still authenticated and relayed");
        Receive(c,data,Packet(data));Check(Count(c)==1,"third party cannot forge the old source");
        data.SetOwner(1);Receive(a,data,Packet(data));Check(Count(c)==1&&Pending.Count==0,"older epoch cannot be resurrected after subsequent owner changes");

        Start(true,10);b=Peer(2);c=Peer(3);actor=Mob(10);data=Data(actor);MobFlightHandoff.Transfer(data,2);
        Check(Count(c)==1&&Count(b)==0,"server source uses same canonical relay and excludes new owner");
        Start(true,10);b=Peer(2);actor=Mob(10);MobFlightHandoff.Transfer(Data(actor),2);Check(Count(b)==0,"server with no capable spectators performs no optional source capture or traffic");

        Start();server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);Receive(server,data,Packet(data));
        Check(Pending.Count==1&&Tracks.Count==0,"spectator waits for its own native owner record");
        data.SetOwner(2);ZDOMan.instance.RPC_ZDOData(delegate{});
        Check(Pending.Count==0&&Tracks.Count==1,"spectator arms exactly one visual track after matching local commit");
        int writes=data.OwnerWrites;Vector3 position=actor.transform.position;Receive(server,data,Packet(data));MobHandoffVisual.Tick();
        Check(Tracks.Count==1&&data.OwnerWrites==writes&&actor.transform.position.sqrMagnitude==position.sqrMagnitude,"duplicate spectator packet cannot restart presentation or modify gameplay");
        MobHandoffVisual.Stop();Receive(server,data,Packet(data));Check(Tracks.Count==0,"disabled render backend remains native despite late relay");

        Start();server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);actor.Flying=true;actor.currentRotSpeedFactor=0f;data.SetOwner(2);
        Receive(server,data,Packet(data,flags:1,rotationFactor:.47f));
        Check(Tracks.Count==1&&Math.Abs((float)Tracks[0].GetType().GetField("FlightFactor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Tracks[0])-.47f)<.00001f,"flying spectator uses transferred source accumulator, not a stale zero on its replica");

        Start();server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);ZNetView view=actor.GetComponent<ZNetView>();ZNetScene.instance.All.Remove(data);
        data.SetOwner(2);Receive(server,data,Packet(data));Check(Pending.Count==1,"native owner record preceding local actor load retains bounded descriptor");
        ZNetScene.instance.All.Add(data,view);actor.GetComponent<BaseAI>().Awake();Check(Pending.Count==0&&Tracks.Count==1,"native AI load wakes matching spectator work without an NPC scan");

        Start();server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);ZDOMan.instance.All.Remove(data.m_uid);
        Receive(server,data,Packet(data));Check(Pending.Count==1,"trusted relay can arrive before spectator knows the native ZDO");
        ZDOMan.instance.All.Add(data.m_uid,data);data.SetOwner(2);Pump(.1);Check(Tracks.Count==1&&Pending.Count==0,"descriptor preceding local ZDO loads only after exact replicated epoch");

        Start();server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);data.SetOwner(2);data.DataRevision=4;
        Receive(server,data,Packet(data,cutoff:5));Check(Pending.Count==1&&Tracks.Count==0,"an ownership header preceding its final native data snapshot waits for the cutoff");
        data.DataRevision=5;ZDOMan.instance.RPC_ZDOData(delegate{});Check(Pending.Count==0&&Tracks.Count==1,"exact final source snapshot is valid and arms observer preview");
        Start();server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);data.SetOwner(2);data.DataRevision=6;
        Receive(server,data,Packet(data,cutoff:5));Check(Pending.Count==0&&Tracks.Count==0,"newer native simulation suppresses late stale-motion preview");
        Receive(server,data,Packet(data,cutoff:5));Check(Pending.Count==0&&Tracks.Count==0,"suppressed stale descriptors are fenced against duplicate retry");
        Start(true,10);a=Peer(1);c=Peer(3);actor=Mob(1);data=Data(actor);data.SetOwner(2);data.DataRevision=6;
        Receive(a,data,Packet(data,cutoff:5));Check(Count(c)==0&&Pending.Count==0,"server also suppresses a source descriptor after newer canonical simulation data arrived");

        Start(false,1);server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);Receive(server,data,Packet(data));Check(Pending.Count==0,"source is excluded even if server relay is misaddressed");
        Start(false,2);server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);Receive(server,data,Packet(data));Check(Pending.Count==0,"new owner never receives spectator prediction");
        Start();server=ZNet.instance.Peer;a=Peer(1);actor=Mob(1);data=Data(actor);Receive(a,data,Packet(data));Check(Pending.Count==0,"client accepts only the current authenticated server RPC");

        Start();server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);
        Receive(server,data,Packet(data,version:2));Receive(server,data,Packet(data,window:Single.NaN));Receive(server,data,Packet(data,window:.6f));Receive(server,data,Packet(data,flags:2));
        Check(Pending.Count==0,"wrong protocol, invalid latency and unknown flag bits are rejected without changing native state");
        Receive(server,data,Packet(data,rotationFactor:Single.NaN));Receive(server,data,Packet(data,rotationFactor:-.1f));Receive(server,data,Packet(data,rotationFactor:1.1f));
        Check(Pending.Count==0,"nonfinite and out-of-range source rotation factors cannot enter spectator presentation");
        Receive(server,data,Packet(data,revision:8));Check(Pending.Count==0,"future native epoch is not guessed for known objects");
        ZPackage extra=Packet(data);extra=new ZPackage(Append(extra.Bytes(),new byte[1]));Receive(server,data,extra);Check(Pending.Count==0,"extra payload data is rejected rather than silently changing protocol");
        Receive(server,data,Packet(data));Pump(5.001);Check(Pending.Count==0&&Tracks.Count==0,"pending state expires after five seconds without applying a late bridge");

        Start();server=ZNet.instance.Peer;for(int i=0;i<300;i++){actor=Mob(1);data=Data(actor);Receive(server,data,Packet(data));}
        Check(Pending.Count==256,"bounded pending queue cannot retain more than 256 mobs");
        Pump(5.001);Check(Pending.Count==0,"one bounded cleanup pass releases every expired descriptor");
        actor=Mob(1);data=Data(actor);Receive(server,data,Packet(data));ZDOMan.instance=new ZDOMan();Pump(5.1);Check(Pending.Count==0,"world replacement releases pending data and duplicate fences");
        actor=Mob(1);data=Data(actor);Receive(server,data,Packet(data));ZNet.instance.Shutdown();Check(Pending.Count==0,"native shutdown releases all retained references");
        MobObserverHandoff.Stop();Check(Pending.Count==0,"explicit stop is idempotent and empty");
        Check(Plugin.Warnings==0,"normal protocol, rejection, load and expiry paths produce no exceptions");
    }
    private static byte[] Append(byte[] a,byte[] b){byte[] all=new byte[a.Length+b.Length];Array.Copy(a,all,a.Length);Array.Copy(b,0,all,a.Length,b.Length);return all;}
    private const string Preview="TooManyLags_ObserverPreview";
    private static int PreviewCount(ZNetPeer peer){int count=0;foreach(Sent item in sends[peer.m_rpc])if(item.Method==Preview)count++;return count;}
    private static ZPackage PreviewPacket(uint frame=1,long source=1,long destination=2)
    {ZPackage p=new ZPackage();p.Write(source);p.Write(destination);p.Write(7U);p.Write(frame);p.Write(new Vector3(.1f,0,0));p.Write(5f);return new ZPackage(p.Bytes());}
    private static void PreviewStream()
    {
        Start(false,1);Humanoid actor=Mob(1);ZDO data=Data(actor);ZNetPeer server=ZNet.instance.Peer;MobFlightHandoff.Transfer(data,2);
        int writes=data.OwnerWrites;MobHandoffVisual.SourcePreview(data,new Vector3(.1f,0,0),5f);MobHandoffVisual.SourcePreview(data,new Vector3(.2f,0,0),6f);
        Check(PreviewCount(server)==1&&data.OwnerWrites==writes,"preview is not throttled or changes real ownership");
        Pump(.05);MobHandoffVisual.SourcePreview(data,new Vector3(.2f,0,0),6f);Check(PreviewCount(server)==2,"active source cannot send subsequent visual frame");
        Start(true,10);ZNetPeer old=Peer(1),dest=Peer(2),observer=Peer(3);actor=Mob(1);data=Data(actor);data.SetOwner(2);Receive(old,data,Packet(data));
        old.m_rpc.Deliver(Preview,data.m_uid,PreviewPacket());Check(PreviewCount(observer)==1&&PreviewCount(dest)==0&&PreviewCount(old)==0,"visual frame reaches wrong peers or is not relayed");
        old.m_rpc.Deliver(Preview,data.m_uid,PreviewPacket());Check(PreviewCount(observer)==1,"duplicate visual frame replayed");
        Pump(.06);observer.m_rpc.Deliver(Preview,data.m_uid,PreviewPacket(2));Check(PreviewCount(observer)==1,"spectator can impersonate old owner");
        Start();server=ZNet.instance.Peer;actor=Mob(1);data=Data(actor);data.SetOwner(2);Receive(server,data,Packet(data));
        Vector3 position=actor.transform.position;server.m_rpc.Deliver(Preview,data.m_uid,PreviewPacket());
        Check(Tracks.Count==1&&(bool)Tracks[0].GetType().GetField("RemoteFrame",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Tracks[0]),"received source preview did not reach local visual track");
        Check(actor.transform.position.Equals(position)&&data.GetOwner()==2&&data.OwnerWrites==1,"visual frame rewrites real position/owner");
        Pump(1.31);server.m_rpc.Deliver(Preview,data.m_uid,PreviewPacket(3));Check(data.OwnerWrites==1,"expired preview touched ownership");
    }
}
