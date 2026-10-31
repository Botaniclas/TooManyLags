// Managed doubles for production combat ownership code. No Unity/network benchmark.
#pragma warning disable 0169, 0649, 0414
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FiresGhettoNetworkMod { public static class NetSim { public static bool Active { get; set; } } }
// Separate production suites execute the negotiated source/checkpoint modules;
// legacy policy fixtures deliberately retain their existing transport path.
namespace TooManyLags
{
    internal static class MobCombatState { internal static bool CanCheckpoint(ZNetPeer peer) { return false; } }
    internal static class MobSourceHandoff
    {
        internal static Func<ZDO,bool> SkipLocalCommit;
        internal static ZDOID LocalTarget { get { return ZDOID.None; } }
        internal static void Reset() { } internal static void Stop() { } internal static void Probe(ZNetPeer peer) { } internal static void Connect(ZNetPeer peer) { }
        internal static bool CanGrant(ZNetPeer peer) { return false; }
        internal static bool NeedsGrant(ZDO data, ZNetPeer peer) { return false; }
        internal static bool IsLocalCommit(ZDO data) { return false; }
        internal static bool Waiting(ZDO data) { return false; }
        internal static void InvalidatePresentationTiming() { }
        internal static bool TryCommit(BaseAI ai,ZDO data,ZDOID target,ZNetPeer peer) { return false; }
        internal static void Grant(ZDO data,ZNetPeer peer,List<KeyValuePair<ZDOID,long>> choices) { }
        internal static void Forget(ZDOID id) { }
    }
    internal static class MobNativePublication { internal static void Send(ZDO data,ZNetPeer peer) { } }
    internal static class MobHandoffVisual { internal static Func<ZDO,bool> SkipPresentation; internal static void Cancel(ZDOID id) { } internal static object CaptureSource(ZDO data,long destination) { return null; } internal static void CommitObserver(object state) { } }
    internal static class MobBridgeVisual { internal static int Cancelled; internal static void Cancel(ZDOID id) { ++Cancelled; } }
    internal static class MobBridgeObserver { internal static void Cancel(ZDOID id) { } }
    // Protocol runtime is exercised in bridge/observer suites. This policy
    // fixture models its cancellation boundary for current solo-test shutdown.
    internal static class MobBridgeProtocol
    {
        internal static void Cancel(ZDOID id) { MobBridgeVisual.Cancel(id); MobBridgeObserver.Cancel(id); }
    }
    internal static class MobFlightHandoff { internal static Func<ZDO,bool> NativeTransfer; internal static void Transfer(ZDO data,long destination) { data.SetOwner(destination); } }
}

namespace UnityEngine
{
    public class Object { public string name="fixture"; public bool Alive = true; public static implicit operator bool(Object v) { return v != null && v.Alive; } public static bool operator !(Object v) { return !(bool)v; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.Equals(b); }
        public static bool operator !=(Vector3 a, Vector3 b) { return !a.Equals(b); }
        public override bool Equals(object o) { if (!(o is Vector3)) return false; Vector3 v=(Vector3)o; return x==v.x && y==v.y && z==v.z; }
        public override int GetHashCode() { return x.GetHashCode()^y.GetHashCode()^z.GetHashCode(); }
    }
    public class Transform { public Transform parent; public Vector3 position; }
    public class GameObject : Object
    {
        public bool activeInHierarchy=true; public Transform transform=new Transform(); public int ComponentQueries;
        private readonly Dictionary<Type, Component> components=new Dictionary<Type, Component>();
        public T Add<T>(T c) where T:Component { c.gameObject=this; components[typeof(T)]=c; return c; }
        public T GetComponent<T>() where T:Component { ComponentQueries++;foreach(Component c in components.Values) if(c is T) return (T)c; return null; }
    }
    public class Component : Object { public GameObject gameObject; public bool isActiveAndEnabled=true; public Transform transform { get { return gameObject.transform; } } public T GetComponent<T>() where T:Component { return gameObject.GetComponent<T>(); } }
    public static class Time
    {
        private static double realtime;
        public static double AutoAdvance;
        public static double realtimeSinceStartupAsDouble {get{double now=realtime;realtime+=AutoAdvance;return now;}set{realtime=value;}}
    }
    public class UnitySynchronizationContext : System.Threading.SynchronizationContext
    {
        private readonly Queue<KeyValuePair<System.Threading.SendOrPostCallback,object>> queue=new Queue<KeyValuePair<System.Threading.SendOrPostCallback,object>>();
        public int Posts; public bool ThrowPost;
        public override void Post(System.Threading.SendOrPostCallback callback,object state)
        {if(ThrowPost)throw new InvalidOperationException("fixture post");Posts++;queue.Enqueue(new KeyValuePair<System.Threading.SendOrPostCallback,object>(callback,state));}
        public void Flush(){while(queue.Count!=0){var entry=queue.Dequeue();entry.Key(entry.Value);}}
    }
}
namespace BepInEx.Configuration
{
    public class ConfigEntry<T> { public T Value; public ConfigEntry(T v) { Value=v; } }
    public class ConfigDescription { public ConfigDescription(string text, object range) {} }
    public class AcceptableValueRange<T> { public AcceptableValueRange(T a,T b) {} }
    public class ConfigFile
    {
        public bool? EnableOverride, MobDefaultOverride; public readonly Dictionary<string,object> Defaults=new Dictionary<string,object>();
        public ConfigEntry<T> Bind<T>(string section,string key,T value,object description)
        { Defaults[key]=value; if(key=="EnableAIOwnership"&&EnableOverride.HasValue)value=(T)(object)EnableOverride.Value;if(key=="ServerOwnedMobsByDefault"&&MobDefaultOverride.HasValue)value=(T)(object)MobDefaultOverride.Value;return new ConfigEntry<T>(value); }
    }
}
public static class HashExtensions
{
    public static int GetStableHashCode(this string v) { unchecked { int h=17; foreach(char c in v) h=h*31+c; return h; } }
}
public struct ZDOID
{
    public long UserID; public uint ID;
    public ZDOID(long u,uint i) { UserID=u; ID=i; }
    public static ZDOID None { get { return new ZDOID(); } }
    public bool IsNone() { return UserID==0 && ID==0; }
    public override bool Equals(object o) { return o is ZDOID && this==(ZDOID)o; }
    public override int GetHashCode() { return UserID.GetHashCode()^(int)ID; }
    public static bool operator ==(ZDOID a,ZDOID b) { return a.UserID==b.UserID && a.ID==b.ID; }
    public static bool operator !=(ZDOID a,ZDOID b) { return !(a==b); }
}
public static class ZDOVars { public static int s_tamed=1,s_user=2,s_health=3; }
public static class ZDOExtraData { public static readonly HashSet<int> Session=new HashSet<int>(); public static void AddSessionHash(int hash) { Session.Add(hash); } }
public class ZDO
{
    public int Type,Prefab=42; public bool Persistent=true,Created; public int GetPrefab(){return Prefab;}
    public ZDOID m_uid; public uint OwnerRevision; public bool Valid=true, Held; public int Writes, OwnerWrites;
    private long owner; public UnityEngine.Vector3 Position;
    private readonly Dictionary<int,long> longs=new Dictionary<int,long>();
    private readonly Dictionary<int,float> floats=new Dictionary<int,float>();
    public ZDO(ZDOID id,long owner) { m_uid=id; this.owner=owner; }
    public bool IsValid() { return Valid; } public long GetOwner() { return owner; }
    public bool IsOwner() { return owner == ZDOMan.GetSessionID(); }
    public void SetOwner(long value) { OwnerWrites++; if(owner!=value) { owner=value; OwnerRevision++; } }
    public UnityEngine.Vector3 GetPosition() { return Position; }
    public Vector2s GetSector() { return ZoneSystem.GetZone(Position); }
    public bool GetBool(int hash) { return GetLong(hash,0)!=0; }
    public long GetLong(int hash,long fallback) { long v; return longs.TryGetValue(hash,out v)?v:fallback; }
    public float GetFloat(int hash,float fallback) { float v; return floats.TryGetValue(hash,out v)?v:fallback; }
    public void Set(int hash,long value) { longs[hash]=value; Writes++; }
    public void SetFloat(int hash,float value) { floats[hash]=value; }
    public static KeyValuePair<int,int> GetHashZDOID(string name) { return new KeyValuePair<int,int>((name+"_u").GetStableHashCode(),(name+"_i").GetStableHashCode()); }
    public void Set(KeyValuePair<int,int> hash,ZDOID id) { Set(hash.Key,id.UserID); Set(hash.Value,id.ID); }
    public ZDOID GetZDOID(KeyValuePair<int,int> hash) { return new ZDOID(GetLong(hash.Key,0),(uint)GetLong(hash.Value,0)); }
}
public class ZNetView : UnityEngine.Component { public ZDO Data; public bool m_persistent=true,m_distant; public int Forwarded; public long ForwardOwner; public HitData ForwardHit; public bool IsValid() { return Data!=null && Data.IsValid(); } public bool IsOwner() { return IsValid() && Data.GetOwner()==ZDOMan.Session; } public ZDO GetZDO() { return Data; } public void InvokeRPC(long owner,string name,params object[] args){Forwarded++;ForwardOwner=owner;ForwardHit=(HitData)args[0];}
    [MethodImpl(MethodImplOptions.NoInlining)] public void ResetZDO(){Data=null;}
    [MethodImpl(MethodImplOptions.NoInlining)] private void OnDestroy(){}
}
public class ZSyncTransform : UnityEngine.Component { public bool m_syncPosition=true; }
public class Character : UnityEngine.Component
{
    public enum Faction { Players=0,AnimalsVeg=1,ForestMonsters=2,Undead=4,Boss=5,PlayerSpawned=11 }
    public Faction Team=Faction.ForestMonsters;
    public virtual bool IsPlayer(){return false;} public Faction GetFaction(){return Team;}
    public static readonly List<Character> All=new List<Character>(); public static List<Character> GetAllCharacters(){return All;}
    public bool Dead,Boss,Tamed,Attached,Riding,Attack,Stagger;
    public bool IsDead(){return Dead;} public bool IsBoss(){return Boss;} public bool IsTamed(){return Tamed;}
    public bool IsAttached(){return Attached;} public bool IsRiding(){return Riding;} public bool InAttack(){return Attack;} public bool IsStaggering(){return Stagger;}
    public ZDOID GetZDOID() { return GetComponent<ZNetView>().GetZDO().m_uid; }
    [MethodImpl(MethodImplOptions.NoInlining)] private void RPC_Damage(HitData hit){}
}
public class Player : Character
{
    public override bool IsPlayer(){return true;}
    public static Player m_localPlayer;
    [MethodImpl(MethodImplOptions.NoInlining)] protected virtual void OnDestroy(){}
    public void Destroy(){OnDestroy();Alive=false;}
}
public class Tameable : UnityEngine.Component { public float m_unsummonDistance,m_unsummonOnOwnerLogoutSeconds; }
public class HitData {}
public class Humanoid : Character
{
    protected Attack m_currentAttack;
    public int StartedAttacks;
    public Attack BeginAttack(){Attack=true;return m_currentAttack=new Attack();}
    [MethodImpl(MethodImplOptions.NoInlining)] public bool StartAttack(Character target,bool secondaryAttack)
    { if(Attack)return false; StartedAttacks++;BeginAttack();return true; }
}
public class Attack
{
    private bool done;
    public bool IsDone(){return done;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void Stop(){if(done)return;done=true;}
}
public class BaseAI : UnityEngine.Component
{
    protected ZNetView m_nview; public Character Target; public bool Rider;
    public void SetView(ZNetView view) { m_nview=view; }
    public virtual Character GetTargetCreature(){return Target;} public bool HaveRider(){return Rider;}
    public virtual bool IsSleeping(){return false;}
    [MethodImpl(MethodImplOptions.NoInlining)] public virtual bool UpdateAI(float dt){return m_nview.IsOwner();}
    [MethodImpl(MethodImplOptions.NoInlining)] protected virtual void Awake(){}
}
public class MonsterAI : BaseAI
{
    internal Character m_targetCreature;
    public new Character Target {get{return m_targetCreature;}set{SetTarget(value);}}
    public override bool IsSleeping(){return Sleeping;}
    [MethodImpl(MethodImplOptions.NoInlining)] public override Character GetTargetCreature(){return m_targetCreature;}
    [MethodImpl(MethodImplOptions.NoInlining)] private void SetTarget(Character value){m_targetCreature=value;}
    public bool Sleeping,SelectOnUpdate,AttackAfterTarget,InsideUpdate; public Character NextTarget; public int DecisionsCompleted;
    public Action AfterSelection;
    [MethodImpl(MethodImplOptions.NoInlining)] public override bool UpdateAI(float dt)
    {if(!base.UpdateAI(dt))return false;if(Sleeping)return true;InsideUpdate=true;bool hear,see;UpdateTarget(null,dt,out hear,out see);
     if(SelectOnUpdate){SelectOnUpdate=false;SetTarget(NextTarget);}if(AfterSelection!=null)AfterSelection();
     if(AttackAfterTarget)GetComponent<Humanoid>().StartAttack(m_targetCreature,false);DecisionsCompleted++;InsideUpdate=false;return true;}
    [MethodImpl(MethodImplOptions.NoInlining)] private void UpdateTarget(Humanoid human,float dt,out bool hear,out bool see) { hear=see=true; }
}
public class ZPackage
{
    private readonly System.IO.MemoryStream stream=new System.IO.MemoryStream();
    public void Write(uint value){new System.IO.BinaryWriter(stream).Write(value);}
    public void Write(int value){new System.IO.BinaryWriter(stream).Write(value);}
    public void Write(long value){new System.IO.BinaryWriter(stream).Write(value);}
    public void Write(bool value){new System.IO.BinaryWriter(stream).Write(value);}
    public void Write(string value){new System.IO.BinaryWriter(stream).Write(value);}
    public void Write(ZDOID value){Write(value.UserID);Write(value.ID);}
    public uint ReadUInt(){return new System.IO.BinaryReader(stream).ReadUInt32();}
    public int ReadInt(){return new System.IO.BinaryReader(stream).ReadInt32();}
    public long ReadLong(){return new System.IO.BinaryReader(stream).ReadInt64();}
    public bool ReadBool(){return new System.IO.BinaryReader(stream).ReadBoolean();}
    public string ReadString(){return new System.IO.BinaryReader(stream).ReadString();}
    public ZDOID ReadZDOID(){return new ZDOID(ReadLong(),ReadUInt());}
    public ZPackage Rewind(){stream.Position=0;return this;}
    public int GetPos(){return (int)stream.Position;} public int Size(){return (int)stream.Length;}
}
public class ZRpc
{
    // Earlier suites intentionally deliver legacy native RPCs by hand. The
    // identified suite disables this adapter and exercises the real new endpoints.
    public static Action LegacySend;
    public int Sent,ModeSent,LastMode; public long Destination; public ZDOID Mob; private Action<ZRpc,int> receive;
    public int WatchSent,TargetSent; public long WatchOwner,WatchRevision,TargetRevision; public ZDOID WatchMob,TargetMob,TargetId;
    public int StatusRequests,StatusReplies,StatusTicket; public string StatusLine;
    public int HandoffResults; public bool HandoffAccepted; public string HandoffReason;
    public int AttackReadySent; public ZDOID AttackReadyMob; public long AttackReadyRevision;
    public long CommitToken,ResultToken; public ZPackage CommitPayload,ResultPayload; public int CommitResults;
    public int BoundaryTargets,BoundaryDecisions; public ZDOID BoundaryMob,BoundaryId; public ZPackage BoundaryPayload,DecisionPayload;
    public int SoloArms,SoloReadies,SoloMessages,SoloControls,SoloModes; public ZDOID SoloMob,SoloModeMob; public ZPackage SoloArmPayload,SoloReadyPayload,SoloModePayload; public string SoloMessage,SoloControl;
    public int LatencyRequests,LatencyReplies; public ZPackage LatencyPayload,LatencyReply;
    private readonly Dictionary<string,Delegate> handlers=new Dictionary<string,Delegate>();
    public void Register<T>(string name,Action<ZRpc,T> handler){handlers[name]=handler;if(name=="TooManyLags_AI_Mode")receive=(rpc,value)=>handler(rpc,(T)(object)value);}
    public void Register<T,U>(string name,Action<ZRpc,T,U> handler){handlers[name]=handler;}
    public void Register<T,U,V>(string name,Action<ZRpc,T,U,V> handler){handlers[name]=handler;}
    public void Receive(int mode){receive(this,mode);}
    public void ReceiveWatch(ZDOID id,long owner,long revision){((Action<ZRpc,ZDOID,long,long>)handlers["TooManyLags_AI_Watch"])(this,id,owner,revision);}
    public void ReceiveTarget(ZDOID id,ZDOID target,long revision){((Action<ZRpc,ZDOID,ZDOID,long>)handlers["TooManyLags_AI_Target"])(this,id,target,revision);}
    public void ReceiveAttackReady(ZDOID id,long revision){((Action<ZRpc,ZDOID,long>)handlers["TooManyLags_AI_AttackReady"])(this,id,revision);}
    public void ReceiveCommit(ZDOID id,long token,ZPackage payload){((Action<ZRpc,ZDOID,long,ZPackage>)handlers["TooManyLags_AI_Commit"])(this,id,token,payload.Rewind());}
    public void ReceiveCommitResult(ZDOID id,long token,ZPackage payload){((Action<ZRpc,ZDOID,long,ZPackage>)handlers["TooManyLags_AI_CommitResult"])(this,id,token,payload.Rewind());}
    public void ReceiveBoundaryTarget(ZDOID id,ZDOID target,ZPackage payload){((Action<ZRpc,ZDOID,ZDOID,ZPackage>)handlers["TooManyLags_AI_BoundaryTarget"])(this,id,target,payload.Rewind());}
    public void ReceiveBoundaryDecision(ZDOID id,ZDOID target,ZPackage payload){((Action<ZRpc,ZDOID,ZDOID,ZPackage>)handlers["TooManyLags_AI_BoundaryDecision"])(this,id,target,payload.Rewind());}
    public void ReceiveSoloControl(string command){((Action<ZRpc,string>)handlers["TooManyLags_AI_SoloControl"])(this,command);}
    public void ReceiveSoloArm(ZDOID id,ZPackage payload){((Action<ZRpc,ZDOID,ZPackage>)handlers["TooManyLags_AI_SoloArm"])(this,id,payload.Rewind());}
    public void ReceiveSoloReady(ZDOID id,ZPackage payload){((Action<ZRpc,ZDOID,ZPackage>)handlers["TooManyLags_AI_SoloReady"])(this,id,payload.Rewind());}
    public void ReceiveLatency(ZPackage payload){((Action<ZRpc,ZPackage>)handlers["TooManyLags_AI_SoloLatency"])(this,payload.Rewind());}
    public void ReceiveLatencyReady(ZPackage payload){((Action<ZRpc,ZPackage>)handlers["TooManyLags_AI_SoloLatencyReady"])(this,payload.Rewind());}
    public bool HasHandler(string name){return handlers.ContainsKey(name);}
    public void ReceiveStatus(ZDOID id,int ticket){((Action<ZRpc,ZDOID,int>)handlers["TooManyLags_AI_Status"])(this,id,ticket);}
    public void ReceiveStatusReply(int ticket,string line){((Action<ZRpc,int,string>)handlers["TooManyLags_AI_StatusReply"])(this,ticket,line);}
    public void Invoke(string name,params object[] data)
    {if(name=="FGN_HandOff"){Sent++;Mob=(ZDOID)data[0];Destination=(long)data[1];}
     else if(name=="TooManyLags_AI_Commit"){Sent++;Mob=(ZDOID)data[0];CommitToken=(long)data[1];CommitPayload=(ZPackage)data[2];CommitPayload.Rewind();CommitPayload.ReadUInt();Destination=CommitPayload.ReadLong();CommitPayload.Rewind();if(LegacySend!=null)LegacySend();}
     else if(name=="TooManyLags_AI_CommitResult"){CommitResults++;Mob=(ZDOID)data[0];ResultToken=(long)data[1];ResultPayload=(ZPackage)data[2];}
     else if(name=="TooManyLags_AI_Mode"){ModeSent++;LastMode=(int)data[0];}
     else if(name=="TooManyLags_AI_Watch"){WatchSent++;WatchMob=(ZDOID)data[0];WatchOwner=(long)data[1];WatchRevision=(long)data[2];}
     else if(name=="TooManyLags_AI_Target"){TargetSent++;TargetMob=(ZDOID)data[0];TargetId=(ZDOID)data[1];TargetRevision=(long)data[2];}
     else if(name=="TooManyLags_AI_BoundaryTarget"){BoundaryTargets++;BoundaryMob=(ZDOID)data[0];BoundaryId=(ZDOID)data[1];BoundaryPayload=(ZPackage)data[2];}
     else if(name=="TooManyLags_AI_BoundaryDecision"){BoundaryDecisions++;BoundaryMob=(ZDOID)data[0];BoundaryId=(ZDOID)data[1];DecisionPayload=(ZPackage)data[2];}
     else if(name=="TooManyLags_AI_SoloArm"){var p=((ZPackage)data[1]).Rewind();long t=p.ReadLong();p.Rewind();if(t<0){SoloModes++;SoloModeMob=(ZDOID)data[0];SoloModePayload=p;}else{SoloArms++;SoloMob=(ZDOID)data[0];SoloArmPayload=p;}}
     else if(name=="TooManyLags_AI_SoloReady"){SoloReadies++;SoloMob=(ZDOID)data[0];SoloReadyPayload=(ZPackage)data[1];}
     else if(name=="TooManyLags_AI_SoloMessage"){SoloMessages++;SoloMessage=(string)data[0];}
     else if(name=="TooManyLags_AI_SoloControl"){SoloControls++;SoloControl=(string)data[0];}
     else if(name=="TooManyLags_AI_SoloLatency"){LatencyRequests++;LatencyPayload=(ZPackage)data[0];}
     else if(name=="TooManyLags_AI_SoloLatencyReady"){LatencyReplies++;LatencyReply=(ZPackage)data[0];}
     else if(name=="TooManyLags_AI_AttackReady"){AttackReadySent++;AttackReadyMob=(ZDOID)data[0];AttackReadyRevision=(long)data[1];}
     else if(name=="TooManyLags_AI_Status"){StatusRequests++;StatusTicket=(int)data[1];}
     else if(name=="TooManyLags_AI_StatusReply"){StatusReplies++;StatusTicket=(int)data[0];StatusLine=(string)data[1];}
     else if(name=="FGN_HandOffResult"){HandoffResults++;HandoffAccepted=(bool)data[1];HandoffReason=(string)data[2];}}
}
public class Terminal : UnityEngine.Object
{
    public readonly List<string> Lines=new List<string>(); public void AddString(string line){Lines.Add(line);}
    public class ConsoleEventArgs { public Terminal Context; public string[] Args=new string[0]; }
    public delegate void ConsoleEvent(ConsoleEventArgs args);
    public class ConsoleCommand { public static readonly Dictionary<string,ConsoleEvent> Commands=new Dictionary<string,ConsoleEvent>(); public ConsoleCommand(string name,string help,ConsoleEvent callback){Commands[name]=callback;} }
}
public class ZNetPeer
{
    public class Socket { public string GetHostName(){return "fixture";} } public Socket m_socket=new Socket();
    public long m_uid; public ZDOID m_characterID; public bool Ready=true,Matched=true,m_server; public float Ping=50; public bool Measured=true;
    public UnityEngine.Vector3 Ref=new UnityEngine.Vector3(100,0,100); public ZRpc m_rpc=new ZRpc();
    public bool IsReady(){return Ready;} public UnityEngine.Vector3 GetRefPos(){return Ref;}
}
public class ZNet : UnityEngine.Object
{
    public bool Admin=true; public bool IsAdmin(string host){return Admin;}
    public static ZNet instance; public bool Dedicated=true; public readonly List<ZNetPeer> Peers=new List<ZNetPeer>();
    public ZNetPeer ServerPeer; public ZDOID LocalPlayerCharacterID;
    public bool IsDedicated(){return Dedicated;} public List<ZNetPeer> GetPeers(){return Peers;}
    public ZNetPeer GetPeer(long id){foreach(ZNetPeer p in Peers)if(p.m_uid==id)return p;return null;}
    public ZNetPeer GetServerPeer(){return ServerPeer;}
    [MethodImpl(MethodImplOptions.NoInlining)] private void OnNewConnection(ZNetPeer peer){}
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetCharacterID(ZDOID id){LocalPlayerCharacterID=id;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void Shutdown(){}
}
public class ZNetScene : UnityEngine.Object
{
    public static ZNetScene instance; public bool Ready=true; public int AreaCalls,PrefabCalls;
    public Func<UnityEngine.Vector3,bool> AreaReady;
    public readonly Dictionary<ZDO,ZNetView> Views=new Dictionary<ZDO,ZNetView>();
    public readonly Dictionary<int,UnityEngine.GameObject> Prefabs=new Dictionary<int,UnityEngine.GameObject>();
    public UnityEngine.GameObject GetPrefab(int hash){PrefabCalls++;UnityEngine.GameObject p;return Prefabs.TryGetValue(hash,out p)?p:null;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void AddInstance(ZDO zdo,ZNetView nview){Views[zdo]=nview;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void Shutdown(){}
    [MethodImpl(MethodImplOptions.NoInlining)] private void OnDestroy(){}
    public ZNetView FindInstance(ZDO d){ZNetView v;return Views.TryGetValue(d,out v)?v:null;}
    public bool IsAreaReady(UnityEngine.Vector3 p){AreaCalls++;return Ready&&(AreaReady==null||AreaReady(p));}
    public static bool InActiveArea(UnityEngine.Vector3 p,UnityEngine.Vector3 reference){return (p-reference).sqrMagnitude<200*200;}
}
public struct Vector2s { public short x,y; public Vector2s(int a,int b){x=(short)a;y=(short)b;} }
public static class ZoneSystem
{
    public static Vector2s GetZone(UnityEngine.Vector3 point){return new Vector2s((int)Math.Floor((point.x+32.0)/64.0),(int)Math.Floor((point.z+32.0)/64.0));}
}
public class ZDOMan
{
    [MethodImpl(MethodImplOptions.NoInlining)] private void RPC_ZDOData(){}
    public static ZDOMan instance; public static long Session=100; public int Forced;
    public readonly Dictionary<ZDOID,ZDO> Data=new Dictionary<ZDOID,ZDO>();
    public static long GetSessionID(){return Session;} public ZDO GetZDO(ZDOID id){ZDO d;return Data.TryGetValue(id,out d)?d:null;}
    public void ForceSendZDO(ZDOID id){Forced++;}
    [MethodImpl(MethodImplOptions.NoInlining)] private void ReleaseNearbyZDOS(UnityEngine.Vector3 position,long owner)
    { foreach(ZDO d in Data.Values){if(d.GetOwner()==owner)d.SetOwner(0L);else d.SetOwner(owner);} }
}
namespace TooManyLags
{
    // Network module is exercised separately with real production queue/hooks.
#if TML_DEBUG_SYSTEM
    internal static class SoloNetworkLatency
    {
        internal static int Starts; internal static bool Active,Fail;
        internal static bool Start(ZNetPeer peer,int ping,out int baseline,out int added,out string error)
        {Starts++;baseline=20;added=Math.Max(0,ping-baseline);error=Fail?"unsupported fixture transport":"";Active=!Fail;return !Fail;}
        internal static void End(){Active=false;}internal static string Describe(){return Active?"latency active":"inactive";}
    }
#endif
    internal static class Plugin { internal const string Id="botanic.toomanylags.fgnaddon",Version="fixture"; internal static bool Diagnostics; internal static NativePup Native=new NativePup(); internal static int Warnings; internal static void Warn(string s){Warnings++;} internal static void TestLog(string s){} }
    internal class NativePup
    {
        internal static bool Ready=true,DestinationReady=true,Available=true; internal static int Checks;
        internal static UnityEngine.Vector3 Destination;
        internal static Action OnReady;
        internal bool Present=true,Active=true;
        internal bool ReadyPoint(UnityEngine.Vector3 p,float radius){Checks++;if(OnReady!=null)OnReady();return Available&&(p.Equals(Destination)?DestinationReady:Ready);}
    }
}
namespace FiresGhettoNetworkMod
{
    internal static class ServerOwnershipPatchesV3
    {
        internal static bool IsExcluded(int prefab){return prefab==9876543;}
    }
    public static class FiresGhettoNetworkMod
    {
        public static BepInEx.Configuration.ConfigEntry<bool> ConfigEnableServerOwnership=new BepInEx.Configuration.ConfigEntry<bool>(false);
        public static BepInEx.Configuration.ConfigEntry<bool> ConfigEnableServerOwnershipSelective=new BepInEx.Configuration.ConfigEntry<bool>(false);
    }
    internal static class FgnVersionGate { internal static bool IsMatched(ZNetPeer peer){return peer.Matched;} }
    internal static class ServerAuthorityPatches { internal static bool SimulationActive=true; }
    internal static class ConnectionEcho { internal static bool TryRtt(ZNetPeer peer,out float ms){ms=peer.Ping;return peer.Measured;} }
    internal static class LinkController { internal static int PingMs(ZNetPeer peer){return peer.Measured?(int)peer.Ping:0;} }
    internal static class ValcastPossession
    {
        internal static bool IsPossessed(ZDO data){return data!=null&&data.GetLong("valcast_possessed".GetStableHashCode(),0L)!=0;}
    }
    internal static class TargetSync
    {
        internal static Character Next;
        [MethodImpl(MethodImplOptions.NoInlining)] private static bool AdoptOnHandover(BaseAI ai,ZDO data,long owner,out ZDOID adopted)
        {((MonsterAI)ai).m_targetCreature=Next;adopted=Next!=null?Next.GetZDOID():ZDOID.None;return true;}
        internal static void Adopt(BaseAI ai){ZDOID id;AdoptOnHandover(ai,null,0,out id);}
    }
    internal static class CreatureOwnership
    {
        private const double PassSeconds=2.0,PendingSeconds=5.0,DeclinedCooldownSeconds=6.0,ForwardHitsSeconds=5.0;
        private const int MaxHandOffsPerPass=16,MaxHandOffsPerOwner=6;
        private static string CombatReason(ZDO data,double now){return "native combat guard";}
        private struct Stamp { public long Peer; public double At; }
        private struct Owned { public long Owner; public double Since; }
        private struct StreakState { public long Candidate; public int Wins; }
        private static readonly Dictionary<ZDOID,Stamp> s_pending=new Dictionary<ZDOID,Stamp>();
        private static readonly Dictionary<ZDOID,Stamp> s_lastHitBy=new Dictionary<ZDOID,Stamp>();
        private static readonly Dictionary<ZDOID,double> s_localHits=new Dictionary<ZDOID,double>();
        private static readonly Dictionary<long,int> s_perOwner=new Dictionary<long,int>();
        private static readonly Dictionary<ZDOID,double> s_cooldowns=new Dictionary<ZDOID,double>();
        private static readonly Dictionary<ZDOID,StreakState> s_streaks=new Dictionary<ZDOID,StreakState>();
        private static readonly Dictionary<ZDOID,Owned> s_owned=new Dictionary<ZDOID,Owned>();
        private static readonly List<ZNetPeer> s_candidates=new List<ZNetPeer>();
        private static readonly HashSet<int> s_creaturePrefabs=new HashSet<int>{42};
        private static readonly HashSet<ZDOID> s_visited=new HashSet<ZDOID>();
        private static int s_creatureTypeMask=1; private static bool s_prefabsScanned=true;
        private static double s_nextPass;
        internal static bool Enabled=true;
        private static bool Active(){return Enabled&&ZNet.instance!=null&&ZNet.instance.IsDedicated();}
        private static float Rtt(ZNetPeer peer){return peer.Measured?peer.Ping:400f;}
        private static bool IsHeld(ZDO data){return data.Held;}
        private static int s_accepted,s_declined;
        private static readonly Dictionary<string,int> s_declineReasons=new Dictionary<string,int>();
        internal static void Clear(){s_pending.Clear();s_lastHitBy.Clear();s_localHits.Clear();s_perOwner.Clear();s_cooldowns.Clear();s_streaks.Clear();s_owned.Clear();s_nextPass=0;s_accepted=s_declined=0;s_declineReasons.Clear();}
        internal static void TrackOwned(ZDOID id,long owner,double since){s_owned[id]=new Owned{Owner=owner,Since=since};}
        internal static double OwnedSince(ZDOID id){Owned value;return s_owned.TryGetValue(id,out value)?value.Since:Double.NaN;}
        internal static void Candidates(IEnumerable<ZNetPeer> peers){s_candidates.Clear();s_candidates.AddRange(peers);}
        internal static void Decline(ZDOID id){s_pending.Remove(id);s_cooldowns[id]=UnityEngine.Time.realtimeSinceStartupAsDouble+6;}
        internal static void Streak(ZDOID id){s_streaks[id]=new StreakState{Candidate=3,Wins=1};}
        internal static void ResetTransfers(){s_pending.Clear();s_cooldowns.Clear();s_perOwner.Clear();s_streaks.Clear();}
        internal static bool HaveStreak(ZDOID id){return s_streaks.ContainsKey(id);}
        internal static void Hit(ZDOID id,long peer){s_lastHitBy[id]=new Stamp{Peer=peer,At=UnityEngine.Time.realtimeSinceStartupAsDouble};s_localHits[id]=UnityEngine.Time.realtimeSinceStartupAsDouble;}
        internal static int RawHits { get { return s_lastHitBy.Count; } }
        [MethodImpl(MethodImplOptions.NoInlining)] private static void Balance(ZDOMan world)
        { if(!Active()||UnityEngine.Time.realtimeSinceStartupAsDouble<s_nextPass)return; s_nextPass=UnityEngine.Time.realtimeSinceStartupAsDouble+2; }
        [MethodImpl(MethodImplOptions.NoInlining)] private static void ScanPrefabs()
        { Character actor=new Character{Boss=true}; if(actor.IsBoss())return; }
        [MethodImpl(MethodImplOptions.NoInlining)] private static bool TryHandOffCreature(ZDO data,double now)
        {
            // Original FGN1.5.36 selection flow, using managed peer/data doubles.
            Owned owned;long owner=data.GetOwner();if(owner==0)return false;
            if(!s_owned.TryGetValue(data.m_uid,out owned)||owned.Owner!=owner)
            {s_owned[data.m_uid]=owned=new Owned{Owner=owner,Since=now};s_streaks.Remove(data.m_uid);}
            ZNetPeer current=ZNet.instance.GetPeer(owner);if(current==null||!current.IsReady()||!FgnVersionGate.IsMatched(current))return false;
            Stamp pending;double cooldown;
            if(s_pending.TryGetValue(data.m_uid,out pending)&&now-pending.At<5)return false;
            if(s_cooldowns.TryGetValue(data.m_uid,out cooldown)&&now<cooldown)return false;
            if(data.GetBool(ZDOVars.s_tamed)||data.GetLong(ZDOVars.s_user,0)!=0||IsHeld(data))return false;
            UnityEngine.Vector3 position=data.GetPosition();if(!ZNetScene.InActiveArea(position,current.GetRefPos()))return false;
            Stamp hit; ZNetPeer target=null;
            if(s_lastHitBy.TryGetValue(data.m_uid,out hit)&&now-hit.At<=6)
            {if(hit.Peer==data.GetOwner())return false;target=ZNet.instance.GetPeer(hit.Peer);}
            ZNetPeer best=null,chosen=null;float lowest=float.MaxValue,ownerPing=Rtt(current);int nearby=0;bool fighter=false;
            foreach(ZNetPeer candidate in s_candidates)
            {if(!ZNetScene.InActiveArea(position,candidate.GetRefPos()))continue;nearby++;if(candidate==current)continue;if(candidate==target)fighter=true;
             if(FgnVersionGate.IsMatched(candidate)&&Rtt(candidate)<lowest){best=candidate;lowest=Rtt(candidate);}}
            if(nearby<2)return false;
            if(fighter&&Rtt(target)<=ownerPing+250)chosen=target;
            else if(target==null&&best!=null&&lowest+40<ownerPing&&now-owned.Since>=10)
            {StreakState streak;s_streaks.TryGetValue(data.m_uid,out streak);streak=streak.Candidate==best.m_uid?
             new StreakState{Candidate=best.m_uid,Wins=streak.Wins+1}:new StreakState{Candidate=best.m_uid,Wins=1};
             s_streaks[data.m_uid]=streak;if(streak.Wins>=2)chosen=best;}
            else s_streaks.Remove(data.m_uid);
            if(chosen==null)return false;int moves;s_perOwner.TryGetValue(owner,out moves);if(moves>=6)return false;
            s_perOwner[owner]=moves+1;current.m_rpc.Invoke("FGN_HandOff",data.m_uid,chosen.m_uid);
            s_pending[data.m_uid]=new Stamp{Peer=chosen.m_uid,At=now};s_streaks.Remove(data.m_uid);return true;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] private static string Veto(ZDO data,long newOwner)
        {
            double at;
            if(s_localHits.TryGetValue(data.m_uid,out at)&&UnityEngine.Time.realtimeSinceStartupAsDouble-at<=6)return "owner fighting";
            if(IsHeld(data))return "held";
            if(ValcastPossession.IsPossessed(data))return "possessed";
            Character mob=ZNetScene.instance.FindInstance(data).GetComponent<Character>();
            if(mob.IsBoss())return "boss";if(mob.InAttack())return "attacking";if(mob.IsStaggering())return "staggering";if(mob.IsTamed())return "tamed";
            return null;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] private static void OnResult(ZRpc rpc,ZDOID id,bool accepted,string reason)
        {
            if(accepted){s_accepted++;return;}
            s_declined++;s_pending.Remove(id);
            s_cooldowns[id]=UnityEngine.Time.realtimeSinceStartupAsDouble+6.0;
            string key=String.IsNullOrEmpty(reason)?"other":reason;int value;
            s_declineReasons.TryGetValue(key,out value);s_declineReasons[key]=value+1;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] private static void OnHandOff(ZRpc rpc,ZDOID id,long newOwner)
        {
            // Native FGN receive ordering: final Veto, ForceSend, SetOwner, result.
            ZDO data=ZDOMan.instance.GetZDO(id);string reason=Veto(data,newOwner);
            if(reason==null){ZDOMan.instance.ForceSendZDO(id);data.SetOwner(newOwner);}
            rpc.Invoke("FGN_HandOffResult",id,reason==null,reason??"");
        }
    }
}
