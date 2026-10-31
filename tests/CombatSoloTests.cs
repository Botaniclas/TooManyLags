#if TML_DEBUG_SYSTEM
using System;
using System.Reflection;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

internal static partial class FgnCombatTests
{
    private static void SoloSetup(UnitySynchronizationContext context)
    {
        HandoffSetup(); Server();
        MobBridgeVisual.Cancelled=0;
        ZNet.instance.Peers.Remove(target);ZNet.instance.Peers.Remove(other);
        FiresGhettoNetworkMod.CreatureOwnership.Candidates(new[]{old});
        Humanoid actor=ai.GetComponent<Humanoid>();actor.transform.position=mob.GetPosition();Character.All.Add(actor);
        Client(1);ZNet.instance.ServerPeer.m_rpc.Receive(9);ZNet.instance.ServerPeer.m_rpc.Receive(21);
        ai.Target=ZNetScene.instance.FindInstance(ZDOMan.instance.GetZDO(old.m_characterID)).GetComponent<Player>();
        context.Flush();Server();Call("ReceiveTarget",old,mob.m_uid,old.m_characterID,(long)mob.OwnerRevision);
        Assert(!(bool)Call("MarkWatch",mob),"ordinary one-player mob remains outside two-candidate watch before solo test");
        old.m_rpc.Receive(20);
    }
    private static void SoloStart(UnitySynchronizationContext context)
    {
        old.m_rpc.ReceiveSoloControl("start");Assert(FgnSoloCombatTest.Active,"admin can start with one eligible nearby mob");
        Client(1);var rpc=ZNet.instance.ServerPeer.m_rpc;
        rpc.ReceiveWatch(mob.m_uid,1,(long)mob.OwnerRevision);context.Flush();
        Server();Call("ReceiveTarget",old,mob.m_uid,rpc.TargetId,rpc.TargetRevision);
        Client(1);rpc.ReceiveSoloArm(old.m_rpc.SoloMob,old.m_rpc.SoloArmPayload);
    }
    private static void SoloTests()
    {
        var context=new UnitySynchronizationContext();var previous=System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(context);
        var hooks=new Harmony("combat.solo.fixture");Type native=typeof(FiresGhettoNetworkMod.CreatureOwnership);
        try
        {
            MethodInfo request=typeof(FgnSoloCombatTest).GetMethod("ParseRequest",BindingFlags.NonPublic|BindingFlags.Static);
            foreach(string name in new[]{"ai_test","ai_test_v","ai_test_vanilla"})
            {
                bool vanilla=name!="ai_test";
                foreach(string[] values in new[]{new[]{name},new[]{name,"100"},new[]{name,"100","2.5"},new[]{name,"start","100","2.5"},new[]{name,"start"},new[]{name,"stop"},new[]{name,"status"},new[]{name," ","",""}})
                {
                    object[] input={values,"",0};
                    Assert((bool)request.Invoke(null,input),"short test command accepts valid optional arguments");
                    string expected=values.Length==1||values[1]==" "?(vanilla?"toggle_vanilla":"toggle"):
                        values[1]=="stop"||values[1]=="status"?values[1]:
                        (vanilla?"start_vanilla ":"start ")+(values[1]=="start"&&values.Length==2?"0 0.5":values.Length==2?"100 0.5":"100 2.5");
                    Assert((string)input[1]==expected,"numeric first argument skips action; empty arguments toggle; aliases preserve mode");
                }
            }
            foreach(string text in new[]{"ai_test 100 2 3","ai_test what","ai_test stop 100","ai_test -1","ai_test 1001","ai_test 100 NaN","ai_test 100 Infinity","ai_test start 0 0"})
            {object[] input={text.Split(' '),"",0};Assert(!(bool)request.Invoke(null,input),"invalid test command rejected before sending control");}
            MethodInfo parse=typeof(FgnSoloCombatTest).GetMethod("ParseStart",BindingFlags.NonPublic|BindingFlags.Static);
            object[] parsed={"start 100 2.5",0,0f};
            Assert(parse!=null&&(bool)parse.Invoke(null,parsed)&&(int)parsed[1]==100&&Math.Abs((float)parsed[2]-2.5f)<.001f,
                "solo test accepts RTT and custom interval");
            parsed=new object[]{"start 100",0,0f};
            Assert((bool)parse.Invoke(null,parsed)&&(int)parsed[1]==100&&Math.Abs((float)parsed[2]-.5f)<.001f,
                "solo test defaults to 0.5-second interval");
            parsed=new object[]{"start 100 0",0,0f};
            Assert(!(bool)parse.Invoke(null,parsed),"solo test rejects zero interval");
            parsed=new object[]{"start 100 60.1",0,0f};
            Assert(!(bool)parse.Invoke(null,parsed),"solo test rejects excessive interval");
            Setup();FgnTargetEvents.Install(hooks,native.Assembly,FgnCombatOwnership.Watching,FgnCombatOwnership.Observe,error=>Call("Fail",error));
            FgnTargetBoundary.Install(hooks);FgnAttackRetry.Install(hooks,(unit,data)=>Call("OnAttackEnded",unit,data),error=>Call("Fail",error));
            hooks.Patch(native.GetMethod("TryHandOffCreature",flags),prefix:new HarmonyMethod(service.GetMethod("TryPrefix",flags)),postfix:new HarmonyMethod(service.GetMethod("TryPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteTarget",flags)));
            hooks.Patch(native.GetMethod("Veto",flags),postfix:new HarmonyMethod(service.GetMethod("VetoPostfix",flags)),transpiler:new HarmonyMethod(service.GetMethod("RewriteVeto",flags)));
            hooks.Patch(native.GetMethod("OnHandOff",flags),transpiler:new HarmonyMethod(typeof(FgnMobHandoff).GetMethod("RewriteResult",flags)));

            SoloSetup(context);int sent=old.m_rpc.Sent;for(int i=0;i<1000;i++)FgnSoloCombatTest.Poll();
            Assert(old.m_rpc.Sent==sent&&!FgnSoloCombatTest.Active,"inactive solo polling performs no handoff work");
            Assert(Harmony.GetPatchInfo(typeof(MonsterAI).GetMethod("UpdateAI")).Owners.Count==1,"inactive solo test installs no AI hooks");
            SoloStart(context);Humanoid actor=ai.GetComponent<Humanoid>();Character actual=ai.GetTargetCreature();var server=ZNet.instance.ServerPeer.m_rpc;
            ai.AttackAfterTarget=true;ai.UpdateAI(0.05f);
            Assert(ReferenceEquals(actual,ai.GetTargetCreature())&&actor.StartedAttacks==0,"synthetic handoff intent preserves real target and blocks same-call new attack");
            Assert(server.SoloReadies==1,"prepared source submits one readiness packet after native AI completion");
            ai.UpdateAI(0.05f);Assert(server.SoloReadies==1,"standing source cannot duplicate a prepared request");
            ZPackage ready=server.SoloReadyPayload.Rewind();long ticket=ready.ReadLong(),revision=ready.ReadLong();ZDOID actualTarget=ready.ReadZDOID();ready.Rewind();
            ZPackage stale=new ZPackage();stale.Write(ticket+1);stale.Write(revision);stale.Write(actualTarget);stale.Write(true);
            Server();old.m_rpc.ReceiveSoloReady(mob.m_uid,stale);
            Assert(old.m_rpc.Sent==sent,"wrong readiness ticket cannot trigger a commit");
            Server();old.m_rpc.ReceiveSoloReady(mob.m_uid,server.SoloReadyPayload);
            Assert(old.m_rpc.Sent==sent+1&&old.m_rpc.Destination==100,"solo source enters existing identified player-to-server commit path");
            int bridgeCancels=MobBridgeVisual.Cancelled;
            DeliverCommit();DeliverReply();Server();Time.realtimeSinceStartupAsDouble=20.1;FgnSoloCombatTest.Poll();
            Assert(mob.GetOwner()==100&&!FgnCombatOwnership.Fgn.Pending(mob.m_uid,20.1)&&MobBridgeVisual.Cancelled==bridgeCancels,
                "completed source handoff canceled fake presentation instead of leaving it for the bridge");
            FgnCombatOwnership.Observe(ai);sent=old.m_rpc.Sent;
            Time.realtimeSinceStartupAsDouble=20.59;FgnSoloCombatTest.Poll();
            Assert(mob.GetOwner()==100&&old.m_rpc.Sent==sent,"next request waits 0.5 seconds after observed completion");
            Time.realtimeSinceStartupAsDouble=20.66;FgnSoloCombatTest.Poll();ai.UpdateAI(0.05f);
            Assert(mob.GetOwner()==1&&ReferenceEquals(actual,ai.GetTargetCreature()),"server uses completed target-evaluation boundary and guarded native return without changing target");
            Assert(actor.StartedAttacks==0,"server-side barrier also prevents a same-call new attack before return");
            Time.realtimeSinceStartupAsDouble=20.73;FgnSoloCombatTest.Poll();
            Assert(!FgnCombatOwnership.Fgn.Pending(mob.m_uid,20.73),"completed direct server return does not impose native five-second in-flight wait on next test cycle");
            int writes=mob.OwnerWrites;bridgeCancels=MobBridgeVisual.Cancelled;
            Time.realtimeSinceStartupAsDouble=21;old.m_rpc.ReceiveSoloControl("stop");
            Assert(!FgnSoloCombatTest.Active&&mob.OwnerWrites==writes,"stopping clears test without forcing owner reset");
            Assert(MobBridgeVisual.Cancelled==bridgeCancels+1,"stopping retires the selected mob presentation through protocol cancellation");
            Assert(Harmony.GetPatchInfo(typeof(MonsterAI).GetMethod("UpdateAI")).Owners.Count==1,"stopping removes temporary solo AI hooks only");

            SoloSetup(context);mob.SetOwner(100);old.m_rpc.ReceiveSoloControl("start");
            Assert(FgnSoloCombatTest.Active,"test can start from existing server ownership with server defaults off");
            SoloSetup(context);ZNetScene.instance.Ready=false;old.m_rpc.ReceiveSoloControl("start");
            Assert(FgnSoloCombatTest.Active,"diagnostic selection waits for pending area readiness instead of rejecting the mob");
            actor=ai.GetComponent<Humanoid>();ai.AttackAfterTarget=true;ai.UpdateAI(0.05f);
            Assert(mob.GetOwner()==1&&actor.StartedAttacks==0,"server-first test bootstraps report and transfers only after target evaluation");
            SoloSetup(context);mob.SetOwner(100);old.m_rpc.ReceiveSoloControl("start");actor=ai.GetComponent<Humanoid>();actor.BeginAttack();
            writes=mob.OwnerWrites;Time.realtimeSinceStartupAsDouble+=0.6;old.m_rpc.ReceiveSoloControl("stop");
            Assert(!FgnSoloCombatTest.Active&&mob.OwnerWrites==writes,"stop does not force an attacking server-owned mob back to the client");

            SoloSetup(context);ZNet.instance.Admin=false;old.m_rpc.ReceiveSoloControl("start");
            Assert(!FgnSoloCombatTest.Active&&old.m_rpc.SoloMessage.Contains("admin"),"unauthorized client cannot start test");
            SoloSetup(context);Assert(!FgnSoloCombatTest.Mode(old,11),"old readiness protocol cannot arm the revised solo test");
            Assert(!FgnSoloCombatTest.Mode(old,14),"previous no-mode arm protocol cannot negotiate revised vanilla comparison payload");
            SoloSetup(context);ZNet.instance.Peers.Add(other);old.m_rpc.ReceiveSoloControl("start");
            Assert(!FgnSoloCombatTest.Active,"test refuses to start with another connected player");
            SoloSetup(context);FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive=false;old.m_rpc.ReceiveSoloControl("start");
            Assert(!FgnSoloCombatTest.Active,"server simulation is required for alternating server/player ownership");
            SoloSetup(context);mob.Set(ZDOVars.s_tamed,1);old.m_rpc.ReceiveSoloControl("start");Assert(!FgnSoloCombatTest.Active,"tamed mob cannot be selected");
            SoloSetup(context);actor=ai.GetComponent<Humanoid>();actor.Boss=true;old.m_rpc.ReceiveSoloControl("start");Assert(!FgnSoloCombatTest.Active,"boss excluded from solo test");
            SoloSetup(context);actor=ai.GetComponent<Humanoid>();actor.Dead=true;old.m_rpc.ReceiveSoloControl("start");Assert(!FgnSoloCombatTest.Active,"dead mob excluded from solo test");

            SoloSetup(context);SoloStart(context);actor=ai.GetComponent<Humanoid>();Attack running=actor.BeginAttack();
            int readies=ZNet.instance.ServerPeer.m_rpc.SoloReadies;ai.UpdateAI(0.05f);
            Assert(ZNet.instance.ServerPeer.m_rpc.SoloReadies==readies&&mob.GetOwner()==1,"running attack blocks test preparation without requesting ownership");
            actor.Attack=false;running.Stop();ai.AttackAfterTarget=true;ai.UpdateAI(0.05f);
            Assert(ZNet.instance.ServerPeer.m_rpc.SoloReadies==readies+1&&actor.StartedAttacks==0,"first eligible post-attack target evaluation prepares exactly one guarded handoff");
            Server();writes=mob.OwnerWrites;ZNet.instance.Peers.Add(other);Time.realtimeSinceStartupAsDouble+=0.1;FgnSoloCombatTest.Poll();
            Assert(!FgnSoloCombatTest.Active&&mob.OwnerWrites==writes,"second player joining cancels test without forced ownership");

            SoloSetup(context);SoloStart(context);Server();actor=ai.GetComponent<Humanoid>();actor.Dead=true;Time.realtimeSinceStartupAsDouble+=0.1;FgnSoloCombatTest.Poll();
            Assert(!FgnSoloCombatTest.Active,"mob death stops test and removes its temporary hooks");
            SoloSetup(context);SoloStart(context);server=ZNet.instance.ServerPeer.m_rpc;int before=server.SoloReadies;
            mob.SetOwner(100);uint future=(uint)(mob.OwnerRevision+1);ZPackage arm=new ZPackage();arm.Write(10000L);arm.Write((long)future);arm.Write(100L);arm.Write(false);
            server.ReceiveSoloArm(mob.m_uid,arm);ai.UpdateAI(0.05f);
            Assert(server.SoloReadies==before,"arm before ownership replication waits without submitting readiness");
            mob.SetOwner(1);server.ReceiveWatch(mob.m_uid,1,(long)future);context.Flush();ai.UpdateAI(0.05f);
            Assert(server.SoloReadies==before+1,"matching replicated tuple wakes armed test on native target evaluation");
            SoloSetup(context);mob.SetOwner(100);old.m_rpc.ReceiveSoloControl("start");
            ai.Sleeping=true;ai.Target=null;ai.UpdateAI(0.05f);
            Assert(mob.GetOwner()==1&&ai.Sleeping,"completed sleeping AI is a safe idle test boundary without forcing it awake");

            SoloSetup(context);SoloStart(context);server=ZNet.instance.ServerPeer.m_rpc;before=server.SoloReadies;
            ai.Target=null;ai.UpdateAI(0.05f);
            Assert(server.SoloReadies==before+1,"idle source without a player target can prepare the synthetic test handoff");

            SoloSetup(context);SoloStart(context);server=ZNet.instance.ServerPeer.m_rpc;before=server.SoloReadies;
            ai.Target=new GameObject().Add(new Character());ai.UpdateAI(0.05f);
            Assert(server.SoloReadies==before+1,"a non-player enemy does not stall the synthetic test handoff");

            for(int kind=0;kind<2;kind++)
            {
                SoloSetup(context);SoloStart(context);server=ZNet.instance.ServerPeer.m_rpc;
                Character enemy=kind==0?null:new GameObject().Add(new Character());ai.Target=enemy;ai.UpdateAI(0.05f);
                ready=server.SoloReadyPayload.Rewind();ticket=ready.ReadLong();revision=ready.ReadLong();actualTarget=ready.ReadZDOID();
                Assert(actualTarget.IsNone()&&ready.ReadBool(),"no-player readiness is distinct from a failed source arm");ready.Rewind();
                Server();sent=old.m_rpc.Sent;Call("ReceiveTarget",old,mob.m_uid,ZDOID.None,(long)mob.OwnerRevision);
                old.m_rpc.ReceiveSoloReady(mob.m_uid,ready);
                Assert(old.m_rpc.Sent==sent+1&&old.m_rpc.Destination==100,"authenticated no-player readiness requests the normal guarded source commit");
                DeliverCommit();DeliverReply();Server();Time.realtimeSinceStartupAsDouble+=0.1;FgnSoloCombatTest.Poll();
                Time.realtimeSinceStartupAsDouble+=0.6;FgnSoloCombatTest.Poll();ai.UpdateAI(0.05f);
                Assert(mob.GetOwner()==1&&ReferenceEquals(ai.GetTargetCreature(),enemy),"idle/non-player test completes both handoff directions without changing its real target");
            }

            SoloSetup(context);mob.SetOwner(100);old.m_rpc.ReceiveSoloControl("start");
            var update=typeof(MonsterAI).GetMethod("UpdateAI");var cancel=typeof(FgnCombatTests).GetMethod("CancelFixtureStop",flags);
            hooks.Patch(update,prefix:new HarmonyMethod(cancel));ai.UpdateAI(0.05f);hooks.Unpatch(update,cancel);
            Assert(mob.GetOwner()==100,"a skipped native AI pass cannot establish diagnostic readiness");
            actor=ai.GetComponent<Humanoid>();actor.Attack=false;running=actor.BeginAttack();actor.Attack=false;
            ai.UpdateAI(0.05f);Assert(mob.GetOwner()==100,"an unfinished attack blocks the idle completion fallback");
            running.Stop();ai.UpdateAI(0.05f);Assert(mob.GetOwner()==1,"the diagnostic fallback releases only after the real unfinished attack ends");

            SoloSetup(context);SoloStart(context);server=ZNet.instance.ServerPeer.m_rpc;ai.UpdateAI(0.05f);
            ready=server.SoloReadyPayload.Rewind();ticket=ready.ReadLong();revision=ready.ReadLong();actualTarget=ready.ReadZDOID();
            var failed=new ZPackage();failed.Write(ticket);failed.Write(revision);failed.Write(ZDOID.None);failed.Write(false);
            Server();writes=mob.OwnerWrites;old.m_rpc.ReceiveSoloReady(mob.m_uid,failed);
            Assert(!FgnSoloCombatTest.Active&&mob.OwnerWrites==writes,"failed source arm stops the diagnostic instead of treating missing target as readiness");

            SoloSetup(context);old.m_rpc.ReceiveSoloControl("start 100");
            Assert(!FgnSoloCombatTest.Active&&old.m_rpc.LatencyRequests==0,"legacy solo capability alone cannot start artificial network latency");
            SoloSetup(context);old.m_rpc.Receive(17);old.m_rpc.ReceiveSoloControl("start 100");
            Assert(FgnSoloCombatTest.Active&&old.m_rpc.LatencyRequests==1&&old.m_rpc.SoloArms==0,"100ms mode waits for authenticated client queue setup before any handoff");
            int starts=SoloNetworkLatency.Starts;Client(1);server=ZNet.instance.ServerPeer.m_rpc;server.Receive(18);
            server.ReceiveLatency(old.m_rpc.LatencyPayload);server.ReceiveLatency(old.m_rpc.LatencyPayload);
            Assert(SoloNetworkLatency.Active&&SoloNetworkLatency.Starts==starts+1&&server.LatencyReplies==1,"server-authorized latency setup starts once; duplicate epoch cannot replace queues");
            var latencyReply=server.LatencyReply.Rewind();long epoch=latencyReply.ReadLong();
            var obsolete=new ZPackage();obsolete.Write(epoch+1);obsolete.Write(true);obsolete.Write(20);obsolete.Write(80);obsolete.Write("");
            Server();old.m_rpc.ReceiveLatencyReady(obsolete);Time.realtimeSinceStartupAsDouble+=.1;FgnSoloCombatTest.Poll();
            Assert(old.m_rpc.SoloArms==0,"wrong latency acknowledgement epoch cannot arm the selected mob");
            old.m_rpc.ReceiveLatencyReady(latencyReply);Time.realtimeSinceStartupAsDouble+=.1;FgnSoloCombatTest.Poll();
            Assert(old.m_rpc.SoloArms==1&&old.m_rpc.SoloMessage.Contains("target RTT=100"),"confirmed 100ms setup arms normal guarded test and reports target RTT");
            Time.realtimeSinceStartupAsDouble+=.6;old.m_rpc.ReceiveSoloControl("stop");Client(1);server.ReceiveSoloArm(old.m_rpc.SoloMob,old.m_rpc.SoloArmPayload);
            Assert(!FgnSoloCombatTest.Active&&!SoloNetworkLatency.Active,"authenticated stop clears mob test and restores native connection timing");
            SoloSetup(context);old.m_rpc.Receive(17);ZNet.instance.Admin=false;old.m_rpc.ReceiveSoloControl("start 100");
            Assert(!FgnSoloCombatTest.Active&&old.m_rpc.LatencyRequests==0,"non-admin cannot activate connection delay");
            SoloSetup(context);old.m_rpc.Receive(17);ZNet.instance.Peers.Add(other);old.m_rpc.ReceiveSoloControl("start 100");
            Assert(!FgnSoloCombatTest.Active&&old.m_rpc.LatencyRequests==0,"artificial delay refuses multiplayer sessions");
            SoloSetup(context);old.m_rpc.Receive(17);old.m_rpc.ReceiveSoloControl("start 1001");
            Assert(!FgnSoloCombatTest.Active&&old.m_rpc.LatencyRequests==0,"out-of-range RTT cannot start or allocate a session");
            SoloSetup(context);old.m_rpc.Receive(17);FiresGhettoNetworkMod.NetSim.Active=true;old.m_rpc.ReceiveSoloControl("start 100");FiresGhettoNetworkMod.NetSim.Active=false;
            Assert(!FgnSoloCombatTest.Active&&old.m_rpc.LatencyRequests==0,"already active native FGN network simulator cannot silently double the server test delay");
            SoloSetup(context);old.m_rpc.Receive(17);old.m_rpc.ReceiveSoloControl("start 100");Client(1);server=ZNet.instance.ServerPeer.m_rpc;server.Receive(18);
            starts=SoloNetworkLatency.Starts;FiresGhettoNetworkMod.NetSim.Active=true;server.ReceiveLatency(old.m_rpc.LatencyPayload);FiresGhettoNetworkMod.NetSim.Active=false;
            Server();old.m_rpc.ReceiveLatencyReady(server.LatencyReply);
            Assert(!FgnSoloCombatTest.Active&&SoloNetworkLatency.Starts==starts,"already active client FGN simulator refuses setup before any extra delay queue starts");
            SoloSetup(context);old.m_rpc.Receive(17);old.m_rpc.ReceiveSoloControl("start 100");Time.realtimeSinceStartupAsDouble+=5.1;FgnSoloCombatTest.Poll();
            Assert(!FgnSoloCombatTest.Active,"missing client latency acknowledgement has a five-second setup timeout");
            SoloSetup(context);old.m_rpc.Receive(17);old.m_rpc.ReceiveSoloControl("start 100");Client(1);server=ZNet.instance.ServerPeer.m_rpc;server.Receive(18);SoloNetworkLatency.Fail=true;
            server.ReceiveLatency(old.m_rpc.LatencyPayload);Server();old.m_rpc.ReceiveLatencyReady(server.LatencyReply);SoloNetworkLatency.Fail=false;
            Assert(!FgnSoloCombatTest.Active&&!SoloNetworkLatency.Active,"failed native latency preflight stops diagnostic without forcing an owner change");

            SoloSetup(context);SoloStart(context);Server();Time.realtimeSinceStartupAsDouble+=301;FgnSoloCombatTest.Poll();Assert(!FgnSoloCombatTest.Active,"five-minute timeout is bounded");
            SoloSetup(context);SoloStart(context);Server();Call("Reset");Assert(!FgnSoloCombatTest.Active,"world cleanup drops test session and references");

            SoloSetup(context);FgnSoloCombatTest.Register();
            Assert(Terminal.ConsoleCommand.Commands.ContainsKey("ai_test")&&Terminal.ConsoleCommand.Commands.ContainsKey("ai_test_v")&&
                Terminal.ConsoleCommand.Commands.ContainsKey("ai_test_vanilla"),"short and vanilla aliases register actual console callbacks");
            Client(1);server=ZNet.instance.ServerPeer.m_rpc;server.Receive(18);
            Terminal.ConsoleCommand.Commands["ai_test"](new Terminal.ConsoleEventArgs{Context=new Terminal(),Args=new[]{"ai_test"}});
            Assert(server.SoloControl=="toggle","no-argument console command delegates toggle to authoritative server state");
            Terminal.ConsoleCommand.Commands["ai_test_v"](new Terminal.ConsoleEventArgs{Context=new Terminal(),Args=new[]{"ai_test_v","100","1.5"}});
            Assert(server.SoloControl=="start_vanilla 100 1.5","numeric-first vanilla command sends latency and interval unchanged");
            SoloSetup(context);old.m_rpc.ReceiveSoloControl("toggle");Assert(FgnSoloCombatTest.Active,"server toggle starts inactive test");
            writes=mob.OwnerWrites;Time.realtimeSinceStartupAsDouble+=.6;old.m_rpc.ReceiveSoloControl("toggle");
            Assert(!FgnSoloCombatTest.Active&&mob.OwnerWrites==writes,"server toggle stops active test without forced ownership");

            SoloSetup(context);mob.SetOwner(100);old.m_rpc.ReceiveSoloControl("start_vanilla 0 0.7");
            Assert(FgnSoloCombatTest.Active&&FgnSoloCombatTest.Vanilla(mob)&&old.m_rpc.SoloModes==1&&old.m_rpc.SoloArms==0,
                "server-first vanilla session announces mode before first transfer");
            Assert(MobFlightHandoff.NativeTransfer(mob)&&MobSourceHandoff.SkipLocalCommit(mob)&&MobHandoffVisual.SkipPresentation(mob),
                "vanilla mode gates extra checkpoints, local-commit extension and legacy presentation");
            var unrelated=new ZDO(new ZDOID(2000,99),100);
            Assert(!FgnSoloCombatTest.Vanilla(unrelated)&&!MobFlightHandoff.NativeTransfer(unrelated),"vanilla comparison remains scoped to selected mob");
            Client(1);server=ZNet.instance.ServerPeer.m_rpc;server.ReceiveSoloArm(old.m_rpc.SoloModeMob,old.m_rpc.SoloModePayload);
            Assert(FgnSoloCombatTest.Vanilla(mob),"client learns vanilla mode before gaining ownership, without an arm or latency acknowledgement");
            server.ReceiveSoloArm(old.m_rpc.SoloModeMob,old.m_rpc.SoloModePayload);
            Assert(FgnSoloCombatTest.Vanilla(mob),"duplicate session announcement cannot reset active mode");
            Server();ai.UpdateAI(.05f);Assert(mob.GetOwner()==1,"vanilla comparison retains guarded server-to-player transfer");
            Time.realtimeSinceStartupAsDouble+=.1;FgnSoloCombatTest.Poll();int arms=old.m_rpc.SoloArms;
            Time.realtimeSinceStartupAsDouble+=.65;FgnSoloCombatTest.Poll();Assert(old.m_rpc.SoloArms==arms,"vanilla mode respects custom completion-relative interval");
            Time.realtimeSinceStartupAsDouble+=.1;FgnSoloCombatTest.Poll();
            Assert(old.m_rpc.SoloArms==arms+1,"vanilla mode arms next transfer at same requested cadence");
            Client(1);server=ZNet.instance.ServerPeer.m_rpc;server.ReceiveWatch(mob.m_uid,1,(long)mob.OwnerRevision);context.Flush();
            Server();Call("ReceiveTarget",old,mob.m_uid,old.m_characterID,(long)mob.OwnerRevision);
            Client(1);server.ReceiveSoloArm(old.m_rpc.SoloMob,old.m_rpc.SoloArmPayload);ai.UpdateAI(.05f);
            Assert(server.SoloReadies>0&&FgnSoloCombatTest.Vanilla(mob),"vanilla source stays scoped while requesting native handoff");
            Server();old.m_rpc.ReceiveSoloReady(mob.m_uid,server.SoloReadyPayload);DeliverCommit();DeliverReply();
            Assert(mob.GetOwner()==100,"vanilla comparison completes player-to-server transfer");
            Time.realtimeSinceStartupAsDouble+=.6;old.m_rpc.ReceiveSoloControl("toggle_vanilla");
            Client(1);server.ReceiveSoloArm(old.m_rpc.SoloMob,old.m_rpc.SoloArmPayload);
            Assert(!FgnSoloCombatTest.Active&&!FgnSoloCombatTest.Vanilla(mob)&&MobFlightHandoff.NativeTransfer==null&&
                MobSourceHandoff.SkipLocalCommit==null&&MobHandoffVisual.SkipPresentation==null,"stop removes scoped vanilla state and all temporary bypass delegates");

            SoloSetup(context);old.m_rpc.Receive(17);old.m_rpc.ReceiveSoloControl("start_vanilla 500 1.5");
            Assert(FgnSoloCombatTest.Active&&FgnSoloCombatTest.Vanilla(mob)&&old.m_rpc.LatencyRequests==1&&old.m_rpc.SoloArms==0,
                "vanilla comparison retains real artificial-latency handshake before arming");
            Server();Time.realtimeSinceStartupAsDouble+=.6;old.m_rpc.ReceiveSoloControl("toggle_vanilla");
        }
        finally{FgnSoloCombatTest.Reset();hooks.UnpatchSelf();Call("Reset");System.Threading.SynchronizationContext.SetSynchronizationContext(previous);}
    }
}
#endif
