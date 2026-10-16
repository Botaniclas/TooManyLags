// Isolated managed fixtures: no Unity simulation or real socket delivery.
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public struct Vector3 { public float x,y,z; }
    public static class Time { public static float unscaledTime; }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public long Value; public static readonly ZDOID None = new ZDOID(); public ZDOID(long value) { Value=value; }
    public bool IsNone() { return Value==0; } public bool Equals(ZDOID value) { return value.Value==Value; }
    public override bool Equals(object value) { return value is ZDOID && Equals((ZDOID)value); } public override int GetHashCode() { return Value.GetHashCode(); }
}
public sealed class ZPackage
{
    private readonly MemoryStream data; private readonly BinaryReader read; private readonly BinaryWriter write;
    public ZPackage() : this(new byte[0]) { } public ZPackage(byte[] bytes) { data=new MemoryStream(); data.Write(bytes,0,bytes.Length); data.Position=0; read=new BinaryReader(data); write=new BinaryWriter(data); }
    public void Write(int value) { write.Write(value); } public void Write(long value) { write.Write(value); } public void Write(float value) { write.Write(value); } public void Write(bool value) { write.Write(value); } public void Write(string value) { write.Write(value); } public void Write(ZDOID value) { write.Write(value.Value); }
    public void Write(ZPackage value) { byte[] bytes=value.GetArray();write.Write(bytes.Length);write.Write(bytes); }
    public int ReadInt() { return read.ReadInt32(); } public long ReadLong() { return read.ReadInt64(); } public float ReadSingle() { return read.ReadSingle(); } public bool ReadBool() { return read.ReadBoolean(); } public string ReadString() { return read.ReadString(); } public ushort ReadUShort() { return read.ReadUInt16(); } public ZDOID ReadZDOID() { return new ZDOID(ReadLong()); }
    public void ReadPackage(ref ZPackage value) { value=new ZPackage(read.ReadBytes(ReadInt())); }
    public byte[] GetArray() { return data.ToArray(); } public int GetPos() { return (int)data.Position; } public int Size() { return (int)data.Length; } public void SetPos(int pos) { data.Position=pos; }
}
public static class ZDOVars { public const int s_health=1; }
public sealed class ZDO
{
    public ZDOID m_uid; public long Owner=11; public float Health=100;
    public bool IsValid() { return true; } public long GetOwner() { return Owner; } public float GetFloat(int key,float fallback) { return Health; }
}
public sealed class ZDOMan
{
    public static ZDOMan instance=new ZDOMan(); public static long Session=11; public static long GetSessionID() { return Session; }
    public readonly Dictionary<ZDOID,ZDO> Objects=new Dictionary<ZDOID,ZDO>(); public ZDO GetZDO(ZDOID id) { ZDO value;return Objects.TryGetValue(id,out value)?value:null; }
}
public sealed class ZNetView : UnityEngine.Object
{
    public ZDO Data; public bool IsValid() { return Data!=null; } public ZDO GetZDO() { return Data; }
}
public sealed class Player : UnityEngine.Object
{
    public static Player m_localPlayer; public ZNetView View; public T GetComponent<T>() where T:class { return View as T; }
}
public sealed class ZSocket
{
    public string Host="socket-user"; public string GetHostName() { return Host; }
}
public sealed class ZNetPeer
{
    public long m_uid; public ZDOID m_characterID; public readonly ZRpc m_rpc=new ZRpc(); public ZSocket m_socket=new ZSocket(); public bool Ready=true;
    public bool IsReady() { return Ready; } public UnityEngine.Vector3 GetRefPos() { return new UnityEngine.Vector3(); }
}
public sealed class ZNet : UnityEngine.Object
{
    public static ZNet instance; public bool Server,Dedicated,Admin; public string AdminIdentity; public ZNetPeer ServerPeer;
    public readonly List<ZNetPeer> Peers=new List<ZNetPeer>();
    public bool IsServer() { return Server; } public bool IsDedicated() { return Dedicated; } public List<ZNetPeer> GetPeers() { return Peers; }
    public ZNetPeer GetPeer(long uid) { foreach(var peer in Peers)if(peer.m_uid==uid)return peer;return null; } public ZNetPeer GetServerPeer() { return ServerPeer; }
    public UnityEngine.Vector3 GetReferencePosition() { return new UnityEngine.Vector3(); }
    public bool IsAdmin(string identity) { AdminIdentity=identity;return Admin; }
}
public sealed class ZRpc
{
    public Action<ZRpc,ZPackage> Handler; public readonly List<ZPackage> Sent=new List<ZPackage>();
    public void Register<T>(string name,Action<ZRpc,T> callback) { Handler=delegate(ZRpc rpc,ZPackage p) { callback(rpc,(T)(object)p); }; }
    public void Invoke(string name,ZPackage packet) { Sent.Add(new ZPackage(packet.GetArray())); }
}
public sealed class Terminal : UnityEngine.Object
{
    public readonly List<string> Lines=new List<string>(); public void AddString(string line) { Lines.Add(line); }
    public delegate void ConsoleEvent(ConsoleEventArgs args); public sealed class ConsoleEventArgs { public Terminal Context;public string[] Args; }
    public sealed class ConsoleCommand { public static ConsoleEvent Execute;public ConsoleCommand(string key,string help,ConsoleEvent action) { Execute=action; } }
}
namespace TooManyLagsStandalone
{
    internal static class TextEditingProtection { internal static bool Supported = true; internal static int Packets, Disconnects; internal static void Receive(ZNetPeer peer,long uid,ZPackage packet) { Packets++; } internal static void Disconnected(ZNetPeer peer) { Disconnects++; } }
    internal enum GridCoveragePattern { Square3x3, Rounded5x5, Rounded7x7 }
    [Flags] internal enum Kinds { None=0,Doors=1,Containers=2,Vehicles=4,TamedAnimals=8,Smelters=16,Incinerators=32,Signs=64,Portals=128,OtherInteractables=256 }
    internal static class NativeLatency { internal static float Read(ZRpc rpc) { return 80; } }
    internal static class RequestRouter { internal static void Relayed(long uid,ZPackage p) { }internal static void Forwarded(long uid,ZPackage p) { } }
    internal sealed class LocalAgent
    {
        internal bool MobsEnabled,Watching; internal int Received;
        internal void SetMobWatch(bool value) { Watching=value; } internal void Receive(Message kind,ZPackage p) { Received++; }
    }
    internal sealed class AuthorityController
    {
        internal int Capabilities; internal bool WatchMobs(long uid) { return true; } internal void CapabilityChanged(long uid) { Capabilities++; }
        internal readonly Endpoint Handoffs=new Endpoint(),Recipients=new Endpoint(); internal readonly MobEndpoint Mobs=new MobEndpoint();
        internal sealed class Endpoint { internal void Receive(long uid,Message message,ZPackage p) { } }
        internal sealed class MobEndpoint { internal void Report(long uid,ZDOID id,ushort revision,ZDOID target) { } }
    }
    internal static class Plugin
    {
        internal static Settings Options; internal static LocalAgent Local; internal static Network Network; internal static AuthorityController Controller;
        internal static ConfigCommands Commands; internal static bool TargetSupport=true;internal static bool HooksActive { get { return Local!=null; } }
        internal static int SessionChanges,CommandsExecuted;
        internal static void ApplySession(bool enabled,bool mobs,float interval,float overlap,float prediction)
        { Options.Session(enabled,mobs,interval,overlap,prediction);Local=enabled?(Local??new LocalAgent()):null;if(Local!=null)Local.MobsEnabled=mobs&&TargetSupport;SessionChanges++; }
        internal static string ConfigCommand(string command,bool server) { CommandsExecuted++;return Commands.Execute(command,server); }
        internal static void Warn(string message) { }
    }
}
