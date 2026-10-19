// Managed boundary fixtures. Production LocalAgent/Handoffs/RequestRouter are
// compiled unchanged; this does not simulate Unity physics or network transport.
using System;
using System.Collections.Generic;
using System.IO;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public struct Vector3 { public float x, y, z; public float sqrMagnitude { get { return x*x+y*y+z*z; } } public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3 { x=a.x-b.x,y=a.y-b.y,z=a.z-b.z }; } }
    public struct Quaternion { public Vector3 eulerAngles; }
    public class Transform { public Vector3 position; public Transform parent; }
    public class GameObject : Object
    {
        public bool activeInHierarchy = true; public readonly Transform transform = new Transform(); internal TooManyLagsStandalone.Kinds Kind;
        private readonly List<Component> components = new List<Component>();
        public T Add<T>() where T : Component, new() { T c = new T(); c.gameObject = this; components.Add(c); return c; }
        public T GetComponent<T>() where T : class { foreach (Component c in components) if (c is T) return c as T; return null; }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : class { var found = new List<T>(); foreach (Component c in components) if (c is T) found.Add(c as T); return found.ToArray(); }
    }
    public class Component : Object
    { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } public T GetComponent<T>() where T:class { return gameObject.GetComponent<T>(); } public T GetComponentInParent<T>(bool inactive) where T:class { return gameObject.GetComponent<T>(); } public T[] GetComponentsInChildren<T>(bool b) where T:class { return gameObject.GetComponentsInChildren<T>(b); } }
    public class MonoBehaviour : Component { public readonly HashSet<string> Invoking = new HashSet<string>(); public bool IsInvoking(string method) { return Invoking.Contains(method); } }
    public static class Time { public static float unscaledTime; public static int frameCount; }
}
public struct Vector2s : IEquatable<Vector2s> { public short x,y; public bool Equals(Vector2s b) { return x==b.x && y==b.y; } public override bool Equals(object b) { return b is Vector2s && Equals((Vector2s)b); } public override int GetHashCode() { return x*397^y; } }
public struct ZDOID : IEquatable<ZDOID>
{
    public long Value; public static readonly ZDOID None = new ZDOID(); public ZDOID(long v) { Value=v; }
    public bool IsNone() { return Value==0; } public bool Equals(ZDOID b) { return Value==b.Value; } public override bool Equals(object b) { return b is ZDOID && Equals((ZDOID)b); } public override int GetHashCode() { return Value.GetHashCode(); }
    public static bool operator ==(ZDOID a,ZDOID b) { return a.Equals(b); } public static bool operator !=(ZDOID a,ZDOID b) { return !a.Equals(b); }
}
public sealed class ZPackage
{
    private readonly MemoryStream data; private readonly BinaryReader read; private readonly BinaryWriter write;
    public ZPackage() : this(new byte[0]) { } public ZPackage(byte[] bytes) { data=new MemoryStream(); data.Write(bytes,0,bytes.Length); data.Position=0; read=new BinaryReader(data); write=new BinaryWriter(data); }
    public void Write(long n) { write.Write(n); } public void Write(float n) { write.Write(n); } public void Write(int n) { write.Write(n); } public void Write(uint n) { write.Write(n); } public void Write(ushort n) { write.Write(n); } public void Write(bool n) { write.Write(n); } public void Write(ZDOID n) { Write(n.Value); } public void Write(string n) { write.Write(n); }
    public void Write(ZPackage p) { byte[] bytes=p.GetArray(); Write(bytes.Length); write.Write(bytes); }
    public long ReadLong() { return read.ReadInt64(); } public float ReadSingle() { return read.ReadSingle(); } public int GetPos() { return (int)data.Position; } public int ReadInt() { return read.ReadInt32(); } public uint ReadUInt() { return read.ReadUInt32(); } public ushort ReadUShort() { return read.ReadUInt16(); } public bool ReadBool() { return read.ReadBoolean(); } public ZDOID ReadZDOID() { return new ZDOID(ReadLong()); }
    public void ReadPackage(ref ZPackage p) { p = new ZPackage(read.ReadBytes(ReadInt())); } public string ReadString() { return read.ReadString(); } public byte[] GetArray() { return data.ToArray(); } public void SetPos(int n) { data.Position=n; } public int Size() { return (int)data.Length; }
}
public static class HashFixture { public static int GetStableHashCode(this string text) { unchecked { int h=17; foreach(char c in text) h=h*31+c; return h; } } }
public static class ZDOVars { public const int s_user=1,s_inUse=2,s_follow=3,s_health=8,s_attachJointHash=9,s_tamed=10; }
public sealed class ZDO
{
    public ZDOID m_uid; public ushort OwnerRevision; public uint DataRevision; public bool Valid=true; public long Owner=11; public UnityEngine.Vector3 Point; public UnityEngine.Quaternion Rotation;
    private readonly Dictionary<object,object> values = new Dictionary<object,object>();
    public bool IsValid() { return Valid; } public bool IsOwner() { return Owner==ZDOMan.GetSessionID(); } public long GetOwner() { return Owner; } public UnityEngine.Vector3 GetPosition() { return Point; }
    public int Prefab=1; public int GetPrefab() { return Prefab; }
    public static KeyValuePair<int,int> GetHashZDOID(string key) { return new KeyValuePair<int,int>(key.GetStableHashCode(),(key+".second").GetStableHashCode()); }
    public UnityEngine.Quaternion GetRotation() { return Rotation; }
    public void SetOwner(long owner) { Owner=owner; OwnerRevision++; }
    public long GetLong(int key,long fallback=0) { object n; return values.TryGetValue(key,out n)?Convert.ToInt64(n):fallback; }
    public int GetInt(int key,int fallback=0) { return (int)GetLong(key,fallback); }
    public bool GetBool(int key) { return GetLong(key,0)!=0; }
    public float GetFloat(int key,float fallback=0) { return GetLong(key,(long)fallback); }
    public string GetString(int key) { object n; return values.TryGetValue(key,out n)?(string)n:String.Empty; }
    public ZDOID GetZDOID(KeyValuePair<int,int> key) { object n; return values.TryGetValue(key,out n)?(ZDOID)n:ZDOID.None; }
    public void Set(int key,long value) { values[key]=value; } public void Set(int key,string value) { values[key]=value; } public void Set(KeyValuePair<int,int> key,ZDOID value) { values[key]=value; }
}
public sealed class ZDOMan
{
    public static ZDOMan instance = new ZDOMan(); public static long Session=11;
    public readonly Dictionary<ZDOID,ZDO> Objects = new Dictionary<ZDOID,ZDO>(); public static long GetSessionID() { return Session; }
    public ZDO GetZDO(ZDOID id) { ZDO d; return Objects.TryGetValue(id,out d)?d:null; } public void ForceSendZDO(ZDOID id) { } public void ForceSendZDO(long peer,ZDOID id) { }
}
public class ZNetView : UnityEngine.Component { public ZDO Data; public bool m_persistent=true,m_distant; public readonly List<string> Replies=new List<string>(); public bool IsValid() { return Data!=null && Data.IsValid(); } public ZDO GetZDO() { return Data; } public bool IsOwner() { return Data.IsOwner(); } public void InvokeRPC(long peer,string response,params object[] args) { Replies.Add(peer+":"+response+":"+args[0]); } }
public sealed class ZNetScene : UnityEngine.Object
{
    public readonly Dictionary<int,UnityEngine.GameObject> Prefabs=new Dictionary<int,UnityEngine.GameObject>();
    public UnityEngine.GameObject GetPrefab(int hash) { UnityEngine.GameObject root;return Prefabs.TryGetValue(hash,out root)?root:null; }
#if MOB_INTEGRATION
    public bool ForbidLiveInstances;
    private void InstanceAccess() { if(ForbidLiveInstances)throw new Exception("Dedicated coordinator attempted to read a live scene instance"); }
#else
    private void InstanceAccess() { }
#endif
    public static ZNetScene instance = new ZNetScene(); public bool Ready=true; public Func<UnityEngine.Vector3,bool> Readiness; public readonly List<UnityEngine.Vector3> ReadinessPoints=new List<UnityEngine.Vector3>(); public readonly Dictionary<ZDOID,ZNetView> Views = new Dictionary<ZDOID,ZNetView>();
    public ZNetView FindInstance(ZDO d) { InstanceAccess();ZNetView v; return Views.TryGetValue(d.m_uid,out v)?v:null; } public UnityEngine.GameObject FindInstance(ZDOID id) { InstanceAccess();ZNetView v; return Views.TryGetValue(id,out v)?v.gameObject:null; } public bool IsAreaReady(UnityEngine.Vector3 p) { InstanceAccess();ReadinessPoints.Add(p);return Readiness!=null?Readiness(p):Ready; }
    public static bool InActiveArea(UnityEngine.Vector3 p,Vector2s reference) { Vector2s zone=ZoneSystem.GetZone(p);return Math.Abs(zone.x-reference.x)<=2 && Math.Abs(zone.y-reference.y)<=2; }
}
public sealed class ZNet : UnityEngine.Object
{
    public static ZNet instance=new ZNet(); public static bool IsSinglePlayer; public bool Server=true, Dedicated; public ZNetPeer ServerPeer;
    public UnityEngine.Vector3 Reference; public readonly List<ZNetPeer> Peers=new List<ZNetPeer>(); public bool IsServer() { return Server; } public bool IsDedicated() { return Dedicated; }
    public List<ZNetPeer> GetPeers() { return Peers; } public ZNetPeer GetPeer(long uid) { foreach(var p in Peers)if(p.m_uid==uid)return p;return null; } public ZNetPeer GetServerPeer() { return ServerPeer; } public UnityEngine.Vector3 GetReferencePosition() { return Reference; }
}
public sealed class ZNetPeer
{ public long m_uid;public ZRpc m_rpc=new ZRpc();public ZDOID m_characterID;public bool Ready=true;public bool IsReady() { return Ready; } public UnityEngine.Vector3 GetRefPos() { return new UnityEngine.Vector3(); } }
public sealed class ZRpc
{
    public Action<ZRpc,ZPackage> Handler;public readonly List<ZPackage> Sent=new List<ZPackage>();
    public ISocket Socket; public ISocket GetSocket() { return Socket; }
    public void Register<T>(string name,Action<ZRpc,T> callback) { Handler=delegate(ZRpc rpc,ZPackage p) { callback(rpc,(T)(object)p); }; }
    public void Invoke(string name,ZPackage p) { Sent.Add(new ZPackage(p.GetArray())); }
}
public interface ISocket
{
    bool IsConnected();
    void GetConnectionQuality(out float local,out float remote,out int ping,out float outgoing,out float incoming);
}
public class ZSteamSocket : ISocket
{
    private Steamworks.HSteamNetConnection m_con; public bool Connected=true;public int Ping=70;public float Quality=1;public bool Throw;
    public ZSteamSocket(uint handle=1) { m_con=new Steamworks.HSteamNetConnection(handle); }
    public Steamworks.HSteamNetConnection Handle { get { return m_con; } }
    public bool IsConnected() { return Connected; }
    public void GetConnectionQuality(out float local,out float remote,out int ping,out float outgoing,out float incoming)
    { if(Throw)throw new Exception("Missing telemetry");local=remote=Quality;ping=Ping;outgoing=incoming=0; }
}
namespace Steamworks
{
    public enum EResult { k_EResultFail=0,k_EResultOK=1 }
    public struct HSteamNetConnection
    {
        public uint Value;public HSteamNetConnection(uint value) { Value=value; }public static readonly HSteamNetConnection Invalid=new HSteamNetConnection();
        public static bool operator ==(HSteamNetConnection a,HSteamNetConnection b) { return a.Value==b.Value; }public static bool operator !=(HSteamNetConnection a,HSteamNetConnection b) { return !(a==b); }
        public override bool Equals(object o) { return o is HSteamNetConnection && this==(HSteamNetConnection)o; }public override int GetHashCode() { return (int)Value; }
    }
    public struct SteamNetConnectionRealTimeStatus_t { public int m_nPing;public float m_flConnectionQualityLocal; }
    public struct SteamNetConnectionRealTimeLaneStatus_t { }
    public static class SteamGameServerNetworkingSockets
    {
        public static EResult Result=EResult.k_EResultOK;public static int Ping=55;public static float Quality=1;public static bool Throw;
        public static EResult GetConnectionRealTimeStatus(HSteamNetConnection c,ref SteamNetConnectionRealTimeStatus_t status,int count,ref SteamNetConnectionRealTimeLaneStatus_t lane)
        { if(Throw)throw new Exception("Steam unavailable");status.m_nPing=Ping;status.m_flConnectionQualityLocal=Quality;return Result; }
    }
}
public sealed class ZoneSystem : UnityEngine.Object { public static ZoneSystem instance=new ZoneSystem(); public bool Loaded=true; public bool IsZoneLoaded(Vector2s zone) { return Loaded; } public static Vector2s GetZone(UnityEngine.Vector3 p) { return new Vector2s { x=(short)Math.Floor((p.x+32)/64),y=(short)Math.Floor((p.z+32)/64) }; } }
public class Character : UnityEngine.Component
{
    public enum Faction { Players, Monsters } public bool Dead, Boss, Tamed, Attack, Stagger, Attached, Riding; public Faction Side=Faction.Monsters;
    public bool IsDead() { return Dead; } public bool IsBoss() { return Boss; } public bool IsTamed() { return Tamed; } public bool InAttack() { return Attack; } public bool IsStaggering() { return Stagger; } public bool IsAttached() { return Attached; } public bool IsRiding() { return Riding; }
    public bool IsPlayer() { return this is Player; } public Faction GetFaction() { return Side; } public ZDOID GetZDOID() { return GetComponent<ZNetView>().Data.m_uid; }
    public long GetOwner() { var view=GetComponent<ZNetView>();return view && view.IsValid()?view.Data.GetOwner():0; }
}
public class Attack { public bool Done; public bool IsDone() { return Done; } }
public class Humanoid : Character { private Attack m_currentAttack; public Attack CurrentAttack { get { return m_currentAttack; } set { m_currentAttack=value; } } }
public class Player : Humanoid { public static Player m_localPlayer; }
public class BaseAI : UnityEngine.Component
{
    private ZNetView m_nview; public void Init() { m_nview=GetComponent<ZNetView>(); } public bool Alerted, Charging; public bool IsCharging() { return Charging; }
    private void SetAlerted(bool value) { Alerted=value; }
    public static bool IsEnemy(Character a,Character b) { return a.Side!=b.Side; }
    public static List<BaseAI> GetAllInstances() { return new List<BaseAI>(); }
    public ZNetView View { get { return m_nview; } }
}
public class MonsterAI : BaseAI { private Character m_targetCreature;public Character Target { get { return m_targetCreature; } set { m_targetCreature=value; } } private float m_timeSinceSensedTargetCreature=9; public float Sensed { get { return m_timeSinceSensedTargetCreature; } } }
public class Tameable : UnityEngine.Component { public bool Rider, m_commandable, Summon; public bool HaveRider() { return Rider; } }
public class Vagon : UnityEngine.Component { public bool Used; private Humanoid m_useRequester; public Humanoid Requester { get { return m_useRequester; } set { m_useRequester=value; } } public bool InUse() { return Used; } }
public class Ship : UnityEngine.Component { private readonly List<Player> m_players=new List<Player>(); public List<Player> Passengers { get { return m_players; } } }
public class Container : UnityEngine.Component { public enum PrivacySetting { Public,Private } public PrivacySetting m_privacy; public ZNetView m_rootObjectOverride; private bool m_loading; public bool Loading { get { return m_loading; } set { m_loading=value; } } public bool Used; public bool IsInUse() { return Used; } }
public class Incinerator : UnityEngine.Component { private bool isInUse; public bool Burning { get { return isInUse; } set { isInUse=value; } } public Container m_container; }
public interface Interactable { }
public class Door : UnityEngine.MonoBehaviour, Interactable { }
public class ItemStand : UnityEngine.MonoBehaviour, Interactable { }
public class Fermenter : UnityEngine.MonoBehaviour, Interactable { }
public class OfferingBowl : UnityEngine.MonoBehaviour, Interactable { }
public class ArmorStand : UnityEngine.MonoBehaviour, Interactable { }
public class Chair : UnityEngine.MonoBehaviour, Interactable { }
public class Barber : UnityEngine.MonoBehaviour, Interactable { }
public class Bed : UnityEngine.MonoBehaviour, Interactable { }
public class CraftingStation : UnityEngine.MonoBehaviour, Interactable { }
public class PrivateArea : UnityEngine.MonoBehaviour, Interactable { }
public class Sign : UnityEngine.MonoBehaviour, Interactable { }
public class TeleportWorld : UnityEngine.MonoBehaviour, Interactable { }
public class Teleport : UnityEngine.MonoBehaviour, Interactable { }
public sealed class InventoryGui : UnityEngine.Object { public static InventoryGui instance=new InventoryGui(); private Container m_currentContainer; public Container Current { get { return m_currentContainer; } set { m_currentContainer=value; } } }
public sealed class ZRoutedRpc
{
    public static ZRoutedRpc instance=new ZRoutedRpc(); public readonly List<RoutedRPCData> Delivered=new List<RoutedRPCData>(); public readonly List<string> Responses=new List<string>();
    public sealed class RoutedRPCData
    {
        public long m_msgID,m_senderPeerID,m_targetPeerID; public ZDOID m_targetZDO; public int m_methodHash; public ZPackage m_parameters=new ZPackage();
        public void Serialize(ZPackage p) { p.Write(m_msgID);p.Write(m_senderPeerID);p.Write(m_targetPeerID);p.Write(m_targetZDO);p.Write(m_methodHash);p.Write(m_parameters); }
        public void Deserialize(ZPackage p) { m_msgID=p.ReadLong();m_senderPeerID=p.ReadLong();m_targetPeerID=p.ReadLong();m_targetZDO=p.ReadZDOID();m_methodHash=p.ReadInt();p.ReadPackage(ref m_parameters); }
    }
    private void HandleRoutedRPC(RoutedRPCData p) { Delivered.Add(p); } private void RouteRPC(RoutedRPCData p) { Delivered.Add(p); }
    public void InvokeRoutedRPC(long peer,ZDOID id,string method,params object[] args) { Responses.Add(method); }
}
namespace TooManyLagsStandalone
{
    internal static class TextEditingProtection { internal static bool Supported = true; internal static int Packets; internal static void Receive(ZNetPeer peer,long uid,ZPackage packet) { Packets++; } internal static void Disconnected(ZNetPeer peer) { } }
    internal enum Kinds { None, Containers, Incinerators, TamedAnimals, Smelters, Doors, Seats, Beds, Workstations, Wards, Signs, Portals, OtherInteractables, ItemStands, Fermenters, Offerings, Vehicles }
    internal static class Plugin { internal const string Id="botanic.toomanylags.standalone";internal static LocalAgent Local; internal static Network Network=new Network(); internal static AuthorityController Controller; internal static readonly Settings Options=new Settings();internal static bool HooksActive=true,TargetSupport=true,SessionEnabled;internal static float CameraOverlap,CameraPrediction,CameraInterval;internal static void ApplySession(bool enabled,bool mobs,float interval,float degrees,float prediction) { SessionEnabled=enabled;Local.MobsEnabled=mobs;ApplyCameraSettings(interval,degrees,prediction); } internal static void ApplyCameraSettings(float interval,float degrees,float prediction) { CameraInterval=interval;CameraOverlap=degrees;CameraPrediction=prediction; } internal static void Warn(string m) { } }
    internal sealed class Settings { internal float PingInterval=2, CameraInterval=.05f,CameraOverlap=30,CameraPrediction=.1f,Margin=30,MobRadius=6;internal int HighPing=200;internal bool Enabled=true,Mobs=true; }
    internal static class ConfigConsole { internal static string Request(ZNetPeer peer,string command) { return null; } internal static void Reply(ZNetPeer peer,int id,string reply) { } }
#if !NETWORK_AUDIT
    internal struct PlayerPeer { internal long Uid; internal ZDO Character; }
    internal enum Message { Prepare, Prepared, Commit, Result, Wake, Cancel, Target, Forward, ActivationCheck, Activated, Status, Ready, ReadyResult, ReadyLost, Relay }
    internal sealed class Network
    {
        internal sealed class Envelope { internal long Uid; internal Message Message; internal ZPackage Data; }
        internal readonly List<Envelope> Sent=new List<Envelope>();
        internal readonly HashSet<long> AdditionalCapable=new HashSet<long>();
        internal readonly HashSet<long> LegacyText=new HashSet<long>();
        internal readonly Dictionary<long,float> Latencies=new Dictionary<long,float>();
        internal float Latency(long uid) { float ping;return Latencies.TryGetValue(uid,out ping)?ping:20f; }
        internal Action<Envelope> OnSend;
        internal void Send(long uid,Message message,Action<ZPackage> write) { ZPackage p=new ZPackage(); write(p); p.SetPos(0);var envelope=new Envelope { Uid=uid,Message=message,Data=p }; Sent.Add(envelope);if(OnSend!=null)OnSend(envelope); }
        internal void ToServer(Message m,Action<ZPackage> write) { Send(999,m,write); }
        internal bool MobCapable(long uid) { return uid==11 || uid==22; }
        internal bool Capable(long uid) { return uid==11 || uid==22 || AdditionalCapable.Contains(uid); }
        internal bool TextCapable(long uid) { return Capable(uid) && !LegacyText.Contains(uid); }
    }
#endif
    internal sealed class AuthorityController
    {
        internal bool Active=true; internal long DesiredOwner=22; internal readonly Handoffs Handoffs; internal int Wakes, Rejections; internal static bool Finite(UnityEngine.Vector3 p) { return !Single.IsNaN(p.x)&&!Single.IsNaN(p.y)&&!Single.IsNaN(p.z)&&!Single.IsInfinity(p.x)&&!Single.IsInfinity(p.y)&&!Single.IsInfinity(p.z); }
#if MOB_INTEGRATION
        internal readonly MobPolicy Mobs;
#else
        internal readonly MobSink Mobs=new MobSink();
#endif
        internal readonly List<PlayerPeer> Players=new List<PlayerPeer>();
        internal readonly RecipientReadiness Recipients;internal bool Shared(long uid) { return true; }
        private readonly Dictionary<ZDOID,RecipientRequirement> requirements=new Dictionary<ZDOID,RecipientRequirement>();
        internal int RecipientLookups;
        internal int AvailabilityLookups;
        internal void RecordRecipient(ZDO d,long owner,ushort revision,uint state) { RecipientRequirement r;requirements.TryGetValue(d.m_uid,out r);r.Include(owner,revision,state);requirements[d.m_uid]=r; }
        internal bool TryRecipientState(ZDO d,long owner,out uint state) { RecipientLookups++;RecipientRequirement r;state=0;if(!requirements.TryGetValue(d.m_uid,out r)||!r.Matches(owner,d.OwnerRevision))return false;state=r.State;return true; }
        internal bool MobPair=true;internal bool WatchMobs(long uid) { return MobPair; }
        internal AuthorityController() { Handoffs=new Handoffs(this); Recipients=new RecipientReadiness(this);
#if MOB_INTEGRATION
            Mobs=new MobPolicy(this);
#endif
        }
        internal bool Available(long uid,UnityEngine.Vector3 p) { AvailabilityLookups++;return uid==11 || uid==22 || uid==33 || uid==34; }
        internal bool NativeTransferSafe(ZDO data) { return ObjectCatalog.Classify(ZNetScene.instance.FindInstance(data).gameObject)==Kinds.Doors; }
        internal int CapabilityUpdates;internal void CapabilityChanged(long uid) { CapabilityUpdates++; }
        internal long Desired(ZDO d,Kinds k,ZDOID t) {
#if MOB_INTEGRATION
            if(k==Kinds.None)return Mobs.Desired(d,t);
#endif
            return DesiredOwner;
        }
        internal void Wake(ZDOID id) { Wakes++; } internal void Rejected(ZDOID id) { Rejections++; }
        internal bool IsManaged(ZDO d,int m) { return d!=null; }
    }
    internal sealed class MobSink { internal void Report(long sender,ZDOID id,ushort revision,ZDOID target) { } }
    internal static class ObjectCatalog
    {
        internal static Kinds Classify(UnityEngine.GameObject root) { return root.Kind; } internal static bool IsSummoned(Tameable t) { return t.Summon; } internal static bool Stationary(UnityEngine.GameObject root,Kinds kind) { return kind!=Kinds.None&&kind!=Kinds.TamedAnimals&&!root.GetComponent<Vagon>(); }
        internal static bool RefillRequest(Kinds k,int m) { return k==Kinds.Smelters && m=="RPC_AddOre".GetStableHashCode(); }
        internal static bool IsRefillMethod(int m) { return m=="RPC_AddOre".GetStableHashCode(); }
        internal static bool PotentialOwnerRequest(int m) { return m=="RPC_RequestOpen".GetStableHashCode() || m=="UseDoor".GetStableHashCode() || m=="RPC_AddOre".GetStableHashCode() || m=="RPC_Damage".GetStableHashCode(); }
        internal static bool OwnerRequest(Kinds k,int m) { return m=="RPC_RequestOpen".GetStableHashCode() || m=="UseDoor".GetStableHashCode() || m=="RPC_AddOre".GetStableHashCode(); }
    }
#if !MOB_INTEGRATION
    internal static class MobPolicy { internal static readonly KeyValuePair<int,int> TargetKey=new KeyValuePair<int,int>(4,5); internal const int WriterKey=6, RevisionKey=7; internal static bool Eligible(ZDO d) { return d!=null && d.IsValid(); } internal static bool HostilePrefab(int hash) { return hash!=0; } }
#endif
    internal static class TargetEvents { internal static Character Read(MonsterAI ai) { return ai.Target; } internal static void Adopt(MonsterAI ai,Character t) { ai.Target=t; } }
    internal sealed class OwnershipCadence { private float next; private readonly float interval; internal OwnershipCadence(float n) { interval=n; } internal bool Due(float n) { if(n<next)return false;next=n+interval;return true; } }
}
