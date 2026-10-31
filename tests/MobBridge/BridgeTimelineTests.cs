using System;
using TooManyLags;
using UnityEngine;

internal static class BridgeTimelineTests
{
    private static int checks;

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static bool Near(float a, float b)
    {
        return Math.Abs(a - b) < .00001f;
    }

    public static int Main()
    {
        try
        {
            Run();
            RecordingChecks();
            AnimationChecks();
            PoseLookupChecks();
            VisualContinuityChecks();
            OwnershipContinuityChecks();
            AdaptiveBufferChecks();
            ClockCadenceChecks();
            SubFrameRecordingChecks();
            Sub50TimingChecks();
            PhysicsPublicationChecks();
            SharedScaleChecks();
            Console.WriteLine("PASS: " + checks + " mob bridge timeline/takeover/native-recording assertions.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static Vector3 StepPosition(MobBridgeTakeover takeover, Vector3 root, double now)
    {
        takeover.AdvancePosition(root, now);
        return takeover.Position;
    }
    private static void SubFrameRecordingChecks()
    {
        foreach(float latency in new[]{.001f,.002f,.005f,.01f,.015f,.019f,.02f,.025f,.033f,.04f,.049f})
        foreach(float frame in new[]{.004f,.008f,.016f,.02f,.033f})
        {
            var capture=new MobBridgeCaptureClock(.02f,latency);
            var history=new MobBridgeRecording(0,latency,1f);
            history.Add(0,Vector3.zero,Quaternion.identity);
            float integrated=0f,previous=0f;int calls=0;float time,dt;bool decision,controller;
            // Exactly the production startup horizon: no elapsed wall time.
            while(capture.Take(0,false,.05f,out time,out dt,out decision,out controller))
            {
                Check(++calls<10,"primed short endpoint loops at zero duration");
                if(decision){Check(time==0,"short startup speculated a future AI decision");continue;}
                integrated+=dt;history.Add(time,new Vector3(integrated*4f,0,0),Quaternion.identity);
                Check(controller==(time>=.02f),"partial physics endpoint changed native-controller cadence");
            }
            Check(history.End>0&&history.End<=latency&&integrated>0,"short bridge still has no moving startup endpoint");
            Vector3 position;Quaternion rotation;
            history.Sample(Math.Min(latency,frame*.5f),out position,out rotation);
            Check(position.x>0&&Near(position.x,Math.Min(latency,frame*.5f)*4f),"sub20ms startup displays t0 instead of interpolated motion");
            for(int step=1;step<1000;++step)
            {
                double wall=step*(double)frame;
                int guard=0;
                while(capture.Take(history.Horizon(wall),wall>=history.Deadline,.05f,out time,out dt,out decision,out controller))
                {
                    Check(++guard<100,"short capture did not terminate");
                    if(decision){Check(time<=history.Horizon(wall)+.000001f,"partial priming advanced future decision");continue;}
                    Check(dt>0&&time<=latency+.000001f,"partial priming changed recording boundary");
                    integrated+=dt;history.Add(time,new Vector3(integrated*4f,0,0),Quaternion.identity);
                }
                if(wall>=history.Deadline)history.Close();
                float sample=Math.Min(latency,(float)wall*.5f);history.Sample(sample,out position,out rotation);
                Check(position.x>=previous&&Near(position.x,sample*4f),"short recording moved backward or changed constant native motion");previous=position.x;
                if(wall>=latency*2d)break;
            }
            Check(Near(integrated,latency),"primed partial endpoint integrated physical time twice");
        }
    }
    private static void SharedScaleChecks()
    {
        var history=new MobBridgeAnimationHistory(2);
        var p=new[]{Vector3.zero,new Vector3(1,2,3)};var q=new[]{Quaternion.identity,Quaternion.identity};
        var s=new[]{new Vector3(1,1,1),new Vector3(2,3,4)};
        history.Add(0,p,q,s,true);history.Add(.02f,p,q,s,true);
        Check(System.Object.ReferenceEquals(history.Export(0).Scales,history.Export(1).Scales),"unchanged scales still allocate per frame");
        s[1]=new Vector3(2,3.000001f,4);history.Add(.04f,p,q,s,true);
        Check(!System.Object.ReferenceEquals(history.Export(1).Scales,history.Export(2).Scales),"tiny real scale change lost through approximate reuse");
        s[1]=new Vector3(99,99,99);history.Sample(.02f,p,q,s);
        Check(s[1].Equals(new Vector3(2,3,4)),"shared scale history retained mutable source scratch");
        history.Sample(.04f,p,q,s);Check(s[1].Equals(new Vector3(2,3.000001f,4)),"new scale snapshot changed older frame or lost precision");
        history.AddRoot(.06f,new Vector3(),Quaternion.identity);
        history.Add(.08f,p,q,s,true);
        Check(System.Object.ReferenceEquals(history.Export(2).Scales,history.Export(4).Scales),"root-only gap defeated immutable scale reuse");
        var longHistory=new MobBridgeAnimationHistory(100);var positions=new Vector3[100];var rotations=new Quaternion[100];var scales=new Vector3[100];
        for(int i=0;i<100;i++){rotations[i]=Quaternion.identity;scales[i]=new Vector3(1,1,1);}
        var unique=new System.Collections.Generic.HashSet<Vector3[]>();
        for(int f=0;f<50;f++){longHistory.Add(f*.02f,positions,rotations,scales,true);unique.Add(longHistory.Export(f).Scales);}
        Check(unique.Count==1,"unchanged100-bone scales allocated50 redundant snapshots");
        Console.WriteLine("ALLOCATION: unchanged scales use101 arrays / 141200 raw bytes instead of150 arrays / 200000 bytes in this 50-frame, 100-bone recording; all poses retained.");
    }
    private static void Sub50TimingChecks()
    {
        foreach(float latency in new[]{.001f,.002f,.005f,.01f,.015f,.02f,.025f,.033f,.04f,.049f})
        foreach(float frame in new[]{.004f,.008f,.016f,.02f,.033f})
        foreach(float firstPositive in new[]{.016f,.033f,.05f})
        {
            var timeline=new MobBridgeTimeline(0,latency,1f,true);timeline.AdvanceFrame(0,0);
            float previous=0f,interval=.05f,lastSample=0f,frontier=0f,lastArrival=0f;int next=0;bool joined=false;
            for(int step=1;step<1000;++step)
            {
                double now=step*(double)frame;
                for(;;)
                {
                    float sample=next==0?0f:firstPositive+(next-1)*.05f;
                    if(latency+sample>now+.0000001d)break;
                    if(next>0)interval=MobBridgeClockCadence.ObserveInterval(interval,sample-lastSample);
                    timeline.ObserveReal(now);frontier=lastSample=sample;lastArrival=latency+sample;next++;
                }
                timeline.ManageBuffer(latency,frontier,next==0?0f:(float)Math.Max(0d,now-lastArrival),1f,interval,next>0);
                timeline.AdvanceFrame(now,frame);float actual=timeline.FakeTime(now);
                Check(actual>=previous&&actual<=latency&&actual-previous<=frame*.5f+.000001f,
                    "sub50ms clock case rewound, extrapolated sealed poses or caught up above scheduled speed");
                if(next>0&&frontier+.000001f>=actual){joined=true;break;}
                Check(!timeline.CompleteAtDestination(now,frontier),"sub50ms bridge handed over to unobserved future progress");
                previous=actual;
            }
            Check(joined,"sub50ms bridge failed to use received matching state");
        }
        foreach(float playback in new[]{.002f,.004f,.01f,.02f,.03f})
            Check(Near(MobBridgeTakeover.SettleForPlayback(playback,0,0,4,300),playback*.5f),
                "fixed20ms floor still lengthens clean low-latency final correction");
        Check(Near(MobBridgeTakeover.SettleForPlayback(.01f,1f,0,5,300),.3f),
            "low-latency base timing shortened a genuine positional correction");
    }

    private sealed class PhysicsPacket
    {
        internal double Arrival;
        internal float Simulation, Sample, Ai;
        internal long Tick;
    }
    private static float PhysicsPlayback(float latency, float frame, bool legacy, float phase, float ownerRate, float delay = 0f)
    {
        var timeline = new MobBridgeTimeline(0, latency, ownerRate, true);
        timeline.AdvanceFrame(0,0);
        var received = new MobBridgeFrame();
        var cadence = new MobBridgeClockCadence();
        var packets = new System.Collections.Generic.List<PhysicsPacket>();
        float actualRoute=Math.Max(.001f,latency+delay);
        packets.Add(new PhysicsPacket { Arrival=actualRoute });
        cadence.Sent(0,-1,0,latency);
        double lastLegacy=0,nextAi=phase;
        long tick=0; float ai=0,minimum=ownerRate*.5f,arrival=0,frontier=0,physicsInterval=0;
        int cursor=0;float previous=0;bool seen=false,joined=false;
        for(int step=1;step<2000;++step)
        {
            double now=step*(double)frame;
            float physical=(float)(Math.Floor((now*ownerRate+1e-7)/.02d)*.02d);
            bool aiPass=false;
            while(now+1e-7>=nextAi){++tick;ai+=.05f;nextAi+=.05d/ownerRate;aiPass=true;}
            bool send=legacy ? aiPass&&(now-lastLegacy>=.05d-1e-7||packets.Count==1&&physical>0) : cadence.Due(now,-1,physical,latency);
            if(send)
            {
                packets.Add(new PhysicsPacket { Arrival=now+actualRoute,Simulation=physical,Sample=(float)now,Tick=tick,Ai=ai });
                cadence.Sent(now,-1,physical,latency);lastLegacy=now;
            }
            while(cursor<packets.Count&&packets[cursor].Arrival<=now+1e-7)
            {
                PhysicsPacket packet=packets[cursor++];
                if(packet.Simulation>frontier&&packet.Simulation<latency&&latency-packet.Simulation<.05d)
                    physicsInterval=Math.Max(physicsInterval,packet.Sample-received.SampleTime);
                Check(received.Observe(packet.Tick,packet.Simulation,packet.Sample,new Vector3(packet.Simulation*4,0,0),
                    Quaternion.identity,new Vector3(4,0,0),-1,"",packet.Ai),"completed physics packet rejected without a new AI pass");
                seen=true;arrival=(float)packet.Arrival;frontier=packet.Simulation;timeline.ObserveReal(now);
            }
            float before=timeline.FakeTime(now);
            if(!legacy&&seen&&before>0&&frontier+1e-6>=before){joined=true;break;}
            bool nativeNotice=!legacy&&seen&&frontier<latency&&latency-frontier<.05d;
            float interval=nativeNotice?Math.Min(.05f,Math.Max(physicsInterval,.02f/ownerRate)):.05f;
            timeline.ManageBuffer(latency*ownerRate,frontier,seen?(float)Math.Max(0,now-arrival):0,ownerRate,interval,seen,legacy?0:.02f);
            timeline.AdvanceFrame(now,frame);float actual=timeline.FakeTime(now);
            Check(actual>=previous&&actual<=latency*ownerRate&&actual-previous<=frame*.5f*ownerRate+1e-6,
                "physics publication rewound, extrapolated recording or accelerated fake playback");
            if(seen&&frontier+1e-6>=actual){joined=true;break;}
            minimum=Math.Min(minimum,timeline.PlaybackRate);previous=actual;
        }
        Check(joined,"physics publication failed to join a received frontier");return minimum;
    }
    private static void PhysicsPublicationChecks()
    {
        var cadence=new MobBridgeClockCadence();cadence.Sent(0,-1,0,.05f);
        Check(cadence.Due(.02,-1,.02f,.05f),"first completed physics step still waits for AI");
        cadence.Sent(.02,-1,.02f,.05f);
        Check(cadence.Due(.05,-1,.04f,.05f),"first progress event postponed regular deadline");
        cadence.Sent(.05,-1,.04f,.05f);
        Check(cadence.Due(.06,-1,.06f,.05f),"finite-window crossing waits for another periodic packet");
        cadence.Sent(.06,-1,.06f,.05f);
        Check(!cadence.Due(.061,-1,.06f,.05f),"horizon event repeated");
        Check(cadence.Due(.1,-1,.1f,.05f),"horizon event postponed periodic deadline");
        var regular=new MobBridgeClockCadence();var tail=new MobBridgeClockCadence();
        regular.Sent(0,-1,0);tail.Sent(0,-1,0,1);
        int regularSends=0,tailSends=0;
        for(int step=1;step<=1000;++step)
        {
            double now=step*.002d;float physical=(float)(Math.Floor((now+1e-7)/.02d)*.02d);
            if(regular.Due(now,-1,physical)){++regularSends;regular.Sent(now,-1,physical);}
            if(tail.Due(now,-1,physical,1)){++tailSends;tail.Sent(now,-1,physical,1);}
        }
        Check(tailSends-regularSends<=Math.Ceiling(.05d/.02d)+2,"tail publication raised the ongoing ordinary send cadence");
        foreach(float latency in new[]{.001f,.005f,.01f,.02f,.033f,.049f,.05f,.1f,.5f,1f})
        foreach(float frame in new[]{.004f,.008f,.016f,.033f})
        foreach(float phase in new[]{.001f,.02f,.049f})
        foreach(float rate in new[]{.5f,1f})
            PhysicsPlayback(latency,frame,false,phase,rate);
        foreach(float latency in new[]{.005f,.02f,.05f,.1f,.5f})
        foreach(float delay in new[]{-.04f,.08f,.5f})
        foreach(float frame in new[]{.008f,.033f})
            PhysicsPlayback(latency,frame,false,.02f,1f,delay);
        float oldMinimum=PhysicsPlayback(.05f,.008f,true,.02f,1f);
        float newMinimum=PhysicsPlayback(.05f,.008f,false,.02f,1f);
        Console.WriteLine("TIMING: actual physics20ms/AI50ms,50ms route; minimum old={0:F3}x new={1:F3}x",oldMinimum,newMinimum);
        Check(oldMinimum<.15f&&newMinimum>.2f,"negative/control low-latency braking reproduction failed");
        var paired=new MobBridgeFrame();
        Check(paired.Observe(0,0,0,Vector3.zero,Quaternion.identity,Vector3.zero,-1,"",0),"initial zero clock rejected");
        Check(paired.Observe(0,.02f,.02f,Vector3.zero,Quaternion.identity,Vector3.zero,-1,"",0),"body-only progress rejected");
        Check(!paired.Observe(0,.02f,.03f,Vector3.zero,Quaternion.identity,Vector3.zero,-1,"",0),"repeated body frontier refreshed observation");
        Check(!paired.Observe(0,.04f,.04f,Vector3.zero,Quaternion.identity,Vector3.zero,-1,"",.05f),"body-only clock invented an AI pass");
        Check(!paired.Observe(0,.01f,.04f,Vector3.zero,Quaternion.identity,Vector3.zero,-1,"",0),"body clock rewound");
        Check(paired.Observe(1,.04f,.04f,Vector3.zero,Quaternion.identity,Vector3.zero,-1,"",.05f),"subsequent real AI progress rejected");
        var missed=new MobBridgeTimeline(0,.02f,1,true);missed.AdvanceFrame(.032,.032f);
        Check(missed.FakeTime(.048)<=.02f,"previously displayed horizon should match received20ms state");
        missed.AdvanceFrame(.048,.016f);
        Check(missed.FakeTime(.048)>.02f,"negative control did not reproduce advancing past an already received match");
    }

    private static float BurstPlayback(float latency, float frame, bool filter)
    {
        var timeline = new MobBridgeTimeline(0, latency, 1, true);
        timeline.AdvanceFrame(0, 0);
        float minimum = .5f, previous = 0f, previousSample = 0f, interval = .05f, frontier = 0f, arrival = 0f;
        int next = 0;
        for (int step = 1; step < 1000; ++step)
        {
            double now = frame * (double)step;
            // Initial clock followed by an immediate event/old first-AI clock.
            // Ordinary periodic states still come only every 50ms afterwards.
            for (;;)
            {
                float sample = next == 0 ? 0f : .001f + (next - 1) * .05f;
                if (latency + sample > now + .0000001d) break;
                if (next != 0)
                {
                    float gap = sample - previousSample;
                    interval = filter ? MobBridgeClockCadence.ObserveInterval(interval, gap) : gap;
                    timeline.ObserveRealRate(1f, interval);
                }
                timeline.ObserveReal(now); previousSample = sample; frontier = sample; arrival = latency + sample; next++;
            }
            timeline.ManageBuffer(latency, frontier, next == 0 ? 0f : (float)Math.Max(0d, now - arrival), 1f, interval, next != 0);
            timeline.AdvanceFrame(now, frame);
            float actual = timeline.FakeTime(now);
            Check(actual >= previous && actual <= latency && actual - previous <= .5f * frame + .000001f,
                "burst filtering rewound/exceeded sealed buffer or accelerated presentation");
            if (next != 0 && frontier + .000001f >= actual) return minimum;
            if (actual < latency * .8f) minimum = Math.Min(minimum, timeline.PlaybackRate);
            if (filter) Check(timeline.PlaybackRate > 0f, "known next healthy packet still exhausts short recording before arrival");
            previous = actual;
        }
        throw new Exception("Healthy burst stream never reached observed matching horizon");
    }

    private static void ClockCadenceChecks()
    {
        var cadence = new MobBridgeClockCadence();
        Check(cadence.Due(0, -1f,0f), "new epoch cannot publish initial state at wall zero");
        cadence.Sent(0, -1f,0f);
        Check(!cadence.Due(.001, -1f,0f), "first AI pass duplicated just-sent setup clock");
        Check(!cadence.Due(.05, -1f,0f) && cadence.Due(.05, -1f,0f,0,1),
            "unchanged epoch emitted duplicate physics or dropped new AI progress");
        Check(cadence.Due(.001, 0f,0f), "real attack event waits for ordinary periodic deadline");
        cadence.Sent(.001, 0f,0f);
        Check(!cadence.Due(.002, 0f,0f), "same attack event can burst repeatedly");
        Check(cadence.Due(.002, .001f,0f), "second distinct attack event lost");
        cadence.Sent(-1, -1f,0f);
        Check(!cadence.Due(.003, 0f,0f), "out-of-order send moved epoch clock backwards");
        Check(!cadence.Due(Double.NaN, 0f,0f), "invalid time permits clock flood");
        Check(cadence.Due(.025,0f,.02f), "useful first positive owner progress delayed until periodic packet");
        cadence.Sent(.025,0f,.02f);
        Check(!cadence.Due(.03,0f,.03f), "first-positive exception turned into extra continuous clock sends");
        Check(cadence.Due(.05,0f,.05f) && !cadence.Due(.04999,0f,.05f), "first-positive event postponed the ordinary periodic deadline");
        Check(Near(MobBridgeClockCadence.InitialInterval(1f), .05f) &&
            Near(MobBridgeClockCadence.InitialInterval(.5f), .05f) &&
            Near(MobBridgeClockCadence.InitialInterval(1.5f), .05f),
            "initial packet cadence confuses faster/slower AI with the protocol send budget");
        float interval = .1f;
        foreach(float gap in new[]{0f,.00001f,.001f,.02f,Single.NaN,-.01f})
        {
            interval = MobBridgeClockCadence.ObserveInterval(interval, gap);
            Check(Near(interval, .1f), "initial/event interval replaced ordinary CPU-aware cadence");
        }
        interval = MobBridgeClockCadence.ObserveInterval(interval, .075f);
        Check(Near(interval,.075f) && Near(MobBridgeClockCadence.ObserveInterval(interval,.05f),.05f),
            "genuine slower cadence or later recovery ignored");
        foreach(float latency in new[]{.05f,.1f})
        {
            float oldMinimum = BurstPlayback(latency, 1f/120f, false);
            float correctedMinimum = BurstPlayback(latency, 1f/120f, true);
            Console.WriteLine("TIMING: {0:F0}ms buffer; first80% min: raw startup/event cadence={1:F3}x; corrected={2:F3}x", latency*1000f, oldMinimum, correctedMinimum);
            Check(oldMinimum < .4f, "negative control failed to reproduce near-stopped low-latency playback");
            Check(correctedMinimum > .45f, "initial/event clock burst excessively brakes the first 80% of 50/100ms playback");
        }
        foreach(float latency in new[]{.05f,.1f})
        foreach(float frame in new[]{.004f,.008f,.016f,.02f,.033f})
        {
            var regular = new MobBridgeTimeline(0, latency, 1f, true);
            regular.AdvanceFrame(0,0);
            float previous = 0f; bool joined = false;
            for(int step=1; step<1000; ++step)
            {
                double now = step*(double)frame;
                bool seen = now + .0000001d >= latency;
                int tick = seen ? (int)Math.Floor((now-latency+.0000001d)/.05d) : 0;
                float frontier = tick*.05f;
                regular.ManageBuffer(latency,frontier,seen?(float)Math.Max(0d,now-latency-frontier):0f,1f,.05f,seen);
                regular.AdvanceFrame(now,frame);
                float actual=regular.FakeTime(now);
                if(seen && frontier+.000001f>=actual){joined=true;break;}
                Check(Near(regular.PlaybackRate,.5f) && Near(actual-previous,frame*.5f),
                    "ordinary 50/100ms handoff no longer preserves half-speed continuous playback");
                previous=actual;
            }
            Check(joined,"ordinary handoff waits beyond received matching state");
        }
    }

    private static void OwnershipContinuityChecks()
    {
        var blend = new MobBridgeTakeover(0, 2.187f, new Vector3(10, 0, 0), Quaternion.identity,
            new Vector3(7.083f, 0, 0), Quaternion.identity, new Vector3(1, 0, 0), new Vector3(.5f, 0, 0));
        for (int i = 1; i <= 180; ++i)
            blend.AdvancePosition(new Vector3(7.083f + i * .01f, 0, 0), i * .01, new Vector3(1, 0, 0));
        Vector3 before = blend.Position;
        Vector3 velocityBefore = blend.DisplayedVelocity;
        float weight = blend.AnimationWeight(1.81), animationTime = blend.AnimationTime(.995f, 1.81);
        Quaternion visibleRotation = blend.Rotation(Quaternion.identity, 1.8);
        Quaternion gainedRotation = Quaternion.AngleAxis(120, Vector3.up);
        blend.RebaseOwnership(1.81, 2.187f, visibleRotation, new Vector3(8.283f, 0, 0), gainedRotation, new Vector3(-1, 0, 0));
        Check((blend.Position - before).magnitude < .000001f && Quaternion.Angle(blend.Rotation(gainedRotation, 1.81), visibleRotation) < .001f,
            "ownership reset changed the last displayed pose");
        Check(blend.AnimationWeight(1.81) == weight && blend.AnimationTime(.995f, 1.81) == animationTime,
            "ownership rebase restarted animation clock/weights");
        blend.AdvancePosition(new Vector3(8.273f, 0, 0), 1.82, new Vector3(-1, 0, 0));
        Check((blend.Position - before).magnitude < .02f * (velocityBefore.magnitude + 1f) * 1.01f,
            "reproduced owner gain still jumps instead of continuing prior velocity through the sampled interval");

        var ownerGain = new MobBridgeTakeover(0, .2f, new Vector3(10, 1, 0), Quaternion.identity,
            new Vector3(10.8f, 1.2f, 0), Quaternion.identity,
            new Vector3(6, 0, 0), new Vector3(4, .2f, 1));
        Vector3 priorGain = ownerGain.Position;
        Vector3 firstStep = Vector3.zero, finalStep = Vector3.zero;
        for (int i = 1; i <= 200; ++i)
        {
            double now = i * .001d;
            Vector3 native = new Vector3(10.8f + 6 * (float)now, 1.2f, 0);
            ownerGain.AdvancePosition(native, now, new Vector3(6, 0, 0));
            Vector3 step = (ownerGain.Position - priorGain) / .001f;
            if (i == 1) firstStep = step;
            if (i == 200) finalStep = step;
            priorGain = ownerGain.Position;
        }
        Check((firstStep - new Vector3(4, .2f, 1)).magnitude < .15f,
            "owner gain did not continue the replica velocity; first=" + firstStep.x + "," +
            firstStep.y + "," + firstStep.z + "; expected=4,0.2,1");
        Check((finalStep - new Vector3(6, 0, 0)).magnitude < .15f,
            "owner gain did not finish at the authoritative velocity");

        foreach (Vector3 direction in new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1) })
        foreach (float frame in new[] { .004f, .016f, .033f })
        {
            var moving = new MobBridgeTakeover(0, .2f, Vector3.zero, Quaternion.identity,
                Vector3.zero, Quaternion.identity, direction * 2, direction * 2);
            moving.AdvancePosition(direction * .2f, .1, direction * 2);
            Vector3 prior = moving.Position, velocity = moving.DisplayedVelocity;
            float oldWeight = moving.AnimationWeight(.1), oldTime = moving.AnimationTime(.5f, .1);
            Vector3 reset = prior - direction * .6f;
            moving.RebaseOwnership(.1, .5f, Quaternion.identity, reset, gainedRotation, direction * -2);
            Check(moving.AnimationWeight(.1) == oldWeight && moving.AnimationTime(.5f, .1) == oldTime,
                "ownership layer changed existing skeletal fade at entry");
            moving.AdvancePosition(reset + direction * -.000002f, .100001, direction * -2);
            Check(((moving.Position - prior) / .000001f - velocity).magnitude < .04f,
                "real direction reversal immediately stops visible movement at rebase; prior="+
                prior.x+","+prior.y+","+prior.z+" position="+moving.Position.x+","+moving.Position.y+","+
                moving.Position.z+" velocity="+velocity.x+","+velocity.y+","+velocity.z+" displayed="+
                moving.DisplayedVelocity.x+","+moving.DisplayedVelocity.y+","+moving.DisplayedVelocity.z);
            Check(moving.AnimationWeight(.100001) >= oldWeight && moving.AnimationTime(.5f, .100001) >= oldTime,
                "position rebase restarted native skeletal fade");
            prior = moving.Position;
            for (int step = 1; step <= 240; ++step)
            {
                double now = .100001 + frame * (double)step;
                Vector3 root = reset - direction * (2f * (float)(now - .1));
                moving.AdvancePosition(root, now, direction * -2);
                Check((moving.Position - prior).magnitude < frame * 9f + .00001f, "late-frame ownership curve snaps after a wait/reversal");
                prior = moving.Position;
            }
            Check((moving.Position - (reset - direction * (2f * (float)(frame * 240 + .000001)))).magnitude < .00001f,
                "ownership correction never rejoins constant native travel");

            var stopped = new MobBridgeTakeover(0, .1f, direction, Quaternion.identity,
                Vector3.zero, Quaternion.identity, direction * 2, direction);
            stopped.AdvancePosition(Vector3.zero, .1, direction * 2);
            Vector3 held = stopped.Position;
            stopped.RebaseOwnership(.1, .2f, Quaternion.identity, Vector3.zero, Quaternion.identity, Vector3.zero);
            for(int step=1;step<=100;step++)
            {
                stopped.AdvancePosition(Vector3.zero, .1+step*(double)frame, Vector3.zero);
                Check(Vector3.Dot(stopped.Position - held,direction) >= -.000001f, "stopped successor pulls displayed mob backwards instead of braking/waiting");
                held = stopped.Position;
            }
            Check(!stopped.Settled(5, Vector3.zero), "unsettled ownership correction expires on its old deadline");
            Vector3 catchUp = held + direction * .02f;
            stopped.AdvancePosition(catchUp, 6, direction);
            Check(stopped.Settled(6, catchUp), "waiting correction cannot retire after observed root catches up; frame="+frame+
                "; shown="+stopped.Position.x+","+stopped.Position.y+","+stopped.Position.z+
                "; target="+catchUp.x+","+catchUp.y+","+catchUp.z);
        }
    }

    private static void AdaptiveBufferChecks()
    {
        foreach (float latency in new[] { .02f, .05f, .1f, .5f, 1f })
        foreach (float frame in new[] { .004f, .008f, .016f, .033f })
        foreach (float delay in new[] { 0f, .08f, .5f })
        {
            var timeline = new MobBridgeTimeline(0d, latency, 1f, true);
            timeline.AdvanceFrame(0d, 0f);
            const float period = .05f;
            float previous = 0f;
            bool slowed = false, joined = false;
            for (int step = 1; step * frame < 2f * latency + delay + 2f; ++step)
            {
                double wall = step * (double)frame;
                double first = latency + delay;
                bool observed = wall >= first;
                int packet = observed ? (int)Math.Floor((wall - first + .00000001d) / period) : 0;
                float frontier = packet * period;
                float age = observed ? (float)Math.Max(0d, wall - first - frontier) : 0f;
                timeline.ManageBuffer(latency, frontier, age, 1f, period, observed);
                if (observed && !timeline.RealStarted) timeline.ObserveReal(wall);
                float incomingRate = timeline.PlaybackRate;
                timeline.AdvanceFrame(wall, frame);
                float actual = timeline.FakeTime(wall);
                Check(actual >= previous && actual <= latency && timeline.PlaybackRate >= 0f,
                    "adaptive buffer rewound, exceeded immutable endpoint or reversed speed");
                Check(actual - previous <= Math.Max(.5f, incomingRate) * frame + .000001f,
                    "returning packets replayed held backlog at excess speed");
                slowed |= timeline.PlaybackRate < .49f && actual < latency;
                if (observed && frontier + .000001f >= actual)
                { joined = true; break; }
                Check(!timeline.CompleteAtDestination(wall, frontier),
                    "estimated packet progress completed takeover without an observed matching frontier");
                previous = actual;
            }
            Check(joined, "adaptive playback did not hand over when actual destination caught up");
            if (delay >= .5f) Check(slowed, "missing updates did not slow remaining finite buffer");
        }
        var nominal = new MobBridgeTimeline(0d, .1f, 1f, true);
        nominal.AdvanceFrame(0d, 0f);
        for (int step = 1; step <= 19; ++step)
        {
            double now = step * .01d;
            float frontier = step >= 15 ? .05f : 0f;
            float age = step >= 15 ? (float)(now - .15d) : step >= 10 ? (float)(now - .1d) : 0f;
            nominal.ManageBuffer(.1f, frontier, age, 1f, .05f, step >= 10);
            nominal.AdvanceFrame(now, .01f);
            Check(Near(nominal.PlaybackRate, .5f) && Near(nominal.FakeTime(now), step * .005f),
                "healthy normal packet spacing unnecessarily slows nominal half-speed playback");
        }
        var waiting = new MobBridgeTimeline(0d, .1f, 1f, true);
        waiting.AdvanceFrame(0d, 0f);
        for (int i = 1; i <= 1000; ++i)
        {
            waiting.ManageBuffer(.1f, 0f, 0f, 1f, .05f, false);
            waiting.AdvanceFrame(i * .01d, .01f);
        }
        float held = waiting.FakeTime(10d);
        Check(held <= .1f && held > .099f && waiting.PlaybackRate < .00001f,
            "prolonged outage did not settle into endpoint waiting");
        waiting.ManageBuffer(.1f, .1f, 0f, 1f, .05f, true);
        waiting.ObserveReal(10d);
        Check(waiting.CompleteAtDestination(10d, .1f) && Near(waiting.FakeTime(10d), held),
            "received matching endpoint rewrote fake progress instead of allowing direct takeover");

        var entry = new MobBridgeTimeline(0d, .1f, 1f, true);
        entry.AdvanceFrame(0d, 0f); entry.AdvanceFrame(.16d, .16f);
        float entryPosition = entry.FakeTime(.16d), entryRate = entry.PlaybackRate;
        entry.ManageBuffer(.1f, 0f, 0f, 1f, .05f, true);
        Check(Near(entry.PlaybackRate, entryRate) && Near(entry.FakeTime(.16d), entryPosition),
            "new delay evidence steps displayed speed/position before clock advance");
        entry.AdvanceFrame(.16001d, .00001f);
        Check(Math.Abs((entry.FakeTime(.16001d) - entryPosition) / .00001f - entryRate) < .002f,
            "buffer braking does not preserve incoming displayed velocity");
        entry.ManageBuffer(Single.NaN, 0f, 0f, 1f, .05f, true);
        Check(MobBridgeTimeline.Finite(entry.FakeTime(.16001d)), "invalid buffer evidence corrupts clock");
        var coarse = new MobBridgeTimeline(0d, .1f, 1f, true);
        var fine = new MobBridgeTimeline(0d, .1f, 1f, true);
        foreach (var clock in new[] { coarse, fine })
        { clock.AdvanceFrame(0d, 0f); clock.AdvanceFrame(.16d, .16f); }
        coarse.ManageBuffer(.1f, 0f, 0f, 1f, .05f, false);
        coarse.AdvanceFrame(.24d, .08f);
        for (int i = 1; i <= 80; ++i)
        {
            fine.ManageBuffer(.1f, 0f, 0f, 1f, .05f, false);
            fine.AdvanceFrame(.16d + i * .001d, .001f);
        }
        Check(Math.Abs(coarse.FakeTime(.24d) - fine.FakeTime(.24d)) < .000001f &&
            Math.Abs(coarse.PlaybackRate - fine.PlaybackRate) < .00001f,
            "critically damped outage playback depends on render frame subdivision");
        var lowPing = new MobBridgeTimeline(0d, .02f, 1f, true);
        lowPing.AdvanceFrame(0d, 0f); lowPing.AdvanceFrame(.02d, .02f);
        lowPing.ManageBuffer(.02f, 0f, 0f, 1f, .05f, true);
        lowPing.AdvanceFrame(.04d, .02f);
        Check(lowPing.FakeTime(.04d) < .02f && lowPing.PlaybackRate < .5f,
            "20ms recording spent its endpoint before next 50ms owner packet could arrive");
        lowPing.ManageBuffer(.04f, 0f, 0f, 1f, .05f, false);
        lowPing.AdvanceFrame(.2d, .16f);
        Check(lowPing.FakeTime(.2d) <= .02f, "arriving evidence extended original immutable recording endpoint");
    }

    private static void VisualContinuityChecks()
    {
        foreach (float latency in new[] { .02f, .05f, .1f, .5f, 1f })
        foreach (double frame in new[] { .004d, .008d, .016d, .033d })
        {
            var capture = new MobBridgeCaptureClock(.02f, latency);
            var recording = new MobBridgeRecording(0d, latency, 1f);
            recording.Add(0f, new Vector3(), Quaternion.identity);
            float integrated = 0f, previousPlayback = 0f, previousPosition = 0f;
            int motors = 0;
            for (double wall = frame; wall < latency * 2d; wall += frame)
            {
                if (!recording.Closed)
                {
                    float time, dt; bool decision, controller;
                    while (capture.Take(recording.Horizon(wall), wall >= recording.Deadline, .05f,
                        out time, out dt, out decision, out controller))
                    {
                        if (decision) { Check(time <= recording.Horizon(wall) + .000001f, "priming speculated a future AI decision"); continue; }
                        integrated += dt; ++motors;
                        Check(time <= latency + .000001f, "priming escaped immutable recording window");
                        recording.Add(time, new Vector3(integrated * 2f, 0f, 0f), Quaternion.identity);
                    }
                    if (wall >= recording.Deadline) recording.Close();
                }
                float playback = Math.Min(latency, (float)wall * .5f);
                Vector3 position; Quaternion rotation;
                recording.Sample(playback, out position, out rotation);
                Check(Near(position.x - previousPosition, (playback - previousPlayback) * 2f),
                    "causal native endpoints still cause startup hold/catch-up at render cadence " + frame);
                previousPosition = position.x; previousPlayback = playback;
            }
            Check(Near(integrated, latency) && motors == (int)Math.Ceiling((latency - .000001f) / .02f),
                "startup interpolation added native simulation work or changed final horizon");
        }
        var velocityRecording = new MobBridgeRecording(0d, .1f, 1f);
        velocityRecording.Add(0f, new Vector3(), Quaternion.identity);
        velocityRecording.Add(.02f, new Vector3(.08f, 0f, 0f), Quaternion.identity);
        velocityRecording.Close();
        Check(Near(velocityRecording.Velocity(.01f).x, 4f) && Near(velocityRecording.Velocity(.03f).x, 0f),
            "display velocity cannot distinguish playback from endpoint hold");

        foreach (Vector3 direction in new[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f) })
        {
            var blend = new MobBridgeTakeover(0d, .2f, new Vector3(), Quaternion.identity,
                new Vector3(), Quaternion.identity, direction * 4f, direction * 2f);
            blend.AdvancePosition(direction * .0004f, .0001d, direction * 4f);
            Check((blend.Position / .0001f - direction * 2f).magnitude < .005f,
                "zero-residual takeover immediately doubles displayed speed");
            for (int i = 1; i <= 20; ++i)
                blend.AdvancePosition(direction * (i * .04f), i * .01d, direction * 4f);
            Check(blend.Settled(.2d, direction * .8f), "moving correction cannot retire on current frame; position=" +
                blend.Position.x + "," + blend.Position.y + "," + blend.Position.z + "; root=" +
                (direction * .8f).x + "," + (direction * .8f).y + "," + (direction * .8f).z + "; velocity=" +
                blend.DisplayedVelocity.x + "," + blend.DisplayedVelocity.y + "," + blend.DisplayedVelocity.z);
            blend.AdvancePosition(direction * .84f, .21d, direction * 4f);
            Check((blend.Position - direction * .84f).magnitude < .000001f, "completed blend misses native trajectory");

            var correction = new MobBridgeTakeover(0d, .1f, direction, Quaternion.identity,
                new Vector3(), Quaternion.identity, direction * 2f, direction);
            correction.AdvancePosition(direction * -.1f, .02d, direction * 2f);
            Check(Vector3.Dot(correction.Position - direction, direction) >= -.000001f,
                "replica backward correction overrides authoritative movement direction");
        }
        var rootHistory = new MobBridgeAnimationHistory(1);
        rootHistory.Add(0f, new[] { new Vector3(1f, 0f, 0f) }, new[] { Quaternion.identity }, new[] { new Vector3(1f, 1f, 1f) });
        rootHistory.Add(.1f, new[] { new Vector3(2f, 0f, 0f) }, new[] { Quaternion.AngleAxis(40f, Vector3.up) }, new[] { new Vector3(1f, 1f, 1f) });
        rootHistory.Close();
        Vector3 model; Quaternion tilt;
        rootHistory.SampleRoot(.05f, out model, out tilt);
        Check(Near(model.x, 1.5f) && Math.Abs(Quaternion.Angle(tilt, Quaternion.identity) - 20f) < .001f,
            "model-local pose is frozen instead of read from immutable recording");
        Check(Near(MobBridgePresentationPoint.ModelPosition(new Vector3(), Quaternion.identity,
            new Vector3(1f, 0f, 0f), new Vector3(2f, 2f, 2f)).x, 2f), "model-local translation ignores actor scale");
    }

    private struct ReferencePose
    { internal float Time; internal Vector3 Position; internal Quaternion Rotation; }
    private static void PoseLookupChecks()
    {
        var history = new MobBridgePoseHistory();
        var reference = new System.Collections.Generic.List<ReferencePose>();
        float time = 0f;
        for (int step = 0; step < 190; ++step)
        {
            time += step % 11 == 0 ? 0f : step % 13 == 0 ? .000005f : .02f;
            var pose = new ReferencePose { Time = time, Position = new Vector3(step, step * .25f, -step),
                Rotation = Quaternion.AngleAxis(step * 7f, Vector3.up) };
            history.Add(time, pose.Position, pose.Rotation); reference.Add(pose);
            if (reference.Count > 64) reference.RemoveAt(0);
            float first = reference[0].Time;
            foreach (float sample in new[] { -1f, first, first + .0000025f, time, time + .1f,
                first + (time - first) * .13f, first + (time - first) * .5f, first + (time - first) * .91f })
            {
                ReferencePose expected = reference[reference.Count - 1];
                if (reference.Count == 1 || sample <= first) expected = reference[0];
                else
                    for (int i = 1; i < reference.Count; ++i)
                    {
                        if (sample > reference[i].Time) continue;
                        ReferencePose a = reference[i - 1], b = reference[i];
                        float span = b.Time - a.Time;
                        float blend = span <= .00001f ? 1f : Math.Max(0f, Math.Min(1f, (sample - a.Time) / span));
                        expected.Position = a.Position * (1f - blend) + b.Position * blend;
                        expected.Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, blend);
                        break;
                    }
                Vector3 actual; Quaternion rotation;
                history.Sample(sample, out actual, out rotation);
                Check(actual.Equals(expected.Position) && rotation.Equals(expected.Rotation),
                    "binary pose lookup differs from former linear interpolation at wrap/duplicate/tiny interval");
            }
        }
    }

    private static void RecordingChecks()
    {
        foreach (float latency in new[] { .02f, .05f, .1f, .5f, 1f })
        foreach (double frame in new[] { .004d, .016d, .033d, .09d })
        {
            var recording = new MobBridgeRecording(0d, latency, 1f);
            var clock = new MobBridgeCaptureClock(.02f);
            recording.Add(0f, new Vector3(), Quaternion.identity);
            int motors = 0, decisions = 0, tails = 0;
            float integrated = 0f, previous = -1f;
            for (double wall = frame; wall < latency + frame * 2d; wall += frame)
            {
                if (recording.Closed) break;
                float horizon = recording.Horizon(wall);
                float time, dt; bool decision, controller;
                while (clock.Take(horizon, wall >= recording.Deadline, .05f, out time, out dt, out decision, out controller))
                {
                    Check(time >= previous - .000001f, "native events ran out of chronological order");
                    previous = time;
                    if (decision) { decisions++; continue; }
                    if (controller) { motors++; Check(Near(dt, .02f), "native acceleration used render-frame delta"); }
                    else { tails++; Check(dt > 0f && dt < .02f, "invalid final physics tail"); }
                    integrated += dt;
                    recording.Add(time, new Vector3(time * 2f, 0, 0), Quaternion.identity);
                }
                if (wall >= recording.Deadline) recording.Close();
            }
            Check(recording.Closed && Near(recording.End, latency) && Near(integrated, latency), "latency window lost final movement time");
            Check(motors == (int)Math.Floor((latency + .000001f) / .02f) && tails <= 1 && decisions > 0,
                "render cadence changed native motor count");
            Vector3 pose; Quaternion rotation;
            recording.Sample(latency * .5f, out pose, out rotation);
            Check(Near(pose.x, latency), "half-speed playback does not interpolate native recording");
            recording.Sample(latency, out pose, out rotation);
            Check(Near(pose.x, latency * 2f), "sealed recording lost its final pose");
            bool rejected = false;
            try { recording.Add(latency + 1f, new Vector3(), Quaternion.identity); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !recording.AcceptsInput(latency * .5d), "sealed recording accepted new states or target input");
        }
        var longRecording = new MobBridgeRecording(10d, 2f, 1f);
        for (int i = 0; i <= 200; ++i) longRecording.Add(i * .01f, new Vector3(i, 0, 0), Quaternion.identity);
        longRecording.Close();
        Vector3 first; Quaternion yaw;
        longRecording.Sample(.015f, out first, out yaw);
        Check(Near(first.x, 1.5f), "ahead producer overwrote early playback after 64 poses");
        var inputs = new MobBridgeRecording(5d, .1f, .8f);
        Check(inputs.AcceptsInput(5.09d) && !inputs.AcceptsInput(5.11d) && Near(inputs.Horizon(6d), .08f),
            "live capture reads beyond deadline or ignores cached game rate");
        var attack = new MobBridgeRecording(0d, .1f, 1f);
        attack.Add(0f, new Vector3(), Quaternion.identity);
        attack.Add(.04f, new Vector3(.04f, 0, 0), Quaternion.identity);
        attack.Add(.05f, new Vector3(.04f, 0, 0), Quaternion.identity); attack.Close();
        attack.Sample(.02f, out first, out yaw);
        Check(Near(first.x, .02f) && Near(attack.End, .05f), "ahead attack decision jumped replay to barrier pose");
    }

    private static void AnimationChecks()
    {
        var history = new MobBridgeAnimationHistory(1);
        Vector3[] pose = { new Vector3() }, scale = { new Vector3(1, 1, 1) };
        Quaternion[] rotation = { Quaternion.identity };
        history.Add(0f, pose, rotation, scale);
        pose[0] = new Vector3(1, 0, 0); rotation[0] = Quaternion.AngleAxis(90f, Vector3.up);
        history.Add(.1f, pose, rotation, scale); pose[0] = new Vector3(99, 0, 0);
        history.Close(); history.Sample(.05f, pose, rotation, scale);
        Check(Near(pose[0].x, .5f) && Math.Abs(Quaternion.Angle(rotation[0], Quaternion.identity) - 45f) < .001f,
            "slowed attack pose does not interpolate recorded bones or retained mutable input arrays");
        history.Sample(5f, pose, rotation, scale);
        Check(Near(pose[0].x, 1f), "sealed attack pose advances past immutable endpoint");
        bool rejected = false;
        try { history.Add(.2f, pose, rotation, scale); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && Near(history.End, .1f), "destination update can rewrite sealed attack pose recording");
        var attackHistory = new MobBridgeAnimationHistory(1);
        attackHistory.Add(0f, pose, rotation, scale);
        attackHistory.Add(.02f, pose, rotation, scale, true);
        attackHistory.Add(.04f, pose, rotation, scale, true);
        attackHistory.Add(.06f, pose, rotation, scale);
        attackHistory.Close();
        Check(Near(attackHistory.AttackWeight(0f), 0f) && Near(attackHistory.AttackWeight(.01f), .5f) &&
            Near(attackHistory.AttackWeight(.03f), 1f) && Near(attackHistory.AttackWeight(.05f), .5f) &&
            Near(attackHistory.AttackWeight(1f), 0f), "visual attack contribution does not interpolate entry/exit metadata");
        foreach (float latency in new[] { .02f, .05f, .1f, .5f, 1f })
        {
            for (int frame = 0; frame <= 150; ++frame)
            {
                float sampleTime = latency * frame / 100f;
                float native = MobBridgeAnimationBlend.NativeWeight(sampleTime * 2f, sampleTime, latency, 0f, 0f);
                Check(Near(native, 1f), "ordinary locomotion replaced native skeleton at fake entry/middle/endpoint");
                // A live native pose progresses even after the immutable fake
                // pose reaches its end. It must pass through unchanged.
                float nativePose = frame * .01f;
                float shown = 99f * (1f - native) + nativePose * native;
                Check(Near(shown, nativePose), "locomotion playback freezes or changes the native pose phase");
            }
            Check(Near(MobBridgeAnimationBlend.NativeWeight(0f, 0f, latency, 0f, 1f), 1f),
                "native-to-fake entry replaces native pose immediately");
            Check(Near(MobBridgeAnimationBlend.NativeWeight(latency, latency * .5f, latency, 0f, 1f), 0f),
                "middle of recording lost visual fake attack pose");
            float previousNative = 0f;
            for (int frame = 0; frame <= 100; ++frame)
            {
                float consumed = latency * (.75f + frame * .0025f);
                float native = MobBridgeAnimationBlend.NativeWeight(consumed * 2f, consumed, latency, 0f, 1f);
                Check(native >= previousNative - .000001f && native <= 1f,
                    "buffer exit native blend reverses or overshoots");
                previousNative = native;
            }
            Check(Near(previousNative, 1f), "sealed endpoint still contributes frozen bones");
            for (int frame = 1; frame <= 10; ++frame)
            {
                float native = MobBridgeAnimationBlend.NativeWeight(latency * 2f + frame * .01f, latency, latency, 0f, 1f);
                Check(Near(native, 1f), "waiting for destination reintroduced frozen endpoint bones");
                Check(Near(MobBridgeAnimationBlend.NativeWeight(latency * 3f, latency * 1.5f, latency, frame * .1f, 1f), 1f),
                    "final takeover blend brought back the exhausted animation pose");
            }
            float entryMid = MobBridgeAnimationBlend.NativeWeight(latency * .25f, latency * .125f, latency, 0f, 1f);
            Check(Near(entryMid, .5f), "native-to-fake entry does not blend gradually");
            Check(Near(MobBridgeAnimationBlend.NativeWeight(latency, latency * .5f, latency, .5f, 1f), .5f),
                "early takeover ignores native animation correction weight");
            var capture = new MobBridgeCaptureClock(.02f);
            var movement = new MobBridgeRecording(0d, latency, 1f);
            movement.Add(0f, new Vector3(), Quaternion.identity);
            int attacks = 0, motorTicks = 0; float time, dt; bool decision, controller;
            while (capture.Take(latency, true, .05f, out time, out dt, out decision, out controller))
            {
                if (decision) { if (attacks == 0) attacks++; continue; }
                if (controller) motorTicks++;
                movement.Add(time, new Vector3(time, 0, 0), Quaternion.identity);
            }
            movement.Close();
            Check(attacks == 1 && motorTicks > 0 && Near(movement.End, latency), "zero-time visual attack stopped capture before latency endpoint");
            var blend = new MobBridgeTakeover(10d, latency * .5f, new Vector3(), Quaternion.identity, new Vector3(), Quaternion.identity);
            float previous = .1f, previousWeight = 0f;
            for (int i = 0; i <= 100; ++i)
            {
                double now = 10d + latency * .5d * i / 100d;
                float sample = blend.AnimationTime(.1f, now), weight = blend.AnimationWeight(now);
                Check(sample >= previous && weight >= previousWeight && weight <= 1f, "attack handoff rewinds pose time or blend weight");
                if (i == 0) Check(Near(sample, .1f) && Near(weight, 0f), "mid-attack takeover restarts clip instead of preserving its phase");
                previous = sample; previousWeight = weight;
            }
            Check(Near(previous, .1f + latency * .375f) && Near(previousWeight, 1f), "animation speed ramp does not integrate from 0.5x toward 1x");
        }
    }

    private static void Run()
    {
        var clock = new MobBridgeTimeline(10d, .1f);
        Check(Near(clock.FakeTime(10d), 0f), "fake starts at zero");
        Check(Near(clock.FakeTime(10.05d), .025f), "fake continues at 0.5x");
        Check(Near(clock.FakeTime(10.1d), .05f), "fake reaches 50ms at first real state");
        clock.ObserveReal(10.1d);
        Check(Near(clock.RealTime(10.1d), 0f), "real starts at zero");
        Check(Near(clock.RealStartOffset(10.1d), .1f), "native prediction clock uses measured real start");
        Check(Near(clock.RealWeight(10.1d), 0f), "blend starts at zero");
        Check(Near(clock.FakeTime(10.15d), .075f), "fake never holds");
        Check(Near(clock.RealTime(10.15d), .05f), "real catches continuously");
        Check(Near(clock.RealWeight(10.15d), .5f), "halfway blend weight");
        Check(Near(clock.FakeTime(10.2d), .1f), "fake reaches common horizon");
        Check(Near(clock.RealTime(10.2d), .1f), "real reaches common horizon");
        Check(clock.Complete(10.2d) && Near(clock.RealWeight(10.2d), 1f), "common takeover at 2L");
        clock.ObserveReal(10.05d);
        Check(Near(clock.RealTime(10.2d), .1f), "late/duplicate real epoch cannot rewind");
        Check(Near(clock.FakeTime(10.09d), .1f), "out-of-order query cannot rewind fake progress");

        var longBridge = new MobBridgeTimeline(15d, .8f);
        for (int i = 1; i <= 14; ++i) longBridge.FakeTime(15d + i * .1d);
        Check(Near(longBridge.Latency, .8f) && Near(longBridge.FakeTime(16.4d), .7f),
            "measured latency beyond 500ms controls fake duration");
        var worseningLatency = new MobBridgeTimeline(16.5d, .1f);
        Check(Near(worseningLatency.FakeTime(16.6d), .05f) &&
            Near(worseningLatency.FakeTime(16.8d), .15f),
            "fake simulation froze at old horizon when real arrival became later than estimated");
        var cached = new MobBridgeTimeline(17d, .1f, .5f);
        Check(Near(cached.PlaybackRate, .25f) && Near(cached.FakeTime(17.1d), .025f),
            "pre-handoff rate cache scales fake from its first frame");

        var late = new MobBridgeTimeline(20d, .5f);
        late.ObserveReal(20.15d);
        float lateFake = late.FakeTime(20.15d), lateReal = late.RealTime(20.15d);
        Check(Near(lateFake, .075f), "late arrival preserves continuous fake progress");
        Check(!late.Complete(20.15d),
            "late arrival keeps real frontier behind fake; fake=" + lateFake + " real=" + lateReal);
        late.ObserveRealRate(.5f, .05f);
        Check(Near(late.PlaybackRate, .5f), "owner-rate sample changes playback speed discontinuously");
        float slowed = late.FakeTime(20.35d);
        float expectedSlowed = .075f + .25f * .2f + .25f * .05f * (1f - (float)Math.Exp(-4d));
        Check(Near(slowed, expectedSlowed) && late.PlaybackRate > .25f && late.PlaybackRate < .26f,
            "continuous CPU-rate transition integrates the wrong fake horizon; fake=" + slowed);
        Check(late.FakeTime(20.4d) >= late.FakeTime(20.35d), "dynamic rate keeps monotone fake time");

        foreach (float period in new[] { .02f, .05f, .113f, .4f })
        foreach (float targetRate in new[] { .2f, .8f, 1.5f })
        {
            var coarse = new MobBridgeTimeline(0d, 1f, 1f, true);
            var fine = new MobBridgeTimeline(0d, 1f, 1f, true);
            coarse.ObserveRealRate(targetRate, period); fine.ObserveRealRate(targetRate, period);
            coarse.AdvanceFrame(0d, 0f); fine.AdvanceFrame(0d, 0f);
            coarse.AdvanceFrame(.2d, .2f);
            float previousTime = 0f, previousRate = fine.PlaybackRate;
            for (int frame = 1; frame <= 20; ++frame)
            {
                fine.MatchHorizon(frame * .01d, 0f, .01f);
                Check(Near(fine.PlaybackRate, previousRate), "horizon matching bypasses rate smoothing");
                fine.AdvanceFrame(frame * .01d, .01f);
                float current = fine.FakeTime(frame * .01d);
                Check(current > previousTime && fine.PlaybackRate > 0f,
                    "rate transition stopped or reversed stretched playback");
                previousTime = current; previousRate = fine.PlaybackRate;
            }
            Check(Near(coarse.FakeTime(.2d), fine.FakeTime(.2d)) && Near(coarse.PlaybackRate, fine.PlaybackRate),
                "rate transition depends on render frame subdivision");
            float unchanged = fine.PlaybackRate;
            fine.ObserveRealRate(.1f, Single.NaN); fine.ObserveRealRate(.1f, 0f);
            Check(Near(fine.PlaybackRate, unchanged), "invalid owner sample spacing changed playback rate");
        }

        var rate = new MobBridgeRateTracker();
        Check(rate.Observe(0f, 0f) && rate.Observe(.4f, .5f) && rate.Observe(.7f, 1f), "rate samples accepted");
        Check(rate.Estimate > .7f && rate.Estimate < 1f, "CPU slowdown estimate is bounded and smoothed");
        Check(!rate.Observe(.6f, 1.1f), "simulation-time rollback rejected");
        var cachedRate = new MobBridgeRateTracker(.5f);
        Check(Near(cachedRate.Estimate, .5f), "cached destination rate seeds extrapolation immediately");
        Check(Near(cachedRate.Frontier(.1f, 10d, 10.2d, 1f), .2f),
            "destination frontier extrapolates from delayed sample without wall-time rewind");

        var delayed = new MobBridgeTimeline(40d, .1f);
        delayed.ObserveReal(40.35d, true);
        Check(Near(delayed.RealTime(40.45d), .1f) && Near(delayed.RealStartOffset(40.45d), .35f),
            "clock-loss fallback starts only after grace period");
        delayed.ObserveReal(40.4d, false);
        Check(Near(delayed.RealTime(40.45d), .05f), "late authoritative clock replaces earlier fallback");

        Check(Near(MobBridgeTakeover.SettleForLatency(.1f), .05f), "200ms stretch uses 50ms final blend");
        Check(Near(MobBridgeTakeover.SettleForLatency(.5f), .25f), "1000ms stretch uses 250ms final blend");
        foreach (float stretch in new[] { .04f, .1f, .2f, .4f, 1f, 2f })
            Check(Near(MobBridgeTakeover.SettleForPlayback(stretch, 0f, 0f, 5f, 300f), stretch * .5f),
                "final correction base is not half the actual stretched playback duration");
        Check(Near(MobBridgeTakeover.SettleForPlayback(.2f, 5f, 0f, 5f, 300f), 1.5f),
            "half-playback timing removed extension for a large positional residual");
        Check(Near(MobBridgeTakeover.SettleForPlayback(.2f, 0f, 120f, 5f, 60f), 3f),
            "half-playback timing removed extension for a large rotation residual");
        Check(Near(MobBridgeTakeover.SettleForPlaybackCapped(.2f, 5f, 120f, 5f, 60f), .2f),
            "large residual did not receive the second simulation-horizon correction window");
        Check(Near(MobBridgeTakeover.SettleForPlaybackCapped(.02f, 0f, 0f, 8f, 300f, 0f, .033f), .033f),
            "sub-frame smoothing was not extended to one render frame");
        Check(Near(MobBridgeTakeover.SettleForPlaybackCapped(.04f, 0f, 0f, 8f, 300f, .05f, .008f), .04f),
            "owner-ready floor ignored or exceeded the two-horizon cap");
        Check(Near(MobBridgeTakeover.SettleForPlaybackCapped(.4f, 1f, 0f, 1f, 300f, .05f, .008f), .4f),
            "large positional correction was truncated before the new cap");
        Check(Near(MobBridgeTakeover.SettleForPlaybackCapped(2f, 5f, 720f, 1f, 30f, .05f, .008f), .5f),
            "smoothing exceeded the 500ms hard cap");
        Check(Near(MobBridgeTakeover.SettleForPose(.5f, 5f, 0f, 5f, 300f, 5d), 1.5f),
            "large residual settles no faster than mob movement speed");
        Check(Near(MobBridgeTakeover.SettleForPose(.1f, .1f, 0f, 5f, 300f, 5d), .05f),
            "small residual keeps quarter-stretch settle");
        float rateDuration = MobBridgeTakeover.SettleForPose(.5f, 5f, 0f, 5f, 300f, 5d);
        var rateTakeover = new MobBridgeTakeover(40d, rateDuration, new Vector3(6f, 0f, 0f),
            Quaternion.identity, new Vector3(1f, 0f, 0f), Quaternion.identity);
        float lastCorrectionX = 6f;
        for (int i = 1; i <= 300; ++i)
        {
            double at = 40d + i * .005d;
            float x = StepPosition(rateTakeover, new Vector3(1f, 0f, 0f), at).x;
            Check(Math.Abs(x - lastCorrectionX) <= 5f * .005f + .0001f,
                "residual correction never exceeds configured mob movement speed");
            lastCorrectionX = x;
        }

        var history = new MobBridgePoseHistory();
        history.Add(0f, new Vector3(0f, 0f, 0f), Quaternion.identity);
        history.Add(.02f, new Vector3(.2f, 0f, 0f), Quaternion.AngleAxis(10f, new Vector3(0f, 1f, 0f)));
        history.Add(.04f, new Vector3(.4f, 0f, 0f), Quaternion.AngleAxis(20f, new Vector3(0f, 1f, 0f)));
        Vector3 pose; Quaternion turn;
        history.Sample(.01f, out pose, out turn);
        Check(Near(pose.x, .1f) && Math.Abs(Quaternion.Angle(Quaternion.identity, turn) - 5f) < .001f,
            "render-time pose interpolation is continuous between fixed steps");
        history.Add(.03f, new Vector3(-1f, 0f, 0f), Quaternion.identity);
        Check(history.Count == 3, "pose history rejects time rollback");
        var rolling = new MobBridgePoseHistory();
        for (int i = 0; i < 100; ++i)
        {
            float at = i * .02f;
            rolling.Add(at, new Vector3(at, 0f, 0f), Quaternion.identity);
        }
        rolling.Sample(1.9f, out pose, out turn);
        Check(rolling.Count == 64 && Near(pose.x, 1.9f),
            "long simulation exceeded fixed pose history and froze at the 64th sample");

        GameObject targetRoot = new GameObject();
        Player local = targetRoot.Add<Player>(); local.Visual = new GameObject(); Player.m_localPlayer = local;
        local.transform.position = new Vector3(0f, 0f, 0f); local.Velocity = new Vector3(10f, 0f, 0f);
        var localTargets = new MobBridgeTargetTimeline(local, 50d, .2f);
        local.transform.position = new Vector3(1f, 0f, 0f);
        localTargets.Observe(50.1d);
        local.transform.position = new Vector3(2f, 0f, 0f);
        localTargets.Observe(50.2d);
        Vector3 lead, targetVelocity; Quaternion targetRotation;
        localTargets.Prepare(.1f, out lead, out targetVelocity, out targetRotation);
        Check(Near(local.transform.position.x + lead.x, 1f), "local test prediction must advance the cutoff pose by 100ms, not add 100ms to an already current 200ms pose");

        Player.m_localPlayer = null; local.transform.position = new Vector3(0f, 0f, 0f);
        local.Velocity = new Vector3(10f, 0f, 0f);
        var remoteTargets = new MobBridgeTargetTimeline(local, 60d, .5f);
        remoteTargets.Observe(60.1d);
        remoteTargets.Prepare(.5f, out lead, out targetVelocity, out targetRotation);
        Check(Near(lead.x, 9f), "remote prediction uses the observed target sample time, not a twice-counted route");

        var slowTarget = new MobBridgeTargetTimeline(local, 65d, .5f, .5f, 1f);
        slowTarget.Observe(65.1d);
        slowTarget.Prepare(.25f, out lead, out targetVelocity, out targetRotation);
        Check(Near(lead.x, 4.5f), "wall RTT was added to game-time prediction without destination rate conversion");

        local.transform.position = new Vector3(0f, 0f, 0f);
        local.Velocity = new Vector3(10f, 0f, 0f);
        local.MoveDir = new Vector3(0f, 0f, -1f);
        Player.m_localPlayer = local;
        var turningTargets = new MobBridgeTargetTimeline(local, 70d, .5f);
        float previousTurnX = 0f, previousTurnZ = 0f;
        for (int i = 1; i <= 12; ++i)
        {
            turningTargets.Observe(70d + i * .02d);
            turningTargets.Prepare(i * .01f, out lead, out targetVelocity, out targetRotation);
            Check((lead - new Vector3(previousTurnX, 0f, previousTurnZ)).magnitude <= .1001f && lead.z <= previousTurnZ + .00001f,
                "fast target reversal predicts a continuous turn from the cutoff; step=" + i +
                "; x=" + lead.x + "; previousX=" + previousTurnX + "; z=" + lead.z +
                "; previousZ=" + previousTurnZ);
            Check(lead.x > 0f && lead.z < 0f,
                "fast target reversal blends old velocity and new move intent");
            previousTurnX = lead.x;
            previousTurnZ = lead.z;
        }
        Check(targetVelocity.z < -2f && targetVelocity.x < 9f,
            "fast reversal reaches the new target direction without snapping");
        local.Velocity = new Vector3(10f, -8f, 0f);
        var fallingTargets = new MobBridgeTargetTimeline(local, 80d, .5f);
        for (int i = 1; i <= 20; ++i) fallingTargets.Observe(80d + i * .05d);
        fallingTargets.Prepare(.4f, out lead, out targetVelocity, out targetRotation);
        Check(Near(lead.y, -3.2f) && targetVelocity.z < -8f && Near(targetVelocity.y, -8f),
            "target direction blending preserves vertical velocity instead of rotating it");
        float previousForward = Single.PositiveInfinity;
        for (int i = 0; i <= 40; ++i)
        {
            turningTargets.Prepare(i * .01f, out lead, out targetVelocity, out targetRotation);
            Check(lead.z <= previousForward + .00001f,
                "predicted target path advances monotonically after direction change");
            previousForward = lead.z;
        }
        Player.m_localPlayer = null;

        var takeover = new MobBridgeTakeover(30d, .1f, new Vector3(6f, 0f, 0f),
            Quaternion.AngleAxis(40f, new Vector3(0f, 1f, 0f)), new Vector3(5f, 0f, 0f),
            Quaternion.identity);
        Check(Near(StepPosition(takeover, new Vector3(5f, 0f, 0f), 30d).x, 6f), "takeover starts with displayed fake pose");
        Check(Near(StepPosition(takeover, new Vector3(5f, 0f, 0f), 30.05d).x, 5.5f), "position blend uses monotone smoothstep");
        Check(Math.Abs(Quaternion.Angle(takeover.Rotation(Quaternion.identity, 30.05d), Quaternion.identity) - 20f) < .001f,
            "rotation blend uses same monotone curve");
        Check(takeover.Active(30.05d) && !takeover.Finished(30.05d), "takeover remains active");
        Check(Near(StepPosition(takeover, new Vector3(5f, 0f, 0f), 30.1d).x, 5f) &&
            Quaternion.Angle(takeover.Rotation(Quaternion.identity, 30.1d), Quaternion.identity) < .001f,
            "takeover ends exactly on real pose");
        Check(takeover.Finished(30.1d), "takeover retires at finite endpoint");

        float previousX = Single.PositiveInfinity, previousYaw = Single.PositiveInfinity;
        for (int i = 0; i <= 20; ++i)
        {
            double now = 31d + i * .005d;
            float x = StepPosition(takeover, new Vector3(2f, 0f, 0f), now).x;
            float yaw = Quaternion.Angle(takeover.Rotation(Quaternion.identity, now), Quaternion.identity);
            Check(x <= previousX + .000001f && yaw <= previousYaw + .000001f,
                "takeover correction is monotone and never reverses");
            previousX = x;
            previousYaw = yaw;
        }
        Check(Near(previousX, 2f) && Near(previousYaw, 0f), "second duplicate interval settles to real");

        var movingTakeover = new MobBridgeTakeover(35d, .1f, new Vector3(6f, 0f, 0f),
            Quaternion.identity, new Vector3(5f, 0f, 0f), Quaternion.identity);
        previousX = Single.NegativeInfinity;
        for (int i = 0; i <= 20; ++i)
        {
            double now = 35d + i * .005d;
            float realX = 5f + (float)(now - 35d) * 10f;
            float x = StepPosition(movingTakeover, new Vector3(realX, 0f, 0f), now).x;
            Check(x >= previousX - .000001f,
                "relative takeover residual follows moving real root without reversal; step=" + i +
                "; x=" + x + "; previous=" + previousX + "; real=" + realX);
            previousX = x;
        }
        Check(!movingTakeover.Settled(35.1d, new Vector3(5.9f, 0f, 0f)),
            "takeover waits when real root has not caught displayed pose");
        float caughtX = StepPosition(movingTakeover, new Vector3(6.2f, 0f, 0f), 35.2d).x;
        Check(caughtX >= previousX && movingTakeover.Settled(35.2d, new Vector3(6.2f, 0f, 0f)),
            "takeover settles after real root catches without backward jump");

        var attackGate = new MobBridgeAttackGate(90d, .2f);
        attackGate.Capture(.075f, 3);
        Check(attackGate.Held && Near(attackGate.Simulation, .075f), "attack decision freezes fake frontier");
        Check(!attackGate.Released(.07f, 90.1d, true, .07f, 3), "attack barrier waits for matching destination simulation");
        Check(attackGate.Released(.075f, 90.1d, true, .075f, 3) && !attackGate.Released(.15f, 90.2d, true, .075f, 4),
            "attack release occurs once at the matching simulation point");
        var attackFallback = new MobBridgeAttackGate(100d, .1f);
        attackFallback.Capture(.05f, 2);
        Check(!attackFallback.Released(0f, 100.5d, true, -1f, 1), "attack barrier keeps waiting before fallback");
        Check(!attackFallback.Released(0f, 100.96d, true, -1f, 1), "elapsed time cannot release unmatched attack barrier");
        Check(attackFallback.Released(.1f, 101d, true, .1f, 3), "late observed frontier releases held attack");
        var zeroAttack = new MobBridgeAttackGate(102d, 1f);
        zeroAttack.Capture(0f, 1);
        Check(!zeroAttack.Released(0f, 102.08d, false, -1f, 0) && zeroAttack.Held,
            "unobserved default zero released zero-time attack before destination clock");
        Check(!zeroAttack.Released(0f, 105d, false, -1f, 0),
            "missing destination clock released attack after wall-time fallback");
        Check(zeroAttack.Released(0f, 105.1d, true, 0f, 1),
            "first actual clock releases zero-time attack");

        var passBarrier = new MobBridgeAttackGate(105d, .5f);
        passBarrier.Capture(.4f, 9);
        Check(!passBarrier.Released(.4f, 106d, true, .4f, 8), "AI pass-start barrier released before matching pass completed");
        Check(!passBarrier.Released(.4f, 106d, true, -1f, 9), "clock-only marker released an attack that destination did not start");
        Check(passBarrier.Released(.4f, 106d, true, .4f, 9), "completed matching native attack pass did not release barrier");

        var gameRate = new MobBridgeRateTracker(1f);
        var aiRate = new MobBridgeRateTracker(.625f);
        Check(gameRate.Observe(.08f, .08f) && gameRate.Observe(.16f, .16f) && Near(gameRate.Estimate, 1f),
            "15fps AI cadence incorrectly slows the body's game-time clock");
        Check(aiRate.Observe(.05f, .08f) && aiRate.Observe(.1f, .16f) && Near(aiRate.Estimate, .625f),
            "native AI rate was not kept separate from physics/game progress");
        var cpuAttack = new MobBridgeAttackGate(106d, 1f);
        cpuAttack.Capture(.64f, 9);
        Check(!cpuAttack.Released(.64f, 107d, true, .64f, 8) &&
            cpuAttack.Released(.64f, 107d, true, .64f, 9),
            "slow CPU attack release confused physical elapsed time with AI pass count");
        var paired = new MobBridgeFrame();
        Vector3 firstPose = new Vector3(10f, 2f, 3f), secondPose = new Vector3(11f, 2f, 3f);
        Check(paired.Observe(1, .05f, 0f, firstPose, Quaternion.identity, new Vector3(2f, 0f, 0f), -1f, ""),
            "first paired owner clock/pose rejected");
        Check(!paired.Observe(1, .05f, 0f, secondPose, Quaternion.identity, new Vector3(), -1f, "") && Near(paired.Position.x, 10f),
            "duplicate clock rewrote authoritative pose");
        Check(paired.Observe(2, .1f, .05f, secondPose, Quaternion.identity, new Vector3(2f, 0f, 0f), .1f, "attack"),
            "paired attack event rejected");
        Check(!paired.Observe(1, .05f, 0f, firstPose, Quaternion.identity, new Vector3(), -1f, "") && Near(paired.AttackTime, .1f),
            "old packet rewound attack marker or observation freshness");
        var liveAttack = new MobBridgeFrame();
        Check(liveAttack.Observe(1, .05f, 0f, firstPose, Quaternion.identity, new Vector3(), .05f, "bite",
                .05f, true) && liveAttack.AttackActive,
            "owner attack-active state was not paired with the clock frame");
        Check(liveAttack.Observe(1, .05f, 0f, firstPose, Quaternion.identity, new Vector3(), .05f, "bite",
                .05f, false) && !liveAttack.AttackActive,
            "owner attack-end state was discarded as a duplicate clock frame");
        Check(!paired.Observe(3, .15f, .1f, new Vector3(float.NaN, 0f, 0f), Quaternion.identity, new Vector3(), -1f, ""),
            "invalid owner position accepted with a valid clock");
        Check(paired.Observe(4, 2.2f, 2.1f, secondPose, Quaternion.identity, new Vector3(), .1f, "attack"),
            "dynamic long bridge clock was rejected after two wall seconds");
        var cpuFrame = new MobBridgeFrame();
        Check(cpuFrame.Observe(2, .16f, .16f, firstPose, Quaternion.identity, new Vector3(), -1f, "", .1f) &&
            Near(cpuFrame.Simulation, .16f) && Near(cpuFrame.AiSimulation, .1f),
            "paired owner packet merges AI elapsed dt with position/game-time horizon");
        paired.History.Sample(.075f, out pose, out turn);
        Check(Near(pose.x, 10.5f), "destination history did not keep poses on their matching simulation frontier");
        Check(Near(MobBridgeTakeover.SettleForPose(.1f, 5f, 0f, 5f, 300f, .01d), 1.5f),
            "obsolete lifetime budget truncated geometric correction");
        var lowLatency = new MobBridgeTimeline(200d, .02f);
        Check(Near(lowLatency.Latency, .02f), "20ms latency was silently stretched to a 40ms minimum");
        Check(Near(lowLatency.FakeTime(202d), 1f), "standalone wall clock discarded its elapsed simulation time");
        var renderClock = new MobBridgeTimeline(300d, .1f, 1f, true);
        renderClock.AdvanceFrame(300.1d, .1f);
        Check(Near(renderClock.FakeTime(310d), .05f), "camera/HUD query consumed an unrendered wall-time gap");
        renderClock.AdvanceFrame(310d, .02f);
        Check(Near(renderClock.FakeTime(310d), .06f), "resuming after local hitch burst through fake movement backlog");
        var phaseClock = new MobBridgeTimeline(400d, .05f, 1f, true);
        phaseClock.AdvanceFrame(400.005d, .016f);
        Check(Near(phaseClock.FakeTime(400.005d), .0025f), "first frame included movement before handoff cutoff");
        double phaseStart = 400.005d;
        for (int i = 1; i <= 120; ++i)
        {
            // Frame starts stay 16ms apart; HUD/plugin LateUpdate switches
            // between an early and a late callback phase within those frames.
            double callback = phaseStart + i * .016d + (i % 2 == 0 ? 0d : .006d);
            float before = phaseClock.FakeTime(callback);
            phaseClock.AdvanceFrame(callback, .016f);
            Check(Near(phaseClock.FakeTime(callback) - before, .008f),
                "callback phase jitter changed fake speed despite a steady native frame delta");
        }
        Check(Math.Abs(phaseClock.FakeTime(403d) - (.0025f + 120f * .008f)) < .00001f,
            "callback phase jitter permanently lost fake simulation time");
        Check(Near(MobBridgeTakeover.SettleForLatency(.02f), .01f) && Near(MobBridgeTakeover.SettleForLatency(2f), 1f),
            "quarter-stretch correction length is clamped instead of following actual latency");

        // Accepted clock/velocity can precede a new replica-root pose. The
        // very first correction frames need the same direction protection
        // as later repeated roots; no observed root delta exists yet.
        foreach (Vector3 direction in new[] { new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f),
            new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f) })
        {
            var initialHold = new MobBridgeTakeover(105d, .1f, direction, Quaternion.identity,
                new Vector3(), Quaternion.identity, direction * 2f);
            for (int frame = 1; frame <= 4; ++frame)
            {
                initialHold.AdvancePosition(new Vector3(), 105d + frame * .02d);
                Check((initialHold.Position - direction).sqrMagnitude < .0000001f,
                    "first unchanged replica root pulled forward-moving preview backwards before direction initialization");
            }
            initialHold.AdvancePosition(direction * 1.1f, 105.12d);
            Check(initialHold.Settled(105.12d, direction * 1.1f),
                "seeded movement direction prevented correction from retiring when real root caught up");
        }
        var noInitialMotion = new MobBridgeTakeover(105d, .1f, new Vector3(1f, 0f, 0f),
            Quaternion.identity, new Vector3(), Quaternion.identity, new Vector3());
        noInitialMotion.AdvancePosition(new Vector3(), 105.02d);
        Check(Near(noInitialMotion.Position.x, .896f),
            "zero observed velocity invented a movement direction instead of preserving stationary pose correction");

        var repeatedPose = new MobBridgeTakeover(106d, .1f, new Vector3(6f, 0f, 0f),
            Quaternion.identity, new Vector3(5f, 0f, 0f), Quaternion.identity);
        repeatedPose.AdvancePosition(new Vector3(5.1f, 0f, 0f), 106.02d);
        float displayed = repeatedPose.Position.x;
        for (int i = 0; i < 10; ++i)
        {
            Check(Near(repeatedPose.Position.x, displayed),
                "HUD/hitbox/camera read changed cached presentation pose");
            repeatedPose.Rotation(Quaternion.identity, 106.03d + i * .001d);
        }
        repeatedPose.AdvancePosition(new Vector3(5.1f, 0f, 0f), 106.06d);
        Check(repeatedPose.Position.x >= displayed - .00001f,
            "unchanged replica root bypassed established movement direction");
        displayed = repeatedPose.Position.x;
        repeatedPose.AdvancePosition(new Vector3(5.1f, 0f, 0f), 106.06d);
        Check(Near(repeatedPose.Position.x, displayed), "duplicate update consumed correction twice");
        repeatedPose.AdvancePosition(new Vector3(0f, 0f, 0f), 106.01d);
        Check(Near(repeatedPose.Position.x, displayed), "stale pose update rewound presentation");
        repeatedPose.AdvancePosition(new Vector3(6.3f, 0f, 0f), 106.2d);
        Check(Near(repeatedPose.Position.x, 6.3f) &&
            repeatedPose.Settled(106.2d, new Vector3(6.3f, 0f, 0f)),
            "real root catch-up did not retire held correction");

        Collider nativeHit = new Collider(), fakeHit = new Collider();
        Check(MobBridgeHitFilter.Accepts(new HitData { m_hitCollider = fakeHit }, nativeHit, fakeHit),
            "player hit on displayed fake pose was rejected");
        Check(!MobBridgeHitFilter.Accepts(new HitData { m_hitCollider = nativeHit }, nativeHit, fakeHit),
            "hidden replicated root hit bypassed fake-pose routing");
        Check(MobBridgeHitFilter.Accepts(new HitData(), nativeHit, fakeHit),
            "non-collider native damage was incorrectly suppressed");
        Vector3 ownerNative = new Vector3(1f, 0f, 0f), ownerOffset = new Vector3(.2f, 0f, 0f);
        Check(Near(MobBridgePresentationPoint.OwnerGainPosition(ownerNative, ownerOffset, 0f).x, 1.2f) &&
            Near(MobBridgePresentationPoint.OwnerGainPosition(ownerNative, ownerOffset, 1f).x, 1f),
            "owner-gain visual does not start at replica pose and end at authoritative pose");
        float ownerPrevious = 1.2f;
        for (int i = 0; i <= 20; ++i)
        {
            float x = MobBridgePresentationPoint.OwnerGainPosition(ownerNative, ownerOffset, i / 20f).x;
            Check(x <= ownerPrevious + .000001f, "owner-gain visual moved backwards before native movement");
            ownerPrevious = x;
        }
        Quaternion ownerTurn = Quaternion.AngleAxis(30f, Vector3.up);
        Check(Quaternion.Angle(MobBridgePresentationPoint.OwnerGainRotation(Quaternion.identity, ownerTurn, 0f), ownerTurn) < .001f &&
            Quaternion.Angle(MobBridgePresentationPoint.OwnerGainRotation(Quaternion.identity, ownerTurn, 1f), Quaternion.identity) < .001f,
            "owner-gain rotation does not blend from replica to authoritative facing");
        Vector3 fakeTop = MobBridgePresentationPoint.TopPoint(new Vector3(10f, 2f, 5f),
            Quaternion.identity, Quaternion.identity, new Vector3(0f, 1f, 0f), 2f,
            new Vector3(2f, 3f, 4f));
        Check(Near(fakeTop.x, 10f) && Near(fakeTop.y, 6f) && Near(fakeTop.z, 5f),
            "vanilla HUD top point does not follow scaled fake presentation root");

        float flightTimer = .4f;
        bool flight = false;
        TooManyLags.NativeFlightTransition.Advance(true, .05f, false, flight, 2f, 20f,
            .5f, .3f, 1f, 1f, .5f, ref flightTimer, ref flight);
        Check(flight && Near(flightTimer, 0f), "fake flight transition applies takeoff decision");
        flightTimer = .6f;
        TooManyLags.NativeFlightTransition.Advance(true, .05f, false, flight, 2f, 20f,
            .5f, .3f, 1f, 1f, .5f, ref flightTimer, ref flight);
        Check(!flight && Near(flightTimer, 0f), "fake flight transition applies landing decision");

        var cpuClock = new MobBridgeTimeline(110d, .2f);
        cpuClock.FakeTime(110.1d);
        cpuClock.ObserveReal(110.2d);
        cpuClock.FakeTime(110.3d);
        Check(!cpuClock.CompleteAtDestination(110.3d, .05f),
            "slow destination simulation cannot complete takeover from wall time");
        Check(cpuClock.CompleteAtDestination(110.3d, .15f),
            "takeover completes only when destination simulation reaches fake frontier");
        var improvedLatency = new MobBridgeTimeline(120d, .5f);
        improvedLatency.FakeTime(120.05d);
        improvedLatency.ObserveReal(120.05d);
        Check(!improvedLatency.CompleteAtDestination(120.1d, .04f),
            "early/latency-improved bridge took over from an extrapolated, unobserved frontier");
        Check(improvedLatency.CompleteAtDestination(120.1d, .06f),
            "observed destination frontier did not permit safe takeover");
        var nearHorizon = new MobBridgeTimeline(130d, .5f);
        nearHorizon.ObserveReal(130.1d);
        float heldStart = nearHorizon.FakeTime(130.1d);
        nearHorizon.MatchHorizon(130.1d, heldStart - .04f, .05f);
        float heldEnd = nearHorizon.FakeTime(130.2d);
        Check(Near(nearHorizon.PlaybackRate, .5f) &&
            Near(heldEnd - heldStart, .05f),
            "near-identical fake/real frontiers inserted a visible stop before takeover");
        var staleHorizon = new MobBridgeTimeline(140d, .5f);
        staleHorizon.ObserveReal(140.1d);
        staleHorizon.FakeTime(140.1d);
        staleHorizon.MatchHorizon(140.1d, staleHorizon.FakeTime(140.1d) - .04f, .2f);
        Check(Near(staleHorizon.PlaybackRate, .5f),
            "stale destination sample held fake simulation indefinitely");
        var aheadHorizon = new MobBridgeTimeline(150d, .5f);
        aheadHorizon.ObserveReal(150.1d);
        aheadHorizon.FakeTime(150.1d);
        float aheadFake = aheadHorizon.FakeTime(150.1d);
        aheadHorizon.MatchHorizon(150.1d, aheadFake - .12f, .05f);
        Check(Near(aheadHorizon.PlaybackRate, .5f),
            "large fake lead accelerated instead of letting observed real frontier catch it");

        // Cached 500ms, actual 100ms: destination is already ahead at the first
        // observed sample. Immediate takeover uses that observed horizon.
        var latencyImproved = new MobBridgeTimeline(160d, .5f);
        float improvedFake = latencyImproved.FakeTime(160.1d);
        latencyImproved.ObserveReal(160.1d);
        latencyImproved.MatchHorizon(160.1d, .08f, .05f);
        Check(Near(improvedFake, .05f) && latencyImproved.CompleteAtDestination(160.1d, .08f),
            "500ms->100ms example did not take over immediately at the observed common frontier");

        // Cached 100ms, actual 500ms: fake passes the old horizon continuously.
        // Real catches at simulation rate 1 - fakeRate; observed equality is
        // horizon 0.45s at wall 0.9s in this deterministic example.
        var latencyWorsened = new MobBridgeTimeline(170d, .1f);
        latencyWorsened.ObserveReal(170.5d);
        float worsenedFake = latencyWorsened.FakeTime(170.5d);
        latencyWorsened.MatchHorizon(170.5d, .05f, .05f);
        Check(Near(worsenedFake, .25f) && Near(latencyWorsened.PlaybackRate, .5f),
            "100ms->500ms example froze or accelerated fake at the old horizon");
        Check(!latencyWorsened.CompleteAtDestination(170.7d, .25f),
            "100ms->500ms example took over before observed frontiers matched");
        Check(Near(latencyWorsened.FakeTime(170.9d), .45f) &&
            latencyWorsened.CompleteAtDestination(170.9d, .45f),
            "100ms->500ms example did not take over at common horizon 0.45s");
    }
}
