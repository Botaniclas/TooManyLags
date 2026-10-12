using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.GridHandoffs
{
    // Only the normal dedicated-server interactive-grid path uses this protocol.
    // Commits are sent once. Ordered per-connection token fencing makes status
    // queries safe without a growing history of completed object transactions.
    internal static class Protocol
    {
        private const string RpcName = "TooManyLags_GridHandoff_1";
        private enum Message { Hello, Welcome, Commit, Status, Result, Ready, ReadyResult, Relay, ReadyLost }
        private sealed class Connection { internal ZNetPeer Peer; internal bool Capable; internal readonly CommitFence Fence = new CommitFence(); }
        private sealed class Advertisement { internal ZNetPeer Peer; internal ZDOID Id; internal long Token; }
        private sealed class StateFloor { internal long Owner; internal uint Revision, State; }
        private sealed class Readiness
        {
            internal ZDO Data; internal ZRpc Rpc; internal long Owner, Token;
            internal uint Revision, MinimumState;
            internal float Next; internal bool Ready;
        }
        private sealed class KindCache { internal Kinds Kind; }
        private sealed class Lease { internal float Until; }
        private static readonly Dictionary<ZRpc, Connection> connections = new Dictionary<ZRpc, Connection>();
        private static readonly Dictionary<ZDOID, Readiness> ready = new Dictionary<ZDOID, Readiness>();
        private static readonly List<ZDOID> retiring = new List<ZDOID>();
        private static ConditionalWeakTable<ZNetView, KindCache> kinds = new ConditionalWeakTable<ZNetView, KindCache>();
        private static ConditionalWeakTable<ZDO, Lease> refills = new ConditionalWeakTable<ZDO, Lease>();
        private static ConditionalWeakTable<ZNetView, Advertisement> advertised = new ConditionalWeakTable<ZNetView, Advertisement>();
        private static ConditionalWeakTable<ZDO, StateFloor> floors = new ConditionalWeakTable<ZDO, StateFloor>();
        private static readonly Dictionary<Vector2s, bool> areas = new Dictionary<Vector2s, bool>();
        private static int frame = -1;
        private static long readinessToken;
        private static Harmony patches;
        private static Action<ZRpc, ZDOID, long> nativeCommit;

        internal static void Start(Assembly fgn)
        {
            MethodInfo method = fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true).GetMethod("OnHandOff", BindingFlags.Static | BindingFlags.NonPublic);
            nativeCommit = (Action<ZRpc, ZDOID, long>)Delegate.CreateDelegate(typeof(Action<ZRpc, ZDOID, long>), method);
            FgnOwnerHandoff.CooperativeClient = Capable;
            FgnOwnerHandoff.IdentifiedSend = SendCommit;
            FgnOwnerHandoff.IdentifiedStatus = SendStatus;
            RequestRouter.RecipientReady = Ready;
            AuthorityController.ReadinessRemoved = Forget;
            AuthorityController.ReadinessReset = ClearReadiness;
            patches = new Harmony(Plugin.Id + ".gridhandoff");
            foreach (Type hook in new[] { typeof(ConnectPatch), typeof(PeerReadyPatch), typeof(ReceivePatch), typeof(UnloadPatch), typeof(ShutdownPatch), typeof(DisconnectPatch) })
                patches.CreateClassProcessor(hook).Patch();
        }
        internal static void Stop()
        {
            if (patches != null) patches.UnpatchSelf(); patches = null;
            FgnOwnerHandoff.CooperativeClient = null; FgnOwnerHandoff.IdentifiedSend = null; FgnOwnerHandoff.IdentifiedStatus = null;
            RequestRouter.RecipientReady = null; AuthorityController.ReadinessRemoved = null; AuthorityController.ReadinessReset = null; nativeCommit = null; Reset();
        }
        private static void Reset()
        { connections.Clear(); ready.Clear(); retiring.Clear(); kinds = new ConditionalWeakTable<ZNetView, KindCache>(); refills = new ConditionalWeakTable<ZDO, Lease>(); advertised = new ConditionalWeakTable<ZNetView, Advertisement>(); floors = new ConditionalWeakTable<ZDO, StateFloor>(); areas.Clear(); frame = -1; }
        internal static bool Capable(ZNetPeer peer)
        {
            Connection c;
            return peer != null && peer.IsReady() && peer.m_rpc != null && connections.TryGetValue(peer.m_rpc, out c) &&
                ReferenceEquals(c.Peer, peer) && c.Capable && Current(c, peer.m_rpc);
        }
        private static bool Current(Connection c, ZRpc rpc)
        { return ZNet.instance && ReferenceEquals(ZNet.instance.GetPeer(c.Peer.m_uid), c.Peer) && ReferenceEquals(c.Peer.m_rpc, rpc); }
        private static void Connect(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null || connections.ContainsKey(peer.m_rpc)) return;
            Connection c = new Connection { Peer = peer }; connections.Add(peer.m_rpc, c);
            peer.m_rpc.Register<ZPackage>(RpcName, delegate(ZRpc rpc, ZPackage packet) { Receive(c, rpc, packet); });
        }
        private static void PeerReady(ZRpc rpc)
        {
            Connection c;
            if (!connections.TryGetValue(rpc, out c) || !Current(c, rpc) || !c.Peer.IsReady()) return;
            // The server advertises the active mode. Clients can register this
            // endpoint regardless of their local server-only mode setting.
            if (ZNet.instance.IsDedicated())
                try { Send(c.Peer, Message.Welcome, null); } catch (Exception error) { Plugin.Warn("Grid capability offer failed: " + error.GetBaseException().Message); }
        }
        private static void Send(ZNetPeer peer, Message message, Action<ZPackage> write)
        {
            ZPackage packet = new ZPackage(); packet.Write((int)message); if (write != null) write(packet);
            peer.m_rpc.Invoke(RpcName, packet);
        }
        private static void SendCommit(ZNetPeer peer, ZDOID id, long target, uint revision, long token)
        { Send(peer, Message.Commit, delegate(ZPackage p) { p.Write(token); p.Write(id); p.Write(target); p.Write(revision); }); }
        private static void SendStatus(ZNetPeer peer, ZDOID id, long token)
        { Send(peer, Message.Status, delegate(ZPackage p) { p.Write(token); p.Write(id); }); }
        private static void Receive(Connection c, ZRpc rpc, ZPackage packet)
        {
            if (!Current(c, rpc) || !c.Peer.IsReady() || packet == null || ZDOMan.instance == null) return;
            try
            {
                Message message = (Message)packet.ReadInt(); bool server = ZNet.instance.IsDedicated();
                // Relays preserve native payload sizes (e.g. large map data).
                // Only this protocol's small control messages have a new cap.
                if (message != Message.Relay && packet.Size() > 256) return;
                if (server)
                {
                    if (message == Message.Hello)
                    { bool changed = !c.Capable; c.Capable = true; Send(c.Peer, Message.Welcome, null); if (changed && Plugin.Controller != null) Plugin.Controller.CapabilityChanged(c.Peer.m_uid); return; }
                    if (!c.Capable || Plugin.Controller == null) return;
                    if (message == Message.Result)
                    {
                        long token = packet.ReadLong(); ZDOID id = packet.ReadZDOID(); bool processed = packet.ReadBool(), present = packet.ReadBool();
                        long owner = packet.ReadLong(); uint revision = packet.ReadUInt(), state = packet.ReadUInt();
                        if (Plugin.Controller.Cooperative == null || !Plugin.Controller.Cooperative.Observed(rpc, id, token, processed, present, owner, revision)) return;
                        ZDO data = ZDOMan.instance.GetZDO(id);
                        Readiness previous;
                        if ((!present || owner != c.Peer.m_uid) && ready.TryGetValue(id, out previous) && ReferenceEquals(previous.Rpc, rpc)) ready.Remove(id);
                        if (present && data != null) SetFloor(data, owner, revision, state);
                        Plugin.Controller.Router.Flush(id); return;
                    }
                    if (message == Message.ReadyResult)
                    {
                        ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(); bool available = packet.ReadBool(); Readiness r;
                        if (ready.TryGetValue(id, out r) && ReferenceEquals(r.Rpc, rpc) && r.Token == token) { r.Ready = available; r.Next = Time.unscaledTime + .5f; }
                        Plugin.Controller.Router.Flush(id); return;
                    }
                    if (message == Message.ReadyLost)
                    {
                        ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(); Readiness r;
                        if (ready.TryGetValue(id, out r) && ReferenceEquals(r.Rpc, rpc) && r.Token == token) { r.Ready = false; r.Next = 0; }
                        return;
                    }
                    if (message == Message.Relay)
                    {
                        ZRoutedRpc.RoutedRPCData request = new ZRoutedRpc.RoutedRPCData(); request.Deserialize(packet);
                        if (request.m_targetPeerID != c.Peer.m_uid || request.m_targetZDO.IsNone()) return;
                        ready.Remove(request.m_targetZDO);
                        Plugin.Controller.Router.Forwarded(request); return;
                    }
                    return;
                }
                if (!c.Peer.m_server || !ReferenceEquals(ZNet.instance.GetServerPeer(), c.Peer)) return;
                if (message == Message.Welcome)
                { bool changed = !c.Capable; c.Capable = true; if (changed) Send(c.Peer, Message.Hello, null); return; }
                if (message != Message.Commit && message != Message.Status && message != Message.Ready) return;
                c.Capable = true; // Authenticated server endpoint; no exact-version gate.
                if (message == Message.Ready)
                {
                    ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(), owner = packet.ReadLong(); uint revision = packet.ReadUInt(), minimum = packet.ReadUInt();
                    ZDO data = ZDOMan.instance.GetZDO(id); ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
                    bool available = data != null && data.IsValid() && data.GetOwner() == owner && owner == ZDOMan.GetSessionID() &&
                        data.OwnerRevision == revision && data.DataRevision >= minimum && view && view.IsValid() && view.gameObject.activeInHierarchy && AreaReady(data.GetPosition());
                    if (!available && data != null) ZDOMan.instance.ForceSendZDO(id);
                    if (available)
                    { advertised.Remove(view); advertised.Add(view, new Advertisement { Peer = c.Peer, Id = id, Token = token }); }
                    Send(c.Peer, Message.ReadyResult, delegate(ZPackage p) { p.Write(id); p.Write(token); p.Write(available); }); return;
                }
                long commitToken = packet.ReadLong(); ZDOID objectId = packet.ReadZDOID();
                if (commitToken <= 0) return;
                if (message == Message.Commit)
                {
                    long target = packet.ReadLong(); uint expected = packet.ReadUInt();
                    if (c.Fence.Begin(commitToken))
                    {
                        // Fence before calling native code, including an exception
                        // after SetOwner. A status query never re-executes it.
                        ZDO data = ZDOMan.instance.GetZDO(objectId);
                        ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
                        Lease lease;
                        if (data != null && data.IsOwner() && data.OwnerRevision == expected && expected < UInt16.MaxValue &&
                            target != 0 && target != ZDOMan.GetSessionID() && view && view.IsValid() && view.gameObject.activeInHierarchy &&
                            data.GetLong(ZDOVars.s_user, 0) == 0 && !StandAction(view) && (!refills.TryGetValue(data, out lease) || Time.unscaledTime >= lease.Until))
                        {
                            try { nativeCommit(rpc, objectId, target); }
                            catch (Exception error) { Plugin.Warn("Native grid handoff failed: " + error.GetBaseException().Message); }
                        }
                    }
                }
                else c.Fence.Settle(commitToken);
                Report(c, objectId, commitToken);
            }
            catch (Exception error) { Plugin.Warn("Grid handoff packet rejected: " + error.GetBaseException().Message); }
        }
        private static void Report(Connection c, ZDOID id, long token)
        {
            ZDO data = ZDOMan.instance.GetZDO(id); bool present = data != null && data.IsValid();
            long owner = present ? data.GetOwner() : 0; uint revision = present ? (uint)data.OwnerRevision : 0, state = present ? data.DataRevision : 0;
            if (present) ZDOMan.instance.ForceSendZDO(id);
            Send(c.Peer, Message.Result, delegate(ZPackage p) { p.Write(token); p.Write(id); p.Write(c.Fence.Decided(token)); p.Write(present); p.Write(owner); p.Write(revision); p.Write(state); });
        }
        private static bool AreaReady(Vector3 point)
        {
            if (frame != Time.frameCount) { frame = Time.frameCount; areas.Clear(); }
            Vector2s zone = ZoneSystem.GetZone(point); bool result;
            if (!areas.TryGetValue(zone, out result)) { result = ZNetScene.instance.IsAreaReady(point); areas.Add(zone, result); }
            return result;
        }
        private static bool StandAction(ZNetView view)
        {
            ItemStand item = view.GetComponent<ItemStand>();
            if (item && (item.IsInvoking("UpdateAttach") || item.IsInvoking("UpdateOrientation"))) return true;
            ArmorStand armor = view.GetComponent<ArmorStand>();
            return armor && armor.IsInvoking("UpdateAttach");
        }
        private static void SetFloor(ZDO data, long owner, uint revision, uint state)
        {
            StateFloor floor = floors.GetOrCreateValue(data); floor.Owner = owner; floor.Revision = revision; floor.State = state;
            Readiness r;
            if (ready.TryGetValue(data.m_uid, out r) && ReferenceEquals(r.Data, data) && r.Owner == owner && r.Revision == revision && state > r.MinimumState)
            { r.MinimumState = state; r.Ready = false; r.Next = 0; }
        }
        internal static bool Ready(ZDOMan world, long owner, ZDO data)
        {
            ZNetPeer peer = ZNet.instance ? ZNet.instance.GetPeer(owner) : null;
            // Peers without this capability keep native delivery; automatic
            // grid transfers to/from them are disabled separately.
            if (!Capable(peer)) return true;
            Readiness r;
            if (!ready.TryGetValue(data.m_uid, out r) || !ReferenceEquals(r.Data, data) || r.Owner != owner ||
                r.Revision != data.OwnerRevision || !ReferenceEquals(r.Rpc, peer.m_rpc))
            { r = new Readiness { Data = data, Rpc = peer.m_rpc, Owner = owner, Revision = data.OwnerRevision, MinimumState = data.DataRevision }; ready[data.m_uid] = r; }
            StateFloor floor;
            if (floors.TryGetValue(data, out floor) && floor.Owner == owner && floor.Revision == r.Revision && floor.State > r.MinimumState)
            { r.MinimumState = floor.State; r.Ready = false; r.Next = 0; }
            if (r.Ready) return true;
            if (Time.unscaledTime >= r.Next)
            {
                r.Next = Time.unscaledTime + .5f; r.Token = ++readinessToken; world.ForceSendZDO(owner, data.m_uid);
                try { Send(peer, Message.Ready, delegate(ZPackage p) { p.Write(data.m_uid); p.Write(r.Token); p.Write(owner); p.Write(r.Revision); p.Write(r.MinimumState); }); }
                catch (Exception error) { Plugin.Warn("Recipient readiness query failed: " + error.GetBaseException().Message); }
            }
            return false;
        }
        internal static void Forget(ZDO data) { if (data != null) { ready.Remove(data.m_uid); floors.Remove(data); } }
        private static void ClearReadiness() { ready.Clear(); floors = new ConditionalWeakTable<ZDO, StateFloor>(); }
        private static void Unload(ZNetView view)
        {
            Advertisement a;
            if (advertised.TryGetValue(view, out a))
            {
                advertised.Remove(view);
                if (Capable(a.Peer))
                    try { Send(a.Peer, Message.ReadyLost, delegate(ZPackage p) { p.Write(a.Id); p.Write(a.Token); }); } catch (Exception) { }
            }
            ZDO data = view.GetZDO(); Forget(data); kinds.Remove(view); if (data != null) refills.Remove(data); areas.Clear();
        }
        private static void Disconnect(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;
            connections.Remove(peer.m_rpc);
            // Rare disconnect event, not an idle scan. Release old transports
            // immediately even when their objects remain loaded for other peers.
            retiring.Clear();
            foreach (KeyValuePair<ZDOID, Readiness> item in ready)
                if (ReferenceEquals(item.Value.Rpc, peer.m_rpc)) retiring.Add(item.Key);
            foreach (ZDOID id in retiring) ready.Remove(id);
            retiring.Clear();
        }
        private static bool ReceiveRequest(ZRoutedRpc.RoutedRPCData request)
        {
            if (!ZNet.instance || ZNet.instance.IsServer() || request == null || request.m_targetPeerID == 0 || request.m_targetZDO.IsNone() ||
                request.m_targetPeerID != ZDOMan.GetSessionID()) return true;
            // Reject unrelated messages before touching object data or components.
            if (!ObjectCatalog.AnyOwnerRequest(request.m_methodHash)) return true;
            ZNetPeer server = ZNet.instance.GetServerPeer(); if (!Capable(server)) return true;
            ZDO data = ZDOMan.instance.GetZDO(request.m_targetZDO); ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            if (view && view.IsValid())
            {
                KindCache kind;
                if (!kinds.TryGetValue(view, out kind)) { kind = new KindCache { Kind = ObjectCatalog.Classify(view.gameObject) }; kinds.Add(view, kind); }
                if (kind.Kind == Kinds.None || kind.Kind == Kinds.TamedAnimals || !ObjectCatalog.OwnerRequest(kind.Kind, request.m_methodHash)) return true;
                if (data.IsOwner() && view.gameObject.activeInHierarchy)
                {
                    if (ObjectCatalog.RefillRequest(kind.Kind, request.m_methodHash))
                    { Lease lease = refills.GetOrCreateValue(data); lease.Until = Time.unscaledTime + 10; }
                    return true;
                }
            }
            if (data != null) ZDOMan.instance.ForceSendZDO(request.m_targetZDO);
            // A single relay, before any native effect. The server holds it
            // until actual recipient readiness; it does not immediately bounce.
            Send(server, Message.Relay, delegate(ZPackage p) { request.Serialize(p); }); return false;
        }
        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        private static class ConnectPatch { [HarmonyPostfix] private static void Postfix(ZNetPeer peer) { Connect(peer); } }
        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static class PeerReadyPatch { [HarmonyPostfix] private static void Postfix(ZRpc rpc) { PeerReady(rpc); } }
        [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
        private static class ReceivePatch
        {
            [HarmonyPrefix, HarmonyPriority(Priority.First)] private static bool Prefix(ZRoutedRpc.RoutedRPCData __0, bool __runOriginal)
            {
                if (!__runOriginal) return false;
                try { return ReceiveRequest(__0); }
                catch (Exception error) { Plugin.Warn("Receiver handoff safeguard failed: " + error.GetBaseException().Message); return false; }
            }
        }
        [HarmonyPatch]
        private static class UnloadPatch
        {
            private static IEnumerable<MethodBase> TargetMethods() { yield return AccessTools.Method(typeof(ZNetView), "ResetZDO"); yield return AccessTools.Method(typeof(ZNetView), "OnDestroy"); }
            [HarmonyPrefix] private static void Prefix(ZNetView __instance) { Unload(__instance); }
        }
        [HarmonyPatch(typeof(ZNet), "Shutdown")]
        private static class ShutdownPatch { [HarmonyPrefix] private static void Prefix() { Reset(); } }
        [HarmonyPatch(typeof(ZNet), "Disconnect", new[] { typeof(ZNetPeer) })]
        private static class DisconnectPatch { [HarmonyPrefix] private static void Prefix(ZNetPeer peer) { Disconnect(peer); } }
    }
}
