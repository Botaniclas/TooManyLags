using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using TooManyLags;
using UnityEngine;
using FiresGhettoNetworkMod;

internal static class TrajectoryAudit
{
    private static readonly FieldInfo tracks=typeof(MobHandoffVisual).GetField("tracks",BindingFlags.NonPublic|BindingFlags.Static);
    private static Humanoid Setup(int ping,float speed)
    {
        MobHandoffVisual.Stop();ZNet.instance=new ZNet();ZNet.instance.Peer.m_socket.Ping=ping;
        ZDOMan.instance=new ZDOMan();ZNetScene.instance=new ZNetScene();Time.realtimeSinceStartupAsDouble=0;Application.isBatchMode=false;CreatureOwnership.Reject=false;
        GameObject root=new GameObject();Humanoid mob=root.Add<Humanoid>();mob.Visual=new GameObject();mob.Visual.transform.parent=root.transform;root.transform.Children.Add(mob.Visual.transform);
        mob.Velocity=new Vector3(speed,0,0);mob.MoveDir=new Vector3(1,0,0);ZNetView view=root.Add<ZNetView>();view.Data=new ZDO{m_uid=new ZDOID{UserID=1,ID=1}};
        root.Add<ZSyncTransform>();root.Add<BaseAI>();ZDOMan.instance.All.Add(view.Data.m_uid,view.Data);ZNetScene.instance.All.Add(view.Data,view);
        MobHandoffVisual.Start(Assembly.GetExecutingAssembly(),true);CreatureOwnership.OnHandOff(new ZRpc(),view.Data.m_uid,2);return mob;
    }
    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        try
        {
            string output=args.Length==0?Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"trajectories"):args[0];
            Directory.CreateDirectory(output);
            using(var summary=new StreamWriter(Path.Combine(output,"summary.csv")))
            {
                summary.WriteLine("scenario,max_native_frame_m,max_visual_frame_m,max_offset_m,max_negative_visual_step_m,exit_time_s,exit_native_step_m,exit_visual_step_m,owner_writes");
                Run(output,summary,"native_extrapolation_without_fresh_state",50,2f,99,99,99,99);
                Run(output,summary,"steady_movement_50ms_packets",50,2f,.1,99,99,99);
                Run(output,summary,"attack_during_recovery",150,4f,.2,.2333333,99,99);
                Run(output,summary,"ownership_return_during_recovery",300,4f,.35,99,.5,99);
                Run(output,summary,"early_nonpose_revision",50,2f,.35,99,99,.05);
                Run(output,summary,"one_missing_render_frame",150,4f,.15,99,99,99,true);
            }
            RecoverySamples(output);Correction(output);TimingCarryover(output);TurnRecovery(output);Console.WriteLine("PASS: production visual trajectories retain native progress, avoid correction-velocity overshoot and isolate handoff timing. Modeled native ClientSync; no live Unity world.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{MobHandoffVisual.Stop();}
    }
    private static void RecoverySamples(string output)
    {
        foreach(int ping in new[]{50,100,200,500,1200})
        {
            Humanoid mob=Setup(ping,8f);ZDO data=mob.GetComponent<ZNetView>().Data;Camera camera=new Camera();
            double window=MobVisualCurve.Window(ping/1000f), dt=1d/120;float previous=0,minRatio=1f,maxRatio=0f;
            double ended=-1;bool arrived=false;float blend=0;
            using(var csv=new StreamWriter(Path.Combine(output,"recovery-"+ping+"ms.csv")))
            {
                csv.WriteLine("time_s,native_x,visual_x,bridge_active");
                for(int frame=1;frame<=200;frame++)
                {
                    double now=frame*dt;
                    if(now+1e-7>=window)
                    {
                        if(!arrived){data.DataRevision++;arrived=true;mob.Velocity=new Vector3(2,0,0);}
                        mob.transform.position=new Vector3(2*(float)Math.Max(0,now-window),0,0);
                    }
                    Time.realtimeSinceStartupAsDouble=now;MobHandoffVisual.Tick();
                    bool active=((IList)tracks.GetValue(null)).Count!=0;
                    if(active&&arrived){object t=((IList)tracks.GetValue(null))[0];blend=(float)t.GetType().GetField("Blend",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(t);}
                    Camera.Before(camera);float visual=mob.Visual.transform.position.x;Camera.After(camera);
                    if(now>window+dt*2&&active)
                    {float ratio=(visual-previous)/(2*(float)dt);minRatio=Math.Min(minRatio,ratio);maxRatio=Math.Max(maxRatio,ratio);}
                    if(!active&&ended<0)ended=now;
                    if(Math.Abs(visual-mob.transform.position.x)>.2501f)throw new Exception("Forecast exceeded its 25cm render-only bound.");
                    csv.WriteLine(String.Format(CultureInfo.InvariantCulture,"{0:F6},{1:F6},{2:F6},{3}",now,mob.transform.position.x,visual,active));
                    previous=visual;
                }
            }
            if(minRatio<.65f||maxRatio>1.01f||ended<0||ended>1.26||blend>.741f)
                throw new Exception("Recovery must move mildly slower, never sprint to catch up; ping="+ping+" min="+minRatio+" max="+maxRatio+" end="+ended);
            Console.WriteLine("PASS: "+ping+"ms model: recovery movement "+minRatio.ToString("F2")+".."+maxRatio.ToString("F2")+" of native speed; expires "+ended.ToString("F3")+"s.");
        }
    }
    private static void Run(string output,StreamWriter summary,string name,int ping,float speed,double firstPacket,double attackAt,double gainAt,double revisionAt,bool skipFrame=false)
    {
        Humanoid mob=Setup(ping,speed);ZDO data=mob.GetComponent<ZNetView>().Data;Camera camera=new Camera();
        double dt=1d/60,physicsAt=.02,packetAt=firstPacket;float wirePosition=0,wireVelocity=speed,timer=0;uint posRevision=uint.MaxValue;
        float nativeLast=0,visualLast=0,maxNative=0,maxVisual=0,maxOffset=0,minStep=0,exitNative=0,exitVisual=0;double exitAt=-1;
        bool revisionDone=false,attackDone=false,gainDone=false,wasActive=true;
        using(var csv=new StreamWriter(Path.Combine(output,name+".csv")))
        {
            csv.WriteLine("time_s,native_x,visual_x,native_delta,visual_delta,bridge_active,data_revision,attacking");
            for(int frame=1;frame<=150;frame++)
            {
                double now=frame*dt;
                while(packetAt<=now+1e-7)
                {
                    // Constant-speed sender sampled 50ms before delivery.
                    wirePosition=speed*(float)Math.Max(0,packetAt-.05);data.DataRevision++;packetAt+=.05;
                }
                if(!revisionDone&&now+1e-7>=revisionAt){data.DataRevision++;revisionDone=true;}
                while(physicsAt<=now+1e-7)
                {
                    // Port of native non-parented, non-kinematic SyncPosition:
                    // revision resets extrapolation; max 2s; Lerp .2 below 5m.
                    if(posRevision!=data.DataRevision){posRevision=data.DataRevision;timer=0;}
                    timer=Math.Min(2,timer+.02f);float target=wirePosition+wireVelocity*timer;
                    float current=mob.transform.position.x,gap=Math.Abs(target-current);
                    if(gap>.001f)mob.transform.position=new Vector3(gap<5?current+(target-current)*.2f:target,0,0);
                    physicsAt+=.02;
                }
                if(!attackDone&&now+1e-7>=attackAt){mob.Attacking=true;attackDone=true;}
                if(!gainDone&&now+1e-7>=gainAt)
                {data.SetOwner(1);mob.transform.position=new Vector3(wirePosition,0,0);gainDone=true;}
                Time.realtimeSinceStartupAsDouble=now;
                if(skipFrame&&frame==12){continue;}
                MobHandoffVisual.Tick();Camera.Before(camera);float visual=mob.Visual.transform.position.x;Camera.After(camera);
                float native=mob.transform.position.x,nativeStep=native-nativeLast,visualStep=visual-visualLast;
                bool active=((IList)tracks.GetValue(null)).Count!=0;
                maxNative=Math.Max(maxNative,Math.Abs(nativeStep));maxVisual=Math.Max(maxVisual,Math.Abs(visualStep));maxOffset=Math.Max(maxOffset,Math.Abs(visual-native));minStep=Math.Min(minStep,visualStep);
                if(wasActive&&!active&&exitAt<0){exitAt=now;exitNative=nativeStep;exitVisual=visualStep;}
                csv.WriteLine(String.Format(CultureInfo.InvariantCulture,"{0:F6},{1:F6},{2:F6},{3:F6},{4:F6},{5},{6},{7}",now,native,visual,nativeStep,visualStep,active,data.DataRevision,mob.Attacking));
                wasActive=active;nativeLast=native;visualLast=visual;
            }
        }
        summary.WriteLine(String.Format(CultureInfo.InvariantCulture,"{0},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8}",name,maxNative,maxVisual,maxOffset,minStep,exitAt,exitNative,exitVisual,data.OwnerWrites));
        Console.WriteLine(name+": native frame max="+maxNative.ToString("F4")+"m; visual="+maxVisual.ToString("F4")+"m; offset="+maxOffset.ToString("F4")+"m; exit="+exitAt.ToString("F3")+"s, native step="+exitNative.ToString("F4")+"m / visual step="+exitVisual.ToString("F4")+"m");
        if(maxVisual>maxNative+.02f||maxOffset>.08f||exitAt<0||exitAt>.81||data.OwnerWrites!=(gainDone?2:1))throw new Exception("Native progress/finite recovery regression: "+name);
    }
    private static void Correction(string output)
    {
        Humanoid mob=Setup(150,0);ZDO data=mob.GetComponent<ZNetView>().Data;Camera camera=new Camera();float maximum=0,minimum=.4f;int reversals=0;float previous=0;
        for(int frame=1;frame<=30;frame++)
        {
            Time.realtimeSinceStartupAsDouble=frame/60d;
            if(frame==4){mob.transform.position=new Vector3(.4f,0,0);data.DataRevision++;}
            MobHandoffVisual.Tick();Camera.Before(camera);float position=mob.Visual.transform.position.x;Camera.After(camera);
            if(frame>=4){maximum=Math.Max(maximum,position);minimum=Math.Min(minimum,position);if(position<previous-1e-6)reversals++;}
            previous=position;
        }
        if(Math.Abs(maximum-.4f)>.00001f||Math.Abs(minimum-.4f)>.00001f||reversals!=0)throw new Exception("A root correction created visual overshoot/reversal.");
        using(var f=new StreamWriter(Path.Combine(output,"correction.txt")))
        {
            f.WriteLine("Stationary native root corrects forward 0.4m in one frame; actual production bridge receives the changed revision.");
            f.WriteLine("Visual min/max after correction="+minimum+" / "+maximum+"m; backwards correction frames="+reversals+". Native correction retained, no invented display velocity.");
        }
        // Native turns must consume the same predicted angular progress too.
        mob=Setup(150,0);mob.MoveDir=new Vector3(1,0,0);MobHandoffVisual.Stop();MobHandoffVisual.Start(Assembly.GetExecutingAssembly(),true);
        data=mob.GetComponent<ZNetView>().Data;data.SetOwner(1);CreatureOwnership.OnHandOff(new ZRpc(),data.m_uid,2);
        for(int frame=1;frame<=30;frame++)
        {
            Time.realtimeSinceStartupAsDouble=frame/60d;
            mob.transform.rotation=Quaternion.RotateTowards(Quaternion.identity,Quaternion.LookRotation(new Vector3(1,0,0)),frame*3f);
            MobHandoffVisual.Tick();Camera.Before(camera);float angle=Quaternion.Angle(mob.Visual.transform.rotation,mob.transform.rotation);Camera.After(camera);
            if(angle>.0001f)throw new Exception("Native turning already ahead must not be slowed or duplicated.");
        }
    }
    private static void TimingCarryover(string output)
    {
        Humanoid first=Setup(50,0);ZDO old=first.GetComponent<ZNetView>().Data;
        for(int frame=1;frame<=48;frame++)
        {Time.realtimeSinceStartupAsDouble=frame/60d;if(frame==48)old.DataRevision++;MobHandoffVisual.Tick();}
        old.SetOwner(1);Time.realtimeSinceStartupAsDouble=.81;MobHandoffVisual.Tick();
        GameObject root=new GameObject();Humanoid second=root.Add<Humanoid>();second.Visual=new GameObject();second.Visual.transform.parent=root.transform;root.transform.Children.Add(second.Visual.transform);
        second.Velocity=new Vector3(2,0,0);root.Add<ZSyncTransform>();root.Add<BaseAI>();ZNetView view=root.Add<ZNetView>();view.Data=new ZDO{m_uid=new ZDOID{UserID=1,ID=2}};
        ZDOMan.instance.All.Add(view.Data.m_uid,view.Data);ZNetScene.instance.All.Add(view.Data,view);
        Time.realtimeSinceStartupAsDouble=.85;CreatureOwnership.OnHandOff(new ZRpc(),view.Data.m_uid,2);
        object current=((IList)tracks.GetValue(null))[0];Type type=current.GetType();
        float window=(float)type.GetField("Window",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(current);
        for(int frame=1;frame<=30;frame++)
        {
            double age=frame/60d;Time.realtimeSinceStartupAsDouble=.85+age;second.transform.position=new Vector3((float)age*2,0,0);
            if(frame>=6)view.Data.DataRevision++;MobHandoffVisual.Tick();
        }
        float blend=(float)type.GetField("Blend",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(current);
        bool active=((IList)tracks.GetValue(null)).Count!=0;
        using(var f=new StreamWriter(Path.Combine(output,"timing-carryover.txt")))
        {
            f.WriteLine("Socket RTT remains 50ms. A first mob supplied a changed replica revision after 800ms.");
            f.WriteLine("A different mob's next handoff uses its own RTT window="+window+"s.");
            f.WriteLine("Its own first changed update arrives after 100ms; its blend="+blend+"s.");
            f.WriteLine("The second bridge remains active at the solo test's 500ms return interval="+active+".");
            f.WriteLine("No previous-mob history. Missing replies also trigger finite per-track recovery.");
        }
        if(Math.Abs(window-.05f)>.00001f||blend>.111f||active)throw new Exception("A slow unrelated mob contaminated the current handoff.");
    }
    private static void TurnRecovery(string output)
    {
        Humanoid mob=Setup(200,0);ZDO data=mob.GetComponent<ZNetView>().Data;Camera camera=new Camera();
        float previous=0,oldPrevious=0,oldTurn=0,oldFade=1,minRatio=1,maxRatio=0,minOldRatio=1,turnAtReply=0,blend=0;bool arrived=false;
        const float dt=1f/120f,speed=300f,window=.2f;
        using(var csv=new StreamWriter(Path.Combine(output,"turn-recovery-200ms.csv")))
        {
            csv.WriteLine("time_s,native_yaw,visual_yaw,legacy_visual_yaw,new_speed_ratio,legacy_speed_ratio");
            for(int frame=1;frame<=120;frame++)
            {
                float now=frame*dt,native=Math.Min(90f,Math.Max(0,now-window)*speed),lastNative=Math.Min(90f,Math.Max(0,(frame-1)*dt-window)*speed);
                mob.transform.rotation=Quaternion.AngleAxis(native,new Vector3(0,1,0));Time.realtimeSinceStartupAsDouble=now;
                if(!arrived&&now>=window){data.DataRevision++;arrived=true;}
                MobHandoffVisual.Tick();bool active=((IList)tracks.GetValue(null)).Count!=0;
                if(active&&arrived&&turnAtReply==0)
                {object item=((IList)tracks.GetValue(null))[0];Type type=item.GetType();turnAtReply=(float)type.GetField("Turn",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(item);oldTurn=turnAtReply;blend=(float)type.GetField("Blend",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(item);}
                Camera.Before(camera);float visual=MobVisualCurve.Yaw(Quaternion.identity,mob.Visual.transform.rotation);Camera.After(camera);
                float oldVisual=visual;
                if(arrived)
                {
                    // Exact former angular recovery rule: consume ALL native
                    // turning, then fade the remaining lead. This can stop or
                    // reverse the rendered yaw despite ordinary native motion.
                    oldTurn=Math.Max(0,oldTurn-Math.Max(0,native-lastNative));float fade=MobVisualCurve.Fade(now-window,blend);
                    oldTurn*=oldFade>0?fade/oldFade:0;oldFade=fade;oldVisual=Math.Min(90,native+oldTurn);
                    if(now>window+dt*2&&native<75&&active)
                    {
                        float ratio=(visual-previous)/(speed*dt),oldRatio=(oldVisual-oldPrevious)/(speed*dt);
                        minRatio=Math.Min(minRatio,ratio);maxRatio=Math.Max(maxRatio,ratio);minOldRatio=Math.Min(minOldRatio,oldRatio);
                    }
                }
                csv.WriteLine(String.Format(CultureInfo.InvariantCulture,"{0:F6},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6}",now,native,visual,oldVisual,(visual-previous)/(speed*dt),(oldVisual-oldPrevious)/(speed*dt)));
                previous=visual;oldPrevious=oldVisual;
            }
        }
        if(minRatio<.7f||maxRatio>1.001f||minOldRatio>.1f||data.OwnerWrites!=1)throw new Exception("Turn recovery still consumes ordinary native yaw: new="+minRatio+".."+maxRatio+" old="+minOldRatio);
        Console.WriteLine("PASS: 200ms turn model: new recovery "+minRatio.ToString("F2")+".."+maxRatio.ToString("F2")+" of native angular speed; former rule minimum "+minOldRatio.ToString("F2")+". Roots unchanged.");
    }
}
