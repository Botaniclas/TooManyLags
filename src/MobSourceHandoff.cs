using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Source chooses at the real safe AI boundary. No permission or return
    // acknowledgement; native identity/ownership/revision fences remain.
    internal static class MobSourceHandoff
    {
        private const int Protocol = 3, Limit = 1024;
        private const string ModeRpc = "TooManyLags_AI_SourceMode", BeginRpc = "TooManyLags_AI_SourceBegin", CancelRpc = "TooManyLags_AI_SourceCancel", TimingRpc = "TooManyLags_AI_SourceTiming";
        private sealed class Capability { internal bool Registered, Ready; internal long Last; }
        private sealed class Started { internal ZDO Data; internal long Source, Destination, Token; internal uint Revision; internal ZDOID Target; internal double Until; }
        private sealed class Timing { internal float Seconds; internal double Until; }
        private static readonly Dictionary<ZDOID, Started> started = new Dictionary<ZDOID, Started>();
        private static readonly Dictionary<long, Timing> timings = new Dictionary<long, Timing>();
        private static readonly List<ZDOID> scratch = new List<ZDOID>();
        private static ConditionalWeakTable<ZRpc, Capability> capabilities = new ConditionalWeakTable<ZRpc, Capability>();
        private static Harmony hooks;
        private static ZDOMan world;
        private static ZNet network;
        private static Started local;
        private static long nextToken;
        internal static Action<ZRpc, ZDOID, long> CommitLocal;
        internal static Func<ZDO, ZDOID, long, bool> ValidateLocal;
        internal static Func<ZDO, ZDOID, long> ResolveDestination;
        internal static Action<ZDO, long, ZDOID, long> AcceptedServer;
        internal static Action<ZDO, ZNetPeer> PublishFinal;
        internal static Func<ZNetPeer, float> RoundtripSeconds;
        internal static Action<ZDO, uint, float> PresentationTiming;
        internal static Func<ZDO, bool> SkipLocalCommit = null;
        internal static ZDOID LocalTarget { get { return local == null ? ZDOID.None : local.Target; } }
        internal static bool IsLocalCommit(ZDO data) { return local != null && ReferenceEquals(local.Data, data); }
        internal static bool Waiting(ZDO data)
        {
            Started item;
            return hooks != null && data != null && ReferenceEquals(world, ZDOMan.instance) && started.TryGetValue(data.m_uid, out item) &&
                ReferenceEquals(item.Data, data) && data.IsValid() && data.GetOwner() == item.Source && data.OwnerRevision == item.Revision && item.Until >= Time.realtimeSinceStartupAsDouble;
        }
        internal static void Start()
        {
            Stop();
            try
            {
                hooks = new Harmony(Plugin.Id + ".mobsourcehandoff");
                hooks.Patch(AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData"), postfix: new HarmonyMethod(typeof(MobSourceHandoff), "Replicated"));
                hooks.Patch(AccessTools.Method(typeof(ZNet), "Shutdown"), prefix: new HarmonyMethod(typeof(MobSourceHandoff), "Reset"));
                if (!ReferenceEquals(ZNet.instance, null) && ZNet.instance) foreach (ZNetPeer peer in ZNet.instance.GetPeers()) { Connect(peer); if (peer.IsReady()) Probe(peer); }
            }
            catch (Exception error) { Stop(); Warn("Source handoff unavailable: ", error); }
        }
        internal static void Stop() { if (hooks != null) hooks.UnpatchSelf(); hooks = null; Reset(); }
        internal static void Reset() { started.Clear(); timings.Clear(); scratch.Clear(); capabilities = new ConditionalWeakTable<ZRpc, Capability>(); world = null; network = null; local = null; }
        private static void Context()
        { if (!ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance)) { started.Clear(); timings.Clear(); world = ZDOMan.instance; network = ZNet.instance; } }
        // Presentation estimate only: no poll, permission or gameplay delay.
        // Source -> server -> destination -> server -> source is the sum of RTTs.
        internal static float PresentationWindow(long destination, float fallback)
        {
            Context(); Timing value;
            if (timings.TryGetValue(destination, out value))
            {
                if (value.Until >= Time.realtimeSinceStartupAsDouble)
                {
                    float cached = 2f * value.Seconds;
                    if (cached > 0f && !Single.IsNaN(cached) && !Single.IsInfinity(cached))
                        return cached;
                }
                timings.Remove(destination);
            }
            return Math.Max(.001f, fallback);
        }
        internal static void InvalidatePresentationTiming()
        {
            timings.Clear();
        }
        private static bool Trusted(ZNetPeer peer, ZRpc rpc)
        {
            if (hooks == null || !ZNet.instance || peer == null || !peer.IsReady() || !ReferenceEquals(peer.m_rpc, rpc)) return false;
            return ZNet.instance.IsServer() ? ReferenceEquals(ZNet.instance.GetPeer(peer.m_uid), peer) : peer.m_server && ReferenceEquals(ZNet.instance.GetServerPeer(), peer);
        }
        internal static bool Capable(ZNetPeer peer)
        { Capability value; return hooks != null && peer != null && peer.IsReady() && peer.m_rpc != null && capabilities.TryGetValue(peer.m_rpc, out value) && value.Ready; }
        internal static void Connect(ZNetPeer peer)
        {
            if (hooks == null || peer == null || peer.m_rpc == null) return;
            Capability state = capabilities.GetOrCreateValue(peer.m_rpc); if (state.Registered) return; state.Registered = true;
            peer.m_rpc.Register<int>(ModeRpc, delegate(ZRpc rpc, int mode)
            { if (!Trusted(peer, rpc) || (mode != Protocol && mode != -Protocol)) return; capabilities.GetOrCreateValue(rpc).Ready = true; if (mode == Protocol) rpc.Invoke(ModeRpc, -Protocol); });
            peer.m_rpc.Register<ZDOID, ZPackage>(BeginRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
            { try { Begin(peer, rpc, id, packet); } catch (Exception error) { Warn("Source begin rejected: ", error); } });
            peer.m_rpc.Register<ZDOID, long>(CancelRpc, delegate(ZRpc rpc, ZDOID id, long token)
            { if (!Trusted(peer, rpc) || !ZNet.instance.IsServer()) return; Context(); Started item; if (started.TryGetValue(id, out item) && item.Source == peer.m_uid && item.Token == token) started.Remove(id); });
            peer.m_rpc.Register<ZDOID, ZPackage>(TimingRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
            {
                if (!Trusted(peer, rpc) || ZNet.instance.IsServer() || packet == null || packet.Size() != 16) return;
                Context(); uint revision = packet.ReadUInt(); float seconds = packet.ReadSingle(); long destination = packet.ReadLong();
                ZDO data = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(id);
                if (data == null || !data.IsValid() || data.OwnerRevision != revision + 1U ||
                    data.GetOwner() != destination || !(seconds > 0f) || Single.IsNaN(seconds) || Single.IsInfinity(seconds)) return;
                if (timings.Count >= 128 && !timings.ContainsKey(destination)) timings.Clear();
                timings[destination] = new Timing { Seconds = seconds, Until = Time.realtimeSinceStartupAsDouble + 5.0 };
                if (PresentationTiming != null) PresentationTiming(data, revision, seconds);
            });
        }
        internal static void Probe(ZNetPeer peer) { if (hooks != null && peer != null && peer.IsReady() && peer.m_rpc != null) peer.m_rpc.Invoke(ModeRpc, Protocol); }
        internal static bool TryCommit(BaseAI ai, ZDO data, ZDOID target, ZNetPeer server)
        {
            if (SkipLocalCommit != null && SkipLocalCommit(data)) return false;
            if (hooks == null || !ai || !ZNet.instance || ZNet.instance.IsServer() || !Capable(server) || !Trusted(server, server.m_rpc) ||
                CommitLocal == null || ValidateLocal == null || ResolveDestination == null || PublishFinal == null || data == null || !data.IsValid() || !data.IsOwner() || data.OwnerRevision >= UInt16.MaxValue) return false;
            Context(); long destination = ResolveDestination(data, target), source = data.GetOwner();
            if (destination == 0 || destination == source || !ValidateLocal(data, target, destination)) return false;
            Started item = new Started { Data = data, Source = source, Destination = destination, Revision = data.OwnerRevision, Target = target, Token = ++nextToken };
            ZPackage packet = new ZPackage(); packet.Write(item.Token); packet.Write(item.Revision); packet.Write(data.GetPrefab()); packet.Write(target); packet.Write(destination);
            Started previous = local; bool sent = false;
            try { server.m_rpc.Invoke(BeginRpc, data.m_uid, packet); sent = true; local = item; CommitLocal(server.m_rpc, data.m_uid, destination); }
            catch (Exception error) { Warn("Local source handoff failed: ", error); }
            finally { local = previous; }
            bool committed = data.IsValid() && data.GetOwner() == destination && data.OwnerRevision == item.Revision + 1U;
            if (!committed)
            { if (sent) try { server.m_rpc.Invoke(CancelRpc, data.m_uid, item.Token); } catch (Exception error) { Warn("Source cancellation failed: ", error); } return false; }
            try { PublishFinal(data, server); } catch (Exception error) { Warn("Immediate mob publication failed; native queue retained: ", error); }
            return true;
        }
        private static void Begin(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage packet)
        {
            if (!Trusted(peer, rpc) || !ZNet.instance.IsServer() || !Capable(peer) || id.IsNone() || packet == null || packet.Size() > 64) return;
            Context(); if (world == null) return;
            long token = packet.ReadLong(); uint revision = packet.ReadUInt(); int prefab = packet.ReadInt(); ZDOID target = packet.ReadZDOID(); long destination = packet.ReadLong();
            Capability fence = capabilities.GetOrCreateValue(rpc);
            if (packet.GetPos() != packet.Size() || token <= fence.Last || revision >= UInt16.MaxValue || destination == 0 || destination == peer.m_uid) return;
            ZDO data = world.GetZDO(id);
            if (data == null || !data.IsValid() || data.GetPrefab() != prefab || data.GetOwner() != peer.m_uid || data.OwnerRevision != revision || Waiting(data)) return;
            // Authentication and owner epoch only: no target/ping/boss/loading
            // policy approval. The actual current owner initiates directly.
            if (destination != ZDOMan.GetSessionID() && network.GetPeer(destination) == null) return;
            Prune(); if (!started.ContainsKey(id) && started.Count >= Limit) { Plugin.Warn("Source queue full; native owner replication retained."); return; }
            fence.Last = token; started[id] = new Started { Data = data, Source = peer.m_uid, Destination = destination, Token = token, Revision = revision, Target = target, Until = Time.realtimeSinceStartupAsDouble + 5.0 };
            if (RoundtripSeconds != null)
            {
                float seconds = .5f * (RoundtripSeconds(peer) + (destination == ZDOMan.GetSessionID() ? 0f : RoundtripSeconds(network.GetPeer(destination))));
                if (!Single.IsNaN(seconds) && !Single.IsInfinity(seconds) && seconds > .0001f)
                { ZPackage timing = new ZPackage(); timing.Write(revision); timing.Write(seconds); timing.Write(destination); peer.m_rpc.Invoke(TimingRpc, id, timing); }
            }
        }
        private static void Prune()
        {
            scratch.Clear(); foreach (KeyValuePair<ZDOID, Started> pair in started) if (pair.Value.Until < Time.realtimeSinceStartupAsDouble || !pair.Value.Data.IsValid()) scratch.Add(pair.Key);
            foreach (ZDOID id in scratch) started.Remove(id); scratch.Clear();
        }
        private static void Replicated()
        {
            if (hooks == null || started.Count == 0 || !ZNet.instance || !ZNet.instance.IsServer()) return;
            try
            {
                Context(); if (world == null) return;
                foreach (ZDOID id in new List<ZDOID>(started.Keys))
                {
                    Started item; if (!started.TryGetValue(id, out item)) continue; ZDO data = world.GetZDO(id);
                    if (!ReferenceEquals(data, item.Data) || data == null || !data.IsValid() || data.OwnerRevision > item.Revision + 1U || item.Until < Time.realtimeSinceStartupAsDouble) { started.Remove(id); continue; }
                    if (data.OwnerRevision != item.Revision + 1U) continue;
                    started.Remove(id); if (data.GetOwner() == item.Destination && AcceptedServer != null) AcceptedServer(data, item.Source, item.Target, item.Destination);
                }
            }
            catch (Exception error) { Warn("Source reconciliation failed: ", error); }
        }
        // Watch rebinding during import cannot erase a pending owner receipt.
        internal static void Forget(ZDOID id) { GC.KeepAlive(id); }
        private static void Warn(string text, Exception error) { Plugin.Warn(text + error.GetBaseException().Message); }
    }
}

