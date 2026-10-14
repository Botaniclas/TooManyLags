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
        private enum Message { Hello, Welcome, Commit, Status, Result, Ready, ReadyResult, Relay, ReadyLost, Loaded }
        private sealed class Connection { internal ZNetPeer Peer; internal bool Capable, LoadedProof; internal readonly CommitFence Fence = new CommitFence(); internal readonly CapabilityGreeting Greeting = new CapabilityGreeting(); }
        private sealed class Advertisement { internal ZNetPeer Peer; internal ZDOID Id; internal long Token; }
        private sealed class Readiness
        {
            internal ZDO Data; internal ZRpc Rpc; internal long Owner;
            internal uint Revision, MinimumState;
            internal bool BeforeOwner, ReplicaPresent; internal Vector3 Position;
            internal ReadinessProbe Probe;
        }
        private sealed class KindCache { internal Kinds Kind; }
        private sealed class Lease { internal float Until; }
        private struct LocalProbe
        {
            internal Connection Link; internal ZRpc Rpc; internal ZDOID Id;
            internal long Token, Owner; internal uint Revision, Minimum; internal Vector3 Position;
        }
        private static List<LocalProbe> localPending = new List<LocalProbe>(), localWork = new List<LocalProbe>();
        private static readonly Dictionary<ZRpc, Connection> connections = new Dictionary<ZRpc, Connection>();
        private static readonly Dictionary<ZDOID, Readiness> ready = new Dictionary<ZDOID, Readiness>();
        private static readonly List<ZDOID> retiring = new List<ZDOID>();
        private static ConditionalWeakTable<ZNetView, KindCache> kinds = new ConditionalWeakTable<ZNetView, KindCache>();
        private static ConditionalWeakTable<ZDO, Lease> refills = new ConditionalWeakTable<ZDO, Lease>();
        private static ConditionalWeakTable<ZNetView, Advertisement> advertised = new ConditionalWeakTable<ZNetView, Advertisement>();
        private static readonly Dictionary<Vector2s, bool> areas = new Dictionary<Vector2s, bool>();
        private static int frame = -1;
        private static long readinessToken;
        private static Harmony patches;
        private static Action<ZRpc, ZDOID, long> nativeCommit;
        private static bool started, clientGreetingPending;

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
            AuthorityController.DestinationLoaded = Loaded;
            patches = new Harmony(Plugin.Id + ".gridhandoff");
            foreach (Type hook in new[] { typeof(ConnectPatch), typeof(PeerReadyPatch), typeof(ReceivePatch), typeof(UnloadPatch), typeof(ShutdownPatch), typeof(DisconnectPatch) })
                patches.CreateClassProcessor(hook).Patch();
            started = true;
            if (ZNet.instance) foreach (ZNetPeer peer in ZNet.instance.GetPeers()) Connect(peer);
        }
        internal static void Stop()
        {
            if (patches != null) patches.UnpatchSelf(); patches = null;
            FgnOwnerHandoff.CooperativeClient = null; FgnOwnerHandoff.IdentifiedSend = null; FgnOwnerHandoff.IdentifiedStatus = null;
            RequestRouter.RecipientReady = null; AuthorityController.ReadinessRemoved = null; AuthorityController.ReadinessReset = null; AuthorityController.DestinationLoaded = null; nativeCommit = null; started = false; Reset();
        }
        private static void Reset()
        { connections.Clear(); ready.Clear(); retiring.Clear(); localPending.Clear(); localWork.Clear(); kinds = new ConditionalWeakTable<ZNetView, KindCache>(); refills = new ConditionalWeakTable<ZDO, Lease>(); advertised = new ConditionalWeakTable<ZNetView, Advertisement>(); areas.Clear(); frame = -1; clientGreetingPending = false; }
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
        internal static void ClientHello()
        {
            if (!started || !clientGreetingPending || !ZNet.instance || ZNet.instance.IsDedicated()) return;
            ZNetPeer peer = ZNet.instance.GetServerPeer(); Connection c;
            if (peer == null || !peer.IsReady() || peer.m_rpc == null || !connections.TryGetValue(peer.m_rpc, out c) || !Current(c, peer.m_rpc)) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (c.Capable || !c.Greeting.Waiting(now)) { clientGreetingPending = false; return; }
            if (c.Greeting.Due(now))
                try { Send(peer, Message.Hello, delegate(ZPackage p) { p.Write(1); }); } catch (Exception) { }
        }
        internal static void Offer(ZNetPeer peer)
        {
            if (!started || !ZNet.instance || !ZNet.instance.IsDedicated() || peer == null || !peer.IsReady() || peer.m_rpc == null) return;
            Connection c;
            if (connections.TryGetValue(peer.m_rpc, out c) && Current(c, peer.m_rpc))
                try { Send(peer, Message.Welcome, delegate(ZPackage p) { p.Write(c.Capable); }); } catch (Exception) { }
        }
        private static void PeerReady(ZRpc rpc)
        {
            Connection c;
            if (!connections.TryGetValue(rpc, out c) || !Current(c, rpc) || !c.Peer.IsReady()) return;
            // The server advertises the active mode. Clients can register this
            // endpoint regardless of their local server-only mode setting.
            if (ZNet.instance.IsDedicated()) Offer(c.Peer);
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
            if (!Current(c, rpc) || !c.Peer.IsReady() || packet == null) return;
            try
            {
                Message message = (Message)packet.ReadInt(); bool server = ZNet.instance.IsDedicated();
                // Relays preserve native payload sizes (e.g. large map data).
                // Only this protocol's small control messages have a new cap.
                if (message != Message.Relay && packet.Size() > 256) return;
                if (server)
                {
                    if (message == Message.Hello)
                    { bool proof = packet.Size() >= 2 * sizeof(int) && (packet.ReadInt() & 1) != 0;
                      bool changed = !c.Capable || (!c.LoadedProof && proof); c.Capable = true; c.LoadedProof = proof;
                      if (changed && Plugin.Controller != null) Plugin.Controller.CapabilityChanged(c.Peer.m_uid);
                      Send(c.Peer, Message.Welcome, delegate(ZPackage p) { p.Write(true); }); return; }
                    if (!c.Capable || Plugin.Controller == null || ZDOMan.instance == null) return;
                    if (message == Message.Result)
                    {
                        long token = packet.ReadLong(); ZDOID id = packet.ReadZDOID(); bool processed = packet.ReadBool(), present = packet.ReadBool();
                        long owner = packet.ReadLong(); uint revision = packet.ReadUInt(), state = packet.ReadUInt();
                        if (Plugin.Controller.Cooperative == null || !Plugin.Controller.Cooperative.Observed(rpc, id, token, processed, present, owner, revision)) return;
                        ZDO data = ZDOMan.instance.GetZDO(id);
                        // An identified command can beat its preceding native
                        // ownership packet. Repair only that lagging source;
                        // never install its older owner/revision as server truth.
                        if (present && data != null && revision < data.OwnerRevision && data.GetOwner() == c.Peer.m_uid)
                            ZDOMan.instance.ForceSendZDO(c.Peer.m_uid, id);
                        Readiness previous;
                        if ((!present || owner != c.Peer.m_uid) && ready.TryGetValue(id, out previous) && ReferenceEquals(previous.Rpc, rpc)) ready.Remove(id);
                        if (present && data != null) SetFloor(data, owner, revision, state);
                        Plugin.Controller.Router.Flush(id); return;
                    }
                    if (message == Message.ReadyResult)
                    {
                        ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(); bool available = packet.ReadBool();
                        bool replica = packet.GetPos() < packet.Size() && packet.ReadBool(); Readiness r;
                        if (ready.TryGetValue(id, out r) && ReferenceEquals(r.Rpc, rpc) && r.Probe.Response(token, available, Time.unscaledTime) && r.BeforeOwner)
                        {
                            // Knowing the exact data is not loading permission.
                            // Stop resending full state only after its receipt.
                            bool lost = r.ReplicaPresent && !replica; r.ReplicaPresent = replica;
                            if (lost) ZDOMan.instance.ForceSendZDO(c.Peer.m_uid, id);
                            Plugin.Controller.DestinationReply(id, c.Peer.m_uid, available, r.Probe.Next);
                        }
                        Plugin.Controller.Router.Flush(id); return;
                    }
                    if (message == Message.ReadyLost)
                    {
                        ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(); Readiness r;
                        if (ready.TryGetValue(id, out r) && ReferenceEquals(r.Rpc, rpc) && r.Probe.Lost(token)) r.ReplicaPresent = false;
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
                {
                    // New offers distinguish advertisement from acknowledgement.
                    // Old servers acknowledge with a second header-only Welcome.
                    bool? acknowledgement = packet.Size() > sizeof(int) ? packet.ReadBool() : (bool?)null;
                    if (c.Greeting.Welcome(acknowledgement)) { c.Capable = true; clientGreetingPending = false; }
                    else { clientGreetingPending = true; ClientHello(); }
                    return;
                }
                if (ZDOMan.instance == null || (message != Message.Commit && message != Message.Status && message != Message.Ready && message != Message.Loaded)) return;
                c.Capable = true; // Authenticated server endpoint; no exact-version gate.
                if (message == Message.Ready || message == Message.Loaded)
                {
                    ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(), owner = packet.ReadLong(); uint revision = packet.ReadUInt(), minimum = packet.ReadUInt();
                    bool before = message == Message.Loaded; Vector3 position = before ? packet.ReadVector3() : default(Vector3);
                    ZDO data = ZDOMan.instance.GetZDO(id); ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
                    bool replica = token > 0 && data != null && data.IsValid() && data.GetOwner() == owner &&
                        owner == (before ? c.Peer.m_uid : ZDOMan.GetSessionID()) && (!before || data.GetPosition() == position) &&
                        data.OwnerRevision == revision && data.DataRevision >= minimum;
                    bool objectReady = replica && view && view.IsValid() && ReferenceEquals(view.GetZDO(), data) && view.gameObject.activeInHierarchy;
                    // Control RPCs can beat their queued native object packet.
                    // Recheck only this incomplete snapshot after this frame's
                    // network/scene updates, instead of a fixed 0.5-second wait.
                    if (before && !objectReady && localPending.Count < 4096)
                    {
                        localPending.Add(new LocalProbe { Link = c, Rpc = rpc, Id = id, Token = token, Owner = owner,
                            Revision = revision, Minimum = minimum, Position = position }); return;
                    }
                    bool available = objectReady && ZoneSystem.instance && AreaReady(data.GetPosition());
                    // Before ownership the data belongs to the server. Its
                    // authenticated reply requests any needed resync there.
                    if (!available && !before && data != null) ZDOMan.instance.ForceSendZDO(id);
                    if (available)
                    { advertised.Remove(view); advertised.Add(view, new Advertisement { Peer = c.Peer, Id = id, Token = token }); }
                    Send(c.Peer, Message.ReadyResult, delegate(ZPackage p) { p.Write(id); p.Write(token); p.Write(available); if (before) p.Write(replica); }); return;
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
        internal static void FlushDeferred()
        {
            if (localPending.Count == 0) return;
            List<LocalProbe> wave = localWork; localWork = localPending; localPending = wave;
            try
            {
                if (!started || !ZNet.instance || ZNet.instance.IsDedicated() || ZDOMan.instance == null || !ZNetScene.instance) return;
                areas.Clear();
                for (int i = 0; i < localWork.Count; i++)
                {
                    LocalProbe q = localWork[i]; Connection c = q.Link;
                    if (!Current(c, q.Rpc) || !c.Peer.IsReady() || !c.Peer.m_server || !ReferenceEquals(ZNet.instance.GetServerPeer(), c.Peer)) continue;
                    ZDO data = ZDOMan.instance.GetZDO(q.Id); ZNetView view = data != null ? ZNetScene.instance.FindInstance(data) : null;
                    bool replica = q.Token > 0 && data != null && data.IsValid() && data.GetOwner() == q.Owner && q.Owner == c.Peer.m_uid &&
                        data.OwnerRevision == q.Revision && data.DataRevision >= q.Minimum && data.GetPosition() == q.Position;
                    bool available = replica && view && view.IsValid() && ReferenceEquals(view.GetZDO(), data) &&
                        view.gameObject.activeInHierarchy && ZoneSystem.instance && AreaReady(q.Position);
                    if (available)
                    { advertised.Remove(view); advertised.Add(view, new Advertisement { Peer = c.Peer, Id = q.Id, Token = q.Token }); }
                    try { Send(c.Peer, Message.ReadyResult, delegate(ZPackage p) { p.Write(q.Id); p.Write(q.Token); p.Write(available); p.Write(replica); }); }
                    catch (Exception error) { Plugin.Warn("Deferred destination proof failed: " + error.GetBaseException().Message); }
                }
            }
            finally { localWork.Clear(); }
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
            // Update the loaded node only. Cached reply invalidation is checked
            // lazily by Ready, including a reply arriving before the next call.
            if (Plugin.Controller != null) Plugin.Controller.RecordRecipient(data, owner, revision, state);
        }
        internal static bool Ready(ZDOMan world, long owner, ZDO data)
        {
            // Experimental narrow path: unchanged native ownership needs no
            // additional handshake. Real TML transfers and receiver relays arm
            // the existing loaded node, retaining their readiness protection.
            uint minimum;
            if (Plugin.Controller == null || !Plugin.Controller.TryRecipientState(data, owner, out minimum)) return true;
            ZNetPeer peer = ZNet.instance ? ZNet.instance.GetPeer(owner) : null;
            // Peers without this capability keep native delivery; automatic
            // grid transfers to/from them are disabled separately.
            if (!Capable(peer)) return true;
            Readiness r;
            if (!ready.TryGetValue(data.m_uid, out r) || !ReferenceEquals(r.Data, data) || r.Owner != owner ||
                r.BeforeOwner || r.Revision != data.OwnerRevision || !ReferenceEquals(r.Rpc, peer.m_rpc))
            { r = new Readiness { Data = data, Rpc = peer.m_rpc, Owner = owner, Revision = data.OwnerRevision, MinimumState = data.DataRevision }; ready[data.m_uid] = r; }
            if (minimum > r.MinimumState)
            { r.MinimumState = minimum; r.Probe.Invalidate(); }
            if (r.Probe.Ready) return true;
            if (r.Probe.Query(Time.unscaledTime, ref readinessToken))
            {
                world.ForceSendZDO(owner, data.m_uid);
                try { Send(peer, Message.Ready, delegate(ZPackage p) { p.Write(data.m_uid); p.Write(r.Probe.Token); p.Write(owner); p.Write(r.Revision); p.Write(r.MinimumState); }); }
                catch (Exception error) { Plugin.Warn("Recipient readiness query failed: " + error.GetBaseException().Message); }
            }
            return false;
        }
        private static bool Loaded(ZDO data, long target)
        {
            if (data == null || !data.IsValid() || data.GetOwner() != ZDOMan.GetSessionID()) return false;
            ZNetPeer peer = ZNet.instance ? ZNet.instance.GetPeer(target) : null; Connection c;
            if (!Capable(peer) || !connections.TryGetValue(peer.m_rpc, out c) || !c.LoadedProof)
            { if (Plugin.Controller != null) Plugin.Controller.WaitForDestination(data, target, Single.PositiveInfinity); return false; }
            Readiness r; Vector3 position = data.GetPosition();
            if (!ready.TryGetValue(data.m_uid, out r) || !r.BeforeOwner || !ReferenceEquals(r.Data, data) ||
                r.Owner != data.GetOwner() || r.Revision != data.OwnerRevision || r.Position != position || !ReferenceEquals(r.Rpc, peer.m_rpc))
            { r = new Readiness { BeforeOwner = true, Data = data, Rpc = peer.m_rpc, Owner = data.GetOwner(), Revision = data.OwnerRevision,
                MinimumState = data.DataRevision, Position = position }; ready[data.m_uid] = r; }
            // Consume this exceptional preflight once. Normal owner writes
            // need no extra tracking-table lookup or cleanup delegate.
            if (r.Probe.Ready) { ready.Remove(data.m_uid); return true; }
            if (r.Probe.Query(Time.unscaledTime, ref readinessToken))
            {
                if (!r.ReplicaPresent) ZDOMan.instance.ForceSendZDO(target, data.m_uid);
                try { Send(peer, Message.Loaded, delegate(ZPackage p) { p.Write(data.m_uid); p.Write(r.Probe.Token); p.Write(r.Owner); p.Write(r.Revision); p.Write(r.MinimumState); p.Write(r.Position); }); }
                catch (Exception error) { Plugin.Warn("Destination load query failed: " + error.GetBaseException().Message); }
            }
            if (Plugin.Controller != null) Plugin.Controller.WaitForDestination(data, target, r.Probe.Next);
            return false;
        }
        internal static void Forget(ZDO data) { if (data != null) ready.Remove(data.m_uid); }
        private static void ClearReadiness() { ready.Clear(); }
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
            if (localPending.Count != 0) localPending.RemoveAll(delegate(LocalProbe q) { return ReferenceEquals(q.Rpc, peer.m_rpc); });
            if (localWork.Count != 0) localWork.RemoveAll(delegate(LocalProbe q) { return ReferenceEquals(q.Rpc, peer.m_rpc); });
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
