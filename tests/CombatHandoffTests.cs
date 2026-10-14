using System;
using System.Reflection;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static partial class FgnCombatTests
{
    private static void HandoffSetup()
    {
        Setup(); ZRpc.LegacySend=null; Server(); Call("SessionConnection",old);
        Client(1); Call("SessionConnection",ZNet.instance.ServerPeer); Server();
    }
    private static void DeliverTarget()
    {
        var rpc=ZNet.instance.ServerPeer.m_rpc; Server();
        Call("ReceiveTarget",old,rpc.TargetMob,rpc.TargetId,rpc.TargetRevision);
    }
    private static void DeliverCommit()
    {
        Client(1); ZNet.instance.ServerPeer.m_rpc.ReceiveCommit(mob.m_uid,old.m_rpc.CommitToken,old.m_rpc.CommitPayload);
    }
    private static void DeliverReply()
    {
        var rpc=ZNet.instance.ServerPeer.m_rpc; Server();
        old.m_rpc.ReceiveCommitResult(mob.m_uid,rpc.ResultToken,rpc.ResultPayload);
    }
    private static void IdentifiedHandoffTests()
    {
        Harmony hooks=new Harmony("combat.identified.fixture"); Type native=typeof(FiresGhettoNetworkMod.CreatureOwnership);
        var handoff=typeof(FgnMobHandoff);var context=new UnitySynchronizationContext();
        var previous=System.Threading.SynchronizationContext.Current;System.Threading.SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            HandoffSetup();
            hooks.Patch(native.GetMethod("TryHandOffCreature",flags),prefix:new HarmonyMethod(service.GetMethod("TryPrefix",flags)),
                postfix:new HarmonyMethod(service.GetMethod("TryPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
            hooks.Patch(native.GetMethod("Veto",flags),postfix:new HarmonyMethod(service.GetMethod("VetoPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteVeto",flags)));
            hooks.Patch(native.GetMethod("OnHandOff",flags),transpiler:new HarmonyMethod(handoff.GetMethod("RewriteResult",flags)));
            hooks.Patch(native.GetMethod("OnResult",flags),prefix:new HarmonyMethod(service.GetMethod("ResultPrefix",flags)),postfix:new HarmonyMethod(service.GetMethod("ResultPostfix",flags)));
            hooks.Patch(typeof(ZDOMan).GetMethod("RPC_ZDOData",BindingFlags.Instance|BindingFlags.NonPublic),postfix:new HarmonyMethod(handoff.GetMethod("AfterReplication",flags)));
            FgnAttackRetry.Install(hooks,(actor,data)=>Call("OnAttackEnded",actor,data),error=>Call("Fail",error));

            HandoffSetup(); Assert(FgnCombatOwnership.Fgn.TryTransfer(mob,20)&&old.m_rpc.CommitToken>0,"managed native send uses identified source request");
            long first=old.m_rpc.CommitToken; int sent=old.m_rpc.Sent;
            Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();
            FgnCombatOwnership.Observe(ai);DeliverTarget();
            Assert(old.m_rpc.Sent==sent&&FgnMobHandoff.Count==1,"latest target report updates policy without a second in-flight request");
            Time.realtimeSinceStartupAsDouble=40; Assert(!FgnCombatOwnership.Fgn.TryTransfer(mob,40)&&old.m_rpc.Sent==sent,"five-second FGN timeout never launches parallel identified commit");
            DeliverCommit(); Assert(mob.GetOwner()==1&&ZDOMan.instance.Forced==0,"stale target is rejected before native ownership write");
            int replies=ZNet.instance.ServerPeer.m_rpc.CommitResults;DeliverCommit();
            Assert(mob.GetOwner()==1&&ZNet.instance.ServerPeer.m_rpc.CommitResults==replies,"duplicate commit cannot execute a second native transfer or reply");
            DeliverReply(); Assert(old.m_rpc.Destination==3&&old.m_rpc.CommitToken!=first&&old.m_rpc.Sent==sent+1,"stale response immediately replaces request with latest target");
            Assert(!FgnCombatOwnership.Fgn.Cooldowns.ContainsKey(mob.m_uid),"identified stale target adds no six-second cooldown");
            DeliverCommit(); DeliverReply(); Assert(mob.GetOwner()==3&&FgnMobHandoff.Count==0&&!FgnCombatOwnership.Fgn.Pending(mob.m_uid,40),"accepted latest target clears native pending on observed commit");

            HandoffSetup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);sent=old.m_rpc.Sent;
            var body=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();var attack=body.BeginAttack();
            Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();FgnCombatOwnership.Observe(ai);DeliverTarget();
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==1&&FgnMobHandoff.Count==0&&old.m_rpc.Sent==sent,"attack rejection keeps source owner without immediate stale-target retry");
            Assert(!FgnCombatOwnership.Fgn.Cooldowns.ContainsKey(mob.m_uid),"attack veto remains active without six-second retry lock");
            Client(1);body.Attack=false;attack.Stop();context.Flush();DeliverTarget();
            Server();var ready=ZNet.instance.ServerPeer.m_rpc;Call("ReceiveAttackReady",old,ready.AttackReadyMob,ready.AttackReadyRevision);
            Assert(old.m_rpc.Destination==3&&old.m_rpc.Sent==sent+1,"attack end rereads latest target and immediately retries");
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==3,"native transfer after attack uses latest target");

            HandoffSetup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);sent=old.m_rpc.Sent;
            body=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();attack=body.BeginAttack();DeliverCommit();
            // The attack can finish while its veto reply is delayed beyond FGN's
            // old pending timeout. Its real latest target must still win.
            Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();
            body.Attack=false;attack.Stop();context.Flush();Time.realtimeSinceStartupAsDouble=40;DeliverTarget();
            ready=ZNet.instance.ServerPeer.m_rpc;Call("ReceiveAttackReady",old,ready.AttackReadyMob,ready.AttackReadyRevision);
            Assert(old.m_rpc.Sent==sent&&FgnMobHandoff.Count==1,"attack-end target update waits for older veto reply even after native timeout");
            DeliverReply();
            Assert(old.m_rpc.Destination==3&&old.m_rpc.Sent==sent+1,"delayed attack veto releases latest target without six-second lock");

            HandoffSetup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);sent=old.m_rpc.Sent;
            Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();FgnCombatOwnership.Observe(ai);DeliverTarget();
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==1&&old.m_rpc.Sent==sent&&FgnMobHandoff.Count==0,"target returning to current owner needs no unnecessary transfer");
            HandoffSetup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);Client(1);ai.Target=null;FgnCombatOwnership.Observe(ai);DeliverTarget();
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==1&&FgnMobHandoff.Count==0&&!FgnCombatOwnership.Fgn.Cooldowns.ContainsKey(mob.m_uid),"losing player target cannot commit stale combat destination");

            HandoffSetup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]=35;
            body=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();body.BeginAttack();DeliverCommit();DeliverReply();
            Assert(FgnCombatOwnership.Fgn.Cooldowns[mob.m_uid]==35,"authenticated result preserves unrelated pre-existing cooldown");

            HandoffSetup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);first=old.m_rpc.CommitToken;
            DeliverCommit();var response=ZNet.instance.ServerPeer.m_rpc.ResultPayload;
            // Independent replicas: accepted RPC can arrive before the source's owner revision.
            mob.SetOwner(1);mob.OwnerRevision=0;Server();DeliverReply();
            Assert(FgnMobHandoff.Count==1&&mob.GetOwner()==1,"acceptance alone never force-writes server replica ownership");
            sent=old.m_rpc.Sent;Time.realtimeSinceStartupAsDouble=30;
            Assert(!FgnCombatOwnership.Fgn.TryTransfer(mob,30)&&old.m_rpc.Sent==sent,"accepted-but-unreplicated source cannot start another request");
            old.m_rpc.ReceiveCommitResult(mob.m_uid,first,response);Assert(FgnMobHandoff.Count==1,"duplicate accepted result cannot finish another request");
            mob.SetOwner(2);typeof(ZDOMan).GetMethod("RPC_ZDOData",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ZDOMan.instance,null);
            Assert(FgnMobHandoff.Count==0&&!FgnCombatOwnership.Fgn.Pending(mob.m_uid,30)&&target.m_rpc.WatchSent>0,"network replication clears pending and immediately watches new owner");

            HandoffSetup();target.Ping=400;bool moved;Handle(out moved);Assert(moved&&old.m_rpc.Destination==100,"identified high-ping request preserves server destination");
            Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();FgnCombatOwnership.Observe(ai);DeliverTarget();
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==1&&old.m_rpc.Destination==3,"changed fallback target is reevaluated on server before ownership commit");

            HandoffSetup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);first=old.m_rpc.CommitToken;
            DeliverCommit();response=ZNet.instance.ServerPeer.m_rpc.ResultPayload;mob.SetOwner(1);mob.OwnerRevision=0;Server();
            other.m_rpc.Register<ZDOID,long,ZPackage>("TooManyLags_AI_CommitResult",delegate{});
            Call("SessionConnection",other);other.m_rpc.ReceiveCommitResult(mob.m_uid,first,response);
            old.m_rpc.ReceiveCommitResult(mob.m_uid,first+1,response);Assert(FgnMobHandoff.Count==1,"foreign peer and stale token cannot complete pending handoff");
            old.Ready=false;FgnMobHandoff.AfterReplication();Assert(FgnMobHandoff.Count==0,"disconnect removes pending request without retaining world actors");
            HandoffSetup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);mob.Valid=false;FgnMobHandoff.AfterReplication();Assert(FgnMobHandoff.Count==0,"destroyed mob prunes pending request");
            HandoffSetup();FgnCombatOwnership.Fgn.TryTransfer(mob,20);Call("Reset");Assert(FgnMobHandoff.Count==0,"world reset clears identified request cache");
            HandoffSetup();sent=old.m_rpc.Sent;int writes=mob.Writes;for(int i=0;i<10000;i++)FgnMobHandoff.AfterReplication();
            Assert(old.m_rpc.Sent==sent&&mob.Writes==writes&&FgnMobHandoff.Count==0,"empty replication path adds no searches, writes or network traffic");

            HandoffSetup();Client(1);var server=ZNet.instance.ServerPeer;server.m_rpc.Receive(5);
            Assert(!FgnCombatOwnership.Watching(ai),"legacy protocol retains native FGN instead of unsupported identified RPC");
        }
        finally{hooks.UnpatchSelf();FgnMobHandoff.Reset();System.Threading.SynchronizationContext.SetSynchronizationContext(previous);}
    }
}
