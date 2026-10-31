using System;
using TooManyLags;
using UnityEngine;

// Executes production method bodies extracted verbatim, apart from a sample
// counter and public fixture access. Matrix/Transform engine seam is managed.
namespace UnityEngine
{
    public partial class Transform
    {
        public Vector3 localScale=new Vector3(1,1,1);
        public Matrix4x4 localToWorldMatrix { get { return parent==null?Matrix4x4.TRS(localPosition,localRotation,localScale):parent.localToWorldMatrix*Matrix4x4.TRS(localPosition,localRotation,localScale); } }
        public Vector3 lossyScale { get { return localToWorldMatrix.lossyScale; } }
        public Vector3 InverseTransformPoint(Vector3 point) { return localToWorldMatrix.InversePoint(point); }
    }
    public struct Matrix4x4
    {
        private float[] v;
        public static Matrix4x4 identity { get { return TRS(Vector3.zero,Quaternion.identity,new Vector3(1,1,1)); } }
        public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)
        {
            float x=q.x,y=q.y,z=q.z,w=q.w;
            return new Matrix4x4 {v=new[]{(1-2*y*y-2*z*z)*s.x,(2*x*y-2*w*z)*s.y,(2*x*z+2*w*y)*s.z,p.x,
                (2*x*y+2*w*z)*s.x,(1-2*x*x-2*z*z)*s.y,(2*y*z-2*w*x)*s.z,p.y,
                (2*x*z-2*w*y)*s.x,(2*y*z+2*w*x)*s.y,(1-2*x*x-2*y*y)*s.z,p.z,0,0,0,1}};
        }
        public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)
        {var result=new Matrix4x4{v=new float[16]};for(int r=0;r<4;r++)for(int c=0;c<4;c++)for(int k=0;k<4;k++)result.v[r*4+c]+=a.v[r*4+k]*b.v[k*4+c];return result;}
        public Vector3 MultiplyPoint3x4(Vector3 p)
        {return new Vector3(v[0]*p.x+v[1]*p.y+v[2]*p.z+v[3],v[4]*p.x+v[5]*p.y+v[6]*p.z+v[7],v[8]*p.x+v[9]*p.y+v[10]*p.z+v[11]);}
        public Vector3 lossyScale {get{return new Vector3((float)Math.Sqrt(v[0]*v[0]+v[4]*v[4]+v[8]*v[8]),(float)Math.Sqrt(v[1]*v[1]+v[5]*v[5]+v[9]*v[9]),(float)Math.Sqrt(v[2]*v[2]+v[6]*v[6]+v[10]*v[10]));}}
        public Vector3 InversePoint(Vector3 p)
        {
            p-=new Vector3(v[3],v[7],v[11]);float a=v[0],b=v[1],c=v[2],d=v[4],e=v[5],f=v[6],g=v[8],h=v[9],i=v[10];
            float determinant=a*(e*i-f*h)-b*(d*i-f*g)+c*(d*h-e*g);
            return new Vector3(((e*i-f*h)*p.x+(c*h-b*i)*p.y+(b*f-c*e)*p.z)/determinant,
                ((f*g-d*i)*p.x+(a*i-c*g)*p.y+(c*d-a*f)*p.z)/determinant,
                ((d*h-e*g)*p.x+(b*g-a*h)*p.y+(a*e-b*d)*p.z)/determinant);
        }
    }
}
namespace TooManyLags
{
    internal interface IPreparedRig
    {
        int Samples {get;}
        void Prepare(float time,float weight,Vector3 position,Quaternion rotation);
        bool TryPose(Transform source,out Vector3 p,out Quaternion q,out Vector3 s);
        void Apply();void Restore();
    }
}
internal static class AnimationPassthroughTests
{
    private static int checks;
    private static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    private static Transform[] Rig(float yaw,Vector3 scale)
    {
        var root=new GameObject().transform;root.localPosition=new Vector3(7,3,-5);root.localRotation=Quaternion.AngleAxis(yaw,Vector3.up);root.localScale=scale;
        var rig=new Transform[5];Transform previous=root;
        for(int i=0;i<rig.Length;i++){rig[i]=new GameObject().transform;rig[i].parent=previous;previous.Children.Add(rig[i]);
            rig[i].localPosition=new Vector3(.1f*i,.3f*i,-.2f*i);rig[i].localRotation=Quaternion.AngleAxis(i*13,Vector3.up);rig[i].localScale=new Vector3(1+.1f*i,1+.2f*i,1);previous=rig[i];}
        return rig;
    }
    private static MobBridgeAnimationHistory History(int n)
    {
        var h=new MobBridgeAnimationHistory(n);var p=new Vector3[n];var q=new Quaternion[n];var s=new Vector3[n];
        for(int frame=0;frame<3;frame++){for(int i=0;i<n;i++){p[i]=new Vector3(i*.2f,frame*.1f+i*.1f,i*.03f);q[i]=Quaternion.AngleAxis(frame*25+i*7,Vector3.up);s[i]=new Vector3(1,1+frame*.1f,1);}h.Add(frame*.02f,p,q,s,true);}return h;
    }
    private static Transform[] Attach(Transform[] rig)
    {
        var next=new Transform[rig.Length+1];Array.Copy(rig,next,rig.Length);
        var attachment=new GameObject().transform;attachment.parent=rig[2];rig[2].Children.Add(attachment);
        attachment.localPosition=new Vector3(.8f,.4f,-.2f);attachment.localRotation=Quaternion.AngleAxis(19,Vector3.up);
        attachment.localScale=new Vector3(2,.5f,3);next[rig.Length]=attachment;return next;
    }
    private static void Compare(IPreparedRig a,IPreparedRig b,Transform[] x,Transform[] y)
    {
        for(int i=0;i<x.Length;i++){Vector3 p1,p2,s1,s2;Quaternion q1,q2;
            Check(a.TryPose(x[i],out p1,out q1,out s1)==b.TryPose(y[i],out p2,out q2,out s2),"pose membership changed");
            Check((p1-p2).magnitude<.0002f&&(s1-s2).magnitude<.0002f&&Quaternion.Angle(q1,q2)<.001f,"recorded/native pose differs from verified reference");}
    }
    public static int Main()
    {
        try
        {
            foreach(float yaw in new[]{0f,27f,145f})foreach(Vector3 scale in new[]{new Vector3(1,1,1),new Vector3(2,3,4)})
            foreach(float weight in new[]{0f,.001f,.25f,.75f,.999f,1f})foreach(float time in new[]{0f,.005f,.02f,.035f,.04f})
            {
                Transform[] x=Rig(yaw,scale),y=Rig(yaw,scale);var h=History(x.Length);
                IPreparedRig a=new ReferenceRig(x,h),b=new OptimizedRig(y,h);
                x=Attach(x);y=Attach(y); // Live equipment absent from captured bone mapping must stay live.
                a.Prepare(time,weight,new Vector3(9,4,2),Quaternion.AngleAxis(35,Vector3.up));
                b.Prepare(time,weight,new Vector3(9,4,2),Quaternion.AngleAxis(35,Vector3.up));
                Check(a.Samples==1&&b.Samples==(weight==1?0:1),"full-native pose still interpolates unused recording or attack pose skipped");
                Compare(a,b,x,y);a.Apply();b.Apply();
                x[2].localPosition=y[2].localPosition=new Vector3(2,5,7); // A late external writer must not replace Apply's saved native query snapshot.
                Compare(a,b,x,y);a.Restore();b.Restore();
                for(int i=0;i<x.Length;i++)Check(x[i].localPosition.Equals(y[i].localPosition)&&Quaternion.Angle(x[i].localRotation,y[i].localRotation)<.001f&&x[i].localScale.Equals(y[i].localScale),"apply/restore differs after native IK change");
            }
            var rig=Rig(0,new Vector3(1,1,1));var empty=new MobBridgeAnimationHistory(rig.Length);
            var passthrough=new OptimizedRig(rig,empty);passthrough.Prepare(0,1,Vector3.zero,Quaternion.identity);
            bool failed=false;try{new ReferenceRig(rig,empty).Prepare(0,1,Vector3.zero,Quaternion.identity);}catch(InvalidOperationException){failed=true;}
            Check(failed&&passthrough.Samples==0,"negative control did not detect unconditional history interpolation");
            Console.WriteLine("PASS: "+checks+" extracted production Prepare/TryPose/Apply/Restore comparisons; native/attack weights, nonuniform scale and late-IK snapshots. Managed transform seam.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
