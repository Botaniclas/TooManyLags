using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TooManyLags;
using UnityEngine;

internal static class BridgeObserverTests
{
    private const string Cap = "TooManyLags_RecordedObserverCapability", Stream = "TooManyLags_RecordedObserver";
    private static int checks;
    private sealed class Sent { internal string Method; internal object[] Args; }
    private static readonly Dictionary<ZRpc, List<Sent>> sent = new Dictionary<ZRpc, List<Sent>>();
    private static void Check(bool result, string message) { ++checks; if (!result) throw new Exception(message); }
    private static IDictionary Entries { get { return (IDictionary)typeof(MobBridgeObserver).GetField("entries", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null); } }
    private static object Field(object value, string name) { return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value); }
    private static ZDO data;
    private static ZNetPeer Peer(long uid, bool capable = true, bool server = false)
    {
        var peer = new ZNetPeer { m_uid = uid, m_server = server }; sent[peer.m_rpc] = new List<Sent>();
        peer.m_rpc.Send = delegate(string method, object[] args)
        {
            var copy = (object[])args.Clone();
            for (int i = 0; i < copy.Length; ++i) if (copy[i] is ZPackage) copy[i] = new ZPackage(((ZPackage)copy[i]).Bytes());
            sent[peer.m_rpc].Add(new Sent { Method = method, Args = copy });
        };
        ZNet.instance.Peers[uid] = peer; if (server) ZNet.instance.Peer = peer;
        MobBridgeObserver.Connect(peer);
        if (capable) peer.m_rpc.Deliver(Cap, ZNet.instance.IsServer() ? 4 : -4);
        return peer;
    }
    private static ZNetPeer Start(bool server = false, long local = 3)
    {
        MobBridgeObserver.Stop(); sent.Clear(); FgnSoloCombatTest.Native = false;
        MobBridgeVisual.Starts = MobBridgeVisual.Appends = MobBridgeVisual.Clocks = MobHandoffVisual.Cancelled = 0;
        MobBridgeVisual.Received.Clear(); MobBridgeVisual.Loaded = true;
        ZDOMan.SessionID = local; ZDOMan.instance = new ZDOMan(); ZNet.instance = new ZNet { Server = server };
        ZNetScene.instance = new ZNetScene(); Time.realtimeSinceStartupAsDouble = 0;
        data = new ZDO { m_uid = new ZDOID { UserID = 99, ID = 1 }, Owner = 1, OwnerRevision = 7 };
        ZDOMan.instance.All[data.m_uid] = data; MobBridgeObserver.Start();
        return server ? null : Peer(10, true, true);
    }
    private static MobBridgeObserverPacket Packet(byte kind = 0, uint sequence = 0)
    {
        return new MobBridgeObserverPacket { Kind = kind, Source = 1, Destination = 2, Revision = 7, Token = 42,
            Sequence = sequence, Prefab = 42, Bones = 2, Paths = new[] { "", "4:Head:0/" }, Window = .1f, Rate = 1f, AiRate = 1f,
            Rotation = Quaternion.identity, ModelRotation = Quaternion.identity };
    }
    private static MobBridgeObserverPacket.Sample Sample(float time, bool bones = false, bool attack = false)
    {
        var sample = new MobBridgeObserverPacket.Sample { Time = time, Position = new Vector3(time * 4, time, 0),
            Rotation = Quaternion.AngleAxis(time * 90, new Vector3(0, 1, 0)), ModelRotation = Quaternion.identity, Attack = attack };
        if (bones)
        {
            sample.Positions = new[] { new Vector3(), new Vector3(0, 2, time) };
            sample.Rotations = new[] { Quaternion.identity, sample.Rotation };
            sample.Scales = new[] { new Vector3(1, 1, 1), new Vector3(1, 1, 1) };
        }
        return sample;
    }
    private static MobBridgeObserverPacket Batch(uint sequence, params float[] times)
    { var packet = Packet(1, sequence); foreach (float time in times) packet.Samples.Add(Sample(time)); return packet; }
    private static void Receive(ZNetPeer peer, MobBridgeObserverPacket packet)
    { peer.m_rpc.Deliver(Stream, data.m_uid, new ZPackage(packet.Encode().Bytes())); }
    private static int Count(ZNetPeer peer, string method = Stream)
    { int count = 0; foreach (Sent message in sent[peer.m_rpc]) if (message.Method == method) ++count; return count; }
    private static MobBridgeObserverPacket Last(ZNetPeer peer)
    { var list = sent[peer.m_rpc]; for (int i = list.Count - 1; i >= 0; --i) if (list[i].Method == Stream) return MobBridgeObserverPacket.Decode(new ZPackage(((ZPackage)list[i].Args[1]).Bytes())); return null; }
    public static int Main()
    {
        try { Codec(); RelayCodec(); Source(); Server(); ReservedRecording(); LeanServerRelay(); Receiver(); Timeline(); Console.WriteLine("PASS: " + checks + " recorded observer codec/transport/timeline assertions; managed RPC doubles, no live Unity claim."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { MobBridgeObserver.Stop(); }
    }
    private static void Reject(ZPackage packet, string reason)
    { bool rejected = false; try { MobBridgeObserverPacket.Decode(new ZPackage(packet.Bytes())); } catch (Exception) { rejected = true; } Check(rejected, reason); }
    private static void Codec()
    {
        Check(new MobBridgeObserverPacket().PhysicsLayer == -1, "observers defaulted to a native physics layer");
        var descriptor = Packet(); descriptor.Effects = new[] { new ZDOID { UserID = 12, ID = 9 } };
        var copy = MobBridgeObserverPacket.Decode(new ZPackage(descriptor.Encode().Bytes()));
        Check(copy.Paths[1] == descriptor.Paths[1] && copy.Effects[0] == descriptor.Effects[0] && copy.Same(1, 2, 7, 42), "descriptor identity/rig/FX roundtrip");
        var packet = Packet(1, 1); packet.Samples.Add(Sample(.02f, true, true)); packet.Samples.Add(Sample(.04f, true, true));
        copy = MobBridgeObserverPacket.Decode(new ZPackage(packet.Encode().Bytes()));
        Check(copy.Samples[1].Positions[1].Equals(packet.Samples[1].Positions[1]) && copy.Samples[1].Rotations[1].Equals(packet.Samples[1].Rotations[1]), "pose delta roundtrip lost values");
        packet = Batch(1, 0, .02f); copy = MobBridgeObserverPacket.Decode(new ZPackage(packet.Encode().Bytes()));
        Check(copy.Samples[1].Positions == null, "locomotion sent unnecessary bone data");
        var original = packet.Encode().Bytes();
        for (int length = 0; length < original.Length; ++length)
        { var truncated = new byte[length]; Array.Copy(original, truncated, length); Reject(new ZPackage(truncated), "truncated packet accepted: " + length); }
        packet.Source = 2; Reject(packet.Encode(), "same endpoint accepted");
        packet = Batch(1, .02f, .01f); Reject(packet.Encode(), "reversed packet samples accepted");
        packet = Batch(1, .02f); packet.Samples[0].Position.x = Single.NaN; Reject(packet.Encode(), "NaN pose accepted");
        descriptor.Paths[1] = ""; Reject(descriptor.Encode(), "ambiguous bone path accepted");
        var extra = new byte[original.Length + 1]; Array.Copy(original, extra, original.Length); Reject(new ZPackage(extra), "trailing bytes accepted");
        for (byte kind = 2; kind <= 5; ++kind)
        { packet = Packet(kind, 3); packet.End = .1f; copy = MobBridgeObserverPacket.Decode(new ZPackage(packet.Encode().Bytes())); Check(copy.Kind == kind, "control packet roundtrip"); }
    }
    private static bool Decodes(byte[] bytes, bool relay, out MobBridgeObserverPacket packet)
    {
        packet=null;
        try { var input=new ZPackage(bytes); packet=relay?MobBridgeObserverPacket.DecodeForRelay(input):MobBridgeObserverPacket.Decode(input); return true; }
        catch(Exception) { return false; }
    }
    private static void SameValidation(byte[] bytes)
    {
        MobBridgeObserverPacket full, relay;
        bool a=Decodes(bytes,false,out full),b=Decodes(bytes,true,out relay);
        Check(a==b,"validation-only server decoder accepts/rejects different bytes");
        if(!a)return;
        Check(full.Kind==relay.Kind&&full.Source==relay.Source&&full.Destination==relay.Destination&&
            full.Revision==relay.Revision&&full.Token==relay.Token&&full.Sequence==relay.Sequence&&
            full.End==relay.End&&full.Samples.Count==relay.Samples.Count,"relay changed envelope/endpoint metadata");
        for(int i=0;i<full.Samples.Count;++i)
        {
            var x=full.Samples[i];var y=relay.Samples[i];
            Check(x.Time==y.Time&&x.Position.Equals(y.Position)&&x.Rotation.Equals(y.Rotation)&&
                x.ModelPosition.Equals(y.ModelPosition)&&x.ModelRotation.Equals(y.ModelRotation)&&x.Attack==y.Attack,
                "relay changed sampled root/time/attack metadata");
            Check(y.Positions==null&&y.Rotations==null&&y.Scales==null,"server allocated unused skeletal arrays");
        }
    }
    private static void RelayCodec()
    {
        var batch=Packet(1,1);
        batch.Samples.Add(Sample(0f,true,true));batch.Samples.Add(Sample(.02f,true,true));
        batch.Samples.Add(Sample(.03f)); // Root-only sample must retain previous delta baseline.
        batch.Samples.Add(Sample(.04f,true,true));
        byte[] valid=batch.Encode().Bytes();SameValidation(valid);
        for(int length=0;length<valid.Length;++length)
        {var shorter=new byte[length];Array.Copy(valid,shorter,length);SameValidation(shorter);}
        foreach(byte mutation in new byte[]{0,1,7,8,127,255})
        for(int offset=0;offset<valid.Length;++offset)
        {var changed=(byte[])valid.Clone();changed[offset]=mutation;SameValidation(changed);}
        foreach(float invalid in new[]{Single.NaN,Single.PositiveInfinity,Single.NegativeInfinity})
        foreach(int component in new[]{0,1,2})
        {
            var packet=Packet(1,1);var sample=Sample(0,true,true);
            if(component==0)sample.Positions[1]=new Vector3(invalid,0,0);
            else if(component==1)sample.Rotations[1]=new Quaternion{x=invalid,w=1};
            else sample.Scales[1]=new Vector3(1,invalid,1);
            packet.Samples.Add(sample);byte[] bytes=packet.Encode().Bytes();SameValidation(bytes);
            MobBridgeObserverPacket ignored;
            Check(!Decodes(bytes,true,out ignored),"relay admitted nonfinite explicit bone payload");
        }
        for(byte kind=0;kind<=5;++kind){var packet=Packet(kind,1);packet.End=.1f;
            if(kind==1)packet.Samples.Add(Sample(0,true,true));SameValidation(packet.Encode().Bytes());}
    }
    private static void LeanServerRelay()
    {
        Start(true,10);var source=Peer(1);Peer(2);var observer=Peer(3);
        var header=Packet();header.Bones=130;header.Paths=new string[130];header.Paths[0]="";
        for(int i=1;i<header.Paths.Length;i++)header.Paths[i]="4:Bone:"+i+"/";
        Receive(source,header);var batch=Packet(1,1);
        for(int f=0;f<64;f++)
        {
            var sample=Sample(f*.001f);sample.Attack=true;
            sample.Positions=new Vector3[130];sample.Rotations=new Quaternion[130];sample.Scales=new Vector3[130];
            for(int b=0;b<130;b++){sample.Positions[b]=new Vector3(b*.1f,b*.2f,0);sample.Rotations[b]=Quaternion.identity;sample.Scales[b]=new Vector3(1,1,1);}
            batch.Samples.Add(sample);
        }
        byte[] raw=batch.Encode().Bytes();var relay=MobBridgeObserverPacket.DecodeForRelay(new ZPackage(raw));
        Check(relay.Samples.Count==64,"lean server lost sample metadata");
        foreach(var sample in relay.Samples)Check(sample.Positions==null&&sample.Rotations==null&&sample.Scales==null,"large relay rebuilt dense skeletons");
        Receive(source,batch);var messages=sent[observer.m_rpc];var forwarded=((ZPackage)messages[messages.Count-1].Args[1]).Bytes();
        Check(raw.Length==forwarded.Length,"lean server changed relayed payload length");
        for(int i=0;i<raw.Length;i++)Check(raw[i]==forwarded[i],"lean server changed relayed packet bytes");
        Console.WriteLine("ALLOCATION: relay validation avoids 192 bone arrays / 332800 raw pose bytes for this 64-frame, 130-bone modeled batch; wire bytes unchanged.");
    }
    private static void Source()
    {
        ZNetPeer server = Start(false, 1); server.m_rpc.Deliver(Cap, -1);
        var animation = new MobBridgeAnimation(); var recording = new MobBridgeRecording(0, .1f, 1);
        animation.History.Add(0, new Vector3[2], new[] { Quaternion.identity, Quaternion.identity }, new[] { new Vector3(1, 1, 1), new Vector3(1, 1, 1) });
        recording.Add(0, new Vector3(), Quaternion.identity);
        MobBridgeObserver.Begin(data, 2, 7, 42, .1f, 1, 1, new Vector3(), Quaternion.identity, animation, null);
        Check(Entries.Count == 0 && Count(server) == 0, "solo session streams recording");
        server.m_rpc.Deliver(Cap, -4);
        MobBridgeObserver.Begin(data, 2, 7, 42, .1f, 1, 1, new Vector3(), Quaternion.identity, animation, null);
        Check(MobBridgeObserver.Publishing(data.m_uid, 7, 42), "source recording did not open");
        int cursor = 0; server.m_rpc.Send = delegate { throw new InvalidOperationException("socket send failed"); };
        try { MobBridgeObserver.Publish(data, recording, animation, ref cursor, false); } catch (InvalidOperationException) { }
        Check(cursor == 0 && (uint)Field(Entries[data.m_uid], "Sequence") == 0, "failed send consumed immutable stream cursor");
        server.m_rpc.Send = delegate(string method, object[] args) { sent[server.m_rpc].Add(new Sent { Method = method, Args = args }); };
        MobBridgeObserver.Publish(data, recording, animation, ref cursor, false);
        Check(cursor == 1 && (uint)Field(Entries[data.m_uid], "Sequence") == 1, "retry lost first sample");
        for (int i = 1; i <= 5; ++i)
        { float t = i * .02f; animation.History.Add(t, new Vector3[2], new[] { Quaternion.identity, Quaternion.identity }, new[] { new Vector3(1, 1, 1), new Vector3(1, 1, 1) }, i == 3); recording.Add(t, new Vector3(t, 0, 0), Quaternion.identity); }
        Time.realtimeSinceStartupAsDouble = .1; recording.Close(); animation.History.Close();
        MobBridgeObserver.Publish(data, recording, animation, ref cursor, true);
        Check(cursor == 6 && !MobBridgeObserver.Publishing(data.m_uid, 7, 42) && Last(server).Kind == 2, "sealed recording lost final endpoints");
        Check(data.OwnerWrites == 0 && data.Position.sqrMagnitude == 0, "presentation source mutated authority");
        server = Start(false, 1); FgnSoloCombatTest.Native = true;
        MobBridgeObserver.Begin(data, 2, 7, 42, .1f, 1, 1, new Vector3(), Quaternion.identity, animation, null);
        Check(Entries.Count == 0, "vanilla comparator enabled recording");
        server = Start(false, 1); server.m_rpc.Deliver(Cap, -2);
        MobBridgeObserver.Begin(data, 10, 7, 42, .1f, 1, 1, new Vector3(), Quaternion.identity, animation, null);
        Check(Entries.Count == 1, "server destination incorrectly counted as another observer client");
        data.SetOwner(10); Check(MobObserverHandoff.RecordedActive(data), "matching recorded epoch did not suppress duplicate legacy presentation");
        data.SetOwner(1); data.SetOwner(10); Check(!MobObserverHandoff.RecordedActive(data), "stale recording suppressed later fallback");
    }
    private static void Server()
    {
        Start(true, 10); ZNetPeer source = Peer(1), destination = Peer(2), observer = Peer(3), old = Peer(4, false), far = Peer(5);
        far.Reference = new Vector3(1000, 0, 0);
        Receive(source, Packet());
        Check(Count(observer) == 1 && Count(source) == 0 && Count(destination) == 1 && Count(old) == 0 && Count(far) == 0, "recipient relevance/endpoints/capability filtering");
        Check(Last(destination).Kind == 3 && Entries.Count == 1, "destination clock demand missing");
        Receive(observer, Batch(1, .02f)); Check(Count(observer) == 1, "observer impersonated source");
        Receive(source, Batch(2, .04f)); Check(Count(observer) == 1, "gapped sequence accepted");
        Receive(source, Batch(1, 0, .02f)); Check(Count(observer) == 2, "source first native interval not relayed");
        Receive(source, Batch(1, .04f)); Check(Count(observer) == 2, "duplicate sample accepted");
        var bad = Batch(2, .2f); Receive(source, bad); Check(Count(observer) == 2, "sample escaped recording horizon");
        Receive(source, Batch(2, .04f, .1f)); var seal = Packet(2, 3); seal.End = .09f; Receive(source, seal);
        Check(Count(observer) == 3, "mismatched seal accepted");
        seal.End = .1f; Receive(source, seal); Check(Count(observer) == 4 && Last(observer).Kind == 2, "recording seal not relayed");
        data.SetOwner(2); Time.realtimeSinceStartupAsDouble = 40; MobBridgeObserver.Tick();
        Check(Entries.Count == 1, "valid observer stream expired during delayed matching");
        Receive(observer, Packet(4)); Check(Last(destination).End == 0f && Last(source).Kind == 5, "last observer did not release clock demand/source export");
        MobBridgeObserver.Tick(); Check(Entries.Count == 0, "finished observer retained references");
        Check(data.OwnerWrites == 1, "relay changed authority");
        Start(true, 10); source = Peer(1); destination = Peer(2); Receive(source, Packet());
        Check(Entries.Count == 0 && Last(source).Kind == 5, "no relevant recipients did not decline");
        Start(true, 10); source = Peer(1); destination = Peer(2); observer = Peer(3);
        Receive(source, Packet()); Receive(source, Batch(1, 0, .02f)); data.SetOwner(2);
        ZNet.instance.Peers.Remove(1); MobBridgeObserver.Tick();
        Check(Last(observer).Kind == 2 && Math.Abs(Last(observer).End - .02f) < .00001f, "disconnected producer did not seal accepted prefix");
        ZNet.instance.Peers.Remove(3); Time.realtimeSinceStartupAsDouble = .06; MobBridgeObserver.Tick();
        Check(Entries.Count == 0 && Last(destination).End == 0f, "disconnected observer retained relay/demand");
        Start(true, 10); source = Peer(1); destination = Peer(2); observer = Peer(3); var other = Peer(4);
        Receive(source, Packet()); data.SetOwner(2);
        observer.m_rpc.Send = delegate { throw new InvalidOperationException("observer socket failed"); };
        MobBridgeObserver.ObserveClock(data.m_uid, 1, 2, 7, 42, 9, .1f, .1f, new Vector3(), Quaternion.identity, new Vector3(), -1f, "", .1f, new ZPackage());
        Check(Count(other, "TooManyLags_MobBridgeClock") == 1 && (bool)Field(Entries[data.m_uid], "ClockRetry"),
            "one observer send failure escaped native AI path or blocked another recipient");
        int retried = 0; observer.m_rpc.Send = delegate(string method, object[] args) { if(method == "TooManyLags_MobBridgeClock") ++retried; };
        Time.realtimeSinceStartupAsDouble = .06; MobBridgeObserver.Tick();
        Check(retried == 1 && !(bool)Field(Entries[data.m_uid], "ClockRetry"), "final observed frontier not retried after source clock stops");
    }
    private static void ReservedRecording()
    {
        Start(true,10);var source=Peer(1);var destination=Peer(2);var observer=Peer(3);
        var descriptor=Packet();descriptor.Window=.01f;descriptor.RecordingWindow=.024f;
        descriptor.FirstArrival=.04f;descriptor.ExpectedMatch=.04f;descriptor.PhysicsStep=.02f;
        Receive(source,descriptor);
        Check(Math.Abs(Last(destination).End-.024f)<.000001f,"observer clock demand ignored reserved horizon");
        var forwarded=Last(observer);
        Check(Math.Abs(forwarded.Window-.01f)<.000001f&&Math.Abs(forwarded.RecordingSeconds-.024f)<.000001f&&
            Math.Abs(forwarded.ExpectedArrival-.04f)<.000001f,"observer route/arrival/capacity were merged");
        Receive(source,Batch(1,0,.02f,.024f));
        Check(Count(observer)==2,"valid reserve samples beyond route latency were rejected");
        Receive(source,Batch(2,.025f));Check(Count(observer)==2,"sample escaped reserved recording bound");
        var seal=Packet(2,2);seal.End=.024f;Receive(source,seal);
        Check(Last(observer).Kind==2&&Math.Abs(Last(observer).End-.024f)<.000001f,"reserved recording not sealed immutably");
    }
    private static void Receiver()
    {
        ZNetPeer server = Start(); Receive(server, Packet());
        Receive(server, Batch(1, 0, .02f)); Check(MobBridgeVisual.Starts == 0, "observer started before committed owner epoch");
        data.SetOwner(2); MobBridgeObserver.ObserveClock(data.m_uid, 1, 2, 7, 42, 1, .02f, 0f, new Vector3(), Quaternion.identity, new Vector3(), -1f, "", .02f, new ZPackage());
        MobBridgeObserver.Tick(); Check(MobBridgeVisual.Starts == 1 && MobBridgeVisual.Clocks == 1, "pending start lost cached real frontier");
        MobBridgeObserver.ObserveClock(data.m_uid, 1, 2, 7, 42, 2, .01f, .01f, new Vector3(), Quaternion.identity, new Vector3(), -1f, "", .01f, new ZPackage());
        Check(MobBridgeVisual.Clocks == 1, "cached destination frontier rewound");
        MobBridgeObserver.ObserveClock(data.m_uid, 1, 2, 7, 42, 1, .04f, .02f, new Vector3(), Quaternion.identity, new Vector3(), -1f, "", .02f, new ZPackage());
        Check(MobBridgeVisual.Clocks == 2, "observer rejected completed physics without another AI pass");
        MobBridgeObserver.ObserveClock(data.m_uid, 1, 2, 7, 42, 1, .04f, .03f, new Vector3(), Quaternion.identity, new Vector3(), -1f, "", .02f, new ZPackage());
        Check(MobBridgeVisual.Clocks == 2, "observer refreshed duplicate physical frontier");
        MobBridgeObserver.ObserveClock(data.m_uid, 1, 2, 7, 42, 1, .06f, .04f, new Vector3(), Quaternion.identity, new Vector3(), -1f, "", .04f, new ZPackage());
        Check(MobBridgeVisual.Clocks == 2, "observer invented AI progress on body-only update");
        Receive(server, Batch(2, .04f, .1f)); Check(MobBridgeVisual.Appends == 1, "received immutable recording not appended");
        var seal = Packet(2, 3); seal.End = .1f; Receive(server, seal); Check(MobBridgeVisual.Appends == 2, "received recording not sealed");
        Time.realtimeSinceStartupAsDouble = 60; MobBridgeObserver.Tick(); Check(Entries.Count == 1, "active delayed presentation expired");
        MobBridgeObserver.Finish(data.m_uid, 7, 42, false); Check(Last(server).Kind == 4, "observer horizon match not acknowledged");
        int count = Count(server); MobBridgeObserver.Finish(data.m_uid, 7, 42, true); Check(Count(server) == count && Entries.Count == 0, "duplicate finish or retained track");
        server = Start(false, 2); var demand = Packet(3); demand.End = .1f; Receive(server, demand); data.SetOwner(2);
        Time.realtimeSinceStartupAsDouble = 60; MobBridgeObserver.Tick();
        Check(MobBridgeObserver.KeepClock(data.m_uid, 1, 2, 7, 42, .1f), "clock ended before observed endpoint actually sent");
        MobBridgeObserver.ClockSent(data.m_uid, 1, 2, 7, 42, .1f);
        Check(!MobBridgeObserver.KeepClock(data.m_uid, 1, 2, 7, 42, .1f), "clock demand continued beyond sent endpoint");
        server = Start(); MobBridgeVisual.Loaded = false; Receive(server, Packet()); Receive(server, Batch(1, 0, .02f));
        data.SetOwner(2); Time.realtimeSinceStartupAsDouble = 6; MobBridgeObserver.Tick();
        Check(Entries.Count == 0 && Last(server).Kind == 4, "unloaded pending observer leaked demand");
        server = Start(); FgnSoloCombatTest.Native = true; Receive(server, Packet()); Check(Entries.Count == 0, "vanilla client received observer presentation");
        server = Start(); Receive(server, Packet()); var newer = Packet(); newer.Revision = 8; newer.Token = 43;
        data.SetOwner(2); data.Owner = 1; Receive(server, newer); Check(((MobBridgeObserverPacket)Field(Entries[data.m_uid], "Descriptor")).Token == 43, "new epoch did not replace old stream");
        data.SetOwner(9); MobBridgeObserver.Tick(); Check(Entries.Count == 0, "superseded stream retained references");
    }
    private static void Timeline()
    {
        var shortened = new MobBridgeTimeline(0, .1f, 1f, true);
        shortened.ManageInputBuffer(.02f, 0, .05f, false); shortened.ManageBuffer(.1f, 0, 0, 1, .05f, false);
        shortened.AdvanceFrame(.01, .01f);
        shortened.ManageInputBuffer(.03f, 0, .05f, true); shortened.ManageBuffer(.03f, .03f, 0, 1, .05f, true);
        for(int i=2;i<=100;i++)shortened.AdvanceFrame(i*.01,.01f);
        Check(Math.Abs(shortened.FakeTime(1)-.03f)<.00001f,"early sealed prefix retains estimated full-window braking");
        foreach (float latency in new[] { .02f, .05f, .1f, .5f, 1f })
        foreach (float frame in new[] { .004f, .016f, .033f })
        foreach (float outage in new[] { 0f, .1f, .5f })
        {
            var old = new MobBridgeTimeline(0, latency, 1, true);
            var observer = new MobBridgeTimeline(0, latency, 1, true); float previous = 0, available = Math.Min(.02f, latency);
            for (int i = 1; i * frame < 3 * latency + outage + 1; ++i)
            {
                float time = i * frame;
                old.AdvanceFrame(time, frame);
                Check(Math.Abs(old.FakeTime(time) - time * .5f) < .00005f, "default source half-speed clock changed");
                bool packet = time < latency * .5f || time > latency * .5f + outage;
                if (packet) available = Math.Min(latency, time + .02f);
                observer.ManageInputBuffer(available, packet ? 0f : frame, .05f, available == latency);
                observer.ManageBuffer(latency, latency, 0f, 1f, .05f, true);
                observer.AdvanceFrame(time, frame); float value = observer.FakeTime(time);
                Check(value >= previous && value <= available + .000001f, "observer rewind/overrun during buffer outage"); previous = value;
            }
            Check(Math.Abs(previous - latency) < .00001f, "observer failed recovery to sealed horizon");
        }
    }
}
