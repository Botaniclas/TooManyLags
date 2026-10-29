using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace UnityEngine.Rendering
{
 public enum ShadowCastingMode { Off, On, ShadowsOnly }
 public enum BlendMode { Zero=0, One=1, SrcAlpha=5, OneMinusSrcAlpha=10 }
 public enum CompareFunction { Always=8 }
 public enum RenderQueue { AlphaTest=2450 }
 public enum LightProbeUsage { Off }
 public enum IndexFormat { UInt16, UInt32 }
 public enum CameraEvent { AfterImageEffects, BeforeImageEffects }
 public sealed class CommandBuffer
 {
  public string name;public bool Released;public readonly List<UnityEngine.Draw> Queued=new List<UnityEngine.Draw>();
  public UnityEngine.Matrix4x4 View,Projection;
  public void Clear(){if(Released)throw new Exception("Released buffer reused.");Queued.Clear();}
  public void SetViewProjectionMatrices(UnityEngine.Matrix4x4 view,UnityEngine.Matrix4x4 projection){View=view;Projection=projection;}
  public void DrawMesh(UnityEngine.Mesh mesh,UnityEngine.Matrix4x4 matrix,UnityEngine.Material material,int sub,int pass,UnityEngine.MaterialPropertyBlock block=null)
  {if(pass!=0)throw new Exception("Only flat pass 0 allowed.");Queued.Add(new UnityEngine.Draw{Mesh=mesh,Material=material,MatrixMarker=matrix.Marker,RecordingMarker=matrix.Marker,Submesh=sub,Colour=material.GetColor(UnityEngine.Shader.PropertyToID("_Color")),Count=1,Instanced=false,ShaderPass=pass,ShaderName=material.Shader.name});}
  public void DrawRenderer(UnityEngine.Renderer renderer,UnityEngine.Material material,int sub,int pass)
  {if(pass!=0)throw new Exception("Only flat pass 0 allowed.");Queued.Add(new UnityEngine.Draw{Renderer=renderer,Material=material,Submesh=sub,Count=1,Instanced=false,ShaderPass=pass,ShaderName=material.Shader.name,RecordingMarker=renderer.localToWorldMatrix.Marker});}
  public void Release(){if(Released)throw new Exception("Double release.");Released=true;Queued.Clear();UnityEngine.Metrics.Releases++;}
 }
}
namespace UnityEngine
{
 public class Object
 {
  static int next; readonly int id=++next;
  public bool Alive=true; public string name; public HideFlags hideFlags;
  public static readonly List<Object> Destroyed=new List<Object>();
  public static implicit operator bool(Object value){return !ReferenceEquals(value,null)&&value.Alive;}
  public int GetInstanceID(){return id;}
  public static void Destroy(Object value){value.Alive=false;Destroyed.Add(value);}
 }
 public enum HideFlags { HideAndDontSave }
 public class GameObject : Object
 {
  public int layer; public bool activeInHierarchy=true;
  public Transform transform=new Transform();
  public readonly List<Component> Components=new List<Component>();
  public readonly List<GameObject> Children=new List<GameObject>();
  public GameObject Parent;
  public T Add<T>() where T:Component,new(){var c=new T();c.gameObject=this;Components.Add(c);return c;}
  public T Get<T>() where T:Component {return Components.OfType<T>().FirstOrDefault();}
  public T GetComponent<T>() where T:class {return Components.OfType<T>().FirstOrDefault();}
  public T[] GetComponentsInChildren<T>(bool inactive) where T:Component {return ChildrenGet<T>();}
  public T GetComponentInChildren<T>(bool inactive) where T:Component {return ChildrenGet<T>().FirstOrDefault();}
  public T ParentGet<T>() where T:Component {for(var g=this;g!=null;g=g.Parent){var c=g.Get<T>();if(c)return c;}return null;}
  public T[] ChildrenGet<T>() where T:Component {Metrics.Scans++;var list=new List<T>();Collect(list);return list.ToArray();}
  void Collect<T>(List<T> list) where T:Component {list.AddRange(Components.OfType<T>());foreach(var c in Children)c.Collect(list);}
  public void Child(GameObject value){value.Parent=this;Children.Add(value);}
 }
 public class Component:Object
 {
  public GameObject gameObject; public Transform transform {get{return gameObject.transform;}}
  public T GetComponent<T>() where T:class {return gameObject.Components.OfType<T>().FirstOrDefault();}
  public T GetComponentInParent<T>() where T:Component {return gameObject.ParentGet<T>();}
  public T GetComponentInParent<T>(bool inactive) where T:Component {return gameObject.ParentGet<T>();}
  public T[] GetComponentsInChildren<T>(bool inactive) where T:Component {return gameObject.ChildrenGet<T>();}
 }
 public class MonoBehaviour:Component {}
 public class Rigidbody:Component {}
 public class Joint:Component {}
 public class Transform {public Vector3 position;public Quaternion rotation;public Vector3 SkinWorldScale=Vector3.one;public Matrix4x4 localToWorldMatrix;}
 public struct Quaternion {public int Marker;}
 public struct Vector3
 {
  public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public float sqrMagnitude {get{return x*x+y*y+z*z;}}
  public static Vector3 one {get{return new Vector3(1,1,1);}}
  public static Vector3 operator-(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
 }
 public struct Bounds {public Vector3 center,size;public bool Visible;public bool Contains(Vector3 p){return Math.Abs(p.x-center.x)<=size.x*.5f&&Math.Abs(p.y-center.y)<=size.y*.5f&&Math.Abs(p.z-center.z)<=size.z*.5f;}}
 public struct Plane {}
 public struct Matrix4x4
 {
  public int Marker;public Vector3 Scale,Translation;public Quaternion Rotation;
  public static Matrix4x4 TRS(Vector3 p,Quaternion r,Vector3 s){return new Matrix4x4{Marker=r.Marker,Translation=p,Rotation=r,Scale=s};}
 }
 public struct Color
 {
  public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}
 }
 public class Renderer:Component
 {
  public bool enabled=true,isVisible=true,forceRenderingOff;
  public Rendering.ShadowCastingMode shadowCastingMode=Rendering.ShadowCastingMode.On;
  public Bounds bounds=new Bounds{Visible=true}; public Matrix4x4 localToWorldMatrix;
  // Production must not need or alter original materials or property blocks.
  public readonly object OriginalMaterial=new object();
  public readonly List<Material> SourceMaterials=new List<Material>{new Material(new Shader{name="Custom/Piece"})};
  public void GetSharedMaterials(List<Material> list){Metrics.MaterialReads++;list.Clear();list.AddRange(SourceMaterials);}
  public Material[] sharedMaterials {get{return SourceMaterials.ToArray();}set{SourceMaterials.Clear();SourceMaterials.AddRange(value);Metrics.Assignments++;}}
 }
 public class MeshRenderer:Renderer {}
 public class ParticleSystemRenderer:Renderer {}
 public class MeshFilter:Component {public Mesh sharedMesh;}
 public class SkinnedMeshRenderer:Renderer
 {
  public Mesh sharedMesh;public int BoneMarker;
  public void BakeMesh(Mesh target,bool scale){if(ReferenceEquals(target,sharedMesh))throw new Exception("Mutated native mesh.");target.subMeshCount=sharedMesh.subMeshCount;target.PoseMarker=BoneMarker;target.BakedScale=scale?transform.SkinWorldScale:Vector3.one;target.BakedWithScale=scale;Metrics.Bakes++;Metrics.LastBake=target;}
 }
 public class Mesh:Object {public int subMeshCount=1,PoseMarker;public bool BakedWithScale;public Vector3 BakedScale;public Rendering.IndexFormat indexFormat;public void MarkDynamic(){}}
 public class Shader:Object
 {
  public bool isSupported=true;public static bool MissingColour;
  public static readonly List<string> Searches=new List<string>();
  public static Shader Find(string name){Searches.Add(name);return name=="Hidden/Internal-Colored"&&MissingColour?null:new Shader{name=name};}
  public static int PropertyToID(string value){return value.GetHashCode();}
 }
 public class Material:Object
 {
  public bool enableInstancing;public int renderQueue=2000;
  public Shader Shader;public Shader shader {get{return Shader;}set{Shader=value;}}
  public string Tag="Opaque";public readonly Dictionary<string,float> Floats=new Dictionary<string,float>();
  public readonly Dictionary<string,int> Ints=new Dictionary<string,int>();
  public readonly Dictionary<int,Color> Colours=new Dictionary<int,Color>();
  public string GetTag(string key,bool fallback,string defaultValue){return Tag;}
  public bool HasProperty(string key){return Ints.ContainsKey(key)||Floats.ContainsKey(key)||Colours.ContainsKey(UnityEngine.Shader.PropertyToID(key));}
  public float GetFloat(string key){return Floats[key];}public int GetInt(string key){return Ints[key];}
  public Material(Shader shader){Shader=shader;Colours[UnityEngine.Shader.PropertyToID("_Color")]=new Color(1,1,1,1);Metrics.Materials++;Metrics.LastMaterial=this;}
  public Material(Material source):this(source.shader){CopyPropertiesFromMaterial(source);}
  public Color GetColor(string key){return GetColor(UnityEngine.Shader.PropertyToID(key));}
  public Color GetColor(int key){return Colours[key];}
  public void SetColor(int key,Color value){Colours[key]=value;Metrics.ColourWrites++;}
  public void CopyPropertiesFromMaterial(Material source){renderQueue=source.renderQueue;Tag=source.Tag;enableInstancing=source.enableInstancing;Floats.Clear();foreach(var x in source.Floats)Floats[x.Key]=x.Value;Ints.Clear();foreach(var x in source.Ints)Ints[x.Key]=x.Value;Colours.Clear();foreach(var x in source.Colours)Colours[x.Key]=x.Value;Metrics.Copies++;}
  public void SetFloat(string name,float value){Floats[name]=value;}public void SetInt(string name,int value){Ints[name]=value;}
  public void EnableKeyword(string name){}public void DisableKeyword(string name){}
 }
 public class MaterialPropertyBlock
 {
  public readonly Dictionary<int,Color> Colours=new Dictionary<int,Color>();
  public void SetColor(int id,Color value){Colours[id]=value;Metrics.ColourWrites++;}
 }
 public class Camera:Component
 {
  public delegate void CameraCallback(Camera camera);
  public static CameraCallback onPreRender;
  [MethodImpl(MethodImplOptions.NoInlining)]private static void FireOnPreRender(Camera camera){if(onPreRender!=null)onPreRender(camera);}
  public static Camera main;public int cullingMask=-1;public Matrix4x4 worldToCameraMatrix=new Matrix4x4{Marker=1},projectionMatrix=new Matrix4x4{Marker=2};
  public readonly List<Rendering.CommandBuffer> Buffers=new List<Rendering.CommandBuffer>();public int Added,Removed;
  public void AddCommandBuffer(Rendering.CameraEvent stage,Rendering.CommandBuffer buffer){if(stage!=Rendering.CameraEvent.BeforeImageEffects||Buffers.Contains(buffer))throw new Exception("Wrong/duplicate camera hook.");Buffers.Add(buffer);Added++;}
  public void RemoveCommandBuffer(Rendering.CameraEvent stage,Rendering.CommandBuffer buffer){if(Buffers.Remove(buffer))Removed++;}
  [MethodImpl(MethodImplOptions.NoInlining)]private static void FireOnPostRender(Camera camera){}
  public void RenderDebug(){FireOnPreRender(this);foreach(var b in Buffers)foreach(var source in b.Queued){var draw=source;if(draw.Renderer){var skin=draw.Renderer as SkinnedMeshRenderer;draw.Mesh=skin?skin.sharedMesh:draw.Renderer.GetComponent<MeshFilter>().sharedMesh;draw.MatrixMarker=draw.Renderer.localToWorldMatrix.Marker;draw.Colour=draw.Material.GetColor(Shader.PropertyToID("_Color"));}Graphics.Draws.Add(draw);}FireOnPostRender(this);}
 }
 public static class SystemInfo {public static bool supportsInstancing=true;}
 public static class Time {public static float unscaledTime;}
 public static class Mathf {public static float Clamp01(float value){return Math.Max(0,Math.Min(1,value));}}
 public static class GeometryUtility
 {
  public static void CalculateFrustumPlanes(Camera camera,Plane[] planes){}
  public static bool TestPlanesAABB(Plane[] planes,Bounds bounds){return bounds.Visible;}
 }
 public struct Draw {public Mesh Mesh;public Renderer Renderer;public Material Material;public int Submesh,MatrixMarker,RecordingMarker;public Color Colour;public int Count,ShaderPass;public bool Instanced;public string ShaderName;}
 public static class Graphics
 {
  public static readonly List<Draw> Draws=new List<Draw>();
  static void Record(Mesh mesh,int count,MaterialPropertyBlock block,bool instanced){Draws.Add(new Draw{Mesh=mesh,Count=count,Colour=block.Colours[Shader.PropertyToID("_Color")],Instanced=instanced});}
  public static void DrawMeshInstanced(Mesh mesh,int sub,Material material,Matrix4x4[] matrices,int count,MaterialPropertyBlock block,Rendering.ShadowCastingMode shadows,bool receive,int layer,Camera camera,Rendering.LightProbeUsage usage,object probes){if(count>1023||count<1)throw new Exception("Invalid batch.");Record(mesh,count,block,true);}
  public static void DrawMesh(Mesh mesh,Matrix4x4 matrix,Material material,int layer,Camera camera,int sub,MaterialPropertyBlock block,Rendering.ShadowCastingMode shadows,bool receive,object anchor,Rendering.LightProbeUsage usage,object probes){Record(mesh,1,block,false);}
 }
 public static class Metrics
 {
  public static int Scans,OwnerReads,ColourWrites,Bakes,Materials,Releases,MaterialReads,Assignments,Copies;public static Mesh LastBake;public static Material LastMaterial;
 }
}
public struct ZDOID:IEquatable<ZDOID>
{
 public long UserID;public uint ID;public bool IsNone(){return UserID==0&&ID==0;}
 public bool Equals(ZDOID v){return UserID==v.UserID&&ID==v.ID;}
 public override bool Equals(object o){return o is ZDOID&&Equals((ZDOID)o);}
 public override int GetHashCode(){return UserID.GetHashCode()^(int)ID;}
}
public class ZDO
{
 public ZDOID m_uid;public bool Valid=true;public long Owner;public ushort revision;
 public bool IsValid(){return Valid;}
 public long GetOwner(){UnityEngine.Metrics.OwnerReads++;return Owner;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void SetOwnerInternal(long value){Owner=value;revision++;}
 public ushort OwnerRevision {get{return revision;}[MethodImpl(MethodImplOptions.NoInlining)]set{revision=value;}}
}
public interface Interactable {}
public interface IDestructible {}
public class Destructible:UnityEngine.MonoBehaviour,IDestructible { public float m_ttl; public UnityEngine.GameObject m_spawnWhenDestroyed; }
public class TreeBase:UnityEngine.MonoBehaviour,IDestructible { public UnityEngine.GameObject m_logPrefab; }
public class MineRock:UnityEngine.MonoBehaviour,IDestructible {}
public class MineRock5:UnityEngine.MonoBehaviour,IDestructible {}
public class TreeLog:UnityEngine.MonoBehaviour,IDestructible {}
public class ModDestructible:UnityEngine.MonoBehaviour,IDestructible {}
public class TestInteractable:UnityEngine.MonoBehaviour,Interactable {}
public class Container:UnityEngine.MonoBehaviour,Interactable { public ZNetView m_rootObjectOverride; }
public class Piece:UnityEngine.MonoBehaviour {}
public class WearNTear:UnityEngine.MonoBehaviour { public bool m_staticPosition = true; }
public class Character:UnityEngine.MonoBehaviour { public bool Tamed; public bool IsTamed(){return Tamed;} }
public class BaseAI:UnityEngine.MonoBehaviour {}
public class Projectile:UnityEngine.MonoBehaviour {}
public class Ship:UnityEngine.MonoBehaviour {}
public class Vagon:UnityEngine.MonoBehaviour {}
public class SpawnArea:UnityEngine.MonoBehaviour {}
public class CreatureSpawner:UnityEngine.MonoBehaviour {}
public class SpawnSystem:UnityEngine.MonoBehaviour {}
public class ZSyncTransform:UnityEngine.MonoBehaviour { public bool m_syncPosition=true,m_syncRotation=true,m_syncBodyVelocity=true; }
public class Humanoid:Character {}
public class Player:Humanoid {}
public class VisEquipment:UnityEngine.MonoBehaviour
{
 [MethodImpl(MethodImplOptions.NoInlining)]public UnityEngine.GameObject AttachItem(){return null;}
 [MethodImpl(MethodImplOptions.NoInlining)]public UnityEngine.GameObject AttachArmor(){return null;}
 [MethodImpl(MethodImplOptions.NoInlining)]public UnityEngine.GameObject AttachBackItem(){return null;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void SetRightItemVisual(){}
}
public class ItemDrop:UnityEngine.MonoBehaviour,Interactable {}
public class ZNetView:UnityEngine.MonoBehaviour
{
 public bool m_persistent=true,m_distant;public ZDO Data;public bool IsValid(){return Data!=null&&Data.Valid;}public ZDO GetZDO(){return Data;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void ResetZDO(){Data=null;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void OnDestroy(){Data=null;Alive=false;}
}
public class ZDOMan
{
 public static ZDOMan instance;public readonly Dictionary<ZDOID,ZDO> Objects=new Dictionary<ZDOID,ZDO>();
 public ZDO GetZDO(ZDOID id){ZDO value;return Objects.TryGetValue(id,out value)?value:null;}
 public static long GetSessionID(){return 1;}
}
public class ZNetScene:UnityEngine.MonoBehaviour
{
 public static ZNetScene instance;
 readonly Dictionary<ZDO,ZNetView> m_instances=new Dictionary<ZDO,ZNetView>();
 [MethodImpl(MethodImplOptions.NoInlining)]public void AddInstance(ZDO data,ZNetView view){m_instances[data]=view;}
}
public class ZNetPeer {public long m_uid=1;public bool Ready=true;public bool IsReady(){return Ready;}}
public class ZNet:UnityEngine.MonoBehaviour
{
 public static ZNet instance;public bool Dedicated,Server;public ZNetPeer Peer=new ZNetPeer();
 public bool IsDedicated(){return Dedicated;}public bool IsServer(){return Server;}public ZNetPeer GetServerPeer(){return Peer;}
 public struct PlayerInfo {public string m_name;public ZDOID m_characterID;}
 public readonly List<PlayerInfo> Players=new List<PlayerInfo>();
 public List<PlayerInfo> GetPlayerList(){return Players;}
 [MethodImpl(MethodImplOptions.NoInlining)]public void Shutdown(bool save=true){}
}
namespace BotanicOwnerDebug
{
 public class Plugin:UnityEngine.MonoBehaviour
 {
  internal const string Id="botanic.owner.debug";internal static Plugin Instance;internal DebugView View;
  internal Action<UnityEngine.Camera> RenderAction;
  internal void Render(UnityEngine.Camera camera){if(RenderAction!=null)RenderAction(camera);}
 }
}
namespace TooManyLags
{
 public static class OwnerDebugApi
 {
  public static ZDO Selected; public static int CurrentPhase;
  public static int Category(ZNetView view){return BotanicOwnerDebug.DebugCategories.Classify(view);}
  public static int Phase(ZDO data){return ReferenceEquals(data,Selected)?CurrentPhase:-1;}
 }
}
public class Door:UnityEngine.MonoBehaviour,Interactable {}
public class Chair:UnityEngine.MonoBehaviour,Interactable {}
public class Barber:UnityEngine.MonoBehaviour,Interactable {}
public class Bed:UnityEngine.MonoBehaviour,Interactable {}
public class CraftingStation:UnityEngine.MonoBehaviour,Interactable {}
public class StationExtension:UnityEngine.MonoBehaviour,Interactable {}
public class Smelter:UnityEngine.MonoBehaviour,Interactable {}
public class CookingStation:UnityEngine.MonoBehaviour,Interactable {}
public class Fireplace:UnityEngine.MonoBehaviour,Interactable {}
public class Beehive:UnityEngine.MonoBehaviour,Interactable {}
public class SapCollector:UnityEngine.MonoBehaviour,Interactable {}
public class PrivateArea:UnityEngine.MonoBehaviour,Interactable {}
public class MapTable:UnityEngine.MonoBehaviour,Interactable {}
public class WispSpawner:UnityEngine.MonoBehaviour,Interactable {}
public class ItemStand:UnityEngine.MonoBehaviour,Interactable {}
public class ArmorStand:UnityEngine.MonoBehaviour,Interactable {}
public class Sign:UnityEngine.MonoBehaviour,Interactable {}
public class TeleportWorld:UnityEngine.MonoBehaviour,Interactable {}
public class Fermenter:UnityEngine.MonoBehaviour,Interactable {}
public class Incinerator:UnityEngine.MonoBehaviour,Interactable {}
public class Trap:UnityEngine.MonoBehaviour,Interactable {}
public class Turret:UnityEngine.MonoBehaviour,Interactable {}
public class ShieldGenerator:UnityEngine.MonoBehaviour,Interactable {}
public class Pickable:UnityEngine.MonoBehaviour,Interactable {}
public class PickableItem:UnityEngine.MonoBehaviour,Interactable {}
public class Feast:UnityEngine.MonoBehaviour,Interactable {}
public class OfferingBowl:UnityEngine.MonoBehaviour,Interactable {}
public class ShipControlls:UnityEngine.MonoBehaviour,Interactable {}
public class Teleport:UnityEngine.MonoBehaviour,Interactable {}
public class Vegvisir:UnityEngine.MonoBehaviour,Interactable {}
