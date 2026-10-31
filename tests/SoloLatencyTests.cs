using System;
using TooManyLags;
using UnityEngine;

internal static class SoloLatencyTests
{
    private static int checks;private static TestSocket socket;private static ZNetPeer peer;
    private static void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
    private static void Setup(int ping=20)
    {
        SoloNetworkLatency.Reset();Time.realtimeSinceStartupAsDouble=0;socket=new TestSocket{Ping=ping};
        peer=new ZNetPeer{m_socket=socket,m_rpc=new ZRpc(socket)};ZNet.instance=new ZNet{Peer=peer};ZDOMan.instance=new ZDOMan();
    }
    private static void Start(int target=100)
    {int baseline,added;string error;Check(SoloNetworkLatency.Start(peer,target,out baseline,out added,out error),"start: "+error);Check(baseline==socket.Ping&&added==Math.Max(0,target-socket.Ping),"base RTT subtracted, never negative delay");}
    private static void Incoming(int value){ZPackage data=new ZPackage();data.Write(value);data.SetPos(0);socket.Wire.Enqueue(data);}
    public static int Main()
    {
        try{Run();Console.WriteLine("PASS: "+checks+" actual solo-latency module/Harmony assertions: RTT split, immutable ordered packets, single native encode/decode, drain, isolation, bounds, timeout and teardown. Simulated clock/sockets, not live transport.");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{SoloNetworkLatency.Reset();}
    }
    private static void Run()
    {
        Setup();Start();peer.m_rpc.Invoke(1);peer.m_rpc.Invoke(2);
        Check(socket.Sent.Count==0,"outgoing packets really wait, not just a reported ping override");
        Time.realtimeSinceStartupAsDouble=.039;SoloNetworkLatency.Poll();Check(socket.Sent.Count==0,"20ms native RTT requires 40ms in each direction for target 100ms");
        Time.realtimeSinceStartupAsDouble=.04;SoloNetworkLatency.Poll();
        Check(socket.Sent.Count==2&&socket.Sent[0]==1&&socket.Sent[1]==2,"shared RPC scratch-buffer reuse cannot mutate queued packet snapshots or order");
        Check(socket.Encoded==2,"socket compression/encoding only runs at actual release, exactly once");
        float a,b,c,d;int ping;socket.GetConnectionQuality(out a,out b,out ping,out c,out d);Check(ping==100,"native socket RTT + actual injected delay is exposed to visual bridge");
        Incoming(3);Incoming(4);peer.m_rpc.Update(0);Check(peer.m_rpc.Received.Count==0&&socket.Decoded==2,"incoming gameplay packets wait after their single native decode");
        Time.realtimeSinceStartupAsDouble=.079;peer.m_rpc.Update(0);Check(peer.m_rpc.Received.Count==0,"incoming leg waits its separate 40ms");
        Time.realtimeSinceStartupAsDouble=.08;peer.m_rpc.Update(0);
        Check(peer.m_rpc.Received.Count==2&&peer.m_rpc.Received[0]==3&&peer.m_rpc.Received[1]==4&&socket.Decoded==2,"delayed receives retain order, cursor and single decode");
        var other=new TestSocket{Ping=7};var rpc=new ZRpc(other);rpc.Invoke(5);other.GetConnectionQuality(out a,out b,out ping,out c,out d);
        Check(other.Sent.Count==1&&ping==7,"unrelated socket's real traffic and quality stay native");
        SoloNetworkLatency.End();Check(!SoloNetworkLatency.Active,"ending empty test removes latency");
        socket.GetConnectionQuality(out a,out b,out ping,out c,out d);Check(ping==20,"native RTT restored after normal end");

        Setup();Start();peer.m_rpc.Invoke(1);peer.m_rpc.Invoke(2);Incoming(3);Incoming(4);peer.m_rpc.Update(0);
        SoloNetworkLatency.End();peer.m_rpc.Invoke(5);Incoming(6);peer.m_rpc.Update(0);
        Check(socket.Sent.Count==3&&socket.Sent[0]==1&&socket.Sent[1]==2&&socket.Sent[2]==5,"normal stop flushes queued sends before later immediate sends");
        Check(peer.m_rpc.Received.Count==3&&peer.m_rpc.Received[0]==3&&peer.m_rpc.Received[1]==4&&peer.m_rpc.Received[2]==6,"normal stop drains delayed incoming data before new native data, with no loss/replay");
        Check(socket.Decoded==3&&socket.Encoded==3,"normal drain does not repeat concrete socket hooks");
        Setup();Start();peer.m_rpc.Invoke(1);peer.m_rpc.Invoke(2);socket.ThrowAfterSend=true;Time.realtimeSinceStartupAsDouble=.04;
        bool failed=false;try{SoloNetworkLatency.Poll();}catch(InvalidOperationException){failed=true;}socket.ThrowAfterSend=false;SoloNetworkLatency.Poll();
        Check(failed&&socket.Sent.Count==2&&socket.Sent[0]==1&&socket.Sent[1]==2,"a socket hook throwing after send cannot replay the first action during the next pump");
        Setup();Start();peer.m_rpc.Invoke(10);Incoming(11);peer.m_rpc.Update(0);socket.Connected=false;SoloNetworkLatency.Poll();
        Check(!SoloNetworkLatency.Active&&socket.Sent.Count==0,"disconnect discards old-connection data instead of replaying it");
        Setup();Start();peer.m_rpc.Invoke(10);ZDOMan.instance=new ZDOMan();SoloNetworkLatency.Poll();
        Check(!SoloNetworkLatency.Active&&socket.Sent.Count==0,"changed world cannot receive an old queued ownership packet");
        Setup();Start();peer.m_rpc.Invoke(10);ZNet.instance.Peer=new ZNetPeer();SoloNetworkLatency.Poll();
        Check(!SoloNetworkLatency.Active&&socket.Sent.Count==0,"changed peer cannot inherit queue or quality override");
        Setup();Start();peer.m_rpc.Invoke(10);Time.realtimeSinceStartupAsDouble=301;SoloNetworkLatency.Poll();
        Check(!SoloNetworkLatency.Active&&socket.Sent.Count==1,"five-minute timeout restores native traffic and flushes valid queued data");
        Setup(150);Start();peer.m_rpc.Invoke(10);Check(socket.Sent.Count==1,"test cannot lower an already higher real RTT");
        socket.GetConnectionQuality(out a,out b,out ping,out c,out d);Check(ping==150,"higher native RTT is reported honestly, not forced down to 100");
        Setup(0);Start();peer.m_rpc.Invoke(10);Time.realtimeSinceStartupAsDouble=.049;SoloNetworkLatency.Poll();Check(socket.Sent.Count==0,"local 0ms base still gets 50ms outbound");
        Time.realtimeSinceStartupAsDouble=.05;SoloNetworkLatency.Poll();Check(socket.Sent.Count==1,"100ms target on local link releases after 50ms");
        Setup();int baseline,added;string error;ZNet.instance.Server=true;
        Check(!SoloNetworkLatency.Start(peer,100,out baseline,out added,out error),"no global/server-side traffic delay");
        ZNet.instance.Server=false;Check(!SoloNetworkLatency.Start(peer,1001,out baseline,out added,out error),"unbounded target refused");
        Setup();Start();Check(!SoloNetworkLatency.Start(peer,100,out baseline,out added,out error),"running/draining queue cannot be replaced by another test");
        for(int i=0;i<8193;i++)peer.m_rpc.Invoke(i);
        SoloNetworkLatency.Poll();Check(!SoloNetworkLatency.Active&&socket.Sent.Count==8193,"buffer limit aborts delay and drains every accepted packet");
        for(int i=0;i<8193;i++)Check(socket.Sent[i]==i,"bounded abort preserves all accepted packet order");
        peer.m_rpc.Invoke(9000);Check(socket.Sent[socket.Sent.Count-1]==9000,"native path works after complete teardown");
    }
}
