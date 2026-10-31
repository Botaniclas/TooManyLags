using System;
using TooManyLags;
using UnityEngine;
internal static class BridgeContinuityTests
{
    private static int checks;
    private static void Check(bool valid,string reason){++checks;if(!valid)throw new Exception(reason);}
    private static PlaneCollider Plane(Vector3 normal,float offset)
    {var p=new GameObject().Add<PlaneCollider>();p.Normal=normal.normalized;p.Offset=offset;Physics.Planes.Add(p);return p;}
    private static Character Actor()
    {var g=new GameObject();var a=g.Add<Character>();a.Capsule=g.Add<CapsuleCollider>();g.Add<Rigidbody>();return a;}
    private static void Placement()
    {
        Physics.Planes.Clear();var actor=Actor();var resolver=new MobBridgePlacement(actor);
        Plane(Vector3.up,0);Vector3 from=new Vector3(0,1,0),to=new Vector3(2,1,2);
        Vector3 p=resolver.Resolve(from,to,Quaternion.identity);Check((p-to).magnitude<.00001f,"free native-shaped motion changed");
        int before=Physics.Queries;Check(resolver.Resolve(to,to,Quaternion.identity).Equals(to)&&Physics.Queries==before,"stationary presentation performed queries");
        Physics.Planes.Clear();Plane(new Vector3(-.5f,1,0),0);
        p=resolver.Resolve(new Vector3(0,1.2f,0),new Vector3(2,1.2f,0),Quaternion.identity);
        // Capsule clearance is analytic: lower endpoint minus radius along plane.
        float clearance=Vector3.Dot(p-new Vector3(0,.5f,0),new Vector3(-.5f,1,0).normalized)-.5f;
        Check(clearance>=-.00001f&&p.y>1.2f,"inherited horizontal offset pushed grounded display below slope");
        Physics.Planes.Clear();Plane(new Vector3(-1,0,0),-1);
        p=resolver.Resolve(from,to,Quaternion.identity);Check(p.x<=.50001f&&p.z>1.9f,"wall contact stopped tangential visual correction");
        Physics.Planes.Clear();var ignored=Plane(new Vector3(-1,0,0),-1);ignored.Ignored=true;
        Check((resolver.Resolve(from,to,Quaternion.identity)-to).magnitude<.00001f,"native ignored collider blocked presentation");
        ignored.Ignored=false;ignored.isTrigger=true;
        Check((resolver.Resolve(from,to,Quaternion.identity)-to).magnitude<.00001f,"trigger blocked visual movement");
        ignored.isTrigger=false;MobBridgeHit.Owners[ignored]=actor;
        Check((resolver.Resolve(from,to,Quaternion.identity)-to).magnitude<.00001f,"own fake hitbox blocked its display");MobBridgeHit.Owners.Clear();
        Physics.Planes.Clear();Physics.Resizes=0;for(int i=0;i<40;i++)Plane(new Vector3(-1,0,0),-1);
        p=resolver.Resolve(from,to,Quaternion.identity);Check(p.x<=.50001f&&Physics.Resizes>0,"query overflow lost blocking geometry");
        Physics.Planes.Clear();Plane(Vector3.up,0);
        p=resolver.Resolve(new Vector3(0,1,0),new Vector3(2,-1,0),Quaternion.identity);
        Check(p.y>=.99999f,"descending correction crossed ground");
        Check(actor.transform.position.Equals(Vector3.zero),"resolver mutated actual actor root");
        Physics.Planes.Clear();
    }
    private static void MovingEntry()
    {
        foreach(float frame in new[]{.004f,.008f,.016f,.033f})
        foreach(float lag in new[]{0f,.01f,.03f,.1f,.3f})
        {
            var blend=new MobBridgeTakeover(0,.05f,new Vector3(.3f,0,0),Quaternion.identity,
                new Vector3(.3f-lag*4,0,0),Quaternion.identity,new Vector3(4,0,0),new Vector3(2,0,0));
            float prior=.3f;
            for(int i=1;i<2000;++i)
            {
                double now=i*(double)frame;var real=new Vector3(.3f+4*((float)now-lag),0,0);
                blend.AdvancePosition(real,now,new Vector3(4,0,0));float step=blend.Position.x-prior;
                Check(step>=-.000001f,"moving coherent/stale-root entry reversed; frame="+frame+
                    "; lag="+lag+"; stepIndex="+i+"; displayedStep="+step+"; displayed="+blend.Position.x+
                    "; real="+real.x+"; displayedVelocity="+blend.DisplayedVelocity.x);prior=blend.Position.x;
                if(blend.Settled(now,real))break;
                Check(i<1999,"moving join did not settle");
            }
        }
        var stopped=new MobBridgeTakeover(0,.1f,new Vector3(1,0,0),Quaternion.identity,Vector3.zero,Quaternion.identity,Vector3.zero,new Vector3(2,0,0));
        float last=1;
        for(int i=1;i<200;++i){stopped.AdvancePosition(Vector3.zero,i*.01,Vector3.zero);Check(stopped.Position.x>=last-.000001f,"stopped successor pulled display backwards");last=stopped.Position.x;}
        var capped=new MobBridgeTakeover(0,.1f,new Vector3(.3f,0,0),Quaternion.identity,Vector3.zero,
            Quaternion.identity,new Vector3(4,0,0),new Vector3(2,0,0));
        for(int i=1;i<=10;i++)
        {
            capped.AdvancePosition(new Vector3(i*.04f,0,0),i*.01d,new Vector3(4,0,0));
            Check(capped.Position.x<=.41f,"capped correction moved beyond its initial residual");
        }
        Check(capped.Finished(.1d)&&Math.Abs(capped.Position.x-.4f)<.0001f,
            "capped correction did not end at the authoritative root");
    }
    private static void BoundaryRate()
    {
        var t=new MobBridgeTimeline(0,.01f,1,true);t.AdvanceFrame(0,0);
        t.ManageBuffer(.02f,.02f,0,1,.02f,true,.02f,.04f);t.AdvanceFrame(.04,.04f);
        Check(t.FakeTime(.04)==.02f&&Math.Abs(t.PlaybackRate-.5f)<.000001f,"matched buffer endpoint erased outgoing velocity");
        var wait=new MobBridgeTimeline(0,.01f,1,true);wait.AdvanceFrame(0,0);
        wait.ManageBuffer(.02f,0,0,1,.02f,false,.02f,.04f);wait.AdvanceFrame(.04,.04f);
        Check(wait.FakeTime(.04)<=.02f,"missing destination overran finite reserve");
        var timing=MobBridgeTiming.Create(.01f,1,.02f,.016f,.032f,.02f,.016f,.05f);
        Check(timing.FirstArrival>=.09f&&timing.Startup==.05f&&timing.RecordingWindow> .0384f,"startup application residence omitted from recording budget");
        Check(Math.Abs(timing.MatchForRoute(.06f)-timing.ExpectedMatch)<.00001f,"observed startup delay counted twice");
    }
    public static int Main()
    {
        try{Placement();MovingEntry();BoundaryRate();checks+=AdvanceProbe.Run();Console.WriteLine("PASS: "+checks+" production half-speed boundary/moving-entry/capsule-contact checks; extracted Advance and analytic-plane fixture, no live Unity claim.");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
