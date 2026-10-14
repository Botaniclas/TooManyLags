using System;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static partial class FgnCombatTests
{
    private static void BossSetup(bool enabled=true)
    {
        HandoffSetup(); ZNetScene.instance.FindInstance(mob).GetComponent<Character>().Boss=true;
        FgnBossPolicy.Configure(enabled,30f);
    }
    private static void BossEvent() { Call("ProcessTargetEvent",mob,target.m_characterID); }
    private static void BossTargetTests()
    {
        Harmony hooks=new Harmony("combat.boss.fixture");var native=typeof(FiresGhettoNetworkMod.CreatureOwnership);
        var context=new UnitySynchronizationContext();var previous=System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            BossSetup();
            hooks.Patch(native.GetMethod("TryHandOffCreature",flags),prefix:new HarmonyMethod(service.GetMethod("TryPrefix",flags)),
                postfix:new HarmonyMethod(service.GetMethod("TryPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
            hooks.Patch(FgnCombatOwnership.Fgn.Veto,postfix:new HarmonyMethod(service.GetMethod("VetoPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteVeto",flags)));
            hooks.Patch(FgnCombatOwnership.Fgn.OnHandOff,transpiler:new HarmonyMethod(typeof(FgnMobHandoff).GetMethod("RewriteResult",flags)));
            FgnBossPolicy.Install(hooks,native.Assembly);
            FgnAttackRetry.Install(hooks,(actor,data)=>Call("OnAttackEnded",actor,data),error=>Call("Fail",error));

            BossSetup();BossEvent();
            Assert(old.m_rpc.Destination==100&&FgnBossPolicy.Evaluations==1,"two nearby players: boss target event requests ready server exactly once");
            int sends=old.m_rpc.Sent;for(int i=0;i<10000;i++){FgnBossPolicy.Record(mob,target.m_characterID);bool changed;Handle(out changed);}
            Assert(old.m_rpc.Sent==sends&&FgnBossPolicy.Evaluations==1,"unchanged boss target has no repeated radius scans or parallel requests");
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==100,"identified boss request passes narrow native boss veto and commits on source");
            FgnCombatOwnership.Observe(ai);
            Assert(FgnBossPolicy.Evaluations==1,"new owner reporting same boss target reuses prior distance decision");
            ZDOMan.instance.GetZDO(old.m_characterID).Position=new Vector3(140,0,100);
            ZDOMan.instance.GetZDO(other.m_characterID).Position=new Vector3(140,0,100);
            bool moved;Handle(out moved);
            Assert(mob.GetOwner()==100&&FgnBossPolicy.Evaluations==1,"movement alone never reevaluates boss radius or reverses settled server choice");
            ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();
            FgnCombatOwnership.Observe(ai);
            Assert(mob.GetOwner()==3&&FgnBossPolicy.Evaluations==2,"next actual target change returns solo boss to its current target");

            BossSetup(false);BossEvent();Assert(old.m_rpc.Destination==2&&FgnBossPolicy.Evaluations==0,"disabled special boss option uses normal Hostile Mob target path without distance work");
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==2,"disabled option still supports cooperative player-target boss ownership");
            BossSetup();ZDOMan.instance.GetZDO(old.m_characterID).Position=new Vector3(131,0,100);
            ZDOMan.instance.GetZDO(other.m_characterID).Position=new Vector3(131,0,100);BossEvent();
            Assert(old.m_rpc.Destination==2&&!FgnBossPolicy.WantsServer(mob),"only one nearby player retains normal target ownership");
            BossSetup();ZDOMan.instance.GetZDO(old.m_characterID).Position=new Vector3(130,0,100);
            ZDOMan.instance.GetZDO(other.m_characterID).SetFloat(ZDOVars.s_health,0f);BossEvent();
            Assert(FgnBossPolicy.WantsServer(mob),"living player at exact thirty-metre boundary counts");
            BossSetup();ZDOMan.instance.GetZDO(old.m_characterID).SetFloat(ZDOVars.s_health,0f);
            ZDOMan.instance.GetZDO(other.m_characterID).SetFloat(ZDOVars.s_health,0f);BossEvent();
            Assert(old.m_rpc.Destination==2,"dead nearby players cannot activate server boss rule");
            BossSetup();FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive=false;BossEvent();
            Assert(old.m_rpc.Destination==2&&FgnBossPolicy.Evaluations==0,"no Server-Side Simulation: boss uses player target without server transfer or radius work");
            BossSetup();NativePup.Ready=false;BossEvent();Assert(old.m_rpc.Sent==0&&FgnBossPolicy.WantsServer(mob),"boss server transfer waits for original collider readiness proof");
            NativePup.Ready=true;Handle(out moved);Assert(moved&&old.m_rpc.Destination==100,"blocked readiness retry reuses cached boss decision");

            BossSetup();BossEvent();Client(1);var body=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();var attack=body.BeginAttack();
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==1&&FgnMobHandoff.Count==0,"native boss attack veto preserves current owner");
            Assert(!FgnCombatOwnership.Fgn.Cooldowns.ContainsKey(mob.m_uid),"boss attack rejection adds no six-second lock");
            Client(1);body.Attack=false;attack.Stop();context.Flush();Server();var ready=ZNet.instance.ServerPeer.m_rpc;
            Call("ReceiveAttackReady",old,ready.AttackReadyMob,ready.AttackReadyRevision);
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==100,"attack-end event retries boss decision with current target");

            BossSetup(false);BossEvent();Client(1);body=ZNetScene.instance.FindInstance(mob).GetComponent<Humanoid>();body.Stagger=true;
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==1,"boss exception retains native stagger veto");
            BossSetup();BossEvent();Client(1);ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();
            FgnCombatOwnership.Observe(ai);DeliverTarget();DeliverCommit();DeliverReply();
            Assert(mob.GetOwner()==1&&old.m_rpc.CommitToken>1,"in-flight boss retarget rejects old destination then requests newest decision");
            DeliverCommit();DeliverReply();Assert(mob.GetOwner()==100,"newest two-player boss target decision still reaches server");

            BossSetup();Client(1);
            Assert((string)FgnCombatOwnership.Fgn.Veto.Invoke(null,new object[]{mob,2L})=="boss","legacy/unidentified boss transfer retains FGN native boss veto");
            Assert(!FgnMobHandoff.AuthorizesBoss(mob.m_uid),"boss exception unavailable outside identified commit scope");
            Server();FgnBossPolicy.Record(mob,ZDOID.None);Assert(!FgnBossPolicy.WantsServer(mob)&&FgnBossPolicy.Evaluations==0,"boss without player target performs no distance evaluation");
            Call("Reset");Assert(!FgnBossPolicy.WantsServer(mob)&&FgnBossPolicy.Evaluations==0,"world reset discards all weak boss decisions");
        }
        finally{hooks.UnpatchSelf();FgnBossPolicy.Reset();FgnMobHandoff.Reset();System.Threading.SynchronizationContext.SetSynchronizationContext(previous);}
    }
}
