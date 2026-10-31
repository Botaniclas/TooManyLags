using System;
using System.Reflection;
using TooManyLags;
using UnityEngine;

internal sealed class GuardPupFixture:PupReadinessFixture
{
    public sealed class ZoneBodyGuard:MonoBehaviour
    {
        internal bool held=true;internal int Calls;
        private void PrepareDedicatedCollision(){Calls++;}
    }
    private bool PrepareMobCollision(Character actor){return false;}
}
internal static class MobCollisionGateTests
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    internal static void Run()
    {
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        Action<Character> before=unit=>typeof(MobCollisionReadiness).GetMethod("BeforeMob",flags).Invoke(null,new object[]{unit});
#if TML_DEBUG_SYSTEM
        Action<Character,bool> after=(unit,result)=>typeof(MobCollisionReadiness).GetMethod("AfterMob",flags).Invoke(null,new object[]{unit,result});
#else
        Check(typeof(MobCollisionReadiness).GetMethod("AfterMob",flags)==null,"normal build contains no test-only PUP postfix");
        Check(typeof(MobCollisionReadiness).GetField("traced",flags)==null,"normal build contains no selected-mob trace state");
#endif
        ZNet.instance=new ZNet{Dedicated=true,Server=true};ZNet.IsSinglePlayer=false;
        MobCollisionReadiness.Start(null);
        Check(FgnCombatOwnership.ServerCollisionReady(new Vector3()),"no PUP has no readiness cost or guard requirement");MobCollisionReadiness.Stop();
        GuardPupFixture pup=new GuardPupFixture();MobCollisionReadiness.Start(pup);
        var root=new GameObject();var actor=root.Add(new Character());var view=root.Add(new ZNetView());var guard=root.Add(new GuardPupFixture.ZoneBodyGuard());
        before(actor);Check(guard.Calls==0,"no player nearby adds no preparation");
        ZNet.instance.Peers.Add(new ZNetPeer{Position=new Vector3(1000,0,1000)});before(actor);
        Check(guard.Calls==0,"faraway player's area cannot request this mob preparation");
        ZNet.instance.Peers[0].Position=new Vector3(5,0,5);before(actor);
        Check(guard.Calls==1&&guard.held,"near held mob uses native preparation without releasing native hold");
        guard.held=false;before(actor);Check(guard.Calls==1,"unblocked steady mob does no preparation");
        guard.held=true;view.Owned=false;before(actor);Check(guard.Calls==1,"passive replica does no preparation");view.Owned=true;
        ZNet.instance.Dedicated=false;before(actor);Check(guard.Calls==1,"clients retain native scheduling");ZNet.instance.Dedicated=true;
        typeof(MobCollisionReadiness).GetField("spent",flags).SetValue(null,System.Diagnostics.Stopwatch.Frequency);
        before(actor);Check(guard.Calls==1,"spent frame budget does not force more preparation");
        Time.frameCount++;before(actor);Check(guard.Calls==2,"next frame restores bounded preparation opportunity");
#if TML_DEBUG_SYSTEM
        FgnSoloCombatTest.Chosen=view.Data;after(actor,false);
        Check(FgnSoloCombatTest.CollisionStatus(view.Data).Contains("no current-owner sample"),"inactive test gathers no PUP diagnostics even for a formerly selected mob");
        FgnSoloCombatTest.Active=true;after(actor,false);string status=FgnSoloCombatTest.CollisionStatus(view.Data);
        Check(status.Contains("PUP allowed=False")&&status.Contains("PUP blocked=1"),"selected mob reports actual PUP refusal independently of attack state");
        after(actor,true);status=FgnSoloCombatTest.CollisionStatus(view.Data);
        Check(status.Contains("PUP allowed=True")&&status.Contains("PUP blocked=1"),"successful native result updates trace without clearing refusal count");
        view.Data.OwnerRevision++;status=FgnSoloCombatTest.CollisionStatus(view.Data);
        Check(status.Contains("no current-owner sample"),"old-owner PUP result cannot be mistaken for current owner's readiness");
        after(actor,false);Time.realtimeSinceStartupAsDouble+=3;status=FgnSoloCombatTest.CollisionStatus(view.Data);
        Check(status.Contains("PUP calls=1")&&status.Contains("sample age=3.0s"),"new ownership starts fresh counters and stale samples show their age");
#endif
        pup.Distant();Check(FgnCombatOwnership.ServerCollisionReady(new Vector3(10,0,10)),"distant PUP world records do not block new local server takeover");
        pup.StaleSession();Check(!FgnCombatOwnership.ServerCollisionReady(new Vector3(10,0,10)),"unknown session still blocks readiness, never bypassed as empty wilderness");
        MobCollisionReadiness.Stop();Check(FgnCombatOwnership.ServerCollisionReady==null&&AuthorityController.MobServerReady==null,"stop releases production readiness delegates");
#if TML_DEBUG_SYSTEM
        Check(FgnSoloCombatTest.CollisionStatus==null,"stop releases test-only diagnostics");
#endif
        Console.WriteLine("PASS: "+checks+" local mob preparation, bounded budget, no forced release, server/client scope and PUP diagnostics assertions.");
    }
}
