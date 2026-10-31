using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine
{
    public partial class GameObject { public int layer; }
    public partial class Transform
    {
        public Vector3 lossyScale=new Vector3(1,1,1);
        public Vector3 InverseTransformPoint(Vector3 p){return Quaternion.Inverse(rotation)*(p-position);}
    }
    public partial class Rigidbody { public int solverIterations=6; }
    public partial class Collider { public bool isTrigger; public bool Ignored; }
    public sealed class CapsuleCollider:Collider
    { public float radius=.5f,height=2,contactOffset=.01f; public int direction=1; public Vector3 center; }
    public sealed class PlaneCollider:Collider { public Vector3 Normal=Vector3.up; public float Offset; }
    public struct RaycastHit { public Collider collider; public float distance; public Vector3 normal; }
    public enum QueryTriggerInteraction { Ignore }
    // Independent analytic infinite-plane oracle. No copy of resolver logic.
    public static class Physics
    {
        public static readonly List<PlaneCollider> Planes=new List<PlaneCollider>();
        public static int Queries,Resizes;
        public static bool GetIgnoreLayerCollision(int a,int b){return b==31;}
        public static bool GetIgnoreCollision(Collider a,Collider b){return b.Ignored;}
        private static float Gap(Vector3 a,Vector3 b,float r,PlaneCollider p)
        {return Math.Min(Vector3.Dot(a,p.Normal),Vector3.Dot(b,p.Normal))-r-p.Offset;}
        public static int OverlapCapsuleNonAlloc(Vector3 a,Vector3 b,float r,Collider[] output,int mask,QueryTriggerInteraction trigger)
        {
            ++Queries;int count=0;
            foreach(var p in Planes)if((mask&(1<<p.gameObject.layer))!=0&&Gap(a,b,r,p)<0)
            {if(count==output.Length){++Resizes;return count;}output[count++]=p;}
            return count;
        }
        public static int CapsuleCastNonAlloc(Vector3 a,Vector3 b,float r,Vector3 direction,RaycastHit[] output,float distance,int mask,QueryTriggerInteraction trigger)
        {
            ++Queries;int count=0;
            foreach(var p in Planes)
            {
                if((mask&(1<<p.gameObject.layer))==0)continue;
                float closing=-Vector3.Dot(direction,p.Normal);if(closing<=0)continue;
                float t=Math.Max(0,Gap(a,b,r,p)/closing);if(t>distance)continue;
                if(count==output.Length){++Resizes;return count;}
                output[count++]=new RaycastHit{collider=p,distance=t,normal=p.Normal};
            }
            return count;
        }
        public static bool ComputePenetration(Collider c,Vector3 position,Quaternion rotation,Collider other,Vector3 op,Quaternion oq,out Vector3 direction,out float distance)
        {
            ++Queries;var capsule=(CapsuleCollider)c;var plane=(PlaneCollider)other;
            Vector3 scale=capsule.transform.lossyScale;
            Vector3 axis=capsule.direction==0?Vector3.right:capsule.direction==2?Vector3.forward:Vector3.up;
            float along=capsule.direction==0?scale.x:capsule.direction==2?scale.z:scale.y;
            float across=capsule.direction==0?Math.Max(scale.y,scale.z):capsule.direction==2?Math.Max(scale.x,scale.y):Math.Max(scale.x,scale.z);
            float r=capsule.radius*across;Vector3 center=position+rotation*Vector3.Scale(capsule.center,scale);
            Vector3 offset=rotation*axis*Math.Max(0,capsule.height*along*.5f-r);
            distance=Math.Max(0,-Gap(center+offset,center-offset,r,plane));direction=plane.Normal;return distance>0;
        }
    }
}
public partial class Character { public CapsuleCollider Capsule; public CapsuleCollider GetCollider(){return Capsule;} }
namespace TooManyLags
{
    internal static class MobBridgeHit
    {
        internal static readonly Dictionary<Collider,Character> Owners=new Dictionary<Collider,Character>();
        internal static bool TryOwner(Collider c,out Character actor){return Owners.TryGetValue(c,out actor);}
        internal static Collider NativeCollider(Character actor,Collider c){return actor.GetCollider();}
    }
}
