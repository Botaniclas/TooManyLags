using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace UnityEngine { public static class Time { public static double realtimeSinceStartupAsDouble; } }
public interface ISocket
{
    void Send(ZPackage data); ZPackage Recv(); bool IsConnected();
    void GetConnectionQuality(out float local, out float remote, out int ping, out float sent, out float received);
}
public class TestSocket : ISocket
{
    public readonly List<int> Sent = new List<int>(); public readonly Queue<ZPackage> Wire = new Queue<ZPackage>();
    public bool Connected = true,ThrowAfterSend; public int Ping = 20, Decoded, Encoded;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Send(ZPackage data) { Encoded++; Sent.Add(BitConverter.ToInt32(data.GetArray(),0));if(ThrowAfterSend)throw new InvalidOperationException("socket hook failed after sending"); }
    [MethodImpl(MethodImplOptions.NoInlining)] public ZPackage Recv() { if(Wire.Count==0)return null; Decoded++;return Wire.Dequeue(); }
    public bool IsConnected() { return Connected; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void GetConnectionQuality(out float local,out float remote,out int ping,out float sent,out float received)
    {local=remote=sent=received=0;ping=Ping;}
}
public class ZPackage
{
    private readonly MemoryStream bytes; public ZPackage():this(new byte[0]){} public ZPackage(byte[] data){bytes=new MemoryStream();bytes.Write(data,0,data.Length);bytes.Position=0;}
    public byte[] GetArray(){return bytes.ToArray();}public void SetPos(int pos){bytes.Position=pos;}public int GetPos(){return (int)bytes.Position;}public int Size(){return (int)bytes.Length;}
    public void Write(int value){new BinaryWriter(bytes).Write(value);}public int ReadInt(){return new BinaryReader(bytes).ReadInt32();}
    public void Clear(){bytes.SetLength(0);bytes.Position=0;}
}
public class ZRpc
{
    private readonly ISocket m_socket; private readonly ZPackage m_pkg=new ZPackage(); public readonly List<int> Received=new List<int>();
    public ZRpc(ISocket socket){m_socket=socket;}public ISocket GetSocket(){return m_socket;}
    public void Invoke(int value){m_pkg.Clear();m_pkg.Write(value);SendPackage(m_pkg);}
    [MethodImpl(MethodImplOptions.NoInlining)] private void SendPackage(ZPackage package){m_socket.Send(m_pkg);}
    [MethodImpl(MethodImplOptions.NoInlining)] public void Update(float dt)
    {for(ZPackage package=m_socket.Recv();package!=null;package=m_socket.Recv())Received.Add(package.ReadInt());}
}
public class ZNetPeer { public bool m_server=true,Ready=true;public ISocket m_socket;public ZRpc m_rpc;public bool IsReady(){return Ready;} }
public class ZDOMan { public static ZDOMan instance; }
public class ZNet
{
    public static ZNet instance;public bool Server;public ZNetPeer Peer;public bool IsServer(){return Server;}public ZNetPeer GetServerPeer(){return Peer;}
    public static implicit operator bool(ZNet value){return value!=null;}
}
namespace TooManyLags
{internal static class Plugin{internal const string Id="solo-latency-fixture";internal static readonly List<string> Logs=new List<string>();internal static void TestLog(string text){Logs.Add(text);}internal static void Warn(string text){Logs.Add(text);}}}
