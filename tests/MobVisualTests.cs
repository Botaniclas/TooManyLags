using System;
using System.Collections;
using System.Reflection;
using TooManyLags;
using UnityEngine;
using FiresGhettoNetworkMod;

internal static class MobVisualTests
{
    private static int checks; private static uint next;
    private static void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
    private static IList Tracks { get { return (IList)typeof(MobHandoffVisual).GetField("tracks",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null); } }
    private static Humanoid Mob()
    {
        GameObject root=new GameObject();Humanoid actor=root.Add<Humanoid>();actor.Visual=new GameObject();
        actor.Visual.transform.parent=root.transform;root.transform.Children.Add(actor.Visual.transform);
        actor.Velocity=new Vector3(2,0,0);actor.Visual.transform.localPosition=new Vector3(0,.2f,0);
        ZNetView view=root.Add<ZNetView>();view.Data=new ZDO{m_uid=new ZDOID{UserID=1,ID=++next}};
        root.Add<ZSyncTransform>();root.Add<BaseAI>();ZDOMan.instance.All.Add(view.Data.m_uid,view.Data);ZNetScene.instance.All.Add(view.Data,view);
        return actor;
    }
    private static void Start(bool enabled=true)
    {
        MobHandoffVisual.Stop();MobHandoffVisual.LatencyWindow=null;ZNet.instance=new ZNet();ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();Time.realtimeSinceStartupAsDouble=0;
        Application.isBatchMode=false;CreatureOwnership.Reject=false;Player.m_localPlayer=null;MobHandoffVisual.Start(Assembly.GetExecutingAssembly(),enabled);
    }
    private static void Commit(Humanoid actor){CreatureOwnership.OnHandOff(new ZRpc(),actor.GetComponent<ZNetView>().Data.m_uid,2);}
    private static void Tick(double time){Time.realtimeSinceStartupAsDouble=time;MobHandoffVisual.Tick();}
    public static int Main()
    {
        try { Run(); Console.WriteLine("PASS: "+checks+" production render-bridge assertions; managed camera/transport/yaw fixtures, not live Unity rendering.");return 0; }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{MobHandoffVisual.Stop();}
    }
    private static void Run()
    {
        {
            Start();Humanoid hidden=Mob(),visibleA=Mob(),visibleB=Mob();ZDO skipped=hidden.GetComponent<ZNetView>().Data;
            MobHandoffVisual.SkipPresentation=delegate(ZDO value){return ReferenceEquals(value,skipped);};
            try
            {
                Commit(hidden);Check(Tracks.Count==0&&skipped.GetOwner()==2,"vanilla test skips legacy visual capture without preventing native transfer");
                Commit(visibleA);Commit(visibleB);Check(Tracks.Count==2,"vanilla comparison does not disable other mobs' visual bridges");
                Tick(.03);Camera.Before(new Camera());
                MobHandoffVisual.Cancel(visibleA.GetComponent<ZNetView>().Data.m_uid);
                Check(Tracks.Count==1&&Camera.Subscribed&&visibleA.Visual.transform.localPosition.Equals(new Vector3(0,.2f,0)),
                    "scoped cancellation restores native model and retains unrelated track/subscription");
                MobHandoffVisual.Cancel(visibleB.GetComponent<ZNetView>().Data.m_uid);
                Check(Tracks.Count==0&&!Camera.Subscribed,"last cancelled visual releases render subscriptions");
            }
            finally{MobHandoffVisual.SkipPresentation=null;}
        }
        Start();MobHandoffVisual.LatencyWindow=delegate{return .2f;};Humanoid timed=Mob();Commit(timed);
        Check(Math.Abs((float)TrackField("Window")-.2f)<.00001f&&Math.Abs((float)TrackField("TargetHorizon")-.1f)<.00001f,"route cache not used at local cutoff");
        ZDO timingData=timed.GetComponent<ZNetView>().Data;MobHandoffVisual.TargetTiming(timingData,timingData.OwnerRevision-1U,.15f);
        Check(Math.Abs((float)TrackField("Window")-.2f)<.00001f&&Math.Abs((float)TrackField("TargetHorizon")-.1f)<.00001f,"timing reply changes presentation instantly");
        Tick(.01);float refined=(float)TrackField("Window");
        Check(refined>.2f&&refined<.3f&&(float)TrackField("TargetHorizon")>.1f&&(float)TrackField("TargetHorizon")<.15f,"new RTT not blended into bridge and target horizon");
        timingData.DataRevision++;Tick(.02);refined=(float)TrackField("Window");MobHandoffVisual.TargetTiming(timingData,timingData.OwnerRevision-1U,.25f);Tick(.03);
        Check(Math.Abs((float)TrackField("Window")-refined)<.00001f,"late timing extends recovery after real state arrived");
        Start();MobHandoffVisual.LatencyWindow=delegate{return .5f;};timed=Mob();Commit(timed);timingData=timed.GetComponent<ZNetView>().Data;
        Tick(.04);MobHandoffVisual.TargetTiming(timingData,timingData.OwnerRevision-1U,.03f);float lastSim=(float)TrackField("SimTime");
        for(int step=5;step<=12;step++){Tick(step*.01);if(Tracks.Count==0)break;float sim=(float)TrackField("SimTime");Check(sim>=lastSim,"lower RTT rewinds local preview time");lastSim=sim;}
        Start(false);Humanoid a=Mob();Commit(a);Check(Tracks.Count==0&&!Camera.Subscribed,"disabled has no visual state or render subscriptions");
        Start();a=Mob();CreatureOwnership.Reject=true;Commit(a);Check(Tracks.Count==0&&!Camera.Subscribed,"native refusal creates no bridge");
        CreatureOwnership.Reject=false;a.Attacking=true;Commit(a);Check(Tracks.Count==0,"attack animation cannot be predicted");
        Start();a=Mob();a.m_currentAttack=new Attack();Commit(a);Check(Tracks.Count==0,"unfinished attack clone blocks presentation too");
        Start();a=Mob();a.Visual.Add<Collider>();Commit(a);Check(Tracks.Count==0,"collider-bearing visual subtree stays native");
        Start();a=Mob();a.Visual.Add<Rigidbody>();Commit(a);Check(Tracks.Count==0,"body-bearing visual subtree stays native");
        Start();a=Mob();a.Visual.Add<ZNetView>();Commit(a);Check(Tracks.Count==0,"network-bearing visual subtree stays native");
        Start();a=Mob();a.Visual=a.gameObject;Commit(a);Check(Tracks.Count==0,"root visual never moved");
        Start();a=Mob();ZDO d=a.GetComponent<ZNetView>().Data;Vector3 root=a.transform.position,local=a.Visual.transform.localPosition;
        Commit(a);Check(Tracks.Count==1&&Camera.Subscribed&&d.OwnerWrites==1,"accepted native handoff arms exactly one visual track");
        Check(a.GetComponent<ZSyncTransform>().Flushes==0,"presentation cannot trigger native root-changing SyncNow");
        Camera c=new Camera();Tick(.02);Tick(.05);Camera.Before(c);
        Check(a.Visual.transform.position.x>0&&a.Visual.transform.position.x<.05f,"50ms RTT produces bounded slower visual continuation");
        Check(a.transform.position.sqrMagnitude==root.sqrMagnitude&&d.OwnerWrites==1,"rendering leaves root and ownership untouched");
        Camera.After(c);Check((a.Visual.transform.localPosition-local).sqrMagnitude<1e-10,"post-render restores exact local visual pose");
        Camera nested=new Camera();Camera.Before(c);Vector3 drawn=a.Visual.transform.position;Camera.Before(nested);Camera.After(nested);
        Check((a.Visual.transform.position-drawn).sqrMagnitude<1e-10,"nested reflection camera cannot undo the outer camera's visual offset");Camera.After(c);
        Check((a.Visual.transform.localPosition-local).sqrMagnitude<1e-10,"outermost camera alone restores the original pose");
        Camera.Before(c);Camera.Before(new Camera());MobHandoffVisual.Tick();Check((a.Visual.transform.localPosition-local).sqrMagnitude<1e-10,"nested/aborted camera fallback restores before next bridge pass");
        d.DataRevision++;a.transform.position=new Vector3(.08f,0,0);Tick(.1);Camera.Before(c);
        Check(a.Visual.transform.position.x>=.08f&&a.Visual.transform.position.x<.1f,"native progress consumes preview instead of dragging root movement backwards");Camera.After(c);
        for(int i=11;i<150;i++){a.transform.position=new Vector3(.08f+(i-10)*.002f,0,0);d.DataRevision++;Tick(i*.01);}
        Check(Tracks.Count==0&&!Camera.Subscribed,"recovery bounded; subscriptions removed after last bridge");
        Check(d.OwnerWrites==1&&a.GetComponent<ZSyncTransform>().Flushes==0,"no speculative state writes, forced packets or new owner commits during playback");
        Start();a=Mob();a.MoveDir=new Vector3(1,0,0);Commit(a);Tick(.02);
        a.transform.position=new Vector3(.4f,0,0);a.transform.rotation=Quaternion.LookRotation(new Vector3(1,0,0));
        Camera.Before(c);
        Check(Math.Abs(a.Visual.transform.position.x-.4f)<.00001f,"native correction after LateUpdate wins immediately at render time");
        Check(Quaternion.Angle(a.Visual.transform.rotation,a.transform.rotation)<.001f,"native turn after LateUpdate is neither doubled nor replaced by a stale absolute rotation");Camera.After(c);
        Start();a=Mob();Commit(a);a.GetComponent<ZNetView>().Data.SetOwner(1);Tick(.02);Check(Tracks.Count==0,"ownership regain cancels old bridge");
        Start();a=Mob();Commit(a);a.GetComponent<ZNetView>().Data.SetOwner(3);Tick(.02);Check(Tracks.Count==0,"third-owner change cancels old bridge");
        Start();a=Mob();Commit(a);a.transform.position=new Vector3(20,0,0);Tick(.02);Check(Tracks.Count==0,"teleports never dragged across world");
        Start();a=Mob();Commit(a);a.Attacking=true;Tick(.02);Check(Tracks.Count==0,"new remote attack immediately resumes native rendering");
        Start();a=Mob();Commit(a);ZDOMan.instance=new ZDOMan();Tick(.02);Check(Tracks.Count==0&&!Camera.Subscribed,"world replacement releases all references and callbacks");
        Start();a=Mob();Commit(a);for(int i=1;i<210;i++)Tick(i*.01);Check(Tracks.Count==0,"missing updates cannot retain a bridge indefinitely");
        Start();ZNet.instance.Peer.m_socket.Fail=true;a=Mob();Commit(a);Tick(.02);Check(Tracks.Count==1,"unsupported ping accessor leaves native commit and bounded visual fallback usable");
        Start();for(int i=0;i<50;i++)Commit(Mob());Check(Tracks.Count==32,"active reference count bounded under high handoff load");MobHandoffVisual.Stop();Check(!Camera.Subscribed&&Tracks.Count==0,"stop unpatches and releases all active references");
        Start();a=Mob();a.Visual=null;Commit(a);Check(Tracks.Count==0,"missing modded visual fails locally without breaking native handoff");
        Start();a=Mob();a.MoveDir=new Vector3(1,0,0);
        Commit(a);Tick(.02);Camera.Before(c);Check(Quaternion.Angle(a.Visual.transform.rotation,Quaternion.identity)>0,"target-facing turning continues visually");
        Check(Quaternion.Angle(a.transform.rotation,Quaternion.identity)<.001,"predicted turning cannot rotate gameplay root");
        MobHandoffVisual.Stop();Check(Quaternion.Angle(a.Visual.transform.localRotation,Quaternion.identity)<.001&&!Camera.Subscribed,"turning render pose restored when stopped mid-camera");
        Start();a=Mob();Commit(a);Tick(.05);a.Attacking=true;Camera.Before(c);Check(a.Visual.transform.position.x==0,"attack appearing between tick and rendering bypasses old prediction");Camera.After(c);
        Start();ZNet.instance.Peer.m_socket.Ping=300;a=Mob();Commit(a);Tick(.02);Tick(.05);Camera.Before(c);float slow=a.Visual.transform.position.x;Camera.After(c);
        Start();a=Mob();Commit(a);Tick(.02);Tick(.05);Camera.Before(c);Check(slow>a.Visual.transform.position.x,"larger measured RTT extends continuation window");Camera.After(c);
        Start();a=Mob();Commit(a);Tick(.1);Tick(.2);a.GetComponent<ZNetView>().Data.DataRevision++;Tick(.3);
        a=Mob();Commit(a);object track=Tracks[0];
        Check(Math.Abs((float)track.GetType().GetField("Window",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(track)-.05f)<.00001f,"another handoff cannot inherit the preceding mob's wait");
        Tick(.32);Tick(.35);Tick(.4);Tick(.48);Check(Tracks.Count==0,"short current RTT recovers before next 0.5-second handoff despite preceding slow mob");
        Start();a=Mob();Commit(a);Tick(.1);a.Dead=true;Tick(.11);Check(Tracks.Count==0,"death ends prediction");
        Start();a=Mob();Commit(a);Tick(.1);a.GetComponent<ZNetView>().Data.Valid=false;Tick(.11);Check(Tracks.Count==0,"destroyed/recycled ZDO cannot retain references");
        Start();a=Mob();Commit(a);Tick(.1);a.GetComponent<ZNetView>().Data.DataRevision++;Tick(.11);
        Check(Tracks.Count==1,"even a stationary authoritative update starts bounded recovery");
        for(int i=12;i<160;i++)Tick(i*.01);Check(Tracks.Count==0,"stationary updates do not leave lingering render state");
        Start();ZNet.instance.Server=true;MobHandoffVisual.Start(Assembly.GetExecutingAssembly(),true);a=Mob();Commit(a);Check(Tracks.Count==0&&!Camera.Subscribed,"server never creates visual bridges");
        Start();ZDOMan.instance=null;MobHandoffVisual.Start(Assembly.GetExecutingAssembly(),true);ZDOMan.instance=new ZDOMan();a=Mob();Commit(a);Check(Tracks.Count==1,"a world initialized after session settings can still capture the first handoff");
        Start();a=Mob();object captured=TooManyLags.PlayerSimulation.LocalAgent.VisualCapture(a.GetComponent<ZNetView>().Data,2);
        a.GetComponent<ZNetView>().Data.SetOwner(2);TooManyLags.PlayerSimulation.LocalAgent.VisualCommitted(captured);
        Check(Tracks.Count==1,"player-only simulation's real source commit uses the same render bridge");MobHandoffVisual.Stop();
        Check(TooManyLags.PlayerSimulation.LocalAgent.VisualCapture==null&&TooManyLags.PlayerSimulation.LocalAgent.VisualCommitted==null,"disabled mode removes player-only callbacks too");
        Check(Plugin.Warnings==0,"normal and unsupported model/transport paths raise no exceptions");
        FreshTargetsAndModes();ObserverBridge();
        Curves();
    }
    private static object TrackField(string name){object item=Tracks[0];return item.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(item);}
    private static Player Target(long owner,Vector3 position,Vector3 velocity)
    {
        GameObject root=new GameObject();Player player=root.Add<Player>();player.transform.position=position;player.Velocity=velocity;
        ZNetView view=root.Add<ZNetView>();view.Data=new ZDO{m_uid=new ZDOID{UserID=owner,ID=++next},Owner=owner};
        ZDOMan.instance.All[view.Data.m_uid]=view.Data;ZNetScene.instance.All[view.Data]=view;return player;
    }
    private static void FreshTargetsAndModes()
    {
        Start();ZNet.instance.Peer.m_socket.Ping=500;Humanoid mob=Mob();BaseAI ai=mob.GetComponent<BaseAI>();Player target=Target(2,new Vector3(0,0,10),new Vector3(10,0,0));ai.Fresh=true;ai.Target=target;mob.MoveDir=new Vector3(-1,0,0);Vector3 position=target.transform.position;
        Commit(mob);Quaternion aim=(Quaternion)TrackField("Aim");Check(Math.Abs(MobVisualCurve.Yaw(Quaternion.identity,aim))<.001f,"target prediction starts with a discontinuous future position");
        Check(((Vector3)TrackField("Intent")).sqrMagnitude==0&&target.transform.position.Equals(position),"fresh target retains old locomotion or moves the real target");
        Check(ReferenceEquals(TrackField("Target"),target),"capture does not retain actual current target identity");
        Tick(.05);float initial=(float)TrackField("Turn");ai.Target=Target(3,new Vector3(-10,0,0),new Vector3());Tick(.06);
        Check((bool)TrackField("TargetChanged")&&(bool)TrackField("HasState")&&(float)TrackField("Turn")>=0&&(float)TrackField("Turn")<=initial,"second target change flips old render yaw instead of easing its residual");
        Start();ZNet.instance.Peer.m_socket.Ping=500;mob=Mob();ai=mob.GetComponent<BaseAI>();target=Target(1,new Vector3(0,0,10),new Vector3(10,0,0));Player.m_localPlayer=target;ai.Fresh=true;ai.Target=target;Commit(mob);
        Check(Math.Abs(MobVisualCurve.Yaw(Quaternion.identity,(Quaternion)TrackField("Aim")))<.001f,"local player position was extrapolated again");
        Start();ZNet.instance.Peer.m_socket.Ping=500;mob=Mob();ai=mob.GetComponent<BaseAI>();target=Target(2,new Vector3(0,0,10),new Vector3(float.NaN,0,0));ai.Fresh=true;ai.Target=target;Commit(mob);
        Check(Tracks.Count==1&&Math.Abs(MobVisualCurve.Yaw(Quaternion.identity,(Quaternion)TrackField("Aim")))<.001f,"bad target velocity poisons visual capture");
        ai.Target=null;Tick(.02);Check((bool)TrackField("TargetChanged"),"target loss still forecasts obsolete target");
        Start();mob=Mob();mob.GetComponent<BaseAI>().m_randomFly=true;mob.Flying=false;mob.m_turnSpeed=90;mob.m_flyTurnSpeed=300;mob.MoveDir=new Vector3(1,0,0);Commit(mob);
        Check(!(bool)TrackField("Flight")&&(float)TrackField("TurnSpeed")==90,"landed random-flight mob incorrectly uses flight movement/turn mode");
        Tick(.02);mob.Flying=true;Tick(.03);Check((bool)TrackField("Flight")&&(bool)TrackField("HasState"),"takeoff continues obsolete ground forecast");
        Start();mob=Mob();mob.Velocity=new Vector3(0,5,0);Commit(mob);Tick(.02);Camera camera=new Camera();Camera.Before(camera);
        Check(Math.Abs(mob.Visual.transform.position.y-.2f)<.00001f,"ground mode adds invented vertical motion on top of native jumping/falling");Camera.After(camera);
        Check(MobVisualCurve.ProgressiveTargetLead(new Vector3(10,3,0),.3f,0).sqrMagnitude==0&&MobVisualCurve.ProgressiveTargetLead(new Vector3(10,0,0),.3f,1).x==3f,"progressive target lead lacks zero/full endpoints or latency scaling");
        Check(MobVisualCurve.ProgressiveTargetLead(new Vector3(100,0,0),.5f,1).sqrMagnitude==0&&MobVisualCurve.ProgressiveTargetLead(new Vector3(float.PositiveInfinity,0,0),.1f,1).sqrMagnitude==0,"implausible target velocity accepted");
        float lastWeight=0;for(int step=0;step<=100;step++){float weight=MobVisualCurve.FutureWeight(step/100f,1f);Check(weight>=lastWeight&&weight<=1,"future compensation is not gradual and monotonic");lastWeight=weight;}
        Check(Math.Abs(MobVisualCurve.Yaw(Quaternion.AngleAxis(170,new Vector3(0,1,0)),Quaternion.AngleAxis(-170,new Vector3(0,1,0)))-20)<.001f,"yaw wrap creates wrong rotation direction");
        Check(MobVisualCurve.ReconcileTurn(5,1,100,.02f)==5&&MobVisualCurve.ReconcileTurn(5,90,100,.02f)==0,"ordinary turns consumed or large correction doubled");
        Check(Math.Abs(MobVisualCurve.FlightTurn(100,0,1,.1f)-.5f)<.0001f&&Math.Abs(MobVisualCurve.FlightTurn(100,1,0,.1f)-9.5f)<.0001f,"flight forecast skips native turn-factor ramp or braking");
        Start();mob=Mob();mob.Flying=true;mob.currentRotSpeedFactor=0;mob.MoveDir=new Vector3(1,0,0);Commit(mob);Tick(.02);
        Check((float)TrackField("Turn")<.01f,"fresh flying target snaps immediately to full native turn speed");
        Start();mob=Mob();mob.MoveDir=new Vector3(1,0,0);mob.GetComponent<BaseAI>().Fresh=true;Commit(mob);Tick(.02);
        Check((float)TrackField("Turn")==0&&((Vector3)TrackField("Intent")).sqrMagnitude==0,"target loss at capture continues previous target's turn/path");
    }
    private static void ObserverBridge()
    {
        Start();Humanoid mob=Mob();ZDO data=mob.GetComponent<ZNetView>().Data;data.Owner=2;data.OwnerRevision=4;mob.transform.position=new Vector3(100,0,0);
        object state=MobHandoffVisual.CaptureObserver(data,2,3,4,ZDOID.None,new Vector3(2,0,0),new Vector3(1,0,0),false,.2f);
        Check(state!=null&&data.OwnerWrites==0&&Tracks.Count==0,"observer capture writes ownership or arms before commit");
        data.SetOwner(3);MobHandoffVisual.CommitObserver(state);Check(Tracks.Count==1,"verified observer owner epoch did not arm presentation");
        Time.realtimeSinceStartupAsDouble=.01;MobHandoffVisual.CommitObserver(state);Check(Tracks.Count==1&&(double)TrackField("Started")==0,"duplicate observer commit restarts or duplicates bridge lifetime");
        Tick(.02);Camera camera=new Camera();Camera.Before(camera);Check(mob.Visual.transform.position.x>100&&mob.transform.position.x==100,"observer bridge uses source root or mutates its own gameplay root");Camera.After(camera);
        Check(MobHandoffVisual.CaptureObserver(data,2,1,4,ZDOID.None,new Vector3(),new Vector3(),false,.2f)==null&&MobHandoffVisual.CaptureObserver(data,1,3,4,ZDOID.None,new Vector3(),new Vector3(),false,.2f)==null,"local owner/source gets duplicate observer bridge");
        Check(MobHandoffVisual.CaptureObserver(data,2,3,3,ZDOID.None,new Vector3(),new Vector3(),false,.2f)==null,"wrong observer epoch accepted");
        MobHandoffVisual.Stop();Check(MobHandoffVisual.CaptureObserver(data,2,3,4,ZDOID.None,new Vector3(),new Vector3(),false,.2f)==null&&!Camera.Subscribed,"disabled bridge still accepts observer metadata");
        Start();mob=Mob();data=mob.GetComponent<ZNetView>().Data;data.Owner=2;data.OwnerRevision=4;
        state=MobHandoffVisual.CaptureObserver(data,2,3,4,ZDOID.None,new Vector3(),new Vector3(),false,.2f);data.SetOwner(3);
        MobHandoffVisual.Stop();MobHandoffVisual.CommitObserver(state);Check(Tracks.Count==0&&!Camera.Subscribed,"late observer commit after disable reactivates rendering");
        Start();mob=Mob();data=mob.GetComponent<ZNetView>().Data;data.Owner=2;data.OwnerRevision=4;
        state=MobHandoffVisual.CaptureObserver(data,2,3,4,ZDOID.None,new Vector3(),new Vector3(),false,.2f);data.SetOwner(3);
        ZDOMan.instance=new ZDOMan();MobHandoffVisual.CommitObserver(state);Check(Tracks.Count==0&&!Camera.Subscribed,"late observer commit after world replacement retains stale actor");
        Start();mob=Mob();mob.Flying=true;mob.currentRotSpeedFactor=1;mob.m_flyTurnSpeed=300;mob.MoveDir=new Vector3(1,0,0);Commit(mob);Tick(.02);float sourceTurn=(float)TrackField("Turn");
        Start();mob=Mob();mob.Flying=true;mob.currentRotSpeedFactor=0;mob.m_flyTurnSpeed=300;data=mob.GetComponent<ZNetView>().Data;data.Owner=2;data.OwnerRevision=4;
        state=MobHandoffVisual.CaptureObserver(data,2,3,4,ZDOID.None,mob.Velocity,new Vector3(1,0,0),true,.05f,1);
        data.SetOwner(3);MobHandoffVisual.CommitObserver(state);Tick(.02);
        Check(Math.Abs((float)TrackField("Turn")-sourceTurn)<.0001f&&mob.currentRotSpeedFactor==0,"observer reuses stale replica flight factor or mutates native state");
        Check(MobHandoffVisual.CaptureObserver(data,2,3,4,ZDOID.None,new Vector3(),new Vector3(),true,.2f,float.NaN)==null&&MobHandoffVisual.CaptureObserver(data,2,3,4,ZDOID.None,new Vector3(),new Vector3(),true,.2f,1.1f)==null,"observer accepts malformed source flight factor");
    }
    private static void Curves()
    {
        foreach(float rtt in new[]{.02f,.05f,.1f,.2f,.5f,1f})
        {
            float w=MobVisualCurve.Window(rtt),previous=0;
            for(int i=0;i<200;i++)
            {
                float p=MobVisualCurve.PredictionTime(i*.01f,w);
                Check(p>=previous&&p<=w*.5001f,"prediction monotone and bounded at measured network windows");previous=p;
            }
            previous=1f;
            for(int i=0;i<200;i++)
            {
                float fade=MobVisualCurve.Fade(i*.01f,MobVisualCurve.Recovery(w));
                Check(fade>=0&&fade<=previous,"residual decays monotonically to zero with no inherited display velocity");previous=fade;
            }
            Check(previous==0,"recovery ends exactly rather than retaining an asymptotic offset");
        }
        // Native Y motion wins; only excessive correction steps are reconciled.
        Humanoid a; Camera c=new Camera();
        foreach(int ping in new[]{50,100,200}) foreach(int sign in new[]{-1,0,1})
        {
            Start();ZNet.instance.Peer.m_socket.Ping=ping;a=Mob();a.Flying=true;a.Velocity=new Vector3(2,sign*5,0);Commit(a);
            for(int frame=1;frame<60;frame++)
            {
                double time=frame/120d;float height=sign*5*(float)time;
                a.transform.position=new Vector3(2*(float)time,height,0);Tick(time);Camera.Before(c);
                Check(Math.Abs(a.Visual.transform.position.y-height-.2f)<.00001f,"normal rise/fall remains native without an extra Y arc");Camera.After(c);
            }
        }
        Start();ZNet.instance.Peer.m_socket.Ping=200;a=Mob();a.Flying=true;a.Velocity=new Vector3(0,5,0);Commit(a);
        Tick(.02);Tick(.04);Camera.Before(c);Check(Math.Abs(a.Visual.transform.position.y-.2f)<.00001f,"stationary native root gains no artificial upward preview");Camera.After(c);
        a.transform.position=new Vector3(0,.5f,0);a.Velocity=new Vector3();Tick(.06);Camera.Before(c);
        Check(a.Visual.transform.position.y<.7f&&a.Visual.transform.position.y>=.45f,"actual height correction receives bounded residual");Camera.After(c);
        for(int frame=8;frame<80;frame++)Tick(frame*.01);Camera.Before(c);
        Check(Math.Abs(a.Visual.transform.position.y-.7f)<.00001f,"height correction ends with bridge lifetime, no persistent drift");Camera.After(c);
        Check(MobVisualCurve.HeightCorrection(0,.1f,5,.02f)==0,"normal one-step vertical flight is not damped");
        Check(MobVisualCurve.HeightCorrection(0,.5f,0,.02f)==-.25f,"positive correction capped");
        Check(MobVisualCurve.HeightCorrection(0,-.5f,0,.02f)==.25f,"negative correction capped");
        Check(MobVisualCurve.Window(float.NaN)==.04f&&MobVisualCurve.Window(float.PositiveInfinity)==.04f,"invalid timing cannot poison prediction");
        Check(Math.Abs(MobVisualCurve.Recovery(.05f)-.11f)<.00001f,"short current reply never inherits a previous slow reply");
        Check(MobVisualCurve.Window(.5f)==.5f&&MobVisualCurve.Window(5f)==.5f,"used network latency capped at 500ms");
        Check(MobVisualCurve.Recovery(5f)==MobVisualCurve.Recovery(.5f)&&MobVisualCurve.Recovery(.5f)<.75f,"higher latency cannot lengthen smoothing beyond the 500ms budget");
        Check(MobVisualCurve.Recovery(.5f)>MobVisualCurve.Recovery(.2f)&&MobVisualCurve.Recovery(.2f)>MobVisualCurve.Recovery(.05f),"longer handoffs get longer recovery independently per mob");
        Vector3 stopped=MobVisualCurve.Displacement(new Vector3(10,0,0),new Vector3(),.25f);
        Vector3 cruising=MobVisualCurve.Displacement(new Vector3(10,0,0),new Vector3(2,0,0),.25f);
        Check(stopped.x<.8f&&cruising.x<1.15f,"brief initial speed is integrated with braking, never used as constant forecast velocity");
        Vector3 curve=MobVisualCurve.Displacement(new Vector3(2,0,0),new Vector3(0,0,2),.2f);
        Check(curve.x>0&&curve.z>curve.x,"forecast turns velocity towards actual movement intent");
        Check(MobVisualCurve.Reconcile(new Vector3(.2f,0,0),new Vector3(.04f,0,0),2f,.02f).x==.2f,"ordinary native motion is not consumed into a visible stop during recovery");
        Check(MobVisualCurve.Reconcile(new Vector3(.2f,0,0),new Vector3(.5f,0,0),2f,.02f).sqrMagnitude==0,"large native correction consumes old lead without adding its velocity");
        for(int i=0;i<300;i++)
        {
            Vector3 native=new Vector3(i*.01f,0,0),lead=MobVisualCurve.Lead(new Vector3(.05f,0,0),native);
            Check(lead.x>=0&&lead.x<=.05f,"native advancement consumes preview without a negative lag or speed impulse");
            Check(i<5||lead.sqrMagnitude<1e-10f,"moving native root wins over an obsolete prediction");
        }
        Check(MobVisualCurve.Lead(new Vector3(100,0,0),new Vector3()).sqrMagnitude<=.0625f,"preview lead capped at 25cm even with large velocity/RTT");
        Check(MobVisualCurve.Consume(new Vector3(.2f,0,0),new Vector3(.4f,0,0)).sqrMagnitude==0,"one 40cm root correction consumes lead without overshooting or a rebound");
        Check(MobVisualCurve.Consume(new Vector3(.2f,0,0),new Vector3(-.4f,0,0)).x==.2f,"opposing correction never grows preview or manufactures velocity");
    }
}
