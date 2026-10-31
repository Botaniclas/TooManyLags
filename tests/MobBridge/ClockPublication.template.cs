using System;
using TooManyLags;
using UnityEngine;
namespace UnityEngine { internal static class Time { internal static float fixedTime; } }
internal static class ClockPublicationTests
{
    private sealed class ClockEpoch
    {
        internal float Simulation, GameStarted, Horizon, AttackTime = -1f, AiSimulation;
        internal long Tick;
        internal double FirstWall;
        internal bool Valid = true, FailSend, AttackActive = false;
        internal Humanoid Actor = null;
        internal MobBridgeClockCadence Cadence;
    }
    private sealed class Humanoid
    {
        internal bool Attack = false;
        internal bool InAttack() { return Attack; }
    }
    private static int checks, sends;
    private static float sentSimulation, sentAi, sentSample;
    private static long sentTick;
    private static bool ClockValid(ClockEpoch clock, double now) { return clock.Valid; }
    private static void SendClock(ClockEpoch clock, float sample, double now)
    {
        if(clock.FailSend) return;
        ++sends;sentSimulation=clock.Simulation;sentTick=clock.Tick;sentAi=clock.AiSimulation;sentSample=sample;
        clock.Cadence.Sent(now,clock.AttackTime,clock.Simulation,clock.Horizon,clock.Tick);
    }
    @@PUBLICATION@@
    private static void Check(bool valid, string message) { ++checks;if(!valid)throw new Exception(message); }
    internal static void Run()
    {
        var clock=new ClockEpoch { GameStarted=100,FirstWall=10,Horizon=.05f };
        clock.Cadence.Sent(10,-1,0,.05f);sends=0;
        Time.fixedTime=100;PublishPhysicsClock(clock,10.001);
        Check(sends==0,"no physics completed but a clock was sent");
        clock.Tick=1;clock.AiSimulation=.05f;PublishPhysicsClock(clock,10.002);
        Check(sends==0&&clock.Simulation==0,"AI pass invented completed physics");
        Time.fixedTime=100.02f;PublishPhysicsClock(clock,10.02);
        Check(sends==1&&Math.Abs(sentSimulation-.02f)<.00001f,"first completed native step still waits for AI callback");
        Check(sentTick==1&&sentAi==.05f&&Math.Abs(sentSample-.02f)<.00001f,"physics notification altered AI pass/dt/wall metadata");
        PublishPhysicsClock(clock,10.021);Check(sends==1,"same native step emitted twice");
        Time.fixedTime=100.04f;PublishPhysicsClock(clock,10.04);
        Check(sends==2&&Math.Abs(sentSimulation-.04f)<.00001f,"short-window next native step withheld");
        Check(sentTick==1&&sentAi==.05f,"body-only notification counted a new AI decision");
        clock.Valid=false;Time.fixedTime=100.06f;PublishPhysicsClock(clock,10.06);
        Check(sends==2,"invalid/replaced owner epoch published physics");
        clock.Valid=true;clock.FailSend=true;PublishPhysicsClock(clock,10.06);
        Check(sends==2,"failed send committed publication");
        clock.FailSend=false;PublishPhysicsClock(clock,10.061);
        Check(sends==3&&Math.Abs(sentSimulation-.06f)<.00001f,"failed endpoint publication could not retry");
        PublishPhysicsClock(clock,10.062);Check(sends==3,"completed horizon event repeated");
        Time.fixedTime=100.08f;PublishPhysicsClock(clock,10.08);
        Check(sends==3,"fine tail notifications continued beyond matching window");
        Time.fixedTime=100.12f;PublishPhysicsClock(clock,10.12);
        Check(sends==4,"regular publication stopped after horizon event");
        Time.fixedTime=100.10f;PublishPhysicsClock(clock,10.121);
        Check(clock.Simulation>=.11999f&&sends==4,"body clock rewound or resent a backward time sample");
        Console.WriteLine("PASS: "+checks+" extracted actual completed-physics publication checks; managed clock/transport seam.");
    }
}
