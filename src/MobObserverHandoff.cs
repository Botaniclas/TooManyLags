using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // One small, authenticated presentation descriptor per real ownership
    // commit. Spectators render locally; this never sends a simulated ZDO or
    // writes gameplay state. Old clients retain their normal native rendering.
    internal static class MobObserverHandoff
    {
        internal static Func<ZDO, bool> RecordedActive = null;
        private const string CapabilityRpc = "TooManyLags_ObserverCapability", TransitionRpc = "TooManyLags_ObserverTransition", PreviewRpc = "TooManyLags_ObserverPreview";
        private const int Protocol = 1, Limit = 256;
        private const double Lifetime = 5.0;
        private sealed class Capability { internal bool Ready, Registered; }
        private sealed class Fence { internal uint Revision; internal bool Applied; }
        private sealed class Transition
        {
            internal ZDOID Id, Target;
            internal ZDO Data;
            internal long Source, Destination;
            internal uint Revision, Cutoff;
            internal int Prefab;
            internal Vector3 Velocity, Direction;
            internal bool Flight;
            internal float Window, RotationFactor;
            internal double Until;
            internal double NextFrame; internal uint Frame; internal Vector3 Preview; internal float Yaw;
        }
        private static Harmony hooks;
        private static ZDOMan world;
        private static ZNet network;
        private static ConditionalWeakTable<ZRpc, Capability> capabilities = new ConditionalWeakTable<ZRpc, Capability>();
        private static ConditionalWeakTable<ZDO, Fence> delivered = new ConditionalWeakTable<ZDO, Fence>();
        private static readonly Dictionary<ZDOID, Transition> pending = new Dictionary<ZDOID, Transition>();
        private static readonly Dictionary<ZDOID, Transition> active = new Dictionary<ZDOID, Transition>();
        private static readonly List<ZDOID> scratch = new List<ZDOID>();
        private static bool pumping;
        private static Func<Character, float> rotationFactor;

        internal static void Start(bool enabled)
        {
            Stop();
            if (!enabled) return;
            try
            {
                rotationFactor = FieldReader.Create<Character, float>(AccessTools.Field(typeof(Character), "currentRotSpeedFactor"));
                hooks = new Harmony(Plugin.Id + ".mobobservers");
                hooks.Patch(AccessTools.Method(typeof(MobFlightHandoff), "Transfer"),
                    prefix: new HarmonyMethod(typeof(MobObserverHandoff), "BeforeTransfer"),
                    postfix: new HarmonyMethod(typeof(MobObserverHandoff), "AfterTransfer"));
                hooks.Patch(AccessTools.Method(typeof(ZNet), "OnNewConnection"), postfix: new HarmonyMethod(typeof(MobObserverHandoff), "Connected"));
                hooks.Patch(AccessTools.Method(typeof(ZNet), "RPC_PeerInfo"), postfix: new HarmonyMethod(typeof(MobObserverHandoff), "PeerReady"));
                hooks.Patch(AccessTools.Method(typeof(ZNet), "Shutdown"), prefix: new HarmonyMethod(typeof(MobObserverHandoff), "Reset"));
                hooks.Patch(AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData"), postfix: new HarmonyMethod(typeof(MobObserverHandoff), "Tick"));
                hooks.Patch(AccessTools.Method(typeof(BaseAI), "Awake"), postfix: new HarmonyMethod(typeof(MobObserverHandoff), "Awoke"));
                Context();
                MobHandoffVisual.SourcePreview = PublishPreview;
                if (!ReferenceEquals(network, null) && network) foreach (ZNetPeer peer in network.GetPeers()) { Connected(peer); Probe(peer); }
            }
            catch (Exception error) { Stop(); Plugin.Report("Mob spectator bridge inactive: " + error.GetBaseException().Message); }
        }
        internal static void Stop()
        {
            MobHandoffVisual.SourcePreview = null;
            if (hooks != null) hooks.UnpatchSelf(); hooks = null; Reset();
        }
        private static void Reset()
        {
            pending.Clear(); active.Clear(); scratch.Clear(); pumping = false;
            capabilities = new ConditionalWeakTable<ZRpc, Capability>();
            delivered = new ConditionalWeakTable<ZDO, Fence>(); world = null; network = null;
        }
        private static void Context()
        {
            if (ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance)) return;
            pending.Clear(); active.Clear(); scratch.Clear(); delivered = new ConditionalWeakTable<ZDO, Fence>();
            // Connection handlers survive a world replacement in the same
            // network session; readiness belongs to those live RPC objects.
            world = ZDOMan.instance; network = ZNet.instance;
        }
        private static bool Trusted(ZNetPeer peer, ZRpc rpc)
        {
            if (!network || peer == null || !peer.IsReady() || !ReferenceEquals(peer.m_rpc, rpc)) return false;
            return network.IsServer() ? ReferenceEquals(network.GetPeer(peer.m_uid), peer) :
                peer.m_server && ReferenceEquals(network.GetServerPeer(), peer);
        }
        private static bool Capable(ZNetPeer peer)
        {
            Capability state;
            return peer != null && peer.IsReady() && peer.m_rpc != null && capabilities.TryGetValue(peer.m_rpc, out state) && state.Ready;
        }
        private static void Connected(ZNetPeer __0)
        {
            if (hooks == null || __0 == null || __0.m_rpc == null) return;
            ZNetPeer peer = __0; Capability state = capabilities.GetOrCreateValue(peer.m_rpc);
            if (state.Registered) return; state.Registered = true;
            peer.m_rpc.Register<int>(CapabilityRpc, delegate(ZRpc rpc, int protocol)
            {
                if (hooks == null) return;
                Context();
                if (!Trusted(peer, rpc) || (protocol != Protocol && protocol != -Protocol)) return;
                capabilities.GetOrCreateValue(rpc).Ready = true;
                if (protocol == Protocol) rpc.Invoke(CapabilityRpc, -Protocol);
            });
            peer.m_rpc.Register<ZDOID, ZPackage>(TransitionRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
            {
                try { Receive(peer, rpc, id, packet); }
                catch (Exception error) { Plugin.Warn("Mob spectator transition ignored: " + error.GetBaseException().Message); }
            });
            peer.m_rpc.Register<ZDOID, ZPackage>(PreviewRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
            { try { ReceivePreview(peer, rpc, id, packet); } catch (Exception error) { Plugin.Warn("Mob visual frame ignored: " + error.GetBaseException().Message); } });
        }
        private static void PeerReady(ZRpc rpc)
        {
            if (hooks == null || !ZNet.instance) return;
            Context();
            foreach (ZNetPeer peer in network.GetPeers())
                if (ReferenceEquals(peer.m_rpc, rpc)) { Connected(peer); Probe(peer); break; }
        }
        private static void Probe(ZNetPeer peer)
        { if (peer != null && peer.IsReady() && peer.m_rpc != null) peer.m_rpc.Invoke(CapabilityRpc, Protocol); }
        private static bool HasObserver(long source, long destination, Vector3 position)
        {
            foreach (ZNetPeer peer in network.GetPeers())
                if (peer.m_uid != source && peer.m_uid != destination && Capable(peer) && ZNetScene.InActiveArea(position, peer.GetRefPos())) return true;
            return false;
        }
        private static float Window(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_socket == null) return .04f;
                float local, remote, sent, received; int ping;
                peer.m_socket.GetConnectionQuality(out local, out remote, out ping, out sent, out received);
                return Math.Max(.04f, Math.Min(.5f, ping > 0 ? ping / 1000f : 0f));
            }
            catch (Exception) { return .04f; }
        }
        private static void BeforeTransfer(ZDO __0, long __1, out Transition __state)
        {
            __state = null;
            try
            {
                if (hooks == null || __0 == null || !ZNet.instance || !ZNetScene.instance || !__0.IsValid() || !__0.IsOwner() ||
                    __1 == 0 || __0.GetOwner() == __1 || __0.OwnerRevision >= UInt16.MaxValue) return;
                Context(); if (world == null || !ReferenceEquals(world.GetZDO(__0.m_uid), __0)) return;
                long source = __0.GetOwner();
                ZNetPeer server = network.IsServer() ? null : network.GetServerPeer();
                if (network.IsServer() ? !HasObserver(source, __1, __0.GetPosition()) : !Capable(server)) return;
                ZNetView view = ZNetScene.instance.FindInstance(__0);
                Character actor = view && view.IsValid() && ReferenceEquals(view.GetZDO(), __0) ? view.GetComponent<Character>() : null;
                BaseAI ai = actor ? view.GetComponent<BaseAI>() : null;
                if (!actor || actor is Player || actor.IsDead() || !ai) return;
                Character target = FgnTargetBoundary.FreshTarget(ai) ? ai.GetTargetCreature() : null;
                Transition state = new Transition { Id = __0.m_uid, Data = __0, Source = source, Destination = __1,
                    Revision = __0.OwnerRevision, Prefab = __0.GetPrefab(), Velocity = Vector3.ClampMagnitude(actor.GetVelocity(), 20f),
                    Direction = Vector3.ClampMagnitude(actor.GetMoveDir(), 1f), Target = target ? target.GetZDOID() : ZDOID.None,
                    Flight = actor.IsFlying(), RotationFactor = rotationFactor(actor), Window = Window(server), Until = Time.realtimeSinceStartupAsDouble + Lifetime };
                if (Valid(state)) __state = state;
            }
            catch (Exception error) { Plugin.Warn("Mob spectator capture ignored: " + error.GetBaseException().Message); }
        }
        private static void AfterTransfer(Transition __state)
        {
            try
            {
                if (__state == null || hooks == null) return;
                Context();
                ZDO data = __state.Data;
                if (RecordedActive != null && RecordedActive(data)) return;
                if (world == null || !ReferenceEquals(world.GetZDO(__state.Id), data) || !Committed(data, __state)) return;
                if (!network.IsServer()) { __state.Until = Time.realtimeSinceStartupAsDouble + 1.3; if (active.Count < Limit || active.ContainsKey(__state.Id)) active[__state.Id] = __state; }
                // The common transfer may SyncNow before its owner write.
                // Capture that real final cutoff, not the prefix's older one.
                __state.Cutoff = data.DataRevision;
                if (network.IsServer()) Relay(data, __state);
                else
                {
                    ZNetPeer server = network.GetServerPeer();
                    if (Capable(server)) server.m_rpc.Invoke(TransitionRpc, __state.Id, Encode(__state));
                }
            }
            catch (Exception error) { Plugin.Warn("Mob spectator commit ignored: " + error.GetBaseException().Message); }
        }
        private static bool Valid(Transition state)
        {
            return !state.Id.IsNone() && state.Source != 0 && state.Destination != 0 && state.Source != state.Destination && state.Revision < UInt16.MaxValue &&
                MobVisualCurve.Finite(state.Velocity) && state.Velocity.sqrMagnitude <= 400.01f &&
                MobVisualCurve.Finite(state.Direction) && state.Direction.sqrMagnitude <= 1.01f &&
                MobVisualCurve.Finite(state.Window) && state.Window >= .04f && state.Window <= .5f &&
                MobVisualCurve.Finite(state.RotationFactor) && state.RotationFactor >= 0f && state.RotationFactor <= 1f;
        }
        private static bool OldOrCommitted(ZDO data, Transition state)
        {
            return data != null && data.IsValid() && data.GetPrefab() == state.Prefab &&
                (data.OwnerRevision == state.Revision && data.GetOwner() == state.Source || Committed(data, state));
        }
        private static bool Committed(ZDO data, Transition state)
        { return data != null && data.IsValid() && data.GetPrefab() == state.Prefab && data.OwnerRevision == state.Revision + 1U && data.GetOwner() == state.Destination; }
        private static ZPackage Encode(Transition state)
        {
            ZPackage packet = new ZPackage(); packet.Write((byte)Protocol); packet.Write(state.Source); packet.Write(state.Destination);
            packet.Write(state.Revision); packet.Write(state.Cutoff); packet.Write(state.Prefab); packet.Write(state.Target); packet.Write(state.Velocity);
            packet.Write(state.Direction); packet.Write((byte)(state.Flight ? 1 : 0)); packet.Write(state.Window); packet.Write(state.RotationFactor); return packet;
        }
        private static Transition Decode(ZDOID id, ZPackage packet)
        {
            if (id.IsNone() || packet == null || packet.Size() > 96 || packet.ReadByte() != Protocol) return null;
            Transition state = new Transition { Id = id, Source = packet.ReadLong(), Destination = packet.ReadLong(), Revision = packet.ReadUInt(), Cutoff = packet.ReadUInt(),
                Prefab = packet.ReadInt(), Target = packet.ReadZDOID(), Velocity = packet.ReadVector3(), Direction = packet.ReadVector3() };
            byte flags = packet.ReadByte(); state.Flight = flags == 1; state.Window = packet.ReadSingle(); state.RotationFactor = packet.ReadSingle();
            state.Until = Time.realtimeSinceStartupAsDouble + Lifetime;
            return flags <= 1 && packet.GetPos() == packet.Size() && Valid(state) ? state : null;
        }
        private static bool Seen(ZDO data, Transition state)
        { Fence fence; return delivered.TryGetValue(data, out fence) && fence.Applied && fence.Revision >= state.Revision; }
        private static void Mark(ZDO data, Transition state)
        { Fence fence = delivered.GetOrCreateValue(data); fence.Revision = state.Revision; fence.Applied = true; }
        private static void Receive(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage packet)
        {
            if (hooks == null) return;
            Context(); if (world == null || !Trusted(peer, rpc) || !Capable(peer)) return;
            Transition state = Decode(id, packet); if (state == null) return;
            bool server = network.IsServer(); ZDO data = world.GetZDO(id);
            // A source may publish its descriptor before its native record is
            // received, or after that record. Neither case authorizes a guessed
            // future epoch, a third-party source or an already superseded one.
            if (server && (state.Source != peer.m_uid || data == null || !OldOrCommitted(data, state))) return;
            if (!server && (state.Source == ZDOMan.GetSessionID() || state.Destination == ZDOMan.GetSessionID() ||
                data != null && !OldOrCommitted(data, state))) return;
            if (data != null && Seen(data, state)) return;
            Transition prior;
            if (pending.TryGetValue(id, out prior) && prior.Revision >= state.Revision) return;
            if (!pending.ContainsKey(id) && pending.Count >= Limit) { Tick(); if (pending.Count >= Limit) return; }
            pending[id] = state;
            Attempt(id, state);
        }
        private static void Relay(ZDO data, Transition state)
        {
            if (Seen(data, state)) return;
            Mark(data, state);
            state.Until = Math.Min(state.Until, Time.realtimeSinceStartupAsDouble + 1.3);
            if (active.Count < Limit || active.ContainsKey(state.Id)) active[state.Id] = state;
            ZPackage packet = null;
            foreach (ZNetPeer peer in network.GetPeers())
            {
                if (peer.m_uid == state.Source || peer.m_uid == state.Destination || !Capable(peer) ||
                    !ZNetScene.InActiveArea(data.GetPosition(), peer.GetRefPos())) continue;
                if (packet == null) packet = Encode(state);
                peer.m_rpc.Invoke(TransitionRpc, state.Id, packet);
            }
        }
        private static void Attempt(ZDOID id, Transition state)
        {
            if (state.Until < Time.realtimeSinceStartupAsDouble) { pending.Remove(id); return; }
            ZDO data = world.GetZDO(id);
            if (data == null) return;
            if (!OldOrCommitted(data, state) || Seen(data, state)) { pending.Remove(id); return; }
            if (!Committed(data, state)) return;
            // Never start a fresh preview from source motion after a newer
            // native simulation update has already reached this recipient.
            // Wait for the cutoff snapshot if its owner header arrived first.
            if (data.DataRevision < state.Cutoff) return;
            if (data.DataRevision > state.Cutoff) { pending.Remove(id); Mark(data, state); return; }
            if (network.IsServer()) { pending.Remove(id); Relay(data, state); return; }
            ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            if (!view || !view.IsValid() || !ReferenceEquals(view.GetZDO(), data)) return;
            pending.Remove(id); Mark(data, state);
            state.Until = Math.Min(state.Until, Time.realtimeSinceStartupAsDouble + 1.3);
            if (active.Count < Limit || active.ContainsKey(id)) active[id] = state;
            object model = MobHandoffVisual.CaptureObserver(data, state.Source, state.Destination, state.Revision, state.Target,
                state.Velocity, state.Direction, state.Flight, Math.Max(state.Window, Window(network.GetServerPeer())), state.RotationFactor);
            if (model != null) MobHandoffVisual.CommitObserver(model);
            if (model != null && state.Frame != 0) MobHandoffVisual.ReceivePreview(data, state.Source, state.Destination, state.Revision, state.Preview, state.Yaw);
        }
        private static void Awoke(BaseAI __instance)
        {
            if (pending.Count == 0 || !__instance) return;
            try
            {
                Context(); if (world == null || !network) return;
                ZNetView view = __instance.GetComponent<ZNetView>(); ZDO data = view && view.IsValid() ? view.GetZDO() : null;
                Transition state;
                if (data != null && pending.TryGetValue(data.m_uid, out state)) Attempt(data.m_uid, state);
            }
            catch (Exception error) { Plugin.Warn("Mob spectator load ignored: " + error.GetBaseException().Message); }
        }
        internal static void Tick()
        {
            if (hooks == null || (pending.Count == 0 && active.Count == 0) || pumping) return;
            pumping = true;
            try
            {
                Context(); if (world == null || !network) return;
                scratch.Clear(); foreach (ZDOID id in pending.Keys) scratch.Add(id);
                foreach (ZDOID id in scratch) { Transition state; if (pending.TryGetValue(id, out state)) Attempt(id, state); }
                scratch.Clear(); foreach (KeyValuePair<ZDOID, Transition> pair in active)
                    if (pair.Value.Until < Time.realtimeSinceStartupAsDouble || !Committed(world.GetZDO(pair.Key), pair.Value)) scratch.Add(pair.Key);
                foreach (ZDOID id in scratch) active.Remove(id);
            }
            catch (Exception error) { pending.Clear(); Plugin.Warn("Mob spectator queue cleared: " + error.GetBaseException().Message); }
            finally { scratch.Clear(); pumping = false; }
        }
        private static void PublishPreview(ZDO data, Vector3 position, float yaw)
        {
            Transition state; double now = Time.realtimeSinceStartupAsDouble;
            if (hooks == null || network == null || network.IsServer() || !active.TryGetValue(data.m_uid, out state) || now > state.Until || now < state.NextFrame || !Committed(data, state)) return;
            state.NextFrame = now + .05; state.Frame++;
            ZNetPeer server = network.GetServerPeer(); if (!Capable(server)) return;
            ZPackage packet = new ZPackage(); packet.Write(state.Source); packet.Write(state.Destination); packet.Write(state.Revision); packet.Write(state.Frame); packet.Write(position); packet.Write(yaw);
            server.m_rpc.Invoke(PreviewRpc, state.Id, packet);
        }
        private static void ReceivePreview(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage packet)
        {
            if (hooks == null || packet == null || packet.Size() != 40) return;
            Context(); if (world == null || !Trusted(peer, rpc) || !Capable(peer)) return;
            long source = packet.ReadLong(), destination = packet.ReadLong(); uint revision = packet.ReadUInt(), frame = packet.ReadUInt();
            Vector3 position = packet.ReadVector3(); float yaw = packet.ReadSingle(); Transition state;
            if (!active.TryGetValue(id, out state) && !pending.TryGetValue(id, out state)) return;
            if (state.Source != source || state.Destination != destination || state.Revision != revision || frame <= state.Frame || state.Until < Time.realtimeSinceStartupAsDouble ||
                !MobVisualCurve.Finite(position) || !MobVisualCurve.Finite(yaw)) return;
            ZDO data = world.GetZDO(id);
            if (data == null || !OldOrCommitted(data, state) || (position - data.GetPosition()).sqrMagnitude > 25f) return;
            if (network.IsServer())
            {
                if (source != peer.m_uid || !Committed(data, state) || Time.realtimeSinceStartupAsDouble < state.NextFrame) return;
                state.NextFrame = Time.realtimeSinceStartupAsDouble + .04;
            }
            else if (source == ZDOMan.GetSessionID() || destination == ZDOMan.GetSessionID()) return;
            state.Frame = frame; state.Preview = position; state.Yaw = yaw;
            if (network.IsServer())
            {
                foreach (ZNetPeer recipient in network.GetPeers())
                    if (recipient.m_uid != source && recipient.m_uid != destination && Capable(recipient) && ZNetScene.InActiveArea(data.GetPosition(), recipient.GetRefPos())) recipient.m_rpc.Invoke(PreviewRpc, id, packet);
            }
            else if (Committed(data, state)) MobHandoffVisual.ReceivePreview(data, source, destination, revision, position, yaw);
        }
    }
}
