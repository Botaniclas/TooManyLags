#pragma warning disable 0169, 0414, 0649
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace UnityEngine
{
    public class Object { public bool Alive = true; public static implicit operator bool(Object o) { return o != null && o.Alive; } public static bool operator !(Object o) { return !(bool)o; } }
    public class Component : Object
    {
        public GameObject gameObject; public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T : Component { return GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component { return gameObject.GetComponentsInChildren<T>(inactive); }
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { public bool isActiveAndEnabled = true; }
    public class GameObject : Object
    {
        public bool activeInHierarchy = true; public readonly Transform transform = new Transform();
        private readonly List<Component> components = new List<Component>();
        public T Add<T>(T value) where T : Component { value.gameObject = this; components.Add(value); return value; }
        public T GetComponent<T>() where T : Component { foreach (Component c in components) if (c is T) return (T)c; return null; }
        public Component GetComponent(Type type) { foreach (Component c in components) if (type.IsInstanceOfType(c)) return c; return null; }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component { var list = new List<T>(); foreach (Component c in components) if (c is T) list.Add((T)c); return list.ToArray(); }
    }
    public class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z; public Vector3(float a, float b, float c) { x=a;y=b;z=c; }
        public static Vector3 zero { get { return new Vector3(); } } public static Vector3 one { get { return new Vector3(1,1,1); } }
        public float magnitude { get { return (float)Math.Sqrt(x*x+y*y+z*z); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public static float Distance(Vector3 a, Vector3 b) { return (a-b).magnitude; }
    }
    public struct Vector2Int { public int x,y; public Vector2Int(int a,int b) { x=a;y=b; } }
    public struct Quaternion { public static Vector3 operator *(Quaternion q,Vector3 v) { return v; } }
    public struct Bounds
    {
        public Vector3 center,size; public Bounds(Vector3 c,Vector3 s) { center=c;size=s; }
        public Vector3 extents { get { return size*0.5f; } } public Vector3 min { get { return center-extents; } }
        public void Encapsulate(Bounds b) { size=b.size;center=b.center; }
    }
    public class Collider : Component { public bool enabled=true,isTrigger; public Bounds bounds; }
    public class Rigidbody : Component { public bool isKinematic { get; set; } }
    public static class Mathf
    {
        public static int FloorToInt(float f) { return (int)Math.Floor(f); } public static int CeilToInt(float f) { return (int)Math.Ceiling(f); }
        public static int Max(int a,int b) { return Math.Max(a,b); } public static float Max(float a,float b) { return Math.Max(a,b); }
        public static float Min(float a,float b) { return Math.Min(a,b); } public static float Abs(float v) { return Math.Abs(v); }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
    }
}
public class ZNet : UnityEngine.Object
{
    public static ZNet instance; public static bool IsSinglePlayer; public bool Dedicated,Server;
    public bool IsServer() { return Server; } public bool IsDedicated() { return Dedicated; }
}
public class ZDO
{
    public long Owner=1; public uint OwnerRevision; public UnityEngine.Vector3 Position;
    public long GetOwner() { return Owner; } public UnityEngine.Vector3 GetPosition() { return Position; }
}
public class ZNetView : UnityEngine.MonoBehaviour
{
    public ZDO Data=new ZDO(); public bool Valid=true;
    public bool IsValid() { return Valid; } public bool IsOwner() { return Data.Owner==1; } public ZDO GetZDO() { return Data; }
}
public class ZSyncTransform : UnityEngine.MonoBehaviour
{
    private bool m_wasOwner; public bool m_syncPosition=true;
    private ZNetView m_nview; private UnityEngine.Rigidbody m_body;
    public void Initialize(ZNetView view,UnityEngine.Rigidbody body) { m_nview=view;m_body=body; }
    public void SetPrevious(bool value) { m_wasOwner=value; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void CustomFixedUpdate(float dt) { m_wasOwner=GetComponent<ZNetView>().IsOwner(); }
}
public class Character : UnityEngine.MonoBehaviour
{
    private ZNetView m_nview; public void Initialize(ZNetView view) { m_nview=view; }
}
public class Player : Character { }
public class Projectile : UnityEngine.MonoBehaviour { }
public class WearNTear : UnityEngine.MonoBehaviour
{
    private ZNetView m_nview; private List<Bound> m_bounds; private UnityEngine.Collider[] m_colliders;
    public struct Bound { public UnityEngine.Vector3 m_pos,m_size; public UnityEngine.Quaternion m_rot; }
    public bool m_noSupportWear=true; public float Support=5,Expected; public int Damage,Weather,Updates,Clears;
    public void Initialize(ZNetView view) { m_nview=view; }
    private void SetupColliders() { m_colliders=new UnityEngine.Collider[0];m_bounds=new List<Bound>(); }
    private void ClearCachedSupport() { Clears++; }
    private bool HaveSupport() { return Support>0; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateSupport() { Updates++;float previous=Support;Support=Expected;FakePup.Current.Preserve(this,previous); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateWear(float time) { Damage+=Weather;if(m_noSupportWear) { UpdateSupport();if(!HaveSupport()) Damage+=100; } }
}
public class FakePup : UnityEngine.MonoBehaviour
{
    public static FakePup Current; public bool Exact=true; public int ExactCalls,NativeCalls;
    private bool multiplayerManifestReceived=true,multiplayerRejected;
    private ZNet loadedZNet;
    private int collisionCoverageRevision,zoneGuardRevision;
    private readonly Dictionary<UnityEngine.Vector2Int,List<int>> records=new Dictionary<UnityEngine.Vector2Int,List<int>>();
    private readonly Dictionary<UnityEngine.Vector2Int,Remote> multiplayerRemoteCells=new Dictionary<UnityEngine.Vector2Int,Remote>();
    private readonly Dictionary<UnityEngine.Vector2Int,object> restoreSupportCells=new Dictionary<UnityEngine.Vector2Int,object>();
    private readonly Dictionary<UnityEngine.Vector2Int,int> zoneTransitions=new Dictionary<UnityEngine.Vector2Int,int>();
    private readonly HashSet<UnityEngine.Vector2Int> activeBodyCollisionCells=new HashSet<UnityEngine.Vector2Int>();
    private readonly Dictionary<UnityEngine.Vector2Int,Fast> fastColliderCells=new Dictionary<UnityEngine.Vector2Int,Fast>();
    private readonly Dictionary<UnityEngine.Vector2Int,UnityEngine.GameObject> clusters=new Dictionary<UnityEngine.Vector2Int,UnityEngine.GameObject>();
    private readonly Dictionary<UnityEngine.Vector2Int,string> cacheIds=new Dictionary<UnityEngine.Vector2Int,string>();
    private readonly Dictionary<UnityEngine.GameObject,string> validatedReplacementCacheIds=new Dictionary<UnityEngine.GameObject,string>();
    private readonly Dictionary<UnityEngine.Vector2Int,bool> colliderCellActive=new Dictionary<UnityEngine.Vector2Int,bool>();
    private readonly Dictionary<UnityEngine.Vector2Int,HashSet<string>> fastLoadedSubclusters=new Dictionary<UnityEngine.Vector2Int,HashSet<string>>();
    private readonly HashSet<UnityEngine.Vector2Int> multiplayerRemotePhysicsReady=new HashSet<UnityEngine.Vector2Int>();
    private readonly HashSet<UnityEngine.Vector2Int> verifiedRemoteReplacements=new HashSet<UnityEngine.Vector2Int>();
    private readonly HashSet<UnityEngine.Vector2Int> pendingWorldSaveRestores=new HashSet<UnityEngine.Vector2Int>();
    private readonly HashSet<UnityEngine.Vector2Int> multiplayerPendingVanillaRestore=new HashSet<UnityEngine.Vector2Int>();
    private readonly HashSet<UnityEngine.Vector2Int> loadingCells=new HashSet<UnityEngine.Vector2Int>();
    private int queuedColliderCount;
    private sealed class Remote { public string cacheId="a"; }
    private sealed class Fast { public string cacheId="a"; public UnityEngine.GameObject root; public HashSet<string> required=new HashSet<string>(); }
    public FakePup() { Current=this;loadedZNet=ZNet.instance; }
    public void Manifest(bool value) { multiplayerManifestReceived=value;zoneGuardRevision++; }
    public void Cell(int x,bool complete)
    {
        var key=new UnityEngine.Vector2Int(x,0);records[key]=new List<int>{1};cacheIds[key]="a";multiplayerRemoteCells[key]=new Remote();
        var root=new UnityEngine.GameObject();clusters[key]=root;validatedReplacementCacheIds[root]="a";
        colliderCellActive[key]=complete;verifiedRemoteReplacements.Add(key);
        if(complete)multiplayerRemotePhysicsReady.Add(key);else multiplayerRemotePhysicsReady.Remove(key);
        collisionCoverageRevision++;
    }
    public void Active(int x,bool value) { clusters[new UnityEngine.Vector2Int(x,0)].activeInHierarchy=value; }
    public void DedicatedCell(int x) { var key=new UnityEngine.Vector2Int(x,0);records[key]=new List<int>{1};cacheIds[key]="a";collisionCoverageRevision++; }
    public void RecipeId(int x,string value) { var key=new UnityEngine.Vector2Int(x,0);if(value==null)cacheIds.Remove(key);else cacheIds[key]=value; }
    public void Restore(int x,bool value) { var key=new UnityEngine.Vector2Int(x,0);if(value)pendingWorldSaveRestores.Add(key);else pendingWorldSaveRestores.Remove(key); }
    public void Transition(int x,bool value) { var key=new UnityEngine.Vector2Int(x,0);if(value)zoneTransitions[key]=1;else zoneTransitions.Remove(key);zoneGuardRevision++; }
    public void BodyCell(int x,bool value) { var key=new UnityEngine.Vector2Int(x,0);if(value)activeBodyCollisionCells.Add(key);else activeBodyCollisionCells.Remove(key); }
    [MethodImpl(MethodImplOptions.NoInlining)] private bool ShouldRetainActorCollisionCell(UnityEngine.Vector2Int cell) { return false; }
    public bool Retained(int x) { return ShouldRetainActorCollisionCell(new UnityEngine.Vector2Int(x,0)); }
    private bool PrepareExactCollision(UnityEngine.Vector3 p,float r) { ExactCalls++;return Exact; }
    private bool HasPendingCollidersForCell(UnityEngine.Vector2Int cell) { return false; }
    private bool IsZoneTransitionNear(UnityEngine.Vector3 p,float r) { return zoneTransitions.ContainsKey(new UnityEngine.Vector2Int((int)Math.Floor(p.x/64),0)) || restoreSupportCells.Count>0; }
    [MethodImpl(MethodImplOptions.NoInlining)] private bool IsZoneReplacementReady(UnityEngine.Vector3 p,float r)
    {
        NativeCalls++;
        if(!multiplayerManifestReceived && !ZNet.instance.Server)return false;
        if(ZNet.instance.Dedicated)return true; // Exact native dedicated skip reproduced.
        var key=new UnityEngine.Vector2Int((int)Math.Floor(p.x/64),0);
        return !multiplayerRemoteCells.ContainsKey(key)||multiplayerRemotePhysicsReady.Contains(key);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private bool PrepareMobCollision(Character c) { return AttachZoneBodyGuard(c.gameObject).Step(); }
    public bool MoveMob(Character c) { return PrepareMobCollision(c); }
    [MethodImpl(MethodImplOptions.NoInlining)] private void PreserveDependentSupport(WearNTear wear,float previous) { if(wear.Support<=0)wear.Support=previous; }
    public void Preserve(WearNTear wear,float previous) { PreserveDependentSupport(wear,previous); }
    private void RepairDependentSupport(WearNTear wear) { }
    private ZoneBodyGuard AttachZoneBodyGuard(UnityEngine.GameObject root)
    {
        var current=root.GetComponent<ZoneBodyGuard>();if(current)return current;
        if(root.GetComponent<Player>()||!root.GetComponent<UnityEngine.Rigidbody>())return null;
        return root.Add(new ZoneBodyGuard(this));
    }
    public class ZoneBodyGuard : UnityEngine.MonoBehaviour
    {
        private readonly FakePup pup; private ZNetView view; private int coverage=-1;
        private bool held,wasKinematic; private UnityEngine.Vector3 position;
        public ZoneBodyGuard(FakePup owner) { pup=owner; }
        internal bool Step()
        {
            if(!view)view=GetComponent<ZNetView>();var body=GetComponent<UnityEngine.Rigidbody>();
            if(!view.IsOwner()) { if(held)body.isKinematic=wasKinematic;held=false;return true; }
            if(coverage==pup.collisionCoverageRevision && position.x==transform.position.x)return !held;
            bool ready=!pup.IsZoneTransitionNear(transform.position,8)&&pup.IsZoneReplacementReady(transform.position,8);
            if(ready) { if(held)body.isKinematic=wasKinematic;held=false; }
            else if(!held) { wasKinematic=body.isKinematic;body.isKinematic=true;held=true; }
            coverage=pup.collisionCoverageRevision;position=transform.position;return ready;
        }
    }
}
namespace FiresGhettoNetworkMod
{
    public static class OwnershipHandoffPatches
    {
        public static bool SawPreFreeze;
        public static bool ZSyncTransform_CustomFixedUpdate_Prefix(ZSyncTransform __instance,ZNetView ___m_nview,UnityEngine.Rigidbody ___m_body)
        {
            SawPreFreeze=___m_body&&___m_body.isKinematic;
            if(___m_nview.IsOwner())__instance.transform.position=___m_nview.GetZDO().GetPosition();
            return true;
        }
    }
    internal static class OwnedPositionEcho
    {
        private static readonly List<KeyValuePair<ZDO,ZNetView>> s_moved=new List<KeyValuePair<ZDO,ZNetView>>();
        [MethodImpl(MethodImplOptions.NoInlining)] private static void Receive_Finalizer()
        { foreach(var entry in s_moved)entry.Key.Position=entry.Value.transform.position;s_moved.Clear(); }
        public static void Receive(ZNetView view) { s_moved.Add(new KeyValuePair<ZDO,ZNetView>(view.GetZDO(),view));Receive_Finalizer(); }
    }
    public static class EchoFixture { public static void Receive(ZNetView view) { OwnedPositionEcho.Receive(view); } }
}

public sealed class ServerZoneStreamBlock : UnityEngine.Behaviour
{
    private bool initialized;
    public int NativeCalls;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void Initialize() { initialized = true; NativeCalls++; }
    public void Run() { Initialize(); }
    public bool Initialized { get { return initialized; } }
}