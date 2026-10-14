using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
#pragma warning disable 0414, 0649

namespace UnityEngine
{
    public class Object
    {
        public bool Alive = true;
        public static implicit operator bool(Object value) { return value != null && value.Alive; }
        public static bool operator !(Object value) { return !(bool)value; }
        public static bool operator ==(Object left,Object right)
        {
            bool leftNull=System.Object.ReferenceEquals(left,null),rightNull=System.Object.ReferenceEquals(right,null);
            if(leftNull&&rightNull)return true;
            if(leftNull)return !right.Alive;
            if(rightNull)return !left.Alive;
            return System.Object.ReferenceEquals(left,right);
        }
        public static bool operator !=(Object left,Object right){return !(left==right);}
        public override bool Equals(object value)
        {return (value is Object||System.Object.ReferenceEquals(value,null))&&this==value as Object;}
        public override int GetHashCode(){return RuntimeHelpers.GetHashCode(this);}
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public static int TransformReads;
        public Transform transform { get { TransformReads++; return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T : Component { for (Transform node=transform; node!=null; node=node.parent) { T found=node.gameObject.GetComponent<T>(); if (found!=null) return found; } return null; }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponentsInChildren<T>(inactive); } public T GetComponentInChildren<T>(bool inactive) where T : Component { T[] values=GetComponentsInChildren<T>(inactive); return values.Length==0?null:values[0]; }
    }
    public class MonoBehaviour : Component { public bool enabled = true, isActiveAndEnabled = true; public readonly HashSet<string> Invoking=new HashSet<string>(); public bool IsInvoking(string name){return Invoking.Contains(name);} }
    public class Rigidbody : Component { public bool isKinematic=true,useGravity; }
    public class GameObject : Object
    {
        public static int Scans, Lookups;
        public string name;
        public bool activeInHierarchy = true;
        public readonly Transform transform = new Transform();
        public GameObject(){transform.gameObject=this;}
        public readonly List<Component> Components = new List<Component>();
        public readonly List<GameObject> Children = new List<GameObject>();
        public T Add<T>(T component) where T : Component { component.gameObject = this; Components.Add(component); return component; }
        public T GetComponent<T>() where T : Component { Lookups++; foreach (Component component in Components) if (component is T) return (T)component; return null; }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component
        {
            Scans++; List<T> result = new List<T>();
            foreach (Component component in Components) if (component is T) result.Add((T)component);
            foreach (GameObject child in Children) result.AddRange(child.GetComponentsInChildren<T>(inactive));
            return result.ToArray();
        }
    }
    public class Transform
    {
        public GameObject gameObject;
        private Vector3 pose;
        public static int PositionReads;
        public Vector3 position { get { PositionReads++; return pose; } set { pose = value; } }
        public Transform parent;
        public Vector3 forward = new Vector3(0,0,1);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object value) { return value is Vector3 && this == (Vector3)value; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
    }
    public struct Quaternion
    {
        private Vector3 angles;
        public Vector3 eulerAngles { get { return angles; } }
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion { angles = new Vector3(x, y, z) }; }
    }
    public static class Time { public static int frameCount; public static float time = 100, unscaledTime = 100; public static double realtimeSinceStartupAsDouble {get{return unscaledTime;}} }
}
public struct Vector2s { public short x,y; public Vector2s(short x,short y) {this.x=x;this.y=y;} }
public struct ZDOID
{
    public static ZDOID None {get{return default(ZDOID);}}
    public int ID;
    public bool IsNone() { return ID == 0; } public static bool operator ==(ZDOID a,ZDOID b){return a.ID==b.ID;} public static bool operator !=(ZDOID a,ZDOID b){return !(a==b);} public override bool Equals(object b){return b is ZDOID && this==(ZDOID)b;} public override int GetHashCode(){return ID;}
}
public sealed class ZDO
{
    private long owner;
    public int Writes;
    public ushort OwnerRevision {get;set;}
    public uint DataRevision {get;set;}
    public ZDOID m_uid;
    public int Hash;
    public bool Valid = true, PersistentValue = true, Parked, ThrowPersistent, Tamed;
    public long User;
    public string Follow="";
    public string GetString(int key){return key==ZDOVars.s_follow?Follow:"";}
    public UnityEngine.Vector3 Position;
    public float Yaw;
    public int RotationReads;
    public UnityEngine.Quaternion GetRotation() { RotationReads++; return UnityEngine.Quaternion.Euler(0, Yaw, 0); }
    public bool Persistent { [MethodImpl(MethodImplOptions.NoInlining)] get { if (ThrowPersistent) throw new InvalidOperationException("native persistent failure"); return PersistentValue; } }
    public bool IsValid() { return Valid; }
    public int GetPrefab() { return Hash; }
    public long GetOwner() { return owner; }
    public bool IsOwner() { return owner == ZDOMan.GetSessionID(); }
    private readonly Dictionary<int,int> ints=new Dictionary<int,int>(); public int IntWrites;
    public bool HasOwner() { return owner != 0; } public int Use;
    public int GetInt(int key,int fallback=0){int value;return key==ZDOVars.s_inUse?Use:ints.TryGetValue(key,out value)?value:fallback;}
    public void Set(int key,int value){int old;if(ints.TryGetValue(key,out old)&&old==value)return;ints[key]=value;IntWrites++;}
    public bool GetBool(int key) { return key == ZDOVars.s_tamed ? Tamed : Parked; }
    public long GetLong(int key,long fallback=0) {return key==ZDOVars.s_user?User:fallback;}
    public int PositionReads;
    public UnityEngine.Vector3 GetPosition() { PositionReads++; return Position; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetOwner(long value) { if (owner != value) { owner = value; Writes++; OwnerRevision++; } }
    public void SetOwnerInternal(long value) { owner=value; }
}
public sealed class Piece : UnityEngine.MonoBehaviour { }
public sealed class WearNTear : UnityEngine.MonoBehaviour
{
    public bool m_staticPosition = true;
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateSupport() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateWear(float time) { }
}
public sealed class ZNetView : UnityEngine.MonoBehaviour
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnDestroy() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void ResetZDO() { }
    public bool m_persistent = true, m_distant; public bool IsOwner(){return Data != null && Data.GetOwner()==ZDOMan.GetSessionID();}
    public Action<long,string,object[]> OnInvoke;
    [MethodImpl(MethodImplOptions.NoInlining)] public void InvokeRPC(long target,string name,params object[] args) { if(OnInvoke!=null)OnInvoke(target,name,args); }
    public ZDO Data;
    public bool IsValid() { return Data != null && Data.Valid; }
    public ZDO GetZDO() { return Data; }
}
public sealed class Container : UnityEngine.MonoBehaviour, Interactable
{
    public enum PrivacySetting { Private, Group, Public } public PrivacySetting m_privacy=PrivacySetting.Public; public ZNetView m_rootObjectOverride; private bool m_loading; public bool InUse, OpenGranted, StackGranted, TakeGranted;
    public void Loading(bool value){m_loading=value;} public bool IsInUse(){return InUse;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_OpenResponse(long sender,bool granted) { OpenGranted=granted; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_StackResponse(long sender,bool granted) { StackGranted=granted; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_TakeAllResponse(long sender,bool granted) { TakeGranted=granted; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetInUse(bool value){InUse=value;}
}
public sealed class Door : UnityEngine.MonoBehaviour, Interactable { }
public sealed class Beehive : UnityEngine.MonoBehaviour, Interactable { }
public sealed class Ship : UnityEngine.MonoBehaviour { }
public sealed class Vagon : UnityEngine.MonoBehaviour, Interactable { public bool Using; public bool InUse(){return Using;} }
public class Character : UnityEngine.MonoBehaviour
{
    public enum Faction { Players = 0, ForestMonsters = 2, Demon = 4, MountainMonsters = 5, PlainsMonsters = 7, PlayerSpawned = 11, DeepNorth = 13 }
    public Faction m_faction = Faction.ForestMonsters;
    public Faction GetFaction(){return m_faction;}
    public virtual bool IsPlayer(){return this is Player;}
    public bool Tamed=true, Dead, Attached, Riding;
    public ZDOID ActorId; public ZDOID GetZDOID(){return ActorId;}
    public virtual bool IsTamed(){return Tamed;}
    public virtual bool IsDead(){return Dead;}
    public virtual bool IsAttached(){return Attached;}
    public virtual bool IsRiding(){return Riding;}
}
public sealed class Player : Character { public static Player m_localPlayer; }
public sealed class GameCamera : UnityEngine.MonoBehaviour
{
    public static GameCamera instance; public static bool FreeFly;
    public static bool InFreeFly(){return FreeFly;}
    private void LateUpdate(){}
    private void OnDestroy(){}
}
public static class ZDOExtraData
{
    public static readonly HashSet<int> SessionHashes=new HashSet<int>();
    public static void AddSessionHash(int key){SessionHashes.Add(key);}
}
public sealed class Tameable : UnityEngine.MonoBehaviour
{
    public bool m_commandable, m_startsTamed;
    public float m_unsummonDistance, m_unsummonOnOwnerLogoutSeconds;
    public Sadle m_saddle;
    public bool HaveRider(){return m_saddle&&m_saddle.HaveValidUser();}
}
public sealed class Sadle : UnityEngine.MonoBehaviour
{
    public bool User;
    public bool HaveValidUser(){return User;}
}
public class BaseAI : UnityEngine.MonoBehaviour { }
public sealed class ZSyncTransform : UnityEngine.MonoBehaviour {public bool m_syncPosition=true;}
public static class ZDOVars {public const int s_tamed=10001,s_user=10002,s_follow=10003,s_state=10004,s_inUse=10005;}

public sealed class UnknownModScript : UnityEngine.MonoBehaviour { }
public class ZNetPeer
{
    public ZRpc m_rpc=new ZRpc();
    public long Uid;
    public long m_uid { get {return Uid;} }
    public ZDOID m_characterID; public bool Ready = true, m_server;
    public UnityEngine.Vector3 Position;
    public bool IsReady() { return Ready; }
    public UnityEngine.Vector3 GetRefPos() { return Position; }
}
public sealed class ZNet : UnityEngine.Object
{
    public static ZNet instance;
    public static bool IsSinglePlayer;
    public bool Dedicated = true;
    public ZNetPeer ServerPeer;
    public ZNetPeer GetServerPeer(){return ServerPeer;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnNewConnection(ZNetPeer peer) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_PeerInfo(ZRpc rpc) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Disconnect(ZNetPeer peer){Peers.Remove(peer);}
    [MethodImpl(MethodImplOptions.NoInlining)] public void Shutdown() { }
    public readonly List<ZNetPeer> Peers = new List<ZNetPeer>();
    public bool IsDedicated() { return Dedicated; } public bool IsServer(){return Dedicated;} public ZNetPeer GetPeer(long uid){foreach(var peer in Peers)if(peer.m_uid==uid)return peer;return null;}
    public List<ZNetPeer> GetPeers() { return Peers; }
}
public sealed class ZoneSystem : UnityEngine.Object
{
    public static ZoneSystem instance;
    public float m_zoneSize = 64f;
    public static Vector2s GetZone(UnityEngine.Vector3 point)
    { return new Vector2s((short)Math.Floor((point.x+32)/64),(short)Math.Floor((point.z+32)/64)); }
}
public sealed class ZNetScene : UnityEngine.Object
{
    public static ZNetScene instance;
    public static int ActiveQueries;
    public readonly Dictionary<int,UnityEngine.GameObject> Prefabs = new Dictionary<int,UnityEngine.GameObject>();
    private readonly Dictionary<ZDO,ZNetView> m_instances = new Dictionary<ZDO,ZNetView>(); public Dictionary<ZDO,ZNetView> Views {get{return m_instances;}}
    public Func<UnityEngine.Vector3,bool> Ready = delegate { return true; };
    public int AreaQueries;
    public UnityEngine.GameObject GetPrefab(int hash) { UnityEngine.GameObject result; return Prefabs.TryGetValue(hash,out result) ? result : null; }
    public ZNetView FindInstance(ZDO zdo) { ZNetView result; return Views.TryGetValue(zdo,out result) ? result : null; }
    public bool IsAreaReady(UnityEngine.Vector3 point) { AreaQueries++; return Ready(point); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool InActiveArea(UnityEngine.Vector3 point, Vector2s center)
    {
        ActiveQueries++;
        float x=point.x-center.x*64, z=point.z-center.y*64;
        return Math.Abs(x)<=96 && Math.Abs(z)<=96 && x*x+z*z<112*112;
    }
}
public sealed class ZDOMan
{
    public static ZDOMan instance;
    public readonly List<ZDO> Objects = new List<ZDO>();
    public static long SessionID=99;
    public static long GetSessionID() { return SessionID; }
    public ZDO GetZDO(ZDOID id){foreach(var data in Objects)if(data.m_uid==id)return data;return null;}
    private sealed class ZDOPeer
    {
        public struct PeerZDOInfo {public ushort m_ownerRevision;}
        public readonly Dictionary<ZDOID,PeerZDOInfo> m_zdos=new Dictionary<ZDOID,PeerZDOInfo>();
    }
    private readonly Dictionary<long,ZDOPeer> replication=new Dictionary<long,ZDOPeer>();
    private ZDOPeer GetPeer(long uid){ZDOPeer p;return replication.TryGetValue(uid,out p)?p:null;}
    public void MarkSent(long uid,ZDO data){ZDOPeer p=GetPeer(uid);if(p==null){p=new ZDOPeer();replication.Add(uid,p);}p.m_zdos[data.m_uid]=new ZDOPeer.PeerZDOInfo {m_ownerRevision=data.OwnerRevision};}
    public readonly List<KeyValuePair<long,ZDOID>> Sends=new List<KeyValuePair<long,ZDOID>>();
    public int Forced, NativeCycles; private float m_releaseZDOTimer;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ReleaseZDOS(float dt) { m_releaseZDOTimer += dt; if (m_releaseZDOTimer <= 2f) return; m_releaseZDOTimer = 0; NativeCycles++; }
    public void ForceSendZDO(long uid,ZDOID id){Forced++;Sends.Add(new KeyValuePair<long,ZDOID>(uid,id));}
    public void ForceSendZDO(ZDOID id){Forced++;Sends.Add(new KeyValuePair<long,ZDOID>(99,id));}
    private bool IsInPeerActiveArea(UnityEngine.Vector3 point,long uid)
    {
        if (uid==99) return ZNetScene.InActiveArea(point, new Vector2s(0,0));
        foreach(ZNetPeer peer in ZNet.instance.Peers)
            if(peer.Uid==uid) return ZNetScene.InActiveArea(point, ZoneSystem.GetZone(peer.Position));
        return false;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ReleaseNearbyZDOS(UnityEngine.Vector3 refPosition,long uid)
    {
        Vector2s zone=ZoneSystem.GetZone(refPosition);
        foreach(ZDO zdo in Objects)
        {
            if(!zdo.Persistent) continue;
            UnityEngine.Vector3 position=zdo.GetPosition();
            if(zdo.GetOwner()==uid)
            { if(!ZNetScene.InActiveArea(position,zone)) zdo.SetOwner(0); }
            else if((!zdo.HasOwner() || !IsInPeerActiveArea(position,zdo.GetOwner())) && ZNetScene.InActiveArea(position,zone)) zdo.SetOwner(uid);
        }
    }
}
namespace BepInEx.Configuration { public sealed class ConfigEntry<T> { public T Value; public ConfigEntry(T value) {Value=value;} } }
namespace BepInEx
{
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class BepInPlugin : Attribute
    {
        public System.Version Version;
        public string GUID;
        public BepInPlugin(string id,string name,string version) {GUID=id;Version=new System.Version(version);}
    }
}
namespace FiresGhettoNetworkMod
{
#if FGN_1541_FIXTURE
    [BepInEx.BepInPlugin("com.Fire.FiresGhettoNetworkMod","FGN","1.5.41")]
#elif FGN_FUTURE_FIXTURE
    [BepInEx.BepInPlugin("com.Fire.FiresGhettoNetworkMod","FGN","1.5.42")]
#else
    [BepInEx.BepInPlugin("com.Fire.FiresGhettoNetworkMod","FGN","1.5.36")]
#endif
    public sealed class FiresGhettoNetworkMod : BepInEx.BaseUnityPlugin
    {
        public static BepInEx.Configuration.ConfigEntry<bool> ConfigEnableServerAuthority = new BepInEx.Configuration.ConfigEntry<bool>(true);
        public static BepInEx.Configuration.ConfigEntry<bool> ConfigEnableServerOwnership = new BepInEx.Configuration.ConfigEntry<bool>(false);
        public static BepInEx.Configuration.ConfigEntry<bool> ConfigEnableServerOwnershipSelective = new BepInEx.Configuration.ConfigEntry<bool>(false);
    }
    public static class ServerAuthorityPatches { internal static bool SimulationActive = true; }
}

