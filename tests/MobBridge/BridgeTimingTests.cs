using System;
using System.Collections.Generic;
using TooManyLags;
using NativeMobPreviewAudit;

internal static class BridgeTimingTests
{
    private static int checks;
    private static float lastMinimum;
    private static void Check(bool valid,string message){++checks;if(!valid)throw new Exception(message);}
    private static bool Near(float a,float b){return Math.Abs(a-b)<.00001f;}
    private static MobBridgeTiming Plan(float route,float rate,float step,float sender,float receiver)
    {float ready,progress;MobBridgeTiming.DefaultReady(rate,step,sender,out ready,out progress);return MobBridgeTiming.Create(route,rate,step,sender,ready,progress,receiver);}
    private static void Plans()
    {
        var ten=Plan(.01f,1,.02f,.008f,.008f);
        Check(Near(ten.Route,.01f)&&Near(ten.FirstArrival,.04f)&&Near(ten.ExpectedMatch,.04f),"10ms route first movement/matching prediction wrong");
        Check(Near(ten.RequiredWindow,.02f)&&Near(ten.RecordingWindow,.024f),"10ms buffer did not receive exactly20% reserve");
        var sixty=Plan(.01f,1,.02f,.016f,.016f);
        Check(Near(sixty.FirstArrival,.048f)&&Near(sixty.ExpectedMatch,.064f),"first20ms packet was mistaken for a matching24ms state or tail deadline ignored");
        Check(Near(sixty.RecordingWindow,.0384f),"16ms frame reserve changed");
        foreach(float route in new[]{.001f,.005f,.01f,.02f,.049f,.05f,.1f,.5f,1f,2f})
        foreach(float rate in new[]{.1f,.5f,1f,1.5f})
        foreach(float step in new[]{.01f,.02f,.033f})
        foreach(float sender in new[]{.004f,.008f,.016f,.033f,.1f})
        foreach(float receiver in new[]{.004f,.016f,.033f,.1f})
        {
            MobBridgeTiming p=Plan(route,rate,step,sender,receiver);
            Check(p.FirstArrival>=route&&p.ExpectedMatch>=p.FirstArrival,"prediction precedes network or first actual state");
            Check(Math.Abs(p.RecordingWindow/p.RequiredWindow-1.2f)<.000001f,"reserve is not exactly20%");
            Check(Near(p.Horizon,p.RecordingWindow*rate),"recording horizon ignored cached CPU rate");
            Check(p.RequiredWindow*rate+.000001f>=step,"recording contains no full native controller interval");
            Check(Near(p.Route,route),"longer recording changed target/network latency");
        }
        bool failed=false;try{Plan(float.NaN,1,.02f,.008f,.008f);}catch(ArgumentException){failed=true;}
        Check(failed,"invalid cached route accepted");
    }
    private sealed class Packet { internal double At,Sample;internal float Progress; }
    private static void Playback(float route,float frame,float delay,out bool reserveUsed,out float matched)
    {
        MobBridgeTiming p=Plan(route,1,.02f,frame,frame);
        var timeline=new MobBridgeTimeline(0,route,1,true);timeline.ExpectFirst(p.FirstArrival);timeline.ExpectMatch(p.ExpectedMatch);timeline.ExpectFrames(frame);timeline.AdvanceFrame(0,0);
        var cadence=new MobBridgeClockCadence();cadence.Sent(0,-1,0,p.Horizon);
        var packets=new List<Packet>{new Packet{At=route+delay}};int next=0;
        float frontier=0,previous=0;double receivedAt=0;bool seen=false,joined=false;reserveUsed=false;matched=0;lastMinimum=.5f;
        for(int i=1;i<3000;++i)
        {
            double now=i*(double)frame;float physical=(float)(Math.Floor((now+1e-7)/.02d)*.02d);
            if(cadence.Due(now,-1,physical,p.Horizon)){packets.Add(new Packet{At=now+route+delay,Sample=now,Progress=physical});cadence.Sent(now,-1,physical,p.Horizon);}
            while(next<packets.Count&&packets[next].At<=now+1e-7)
            {
                Packet q=packets[next++];frontier=q.Progress;receivedAt=now;seen=true;
                float extra=timeline.ObserveDelivery((float)now,(float)q.Sample);
                timeline.ExpectMatch(p.MatchForRoute(route+extra),extra);timeline.ObserveReal(now);
            }
            if(seen&&frontier>0&&frontier+.000001f>=previous)
            {joined=true;matched=previous;break;}
            float interval=frontier<p.Horizon&&p.Horizon-frontier<.05d?Math.Max(.02f,frame):.05f;
            timeline.ManageBuffer(p.Horizon,frontier,seen?(float)(now-receivedAt):0,1,interval,seen,.02f,frame);
            timeline.AdvanceFrame(now,frame);float actual=timeline.FakeTime(now);
            lastMinimum=Math.Min(lastMinimum,timeline.PlaybackRate);
            Check(actual>=previous&&actual<=p.Horizon&&actual-previous<=frame*.5f+.000001f,"reserved playback rewound, accelerated or overran sealed horizon");
            Check(actual>=p.Horizon-.000001f||timeline.PlaybackRate>0f,
                "fake playback stopped before its actual endpoint: route="+route+" frame="+frame+
                "; wall="+now+"; actual="+actual+"; horizon="+p.Horizon+
                "; rate="+timeline.PlaybackRate+"; frontier="+frontier+"; observed="+seen);
            reserveUsed|=actual>p.RequiredWindow+.000001f;
            if(seen&&frontier+.000001f>=actual){joined=true;matched=actual;break;}
            Check(!timeline.CompleteAtDestination(now,frontier),"estimate allowed takeover before received progress");
            if(delay==0&&now<p.ExpectedMatch)Check(Near(actual,(float)now*.5f),
                "healthy reserved playback brakes before predicted match: route="+route+" frame="+frame+" wall="+now+" actual="+actual+" match="+p.ExpectedMatch+" reserve="+p.Horizon);
            previous=actual;
        }
        Check(joined,"reserved buffer never joined actual progress");
        if(delay==0)Check(matched<p.Horizon,"healthy takeover forced unused reserve to finish");
    }
    private static void Checkpoints()
    {
        var plan=new NativeSteeringPlan();var ai=new MonsterAI();var actor=new Humanoid();var target=new Character();var timing=Plan(.01f,1,.02f,.008f,.008f);
        actor.transform.position=new UnityEngine.Vector3(12f,3f,-8f);
        actor.transform.rotation=new UnityEngine.Quaternion(.1f,.2f,.3f,.9273618f);
        actor.Velocity=new UnityEngine.Vector3(2.5f,-.5f,4f);
        MobBridgeCheckpoint source=MobBridgeCheckpoint.Capture(plan,ai,actor,target,7,.01f,timing);
        MobBridgeCheckpoint received=MobBridgeCheckpoint.Decode(new ZPackage(source.Payload));
        Check(Near(received.Latency,.01f)&&Near(received.RecordingWindow,.024f)&&Near(received.FirstArrival,.04f),"checkpoint merged route/startup/recording values");
        Check(Near(received.Horizon,.024f)&&Near(received.PhysicsStep,.02f),"checkpoint dropped extended matching window");
        Check(Near(received.Origin.x,12f)&&Near(received.Origin.y,3f)&&Near(received.Origin.z,-8f),
            "checkpoint dropped the source cutoff position");
        Check(Near(received.Rotation.y,.2f)&&Near(received.Rotation.w,.9273618f),
            "checkpoint dropped the source cutoff rotation");
        Check(Near(received.Velocity.x,2.5f)&&Near(received.Velocity.y,-.5f)&&Near(received.Velocity.z,4f),
            "checkpoint dropped the source cutoff velocity");
        received.Apply(plan,ai,actor,target);Check(MobBridgeFieldCodec.Reads==1,"portable native state was not restored after timing header");
        for(byte version=1;version<5;++version)
        {
            byte[] wrong=(byte[])source.Payload.Clone();wrong[0]=version;bool invalid=false;
            try{MobBridgeCheckpoint.Decode(new ZPackage(wrong));}catch(InvalidOperationException){invalid=true;}
            Check(invalid,"old checkpoint version was accepted with source pose header: "+version);
        }
        bool failed=false;
        for(int length=0;length<source.Payload.Length-8;++length)
        {byte[] shortData=new byte[length];Array.Copy(source.Payload,shortData,length);failed=false;try{MobBridgeCheckpoint.Decode(new ZPackage(shortData));}catch(Exception){failed=true;}Check(failed,"truncated timing header accepted");}
    }
    public static int Main()
    {
        try
        {
            Plans();Checkpoints();TimingCacheChecks.Run();bool used;float normal,late;
            Playback(.01f,.008f,0,out used,out normal);Check(!used,"healthy10ms case consumed safety reserve");
            Playback(.01f,.016f,.008f,out used,out late);
            Check(lastMinimum>.49999f,"sufficient low-latency reserve braked for a duplicate fake arrival frame");
            Playback(.5f,.008f,0,out used,out normal);
            Playback(.5f,.008f,.024f,out used,out late);Check(used&&late>normal,"later real data did not move handoff into reserve");
            Check(lastMinimum>.49999f,"using sufficient reserve alone slowed half-speed playback");
            foreach(float route in new[]{.001f,.01f,.02f,.05f,.1f,.5f,1f})
            foreach(float frame in new[]{.004f,.008f,.016f,.033f})
            foreach(float delay in new[]{0f,.008f,.08f,.5f})Playback(route,frame,delay,out used,out late);
            Console.WriteLine("PASS: "+checks+" arrival/20%-reserve/checkpoint/horizon checks; modeled timing and native-state codec seam, no live Unity claim.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
