using System;
using System.Reflection;
using TooManyLags;
using UnityEngine;

internal static partial class IntegrationTests
{
    private static void TamedHandoffTests()
    {
        Fresh(Kinds.TamedAnimals,defaultInteractives:true);Peer(1);
        ZDO data=Object(typeof(Humanoid),40,1,7);data.Tamed=true;
        ZNetView view=ZNetScene.instance.FindInstance(data);Humanoid actor=view.GetComponent<Humanoid>();
        Tameable tame=view.gameObject.Add(new Tameable());ZSyncTransform sync=view.gameObject.Add(new ZSyncTransform());
        MethodInfo veto=typeof(TamedMobHandoff).GetMethod("AfterVeto",BindingFlags.NonPublic|BindingFlags.Static);
        Func<string,string> reason=delegate(string value){object[] args={data,value};veto.Invoke(null,args);return (string)args[1];};
        int calls=0;ZDOMan.SessionID=7;
        Action<ZRpc,ZDOID,long> native=delegate(ZRpc rpc,ZDOID id,long target)
        {calls++;Check("scoped tame commit clears only native tamed veto",reason("tamed")==null);Check("other native vetoes survive",reason("held")=="held"&&reason("owner fighting")=="owner fighting");data.SetOwner(target);};
        Check("unscoped native tamed veto unchanged",reason("tamed")=="tamed");
        foreach(int mode in new[]{0,1,2,3,4,5,6,7,8,9})
        {
            actor.Attacking=mode==0;actor.Staggering=mode==1;actor.m_currentAttack=mode==2?new Attack():null;
            actor.Attached=mode==3;actor.Riding=mode==4;actor.Dead=mode==5;
            tame.m_saddle=mode==6?new Sadle{User=true}:null;tame.m_commandable=mode==7;data.Follow=mode==7?"owner":"";
            actor.m_faction=mode==8?Character.Faction.Players:Character.Faction.ForestMonsters;data.User=mode==9?7:0;
            TamedMobHandoff.Commit(native,new ZRpc(),data.m_uid,99);
            Check("unsafe source animal does not invoke native commit "+mode,calls==0&&data.GetOwner()==7&&sync.Syncs==0);
        }
        actor.Attacking=actor.Staggering=actor.Attached=actor.Riding=actor.Dead=false;actor.m_currentAttack=new Attack{Done=true};
        tame.m_saddle=null;tame.m_commandable=false;data.Follow="";data.User=0;actor.m_faction=Character.Faction.ForestMonsters;
        TamedMobHandoff.Commit(native,new ZRpc(),data.m_uid,99);
        Check("idle animal commits at real source with final motion flush",calls==1&&data.GetOwner()==99&&sync.Syncs==1);
        Check("authorization is removed after native commit",reason("tamed")=="tamed");
        data.SetOwner(7);
        try { TamedMobHandoff.Commit(delegate {throw new InvalidOperationException("fixture");},new ZRpc(),data.m_uid,99); }
        catch(InvalidOperationException) { }
        Check("authorization also removed after exception",reason("tamed")=="tamed");
        ZDOMan.SessionID=99;int before=calls;TamedMobHandoff.Commit(native,new ZRpc(),data.m_uid,99);
        Check("passive replica cannot authorize animal handoff",calls==before&&data.GetOwner()==7);

        Fresh(Kinds.TamedAnimals,defaultInteractives:true);Peer(1);Peer(7,64);Tick(0);data=Object(typeof(Character),40,1,0);data.Tamed=true;
        AuthorityController.MobServerReady=delegate{return false;};controller.InitialMobLoad(data);
        Check("unprepared tamed initial load keeps native first assignment",data.GetOwner()==0&&controller.NativeOwnerTarget(data,7)==7);
        AuthorityController.MobServerReady=delegate{return true;};controller.InitialMobLoad(data);
        Check("prepared tamed initial load selects server without area-ready wait",data.GetOwner()==99);
        AuthorityController.MobServerReady=null;
#if GRID_PROTOCOL
        TamedProtocolTransfer();
#endif
        Console.WriteLine("PASS: tamed source-commit guards, final motion, scoped veto, initial collision readiness and exception cleanup.");
    }
#if GRID_PROTOCOL
    private static void TamedProtocolTransfer()
    {
        Fresh(Kinds.TamedAnimals,defaultInteractives:true,checkIntervalSeconds:.05f);Peer(1);ZNetPeer source=Peer(7,64);
        var server=new ZNetPeer{Uid=99,m_server=true};ZNet.instance.Peers.Add(server);
        // Production starts protocol before constructing its controller world.
        GridStart();
        ZDO data=Object(typeof(Character),40,1,7);data.Tamed=true;
        var view=ZNetScene.instance.FindInstance(data);view.gameObject.Add(new ZSyncTransform());controller.DataChanged(data);
        try
        {
            GridReceive(source,GridPacket(0,p=>p.Write(1)));
            Check("ordinary grid capability cannot authorize animal handoff",!TooManyLags.GridHandoffs.Protocol.SupportsTamed(source));Tick(0);
            Check("old client keeps native animal ownership",data.GetOwner()==7&&!controller.TransferPending(data));
            GridReceive(source,GridPacket(0,p=>p.Write(257)));
            Check("new animal source capability recognized",TooManyLags.GridHandoffs.Protocol.SupportsTamed(source));
            var commands=GridCapture(source);Tick(2.3f);
            Check("animal grid return sends one identified source command: messages="+commands.Count+"; owner="+data.GetOwner()+"; pending="+controller.TransferPending(data)+"; attempts="+controller.ScheduledAttempts,commands.Count==1&&data.GetOwner()==7&&controller.TransferPending(data));
            commands[0].SetPos(0);Check("animal uses appended wire message, not unguarded legacy commit",commands[0].ReadInt()==12);commands[0].SetPos(0);
            var nativeField=typeof(TooManyLags.GridHandoffs.Protocol).GetField("nativeCommit",BindingFlags.NonPublic|BindingFlags.Static);
            var veto=typeof(TamedMobHandoff).GetMethod("AfterVeto",BindingFlags.NonPublic|BindingFlags.Static);int commits=0;
            nativeField.SetValue(null,(Action<ZRpc,ZDOID,long>)delegate(ZRpc rpc,ZDOID id,long target)
            {commits++;object[] args={data,"tamed"};veto.Invoke(null,args);if(args[1]==null)data.SetOwner(target);});
            ZNet.instance.Dedicated=false;ZDOMan.SessionID=7;ZNet.instance.ServerPeer=server;
            var replies=GridCapture(server);GridReceive(server,commands[0]);GridReceive(server,commands[0]);
            Check("source commits once despite duplicate delivery",commits==1&&data.GetOwner()==99&&replies.Count==2);
            ZNet.instance.Dedicated=true;ZDOMan.SessionID=99;ZNet.instance.ServerPeer=null;
            GridReceive(source,replies[0]);Tick(2.6f);
            Check("native animal receipt settles actual grid transaction",data.GetOwner()==99&&!controller.TransferPending(data));
        }
        finally {ZNet.instance.Dedicated=true;ZDOMan.SessionID=99;ZNet.instance.ServerPeer=null;TooManyLags.GridHandoffs.Protocol.Stop();}
    }
#endif
}
