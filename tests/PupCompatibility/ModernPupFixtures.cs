using System;
#pragma warning disable 0169, 0414, 0649
using System.Collections.Generic;
using System.Runtime.CompilerServices;
public class ModernPup : UnityEngine.MonoBehaviour
{
    public static ModernPup Current; public bool Prepared=true; public int PreparedCalls; public bool Exact=true; public int ExactCalls,NativeCalls;
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
    public ModernPup() { Current=this;loadedZNet=ZNet.instance; }
    public bool ReplacementReady(UnityEngine.Vector3 p,float r) { return IsZoneReplacementReady(p,r); }
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
    [MethodImpl(MethodImplOptions.NoInlining)] private bool ShouldRetainActorCollisionCell(UnityEngine.Vector2Int cell) { return activeBodyCollisionCells.Contains(cell); }
    [MethodImpl(MethodImplOptions.NoInlining)] private bool HasPreparedDedicatedCollision(UnityEngine.Vector2Int cell,UnityEngine.Vector3 point,float radius) { PreparedCalls++;return Prepared; }
    public bool Retained(int x) { return ShouldRetainActorCollisionCell(new UnityEngine.Vector2Int(x,0)); }
    private bool PrepareExactCollision(UnityEngine.Vector3 p,float r) { ExactCalls++;return Exact; }
    private bool HasPendingCollidersForCell(UnityEngine.Vector2Int cell) { return false; }
    private bool IsZoneTransitionNear(UnityEngine.Vector3 p,float r) { return zoneTransitions.ContainsKey(new UnityEngine.Vector2Int((int)Math.Floor(p.x/64),0)) || restoreSupportCells.Count>0; }
    [MethodImpl(MethodImplOptions.NoInlining)] private bool IsZoneReplacementReady(UnityEngine.Vector3 p,float r)
    {
        NativeCalls++;
        if(!multiplayerManifestReceived && !ZNet.instance.Server)return false;
        if(ZNet.instance.Dedicated)return HasPreparedDedicatedCollision(new UnityEngine.Vector2Int((int)Math.Floor(p.x/64),0),p,r);
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
        private readonly ModernPup pup; private ZNetView view; private int coverage=-1;
        private bool held,wasKinematic; private UnityEngine.Vector3 position;
        public ZoneBodyGuard(ModernPup owner) { pup=owner; }
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
