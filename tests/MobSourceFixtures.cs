using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public struct Vector3 { public float x, y, z; public Vector3(float a,float b,float c) { x=a;y=b;z=c; } }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
}
public class BaseAI : UnityEngine.Object { }
public struct ZDOID : IEquatable<ZDOID>
{
    public long UserID; public uint ID; public static readonly ZDOID None = new ZDOID();
    public bool IsNone() { return UserID == 0 && ID == 0; }
    public bool Equals(ZDOID other) { return UserID == other.UserID && ID == other.ID; }
    public override bool Equals(object other) { return other is ZDOID && Equals((ZDOID)other); }
    public override int GetHashCode() { return UserID.GetHashCode() ^ (int)ID; }
    public static bool operator ==(ZDOID a, ZDOID b) { return a.Equals(b); }
    public static bool operator !=(ZDOID a, ZDOID b) { return !a.Equals(b); }
}
public class ZDO
{
    public ZDOID m_uid; public long Owner = 1; public uint OwnerRevision = 4, DataRevision = 7; public int Prefab = 42; public bool Valid = true;
    public UnityEngine.Vector3 Position = new UnityEngine.Vector3(4,5,6); public int Serializations, ExtraBody;
    public bool IsValid() { return Valid; } public bool IsOwner() { return Owner == ZDOMan.Session; }
    public long GetOwner() { return Owner; } public int GetPrefab() { return Prefab; }
    public void SetOwner(long owner) { Owner = owner; OwnerRevision++; }
    public UnityEngine.Vector3 GetPosition() { return Position; }
    public void Serialize(ZPackage body) { Serializations++; body.Write(123); for(int i=0;i<ExtraBody;i++)body.Write((byte)0); }
}
public class ZDOMan
{
    public static ZDOMan instance; public static long Session;
    public readonly Dictionary<ZDOID, ZDO> Values = new Dictionary<ZDOID, ZDO>();
    public static long GetSessionID() { return Session; }
    public ZDO GetZDO(ZDOID id) { ZDO data; return Values.TryGetValue(id, out data) ? data : null; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_ZDOData(Action action) { action(); }
}
public class ZNet : UnityEngine.Object
{
    public static ZNet instance; public bool Server;
    public readonly List<ZNetPeer> Peers = new List<ZNetPeer>();
    public bool IsServer() { return Server; } public List<ZNetPeer> GetPeers() { return Peers; }
    public ZNetPeer GetPeer(long id) { return Peers.Find(p => p.m_uid == id); }
    public ZNetPeer GetServerPeer() { return Peers.Find(p => p.m_server); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Shutdown() { Peers.Clear(); }
}
public class ZNetPeer { public long m_uid; public bool m_server, Ready = true; public ZRpc m_rpc = new ZRpc(); public bool IsReady() { return Ready; } }
public class ZRpc
{
    public Action<string, object[]> Send;
    public readonly Dictionary<string, Delegate> Handlers = new Dictionary<string, Delegate>();
    public void Invoke(string name, params object[] args) { if (Send != null) Send(name, args); }
    public void Register<T>(string name, Action<ZRpc, T> callback) { Handlers[name] = callback; }
    public void Register<T, U>(string name, Action<ZRpc, T, U> callback) { Handlers[name] = callback; }
    public void Receive(string name, params object[] args)
    { object[] all = new object[args.Length + 1]; all[0] = this; Array.Copy(args, 0, all, 1, args.Length); Handlers[name].DynamicInvoke(all); }
}
public class ZPackage
{
    private MemoryStream stream; private BinaryReader reader; private BinaryWriter writer;
    public ZPackage() : this(new byte[0]) { }
    public ZPackage(byte[] bytes) { stream = new MemoryStream(); stream.Write(bytes, 0, bytes.Length); stream.Position = 0; reader = new BinaryReader(stream); writer = new BinaryWriter(stream); }
    public byte[] Bytes() { return stream.ToArray(); } public int Size() { return (int)stream.Length; } public int GetPos() { return (int)stream.Position; }
    public void Write(byte value) { writer.Write(value); } public void Write(long value) { writer.Write(value); }
    public void Write(ushort value) { writer.Write(value); } public void Write(float value) { writer.Write(value); }
    public void Write(uint value) { writer.Write(value); } public void Write(int value) { writer.Write(value); }
    public void Write(ZDOID value) { Write(value.UserID); Write(value.ID); }
    public void Write(UnityEngine.Vector3 value) { Write(value.x);Write(value.y);Write(value.z); }
    public void Write(ZPackage value) { byte[] bytes=value.Bytes();Write(bytes.Length);writer.Write(bytes); }
    public byte ReadByte() { return reader.ReadByte(); } public long ReadLong() { return reader.ReadInt64(); }
    public uint ReadUInt() { return reader.ReadUInt32(); } public int ReadInt() { return reader.ReadInt32(); }
    public ushort ReadUShort() { return reader.ReadUInt16(); } public float ReadSingle() { return reader.ReadSingle(); }
    public UnityEngine.Vector3 ReadVector3() { return new UnityEngine.Vector3(ReadSingle(),ReadSingle(),ReadSingle()); }
    public ZPackage ReadPackage() { return new ZPackage(reader.ReadBytes(ReadInt())); }
    public ZDOID ReadZDOID() { return new ZDOID { UserID = ReadLong(), ID = ReadUInt() }; }
}
namespace TooManyLags
{
    internal static class Plugin
    { internal const string Id = "mob-source-fixture"; internal static int Warnings; internal static void Warn(string message) { Warnings++; Console.WriteLine("WARN: " + message); } }
}
