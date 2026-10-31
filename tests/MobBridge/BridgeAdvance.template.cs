using System;
using TooManyLags;
using UnityEngine;
internal static class AdvanceProbe
{
    private sealed class Body {internal void ObserveInputs(){} }
    private sealed class Target {internal void Observe(double now){} }
    private sealed class Rate {internal float Estimate=1;}
    private sealed class Simulation {internal long DecisionTick=0;}
    private sealed class Ownership {}
    private sealed class Frame {internal Vector3 Velocity=new Vector3(4,0,0);internal long Tick=1;}
    private sealed class Track
    {
        internal double Last=.04,Started=0,InputAt=0,DestinationSampleWall=.04;
        internal MobBridgeTakeover Takeover=null;
        internal float? Frozen=null;
        internal bool ClockSeen=true,Observer=false;
        internal float DestinationSimulation=.02f,DestinationInterval=.05f,OwnerStep=.02f,MatchingHorizon=.2f,PhysicsInterval=.02f,FirstArrival=.01f;
        internal MobBridgeTimeline Timeline;
        internal MobBridgeRecording History;
        internal readonly Rate Rate=new Rate();
        internal readonly Body Body=new Body();
        internal readonly Target TargetTimeline=new Target();
        internal readonly Simulation Simulation=new Simulation();
        internal readonly Frame DestinationFrame=new Frame();
        internal Ownership Ownership=null;
        internal readonly ZDO Data=new ZDO();
        internal Character Actor;
    }
    private static double joinedAt;
    private static int captureCalls;
    private static string Number(float v){return v.ToString();}
    private static float ObservedDestination(Track t){return t.DestinationSimulation;}
    private static void AdvanceSimulation(Track t,bool ignored){++captureCalls;}
    private static void TakeoverTarget(Track t,double now,out Vector3 position,out Quaternion rotation,out Vector3 velocity)
    {
        position=t.Actor.transform.position;
        rotation=t.Actor.transform.rotation;
        velocity=t.DestinationFrame.Velocity;
    }
    private static void BeginTakeover(Track t,double now)
    {
        joinedAt=now;Vector3 p;Quaternion q;
        t.History.Sample(t.Timeline.FakeTime(now),out p,out q);
        t.Takeover=new MobBridgeTakeover(now,.04f,p,q,t.Actor.transform.position,Quaternion.identity,new Vector3(4,0,0),new Vector3(2,0,0));
    }
@@ADVANCE@@
    internal static int Run()
    {
        var t=new Track{Actor=new GameObject().Add<Character>(),Timeline=new MobBridgeTimeline(0,.01f,1,true),History=new MobBridgeRecording(0,.2f,1)};
        t.Actor.transform.position=new Vector3(.144f,0,0);
        t.History.Add(0,Vector3.zero,Quaternion.identity);t.History.Add(.2f,new Vector3(.8f,0,0),Quaternion.identity);
        t.Timeline.AdvanceFrame(.04,.04f);
        Time.deltaTime=.016f;joinedAt=-1;captureCalls=0;Advance(t,.056);
        if(joinedAt!=.04||t.Takeover==null||captureCalls!=0)throw new Exception("actual Advance missed received previous-frame horizon");
        if(t.Takeover.Position.x<=.08f)throw new Exception("actual Advance repeated previous displayed pose at takeover");
        if(Math.Abs(t.Timeline.FakeTime(.056)-.02f)>.000001f)throw new Exception("actual Advance spent another fake frame before matching");
        return 3;
    }
}
namespace TooManyLags
{
    internal static class MobBridgeProtocol {internal const double ClockInterval=.05;internal static void Report(string line){} }
}
