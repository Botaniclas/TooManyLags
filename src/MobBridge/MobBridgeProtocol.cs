#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NativeMobPreviewAudit;
using UnityEngine;

namespace TooManyLags
{
    // One bounded simulation checkpoint per ownership edge. It travels before
    // the final owner write through the client/server relay and is applied before
    // the destination's next AI pass.
    internal static class MobBridgeProtocol
    {
        internal const double ClockInterval = MobBridgeClockCadence.Interval;
        private const string CapabilityRpc = "TooManyLags_MobBridgeCapability";
        private const string StateRpc = "TooManyLags_MobBridgeState";
        private const string ClockRpc = "TooManyLags_MobBridgeClock";
        private const string FinishRpc = "TooManyLags_MobBridgeFinish";
        private const string RateRpc = "TooManyLags_MobBridgeRate";
        private const string DiagnosticRpc = "TooManyLags_MobBridgeDiagnostic";
        private const int Protocol = 7, Limit = 256, MaxBytes = 16384;
        private const double Lifetime = 5.0;
        private sealed class Capability { internal bool Ready, Registered; }
        private sealed class Pending
        {
            internal ZDO Data; internal MobBridgeCheckpoint State;
            internal long Source, Destination; internal uint Revision;
            internal Vector3 Origin; internal Quaternion Rotation; internal Vector3 Velocity;
            internal int Prefab; internal double Until, ReceivedAt; internal byte ReleaseStage;
        }
        private struct OwnerPose
        {
            internal ZDOID Id; internal Humanoid Actor;
            internal Vector3 Origin, Velocity, DisplayedVelocity; internal Quaternion Rotation; internal double Until;
            internal Vector3 VisualPosition; internal Quaternion VisualRotation; internal float GainSeconds;
        }
        private sealed class Outgoing
        {
            internal ZDO Data; internal MonsterAI Ai; internal Humanoid Actor; internal Character Target;
            internal MobBridgeSeed Seed; internal Vector3 Origin, Velocity;
            internal Quaternion Rotation; internal float Latency; internal long Destination; internal double Until;
            internal float InitialRealRate, InitialAiRate; internal uint Revision; internal MobStatusState.Shadow Status;
            internal MobBridgeTiming Timing;
        }
        private sealed class FinishNotice
        {
            internal long Destination; internal uint Revision, Token; internal bool Final;
        }
        private sealed class RandomEpoch
        {
            internal ZDOID Id; internal UnityEngine.Random.State State;
            internal long Destination; internal uint Revision;
            internal float Remaining; internal double Until;
        }
        private sealed class TargetEpoch
        {
            internal Character Target; internal long Destination; internal uint Revision;
            internal float Remaining; internal double Until;
        }
        private struct RandomScope
        {
            internal bool Active; internal UnityEngine.Random.State Original;
            internal ClockEpoch Clock; internal RandomEpoch Random;
            internal float Delta; internal double Wall;
        }
        private sealed class ClockEpoch
        {
            internal ZDOID Id; internal long Source, Destination; internal uint Revision, Token;
            internal long Tick; internal float Simulation, AiSimulation, GameStarted, Horizon; internal double FirstWall;
            internal double Until;
            internal MobBridgeClockCadence Cadence;
            internal bool MatchActive, PresentedSource;
            internal float DecisionDelta;
            internal float AttackTime = -1f; internal string Attack = "";
            internal bool AttackActive;
            internal Humanoid Actor;
            internal Rigidbody Body;
            internal bool ReadyMeasured;
            internal float Startup;
        }
        private sealed class PeerRate
        {
            internal float Ratio, AiRatio, Step, Frame, Ready, FirstProgress, Startup; internal double Received;
        }
        private static readonly Dictionary<ZDOID, FinishNotice> finishes = new Dictionary<ZDOID, FinishNotice>();
        private static readonly Dictionary<ZDOID, Pending> pending = new Dictionary<ZDOID, Pending>();
        private static Dictionary<ZSyncTransform, OwnerPose> ownerPoses;
        private static readonly List<ZSyncTransform> ownerPoseKeys = new List<ZSyncTransform>(16);
        private static readonly Dictionary<ZDOID, Outgoing> outgoing = new Dictionary<ZDOID, Outgoing>();
        private static readonly Dictionary<ZDOID, RandomEpoch> random = new Dictionary<ZDOID, RandomEpoch>();
        private static readonly Dictionary<ZDOID, TargetEpoch> targets = new Dictionary<ZDOID, TargetEpoch>();
        private static readonly Dictionary<ZDOID, ClockEpoch> clocks = new Dictionary<ZDOID, ClockEpoch>();
        private static readonly List<ClockEpoch> clockSnapshot = new List<ClockEpoch>();
        private static readonly Dictionary<long, PeerRate> rates = new Dictionary<long, PeerRate>();
        private static readonly PeerRate localTimingCache = new PeerRate();
        private static readonly List<ZDOID> ids = new List<ZDOID>();
        private static readonly List<long> rateIds = new List<long>();
        private static ConditionalWeakTable<ZRpc, Capability> capabilities = new ConditionalWeakTable<ZRpc, Capability>();
        private static Harmony hooks;
        private static Harmony receiveHooks, aiHooks;
        private static NativeSteeringPlan plan;
        private static ZDOMan world;
        private static ZNet network;
        private static bool tried;
        private static float localRate = 1f, localAiRate = 1f;
        private static double lastRateWall, nextRateAt;
        private static float lastRateSimulation;
        private static float localReady, localFirstProgress, localReadyRate, localReadyStep, localStartup;
        private static double localReadyAt;
        private static Func<ZSyncTransform, bool> syncWasOwner;
        private static readonly Func<MonsterAI, Character> targetRead =
            FieldAccess.Reader<MonsterAI, Character>(AccessTools.Field(typeof(MonsterAI), "m_targetCreature"));
        private static readonly Action<MonsterAI, Character> targetWrite =
            FieldAccess.Writer<MonsterAI, Character>(AccessTools.Field(typeof(MonsterAI), "m_targetCreature"));
        private static readonly Action<MonsterAI, StaticTarget> staticTargetWrite =
            FieldAccess.Writer<MonsterAI, StaticTarget>(AccessTools.Field(typeof(MonsterAI), "m_targetStatic"));
        private static readonly Func<MonsterAI, float> updateTargetRead =
            FieldAccess.Reader<MonsterAI, float>(AccessTools.Field(typeof(MonsterAI), "m_updateTargetTimer"));
        private static readonly Action<MonsterAI, float> updateTargetWrite =
            FieldAccess.Writer<MonsterAI, float>(AccessTools.Field(typeof(MonsterAI), "m_updateTargetTimer"));
        private static readonly Func<MonsterAI, float> unableTargetRead =
            FieldAccess.Reader<MonsterAI, float>(AccessTools.Field(typeof(MonsterAI), "m_unableToAttackTargetTimer"));
        private static readonly Action<MonsterAI, float> unableTargetWrite =
            FieldAccess.Writer<MonsterAI, float>(AccessTools.Field(typeof(MonsterAI), "m_unableToAttackTargetTimer"));
        private static readonly Func<MonsterAI, float> sensedRead =
            FieldAccess.Reader<MonsterAI, float>(AccessTools.Field(typeof(MonsterAI), "m_timeSinceSensedTargetCreature"));
        private static readonly Action<MonsterAI, float> sensedWrite =
            FieldAccess.Writer<MonsterAI, float>(AccessTools.Field(typeof(MonsterAI), "m_timeSinceSensedTargetCreature"));
        private static readonly Func<MonsterAI, float> attackingRead =
            FieldAccess.Reader<MonsterAI, float>(AccessTools.Field(typeof(MonsterAI), "m_timeSinceAttacking"));
        private static readonly Action<MonsterAI, float> attackingWrite =
            FieldAccess.Writer<MonsterAI, float>(AccessTools.Field(typeof(MonsterAI), "m_timeSinceAttacking"));
        internal static NativeSteeringPlan Plan { get { return plan; } }
        internal static float GameRate { get { return localRate; } }
        internal static float FrameSeconds { get { return Math.Max(.001f, Time.smoothDeltaTime / Math.Max(.1f, localRate)); } }
        internal static bool Started { get { return hooks != null; } }
        internal static bool HasActive
        {
            get { return finishes.Count != 0 || pending.Count != 0 || outgoing.Count != 0 || random.Count != 0 ||
                targets.Count != 0 || clocks.Count != 0 || MobBridgeObserver.Active; }
        }

        internal static bool Prepare()
        {
            if (tried) return plan != null;
            tried = true;
            try { plan = new NativeSteeringPlan(); }
            catch (Exception error)
            {
                Plugin.Report("Mob bridge simulation unavailable; .86 transfer retained: " + error.GetBaseException().Message);
            }
            return plan != null;
        }

        internal static void Start()
        {
            Stop();
            if (!Prepare()) return;
            try
            {
                hooks = new Harmony(Plugin.Id + ".mobbridge");
                hooks.Patch(AccessTools.Method(typeof(ZNet), "OnNewConnection"),
                    postfix: new HarmonyMethod(typeof(MobBridgeProtocol), "Connected"));
                hooks.Patch(AccessTools.Method(typeof(ZNet), "RPC_PeerInfo"),
                    postfix: new HarmonyMethod(typeof(MobBridgeProtocol), "PeerReady"));
                hooks.Patch(AccessTools.Method(typeof(ZNet), "Shutdown"),
                    prefix: new HarmonyMethod(typeof(MobBridgeProtocol), "Shutdown"));
                Context();
                MobBridgeObserver.Start();
                if (network != null) foreach (ZNetPeer peer in network.GetPeers()) Connected(peer);
                MobFlightHandoff.SimulationTransfer = BeforeTransfer;
                MobFlightHandoff.SimulationTransferred = Transferred;
                MobBridgeWarmup.Begin();
#if TML_DEBUG_SYSTEM
                FgnSoloCombatTest.VanillaStarted = Cancel;
#endif
                Plugin.TestLog("bridge protocol ready");
            }
            catch (Exception error)
            {
                Stop();
                Plugin.Report("Mob bridge protocol unavailable; .86 transfer retained: " + error.GetBaseException().Message);
            }
        }

        internal static void Stop()
        {
            MobBridgeObserver.Stop();
#if TML_DEBUG_SYSTEM
            FgnSoloCombatTest.VanillaStarted = null;
#endif
            DisableReceiveHook(); DisableAiHook();
            if (hooks != null) { hooks.UnpatchSelf(); hooks = null; }
            MobFlightHandoff.SimulationTransfer = null;
            MobFlightHandoff.SimulationTransferred = null;
            MobBridgeVisual.EndSession();
            MobBridgeWarmup.Cancel();
            ClearOwnerPoses();
            finishes.Clear(); pending.Clear(); outgoing.Clear(); random.Clear(); targets.Clear(); clocks.Clear(); rates.Clear(); ids.Clear();
            capabilities = new ConditionalWeakTable<ZRpc, Capability>();
            world = null; network = null;
        }

        private static void Shutdown()
        {
            MobBridgeObserver.Stop();
            MobBridgeVisual.EndSession(); ClearOwnerPoses(); DisableReceiveHook(); DisableAiHook();
            finishes.Clear(); pending.Clear(); outgoing.Clear(); random.Clear(); targets.Clear(); clocks.Clear(); rates.Clear(); ids.Clear(); world = null; network = null;
        }

        internal static void Maintain()
        {
            if (!Started) return;
            FlushFinishes();
            MobBridgeObserver.Tick();
            double now = Time.realtimeSinceStartupAsDouble;
            ids.Clear();
            foreach (KeyValuePair<ZDOID, RandomEpoch> pair in random)
                if (!Matching(pair.Key) && (pair.Value.Until < now || pair.Value.Remaining <= 0f)) ids.Add(pair.Key);
            foreach (ZDOID id in ids) random.Remove(id);
            ids.Clear();
            foreach (KeyValuePair<ZDOID, TargetEpoch> pair in targets)
                if ((!Matching(pair.Key) && (pair.Value.Until < now || pair.Value.Remaining <= 0f)) ||
                    pair.Value.Target == null || pair.Value.Target.IsDead()) ids.Add(pair.Key);
            foreach (ZDOID id in ids) targets.Remove(id);
            ids.Clear();
            clockSnapshot.Clear();
            foreach (KeyValuePair<ZDOID, ClockEpoch> pair in clocks)
                if (!ClockValid(pair.Value, now)) ids.Add(pair.Key);
                else clockSnapshot.Add(pair.Value);
            foreach (ZDOID id in ids) { clocks.Remove(id); random.Remove(id); targets.Remove(id); }
            try
            {
                // Publishing may finish/cancel an epoch through transport
                // callbacks. Never mutate a live dictionary enumerator.
                for (int i = 0; i < clockSnapshot.Count; ++i)
                {
                    ClockEpoch current;
                    if (clocks.TryGetValue(clockSnapshot[i].Id, out current) && ReferenceEquals(current, clockSnapshot[i]))
                        PublishPhysicsClock(current, now);
                }
            }
            finally { clockSnapshot.Clear(); }
            ids.Clear();
            foreach (KeyValuePair<ZDOID, Outgoing> pair in outgoing)
                if (pair.Value.Until < now || pair.Value.Data == null || !pair.Value.Data.IsValid()) ids.Add(pair.Key);
            foreach (ZDOID id in ids) outgoing.Remove(id);
            rateIds.Clear();
            foreach (KeyValuePair<long, PeerRate> pair in rates)
                if (pair.Value.Received < now - 2d) rateIds.Add(pair.Key);
            foreach (long item in rateIds) rates.Remove(item);
            // Keep wrappers for this enabled world session. Rebuilding Harmony's
            // large AI methods at every ownership edge costs entire frames.
            // Empty-epoch guards below bypass all actor/Unity lookups.
        }

        private static void EnsureReceiveHook()
        {
            if (receiveHooks != null) return;
            receiveHooks = new Harmony(Plugin.Id + ".mobbridge.receive");
            receiveHooks.Patch(AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData"),
                postfix: new HarmonyMethod(typeof(MobBridgeProtocol), "Replicated"));
        }

        private static void DisableReceiveHook()
        {
            if (receiveHooks == null) return;
            receiveHooks.UnpatchSelf(); receiveHooks = null;
        }

        private static void EnsureAiHook()
        {
            if (aiHooks != null) return;
            Harmony candidate = new Harmony(Plugin.Id + ".mobbridge.ai");
            try
            {
                candidate.Patch(AccessTools.Method(typeof(MonsterAI), "UpdateAI", new[] { typeof(float) }),
                    prefix: new HarmonyMethod(typeof(MobBridgeProtocol), "BeforeRandom"),
                    finalizer: new HarmonyMethod(typeof(MobBridgeProtocol), "AfterRandom"));
                candidate.Patch(AccessTools.Method(typeof(MonsterAI), "DoAttack"),
                    postfix: new HarmonyMethod(typeof(MobBridgeProtocol), "AfterAttack"));
                candidate.Patch(AccessTools.Method(typeof(MonsterAI), "UpdateTarget"),
                    prefix: new HarmonyMethod(typeof(MobBridgeProtocol), "BeforeTarget"));
                aiHooks = candidate;
            }
            catch
            {
                candidate.UnpatchSelf();
                throw;
            }
        }

        private static void DisableAiHook()
        {
            if (aiHooks == null) return;
            aiHooks.UnpatchSelf(); aiHooks = null;
        }

        private static void Context()
        {
            if (ZDOMan.instance != null && ZNet.instance != null &&
                ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance)) return;
            MobBridgeVisual.EndSession(); DisableReceiveHook(); DisableAiHook();
            ClearOwnerPoses();
            finishes.Clear(); pending.Clear(); outgoing.Clear(); random.Clear(); targets.Clear(); clocks.Clear(); rates.Clear(); ids.Clear();
            world = ZDOMan.instance; network = ZNet.instance;
            if (hooks != null && world != null && network != null)
            {
                MobBridgeCost cost = null;
#if TML_BRIDGE_DIAGNOSTICS
                cost = new MobBridgeCost(ZDOID.None, "session");
#endif
                try
                {
                    EnsureReceiveHook(); EnsureAiHook(); cost.Mark("protocolHooks");
                    MobBridgeEffects.PrepareSession();
                    if (!Application.isBatchMode && !network.IsServer()) MobBridgeVisual.PrepareSession();
                    cost.Mark("previewHooks");
                }
                finally { cost.Finish(); }
            }
        }

        private static void Connected(ZNetPeer peer)
        {
            if (hooks == null || peer == null || peer.m_rpc == null) return;
            Context();
            MobBridgeObserver.Connect(peer);
            Capability capability = capabilities.GetOrCreateValue(peer.m_rpc);
            if (capability.Registered && capability.Ready) return;
            if (!capability.Registered)
            {
                capability.Registered = true;
                peer.m_rpc.Register<int>(CapabilityRpc, delegate(ZRpc rpc, int value)
                {
                    if (hooks == null || !Trusted(peer, rpc) || (value != Protocol && value != -Protocol)) return;
                    capabilities.GetOrCreateValue(rpc).Ready = true;
                    if (value == Protocol) rpc.Invoke(CapabilityRpc, -Protocol);
                    if (value == -Protocol) Plugin.TestLog("bridge peer ready; uid=" + peer.m_uid);
                });
                peer.m_rpc.Register<ZDOID, ZPackage>(StateRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
                {
                    try { Receive(peer, rpc, id, packet); }
                    catch (Exception error) { Plugin.Warn("Mob bridge state ignored: " + error.GetBaseException().Message); }
                });
                peer.m_rpc.Register<ZDOID, ZPackage>(ClockRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
                {
                    try { ReceiveClock(peer, rpc, id, packet); }
                    catch (Exception error) { Plugin.Warn("Mob bridge clock ignored: " + error.GetBaseException().Message); }
                });
                peer.m_rpc.Register<ZDOID, ZPackage>(FinishRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
                {
                    try { ReceiveFinish(peer, rpc, id, packet); }
                    catch (Exception error) { Plugin.Warn("Mob bridge finish ignored: " + error.GetBaseException().Message); }
                });
                peer.m_rpc.Register<ZPackage>(RateRpc, delegate(ZRpc rpc, ZPackage packet)
                {
                    try { ReceiveRate(peer, rpc, packet); }
                    catch (Exception) { }
                });
                peer.m_rpc.Register<string>(DiagnosticRpc, ReceiveDiagnostic);
                // Older debug clients may still send reports; keep a no-op sink.
            }
            if (!capability.Ready) peer.m_rpc.Invoke(CapabilityRpc, Protocol);
        }
        private static void ReceiveDiagnostic(ZRpc rpc, string line)
        {
#if TML_BRIDGE_DIAGNOSTICS
            if (!network || !network.IsServer() || line == null || line.Length > 1800) return;
            foreach (ZNetPeer peer in network.GetPeers())
                if (Trusted(peer, rpc)) { Plugin.TestLog("CLIENT BRIDGE " + peer.m_uid + ": " + line); return; }
#endif
        }

        private static void PeerReady(ZRpc rpc)
        {
            if (hooks == null || network == null || rpc == null) return;
            foreach (ZNetPeer peer in network.GetPeers())
                if (peer != null && ReferenceEquals(peer.m_rpc, rpc)) { Connected(peer); return; }
        }

        [System.Diagnostics.Conditional("TML_BRIDGE_DIAGNOSTICS")]
        internal static void Report(string line)
        {
#if TML_BRIDGE_DIAGNOSTICS
            if (line == null) return;
            Plugin.TestLog(line);
            if (network == null || network.IsServer()) return;
            try
            {
                ZNetPeer server = network.GetServerPeer();
                if (server != null && server.IsReady() && server.m_rpc != null)
                    server.m_rpc.Invoke(DiagnosticRpc, line);
            }
            catch (Exception) { }
#endif
        }

        private static bool Trusted(ZNetPeer peer, ZRpc rpc)
        {
            if (hooks == null || !network || peer == null || !peer.IsReady() || !ReferenceEquals(peer.m_rpc, rpc)) return false;
            return network.IsServer() ? ReferenceEquals(network.GetPeer(peer.m_uid), peer) :
                peer.m_server && ReferenceEquals(network.GetServerPeer(), peer);
        }

        private static bool Capable(ZNetPeer peer)
        {
            Capability value;
            return peer != null && peer.IsReady() && peer.m_rpc != null &&
                capabilities.TryGetValue(peer.m_rpc, out value) && value.Ready;
        }

        // Small, low-frequency public clock cache. It is available before the
        // handoff and is replaced by the active bridge clock once that starts.
        internal static void PollRate()
        {
            if (!Started || network == null) return;
            double wall = Time.realtimeSinceStartupAsDouble;
            if (wall < nextRateAt) return;
            float simulation = Time.time;
            if (lastRateWall > 0d && wall - lastRateWall >= .1d)
            {
                float ratio = (simulation - lastRateSimulation) / (float)(wall - lastRateWall);
                if (MobBridgeTimeline.Finite(ratio))
                    localRate += (Math.Max(.1f, Math.Min(1.5f, ratio)) - localRate) * .5f;
            }
            lastRateWall = wall; lastRateSimulation = simulation; nextRateAt = wall + .5d;
            float frame = Time.smoothDeltaTime;
            if (frame > 0f && MobBridgeTimeline.Finite(frame))
                localAiRate += (Math.Min(1f, .05f / Math.Max(.05f, frame)) - localAiRate) * .5f;
            if (network != null)
            {
                foreach (ZNetPeer peer in network.GetPeers())
                {
                    if (peer == null || !peer.IsReady() || peer.m_rpc == null) continue;
                    Connected(peer);
                }
            }
            ZPackage packet = new ZPackage();
            packet.Write(ZDOMan.GetSessionID()); packet.Write(localRate); packet.Write(localAiRate);
            PeerRate timing = LocalTiming(wall);
            packet.Write(timing.Step); packet.Write(timing.Frame); packet.Write(timing.Ready); packet.Write(timing.FirstProgress);
            packet.Write(timing.Startup);
            if (network.IsServer())
            {
                foreach (ZNetPeer peer in network.GetPeers())
                    if (peer != null && peer.IsReady() && Capable(peer)) peer.m_rpc.Invoke(RateRpc, packet);
                rates[ZDOMan.GetSessionID()] = timing;
            }
            else
            {
                ZNetPeer server = network.GetServerPeer();
                if (server != null && server.IsReady() && server.m_rpc != null) server.m_rpc.Invoke(RateRpc, packet);
            }
        }

        private static PeerRate LocalTiming(double wall)
        {
            float step = Time.fixedDeltaTime;
            float frame = FrameSeconds;
            float ready, progress;
            float startup = Math.Max(frame, (float)ClockInterval);
            MobBridgeTiming.DefaultReady(localRate, step, frame, out ready, out progress);
            if (localReadyAt > 0d && wall - localReadyAt <= 2d && localReadyStep == step && localReadyRate > 0f)
            { ready = localReady * localReadyRate / localRate; progress = localFirstProgress; startup = localStartup; }
            localTimingCache.Ratio = localRate; localTimingCache.AiRatio = localAiRate;
            localTimingCache.Step = step; localTimingCache.Frame = frame;
            localTimingCache.Ready = ready; localTimingCache.FirstProgress = progress; localTimingCache.Received = wall;
            localTimingCache.Startup = startup;
            return localTimingCache;
        }

        private static PeerRate TimingFor(long destination)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (network != null && network.IsServer() && destination == ZDOMan.GetSessionID()) return LocalTiming(now);
            PeerRate value;
            if (rates.TryGetValue(destination, out value) && value.Received >= now - 2d) return value;
            // Unknown peers retain the native cadence and current local frame
            // estimate; no extra probe/RPC is started by a handoff.
            PeerRate local = LocalTiming(now);
            var fallback = new PeerRate { Ratio = 1f, AiRatio = 1f, Step = local.Step, Frame = local.Frame,
                Startup = Math.Max(local.Frame, (float)ClockInterval) };
            MobBridgeTiming.DefaultReady(1f, fallback.Step, fallback.Frame, out fallback.Ready, out fallback.FirstProgress);
            return fallback;
        }

        private static float RateFor(long destination)
        {
            if (network != null && network.IsServer() && destination == ZDOMan.GetSessionID()) return localRate;
            PeerRate item;
            if (rates.TryGetValue(destination, out item) &&
                item.Received >= Time.realtimeSinceStartupAsDouble - 2d) return item.Ratio;
            return 1f;
        }

        private static float AiRateFor(long destination)
        {
            if (network != null && network.IsServer() && destination == ZDOMan.GetSessionID()) return localAiRate;
            PeerRate item;
            return rates.TryGetValue(destination, out item) && item.Received >= Time.realtimeSinceStartupAsDouble - 2d ? item.AiRatio : 1f;
        }

        private static void ReceiveRate(ZNetPeer peer, ZRpc rpc, ZPackage packet)
        {
            if (!Trusted(peer, rpc) || packet == null || packet.Size() != 36) return;
            long source = packet.ReadLong();
            float ratio = packet.ReadSingle(), aiRatio = packet.ReadSingle();
            float step = packet.ReadSingle(), frame = packet.ReadSingle(), ready = packet.ReadSingle(), progress = packet.ReadSingle();
            float startup = packet.ReadSingle();
            if (source == 0 || !MobBridgeTimeline.Finite(ratio) || ratio < .1f || ratio > 1.5f ||
                !MobBridgeTimeline.Finite(aiRatio) || aiRatio < .1f || aiRatio > 1.5f ||
                !MobBridgeTiming.Positive(step) || !MobBridgeTiming.Positive(frame) ||
                !MobBridgeTiming.Positive(ready) || !MobBridgeTiming.Positive(progress) ||
                !MobBridgeTimeline.Finite(startup) || startup < 0f || startup > Lifetime ||
                step > Lifetime || frame > Lifetime || ready > Lifetime || progress > Lifetime * 1.5f) return;
            var timing = new PeerRate { Ratio = ratio, AiRatio = aiRatio, Step = step, Frame = frame, Ready = ready,
                FirstProgress = progress, Startup = startup, Received = Time.realtimeSinceStartupAsDouble };
            if (network.IsServer())
            {
                if (source != peer.m_uid) return;
                rates[source] = timing;
                foreach (ZNetPeer recipient in network.GetPeers())
                    if (recipient != null && recipient.m_uid != source && recipient.IsReady() && Capable(recipient))
                        recipient.m_rpc.Invoke(RateRpc, packet);
            }
            else
            {
                if (!peer.m_server) return;
                rates[source] = timing;
            }
        }

        private static void BeforeTransfer(ZDO data, long destination)
        {
#if TML_DEBUG_SYSTEM
            if (FgnSoloCombatTest.Vanilla(data)) return;
#endif
            string stage = "prepare";
            MobBridgeCost cost = null;
            try
            {
                Context();
                if (plan == null || world == null || network == null ||
                    data == null || !data.IsValid() || !data.IsOwner() || destination == 0)
                {
                    Plugin.TestLog("bridge skip; unavailable world/network/data");
                    return;
                }
                ZNetView view = ZNetScene.instance == null ? null : ZNetScene.instance.FindInstance(data);
                Character actor = view ? view.GetComponent<Character>() : null;
                MonsterAI ai = view ? view.GetComponent<MonsterAI>() : null;
                Character target = ai ? ai.GetTargetCreature() : null;
                if (!actor || !(actor is Humanoid) || ai == null || !target)
                {
                    Plugin.TestLog("bridge skip; actor/AI/target unavailable");
                    return;
                }
                // Initial owner creation has no source simulation. Every later
                // owner adjustment may bridge regardless of target type or
                // whether the destination is still the target.
                if (data.OwnerRevision == 0U)
                {
                    Plugin.TestLog("bridge skip; initial owner assignment");
                    return;
                }
                ZDO targetData = world.GetZDO(target.GetZDOID());
                if (targetData == null || !targetData.IsValid())
                {
                    Plugin.TestLog("bridge skip; target ZDO unavailable");
                    return;
                }
                ZNetPeer peer = network.IsServer() ? network.GetPeer(destination) : network.GetServerPeer();
                if (!Capable(peer))
                {
                    Plugin.TestLog("bridge skip; destination not bridge-capable; destination=" + destination);
                    return;
                }
#if TML_BRIDGE_DIAGNOSTICS
                cost = new MobBridgeCost(data.m_uid, "checkpoint");
#endif
                uint seed = MobBridgeRandom.NewSeed();
                float injected = 0f;
#if TML_DEBUG_SYSTEM
                injected = SoloNetworkLatency.RoundtripSeconds;
#endif
                float measured = MobBridgeLatency.Measure(peer, FgnCombatOwnership.Fgn, injected);
                float latency = injected > 0f ? measured : MobSourceHandoff.PresentationWindow(destination, measured);
                float initialRate = RateFor(destination);
                PeerRate peerTiming = TimingFor(destination);
                MobBridgeTiming timing = MobBridgeTiming.Create(latency, initialRate, peerTiming.Step, peerTiming.Frame,
                    peerTiming.Ready, peerTiming.FirstProgress, LocalTiming(Time.realtimeSinceStartupAsDouble).Frame, peerTiming.Startup);
                Humanoid humanoid = (Humanoid)actor;
                stage = "native seed";
                MobBridgeSeed simulationSeed = new MobBridgeSeed(plan, ai, humanoid, target, seed, Time.realtimeSinceStartupAsDouble);
                cost.Mark("seed");
                stage = "status shadow";
                byte[] statuses = MobStatusState.Capture(humanoid);
                MobStatusState.Shadow statusShadow = MobStatusState.CreateShadow(humanoid, statuses);
                cost.Mark("status");
                stage = "portable checkpoint";
                MobBridgeCheckpoint checkpoint = MobBridgeCheckpoint.Capture(plan, ai, humanoid, target, seed, latency, timing);
                cost.Mark("portable");
                stage = "outgoing state";
                outgoing[data.m_uid] = new Outgoing
                {
                    Data = data, Ai = ai, Actor = humanoid, Target = target, Seed = simulationSeed,
                    Origin = actor.transform.position, Rotation = actor.transform.rotation,
                    Velocity = actor.GetVelocity(), Latency = latency, Destination = destination,
                    Until = Time.realtimeSinceStartupAsDouble + Lifetime, InitialRealRate = initialRate, InitialAiRate = AiRateFor(destination),
                    Revision = data.OwnerRevision, Status = statusShadow, Timing = timing
                };
                stage = "packet";
                ZPackage packet = new ZPackage();
                packet.Write(data.GetOwner());
                packet.Write(destination);
                packet.Write((long)data.OwnerRevision);
                packet.Write(data.GetPrefab());
                packet.Write(checkpoint.Payload);
                if (packet.Size() > MaxBytes) throw new InvalidOperationException("Mob bridge packet too large.");
                stage = "send";
                peer.m_rpc.Invoke(StateRpc, data.m_uid, packet);
                cost.Mark("send");
            }
            catch (Exception error)
            {
                Plugin.Warn("Mob bridge checkpoint unavailable at " + stage + "; native transfer retained: " +
                    error.GetBaseException().Message + Environment.NewLine + error.GetBaseException().StackTrace);
            }
            finally { if (cost != null) cost.Finish(); }
        }

        internal static void Cancel(ZDOID id)
        {
            MobBridgeVisual.Cancel(id); MobBridgeEffects.Cancel(id);
            MobBridgeObserver.Cancel(id);
            ForgetOwnerPose(id);
            pending.Remove(id); outgoing.Remove(id); random.Remove(id); targets.Remove(id); clocks.Remove(id); finishes.Remove(id);
        }

        private static void Transferred(ZDO data, long destination)
        {
            Outgoing item;
            if (data == null || !outgoing.TryGetValue(data.m_uid, out item))
            {
                Plugin.TestLog("bridge skip transferred; outgoing state missing");
                return;
            }
            outgoing.Remove(data.m_uid);
            if (item.Destination != destination || network == null || network.IsServer()) return;
            MobBridgeVisual.Start(item.Data, destination, item.Ai, item.Actor, item.Target, item.Seed,
                item.Status, item.Origin, item.Rotation, item.Velocity, item.Latency, item.InitialRealRate, item.InitialAiRate, item.Revision, item.Timing);
            if (!MobBridgeVisual.Contains(data.m_uid, item.Revision, item.Seed.Seed))
                Finish(data.m_uid, destination, item.Revision, item.Seed.Seed, true);
        }

        private static void Receive(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage packet)
        {
            if (!Trusted(peer, rpc) || id.IsNone() || packet == null || packet.Size() > MaxBytes) return;
            Context();
#if TML_DEBUG_SYSTEM
            if (world != null && FgnSoloCombatTest.Vanilla(world.GetZDO(id))) return;
#endif
            long source = packet.ReadLong();
            long destination = packet.ReadLong();
            uint revision = (uint)packet.ReadLong();
            int prefab = packet.ReadInt();
            ZPackage payload = new ZPackage(packet.ReadByteArray());
            MobBridgeCheckpoint checkpoint = MobBridgeCheckpoint.Decode(payload);
            if (source == 0 || destination == 0 || source == destination || revision == UInt32.MaxValue)
                return;
            if (network.IsServer())
            {
                if (source != peer.m_uid) return;
                ZDO data = world.GetZDO(id);
                if (data == null || !data.IsValid() || data.GetOwner() != source ||
                    data.OwnerRevision != revision || data.GetPrefab() != prefab) return;
                if (destination != ZDOMan.GetSessionID())
                {
                    ZNetPeer next = network.GetPeer(destination);
                    if (Capable(next)) next.m_rpc.Invoke(StateRpc, id, packet);
                    return;
                }
            }
            else if (destination != ZDOMan.GetSessionID()) return;
            if (pending.ContainsKey(id)) return;
            if (pending.Count >= Limit) Prune();
            if (pending.Count >= Limit) return;
            ZDO item = world.GetZDO(id);
            pending[id] = new Pending
            {
                Data = item, State = checkpoint, Source = source, Destination = destination,
                Revision = revision, Prefab = prefab, Until = Time.realtimeSinceStartupAsDouble + Lifetime,
                ReceivedAt = Time.realtimeSinceStartupAsDouble,
                Origin = checkpoint.Origin, Rotation = checkpoint.Rotation, Velocity = checkpoint.Velocity
            };
            Plugin.TestLog("bridge destination state received; mob=" + id + "; source=" + source +
                "; destination=" + destination + "; revision=" + revision);
            EnsureReceiveHook();
        }

        private static void Replicated()
        {
            if (pending.Count == 0) return;
            try { ApplyPending(); Prune(); }
            catch (Exception error) { Plugin.Warn("Mob bridge state apply stopped: " + error.GetBaseException().Message); }
        }

        private static void ApplyPending()
        {
            Context();
            if (world == null || ZNetScene.instance == null) { pending.Clear(); return; }
            ids.Clear(); foreach (ZDOID id in pending.Keys) ids.Add(id);
            foreach (ZDOID id in ids)
            {
                Pending item; if (!pending.TryGetValue(id, out item)) continue;
                ZDO data = world.GetZDO(id);
                if (!ReferenceEquals(data, item.Data) || data == null || !data.IsValid() ||
                    data.GetPrefab() != item.Prefab || data.OwnerRevision > item.Revision + 1U)
                { pending.Remove(id); continue; }
                if (data.OwnerRevision != item.Revision + 1U || data.GetOwner() != item.Destination) continue;
                ZNetView view = ZNetScene.instance.FindInstance(data);
                MonsterAI ai = view ? view.GetComponent<MonsterAI>() : null;
                Humanoid actor = view ? view.GetComponent<Humanoid>() : null;
                ZDO targetData = world.GetZDO(item.State.Target);
                ZNetView targetView = targetData != null && targetData.IsValid() ? ZNetScene.instance.FindInstance(targetData) : null;
                Character target = targetView ? targetView.GetComponent<Character>() : null;
                if (!ai || !actor || !target || !targetData.IsValid())
                {
                    // A committed epoch can outlive the initial estimate while
                    // its instance loads. Preserve its checkpoint until epoch
                    // invalidation; it must not expire behind an active preview.
                    continue;
                }
                EnsureAiHook();
                item.State.Apply(plan, ai, actor, target);
                Transform visual = actor.GetVisual() ? actor.GetVisual().transform : null;
                Vector3 priorRootPosition = actor.transform.position;
                Quaternion priorRootRotation = actor.transform.rotation;
                Vector3 priorVisualPosition = visual ? visual.position : actor.transform.position;
                Quaternion priorVisualRotation = visual ? visual.rotation : actor.transform.rotation;
                Vector3 priorVelocity = actor.GetVelocity();
                float gainSeconds = Math.Max(Time.fixedDeltaTime, (float)ClockInterval);
                // A client already displays the interpolated replica. Replacing
                // that root with the older owner's cutoff creates a second,
                // delayed pose for the new simulation and forces presentation
                // code to hide the difference. Keep the local displayed root as
                // the simulation basis; carry the source AI/combat state and
                // velocity into it. The server has no displayed replica, so it
                // keeps the exact source cutoff.
                bool localBasis = !network.IsServer();
                Vector3 basisPosition = localBasis ? priorRootPosition : item.Origin;
                Quaternion basisRotation = localBasis ? priorRootRotation : item.Rotation;
                ApplySourcePose(actor, basisPosition, basisRotation, item.Velocity);
                MobBridgeVisual.PrimeOwnerAnimation(actor);
                ArmSourcePose(id, actor, basisPosition, basisRotation, item.Velocity,
                    priorVisualPosition, priorVisualRotation, priorVelocity, gainSeconds);
                Plugin.TestLog("bridge owner basis; mob=" + id + "; local=" + localBasis +
                    "; sourceOffset=" + (item.Origin - priorRootPosition).magnitude.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                // Even a failed/finished preview must preserve the native AI
                // checkpoint. Only matching hooks/traffic can be skipped.
                bool matchingSource = item.ReleaseStage == 0 && SourceHasPresentation(item.Source);
                if (!matchingSource && !MobBridgeObserver.KeepClock(id, item.Source, item.Destination, item.Revision, item.State.Seed, 0f))
                { pending.Remove(id); continue; }
                if (matchingSource)
                {
                random[id] = new RandomEpoch
                {
                    Id = id,
                    State = MobBridgeRandom.Create(item.State.Seed),
                    Destination = item.Destination, Revision = item.Revision,
                    Remaining = item.State.Horizon,
                    Until = Time.realtimeSinceStartupAsDouble + Lifetime
                };
                targets[id] = new TargetEpoch
                {
                    Target = target, Destination = item.Destination, Revision = item.Revision,
                    Remaining = item.State.Horizon,
                    Until = Time.realtimeSinceStartupAsDouble + Lifetime
                };
                }
                clocks[id] = new ClockEpoch
                {
                    Id = id, Source = item.Source, Destination = item.Destination,
                    Revision = item.Revision, Token = item.State.Seed,
                    FirstWall = Time.realtimeSinceStartupAsDouble, GameStarted = Time.fixedTime,
                    Startup = (float)Math.Max(0d, Time.realtimeSinceStartupAsDouble - item.ReceivedAt),
                    Horizon = item.State.Horizon,
                    Until = Time.realtimeSinceStartupAsDouble + item.State.FirstArrival + 2f * item.State.RecordingWindow + .5d,
                    Actor = actor, PresentedSource = SourceHasPresentation(item.Source),
                    Body = actor.GetComponent<Rigidbody>(),
                    MatchActive = matchingSource
                };
                if (matchingSource) MobBridgeEffects.Expect(data, item.Revision, item.State.Seed);
                SendClock(clocks[id], 0f, clocks[id].FirstWall);
                Plugin.TestLog("bridge destination clock armed; mob=" + id + "; source=" + item.Source +
                    "; destination=" + item.Destination + "; latency=" + item.State.Latency.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                pending.Remove(id);
            }
        }

        private static void ApplySourcePose(Humanoid actor, Vector3 origin, Quaternion rotation, Vector3 velocity)
        {
            if (!actor || !MobBridgeFrame.Finite(origin) || !actor.transform) return;
            if (!Finite(rotation) || !MobBridgeFrame.Finite(velocity)) return;
            Transform root = actor.transform;
            Rigidbody body = actor.GetComponent<Rigidbody>();
            // Pose and velocity are independent checkpoint fields. A replica
            // commonly reaches the source position before ownership commits;
            // an equal pose must not suppress the source velocity or the new
            // owner starts with a stale/zero tangent and visibly pauses.
            if (body)
            {
                body.position = origin;
                body.rotation = rotation;
                if (!body.isKinematic) body.linearVelocity = velocity;
            }
            if ((root.position - origin).sqrMagnitude <= .000001f &&
                Quaternion.Angle(root.rotation, rotation) <= .05f) return;
            root.SetPositionAndRotation(origin, rotation);
        }

        private static void ArmSourcePose(ZDOID id, Humanoid actor, Vector3 origin, Quaternion rotation, Vector3 velocity,
            Vector3 visualPosition, Quaternion visualRotation, Vector3 displayedVelocity, float gainSeconds)
        {
            ZSyncTransform sync = actor.GetComponent<ZSyncTransform>();
            if (!sync) return;
            if (syncWasOwner == null)
                syncWasOwner = FieldReader.Create<ZSyncTransform, bool>(AccessTools.Field(typeof(ZSyncTransform), "m_wasOwner"));
            // If native OwnerSync already ran, the immediate checkpoint pose is
            // final. Otherwise OwnerSync can overwrite it with the delayed ZDO;
            // keep one copy for the post-sync callback.
            if (syncWasOwner(sync))
            {
                MobBridgeVisual.BeginOwnerGain(id, actor, visualPosition, visualRotation,
                    displayedVelocity, velocity, gainSeconds);
                return;
            }
            if (ownerPoses == null) ownerPoses = new Dictionary<ZSyncTransform, OwnerPose>();
            ownerPoses[sync] = new OwnerPose
            {
                Id = id, Actor = actor, Origin = origin, Rotation = rotation, Velocity = velocity,
                DisplayedVelocity = displayedVelocity,
                VisualPosition = visualPosition, VisualRotation = visualRotation, GainSeconds = gainSeconds,
                Until = Time.realtimeSinceStartupAsDouble + Lifetime
            };
            MobBridgeEffects.AfterOwnerSync = OwnerSyncPost;
        }

        private static void OwnerSyncPost(ZSyncTransform sync)
        {
            OwnerPose pose;
            if (ownerPoses == null || !ownerPoses.TryGetValue(sync, out pose) || syncWasOwner == null || !syncWasOwner(sync)) return;
            ownerPoses.Remove(sync);
            ApplySourcePose(pose.Actor, pose.Origin, pose.Rotation, pose.Velocity);
            MobBridgeVisual.BeginOwnerGain(pose.Id, pose.Actor, pose.VisualPosition, pose.VisualRotation,
                pose.DisplayedVelocity, pose.Velocity, pose.GainSeconds);
            Plugin.TestLog("bridge owner pose restored after native sync; mob=" + pose.Id +
                "; velocity=" + pose.Velocity.ToString());
            if (ownerPoses.Count == 0) MobBridgeEffects.AfterOwnerSync = null;
        }

        private static void ForgetOwnerPose(ZDOID id)
        {
            if (ownerPoses == null || ownerPoses.Count == 0) return;
            ownerPoseKeys.Clear();
            foreach (KeyValuePair<ZSyncTransform, OwnerPose> pair in ownerPoses)
                if (pair.Value.Id == id) ownerPoseKeys.Add(pair.Key);
            for (int i = 0; i < ownerPoseKeys.Count; ++i) ownerPoses.Remove(ownerPoseKeys[i]);
            ownerPoseKeys.Clear();
            MobBridgeVisual.CancelOwnerGain(id);
            if (ownerPoses.Count == 0) MobBridgeEffects.AfterOwnerSync = null;
        }

        private static void ClearOwnerPoses()
        {
            if (ownerPoses != null) ownerPoses.Clear();
            ownerPoseKeys.Clear();
            MobBridgeEffects.AfterOwnerSync = null;
            MobBridgeVisual.CancelOwnerGains();
        }
        private static bool Finite(Quaternion value)
        {
            float norm = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return MobBridgeTimeline.Finite(norm) && norm > .0001f;
        }

        private static bool BeforeTarget(MonsterAI __instance, Humanoid humanoid, float dt,
            out bool canHearTarget, out bool canSeeTarget)
        {
            canHearTarget = false; canSeeTarget = false;
            if (targets.Count == 0 || __instance == null) return true;
            ZNetView view = __instance.GetComponent<ZNetView>();
            ZDO data = view && view.IsValid() ? view.GetZDO() : null;
            if (data == null) return true;
            TargetEpoch epoch;
            if (!targets.TryGetValue(data.m_uid, out epoch)) return true;
            double now = Time.realtimeSinceStartupAsDouble;
            if ((!Matching(data.m_uid) && (epoch.Until < now || epoch.Remaining <= 0f)) || epoch.Target == null ||
                epoch.Target.IsDead() || data.GetOwner() != epoch.Destination ||
                data.OwnerRevision != epoch.Revision + 1U)
            {
                targets.Remove(data.m_uid);
                return true;
            }
            targetWrite(__instance, epoch.Target);
            staticTargetWrite(__instance, null);
            updateTargetWrite(__instance, Math.Max(0f, updateTargetRead(__instance) - dt));
            unableTargetWrite(__instance, unableTargetRead(__instance) - dt);
            canHearTarget = __instance.CanHearTarget(epoch.Target);
            canSeeTarget = __instance.CanSeeTarget(epoch.Target);
            sensedWrite(__instance, canHearTarget || canSeeTarget ? dt : sensedRead(__instance) + dt);
            attackingWrite(__instance, attackingRead(__instance) + dt);
            epoch.Remaining = Math.Max(0f, epoch.Remaining - dt);
            return false;
        }

        private static void BeforeRandom(MonsterAI __instance, float __0, out RandomScope __state)
        {
            __state = new RandomScope();
            if (clocks.Count == 0 || __instance == null) return;
            ZNetView view = __instance.GetComponent<ZNetView>();
            ZDO data = view && view.IsValid() ? view.GetZDO() : null;
            if (data == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            ClockEpoch clock;
            if (!clocks.TryGetValue(data.m_uid, out clock) || !ClockValid(clock, now) ||
                data.GetOwner() != clock.Destination || data.OwnerRevision != clock.Revision + 1U)
            {
                if (clocks.ContainsKey(data.m_uid)) clocks.Remove(data.m_uid);
                return;
            }
            clock.DecisionDelta = Math.Max(0f, __0);
            __state.Clock = clock;
            __state.Delta = Math.Max(0f, __0);
            __state.Wall = now;
            RandomEpoch epoch;
            if (random.TryGetValue(data.m_uid, out epoch))
            {
                if ((!Matching(data.m_uid) && (epoch.Until < now || epoch.Remaining <= 0f)) ||
                    data.GetOwner() != epoch.Destination || data.OwnerRevision != epoch.Revision + 1U)
                    random.Remove(data.m_uid);
                else
                {
                    __state.Original = UnityEngine.Random.state;
                    __state.Active = true;
                    __state.Random = epoch;
                    UnityEngine.Random.state = epoch.State;
                }
            }

        }

        private static Exception AfterRandom(MonsterAI __instance, RandomScope __state, Exception __exception)
        {
            if (__state.Active)
            {
                try
                {
                    ZNetView view = __instance ? __instance.GetComponent<ZNetView>() : null;
                    ZDO data = view && view.IsValid() ? view.GetZDO() : null;
                    RandomEpoch epoch;
                    if (data != null && random.TryGetValue(data.m_uid, out epoch)) epoch.State = UnityEngine.Random.state;
                }
                finally { UnityEngine.Random.state = __state.Original; }
                if (__state.Random != null)
                {
                    __state.Random.Remaining = Math.Max(0f, __state.Random.Remaining - __state.Delta);
                    if (__state.Random.Remaining <= 0f && !Matching(__state.Random.Id)) random.Remove(__state.Random.Id);
                }
            }
            if (__state.Clock != null && __exception == null)
            {
                ClockEpoch clock = __state.Clock;
                if (Double.IsNaN(clock.FirstWall)) clock.FirstWall = __state.Wall;
                clock.Tick++;
                clock.AiSimulation += __state.Delta;
                // AI dt/pass count is not a completed physics frontier. Publish
                // paired pose/progress after physics, from existing LateUpdate
                // maintenance of active handoffs only.
            }
            return __exception;
        }

        private static void PublishPhysicsClock(ClockEpoch clock, double now)
        {
            float completed = Math.Max(clock.Simulation, Time.fixedTime - clock.GameStarted);
            bool active = clock.Actor != null && clock.Actor.InAttack();
            if (!clock.Cadence.Due(now, clock.AttackTime, completed, clock.Horizon, clock.Tick) &&
                active == clock.AttackActive) return;
            if (!ClockValid(clock, now)) return;
            clock.Simulation = completed;
            SendClock(clock, (float)Math.Max(0d, now - clock.FirstWall), now);
        }

        private static void SendClock(ClockEpoch clock, float sampleTime, double sampledAt)
        {
            if (network == null || !clock.Actor) return;
            ZNetPeer peer = network.IsServer() ? network.GetPeer(clock.Source) : network.GetServerPeer();
            if (!network.IsServer() && (peer == null || !peer.IsReady() || peer.m_rpc == null)) return;
            ZPackage packet = new ZPackage();
            Vector3 position = clock.Body ? clock.Body.position : clock.Actor.transform.position;
            Quaternion rotation = clock.Body ? clock.Body.rotation : clock.Actor.transform.rotation;
            Vector3 velocity = clock.Body ? clock.Body.linearVelocity : clock.Actor.GetVelocity();
            clock.AttackActive = clock.Actor.InAttack();
            packet.Write(clock.Source); packet.Write(clock.Destination);
            packet.Write(clock.Revision); packet.Write(clock.Token);
            packet.Write(clock.Tick); packet.Write(clock.Simulation); packet.Write(sampleTime); packet.Write(clock.AiSimulation);
            packet.Write(position); packet.Write(rotation);
            packet.Write(velocity); packet.Write(clock.AttackTime); packet.Write(clock.Attack);
            packet.Write(clock.AttackActive);
            if (network.IsServer()) MobBridgeObserver.ObserveClock(clock.Id, clock.Source, clock.Destination, clock.Revision,
                clock.Token, clock.Tick, clock.Simulation, sampleTime, position, rotation,
                velocity, clock.AttackTime, clock.Attack, clock.AiSimulation, packet, clock.AttackActive);
            try
            {
                if (peer != null && peer.IsReady() && peer.m_rpc != null) peer.m_rpc.Invoke(ClockRpc, clock.Id, packet);
                MobBridgeObserver.ClockSent(clock.Id, clock.Source, clock.Destination, clock.Revision, clock.Token, clock.Simulation);
                clock.Cadence.Sent(sampledAt, clock.AttackTime, clock.Simulation, clock.Horizon, clock.Tick);
                if (!clock.ReadyMeasured && clock.Simulation > 0f && sampleTime > 0f)
                {
                    clock.ReadyMeasured = true;
                    localReady = sampleTime; localFirstProgress = clock.Simulation;
                    localStartup = clock.Startup;
                    localReadyAt = sampledAt; localReadyRate = localRate; localReadyStep = Time.fixedDeltaTime;
                }
            }
            catch (Exception) { }
        }

        private static void ReceiveClock(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage packet)
        {
            if (!Trusted(peer, rpc) || id.IsNone() || packet == null || packet.Size() > 512) return;
            long source = packet.ReadLong(), destination = packet.ReadLong();
            uint revision = packet.ReadUInt(), token = packet.ReadUInt();
            long tick = packet.ReadLong();
            float simulation = packet.ReadSingle(), sampleTime = packet.ReadSingle(), aiSimulation = packet.ReadSingle();
            Vector3 position = packet.ReadVector3(); Quaternion rotation = packet.ReadQuaternion();
            Vector3 velocity = packet.ReadVector3(); float attackTime = packet.ReadSingle(); string attack = packet.ReadString();
            bool attackActive = packet.ReadBool();
            if (source == 0 || destination == 0 || token == 0 || tick < 0 ||
                !MobBridgeTimeline.Finite(simulation) || !MobBridgeTimeline.Finite(sampleTime) ||
                simulation < 0f || sampleTime < 0f || aiSimulation < 0f ||
                !MobBridgeFrame.Valid(simulation, sampleTime, position, rotation, velocity, attackTime, attack, aiSimulation)) return;
            if (network.IsServer())
            {
                if (peer.m_uid != destination) return;
                MobBridgeObserver.ObserveClock(id, source, destination, revision, token, tick, simulation, sampleTime,
                    position, rotation, velocity, attackTime, attack, aiSimulation, packet, attackActive);
                ZNetPeer oldOwner = network.GetPeer(source);
                if (oldOwner != null && oldOwner.IsReady()) oldOwner.m_rpc.Invoke(ClockRpc, id, packet);
                return;
            }
            if (source != ZDOMan.GetSessionID())
            {
                MobBridgeObserver.ObserveClock(id, source, destination, revision, token, tick, simulation, sampleTime,
                    position, rotation, velocity, attackTime, attack, aiSimulation, packet, attackActive);
                return;
            }
            MobBridgeVisual.ObserveClock(id, revision, token, tick, simulation, sampleTime,
                position, rotation, velocity, attackTime, attack, aiSimulation, Time.realtimeSinceStartupAsDouble, attackActive);
        }

        private static bool SourceHasPresentation(long source)
        {
            ZNetPeer server = network.IsServer() ? null : network.GetServerPeer();
            return network.IsServer() ? source != ZDOMan.GetSessionID() : server != null && source != server.m_uid;
        }

        private static bool Matching(ZDOID id)
        {
            ClockEpoch clock;
            return clocks.TryGetValue(id, out clock) && clock.MatchActive;
        }

        private static bool ClockValid(ClockEpoch clock, double now)
        {
            ZDO data = world == null ? null : world.GetZDO(clock.Id);
            if (data == null || !data.IsValid() || data.GetOwner() != clock.Destination ||
                data.OwnerRevision != clock.Revision + 1U || !clock.Actor || clock.Actor.IsDead()) return false;
            if (!clock.MatchActive && MobBridgeObserver.KeepClock(clock.Id, clock.Source, clock.Destination,
                clock.Revision, clock.Token, clock.Simulation)) return true;
            if (!clock.MatchActive && clock.Until < now) return false;
            if (!clock.PresentedSource) return clock.Until >= now;
            ZNetPeer route = network.IsServer() ? network.GetPeer(clock.Source) : network.GetServerPeer();
            if (route != null && route.IsReady()) return true;
            if (!MobBridgeObserver.KeepClock(clock.Id, clock.Source, clock.Destination, clock.Revision, clock.Token, clock.Simulation)) return false;
            clock.MatchActive = false; random.Remove(clock.Id); targets.Remove(clock.Id);
            return true;
        }

        private static void AfterAttack(MonsterAI __instance, bool __result)
        {
            if (clocks.Count == 0 || !__result || __instance == null) return;
            ZNetView view = __instance.GetComponent<ZNetView>();
            ZDO data = view && view.IsValid() ? view.GetZDO() : null;
            ClockEpoch clock;
            if (data == null || !clocks.TryGetValue(data.m_uid, out clock) || !ClockValid(clock, Time.realtimeSinceStartupAsDouble)) return;
            // Tag the completed AI pass, not its start. Retain the marker when
            // the actual attack has ended before this delayed clock is read.
            clock.AttackTime = Math.Max(clock.Simulation, Time.fixedTime - clock.GameStarted);
            ItemDrop.ItemData weapon = clock.Actor.GetCurrentWeapon();
            clock.Attack = weapon == null || weapon.m_shared.m_attack == null ? "" : weapon.m_shared.m_attack.m_attackAnimation;
        }

        internal static void Finish(ZDOID id, long destination, uint revision, uint token, bool final)
        {
            if (network == null) return;
            FinishNotice previous;
            if (finishes.TryGetValue(id, out previous) && previous.Revision == revision && previous.Token == token)
                previous.Final |= final;
            else
            {
                if (finishes.Count >= Limit) FlushFinishes();
                if (finishes.Count >= Limit) return;
                finishes[id] = new FinishNotice { Destination = destination, Revision = revision, Token = token, Final = final };
            }
            FlushFinishes();
        }

        private static void FlushFinishes()
        {
            if (network == null || finishes.Count == 0) return;
            // A thrown send is retried while the route exists. Successful Invoke
            // has entered Valheim's reliable ordered transport; no polling RPC.
            List<ZDOID> done = new List<ZDOID>();
            foreach (KeyValuePair<ZDOID, FinishNotice> pair in finishes)
            {
                FinishNotice notice = pair.Value;
                ZNetPeer peer = network.IsServer() ? network.GetPeer(notice.Destination) : network.GetServerPeer();
                if (peer == null || !peer.IsReady()) continue;
                ZPackage packet = new ZPackage();
                packet.Write(ZDOMan.GetSessionID()); packet.Write(notice.Destination);
                packet.Write(notice.Revision); packet.Write(notice.Token); packet.Write((byte)(notice.Final ? 2 : 1));
                try { peer.m_rpc.Invoke(FinishRpc, pair.Key, packet); done.Add(pair.Key); }
                catch (Exception) { }
            }
            foreach (ZDOID id in done) finishes.Remove(id);
        }

        private static void ReceiveFinish(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage packet)
        {
            if (!Trusted(peer, rpc) || packet == null || packet.Size() > 32 || id.IsNone()) return;
            long source = packet.ReadLong(), destination = packet.ReadLong();
            uint revision = packet.ReadUInt(), token = packet.ReadUInt(); byte stage = packet.ReadByte();
            if (stage < 1 || stage > 2 || token == 0u) return;
            if (network.IsServer())
            {
                if (source != peer.m_uid) return;
                if (destination != ZDOMan.GetSessionID())
                {
                    ZNetPeer next = network.GetPeer(destination);
                    if (next != null && next.IsReady()) next.m_rpc.Invoke(FinishRpc, id, packet);
                    return;
                }
            }
            else if (destination != ZDOMan.GetSessionID()) return;
            MobBridgeEffects.Finished(id, revision, token, stage);
            ClockEpoch clock;
            if (clocks.TryGetValue(id, out clock) && clock.Source == source && clock.Destination == destination &&
                clock.Revision == revision && clock.Token == token)
            {
                clock.MatchActive = false;
                bool observer = MobBridgeObserver.KeepClock(id, source, destination, revision, token, clock.Simulation);
                if (!observer) { random.Remove(id); targets.Remove(id); }
                // Keep actual owner velocity during the short cosmetic join;
                // a nonowner's cached Character velocity need not be current.
                // Final settlement stops this ordinary-cadence stream. Never
                // retain the matching RNG/target lock just for presentation.
                if (stage == 2 && !observer) clocks.Remove(id);
                else if (stage == 1) clock.Until = Math.Max(clock.Until, Time.realtimeSinceStartupAsDouble + Lifetime);
                // A slower observer can still consume the recorded reserve.
                // Keep its remaining mob-only target/RNG scope until that
                // recorded horizon, rather than releasing at the source's join.
            }
            Pending pendingItem;
            if (pending.TryGetValue(id, out pendingItem) && pendingItem.Source == source &&
                pendingItem.Destination == destination && pendingItem.Revision == revision && pendingItem.State.Seed == token)
                pendingItem.ReleaseStage = Math.Max(pendingItem.ReleaseStage, stage);
        }

        private static void Prune()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (ownerPoses != null && ownerPoses.Count != 0)
            {
                ownerPoseKeys.Clear();
                foreach (KeyValuePair<ZSyncTransform, OwnerPose> pair in ownerPoses)
                    if (pair.Value.Until < now) ownerPoseKeys.Add(pair.Key);
                for (int i = 0; i < ownerPoseKeys.Count; ++i) ownerPoses.Remove(ownerPoseKeys[i]);
                ownerPoseKeys.Clear();
                if (ownerPoses.Count == 0) MobBridgeEffects.AfterOwnerSync = null;
            }
            if (pending.Count == 0) return;
            ids.Clear();
            foreach (KeyValuePair<ZDOID, Pending> pair in pending)
            {
                ZDO data = world == null ? null : world.GetZDO(pair.Key);
                bool committed = data != null && data.IsValid() && data.GetOwner() == pair.Value.Destination &&
                    data.OwnerRevision == pair.Value.Revision + 1U;
                if (pair.Value.Until < now && !committed) ids.Add(pair.Key);
            }
            foreach (ZDOID id in ids) pending.Remove(id);
        }
    }
}
#endif
