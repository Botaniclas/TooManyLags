using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#pragma warning disable 0649
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public class Component : Object
    {
        internal Dictionary<Type, Component> Components = new Dictionary<Type, Component>();
        public T GetComponent<T>() where T : Component { Component c; return Components.TryGetValue(typeof(T), out c) ? (T)c : null; }
    }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
    }
    public static class Time { public static float time; public static double realtimeSinceStartupAsDouble; }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public long UserID; public uint ID; public bool IsNone(){return UserID==0&&ID==0;}
    public static readonly ZDOID None = new ZDOID();
    public bool Equals(ZDOID value){return UserID==value.UserID&&ID==value.ID;}
    public override bool Equals(object value){return value is ZDOID&&Equals((ZDOID)value);}
    public override int GetHashCode(){return UserID.GetHashCode()^(int)ID;}
    public static bool operator ==(ZDOID a,ZDOID b){return a.Equals(b);}public static bool operator !=(ZDOID a,ZDOID b){return !a.Equals(b);}
}
public class ZDO
{
    public ZDOID m_uid;public uint OwnerRevision=4;public long Owner=1;public int Prefab=42;public bool Valid=true;
    public bool IsValid(){return Valid;}public bool IsOwner(){return Owner==ZDOMan.Session;}public long GetOwner(){return Owner;}public int GetPrefab(){return Prefab;}
    public void SetOwner(long owner){Owner=owner;OwnerRevision++;}
}
public class ZDOMan
{
    public static ZDOMan instance;public static long Session=1;public static long GetSessionID(){return Session;}
    public Dictionary<ZDOID,ZDO> Values=new Dictionary<ZDOID,ZDO>();public ZDO GetZDO(ZDOID id){ZDO data;return Values.TryGetValue(id,out data)?data:null;}
    [MethodImpl(MethodImplOptions.NoInlining)]public void RPC_ZDOData(Action receive){receive();}
}
public class ZNetScene : UnityEngine.Object
{
    public static ZNetScene instance;public Dictionary<ZDO,ZNetView> Values=new Dictionary<ZDO,ZNetView>();
    public ZNetView FindInstance(ZDO data){ZNetView view;return Values.TryGetValue(data,out view)?view:null;}
}
public class ZNet : UnityEngine.Object
{
    public static ZNet instance;public bool Server=true;public List<ZNetPeer> Peers=new List<ZNetPeer>();
    public bool IsServer(){return Server;}public List<ZNetPeer> GetPeers(){return Peers;}public ZNetPeer GetPeer(long id){return Peers.Find(p=>p.m_uid==id);}
    public ZNetPeer GetServerPeer(){return Peers.Find(p=>p.m_server);}
    [MethodImpl(MethodImplOptions.NoInlining)]public void OnNewConnection(ZNetPeer peer){if(!Peers.Contains(peer))Peers.Add(peer);}
    [MethodImpl(MethodImplOptions.NoInlining)]public void RPC_PeerInfo(ZRpc rpc){GC.KeepAlive(rpc);}
    [MethodImpl(MethodImplOptions.NoInlining)]public void Shutdown(){Peers.Clear();}
}
public class ZNetPeer {public long m_uid;public bool m_server,Ready=true;public ZRpc m_rpc=new ZRpc();public bool IsReady(){return Ready;}}
public class ZRpc
{
    public Action<string,object[]> Send;public readonly Dictionary<string,Delegate> Handlers=new Dictionary<string,Delegate>();
    public void Invoke(string name,params object[] args){if(Send!=null)Send(name,args);}
    public void Register<T>(string name,Action<ZRpc,T> handler){Handlers[name]=handler;}
    public void Register<T,U>(string name,Action<ZRpc,T,U> handler){Handlers[name]=handler;}
    public void Receive(string name,params object[] args){object[] full=new object[args.Length+1];full[0]=this;Array.Copy(args,0,full,1,args.Length);Handlers[name].DynamicInvoke(full);}
}
public class ZNetView : UnityEngine.Component
{
    public ZDO Data;public bool IsValid(){return Data!=null&&Data.IsValid();}public ZDO GetZDO(){return Data;}
    [MethodImpl(MethodImplOptions.NoInlining)]public void HandleRoutedRPC(ZRoutedRpc.RoutedRPCData rpc){rpc.Callback();}
}
public class ZRoutedRpc
{public class RoutedRPCData{public long m_targetPeerID;public ZDOID m_targetZDO;public int m_methodHash;public Action Callback;}}
public static class StringExtensionMethods
{public static int GetStableHashCode(this string value){unchecked{int hash=17;foreach(char c in value)hash=hash*31+c;return hash;}}}
public class BaseAI : UnityEngine.Component { public static bool IsEnemy(Character a, Character b) { return a != b; } }
public class StaticTarget : UnityEngine.Component { }
public class MonsterAI : BaseAI
{
    private Character m_targetCreature; private float m_updateTargetTimer;
    private StaticTarget m_targetStatic;
    public StaticTarget Static { get { return m_targetStatic; } set { m_targetStatic = value; } }
    public Character Target { get { return m_targetCreature; } set { m_targetCreature = value; } }
    public float TargetDelay { get { return m_updateTargetTimer; } set { m_updateTargetTimer = value; } }
    public Character GetTargetCreature() { return m_targetCreature; }
    [MethodImpl(MethodImplOptions.NoInlining)] public bool UpdateAI(float dt) { m_updateTargetTimer -= dt; return true; }
}
public class Character : UnityEngine.Component
{
    protected ZNetView m_nview;
    public ZNetView View {get{return m_nview;}set{m_nview=value;}}
    private UnityEngine.Vector3 m_pushForce;
    private UnityEngine.Vector3 m_currentVel;
    private float m_currentTurnVel, m_currentTurnVelChange, currentRotSpeedFactor;
    private float m_staggerDamage,m_backstabTime=-9999;
    protected HitData m_lastHit;
    public UnityEngine.Vector3 Push {get{return m_pushForce;}set{m_pushForce=value;}}
    public UnityEngine.Vector3 Velocity {get{return m_currentVel;}set{m_currentVel=value;}}
    public float Turn {get{return m_currentTurnVel;}set{m_currentTurnVel=value;}}
    public float TurnChange {get{return m_currentTurnVelChange;}set{m_currentTurnVelChange=value;}}
    public float RotationFactor {get{return currentRotSpeedFactor;}set{currentRotSpeedFactor=value;}}
    public float Stagger {get{return m_staggerDamage;}set{m_staggerDamage=value;}}
    public float Backstab {get{return m_backstabTime;}set{m_backstabTime=value;}}
    public HitData LastHit {get{return m_lastHit;}set{m_lastHit=value;}}
    public float SeenStagger,SeenTurn,SeenTurnChange;public UnityEngine.Vector3 SeenVelocity;public int Hits,FixedPasses,Staggers,Heals;public float Health=100;public SEMan Effects;
    public SEMan GetSEMan(){return Effects;}
    public bool IsDead() { return Health <= 0f; }
    public ZDOID GetZDOID() { return m_nview.GetZDO().m_uid; }
    [MethodImpl(MethodImplOptions.NoInlining)]public void CustomFixedUpdate(float dt){FixedPasses++;SeenStagger=m_staggerDamage;SeenVelocity=m_currentVel;SeenTurn=m_currentTurnVel;SeenTurnChange=m_currentTurnVelChange;GC.KeepAlive(dt);}
    [MethodImpl(MethodImplOptions.NoInlining)]private void RPC_Damage(long sender,HitData hit)
    {if(GetComponent<ZNetView>().Data.IsOwner()){Hits++;Health-=hit.Damage;m_pushForce=hit.Push;m_staggerDamage+=hit.Damage;m_lastHit=hit;GC.KeepAlive(sender);}}
    public void Damage(long sender,HitData hit){RPC_Damage(sender,hit);}
    [MethodImpl(MethodImplOptions.NoInlining)]private void RPC_Stagger(long sender,UnityEngine.Vector3 forceDirection){Staggers++;GC.KeepAlive(sender);GC.KeepAlive(forceDirection);}
    [MethodImpl(MethodImplOptions.NoInlining)]private void RPC_Heal(long sender,float hp,bool showText){if(m_nview.Data.IsOwner()){Heals++;Health+=hp;GC.KeepAlive(sender);GC.KeepAlive(showText);}}
    public void StaggerRpc(long sender,UnityEngine.Vector3 direction){RPC_Stagger(sender,direction);}public void HealRpc(long sender,float hp,bool show){RPC_Heal(sender,hp,show);}
    public void AddressedStagger(long sender,UnityEngine.Vector3 direction,bool broadcast)
    {m_nview.HandleRoutedRPC(new ZRoutedRpc.RoutedRPCData{m_targetPeerID=broadcast?0:ZDOMan.Session,m_targetZDO=m_nview.Data.m_uid,m_methodHash="RPC_Stagger".GetStableHashCode(),Callback=delegate{RPC_Stagger(sender,direction);}});}
}
public class Player : Character { }
public class StatusEffect { public float Age,Remaining,Next; }
public class SEMan
{
    private Character m_character;private List<StatusEffect> active=new List<StatusEffect>();public int Adds;
    public SEMan(Character actor){m_character=actor;}
    public List<StatusEffect> GetStatusEffects(){return active;}
    [MethodImpl(MethodImplOptions.NoInlining)]private void RPC_AddStatusEffect(long sender,int nameHash,bool resetTime,int itemLevel,float skillLevel,int variant)
    {if(m_character.GetComponent<ZNetView>().Data.IsOwner()){Adds++;active.Add(new StatusEffect());GC.KeepAlive(sender);GC.KeepAlive(nameHash);GC.KeepAlive(resetTime);GC.KeepAlive(itemLevel);GC.KeepAlive(skillLevel);GC.KeepAlive(variant);}}
    public void Add(long sender,int name,bool reset,int level,float skill,int variant){RPC_AddStatusEffect(sender,name,reset,level,skill,variant);}
}
public class HitData
{
    public float Damage;public long Attacker;public UnityEngine.Vector3 Push;public int Status;
    public void Serialize(ref ZPackage p){p.Write(Damage);p.Write(Attacker);p.Write(Push);p.Write(Status);}
    public void Deserialize(ref ZPackage p){Damage=p.ReadSingle();Attacker=p.ReadLong();Push=p.ReadVector3();Status=p.ReadInt();}
}
public class ZPackage
{
    private System.IO.MemoryStream stream;private System.IO.BinaryReader read;private System.IO.BinaryWriter write;
    public ZPackage():this(new byte[0]){}public ZPackage(byte[] bytes){stream=new System.IO.MemoryStream();stream.Write(bytes,0,bytes.Length);stream.Position=0;read=new System.IO.BinaryReader(stream);write=new System.IO.BinaryWriter(stream);}
    public byte[] Bytes(){return stream.ToArray();}public int Size(){return (int)stream.Length;}public int GetPos(){return (int)stream.Position;}
    public void Write(byte v){write.Write(v);}public void Write(bool v){write.Write(v);}public void Write(long v){write.Write(v);}public void Write(uint v){write.Write(v);}public void Write(int v){write.Write(v);}public void Write(float v){write.Write(v);}
    public void Write(UnityEngine.Vector3 v){Write(v.x);Write(v.y);Write(v.z);}
    public void Write(ZDOID id){Write(id.UserID);Write(id.ID);}
    public ZDOID ReadZDOID(){return new ZDOID{UserID=ReadLong(),ID=ReadUInt()};}
    public void Write(byte[] v){Write(v.Length);write.Write(v);}
    public byte ReadByte(){return read.ReadByte();}public bool ReadBool(){return read.ReadBoolean();}public long ReadLong(){return read.ReadInt64();}public uint ReadUInt(){return read.ReadUInt32();}public int ReadInt(){return read.ReadInt32();}public float ReadSingle(){return read.ReadSingle();}
    public UnityEngine.Vector3 ReadVector3(){return new UnityEngine.Vector3(ReadSingle(),ReadSingle(),ReadSingle());}
    public byte[] ReadByteArray(){return read.ReadBytes(ReadInt());}
}
namespace TooManyLags
{
    internal static class Plugin {internal const string Id="combat-state-fixture";internal static int Warnings;internal static void Warn(string text){Warnings++;Console.WriteLine("WARN: "+text);}}
    // Transport integration seam; actual effect codec/lifecycle has a separate
    // production-source/native-contract suite.
    internal static class MobStatusState
    {
        internal static int Captures,Restores,Relinquishes;internal static long Destination;
        internal static void Initialize(){}internal static void Reset(){}internal static void Stop(){}
        internal static void RefreshReferences(){}
        internal static byte[] Capture(Character actor)
        {
            Captures++;var active=actor.GetSEMan().GetStatusEffects();if(active.Count==0)return null;
            ZPackage p=new ZPackage();p.Write(active.Count);foreach(var e in active){p.Write(e.Age);p.Write(e.Remaining);p.Write(e.Next);}return p.Bytes();
        }
        internal static void Restore(Character actor,byte[] state)
        {
            Restores++;var active=actor.GetSEMan().GetStatusEffects();active.Clear();if(state==null)return;
            ZPackage p=new ZPackage(state);int count=p.ReadInt();while(count-->0)active.Add(new StatusEffect{Age=p.ReadSingle(),Remaining=p.ReadSingle(),Next=p.ReadSingle()});
        }
        internal static void Relinquish(Character actor,byte[] state,long destination)
        {Relinquishes++;Destination=destination;actor.GetSEMan().GetStatusEffects().Clear();GC.KeepAlive(state);}
    }
}
namespace FiresGhettoNetworkMod
{
    internal static class CreatureOwnership
    {
        internal static int DamagePrefixes;
        internal static string VetoReason;
        [MethodImpl(MethodImplOptions.NoInlining)]internal static bool OnDamage(Character __instance,HitData hit)
        {DamagePrefixes++;GC.KeepAlive(__instance);GC.KeepAlive(hit);return true;}
        [MethodImpl(MethodImplOptions.NoInlining)]internal static string Veto(ZDO data,long destination){GC.KeepAlive(data);GC.KeepAlive(destination);return VetoReason;}
        [MethodImpl(MethodImplOptions.NoInlining)]internal static bool Commit(ZDO data,long destination,Action<ZDO,long> beforeTransfer)
        {if(Veto(data,destination)!=null)return false;beforeTransfer(data,destination);data.SetOwner(destination);TooManyLags.MobCombatState.CompleteTransfer(data,destination);return true;}
    }
}
