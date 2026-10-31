using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public class GameObject : Object
    {
        public readonly Transform transform;
        public bool activeInHierarchy = true;
        internal readonly List<Component> Components = new List<Component>();
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T Add<T>() where T : Component, new() { T value = new T { gameObject = this }; Components.Add(value); return value; }
        public T GetComponent<T>() where T : Component { foreach (Component c in Components) if (c is T) return (T)c; return null; }
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component
        {
            List<T> found = new List<T>(); Find(found); return found.ToArray();
        }
        private void Find<T>(List<T> found) where T : Component
        { foreach (Component c in Components) if (c is T) found.Add((T)c); foreach (Transform t in transform.Children) t.gameObject.Find(found); }
    }
    public class Component : Object
    {
        public GameObject gameObject; public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class Transform : Object
    {
        public GameObject gameObject; public Transform parent; internal readonly List<Transform> Children = new List<Transform>();
        public Vector3 localPosition; public Quaternion localRotation = Quaternion.identity;
        public Vector3 position { get { return parent == null ? localPosition : parent.position + parent.rotation * localPosition; } set { localPosition = parent == null ? value : Quaternion.Inverse(parent.rotation) * (value - parent.position); } }
        public Quaternion rotation { get { return parent == null ? localRotation : parent.rotation * localRotation; } set { localRotation = parent == null ? value : Quaternion.Inverse(parent.rotation) * value; } }
        public bool IsChildOf(Transform value) { for (Transform t = this; t != null; t = t.parent) if (t == value) return true; return false; }
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
    }
    public class Rigidbody : Component { public Vector3 linearVelocity; public bool isKinematic,useGravity=true; }
    public class Collider : Component { }
    public class Camera : Object
    {
        public static event Action<Camera> onPreCull, onPostRender;
        public static void Before(Camera c) { if (onPreCull != null) onPreCull(c); }
        public static void After(Camera c) { if (onPostRender != null) onPostRender(c); }
        public static bool Subscribed { get { return onPreCull != null || onPostRender != null; } }
    }
    public static class Time { public static double realtimeSinceStartupAsDouble; public static float time; }
    public static class Application { public static bool isBatchMode; }
    public struct Vector3
    {
        public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { float n=magnitude;return n>.00001f?this*(1f/n):new Vector3(); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public static Vector3 operator /(Vector3 a, float b) { return a*(1f/b); }
        public static Vector3 ClampMagnitude(Vector3 value, float max) { float size = (float)Math.Sqrt(value.sqrMagnitude); return size > max ? value*(max/size) : value; }
    }
    // Managed yaw fixtures. Actual Unity quaternion implementation is linked
    // by the release build; these cover horizontal presentation trajectories.
    public struct Quaternion
    {
        public float x, y, z, w;
        private static Quaternion Yaw(float angle) { return new Quaternion { y=(float)Math.Sin(angle/2), w=(float)Math.Cos(angle/2) }; }
        private float AngleRad { get { return 2f*(float)Math.Atan2(y,w); } }
        public static Quaternion identity { get { return Yaw(0); } }
        public static Quaternion Inverse(Quaternion a) { return Yaw(-a.AngleRad); }
        public static Quaternion operator *(Quaternion a, Quaternion b) { return Yaw(a.AngleRad+b.AngleRad); }
        public static Vector3 operator *(Quaternion a, Vector3 b) { float c=(float)Math.Cos(a.AngleRad),s=(float)Math.Sin(a.AngleRad); return new Vector3(c*b.x+s*b.z,b.y,c*b.z-s*b.x); }
        private static float Delta(Quaternion a, Quaternion b) { float d=b.AngleRad-a.AngleRad; while(d>Math.PI)d-=(float)(2*Math.PI); while(d < -Math.PI)d+=(float)(2*Math.PI); return d; }
        public static float Angle(Quaternion a, Quaternion b) { return Math.Abs(Delta(a,b))*180f/(float)Math.PI; }
        public static Quaternion LookRotation(Vector3 v) { return Yaw((float)Math.Atan2(v.x,v.z)); }
        public static Quaternion AngleAxis(float degrees,Vector3 axis) { return Yaw(degrees*(float)Math.PI/180f*(axis.y<0?-1:1)); }
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) { return Yaw(a.AngleRad+Delta(a,b)*Math.Max(0,Math.Min(1,t))); }
        public static Quaternion RotateTowards(Quaternion a, Quaternion b, float degrees) { float d=Angle(a,b); return Slerp(a,b,d<.001f?1:Math.Min(1,degrees/d)); }
    }
}
public struct ZDOID
{
    public long UserID; public uint ID; public static readonly ZDOID None=new ZDOID(); public bool IsNone(){return this==None;}
    public static bool operator ==(ZDOID a,ZDOID b){return a.UserID==b.UserID&&a.ID==b.ID;}
    public static bool operator !=(ZDOID a,ZDOID b){return !(a==b);}
    public override bool Equals(object other){return other is ZDOID&&this==(ZDOID)other;}
    public override int GetHashCode(){return UserID.GetHashCode()^(int)ID;}
}
public class ZDO
{
    public ZDOID m_uid; public bool Valid=true; public long Owner=1; public uint OwnerRevision,DataRevision; public int OwnerWrites;
    public bool IsValid(){return Valid;} public long GetOwner(){return Owner;} public bool IsOwner(){return Owner==ZDOMan.GetSessionID();}
    public void SetOwner(long value){Owner=value;OwnerRevision++;OwnerWrites++;}
    public int Prefab=42;public UnityEngine.Vector3 Position;public int GetPrefab(){return Prefab;}public UnityEngine.Vector3 GetPosition(){return Position;}
}
public class ZDOMan
{
    public static ZDOMan instance; public readonly Dictionary<ZDOID,ZDO> All=new Dictionary<ZDOID,ZDO>();
    public static long SessionID=1; public static long GetSessionID(){return SessionID;} public ZDO GetZDO(ZDOID id){ZDO value;return All.TryGetValue(id,out value)?value:null;}
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_ZDOData(Action deserialize){deserialize();}
}
public class ZNetScene : UnityEngine.Object
{
    public static ZNetScene instance; public readonly Dictionary<ZDO,ZNetView> All=new Dictionary<ZDO,ZNetView>();
    public ZNetView FindInstance(ZDO data){ZNetView value;return All.TryGetValue(data,out value)?value:null;}
    public static bool InActiveArea(UnityEngine.Vector3 position,UnityEngine.Vector3 reference){return (position-reference).sqrMagnitude<=128f*128f;}
}
public class ZNet : UnityEngine.Object
{
    public static ZNet instance; public bool Server; public ZNetPeer Peer=new ZNetPeer();
    public bool IsServer(){return Server;} public ZNetPeer GetServerPeer(){return Peer;}
    public readonly Dictionary<long,ZNetPeer> Peers=new Dictionary<long,ZNetPeer>();
    public ZNetPeer GetPeer(long uid){ZNetPeer value;return Peers.TryGetValue(uid,out value)?value:null;}
    public List<ZNetPeer> GetPeers(){return new List<ZNetPeer>(Peers.Values);}
    [MethodImpl(MethodImplOptions.NoInlining)]public void OnNewConnection(ZNetPeer peer){Peers[peer.m_uid]=peer;}
    [MethodImpl(MethodImplOptions.NoInlining)]public void RPC_PeerInfo(ZRpc rpc){}
    [MethodImpl(MethodImplOptions.NoInlining)]public void Shutdown(){}
}
public class ZNetPeer { public Socket m_socket=new Socket();public long m_uid;public ZRpc m_rpc=new ZRpc();public bool m_server,Ready=true; public UnityEngine.Vector3 Reference;public bool IsReady(){return Ready;}public UnityEngine.Vector3 GetRefPos(){return Reference;} }
public class Socket
{
    public int Ping=50; public bool Fail;
    public void GetConnectionQuality(out float local,out float remote,out int ping,out float sent,out float received)
    {if(Fail)throw new InvalidOperationException("Unsupported socket");local=remote=sent=received=0;ping=Ping;}
}
public class ZRpc
{
    public Action<string,object[]> Send;private readonly Dictionary<string,Delegate> handlers=new Dictionary<string,Delegate>();
    public void Invoke(string method,params object[] args){if(Send!=null)Send(method,args);}
    public void Register<T>(string name,Action<ZRpc,T> handler){handlers[name]=handler;}
    public void Register<T,U>(string name,Action<ZRpc,T,U> handler){handlers[name]=handler;}
    public void Deliver(string name,params object[] values){Delegate method;if(!handlers.TryGetValue(name,out method))return;object[] args=new object[values.Length+1];args[0]=this;Array.Copy(values,0,args,1,values.Length);method.DynamicInvoke(args);}
}
public class ZNetView : UnityEngine.Component
{
    public ZDO Data; public bool IsValid(){return Data!=null&&Data.IsValid();}public ZDO GetZDO(){return Data;}public bool IsOwner(){return Data.IsOwner();}
}
public class Character : UnityEngine.Component
{
    public bool Attacking,Stagger,Dead,Attached,Riding; public UnityEngine.GameObject Visual; public UnityEngine.Vector3 Velocity,MoveDir;
    public bool Flying; public bool IsFlying(){return Flying;}
    public bool m_flying {get{return Flying;}set{Flying=value;}}
    public UnityEngine.Vector3 m_currentVel,m_moveDir;public bool m_run;public float m_currentTurnVel,m_currentTurnVelChange;
    public ZDOID GetZDOID(){ZNetView view=GetComponent<ZNetView>();return view==null?ZDOID.None:view.Data.m_uid;}
    public float m_speed=5f,m_runSpeed=10f,m_flySlowSpeed=5f,m_flyFastSpeed=10f;
    public float m_turnSpeed=300f,m_runTurnSpeed=300f,m_flyTurnSpeed=100f; public UnityEngine.Quaternion Look=UnityEngine.Quaternion.identity;
    public float currentRotSpeedFactor;
    public bool InAttack(){return Attacking;}public bool IsStaggering(){return Stagger;}public bool IsDead(){return Dead;}public bool IsAttached(){return Attached;}public bool IsRiding(){return Riding;}
    public UnityEngine.GameObject GetVisual(){return Visual;}public UnityEngine.Vector3 GetVelocity(){return Velocity;}
    public UnityEngine.Vector3 GetMoveDir(){return MoveDir;}public UnityEngine.Quaternion GetLookYaw(){return Look;}public bool IsRunning(){return false;}
}
public class Humanoid : Character { public Attack m_currentAttack; }
public class Player : Humanoid { public static Player m_localPlayer; }
public class Attack { public bool Done; public bool IsDone(){return Done;} }
public class BaseAI : UnityEngine.Component
{
    public bool m_randomFly,Fresh;public float m_randomFlyTimer,aroundPointUpdateTime,m_randomMoveUpdateTimer,m_randomCircleInterval=5;
    public UnityEngine.Vector3 arroundPointTarget,m_randomMoveTarget;public bool m_reachedRandomMoveTarget;
    public Character Target; public Character GetTargetCreature(){return Target;}
    [MethodImpl(MethodImplOptions.NoInlining)]public void Awake(){m_randomFlyTimer=-1;}
}
public class ZSyncTransform : UnityEngine.Component { public int Flushes; public void SyncNow(){Flushes++;} }
namespace TooManyLags
{
    internal static class Plugin
    {
        internal const string Id="visual-fixture"; internal static int Warnings;
        internal static void Report(string value){Warnings++;Console.WriteLine(value);}
        internal static void Warn(string value){Report(value);}
    }
    internal static class FgnCombatOwnership
    {
        internal static Action<ZNetPeer> FlightConnect=null,FlightProbe=null;
        internal static Func<ZNetPeer,int,bool> FlightMode=null;internal static Action<ZDO,long> FlightTransfer=null;
    }
    internal static class TamedMobHandoff {internal static Action<ZDO,long> FlightTransfer=null;}
    internal static class FgnTargetEvents {internal static Action<BaseAI,Character> FlightAdopt=null;}
    internal static class FgnTargetBoundary {internal static bool FreshTarget(BaseAI ai){return ai.Fresh;}}
    internal static class MobFlightHandoff
    {
        internal static bool Reject=false,Throw=false,ChangeAgain=false;internal static uint AdvanceData=0;
        [MethodImpl(MethodImplOptions.NoInlining)]internal static void Transfer(ZDO data,long destination)
        {if(Throw)throw new InvalidOperationException("rejected checkpoint");if(!Reject){data.DataRevision+=AdvanceData;data.SetOwner(destination);if(ChangeAgain)data.SetOwner(999);}}
    }
}
namespace TooManyLags.PlayerSimulation
{
    internal static class LocalAgent
    {internal static Func<ZDO,long,object> VisualCapture=null;internal static Action<object> VisualCommitted=null;internal static Action<ZDO,long> FlightTransfer=null;}
    internal static class TargetEvents {internal static Action<BaseAI,Character> FlightAdopt=null;}
}
public class ZPackage
{
    private readonly System.IO.MemoryStream stream;private readonly System.IO.BinaryReader reader;private readonly System.IO.BinaryWriter writer;
    public ZPackage():this(new byte[0]){}
    public ZPackage(byte[] bytes){stream=new System.IO.MemoryStream();stream.Write(bytes,0,bytes.Length);stream.Position=0;reader=new System.IO.BinaryReader(stream);writer=new System.IO.BinaryWriter(stream);}
    public byte[] Bytes(){return stream.ToArray();}public int Size(){return (int)stream.Length;}public int GetPos(){return (int)stream.Position;}
    public void Write(byte n){writer.Write(n);}public void Write(long n){writer.Write(n);}public void Write(uint n){writer.Write(n);}public void Write(int n){writer.Write(n);}public void Write(float n){writer.Write(n);}
    public void Write(UnityEngine.Vector3 v){Write(v.x);Write(v.y);Write(v.z);}public void Write(ZDOID id){Write(id.UserID);Write(id.ID);}
    public void Write(bool value){writer.Write(value);}public void Write(string value){writer.Write(value);}
    public void Write(UnityEngine.Quaternion value){Write(value.x);Write(value.y);Write(value.z);Write(value.w);}
    public bool ReadBool(){return reader.ReadBoolean();}public string ReadString(){return reader.ReadString();}
    public UnityEngine.Quaternion ReadQuaternion(){return new UnityEngine.Quaternion{x=ReadSingle(),y=ReadSingle(),z=ReadSingle(),w=ReadSingle()};}
    public byte ReadByte(){return reader.ReadByte();}public long ReadLong(){return reader.ReadInt64();}public uint ReadUInt(){return reader.ReadUInt32();}public int ReadInt(){return reader.ReadInt32();}public float ReadSingle(){return reader.ReadSingle();}
    public UnityEngine.Vector3 ReadVector3(){return new UnityEngine.Vector3(ReadSingle(),ReadSingle(),ReadSingle());}public ZDOID ReadZDOID(){return new ZDOID{UserID=ReadLong(),ID=ReadUInt()};}
}
namespace FiresGhettoNetworkMod
{
    public static class CreatureOwnership
    {
        public static bool Reject;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void OnHandOff(ZRpc rpc,ZDOID id,long destination)
        {ZDO data=ZDOMan.instance.GetZDO(id);if(!Reject)data.SetOwner(destination);}
    }
}

