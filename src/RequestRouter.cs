using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using HarmonyLib;

namespace TooManyLags
{
    // Redirect only verified owner-addressed requests, never replies/broadcasts.
    // The original packet remains untouched: native/FGN callers inspect its
    // original target again after HandleRoutedRPC returns.
    internal sealed class RequestRouter
    {
        internal static Func<ZDOMan, long, ZDO, bool> RecipientReady { get; set; }
        private sealed class DispatchFailure : Exception
        {
            internal readonly ExceptionDispatchInfo Original;
            internal DispatchFailure(Exception error) { Original = ExceptionDispatchInfo.Capture(error); }
        }
        private sealed class QueuedRequest
        {
            internal ZRoutedRpc.RoutedRPCData Packet;
            internal float At;
        }
        // Only cancellable native inventory requests expire. Damage, material
        // inserts and other gameplay calls keep their existing delivery policy.
        internal const float InventoryRequestLifetime = 5f;
        private static readonly int open = "RPC_RequestOpen".GetStableHashCode(),
            stack = "RPC_RequestStack".GetStableHashCode(), take = "RPC_RequestTakeAll".GetStableHashCode();
        private sealed class Waiting
        {
            internal AuthorityController.NativeNode Node;
            internal readonly Queue<QueuedRequest> Requests = new Queue<QueuedRequest>();
            internal long SentTo;
            internal ushort SentRevision;
            internal float NextExpiry = Single.PositiveInfinity;
        }
        private readonly AuthorityController controller;
        private readonly Dictionary<ZDOID, Waiting> waiting = new Dictionary<ZDOID, Waiting>();
        private readonly List<Waiting> scratch = new List<Waiting>();
        private readonly Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData> handle, route;
        private readonly Func<ZDOMan, long, ZDO, bool> knows;
        private bool dispatching, flushing;
        internal RequestRouter(AuthorityController controller)
        {
            this.controller = controller;
            handle = AccessTools.MethodDelegate<Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>>(AccessTools.Method(typeof(ZRoutedRpc), "HandleRoutedRPC"));
            route = AccessTools.MethodDelegate<Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>>(AccessTools.Method(typeof(ZRoutedRpc), "RouteRPC"));
            knows = BindKnowledge();
        }
        internal bool TryHandle(ZRoutedRpc rpc, ZRoutedRpc.RoutedRPCData data, bool local)
        {
            if (dispatching || controller.SendingInventory || !controller.IsActive || data == null || data.m_targetPeerID == 0 || data.m_targetZDO.IsNone()) return false;
            try
            {
                AuthorityController.NativeNode node = controller.RequestNode(data.m_targetZDO, data.m_methodHash);
                if (node == null) return false;
                node.Refilled(data.m_methodHash);
                long target = node.Data.GetOwner();
                bool available = controller.AvailableOwner(target, node.Data.GetPosition());
                // An unavailable former owner cannot answer an inventory request.
                // Keep its cancellable request pending while native reassignment
                // runs, then deliver safely or finish with an explicit denial.
                if (!available && ResponseName(data.m_methodHash) == null) return false;
                Waiting entry;
                if (waiting.TryGetValue(node.Id, out entry)) { Hold(entry, Copy(data)); return true; }
                bool pending = controller.TransferPending(node.Data);
                if (available && !pending && target == data.m_targetPeerID && (target == controller.Server ? local : Knows(target, node.Data))) return false;
                ZRoutedRpc.RoutedRPCData copy = Copy(data);
                if (available && !pending && Deliver(rpc, node, copy, target)) return true;
                entry = new Waiting { Node = node }; waiting.Add(node.Id, entry);
                Hold(entry, copy); if (available) Force(entry, target); return true;
            }
            // Dispatch already started: never run the original request again.
            // Native object errors belong to this call, not the ownership controller.
            catch (DispatchFailure error) { error.Original.Throw(); throw; }
            catch (Exception error) { controller.RouterFault(error); return false; }
        }
        internal void Flush()
        {
            if (flushing || waiting.Count == 0 || ZDOMan.instance == null || ZRoutedRpc.instance == null || !ZNet.instance) return;
            flushing = true;
            try
            {
                scratch.Clear(); foreach (Waiting entry in waiting.Values) scratch.Add(entry);
                foreach (Waiting entry in scratch) FlushEntry(entry);
            }
            catch (Exception error) { controller.RouterFault(error); }
            finally { scratch.Clear(); flushing = false; }
        }
        // A protocol result concerns one object. Keep the full pass for native
        // replication, deadlines and state changes without a protocol reply.
        internal void Flush(ZDOID id)
        {
            if (flushing || waiting.Count == 0 || ZDOMan.instance == null || ZRoutedRpc.instance == null || !ZNet.instance) return;
            Waiting entry; if (!waiting.TryGetValue(id, out entry)) return;
            flushing = true;
            try { FlushEntry(entry); }
            catch (Exception error) { controller.RouterFault(error); }
            finally { flushing = false; }
        }
        private bool Current(Waiting entry)
        { Waiting current; return waiting.TryGetValue(entry.Node.Id, out current) && ReferenceEquals(current, entry); }
        private void FlushEntry(Waiting entry)
        {
            // A native handler may unload an object or reset the whole scene.
            // A snapshot from before that callback must not process its successor.
            if (!Current(entry)) return;
            AuthorityController.NativeNode node = entry.Node;
            if (node.Removed || !node.Identity()) { Remove(node); return; }
            Expire(entry);
            if (!Current(entry)) return;
            if (entry.Requests.Count == 0) { Finish(entry); return; }
            if (controller.IsActive && controller.TransferPending(node.Data)) return;
            long target = node.Data.GetOwner();
            if (!controller.AvailableOwner(target, node.Data.GetPosition())) return;
            if (target != controller.Server && !Knows(target, node.Data)) { Force(entry, target); return; }
            // Dequeue before delivery: a legitimate Remove/Damage request can
            // destroy this object and remove its remaining work.
            while (entry.Requests.Count != 0 && !node.Removed)
            {
                var request = entry.Requests.Dequeue().Packet; node.Requests--;
                try { Deliver(ZRoutedRpc.instance, node, request, target); }
                catch (DispatchFailure error) { controller.RequestFault(error.Original.SourceException); }
                if (node.Removed || node.Data.GetOwner() != target) break;
            }
            if (entry.Requests.Count == 0) Finish(entry);
        }
        private void Finish(Waiting entry)
        {
            if (!Current(entry)) return;
            waiting.Remove(entry.Node.Id); controller.RequestFinished(entry.Node);
        }
        private bool Deliver(ZRoutedRpc rpc, AuthorityController.NativeNode node, ZRoutedRpc.RoutedRPCData request, long target)
        {
            if (target != controller.Server && !Knows(target, node.Data)) return false;
            request.m_targetPeerID = target; request.m_parameters.SetPos(0);
            node.Refilled(request.m_methodHash);
            dispatching = true;
            try { if (target == controller.Server) handle(rpc, request); else route(rpc, request); }
            catch (Exception error) { throw new DispatchFailure(error); }
            finally { dispatching = false; }
            return true;
        }
        private static ZRoutedRpc.RoutedRPCData Copy(ZRoutedRpc.RoutedRPCData data)
        {
            return new ZRoutedRpc.RoutedRPCData { m_msgID = data.m_msgID, m_senderPeerID = data.m_senderPeerID,
                m_targetPeerID = data.m_targetPeerID, m_targetZDO = data.m_targetZDO, m_methodHash = data.m_methodHash,
                m_parameters = new ZPackage(data.m_parameters.GetArray()) };
        }
        private bool Knows(long target, ZDO data)
        { return knows(ZDOMan.instance, target, data) && (RecipientReady == null || RecipientReady(ZDOMan.instance, target, data)); }
        internal void Forwarded(ZRoutedRpc.RoutedRPCData request)
        {
            AuthorityController.NativeNode node = controller.RequestNode(request.m_targetZDO, request.m_methodHash);
            if (node == null) return; // Destroyed/unmanaged native instances are not recreated by a relay.
            Waiting entry;
            if (!waiting.TryGetValue(node.Id, out entry)) { entry = new Waiting { Node = node }; waiting.Add(node.Id, entry); }
            node.Refilled(request.m_methodHash); Hold(entry, Copy(request)); Flush(node.Id);
        }
        private static void Hold(Waiting entry, ZRoutedRpc.RoutedRPCData request)
        {
            float now = UnityEngine.Time.unscaledTime;
            entry.Requests.Enqueue(new QueuedRequest { Packet = request, At = now }); entry.Node.Requests++;
            int method = request.m_methodHash;
            if (method == open || method == stack || method == take) entry.NextExpiry = Math.Min(entry.NextExpiry, now + InventoryRequestLifetime);
        }
        private void Expire(Waiting entry)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now < entry.NextExpiry) return;
            entry.NextExpiry = Single.PositiveInfinity;
            int count = entry.Requests.Count;
            for (int i = 0; i < count; i++)
            {
                QueuedRequest queued = entry.Requests.Dequeue();
                int method = queued.Packet.m_methodHash;
                string response = ResponseName(method);
                if (response == null || (now >= queued.At && now - queued.At < InventoryRequestLifetime))
                {
                    entry.Requests.Enqueue(queued);
                    if (response != null) entry.NextExpiry = Math.Min(entry.NextExpiry, queued.At + InventoryRequestLifetime);
                    continue;
                }
                entry.Node.Requests--;
                Deny(entry.Node, queued.Packet, response);
            }
        }
        private static string ResponseName(int method)
        { return method == open ? "RPC_OpenResponse" : method == stack ? "RPC_StackResponse" : method == take ? "RPC_TakeAllResponse" : null; }
        private void Deny(AuthorityController.NativeNode node, ZRoutedRpc.RoutedRPCData request, string response)
        {
            if (response == null || !ZNet.instance || ZRoutedRpc.instance == null) return;
            ZNetPeer sender = ZNet.instance.GetPeer(request.m_senderPeerID);
            if (sender == null || !sender.IsReady()) return;
            // The response remains native, including when the server view unloads.
            // Its routing uses the retained identity, not a destroyed view pointer.
            try { ZRoutedRpc.instance.InvokeRoutedRPC(sender.m_uid, node.Id, response, false); }
            catch (Exception error) { controller.RequestFault(error); }
        }
        private void Cancel(Waiting entry)
        {
            foreach (QueuedRequest queued in entry.Requests) Deny(entry.Node, queued.Packet, ResponseName(queued.Packet.m_methodHash));
            entry.Node.Requests -= entry.Requests.Count; entry.Requests.Clear();
        }
        private static void Force(Waiting entry, long target)
        {
            if (entry.SentTo == target && entry.SentRevision == entry.Node.Data.OwnerRevision) return;
            entry.SentTo = target; entry.SentRevision = entry.Node.Data.OwnerRevision;
            ZDOMan.instance.ForceSendZDO(target, entry.Node.Id);
        }
        internal void Remove(AuthorityController.NativeNode node)
        { Waiting entry; if (waiting.TryGetValue(node.Id, out entry)) { waiting.Remove(node.Id); Cancel(entry); } }
        internal void Clear()
        {
            if (waiting.Count == 0) return;
            // Teardown is rare; use its own snapshot so a reset inside a native
            // dispatch cannot invalidate a running full-pass enumeration.
            var removed = new List<Waiting>(waiting.Values); waiting.Clear();
            foreach (Waiting entry in removed) Cancel(entry);
        }

        private static Func<ZDOMan, long, ZDO, bool> BindKnowledge()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo getPeer = typeof(ZDOMan).GetMethod("GetPeer", flags, null, new[] { typeof(long) }, null);
            if (getPeer == null) throw new NotSupportedException("Native ZDO peer lookup changed.");
            Type peer = getPeer.ReturnType, info = peer.GetNestedType("PeerZDOInfo", BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo records = peer.GetField("m_zdos", flags), revision = info == null ? null : info.GetField("m_ownerRevision", flags);
            if (records == null || revision == null || revision.FieldType != typeof(ushort) ||
                records.FieldType != typeof(Dictionary<,>).MakeGenericType(typeof(ZDOID), info))
                throw new NotSupportedException("Native owner replication contract changed.");
            DynamicMethod method = new DynamicMethod("BotanicOwnerWasSent", typeof(bool), new[] { typeof(ZDOMan), typeof(long), typeof(ZDO) }, typeof(RequestRouter).Module, true);
            ILGenerator il = method.GetILGenerator(); LocalBuilder p = il.DeclareLocal(peer), value = il.DeclareLocal(info); Label missing = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, getPeer); il.Emit(OpCodes.Stloc, p);
            il.Emit(OpCodes.Ldloc, p); il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Ldloc, p); il.Emit(OpCodes.Ldfld, records); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, typeof(ZDO).GetField("m_uid"));
            il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Callvirt, records.FieldType.GetMethod("TryGetValue")); il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Ldloca, value); il.Emit(OpCodes.Ldfld, revision); il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, AccessTools.PropertyGetter(typeof(ZDO), "OwnerRevision")); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
            il.MarkLabel(missing); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            return (Func<ZDOMan, long, ZDO, bool>)method.CreateDelegate(typeof(Func<ZDOMan, long, ZDO, bool>));
        }
    }
}
