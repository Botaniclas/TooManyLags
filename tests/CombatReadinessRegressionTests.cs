using System;
using TooManyLags;
using UnityEngine;

internal static partial class FgnCombatTests
{
    private static void MobReadinessRegressions()
    {
        bool moved;
        try
        {
            Setup();target.Ping=400;int calls=0;
            FgnCombatOwnership.ServerCollisionReady=delegate {calls++;return false;};
            Assert(!Handle(out moved)&&!moved&&old.m_rpc.Sent==0&&mob.GetOwner()==1,"unprepared server never steals running client AI");
            Assert(calls==1,"server collision proof occurs only at real transfer request");
            FgnCombatOwnership.ServerCollisionReady=delegate{return true;};
            Assert(!Handle(out moved)&&moved&&old.m_rpc.Destination==100,"local collision preparation allows guarded server takeover");
            ServerAccept();target.Ping=50;FgnCombatOwnership.ServerCollisionReady=delegate{return false;};
            Assert(!Handle(out moved)&&moved&&mob.GetOwner()==2,"server collider wait never blocks safe escape to target player");
            FgnCombatOwnership.ServerCollisionReady=null;

            Setup();Set("serverDefaultsConfigured",true);Client(1);ai.Target=null;
            // The previous valid target report remains valid for this owner:
            // a real subsequent loss must still be delivered immediately.
            FgnCombatOwnership.Observe(ai);Server();
            Assert(!Handle(out moved)&&moved,"real target loss after valid owner report still requests configured server default");

            Setup();Set("serverDefaultsConfigured",true);mob.SetOwner(3);Server();Call("MarkWatch",mob);Client(3);
            ai.Target=null;FgnCombatOwnership.Observe(ai);Server();
            int sent=other.m_rpc.Sent;
            Assert(!Handle(out moved)&&!moved&&other.m_rpc.Sent==sent,"new-owner bootstrap None cannot create periodic server ownership flicker");
            Client(3);FgnCombatOwnership.CompletedAi(ai);FgnCombatOwnership.Observe(ai);Server();Ack();
            Assert(!Handle(out moved)&&moved&&other.m_rpc.Destination==100,"completed idle AI supplies real no-target server default");
        }
        finally { FgnCombatOwnership.ServerCollisionReady=null; }
    }
}
