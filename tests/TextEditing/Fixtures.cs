using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) { return o != null; } }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public class Transform { public Vector3 position; }
    public class Component : Object
    {
        public GameObject gameObject; public readonly Transform transform=new Transform();
        public T GetComponent<T>() where T:class { return gameObject.GetComponent<T>(); }
    }
    public class GameObject : Object
    {
        public bool activeInHierarchy=true; private readonly Dictionary<Type,object> parts=new Dictionary<Type,object>();
        public T Add<T>() where T:Component,new() { var c=new T { gameObject=this };parts[typeof(T)]=c;return c; }
        public T GetComponent<T>() where T:class { object c;return parts.TryGetValue(typeof(T),out c)?c as T:null; }
    }
    public static class Time { public static float unscaledTime; }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public long Value; public ZDOID(long id) { Value=id; }
    public bool IsNone() { return Value==0; } public bool Equals(ZDOID other) { return Value==other.Value; }
    public override bool Equals(object o) { return o is ZDOID && Equals((ZDOID)o); } public override int GetHashCode() { return Value.GetHashCode(); }
}
public sealed class ZPackage
{
    private readonly MemoryStream stream;private readonly BinaryReader read;private readonly BinaryWriter write;
    public ZPackage():this(new byte[0]) { }
    public ZPackage(byte[] bytes) { stream=new MemoryStream();stream.Write(bytes,0,bytes.Length);stream.Position=0;read=new BinaryReader(stream);write=new BinaryWriter(stream); }
    public byte[] GetArray() { return stream.ToArray(); } public int Size() { return (int)stream.Length; }
    public void Write(int v) { write.Write(v); } public void Write(long v) { write.Write(v); } public void Write(uint v) { write.Write(v); }
    public void Write(ZDOID v) { write.Write(v.Value); } public int ReadInt() { return read.ReadInt32(); }
    public long ReadLong() { return read.ReadInt64(); } public uint ReadUInt() { return read.ReadUInt32(); } public ZDOID ReadZDOID() { return new ZDOID(read.ReadInt64()); }
}
public sealed class ZRpc
{
    private readonly Dictionary<string,Action<ZRpc,ZPackage>> handlers=new Dictionary<string,Action<ZRpc,ZPackage>>();
    public readonly List<ZPackage> Sent=new List<ZPackage>();
    public void Register<T>(string name,Action<ZRpc,T> handler) { handlers[name]=(Action<ZRpc,ZPackage>)(object)handler; }
    public void Invoke(string name,ZPackage p) { Sent.Add(new ZPackage(p.GetArray())); }
    public void Deliver(ZPackage p) { handlers["TooManyLags_TextEditing_1"](this,new ZPackage(p.GetArray())); }
}
public sealed class ZNetPeer
{
    public long m_uid;public bool m_server,Ready=true,TextSupported=true; public ZRpc m_rpc=new ZRpc(); public ZDOID m_characterID;
    public bool IsReady() { return Ready; }
}
public sealed class ZNet : UnityEngine.Object
{
    public static ZNet instance;public bool Server;public readonly List<ZNetPeer> Peers=new List<ZNetPeer>();
    public bool IsServer() { return Server; } public List<ZNetPeer> GetPeers() { return Peers; }
    public ZNetPeer GetPeer(long uid) { return Peers.Find(p=>p.m_uid==uid); } public ZNetPeer GetServerPeer() { return Peers.Find(p=>p.m_server); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnNewConnection(ZNetPeer peer) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Disconnect(ZNetPeer peer) { Peers.Remove(peer); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Shutdown(bool save=true) { }
}
public sealed class ZDO
{
    public ZDOID m_uid;public long Owner;public ushort OwnerRevision;public uint DataRevision;public bool Valid=true,Text=true;public float Health=1;public UnityEngine.Vector3 Position;
    public bool IsValid() { return Valid; } public long GetOwner() { return Owner; } public bool IsOwner() { return Owner==ZDOMan.Session; }
    public UnityEngine.Vector3 GetPosition() { return Position; } public float GetFloat(int key,float fallback) { return Health; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetOwnerInternal(long owner) { Owner=owner;if(TooManyLags.AuthorityController.TextEditOwnerObserved!=null)TooManyLags.AuthorityController.TextEditOwnerObserved(this); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Reset() { if(TooManyLags.AuthorityController.TextEditDestroyed!=null)TooManyLags.AuthorityController.TextEditDestroyed(this);Valid=false; }
}
public static class ZDOVars { public const int s_health=1; }
public sealed class ZDOMan
{
    public static ZDOMan instance;public static long Session;public int Sends;
    public readonly Dictionary<ZDOID,ZDO> Objects=new Dictionary<ZDOID,ZDO>();
    public ZDO GetZDO(ZDOID id) { ZDO data;return Objects.TryGetValue(id,out data)?data:null; }
    public void ForceSendZDO(long peer,ZDOID id) { Sends++; }
}
public sealed class ZNetView : UnityEngine.Component
{
    public ZDO Data;public bool IsValid() { return Data!=null && Data.IsValid(); } public ZDO GetZDO() { return Data; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void ResetZDO() { Data=null; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroy() { }
}
public interface TextReceiver { void SetText(string text); }
public class Sign : UnityEngine.Component,TextReceiver { public int Saves;public void SetText(string text) { Saves++; } }
public sealed class TeleportWorld : UnityEngine.Component,TextReceiver { public void SetText(string text) { } }
public sealed class CustomSign : Sign { }
public sealed class TextInput : UnityEngine.Object
{
    private TextReceiver m_queuedSign;public int Opened;public string Topic;public int Limit;public bool Visible;
    [MethodImpl(MethodImplOptions.NoInlining)] public void RequestText(TextReceiver sign,string topic,int charLimit) { Opened++;Topic=topic;Limit=charLimit;Visible=true;m_queuedSign=sign; }
    [MethodImpl(MethodImplOptions.NoInlining)] private void setText(string text) { if(m_queuedSign!=null)m_queuedSign.SetText(text);m_queuedSign=null; }
    public void Hide() { Visible=false; } public void Confirm() { setText("saved");Hide(); }
}
public sealed class Player : UnityEngine.Component
{
    public static Player m_localPlayer;public bool Dead;public bool IsDead() { return Dead; } public void Message(MessageHud.MessageType type,string text) { }
}
public sealed class MessageHud { public enum MessageType { Center } }
public static class PrivateArea { public static bool Allowed=true;public static bool CheckAccess(UnityEngine.Vector3 point) { return Allowed; } }
namespace TooManyLags
{
    internal static class Plugin { internal const string Id="test.textediting";internal static AuthorityController Controller;internal static void Warn(string text) { } }
    internal sealed class CooperativeStub { internal bool Busy;internal bool Reserved(ZDO data) { return Busy; } }
    internal sealed class AuthorityController
    {
        internal static Func<ZDO,long> TextEditTarget;internal static Func<ZDO,bool> TextEditLocked;internal static Action<ZDOID> TextEditNodeRemoved;
        internal static Action<ZDO> TextEditOwnerObserved,TextEditDestroyed;
        internal readonly CooperativeStub Cooperative=new CooperativeStub();internal bool Pending;internal int Prepares,Ends;
        internal bool IsActive=true;
        internal uint Floor;internal bool TryRecipientState(ZDO data,long recipient,out uint state) { state=Floor;return Floor!=0; }
        internal bool Managed=true;internal bool ManagesText(ZDO data) { return Managed; }
        internal bool PrepareTextEdit(ZDO data) { Prepares++;return data.Text; } internal void EndTextEdit(ZDO data) { Ends++; }
        internal bool TransferPending(ZDO data) { return Pending; }
    }
}
namespace TooManyLags.GridHandoffs
{
    internal static class Protocol { internal static bool TextEditingAvailable;internal static Func<ZDO,bool> TextEditBusy;internal static bool SupportsTextEditing(ZNetPeer peer) { return peer!=null && peer.Ready && peer.TextSupported; } }
}
namespace TooManyLags.PlayerSimulation
{
    internal static class Plugin { internal static AuthorityController Controller;internal static Network Network; }
    internal sealed class Network { internal static bool TextEditingAvailable;internal bool SupportsTextEditing(ZNetPeer peer) { return peer!=null && peer.TextSupported; } }
    internal sealed class LocalAgent { internal static Func<ZDO,bool> TextEditBusy; }
    internal sealed class HandoffsStub { internal bool Busy;internal bool Pending(ZDOID id) { return Busy; } }
    internal sealed class AuthorityController
    {
        internal static Func<ZDO,long> TextEditTarget;internal static Func<ZDO,bool> TextEditLocked;
        internal static Action<ZDO> TextEditOwnerObserved;internal static Action<ZDOID> TextEditDestroyed;
        internal readonly HandoffsStub Handoffs=new HandoffsStub();internal int Prepares,Ends;
        internal bool Managed=true;internal bool ManagesText(ZDO data) { return Managed; }
        internal bool PrepareTextEdit(ZDO data) { Prepares++;return data.Text; } internal void EndTextEdit(ZDO data) { Ends++; }
        internal static bool Finite(UnityEngine.Vector3 v) { return !Single.IsNaN(v.x) && !Single.IsNaN(v.y) && !Single.IsNaN(v.z) && !Single.IsInfinity(v.x) && !Single.IsInfinity(v.y) && !Single.IsInfinity(v.z); }
    }
}
