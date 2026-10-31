using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TooManyLags;
using UnityEngine;
internal static class MobSourceTests
{
    private const string Mode="TooManyLags_AI_SourceMode",Begin="TooManyLags_AI_SourceBegin",Cancel="TooManyLags_AI_SourceCancel";
    private static int checks; private static ZDO data; private static ZNetPeer source,destination,server;
    private static ZDOID target=new ZDOID{UserID=77,ID=2}; private static List<string> order; private static byte[] beginBytes; private static bool safe;
    private static void Check(bool value,string text){checks++;if(!value)throw new Exception(text);}
    private static IDictionary Map(){return (IDictionary)typeof(MobSourceHandoff).GetField("started",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);}
    private static void New(bool host)
    {
        MobSourceHandoff.Stop();Time.realtimeSinceStartupAsDouble=0;safe=true;order=new List<string>();beginBytes=null;
        ZDOMan.instance=new ZDOMan();ZDOMan.Session=host?100:1;ZNet.instance=new ZNet{Server=host};
        data=new ZDO{m_uid=new ZDOID{UserID=55,ID=9}};ZDOMan.instance.Values[data.m_uid]=data;
        source=new ZNetPeer{m_uid=1};destination=new ZNetPeer{m_uid=2};server=new ZNetPeer{m_uid=100,m_server=true};
        ZNet.instance.Peers.Add(host?source:server);if(host)ZNet.instance.Peers.Add(destination);
        server.m_rpc.Send=delegate(string name,object[] args){order.Add(name);if(name==Begin)beginBytes=((ZPackage)args[1]).Bytes();};
        MobSourceHandoff.CommitLocal=delegate(ZRpc rpc,ZDOID id,long owner){Check(MobSourceHandoff.IsLocalCommit(data)&&MobSourceHandoff.LocalTarget==target,"source scope missing");order.Add("checkpoint");data.SetOwner(owner);order.Add("owner");};
        MobSourceHandoff.ResolveDestination=delegate{return 2;};MobSourceHandoff.ValidateLocal=delegate{return safe;};
        MobSourceHandoff.AcceptedServer=delegate{order.Add("accepted");};MobSourceHandoff.PublishFinal=delegate{order.Add("native");};
        MobSourceHandoff.RoundtripSeconds=null;MobSourceHandoff.PresentationTiming=null;
        MobSourceHandoff.Start();if(host){source.m_rpc.Receive(Mode,-3);destination.m_rpc.Receive(Mode,-3);}else server.m_rpc.Receive(Mode,-3);order.Clear();
    }
    private static byte[] BeginPacket(long token,uint revision,int prefab,long to)
    {ZPackage p=new ZPackage();p.Write(token);p.Write(revision);p.Write(prefab);p.Write(target);p.Write(to);return p.Bytes();}
    private static void Local()
    {
        New(false);MobSourceHandoff.SkipLocalCommit=delegate(ZDO value){return ReferenceEquals(value,data);};
        try{Check(!MobSourceHandoff.TryCommit(new BaseAI(),data,target,server)&&order.Count==0&&data.Owner==1,
            "vanilla comparison bypasses source commit, timing and forced publication without writing owner");}
        finally{MobSourceHandoff.SkipLocalCommit=null;}
        New(false);double before=Time.realtimeSinceStartupAsDouble;
        Check(MobSourceHandoff.TryCommit(new BaseAI(),data,target,server),"source still needs a grant");
        Check(data.Owner==2&&data.OwnerRevision==5&&before==Time.realtimeSinceStartupAsDouble,"source did not stop at local time zero");
        Check(String.Join(",",order.ToArray())==Begin+",checkpoint,owner,native","cutoff/native publication order changed");
        Check(!MobSourceHandoff.IsLocalCommit(data)&&MobSourceHandoff.LocalTarget.IsNone(),"scope leaked");
        Check(!MobSourceHandoff.TryCommit(new BaseAI(),data,target,server),"already transferred mob replayed");
        New(false);safe=false;Check(!MobSourceHandoff.TryCommit(new BaseAI(),data,target,server)&&order.Count==0,"unfinished local action transferred");
        New(false);MobSourceHandoff.ResolveDestination=delegate{return 1;};Check(!MobSourceHandoff.TryCommit(new BaseAI(),data,target,server)&&order.Count==0,"self ownership recreated");
        New(false);MobSourceHandoff.CommitLocal=delegate{};Check(!MobSourceHandoff.TryCommit(new BaseAI(),data,target,server)&&order.Contains(Cancel)&&data.Owner==1,"refusal changed owner");
        New(false);MobSourceHandoff.CommitLocal=delegate{throw new Exception("capture");};Check(!MobSourceHandoff.TryCommit(new BaseAI(),data,target,server)&&data.Owner==1,"capture failure stopped authority");
        New(false);MobSourceHandoff.CommitLocal=delegate(ZRpc rpc,ZDOID id,long to){data.SetOwner(to);throw new Exception("after write");};Check(MobSourceHandoff.TryCommit(new BaseAI(),data,target,server)&&!order.Contains(Cancel),"postcommit exception rolled back");
        New(false);MobSourceHandoff.PublishFinal=delegate{throw new Exception("send");};Check(MobSourceHandoff.TryCommit(new BaseAI(),data,target,server)&&data.Owner==2,"send failure replayed source");
    }
    private static void Server()
    {
        New(true);source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(1,4,42,2)));
        Check(MobSourceHandoff.Waiting(data)&&Map().Count==1&&data.Owner==1,"begin requires prior permission or writes owner");
        MobSourceHandoff.Forget(data.m_uid);Check(Map().Count==1,"watch rebind lost pending cutoff");
        ZDOMan.instance.RPC_ZDOData(delegate{data.SetOwner(2);});Check(order.Contains("accepted")&&Map().Count==0,"native next epoch not reconciled");
        int count=order.Count;source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(1,4,42,2)));Check(Map().Count==0&&order.Count==count,"replayed notification accepted");
        New(true);source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(2,3,42,2)));Check(Map().Count==0,"wrong source revision accepted");
        source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(2,4,999,2)));Check(Map().Count==0,"wrong prefab accepted");
        source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(2,4,42,999)));Check(Map().Count==0,"missing destination accepted");
        source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(2,4,42,2)));source.m_rpc.Receive(Cancel,data.m_uid,2L);Check(Map().Count==0&&!MobSourceHandoff.Waiting(data),"cancel holds mob");
        New(true);source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(3,4,42,2)));Time.realtimeSinceStartupAsDouble=6;ZDOMan.instance.RPC_ZDOData(delegate{data.SetOwner(2);});Check(!order.Contains("accepted")&&Map().Count==0,"expired source tuple accepted");
        New(true);source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(4,4,42,2)));ZDOMan.instance.RPC_ZDOData(delegate{data.SetOwner(999);});Check(!order.Contains("accepted"),"wrong destination reconciled");
        New(true);source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(5,4,42,2)));ZNet.instance.Shutdown();Check(Map().Count==0,"shutdown retained source tuples");
    }
    private static void Publication()
    {
        New(false);byte[] bytes=null;int sends=0;server.m_rpc.Send=delegate(string name,object[] args){Check(name=="ZDOData","non-native publication");bytes=((ZPackage)args[0]).Bytes();sends++;};
        data.SetOwner(2);MobNativePublication.Send(data,server);ZPackage p=new ZPackage(bytes);
        Check(sends==1&&data.Serializations==1&&data.OwnerRevision==5,"publication changes owner or serializes twice");
        Check(p.ReadInt()==0&&p.ReadZDOID()==data.m_uid&&p.ReadUShort()==5&&p.ReadUInt()==7&&p.ReadLong()==2,"native header wrong");
        Vector3 v=p.ReadVector3();Check(v.x==4&&v.y==5&&v.z==6,"native position missing");Check(p.ReadPackage().ReadInt()==123&&p.ReadZDOID().IsNone(),"native body/terminator wrong");
        data.ExtraBody=65537;bool rejected=false;try{MobNativePublication.Send(data,server);}catch(InvalidOperationException){rejected=true;}Check(rejected&&sends==1,"oversize sent");
        server.Ready=false;MobNativePublication.Send(data,server);Check(sends==1,"unready destination sent");
    }
    private static void TimingCache()
    {
        New(false);Check(Math.Abs(MobSourceHandoff.PresentationWindow(2,.05f)-.05f)<.001f,"cold estimate ignored own RTT");
        Check(Math.Abs(MobSourceHandoff.PresentationWindow(2,.02f)-.02f)<.001f,"20ms route raised to artificial 40ms floor");
        Check(MobSourceHandoff.TryCommit(new BaseAI(),data,target,server),"timing estimate blocked immediate handoff");
        Action<uint,float,long> reply=delegate(uint revision,float seconds,long owner){ZPackage p=new ZPackage();p.Write(revision);p.Write(seconds);p.Write(owner);server.m_rpc.Receive("TooManyLags_AI_SourceTiming",data.m_uid,new ZPackage(p.Bytes()));};
        reply(4,.1f,2);Check(Math.Abs(MobSourceHandoff.PresentationWindow(2,.05f)-.2f)<.001f,"source/destination RTT sum not cached");
        reply(4,0f,2);Check(Math.Abs(MobSourceHandoff.PresentationWindow(2,.05f)-.2f)<.001f,
            "zero timing sample poisoned valid cache");
        reply(3,.2f,2);reply(4,.2f,3);reply(4,Single.NaN,2);
        Check(Math.Abs(MobSourceHandoff.PresentationWindow(2,.05f)-.2f)<.001f,"stale/wrong-destination/nonfinite timing poisoned cache");
        MobSourceHandoff.InvalidatePresentationTiming();
        Check(Math.Abs(MobSourceHandoff.PresentationWindow(2,.23f)-.23f)<.001f,
            "changed artificial latency inherited pre-test presentation timing");
        Time.realtimeSinceStartupAsDouble=6;Check(Math.Abs(MobSourceHandoff.PresentationWindow(2,.05f)-.05f)<.001f,"old timing estimate survived expiry");
        reply(4,.1f,2);ZNet.instance=new ZNet();Check(Math.Abs(MobSourceHandoff.PresentationWindow(2,.05f)-.05f)<.001f,"another session inherited timing");
        New(true);byte[] measured=null;source.m_rpc.Send=delegate(string name,object[] args){if(name=="TooManyLags_AI_SourceTiming")measured=((ZPackage)args[1]).Bytes();};
        MobSourceHandoff.RoundtripSeconds=delegate(ZNetPeer peer){return peer==source?.05f:.15f;};
        source.m_rpc.Receive(Begin,data.m_uid,new ZPackage(BeginPacket(1,4,42,2)));
        Check(measured!=null&&measured.Length==16&&MobSourceHandoff.Waiting(data)&&data.Owner==1,"presentation timing changed authority or waited for an answer");
        ZPackage timing=new ZPackage(measured);Check(timing.ReadUInt()==4&&Math.Abs(timing.ReadSingle()-.1f)<.001f&&timing.ReadLong()==2,"presentation measurement used wrong route");
    }
    public static void Main(){try{Local();Server();Publication();TimingCache();Console.WriteLine("PASS: "+checks+" production source-handoff checks: no grants, immediate local cutoff, native publication, fences, presentation RTT cache and failure handling.");}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}finally{MobSourceHandoff.Stop();}}
}

