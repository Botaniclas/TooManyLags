// Focused managed proof for the production optional adapter. No actor guards,
// Unity methods, world simulation, ownership writes or native PUP hooks.
#pragma warning disable 0169, 0414, 0649
using System;
using System.Collections.Generic;
using TooManyLags;
using UnityEngine;

namespace UnityEngine
{
    public class Object { public bool Alive=true; public static implicit operator bool(Object o){return o!=null&&o.Alive;} public static bool operator !(Object o){return !(bool)o;} }
    public class MonoBehaviour:Object { public bool isActiveAndEnabled=true; }
    public class GameObject:Object { public bool activeInHierarchy=true; }
    public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} }
    public struct Vector2Int { public int x,y; public Vector2Int(int a,int b){x=a;y=b;} }
    public static class Mathf { public static int FloorToInt(float f){return (int)Math.Floor(f);} public static float Max(float a,float b){return Math.Max(a,b);} public static float Abs(float f){return Math.Abs(f);} }
}
public class ZNet:UnityEngine.Object
{
    public static ZNet instance; public static bool IsSinglePlayer; public bool Server,Dedicated;
    public bool IsServer(){return Server;} public bool IsDedicated(){return Dedicated;}
}
namespace TooManyLags { internal static class Plugin { internal static int Warnings; internal static void Warn(string value){Warnings++;} } }

internal class PupReadinessFixture:MonoBehaviour
{
    internal bool Exact=true; internal int ExactCalls;
    protected bool multiplayerManifestReceived=true,multiplayerRejected;
    protected ZNet loadedZNet;
    protected readonly Dictionary<Vector2Int,List<int>> records=new Dictionary<Vector2Int,List<int>>();
    protected readonly Dictionary<Vector2Int,Remote> multiplayerRemoteCells=new Dictionary<Vector2Int,Remote>();
    protected readonly Dictionary<Vector2Int,object> restoreSupportCells=new Dictionary<Vector2Int,object>();
    protected readonly Dictionary<Vector2Int,int> zoneTransitions=new Dictionary<Vector2Int,int>();
    protected readonly Dictionary<Vector2Int,Fast> fastColliderCells=new Dictionary<Vector2Int,Fast>();
    protected readonly Dictionary<Vector2Int,GameObject> clusters=new Dictionary<Vector2Int,GameObject>();
    protected readonly Dictionary<Vector2Int,string> cacheIds=new Dictionary<Vector2Int,string>();
    protected readonly Dictionary<GameObject,string> validatedReplacementCacheIds=new Dictionary<GameObject,string>();
    protected readonly Dictionary<Vector2Int,bool> colliderCellActive=new Dictionary<Vector2Int,bool>();
    protected readonly Dictionary<Vector2Int,HashSet<string>> fastLoadedSubclusters=new Dictionary<Vector2Int,HashSet<string>>();
    protected readonly HashSet<Vector2Int> multiplayerRemotePhysicsReady=new HashSet<Vector2Int>();
    protected readonly HashSet<Vector2Int> verifiedRemoteReplacements=new HashSet<Vector2Int>();
    protected readonly HashSet<Vector2Int> pendingWorldSaveRestores=new HashSet<Vector2Int>();
    protected readonly HashSet<Vector2Int> multiplayerPendingVanillaRestore=new HashSet<Vector2Int>();
    protected readonly HashSet<Vector2Int> loadingCells=new HashSet<Vector2Int>();
    protected int queuedColliderCount;
    protected sealed class Remote { public string cacheId="a"; }
    protected sealed class Fast { public string cacheId="a"; public GameObject root; public HashSet<string> required=new HashSet<string>(); }
    protected static readonly Vector2Int cell=new Vector2Int(0,0);
    internal PupReadinessFixture(){loadedZNet=ZNet.instance;}
    internal void Manifest(bool value){multiplayerManifestReceived=value;}
    internal void Rejected(bool value){multiplayerRejected=value;}
    internal void StaleSession(){loadedZNet=new ZNet();}
    internal void Local(){records[cell]=new List<int>{1};cacheIds[cell]="a";}
    internal void RemoteCell(bool ready)
    {
        Local();multiplayerRemoteCells[cell]=new Remote();clusters[cell]=new GameObject();validatedReplacementCacheIds[clusters[cell]]="a";
        verifiedRemoteReplacements.Add(cell);colliderCellActive[cell]=ready;
        if(ready)multiplayerRemotePhysicsReady.Add(cell);else multiplayerRemotePhysicsReady.Remove(cell);
    }
    internal void Active(bool value){clusters[cell].activeInHierarchy=value;}
    internal void Recipe(string value){if(value==null)cacheIds.Remove(cell);else cacheIds[cell]=value;}
    internal void Restore(bool value){if(value)pendingWorldSaveRestores.Add(cell);else pendingWorldSaveRestores.Remove(cell);}
    internal void Transition(bool value){if(value)zoneTransitions[cell]=1;else zoneTransitions.Remove(cell);}
    internal void FastCell(bool complete)
    {
        Local();Fast fast=new Fast{root=new GameObject()};fast.required.Add("part");fastColliderCells[cell]=fast;
        fastLoadedSubclusters[cell]=new HashSet<string>();if(complete)fastLoadedSubclusters[cell].Add("part");
    }
    protected bool PrepareExactCollision(Vector3 point,float radius){ExactCalls++;return Exact;}
    protected bool IsZoneTransitionNear(Vector3 point,float radius){return zoneTransitions.ContainsKey(cell)||restoreSupportCells.Count>0;}
    protected bool HasPendingCollidersForCell(Vector2Int key){return false;}
    protected bool IsZoneReplacementReady(Vector3 point,float radius)
    {
        for(int z=0;z<2;z++)
        {
            if(ZNet.instance.IsDedicated())continue;
            if(!Exact)return false;
        }
        return true;
    }
}

internal sealed class ModernPupReadinessFixture:PupReadinessFixture
{
    internal bool Prepared=true,ThrowPrepared; internal int PreparedCalls;
    internal Vector2Int LastCell; internal Vector3 LastPoint; internal float LastRadius;
    private bool HasPreparedDedicatedCollision(Vector2Int key,Vector3 position,float radius)
    {
        PreparedCalls++;LastCell=key;LastPoint=position;LastRadius=radius;
        if(ThrowPrepared)throw new InvalidOperationException("fixture proxy query failure");
        return Prepared;
    }
    private new bool IsZoneReplacementReady(Vector3 point,float radius)
    {return HasPreparedDedicatedCollision(new Vector2Int(0,0),point,radius);}
}

internal sealed class ChangedPupReadinessFixture:PupReadinessFixture
{
    private new bool IsZoneReplacementReady(Vector3 point,float radius){return true;}
}

internal static class CombatPupReadinessTests
{
    private static int checks;
    private static readonly Vector3 point=new Vector3(10,0,10);
    private static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
    private static PupReadinessFixture World(bool dedicated)
    {ZNet.instance=new ZNet{Server=dedicated,Dedicated=dedicated};ZNet.IsSinglePlayer=false;return new PupReadinessFixture();}
    public static int Main()
    {
        PupReadinessFixture pup=World(true);NativePup bridge=new NativePup(pup);
        Check(bridge.Present&&bridge.Active,"present active PUP recognized");
        Check(bridge.ReadyPoint(point,16)&&pup.ExactCalls==0,"ordinary area skips exact work");
        pup.Local();pup.Exact=false;Check(!bridge.ReadyPoint(point,16),"dedicated local cell requires exact physics proof");
        pup.Exact=true;Check(bridge.ReadyPoint(point,16),"dedicated exact completed recipe permits proof");
        pup.Recipe(null);Check(!bridge.ReadyPoint(point,16),"missing dedicated recipe identifier blocks proof");
        pup.Recipe("RESTORE_PENDING");Check(!bridge.ReadyPoint(point,16),"restore pending recipe blocks proof");
        pup.Recipe("a");pup.Restore(true);Check(!bridge.ReadyPoint(point,16),"world restore gap blocks proof");
        pup.Restore(false);Check(bridge.ReadyPoint(point,16),"completed restore regains proof");
        pup.Transition(true);Check(!bridge.ReadyPoint(point,16),"zone transition blocks proof");
        pup.Transition(false);pup.StaleSession();Check(!bridge.ReadyPoint(point,16),"server session mismatch denies proof");
        pup=World(false);bridge=new NativePup(pup);pup.Manifest(false);
        Check(!bridge.ReadyPoint(point,16)&&pup.ExactCalls==0,"missing manifest denies even apparently empty area");
        pup.Rejected(true);Check(bridge.ReadyPoint(point,16),"native rejection permits vanilla empty-area proof");
        pup.Manifest(true);pup.RemoteCell(false);Check(!bridge.ReadyPoint(point,16),"incomplete remote physics denies proof");
        pup.RemoteCell(true);Check(bridge.ReadyPoint(point,16),"verified active remote recipe permits proof");
        pup.Active(false);Check(!bridge.ReadyPoint(point,16),"inactive remote replacement denies proof");
        pup.Active(true);pup.Recipe("different");Check(!bridge.ReadyPoint(point,16),"mismatched remote recipe denies proof");
        pup=World(false);bridge=new NativePup(pup);pup.FastCell(false);Check(!bridge.ReadyPoint(point,16),"missing fast subcluster denies proof");
        pup.FastCell(true);Check(bridge.ReadyPoint(point,16),"all required fast subclusters permit proof");
        Check(!bridge.ReadyPoint(new Vector3(float.NaN,0,0),16),"nonfinite coordinates deny proof");
        Check(!bridge.ReadyPoint(new Vector3(float.MaxValue,0,0),16),"extreme coordinates deny proof");
        Check(!bridge.ReadyPoint(point,57),"out-of-contract radius denies proof");
        pup.isActiveAndEnabled=false;Check(!bridge.ReadyPoint(point,16),"present inactive PUP denies proof");
        bridge=new NativePup(null);Check(!bridge.Present&&!bridge.Active,"explicit absent adapter is distinct from present inactive PUP");
        Check(bridge.ReadyPoint(point,16),"absent PUP leaves native server readiness available");
        ZNet.instance=new ZNet{Server=true,Dedicated=true};
        ModernPupReadinessFixture modern=new ModernPupReadinessFixture();bridge=new NativePup(modern);
        Check(bridge.ReadyPoint(point,16)&&modern.ExactCalls==0&&modern.PreparedCalls==0,"modern ordinary area does no preparation or proxy query");
        modern.Local();modern.Prepared=false;
        Check(!bridge.ReadyPoint(point,16)&&modern.ExactCalls==1&&modern.PreparedCalls==1,"Exact true cannot release unprepared dedicated proxies");
        modern.Prepared=true;Check(bridge.ReadyPoint(point,16),"current dedicated proxies permit server handoff");
        Check(modern.LastCell.x==0&&modern.LastCell.y==0&&modern.LastPoint.x==point.x&&modern.LastPoint.z==point.z&&modern.LastRadius==16,"native proxy query receives exact local cell and handoff bounds");
        int before=modern.PreparedCalls;modern.Exact=false;
        Check(!bridge.ReadyPoint(point,16)&&modern.PreparedCalls==before,"pending exact preparation short-circuits proxy work");
        modern.Exact=true;modern.Recipe(null);
        Check(!bridge.ReadyPoint(point,16)&&modern.PreparedCalls==before,"modern missing recipe retains metadata protection");
        modern.Recipe("a");modern.Restore(true);
        Check(!bridge.ReadyPoint(point,16)&&modern.PreparedCalls==before,"modern restore gaps still block server handoff");
        modern.Restore(false);modern.ThrowPrepared=true;int warnings=Plugin.Warnings;
        Check(!bridge.ReadyPoint(point,16)&&Plugin.Warnings==warnings+1,"proxy-query failure denies handoff safely");
        modern.ThrowPrepared=false;Check(bridge.ReadyPoint(point,16),"proxy query recovers without a permanent lock");
        modern.isActiveAndEnabled=false;before=modern.PreparedCalls;
        Check(!bridge.ReadyPoint(point,16)&&modern.PreparedCalls==before,"inactive modern PUP performs no proxy query");
        ZNet.instance=new ZNet();modern=new ModernPupReadinessFixture();bridge=new NativePup(modern);
        modern.RemoteCell(true);modern.Prepared=false;
        Check(bridge.ReadyPoint(point,16)&&modern.PreparedCalls==0,"verified client remote replacement does not use dedicated-only proof");
        ZNet.instance=new ZNet{Server=true,Dedicated=true};modern=new ModernPupReadinessFixture();bridge=new NativePup(modern);
        modern.RemoteCell(true);modern.Prepared=false;
        Check(bridge.ReadyPoint(point,16)&&modern.PreparedCalls==0,"dedicated remote replacement retains its existing remote proof");
        bool rejected=false;
        try{new NativePup(new ChangedPupReadinessFixture());}catch(NotSupportedException){rejected=true;}
        Check(rejected,"unknown dedicated readiness fails closed during startup binding");
        Console.WriteLine("PASS: "+checks+" production optional PUP readiness assertions; managed doubles, no guards or physics state writes.");
        return 0;
    }
}
