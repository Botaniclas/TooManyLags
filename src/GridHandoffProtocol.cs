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
        private enum Message { Hello, Welcome, Commit, Status, Result, Ready, ReadyResult, Relay, ReadyLost, Loaded, PhysicsCommit, Bundle, TamedCommit }
        private sealed class Connection
        {
            internal ZNetPeer Peer;
            internal bool Capable, LoadedProof, IncineratorGuard, InitialProof, StaticGrid, TextEditing, LooseItems, PhysicsGuard, PhysicsObjects, Batching, Queued;
            internal List<ZPackage> Outgoing;
            internal bool TamedGuard;
            internal int Bytes;
            internal readonly CommitFence Fence = new CommitFence();
            internal readonly CapabilityGreeting Greeting = new CapabilityGreeting();
        }
        private const int BatchRecords = 32, BatchBytes = 4096, ControlBytes = 256;
        private static readonly List<Connection> outgoing = new List<Connection>();
        private static int batchDepth;
        private static bool flushingBatches;
        private sealed class Advertisement { internal ZNetPeer Peer; internal ZDOID Id; internal long Token; }
        private sealed class Readiness
        {
            internal ZDO Data; internal ZRpc Rpc; internal long Owner;
            internal uint Revision, MinimumState;
            internal ReadinessProbe Probe;
        }
        private sealed class KindCache { internal Kinds Kind; internal bool Inspected, Initial; internal Container Inventory; internal Incinerator Incinerator; }
        private sealed class Lease { internal float Until; }
        private struct LocalProbe
        {
            internal Connection Link; internal ZRpc Rpc; internal ZDOID Id;
            internal long Token, Owner; internal uint Revision, Minimum; internal Vector3 Position;
            internal Kinds InitialKind;
        }
        private static List<LocalProbe> localPending = new List<LocalProbe>(), localWork = new List<LocalProbe>();
        private static readonly Dictionary<ZRpc, Connection> connections = new Dictionary<ZRpc, Connection>();
        private static readonly Dictionary<ZDOID, Readiness> ready = new Dictionary<ZDOID, Readiness>();
        private static readonly List<ZDOID> retiring = new List<ZDOID>();
        private static ConditionalWeakTable<ZNetView, KindCache> kinds = new ConditionalWeakTable<ZNetView, KindCache>();
        private static ConditionalWeakTable<ZDO, Lease> refills = new ConditionalWeakTable<ZDO, Lease>();
        private static ConditionalWeakTable<ZNetView, Advertisement> advertised = new ConditionalWeakTable<ZNetView, Advertisement>();
        private static long readinessToken;
        private static Harmony patches;
        private static Action<ZRpc, ZDOID, long> nativeCommit;
        private static bool started, clientGreetingPending;
        internal static Func<ZDO, bool> TextEditBusy = null;
        internal static bool TextEditingAvailable = false;
        internal static bool SupportsTextEditing(ZNetPeer peer)
        { Connection c; return Capable(peer) && connections.TryGetValue(peer.m_rpc, out c) && c.TextEditing; }
        internal static bool SupportsLooseItems(ZNetPeer peer)
        { Connection c; return Capable(peer) && connections.TryGetValue(peer.m_rpc, out c) && c.LooseItems; }
        internal static bool SupportsPhysics(ZNetPeer peer)
        { Connection c; return Capable(peer) && connections.TryGetValue(peer.m_rpc, out c) && c.PhysicsGuard; }
        internal static bool SupportsTamed(ZNetPeer peer)
        { Connection c; return Capable(peer) && connections.TryGetValue(peer.m_rpc, out c) && c.TamedGuard; }
        private static readonly Func<Container, bool> inventoryLoading = BindInventoryLoading();
        private static Func<Container, bool> BindInventoryLoading()
        { try { return FieldReader.Create<Container, bool>(typeof(Container).GetField("m_loading", BindingFlags.Instance | BindingFlags.NonPublic)); } catch (Exception) { return null; } }
        private static bool InitialPeer(ZNetPeer peer)
        { Connection c; return Capable(peer) && connections.TryGetValue(peer.m_rpc, out c) && c.InitialProof; }

        internal static void Start(Assembly fgn)
        {
            try { NetworkSavings.StateTransfer.Start(fgn); }
            catch (Exception error) { Plugin.Warn("Compact ownership unavailable; full native state retained. " + error.GetBaseException().Message); }
            MethodInfo method = fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true).GetMethod("OnHandOff", BindingFlags.Static | BindingFlags.NonPublic);
            nativeCommit = (Action<ZRpc, ZDOID, long>)Delegate.CreateDelegate(typeof(Action<ZRpc, ZDOID, long>), method);
            FgnOwnerHandoff.CooperativeClient = Capable;
            AuthorityController.LooseItemClient = SupportsLooseItems;
            AuthorityController.PhysicsClient = SupportsPhysics;
            AuthorityController.TamedClient = SupportsTamed;
            AuthorityController.BeginNetworkBatch = BeginBatch; AuthorityController.EndNetworkBatch = EndBatch;
            AuthorityController.FlushNetworkBatch = FlushBatches;
            FgnOwnerHandoff.IdentifiedSend = SendCommit;
            FgnOwnerHandoff.IdentifiedStatus = SendStatus;
            RequestRouter.RecipientReady = Ready;
            AuthorityController.ReadinessRemoved = Forget;
            AuthorityController.ReadinessReset = ClearReadiness;
            AuthorityController.InitialClient = InitialPeer;
            patches = new Harmony(Plugin.Id + ".gridhandoff");
            TamedMobHandoff.Install(patches, fgn);
            foreach (Type hook in new[] { typeof(ConnectPatch), typeof(PeerReadyPatch), typeof(ReceivePatch), typeof(UnloadPatch), typeof(ShutdownPatch), typeof(DisconnectPatch), typeof(IncineratorOpenPatch), typeof(IncineratorStackPatch), typeof(IncineratorTakeAllPatch) })
                patches.CreateClassProcessor(hook).Patch();
            patches.CreateClassProcessor(typeof(PhysicsObjects.GainPatch)).Patch();
            IncineratorProtection.Active = IncineratorProtection.Supported;
            IncineratorProtection.Client = IncineratorCapable;
            started = true;
            if (ZNet.instance) foreach (ZNetPeer peer in ZNet.instance.GetPeers()) Connect(peer);
        }
        internal static void Stop()
        {
            NetworkSavings.StateTransfer.Stop();
            if (patches != null) patches.UnpatchSelf(); patches = null;
            IncineratorProtection.Active = false; IncineratorProtection.Client = null;
            FgnOwnerHandoff.CooperativeClient = null; FgnOwnerHandoff.IdentifiedSend = null; FgnOwnerHandoff.IdentifiedStatus = null;
            AuthorityController.LooseItemClient = null;
            AuthorityController.TamedClient = null;
            AuthorityController.PhysicsClient = null; PhysicsObjects.Reset();
            AuthorityController.BeginNetworkBatch = AuthorityController.EndNetworkBatch = null;
            AuthorityController.FlushNetworkBatch = null;
            RequestRouter.RecipientReady = null; AuthorityController.ReadinessRemoved = null; AuthorityController.ReadinessReset = null; AuthorityController.InitialClient = null; nativeCommit = null; started = false; Reset();
        }
        private static void Reset()
        { NetworkSavings.StateTransfer.Reset(); connections.Clear(); outgoing.Clear(); batchDepth = 0; ready.Clear(); retiring.Clear(); localPending.Clear(); localWork.Clear(); kinds = new ConditionalWeakTable<ZNetView, KindCache>(); refills = new ConditionalWeakTable<ZDO, Lease>(); advertised = new ConditionalWeakTable<ZNetView, Advertisement>(); clientGreetingPending = false; }
        internal static bool Capable(ZNetPeer peer)
        {
            Connection c;
            return peer != null && peer.IsReady() && peer.m_rpc != null && connections.TryGetValue(peer.m_rpc, out c) &&
                ReferenceEquals(c.Peer, peer) && c.Capable && Current(c, peer.m_rpc);
        }
        private static bool IncineratorCapable(ZNetPeer peer)
        { Connection c; return Capable(peer) && connections.TryGetValue(peer.m_rpc, out c) && c.IncineratorGuard; }
        private static bool Current(Connection c, ZRpc rpc)
        { return ZNet.instance && ReferenceEquals(ZNet.instance.GetPeer(c.Peer.m_uid), c.Peer) && ReferenceEquals(c.Peer.m_rpc, rpc); }
        private static void Connect(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null || connections.ContainsKey(peer.m_rpc)) return;
            NetworkSavings.StateTransfer.Connect(peer);
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
                try { Send(peer, Message.Hello, delegate(ZPackage p) { p.Write((IncineratorProtection.Active ? 3 : 1) | (inventoryLoading != null ? 4 : 0) | (TextEditingAvailable ? 8 : 0) | 16 | 32 | 64 | 256 | (NetworkSavings.StateTransfer.Active ? 128 : 0)); }); } catch (Exception) { }
        }
        internal static void Offer(ZNetPeer peer)
        {
            if (!started || !ZNet.instance || !ZNet.instance.IsDedicated() || peer == null || !peer.IsReady() || peer.m_rpc == null) return;
            Connection c;
            if (connections.TryGetValue(peer.m_rpc, out c) && Current(c, peer.m_rpc))
                try { Send(peer, Message.Welcome, delegate(ZPackage p) { Welcome(p, c.Capable); }); } catch (Exception) { }
        }
        private static void Welcome(ZPackage packet, bool acknowledged)
        { packet.Write(acknowledged); packet.Write(Plugin.Controller != null && Plugin.Controller.StaticGridEnabled); packet.Write(TextEditingAvailable); packet.Write(true); packet.Write(Plugin.Controller != null && Plugin.Controller.PhysicsEnabled); packet.Write(true); packet.Write(NetworkSavings.StateTransfer.Active); }
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
            Connection c;
            if (connections.TryGetValue(peer.m_rpc, out c))
            {
                if (batchDepth > 0 && c.Capable && c.Batching && Batchable(message) && packet.Size() <= ControlBytes)
                {
                    if (c.Outgoing == null) c.Outgoing = new List<ZPackage>(BatchRecords);
                    if (c.Outgoing.Count != 0 && (c.Outgoing.Count == BatchRecords || c.Bytes + 3 * sizeof(int) + packet.Size() > BatchBytes)) Flush(c);
                    if (!c.Queued) { c.Queued = true; outgoing.Add(c); }
                    c.Outgoing.Add(packet); c.Bytes += sizeof(int) + packet.Size(); return;
                }
                // An immediate relay/greeting must not overtake queued tokens.
                Flush(c);
            }
            peer.m_rpc.Invoke(RpcName, packet);
        }
        private static bool Batchable(Message message)
        { return message == Message.Commit || message == Message.PhysicsCommit || message == Message.Status || message == Message.Result || message == Message.Ready || message == Message.ReadyResult || message == Message.ReadyLost || message == Message.Loaded; }
        internal static void BeginBatch() { batchDepth++; }
        internal static void EndBatch()
        {
            if (batchDepth <= 0 || --batchDepth != 0) return;
            FlushBatches();
        }
        private static void FlushBatches()
        {
            if (flushingBatches || outgoing.Count == 0) return;
            flushingBatches = true;
            try
            {
                // Only peers actually queued by this operation. No idle scan,
                // frame timer or cross-pass history; red requests stay first.
                for (int i = 0; i < outgoing.Count; i++) { Connection c = outgoing[i]; c.Queued = false; Flush(c); }
            }
            finally { outgoing.Clear(); flushingBatches = false; }
        }
        private static void Flush(Connection c)
        {
            if (c.Outgoing == null || c.Outgoing.Count == 0) return;
            try
            {
                if (!Current(c, c.Peer.m_rpc) || !c.Peer.IsReady()) { c.Outgoing.Clear(); c.Bytes = 0; return; }
                if (!c.Batching) { FlushIndividual(c); return; }
                ZPackage packet;
                try
                {
                    if (c.Outgoing.Count == 1) packet = c.Outgoing[0];
                    else
                    {
                        packet = new ZPackage(); packet.Write((int)Message.Bundle); packet.Write(c.Outgoing.Count);
                        foreach (ZPackage part in c.Outgoing) packet.Write(part);
                    }
                }
                catch (Exception error)
                {
                    // No transport call has occurred: the original records
                    // can safely use the existing individual wire format.
                    BatchWarning("pack", error); FlushIndividual(c); return;
                }
                c.Outgoing.Clear(); c.Bytes = 0;
                InvokeControl(c, packet);
            }
            catch (Exception error)
            {
                // Connection validation or fallback preparation must not
                // strand subsequent peers with Queued still set.
                c.Outgoing.Clear(); c.Bytes = 0; BatchWarning("flush", error);
            }
        }
        private static void FlushIndividual(Connection c)
        {
            // Only a capability downgrade or envelope construction failure
            // takes this allocating fallback. Detach before invoking callbacks.
            ZPackage[] parts = c.Outgoing.ToArray(); c.Outgoing.Clear(); c.Bytes = 0;
            foreach (ZPackage part in parts)
            {
                if (part == null) continue;
                if (!Current(c, c.Peer.m_rpc) || !c.Peer.IsReady()) break;
                InvokeControl(c, part);
            }
        }
        private static void InvokeControl(Connection c, ZPackage packet)
        {
            try { c.Peer.m_rpc.Invoke(RpcName, packet); }
            // A throwing transport may already have accepted the packet.
            // Never retry it here; existing token/status recovery stays in charge.
            catch (Exception error) { BatchWarning("send", error); }
        }
        private static void BatchWarning(string stage, Exception error)
        { Plugin.Warn("Grid control batch " + stage + " failed: " + error.GetBaseException().Message); }
        private static void ReceiveBundle(Connection c, ZRpc rpc, ZPackage packet)
        {
            if (!c.Capable || !c.Batching || packet.Size() > BatchBytes) return;
            int count = packet.ReadInt(); if (count < 2 || count > BatchRecords) return;
            var parts = new List<ZPackage>(count);
            // Validate the bounded envelope completely before executing it.
            for (int i = 0; i < count; i++)
            {
                int start = packet.GetPos(), length = packet.ReadInt();
                if (length < sizeof(int) || length > ControlBytes || length > packet.Size() - packet.GetPos()) return;
                packet.SetPos(start); ZPackage part = packet.ReadPackage();
                if (!Batchable((Message)part.ReadInt())) return;
                part.SetPos(0); parts.Add(part);
            }
            if (packet.GetPos() != packet.Size()) return;
            BeginBatch();
            try { foreach (ZPackage part in parts) Receive(c, rpc, part); }
            finally { EndBatch(); }
        }
        private static void SendCommit(ZNetPeer peer, ZDOID id, long target, uint revision, long token)
        {
            ZDO data = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(id);
            Kinds initial = data != null && Plugin.Controller != null ? Plugin.Controller.CommitKind(data) : Kinds.None;
            bool sleepRequired = true;
            bool physics = data != null && Plugin.Controller != null && Plugin.Controller.PhysicsTransfer(data, out sleepRequired);
            bool tamed = data != null && data.GetBool(ZDOVars.s_tamed);
            Send(peer, tamed ? Message.TamedCommit : physics ? Message.PhysicsCommit : Message.Commit, delegate(ZPackage p)
            { p.Write(token); p.Write(id); p.Write(target); p.Write(revision); if (physics) p.Write(sleepRequired); else if (initial != Kinds.None) p.Write((int)initial); });
        }
        private static void SendStatus(ZNetPeer peer, ZDOID id, long token)
        { Send(peer, Message.Status, delegate(ZPackage p) { p.Write(token); p.Write(id); }); }
        private static void Receive(Connection c, ZRpc rpc, ZPackage packet)
        {
            if (!Current(c, rpc) || !c.Peer.IsReady() || packet == null) return;
            try
            {
                Message message = (Message)packet.ReadInt(); bool server = ZNet.instance.IsDedicated();
                if (message == Message.Bundle) { ReceiveBundle(c, rpc, packet); return; }
                // Relays preserve native payload sizes (e.g. large map data).
                // Only this protocol's small control messages have a new cap.
                if (message != Message.Relay && packet.Size() > 256) return;
                if (server)
                {
                    if (message == Message.Hello)
                    { int flags = packet.Size() >= 2 * sizeof(int) ? packet.ReadInt() : 0;
                      bool proof = (flags & 1) != 0, incinerator = (flags & 2) != 0, initial = (flags & 4) != 0;
                      bool changed = !c.Capable || (!c.LoadedProof && proof) || c.IncineratorGuard != incinerator || c.InitialProof != initial;
                      c.Capable = true; c.LoadedProof = proof; c.IncineratorGuard = incinerator; c.InitialProof = initial; c.TextEditing = (flags & 8) != 0;
                      c.LooseItems = (flags & 16) != 0;
                      changed |= c.PhysicsGuard != ((flags & 32) != 0); c.PhysicsGuard = (flags & 32) != 0;
                      c.Batching = (flags & 64) != 0;
                      changed |= c.TamedGuard != ((flags & 256) != 0); c.TamedGuard = (flags & 256) != 0;
                      NetworkSavings.StateTransfer.Enable(rpc, (flags & 128) != 0);
                      if (changed && Plugin.Controller != null) Plugin.Controller.CapabilityChanged(c.Peer.m_uid);
                      Send(c.Peer, Message.Welcome, delegate(ZPackage p) { Welcome(p, true); }); return; }
                    if (!c.Capable || Plugin.Controller == null || ZDOMan.instance == null) return;
                    if (message == Message.Result)
                    {
                        long token = packet.ReadLong(); ZDOID id = packet.ReadZDOID(); bool processed = packet.ReadBool(), present = packet.ReadBool();
                        long owner = packet.ReadLong(); uint revision = packet.ReadUInt(), state = packet.ReadUInt();
                        bool pickupBlocked = packet.GetPos() < packet.Size() && packet.ReadBool();
                        bool physicsVeto = packet.GetPos() < packet.Size() && packet.ReadBool();
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
                        if (processed && present && pickupBlocked && data != null && owner == c.Peer.m_uid)
                            Plugin.Controller.PickupBlocked(data, owner, revision);
                        if (processed && present && physicsVeto && data != null && owner == c.Peer.m_uid)
                            Plugin.Controller.PhysicsBlocked(data, owner, revision);
                        Plugin.Controller.Router.Flush(id); return;
                    }
                    if (message == Message.ReadyResult)
                    {
                        ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(); bool available = packet.ReadBool(); Readiness r;
                        if (ready.TryGetValue(id, out r) && ReferenceEquals(r.Rpc, rpc)) r.Probe.Response(token, available, Time.unscaledTime);
                        Plugin.Controller.Router.Flush(id); return;
                    }
                    if (message == Message.ReadyLost)
                    {
                        ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(); Readiness r;
                        if (ready.TryGetValue(id, out r) && ReferenceEquals(r.Rpc, rpc)) r.Probe.Lost(token);
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
                    c.StaticGrid = packet.GetPos() < packet.Size() && packet.ReadBool();
                    c.TextEditing = packet.GetPos() < packet.Size() && packet.ReadBool();
                    c.LooseItems = packet.GetPos() < packet.Size() && packet.ReadBool();
                    c.PhysicsObjects = packet.GetPos() < packet.Size() && packet.ReadBool(); PhysicsObjects.Enabled = c.PhysicsObjects;
                    c.Batching = packet.GetPos() < packet.Size() && packet.ReadBool();
                    NetworkSavings.StateTransfer.Enable(rpc, packet.GetPos() < packet.Size() && packet.ReadBool());
                    if (c.Greeting.Welcome(acknowledgement)) { c.Capable = true; clientGreetingPending = false; }
                    else { clientGreetingPending = true; ClientHello(); }
                    return;
                }
                if (ZDOMan.instance == null || (message != Message.Commit && message != Message.PhysicsCommit && message != Message.TamedCommit && message != Message.Status && message != Message.Ready && message != Message.Loaded)) return;
                c.Capable = true; // Authenticated server endpoint; no exact-version gate.
                if (message == Message.Ready || message == Message.Loaded)
                {
                    ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(), owner = packet.ReadLong(); uint revision = packet.ReadUInt(), minimum = packet.ReadUInt();
                    bool before = message == Message.Loaded; Vector3 position = before ? packet.ReadVector3() : default(Vector3);
                    Kinds initial = before && packet.GetPos() < packet.Size() && packet.ReadBool() ? (Kinds)packet.ReadInt() : Kinds.None;
                    ZDO data = ZDOMan.instance.GetZDO(id); ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
                    bool replica = token > 0 && data != null && data.IsValid() && data.GetOwner() == owner &&
                        (before ? (initial != Kinds.None ? owner != 0 && owner != ZDOMan.GetSessionID() : owner == c.Peer.m_uid) : owner == ZDOMan.GetSessionID()) && (!before || data.GetPosition() == position) &&
                        data.OwnerRevision == revision && data.DataRevision >= minimum;
                    // Control RPCs can beat their queued native object packet.
                    // Recheck only this incomplete snapshot after this frame's
                    // network/scene updates, instead of a fixed 0.5-second wait.
                    if (before && !replica && localPending.Count < 4096)
                    {
                        localPending.Add(new LocalProbe { Link = c, Rpc = rpc, Id = id, Token = token, Owner = owner,
                            Revision = revision, Minimum = minimum, Position = position, InitialKind = initial }); return;
                    }
                    // Ownership is already assigned. Only an actual routed use
                    // needs its local RPC instance; unrelated neighbours do not.
                    bool available = replica && (before || (view && view.IsValid() && ReferenceEquals(view.GetZDO(), data) && view.gameObject.activeInHierarchy));
                    // Before ownership the data belongs to the server. Its
                    // authenticated reply requests any needed resync there.
                    if (!available && !before && data != null) ZDOMan.instance.ForceSendZDO(id);
                    if (available && view)
                    { advertised.Remove(view); advertised.Add(view, new Advertisement { Peer = c.Peer, Id = id, Token = token }); }
                    Send(c.Peer, Message.ReadyResult, delegate(ZPackage p) { p.Write(id); p.Write(token); p.Write(available); if (before) p.Write(replica); }); return;
                }
                long commitToken = packet.ReadLong(); ZDOID objectId = packet.ReadZDOID();
                if (commitToken <= 0) return;
                bool physicsBlocked = false;
                if (message == Message.Commit || message == Message.PhysicsCommit || message == Message.TamedCommit)
                {
                    long target = packet.ReadLong(); uint expected = packet.ReadUInt();
                    bool physics = message == Message.PhysicsCommit;
                    bool requireSleep = physics && packet.ReadBool();
                    Kinds initial = !physics && packet.GetPos() < packet.Size() ? (Kinds)packet.ReadInt() : Kinds.None;
                    if (c.Fence.Begin(commitToken))
                    {
                        // Fence before calling native code, including an exception
                        // after SetOwner. A status query never re-executes it.
                        ZDO data = ZDOMan.instance.GetZDO(objectId);
                        ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
                        if (physics && (!c.PhysicsObjects || (!requireSleep && target != c.Peer.m_uid) || !view || !PhysicsObjects.Body(view.gameObject) ||
                            (requireSleep && !PhysicsObjects.Sleeping(view)))) physicsBlocked = true;
                        if (physics && !physicsBlocked && ObjectCatalog.Classify(view.gameObject) != Kinds.PhysicsObjects) physicsBlocked = true;
                        Lease lease;
                        if (!physicsBlocked && data != null && (TextEditBusy == null || !TextEditBusy(data)) && data.IsOwner() && data.OwnerRevision == expected && expected < UInt16.MaxValue &&
                            target != 0 && target != ZDOMan.GetSessionID() && view && view.IsValid() && view.gameObject.activeInHierarchy &&
                            data.GetLong(ZDOVars.s_user, 0) == 0 && !StandAction(view) && !LooseItemGuard.Busy(view) && (!refills.TryGetValue(data, out lease) || Time.unscaledTime >= lease.Until) &&
                            (initial == Kinds.None || InitialCommit(view, initial)))
                        {
                            if (physics) PhysicsObjects.FlushMotion(view);
                            try
                            {
                                if (message == Message.TamedCommit) TamedMobHandoff.Commit(nativeCommit, rpc, objectId, target);
                                else if (!data.GetBool(ZDOVars.s_tamed)) nativeCommit(rpc, objectId, target);
                            }
                            catch (Exception error) { Plugin.Warn("Native grid handoff failed: " + error.GetBaseException().Message); }
                        }
                    }
                }
                else c.Fence.Settle(commitToken);
                Report(c, objectId, commitToken, physicsBlocked);
            }
            catch (Exception error) { Plugin.Warn("Grid handoff packet rejected: " + error.GetBaseException().Message); }
        }
        private static void Report(Connection c, ZDOID id, long token, bool physicsBlocked = false)
        {
            ZDO data = ZDOMan.instance.GetZDO(id); bool present = data != null && data.IsValid();
            long owner = present ? data.GetOwner() : 0; uint revision = present ? (uint)data.OwnerRevision : 0, state = present ? data.DataRevision : 0;
            if (present) ZDOMan.instance.ForceSendZDO(id);
            ZNetView view = present && data.IsOwner() && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            bool pickupBlocked = view && LooseItemGuard.Busy(view);
            Send(c.Peer, Message.Result, delegate(ZPackage p) { p.Write(token); p.Write(id); p.Write(c.Fence.Decided(token)); p.Write(present); p.Write(owner); p.Write(revision); p.Write(state); p.Write(pickupBlocked); p.Write(physicsBlocked); });
        }
        internal static void FlushDeferred()
        {
            if (localPending.Count == 0) return;
            List<LocalProbe> wave = localWork; localWork = localPending; localPending = wave;
            BeginBatch();
            try
            {
                if (!started || !ZNet.instance || ZNet.instance.IsDedicated() || ZDOMan.instance == null || !ZNetScene.instance) return;
                for (int i = 0; i < localWork.Count; i++)
                {
                    LocalProbe q = localWork[i]; Connection c = q.Link;
                    if (!Current(c, q.Rpc) || !c.Peer.IsReady() || !c.Peer.m_server || !ReferenceEquals(ZNet.instance.GetServerPeer(), c.Peer)) continue;
                    ZDO data = ZDOMan.instance.GetZDO(q.Id); ZNetView view = data != null ? ZNetScene.instance.FindInstance(data) : null;
                    bool replica = q.Token > 0 && data != null && data.IsValid() && data.GetOwner() == q.Owner &&
                        (q.InitialKind != Kinds.None ? q.Owner != 0 && q.Owner != ZDOMan.GetSessionID() : q.Owner == c.Peer.m_uid) &&
                        data.OwnerRevision == q.Revision && data.DataRevision >= q.Minimum && data.GetPosition() == q.Position;
                    bool available = replica;
                    if (available && view)
                    { advertised.Remove(view); advertised.Add(view, new Advertisement { Peer = c.Peer, Id = q.Id, Token = q.Token }); }
                    try { Send(c.Peer, Message.ReadyResult, delegate(ZPackage p) { p.Write(q.Id); p.Write(q.Token); p.Write(available); p.Write(replica); }); }
                    catch (Exception error) { Plugin.Warn("Deferred destination proof failed: " + error.GetBaseException().Message); }
                }
            }
            finally { localWork.Clear(); EndBatch(); }
        }
        private static bool StandAction(ZNetView view)
        {
            ItemStand item = view.GetComponent<ItemStand>();
            if (item && (item.IsInvoking("UpdateAttach") || item.IsInvoking("UpdateOrientation"))) return true;
            ArmorStand armor = view.GetComponent<ArmorStand>();
            return armor && armor.IsInvoking("UpdateAttach");
        }
        private static KindCache InitialView(ZNetView view, Kinds expected)
        {
            KindCache cache;
            if (!kinds.TryGetValue(view, out cache)) { cache = new KindCache { Kind = ObjectCatalog.Classify(view.gameObject) }; kinds.Add(view, cache); }
            if (cache.Kind != expected) return null;
            if (!cache.Inspected)
            {
                cache.Inspected = true; cache.Initial = ObjectCatalog.InitialRoot(view.gameObject, expected, out cache.Inventory);
                if (cache.Initial && expected == Kinds.Incinerators) cache.Incinerator = view.GetComponent<Incinerator>();
            }
            return cache.Initial && (!cache.Inventory || cache.Inventory.m_privacy == Container.PrivacySetting.Public) ? cache : null;
        }
        private static bool InitialCommit(ZNetView view, Kinds expected)
        {
            KindCache cache = InitialView(view, expected);
            if (cache == null) return false;
            // Native OnContainerChanged saves inventory changes synchronously.
            // Only its actual idle owner may commit; never migrate a local load,
            // an open inventory or an incinerator's private burning coroutine.
            return (!cache.Inventory || (inventoryLoading != null && !inventoryLoading(cache.Inventory) && !cache.Inventory.IsInUse() &&
                cache.Inventory.m_privacy == Container.PrivacySetting.Public)) && (!cache.Incinerator || !IncineratorProtection.Busy(cache.Incinerator));
        }
        private static void SetFloor(ZDO data, long owner, uint revision, uint state)
        {
            // Update the loaded node only. Cached reply invalidation is checked
            // lazily by Ready, including a reply arriving before the next call.
            if (Plugin.Controller != null) Plugin.Controller.RecordRecipient(data, owner, revision, state);
        }
        internal static bool Ready(ZDOMan world, long owner, ZDO data)
        {
            // Guarded narrow path: unchanged native ownership needs no
            // additional handshake. Real TML transfers and receiver relays arm
            // the existing node, retaining only the required state receipt.
            uint minimum;
            if (Plugin.Controller == null || !Plugin.Controller.TryRecipientState(data, owner, out minimum)) return true;
            ZNetPeer peer = ZNet.instance ? ZNet.instance.GetPeer(owner) : null;
            // Peers without this capability keep native delivery; automatic
            // grid transfers to/from them are disabled separately.
            if (!Capable(peer)) return true;
            Readiness r;
            if (!ready.TryGetValue(data.m_uid, out r) || !ReferenceEquals(r.Data, data) || r.Owner != owner ||
                r.Revision != data.OwnerRevision || !ReferenceEquals(r.Rpc, peer.m_rpc))
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
            ZDO data = view.GetZDO(); Forget(data); kinds.Remove(view); if (data != null) refills.Remove(data);
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
                Connection selected;
                if (kind.Kind == Kinds.StaticBuildings && (!connections.TryGetValue(server.m_rpc, out selected) || !selected.StaticGrid)) return true;
                if (kind.Kind == Kinds.PhysicsObjects && (!connections.TryGetValue(server.m_rpc, out selected) || !selected.PhysicsObjects)) return true;
                if (kind.Kind == Kinds.None || kind.Kind == Kinds.TamedAnimals || !ObjectCatalog.OwnerRequest(kind.Kind, request.m_methodHash)) return true;
                if (data.IsOwner() && view.gameObject.activeInHierarchy)
                {
                    if (ObjectCatalog.RefillRequest(kind.Kind, request.m_methodHash))
                    { Lease lease = refills.GetOrCreateValue(data); lease.Until = Time.unscaledTime + 1f; }
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
        [HarmonyPatch(typeof(Container), "RPC_RequestOpen")]
        private static class IncineratorOpenPatch
        { [HarmonyPrefix] private static bool Prefix(Container __instance, long __0, bool __runOriginal) { return __runOriginal && IncineratorProtection.Allow(__instance, __0, "RPC_OpenResponse"); } }
        [HarmonyPatch(typeof(Container), "RPC_RequestStack")]
        private static class IncineratorStackPatch
        { [HarmonyPrefix] private static bool Prefix(Container __instance, long __0, bool __runOriginal) { return __runOriginal && IncineratorProtection.Allow(__instance, __0, "RPC_StackResponse"); } }
        [HarmonyPatch(typeof(Container), "RPC_RequestTakeAll")]
        private static class IncineratorTakeAllPatch
        { [HarmonyPrefix] private static bool Prefix(Container __instance, long __0, bool __runOriginal) { return __runOriginal && IncineratorProtection.Allow(__instance, __0, "RPC_TakeAllResponse"); } }
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
