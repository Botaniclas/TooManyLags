// Isolated native UI/transport doubles; production protection is unchanged.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c) {x=a;y=b;z=c;} public float sqrMagnitude {get{return x*x+y*y+z*z;}} public static Vector3 operator -(Vector3 a,Vector3 b) {return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);} }
    public sealed class Transform { public Vector3 position; }
    public class Component:Object { public GameObject gameObject; public readonly Transform transform=new Transform(); public T GetComponent<T>() where T:class {return gameObject.GetComponent<T>();} }
    public sealed class GameObject:Object { public bool activeInHierarchy=true;private readonly Dictionary<Type,object> parts=new Dictionary<Type,object>(); public T Add<T>() where T:Component,new() {var value=new T {gameObject=this};parts[typeof(T)]=value;return value;}public T GetComponent<T>() where T:class {object value;return parts.TryGetValue(typeof(T),out value)?value as T:null;} }
    public static class Time {public static float unscaledTime;}
}
public struct ZDOID:IEquatable<ZDOID> {public long Value;public ZDOID(long value){Value=value;}public bool IsNone(){return Value==0;}public bool Equals(ZDOID value){return Value==value.Value;}public override bool Equals(object value){return value is ZDOID&&Equals((ZDOID)value);}public override int GetHashCode(){return Value.GetHashCode();}}
public sealed class ZPackage
{
    private readonly MemoryStream bytes;private readonly BinaryReader read;private readonly BinaryWriter write;
    public ZPackage():this(new byte[0]){}public ZPackage(byte[] value){bytes=new MemoryStream();bytes.Write(value,0,value.Length);bytes.Position=0;read=new BinaryReader(bytes);write=new BinaryWriter(bytes);}
    public void Write(int value){write.Write(value);}public void Write(long value){write.Write(value);}public void Write(ushort value){write.Write(value);}public void Write(ZDOID value){write.Write(value.Value);}
    public int ReadInt(){return read.ReadInt32();}public long ReadLong(){return read.ReadInt64();}public ushort ReadUShort(){return read.ReadUInt16();}public ZDOID ReadZDOID(){return new ZDOID(ReadLong());}public int Size(){return (int)bytes.Length;}public int GetPos(){return (int)bytes.Position;}public byte[] GetArray(){return bytes.ToArray();}
}
public sealed class ZDO {public ZDOID m_uid;public long Owner;public ushort OwnerRevision;public bool Valid=true,Text=true;public float Health=1;public UnityEngine.Vector3 Position;public bool IsValid(){return Valid;}public long GetOwner(){return Owner;}public bool IsOwner(){return Owner==ZDOMan.Session;}public float GetFloat(int hash,float fallback){return Health;}public UnityEngine.Vector3 GetPosition(){return Position;}}
public static class ZDOVars {public const int s_health=1;}
public sealed class ZDOMan {public static ZDOMan instance;public static long Session;public int Sends;public readonly Dictionary<ZDOID,ZDO> Objects=new Dictionary<ZDOID,ZDO>();public static long GetSessionID(){return Session;}public ZDO GetZDO(ZDOID id){ZDO value;return Objects.TryGetValue(id,out value)?value:null;}public void ForceSendZDO(long uid,ZDOID id){Sends++;}}
public sealed class ZNetPeer {public long m_uid;public ZDOID m_characterID;public bool m_server,Ready=true,TextSupported=true;public readonly List<ZPackage> Out=new List<ZPackage>();public bool IsReady(){return Ready;}}
public sealed class ZNet:UnityEngine.Object {public static ZNet instance;public bool Server,Dedicated;public readonly List<ZNetPeer> Peers=new List<ZNetPeer>();public bool IsServer(){return Server;}public ZNetPeer GetPeer(long uid){return Peers.Find(p=>p.m_uid==uid);}public ZNetPeer GetServerPeer(){return Peers.Find(p=>p.m_server);}}
public sealed class ZNetView:UnityEngine.Component {public ZDO Data;public bool IsValid(){return Data!=null&&Data.IsValid();}public ZDO GetZDO(){return Data;}}
public interface TextReceiver {void SetText(string text);}
public class Sign:UnityEngine.Component,TextReceiver {public int Saved;public void SetText(string text){Saved++;}}
public sealed class CustomSign:Sign {}
public sealed class TeleportWorld:UnityEngine.Component,TextReceiver {public int Saved;public void SetText(string text){Saved++;}}
public sealed class TextInput:UnityEngine.Object
{
    private TextReceiver m_queuedSign;public int Opened;public bool Visible;public string Topic;public int Limit;
    [MethodImpl(MethodImplOptions.NoInlining)] public void RequestText(TextReceiver receiver,string topic,int limit){m_queuedSign=receiver;Opened++;Visible=true;Topic=topic;Limit=limit;}
    public void Confirm(){if(m_queuedSign!=null)m_queuedSign.SetText("saved");m_queuedSign=null;Hide();}public void Hide(){Visible=false;}
}
public sealed class Player:UnityEngine.Component {public static Player m_localPlayer;public bool Dead;public bool IsDead(){return Dead;}public readonly List<string> Notices=new List<string>();public void Message(MessageHud.MessageType type,string value){Notices.Add(value);}}
public sealed class MessageHud {public enum MessageType {Center}}
public static class PrivateArea {public static bool Allowed=true;public static bool CheckAccess(UnityEngine.Vector3 point){return Allowed;}}
namespace TooManyLagsStandalone
{
    internal enum Message {TextEdit}
    internal static class Plugin {internal const string Id="test.standalone.text";internal static AuthorityController Controller;internal static Network Network=new Network();internal static bool HooksActive=true;internal static void Warn(string text){}}
    internal sealed class Network
    {
        internal int Flushes;
        internal bool TextCapable(long uid){if(ZNet.instance==null)return false;if(uid==ZDOMan.Session)return ZNet.instance.Server&&!ZNet.instance.Dedicated&&Player.m_localPlayer;var peer=ZNet.instance.GetPeer(uid);return peer!=null&&peer.Ready&&peer.TextSupported;}
        internal void Send(long uid,Message message,Action<ZPackage> write){var packet=new ZPackage();write(packet);if(ZNet.instance.Server&&uid==ZDOMan.Session){TextEditingProtection.Receive(null,uid,new ZPackage(packet.GetArray()));return;}var peer=ZNet.instance.GetPeer(uid);if(peer!=null)peer.Out.Add(new ZPackage(packet.GetArray()));}
        internal void FlushAll(){Flushes++;}
    }
    internal sealed class HandoffsStub {internal bool Waiting,InFlight;internal bool Pending(ZDOID id){return Waiting;}internal bool Committing(ZDOID id){return InFlight;}}
    internal sealed class AuthorityController
    {
        internal static Func<ZDO,long> TextEditTarget;internal static Func<ZDO,bool> TextEditLocked;internal static Action<long,ZDO,UnityEngine.Vector3> TextEditMovement;
        internal readonly HandoffsStub Handoffs=new HandoffsStub();internal int Prepares,Ends;internal bool Managed=true,Active=true;
        internal bool ManagesText(ZDO data){return Managed;}internal bool PrepareTextEdit(ZDO data){Prepares++;return data.Text;}internal void EndTextEdit(ZDO data){Ends++;}
        internal static bool Finite(UnityEngine.Vector3 point){return !Single.IsNaN(point.x)&&!Single.IsNaN(point.y)&&!Single.IsNaN(point.z)&&!Single.IsInfinity(point.x)&&!Single.IsInfinity(point.y)&&!Single.IsInfinity(point.z);}
    }
    internal sealed class LocalAgent {internal static Func<ZDO,bool> TextEditBusy;}
    internal static class NativeEvents {internal static Action<ZDO> TextChanged;internal static Func<ZDO,bool> TextImport;}
}
