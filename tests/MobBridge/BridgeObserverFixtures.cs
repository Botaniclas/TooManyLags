using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags
{
    internal static class FgnSoloCombatTest
    {
        internal static bool Native;
        internal static bool Vanilla(ZDO data) { return Native && data != null; }
    }
    internal static class MobObserverHandoff { internal static Func<ZDO, bool> RecordedActive; }
    internal static class MobHandoffVisual
    {
        internal static int Cancelled;
        internal static void Cancel(ZDOID id) { ++Cancelled; }
    }
    internal static class MobBridgeProtocol { internal const double ClockInterval = .05d; }
    internal sealed class MobBridgeAnimation
    {
        internal readonly MobBridgeAnimationHistory History = new MobBridgeAnimationHistory(2);
        internal int BoneCount { get { return 2; } }
        internal string[] RigPaths() { return new[] { "", "4:Head:0/" }; }
    }
    internal static class MobBridgeVisual
    {
        internal static int Starts, Appends, Clocks;
        internal static bool Loaded = true;
        internal static readonly List<MobBridgeObserverPacket.Sample> Received = new List<MobBridgeObserverPacket.Sample>();
        internal static bool StartObserver(ZDO data, MobBridgeObserverPacket descriptor, IList<MobBridgeObserverPacket.Sample> samples, bool closed)
        { if (!Loaded) return false; ++Starts; Received.AddRange(samples); return true; }
        internal static void AppendObserver(ZDOID id, uint revision, uint token, IList<MobBridgeObserverPacket.Sample> samples, bool closed)
        { ++Appends; Received.AddRange(samples); }
        internal static void ObserveClock(ZDOID id, uint revision, uint token, long tick, float simulation, float sample,
            Vector3 position, Quaternion rotation, Vector3 velocity, float attackTime, string attack, float ai, double received,
            bool attackActive = false)
        { ++Clocks; }
    }
}
