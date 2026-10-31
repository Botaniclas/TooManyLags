using System;
using System.Collections;
using System.Reflection;
using TooManyLags;
using UnityEngine;
using FiresGhettoNetworkMod;

internal static class FlightHandoffTests
{
    private static int checks;private static byte[] payload;private static ZDOID id=new ZDOID{UserID=7,ID=8};
    private static Humanoid actor;private static BaseAI ai;private static ZDO data;private static ZNetPeer peer;
    private static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    private static IDictionary Pending {get{return (IDictionary)typeof(MobFlightHandoff).GetField("pending",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);}}
    private static void Setup(long session,bool server,long source=1,uint revision=0)
    {
        MobFlightHandoff.Stop();ZDOMan.SessionID=session;ZDOMan.instance=new ZDOMan();ZNet.instance=new ZNet{Server=server};ZNetScene.instance=new ZNetScene();
        Time.time=100;Time.realtimeSinceStartupAsDouble=0;CreatureOwnership.Reject=false;
        peer=ZNet.instance.Peer;peer.m_uid=server?source:9;peer.m_server=!server;ZNet.instance.Peers[peer.m_uid]=peer;
        GameObject root=new GameObject();actor=root.Add<Humanoid>();ai=root.Add<BaseAI>();
        root.Add<Rigidbody>();root.Add<ZSyncTransform>();ZNetView view=root.Add<ZNetView>();
        data=view.Data=new ZDO{m_uid=id,Owner=source,OwnerRevision=revision};ZDOMan.instance.All.Add(id,data);ZNetScene.instance.All.Add(data,view);
        MobFlightHandoff.Start(Assembly.GetExecutingAssembly());
        Check(FgnCombatOwnership.FlightTransfer!=null,"production hooks installed");FgnCombatOwnership.FlightConnect(peer);
        Check(FgnCombatOwnership.FlightMode(peer,server?23:24),"capability negotiation");
    }
    private static void Flight()
    {
        actor.m_flying=true;actor.m_currentVel=new Vector3(1,2,3);actor.m_moveDir=new Vector3(.1f,.2f,.3f);actor.m_run=true;
        actor.m_currentTurnVel=17;actor.m_currentTurnVelChange=4;actor.GetComponent<Rigidbody>().linearVelocity=new Vector3(4,5,6);
        ai.m_randomFly=true;ai.m_randomFlyTimer=12;ai.aroundPointUpdateTime=98;ai.arroundPointTarget=new Vector3(10,7,5);
        ai.m_randomMoveUpdateTimer=3;ai.m_randomMoveTarget=new Vector3(2,9,8);ai.m_reachedRandomMoveTarget=false;
    }
    private static byte[] Send(long target)
    {
        payload=null;peer.m_rpc.Send=delegate(string method,object[] args)
        {
            if(method!="TooManyLags_MobFlight")return;
            Check(data.GetOwner()==ZDOMan.GetSessionID(),"checkpoint precedes final native owner write");
            Check(actor.GetComponent<ZSyncTransform>().Flushes==1,"checkpoint shares final native motion cutoff");
            payload=((ZPackage)args[1]).Bytes();
        };
        CreatureOwnership.OnHandOff(new ZRpc(),id,target);return payload;
    }
    private static void Deliver(byte[] bytes){peer.m_rpc.Deliver(id,new ZPackage(bytes));}
    private static void Own(long target,uint revision=1){ZDOMan.instance.RPC_ZDOData(delegate{data.Owner=target;data.OwnerRevision=revision;});}
    private static void Near(float a,float b,string message){Check(Math.Abs(a-b)<.0001f,message);}
    public static int Main()
    {
        try{Run();Console.WriteLine("PASS: "+checks+" production flight checkpoint assertions; ordered modeled transport and native owner-write hooks, not live Unity.");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{MobFlightHandoff.Stop();}
    }
    private static void Run()
    {
        Setup(1,false);Flight();byte[] full=Send(2);Check(full!=null&&full.Length==118,"bounded 118-byte full flight payload");Check(data.Owner==2,"native commit retained");
        Setup(2,false);Deliver(full);Check(Pending.Count==1&&!actor.IsFlying(),"checkpoint waits for ownership epoch");
        Time.time=900;Time.realtimeSinceStartupAsDouble=.1;Own(2);
        Check(Pending.Count==0&&actor.IsFlying(),"restored at end of native ownership receive");Near(ai.m_randomFlyTimer,0,"non-random destination does not receive random phase");
        Check(actor.m_run&&actor.m_currentVel.y==2&&actor.m_moveDir.y==.2f,"movement intent restored");Near(actor.m_currentTurnVel,17,"turn accumulator");
        Near(actor.GetComponent<Rigidbody>().linearVelocity.y,5,"body vertical velocity");Check(actor.GetComponent<Rigidbody>().useGravity,"gravity policy remains untouched");
        Near(ai.aroundPointUpdateTime,897.9f,"circle deadline rebased to recipient clock and local wait");Near(ai.m_randomMoveUpdateTimer,2.9f,"wander remaining rebased");
        actor.m_moveDir=new Vector3(7,7,7);Own(2);Check(actor.m_moveDir.y==7,"checkpoint applied once");Deliver(full);Check(Pending.Count==0,"late checkpoint never rewinds current owner");
        Setup(2,false);ai.m_randomFly=true;actor.GetComponent<Rigidbody>().isKinematic=true;Deliver(full);Own(2);
        Near(ai.m_randomFlyTimer,12,"flight/land phase preserved");Check(actor.GetComponent<Rigidbody>().isKinematic&&actor.GetComponent<Rigidbody>().linearVelocity.sqrMagnitude==0,"PUP/body freeze remains in control");
        Setup(1,false);Flight();ai.Fresh=true;byte[] fresh=Send(2);Check(fresh.Length==86,"new target discards stale movement goals, 86-byte core");
        Setup(2,false);ai.aroundPointUpdateTime=100;ai.m_randomMoveUpdateTimer=99;Deliver(fresh);Own(2);
        Check(ai.m_randomMoveUpdateTimer==0&&ai.aroundPointUpdateTime<Time.time-5,"old local goals invalidated even with no source goal");
        Setup(1,false);Flight();actor.m_flying=false;byte[] grounded=Send(2);Setup(2,false);actor.m_flying=true;ai.m_randomFly=true;Deliver(grounded);Own(2);
        Check(!actor.IsFlying()&&ai.m_randomFlyTimer==12,"grounded random flyer retains ground phase without triggering takeoff");
        Setup(1,false);Flight();CreatureOwnership.Reject=true;Check(Send(2)==null&&data.Owner==1,"native attack/veto occurs before checkpoint");
        Setup(1,false);Check(Send(2)==null&&data.Owner==2,"terrestrial handoff has no checkpoint traffic");
        Check(actor.GetComponent<ZSyncTransform>().Flushes==1,"ground handoff did not publish one final native pose before the owner write");
        Setup(1,false);Flight();MobFlightHandoff.Stop();Check(Send(2)==null&&data.Owner==2,"disabled/unavailable module retains native commit");
        Setup(2,false);peer.Ready=false;Deliver(full);Check(Pending.Count==0,"non-ready peer rejected");
        Setup(2,false);peer.m_server=false;Deliver(full);Check(Pending.Count==0,"non-server transport rejected");
        Setup(2,false);data.Prefab=99;Deliver(full);Check(Pending.Count==0,"wrong prefab rejected");
        Setup(2,false);data.OwnerRevision=1;Deliver(full);Check(Pending.Count==0,"stale source epoch rejected");
        Setup(2,false);byte[] bad=(byte[])full.Clone();bad[0]=9;Deliver(bad);Check(Pending.Count==0,"unknown wire version rejected");
        Setup(2,false);bad=new byte[119];Array.Copy(full,bad,118);Deliver(bad);Check(Pending.Count==0,"trailing payload rejected");
        Setup(2,false);Deliver(new byte[161]);Deliver(new byte[]{1});Check(Pending.Count==0,"oversized/truncated payload fails open to native");
        Setup(2,false);bad=(byte[])full.Clone();Array.Copy(BitConverter.GetBytes(float.NaN),0,bad,26,4);Deliver(bad);Check(Pending.Count==0,"nonfinite flight phase rejected");
        Setup(2,false);Deliver(full);Deliver(full);Check(Pending.Count==1,"duplicate packet bounded");Time.realtimeSinceStartupAsDouble=6;Own(2);Check(!actor.IsFlying()&&Pending.Count==0,"expired packet cannot overwrite newer local simulation");
        Setup(2,false);Deliver(full);Own(3);Check(Pending.Count==0&&!actor.IsFlying(),"superseded destination rejected");
        Setup(2,false);Deliver(full);ZDOMan.instance=new ZDOMan();ZDOMan.instance.RPC_ZDOData(delegate{});Check(Pending.Count==0,"world change discards transient state");
        Setup(2,false);Deliver(full);ZNet.instance.Shutdown();Check(Pending.Count==0,"shutdown releases transient state");
        Setup(2,false);ZNetScene.instance.All.Clear();Deliver(full);Own(2);Check(Pending.Count==1,"unloaded native instance retains bounded checkpoint");
        ZNetScene.instance.All.Add(data,actor.GetComponent<ZNetView>());ai.m_randomFly=true;ai.Awake();Check(Pending.Count==0&&ai.m_randomFlyTimer==12,"late instance restored after native initialization");
        // Client -> server: same checkpoint, authenticated source, before final data owner update.
        Setup(1,false);Flight();byte[] toServer=Send(9);Setup(9,true);Deliver(toServer);Check(Pending.Count==1,"server destination accepts real source owner");Own(9);Check(actor.IsFlying(),"server ownership applies flight state");
        // Server -> client, including registered grid/tamed/player-only final write paths.
        Setup(9,true,9);peer.m_uid=2;ZNet.instance.Peers[2]=peer;Flight();byte[] fromServer=Send(2);Setup(2,false,9);Deliver(fromServer);Own(2);Check(actor.IsFlying(),"server-to-client checkpoint");
        Setup(9,true);byte[] relayed=null;ZNetPeer destination=new ZNetPeer{m_uid=2};ZNet.instance.Peers[2]=destination;
        FgnCombatOwnership.FlightConnect(destination);FgnCombatOwnership.FlightMode(destination,23);
        destination.m_rpc.Send=delegate(string method,object[] args){if(method=="TooManyLags_MobFlight"){Check(data.Owner==1,"relay precedes replicated owner write");relayed=((ZPackage)args[1]).Bytes();}};
        Deliver(full);Check(relayed!=null&&Pending.Count==0,"client-to-client server relay does not apply on replica");
        Setup(2,false);Deliver(relayed);Own(2);Check(actor.IsFlying(),"relayed destination installs state");
        Setup(1,false);Flight();peer.m_rpc.Send=delegate{throw new InvalidOperationException("transport failure");};MobFlightHandoff.Transfer(data,2);Check(data.Owner==2,"optional send failure cannot prevent native transfer");
        // Deferred goal adoption: native target sync stays authoritative.
        Setup(1,false);Flight();Player victim=new GameObject().Add<Player>();victim.gameObject.Add<ZNetView>().Data=new ZDO{m_uid=new ZDOID{UserID=42,ID=1}};ai.Target=victim;byte[] targeted=Send(2);
        Setup(2,false);Deliver(targeted);Own(2);Check(ai.Target==null&&ai.m_randomMoveUpdateTimer==0,"checkpoint never chooses a target on behalf of native AI");
        ai.Target=victim;FgnTargetEvents.FlightAdopt(ai,victim);Check(ai.m_randomMoveUpdateTimer>0,"matching native target adoption restores pending goal");
        Check(Plugin.Warnings==2,"only deliberate malformed packet and failed send warn");
        Setup(1,false);Flight();int extras=0;
        MobFlightHandoff.NativeTransfer=delegate(ZDO value){return ReferenceEquals(value,data);};
        MobFlightHandoff.SimulationTransfer=delegate{extras++;};MobFlightHandoff.CombatTransfer=delegate{extras++;};
        MobFlightHandoff.SimulationTransferred=delegate{extras++;};MobFlightHandoff.CombatTransferred=delegate{extras++;};
        int packets=0;peer.m_rpc.Send=delegate{packets++;};
        try
        {
            uint before=data.OwnerRevision;MobFlightHandoff.Transfer(data,2);
            Check(data.Owner==2&&data.OwnerRevision==before+1,"vanilla comparison preserves exactly one native final owner write");
            Check(packets==0&&extras==0&&actor.GetComponent<ZSyncTransform>().Flushes==0,
                "vanilla comparison performs no flight/combat/simulation checkpoint or forced pose publication");
        }
        finally{MobFlightHandoff.NativeTransfer=null;MobFlightHandoff.CombatTransfer=null;MobFlightHandoff.CombatTransferred=null;}
    }
}

