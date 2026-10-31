using System;
using System.IO;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value){return value!=null;} }
    public static class Mathf { public static float Max(float a,float b){return Math.Max(a,b);} }
    public static class Time { public static float time=123f;public static float fixedDeltaTime=.02f;public static float smoothDeltaTime=.008f;public static double realtimeSinceStartupAsDouble; }
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
    public struct Quaternion { public float x,y,z,w; public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;} }
    public sealed class Transform { public Vector3 position; public Quaternion rotation=new Quaternion(0f,0f,0f,1f); }
}
internal struct ZDOID
{
    internal long UserID;internal uint ID;
    public static bool operator ==(ZDOID a,ZDOID b){return a.UserID==b.UserID&&a.ID==b.ID;}
    public static bool operator !=(ZDOID a,ZDOID b){return !(a==b);}
    public override bool Equals(object b){return b is ZDOID&&this==(ZDOID)b;}
    public override int GetHashCode(){return UserID.GetHashCode()^ID.GetHashCode();}
}
internal class Character:UnityEngine.Object
{
    internal ZDOID Id=new ZDOID{UserID=4,ID=7};
    internal UnityEngine.Transform transform=new UnityEngine.Transform();
    internal ZDOID GetZDOID(){return Id;}
    internal bool IsDead(){return false;}
}
internal class Humanoid:Character
{
    internal UnityEngine.Vector3 Velocity;
    internal T GetComponent<T>()where T:new(){return new T();}
    internal UnityEngine.Vector3 GetVelocity(){return Velocity;}
}
internal sealed class ZNetView
{
    public ZNetView(){}
    internal bool IsValid(){return true;}
}
internal sealed class MonsterAI {}
internal sealed class ZPackage
{
    private readonly MemoryStream stream;
    private readonly BinaryReader read;
    private readonly BinaryWriter write;
    internal ZPackage():this(new byte[0]){}
    internal ZPackage(byte[] bytes){stream=new MemoryStream();stream.Write(bytes,0,bytes.Length);stream.Position=0;read=new BinaryReader(stream);write=new BinaryWriter(stream);}
    internal void Write(byte value){write.Write(value);}internal void Write(uint value){write.Write(value);}
    internal void Write(float value){write.Write(value);}internal void Write(int value){write.Write(value);}
    internal void Write(long value){write.Write(value);}internal long ReadLong(){return read.ReadInt64();}
    internal void Write(UnityEngine.Vector3 value){write.Write(value.x);write.Write(value.y);write.Write(value.z);}
    internal void Write(UnityEngine.Quaternion value){write.Write(value.x);write.Write(value.y);write.Write(value.z);write.Write(value.w);}
    internal void Write(ZDOID value){write.Write(value.UserID);write.Write(value.ID);}
    internal byte ReadByte(){return read.ReadByte();}internal uint ReadUInt(){return read.ReadUInt32();}
    internal float ReadSingle(){return read.ReadSingle();}internal int ReadInt(){return read.ReadInt32();}
    internal ZDOID ReadZDOID(){return new ZDOID{UserID=read.ReadInt64(),ID=read.ReadUInt32()};}
    internal UnityEngine.Vector3 ReadVector3(){return new UnityEngine.Vector3(read.ReadSingle(),read.ReadSingle(),read.ReadSingle());}
    internal UnityEngine.Quaternion ReadQuaternion(){return new UnityEngine.Quaternion(read.ReadSingle(),read.ReadSingle(),read.ReadSingle(),read.ReadSingle());}
    internal int Size(){return (int)stream.Length;}internal int GetPos(){return (int)stream.Position;}
    internal byte[] GetArray(){return stream.ToArray();}
}
internal sealed class ZNetPeer
{
    internal long m_uid;internal bool m_server;internal readonly ZRpc m_rpc=new ZRpc();
    internal bool IsReady(){return true;}
}
internal sealed class ZRpc
{
    internal int Sends;internal ZPackage Last;
    internal void Invoke(string name,ZPackage packet){++Sends;Last=new ZPackage(packet.GetArray());}
}
internal sealed class ZNet
{
    internal bool Server;
    internal readonly System.Collections.Generic.List<ZNetPeer> Peers=new System.Collections.Generic.List<ZNetPeer>();
    internal bool IsServer(){return Server;}
    internal System.Collections.Generic.List<ZNetPeer> GetPeers(){return Peers;}
    internal ZNetPeer GetServerPeer(){return Peers.Find(p=>p.m_server);}
}
internal static class ZDOMan { internal static long GetSessionID(){return 1;} }
namespace NativeMobPreviewAudit { internal sealed class NativeSteeringPlan{} }
namespace TooManyLags
{
    internal static class MobBridgeFieldCodec
    {
        internal static int Reads;
        internal static uint Hash(NativeMobPreviewAudit.NativeSteeringPlan plan){return 23;}
        internal static void Write(ZPackage packet,NativeMobPreviewAudit.NativeSteeringPlan plan,MonsterAI ai,Humanoid actor,float sourceTime)
        {packet.Write(1907);packet.Write(sourceTime);}
        internal static void Read(ZPackage packet,NativeMobPreviewAudit.NativeSteeringPlan plan,MonsterAI ai,Humanoid actor,Character target,float sourceTime)
        {if(packet.ReadInt()!=1907||packet.ReadSingle()!=sourceTime)throw new Exception("checkpoint timing header shifted native state");++Reads;}
    }
}
