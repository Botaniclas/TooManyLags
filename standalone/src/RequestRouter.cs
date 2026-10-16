using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsStandalone
{
    internal static class RequestRouter
    {
        private sealed class Waiting { internal ZRoutedRpc.RoutedRPCData Packet; internal float At; }
        private sealed class WaitingQueue : Queue<Waiting> { internal bool Flushing; }
        private static readonly Dictionary<ZDOID, WaitingQueue> waiting = new Dictionary<ZDOID, WaitingQueue>();
        private static readonly List<ZDOID> scratch = new List<ZDOID>();
        private static readonly Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData> handle = AccessTools.MethodDelegate<Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>>(AccessTools.Method(typeof(ZRoutedRpc), "HandleRoutedRPC"));
        private static readonly Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData> route = AccessTools.MethodDelegate<Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>>(AccessTools.Method(typeof(ZRoutedRpc), "RouteRPC"));
        private static bool dispatching;
        private static int held;
        private static float nextFlush;
        private static readonly int open = "RPC_RequestOpen".GetStableHashCode(), stack = "RPC_RequestStack".GetStableHashCode(), take = "RPC_RequestTakeAll".GetStableHashCode();
        internal static bool Handle(ZRoutedRpc rpc, ZRoutedRpc.RoutedRPCData packet, bool local)
        {
            if (dispatching || ZNet.IsSinglePlayer || packet == null || packet.m_targetPeerID == 0 || packet.m_targetZDO.IsNone() || !ObjectCatalog.PotentialOwnerRequest(packet.m_methodHash) || !ZNet.instance || ZDOMan.instance == null) return true;
            ZDO data = ZDOMan.instance.GetZDO(packet.m_targetZDO);
            if (local && Plugin.Local != null && data != null && data.IsOwner()) Plugin.Local.Refilling(data, packet.m_methodHash);
            if (ZNet.instance.IsServer() && Plugin.Controller != null && Plugin.Controller.Active && Plugin.Controller.IsManaged(data, packet.m_methodHash))
            {
                if (Plugin.Controller.Handoffs.Committing(packet.m_targetZDO) || waiting.ContainsKey(packet.m_targetZDO) || !Plugin.Controller.Recipients.Ready(data))
                { Hold(packet); Flush(packet.m_targetZDO, Time.unscaledTime); return false; }
                long owner = data.GetOwner();
                if (owner != packet.m_targetPeerID && Plugin.Controller.Available(owner, data.GetPosition())) { Deliver(rpc, Copy(packet), owner); return false; }
                return true;
            }
            if (local && Plugin.Local != null)
            {
                long owner; ushort revision;
                if (Plugin.Local.ForwardDestination(packet.m_targetZDO, data, packet.m_methodHash, out owner, out revision))
                {
                    // Preserve the original sender/message ID; relay via the
                    // coordinator so a stale owner header cannot cause a loop.
                    ZPackage original = new ZPackage(); packet.Serialize(original);
                    Plugin.Network.ToServer(Message.Forward, delegate(ZPackage p) { p.Write(owner); p.Write(revision); p.Write(original); }); return false;
                }
                if (Plugin.Local.RequestScope(data, packet.m_methodHash) && !Plugin.Local.CanDispatch(packet.m_targetZDO, data, packet.m_methodHash))
                {
                    ZPackage original = new ZPackage(); packet.Serialize(original);
                    Plugin.Network.ToServer(Message.Relay, delegate(ZPackage p) { p.Write(original); }); return false;
                }
            }
            return true;
        }
        internal static void Forwarded(long sender, ZPackage packet)
        {
            if (Plugin.Controller == null || !Plugin.Controller.Active) return;
            long target = packet.ReadLong(); ushort revision = packet.ReadUShort(); ZPackage original = new ZPackage(); packet.ReadPackage(ref original);
            if (original.Size() > 16384) return;
            ZRoutedRpc.RoutedRPCData data = new ZRoutedRpc.RoutedRPCData(); data.Deserialize(original);
            if (data.m_targetPeerID != sender || data.m_targetPeerID == 0 || data.m_targetZDO.IsNone() ||
                !Plugin.Controller.Handoffs.AcceptsForward(data.m_targetZDO, sender, target, revision, data.m_methodHash)) return;
            ZDO zdo = ZDOMan.instance.GetZDO(data.m_targetZDO);
            if (zdo == null) return;
            Hold(data); Flush(data.m_targetZDO, Time.unscaledTime);
        }
        internal static void Relayed(long sender, ZPackage packet)
        {
            if (Plugin.Controller == null || !Plugin.Controller.Active) return;
            ZPackage original = new ZPackage(); packet.ReadPackage(ref original); if (original.Size() > 16384) return;
            ZRoutedRpc.RoutedRPCData call = new ZRoutedRpc.RoutedRPCData(); call.Deserialize(original);
            if (call.m_targetPeerID != sender || call.m_targetPeerID == 0 || call.m_targetZDO.IsNone() || !ObjectCatalog.PotentialOwnerRequest(call.m_methodHash)) return;
            ZDO data = ZDOMan.instance.GetZDO(call.m_targetZDO);
            if (!Plugin.Controller.IsManaged(data, call.m_methodHash)) return;
            Plugin.Controller.RecordRecipient(data, data.GetOwner(), data.OwnerRevision, data.DataRevision);
            Plugin.Controller.Recipients.Forget(call.m_targetZDO);
            Hold(call); Flush(call.m_targetZDO, Time.unscaledTime);
        }
        private static bool Hold(ZRoutedRpc.RoutedRPCData packet)
        {
            WaitingQueue queue;
            if (!waiting.TryGetValue(packet.m_targetZDO, out queue)) { queue = new WaitingQueue(); waiting.Add(packet.m_targetZDO, queue); }
            queue.Enqueue(new Waiting { Packet = Copy(packet), At = Time.unscaledTime }); held++; return true;
        }
        internal static void Flush(float now)
        {
            if (held == 0 || !ZNet.instance || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null || ZDOMan.instance == null) return;
            if (now < nextFlush) return; nextFlush = now + .5f;
            scratch.Clear(); foreach (ZDOID id in waiting.Keys) scratch.Add(id);
            foreach (ZDOID id in scratch) Flush(id, now);
            scratch.Clear();
        }
        internal static void Flush(ZDOID id, float now)
        {
            if (held == 0 || !ZNet.instance || !ZNet.instance.IsServer() || ZRoutedRpc.instance == null || ZDOMan.instance == null) return;
            WaitingQueue queue; if (!waiting.TryGetValue(id, out queue) || queue.Flushing) return;
            queue.Flushing = true;
            try
            {
            ZDO data = ZDOMan.instance.GetZDO(id);
            bool commit = Plugin.Controller != null && Plugin.Controller.Handoffs.Committing(id);
            bool ready = data == null || Plugin.Controller == null || !Plugin.Controller.Active ||
                !Plugin.Controller.Available(data.GetOwner(), data.GetPosition()) || Plugin.Controller.Recipients.Ready(data);
            long checkedOwner = data == null ? 0 : data.GetOwner(); ushort checkedRevision = data == null ? (ushort)0 : data.OwnerRevision;
                int count = queue.Count;
                for (int i = 0; i < count; i++)
                {
                    Waiting call = queue.Dequeue(); int method = call.Packet.m_methodHash;
                    if (Inventory(method) && now - call.At >= 5f) { held--; Deny(call.Packet); continue; }
                    if (data == null || !data.IsValid()) { held--; if (Inventory(method)) Deny(call.Packet); continue; }
                    if (data.GetOwner() != checkedOwner || data.OwnerRevision != checkedRevision)
                    {
                        checkedOwner = data.GetOwner(); checkedRevision = data.OwnerRevision;
                        commit = Plugin.Controller != null && Plugin.Controller.Handoffs.Committing(id);
                        ready = Plugin.Controller == null || !Plugin.Controller.Active || !Plugin.Controller.Available(checkedOwner, data.GetPosition()) || Plugin.Controller.Recipients.Ready(data);
                    }
                    if (commit || !ready || (data.GetOwner() == 0 && now - call.At < 12f)) { queue.Enqueue(call); continue; }
                    // After the bounded transition, use native routing even if
                    // the owner left its area. Disconnected/unloaded targets
                    // behave as in vanilla; their calls must not remain cached.
                    if (data.GetOwner() == 0) { held--; if (Inventory(method)) Deny(call.Packet); continue; }
                    held--; Deliver(ZRoutedRpc.instance, call.Packet, data.GetOwner());
                }
                if (queue.Count == 0) waiting.Remove(id);
            }
            finally { queue.Flushing = false; }
        }
        private static bool Inventory(int method) { return method == open || method == stack || method == take; }
        private static void Deny(ZRoutedRpc.RoutedRPCData packet)
        {
            string response = packet.m_methodHash == open ? "RPC_OpenResponse" : packet.m_methodHash == stack ? "RPC_StackResponse" : "RPC_TakeAllResponse";
            if (ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(packet.m_senderPeerID, packet.m_targetZDO, response, false);
        }
        private static void Deliver(ZRoutedRpc rpc, ZRoutedRpc.RoutedRPCData packet, long target)
        {
            packet.m_targetPeerID = target; packet.m_parameters.SetPos(0); dispatching = true;
            try
            {
                if (target == ZDOMan.GetSessionID())
                { if (Plugin.Local != null) Plugin.Local.Refilling(ZDOMan.instance.GetZDO(packet.m_targetZDO), packet.m_methodHash); handle(rpc, packet); }
                else route(rpc, packet);
            }
            finally { dispatching = false; }
        }
        private static ZRoutedRpc.RoutedRPCData Copy(ZRoutedRpc.RoutedRPCData packet)
        { return new ZRoutedRpc.RoutedRPCData { m_msgID = packet.m_msgID, m_senderPeerID = packet.m_senderPeerID, m_targetPeerID = packet.m_targetPeerID, m_targetZDO = packet.m_targetZDO, m_methodHash = packet.m_methodHash, m_parameters = new ZPackage(packet.m_parameters.GetArray()) }; }
        internal static void Reset() { waiting.Clear(); scratch.Clear(); held = 0; nextFlush = 0; dispatching = false; }
        internal static bool Saturated { get { return held >= 2048; } }
    }
}
