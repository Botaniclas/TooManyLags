using System;
using System.Reflection;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static partial class FgnCombatTests
{
    private static void BoundarySetup(bool negotiated)
    {
        HandoffSetup(); Client(1);
        if(negotiated)ZNet.instance.ServerPeer.m_rpc.Receive(9);
    }
    private static Player ThirdPlayer()
    {return ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(other.m_characterID)).GetComponent<Player>();}
    private static void SelectAndAttack(Character next)
    { ai.NextTarget=next;ai.SelectOnUpdate=true;ai.AttackAfterTarget=true;ai.UpdateAI(0.05f); }
    private static ZPackage Decision(long revision,long token,bool pending)
    {var p=new ZPackage();p.Write(revision);p.Write(token);p.Write(pending);return p;}
    private static long BoundaryToken(ZRpc rpc)
    {var p=rpc.BoundaryPayload.Rewind();p.ReadLong();long token=p.ReadLong();p.Rewind();return token;}
    private static void BoundaryTests()
    {
        var context=new UnitySynchronizationContext();var previous=System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(context);
        var hooks=new Harmony("combat.target.boundary.fixture");Type native=typeof(FiresGhettoNetworkMod.CreatureOwnership);
        try
        {
            Setup();
            FgnTargetEvents.Install(hooks,native.Assembly,FgnCombatOwnership.Watching,FgnCombatOwnership.Observe,error=>Call("Fail",error));
            FgnTargetBoundary.Install(hooks);
            FgnAttackRetry.Install(hooks,(unit,data)=>Call("OnAttackEnded",unit,data),error=>Call("Fail",error));
            hooks.Patch(native.GetMethod("TryHandOffCreature",flags),prefix:new HarmonyMethod(service.GetMethod("TryPrefix",flags)),
                postfix:new HarmonyMethod(service.GetMethod("TryPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
            hooks.Patch(native.GetMethod("Veto",flags),postfix:new HarmonyMethod(service.GetMethod("VetoPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteVeto",flags)));
            hooks.Patch(native.GetMethod("OnHandOff",flags),transpiler:new HarmonyMethod(typeof(FgnMobHandoff).GetMethod("RewriteResult",flags)));

            BoundarySetup(true);ZRpc bootstrap=ZNet.instance.ServerPeer.m_rpc;int bootstrapPackets=bootstrap.TargetSent;
            FieldInfo reportField=service.GetField("reports",flags);reportField.SetValue(null,Activator.CreateInstance(reportField.FieldType));
            FgnCombatOwnership.Observe(ai);
            Assert(bootstrap.BoundaryTargets==0&&bootstrap.TargetSent==bootstrapPackets+1&&FgnTargetBoundary.Changes==0,
                "bootstrap reports retain original packet with no extra decision reply or target-change intent");

            BoundarySetup(true);Humanoid actor=ai.GetComponent<Humanoid>();ZRpc server=ZNet.instance.ServerPeer.m_rpc;
            ai.AfterSelection=delegate {Assert(server.BoundaryTargets==0,"no report/ownership dispatch from unfinished target-selection operation");};
            SelectAndAttack(ThirdPlayer());ai.AfterSelection=null;
            Assert(actor.StartedAttacks==0&&!actor.InAttack(),"target write blocks a new attack in the same AI invocation before its native start");
            Assert(ai.DecisionsCompleted==1&&!ai.InsideUpdate,"native AI operation completes instead of freezing the AI");
            Assert(server.BoundaryTargets==1&&server.BoundaryId==other.m_characterID,"completed AI boundary sends latest target immediately without context wait");
            int packets=server.BoundaryTargets;context.Flush();Assert(server.BoundaryTargets==packets,"queued fallback dispatch cannot duplicate an already flushed report");
            for(int i=0;i<1000;i++)ai.UpdateAI(0.05f);
            Assert(server.BoundaryTargets==packets&&ai.DecisionsCompleted==1001,"stable AI sends no additional target packets while native decisions continue");
            Time.realtimeSinceStartupAsDouble=20.025;Server();old.m_rpc.ReceiveBoundaryTarget(mob.m_uid,server.BoundaryId,server.BoundaryPayload);
            Assert(old.m_rpc.Sent==1&&old.m_rpc.Destination==3&&old.m_rpc.BoundaryDecisions==1,"authenticated target event requests the existing identified handoff");
            Client(1);server.ReceiveBoundaryDecision(mob.m_uid,other.m_characterID,old.m_rpc.DecisionPayload);
            Assert(!actor.StartAttack(ThirdPlayer(),false),"pending server decision preserves the short new-attack barrier");
            Time.realtimeSinceStartupAsDouble=20.05;DeliverCommit();Assert(mob.GetOwner()==3&&actor.StartedAttacks==0,"two 25ms source/server legs commit before a new attack (transport is simulated)");

            BoundarySetup(true);actor=ai.GetComponent<Humanoid>();server=ZNet.instance.ServerPeer.m_rpc;SelectAndAttack(ThirdPlayer());
            long token=BoundaryToken(server);server.ReceiveBoundaryDecision(mob.m_uid,other.m_characterID,Decision(mob.OwnerRevision,token,false));
            Assert(actor.StartAttack(ThirdPlayer(),false),"retain-owner decision immediately releases the barrier");

            BoundarySetup(true);actor=ai.GetComponent<Humanoid>();server=ZNet.instance.ServerPeer.m_rpc;SelectAndAttack(ThirdPlayer());token=BoundaryToken(server);
            SelectAndAttack(targetPlayer);long next=BoundaryToken(server);
            server.ReceiveBoundaryDecision(mob.m_uid,other.m_characterID,Decision(mob.OwnerRevision,token,false));
            Assert(next>token&&!actor.StartAttack(targetPlayer,false),"old-target reply cannot release a newer target intent");
            server.ReceiveBoundaryDecision(mob.m_uid,target.m_characterID,Decision(mob.OwnerRevision+1,next,false));
            Assert(!actor.StartAttack(targetPlayer,false),"wrong owner revision cannot release an intent");
            FgnTargetBoundary.CommitFinished(mob,other.m_characterID);
            Assert(!actor.StartAttack(targetPlayer,false),"obsolete commit cannot release a different target's intent");
            server.ReceiveBoundaryDecision(mob.m_uid,target.m_characterID,Decision(mob.OwnerRevision,next,false));
            Assert(actor.StartAttack(targetPlayer,false),"matching final decision releases the current intent");

            BoundarySetup(true);actor=ai.GetComponent<Humanoid>();SelectAndAttack(ThirdPlayer());
            Time.realtimeSinceStartupAsDouble=20.249;Assert(!actor.StartAttack(ThirdPlayer(),false),"watchdog does not release before its bounded hold");
            Time.realtimeSinceStartupAsDouble=20.251;Assert(actor.StartAttack(ThirdPlayer(),false)&&FgnTargetBoundary.ExpiredHolds==1,"lost decision cannot leave combat blocked indefinitely");

            BoundarySetup(true);actor=ai.GetComponent<Humanoid>();SelectAndAttack(ThirdPlayer());
            FgnTargetBoundary.Reset();Assert(actor.StartAttack(ThirdPlayer(),false),"reset drops negotiation and actor intents without deferred work");
            BoundarySetup(true);actor=ai.GetComponent<Humanoid>();SelectAndAttack(ThirdPlayer());mob.SetOwner(3);
            Assert(actor.StartAttack(ThirdPlayer(),false),"changed owner/revision invalidates the old source barrier");

            BoundarySetup(false);actor=ai.GetComponent<Humanoid>();server=ZNet.instance.ServerPeer.m_rpc;int legacy=server.TargetSent;
            SelectAndAttack(ThirdPlayer());context.Flush();
            Assert(actor.StartedAttacks==1&&server.BoundaryTargets==0&&server.TargetSent==legacy+1,"unnegotiated peer keeps existing target protocol and native attack timing");
            BoundarySetup(true);actor=ai.GetComponent<Humanoid>();Player own=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();
            SelectAndAttack(own);Assert(actor.StartedAttacks==1,"target already owned by this client needs no attack barrier");

            BoundarySetup(true);actor=ai.GetComponent<Humanoid>();FiresGhettoNetworkMod.TargetSync.Next=ThirdPlayer();
            FiresGhettoNetworkMod.TargetSync.Adopt(ai);
            Assert(FgnTargetBoundary.Changes==0&&actor.StartAttack(ThirdPlayer(),false),"FGN handover adoption does not delay the new owner's first attack");
            context.Flush();

            BoundarySetup(true);actor=ai.GetComponent<Humanoid>();Attack active=actor.BeginAttack();actor.Attack=false;
            SelectAndAttack(ThirdPlayer());Assert(actor.StartedAttacks==1,"barrier does not cancel an already existing Attack clone or its native chain decision");
            actor.Attack=false;string veto=(string)native.GetMethod("Veto",flags).Invoke(null,new object[]{mob,3L});
            Assert(veto=="attacking"&&FgnTargetBoundary.Busy(actor),"unfinished Attack clone remains a handoff veto even before animator attack tag");
            active.Stop();

            BoundarySetup(true);actor=ai.GetComponent<Humanoid>();server=ZNet.instance.ServerPeer.m_rpc;SelectAndAttack(ThirdPlayer());token=BoundaryToken(server);
            Server();int sent=old.m_rpc.Sent;old.m_rpc.ReceiveBoundaryTarget(mob.m_uid,other.m_characterID,server.BoundaryPayload);
            int decisions=old.m_rpc.BoundaryDecisions;old.m_rpc.ReceiveBoundaryTarget(mob.m_uid,other.m_characterID,server.BoundaryPayload);
            Assert(old.m_rpc.Sent==sent+1&&old.m_rpc.BoundaryDecisions==decisions,"duplicate target token cannot request or acknowledge another handoff");
            old.m_rpc.ReceiveBoundaryTarget(mob.m_uid,other.m_characterID,new ZPackage());
            Assert(FgnCombatOwnership.Enabled,"malformed optional target packet cannot disable combat ownership");
            Client(1);server.ReceiveBoundaryDecision(mob.m_uid,other.m_characterID,new ZPackage());
            Assert(FgnCombatOwnership.Enabled,"malformed decision does not disable combat ownership");
            server.Receive(10);Assert(actor.StartAttack(ThirdPlayer(),false),"server withdrawal clears the new-attack barrier");
        }
        finally
        {hooks.UnpatchSelf();Call("Reset");System.Threading.SynchronizationContext.SetSynchronizationContext(previous);}
    }
}
