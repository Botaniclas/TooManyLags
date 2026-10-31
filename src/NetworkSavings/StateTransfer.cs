using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace TooManyLags.NetworkSavings
{
    // Reuses the native ordered ZDOData stream, native revision bookkeeping,
    // native compression and native owner notifications. No per-object ACKs.
    internal static class StateTransfer
    {
        private sealed class Link
        {
            internal ZNetPeer Peer;
            internal bool Supported, SendFailed;
        }
        private sealed class Invalid { internal ZDOID Id; }
        private struct SendScope
        {
            internal object Peer;
            internal bool Compact, Forced, Omitted;
        }
        private sealed class ReferenceComparer : IEqualityComparer<ZDO>
        {
            public bool Equals(ZDO a, ZDO b) { return ReferenceEquals(a, b); }
            public int GetHashCode(ZDO value) { return RuntimeHelpers.GetHashCode(value); }
        }
        private const string RepairName = "TooManyLags_StateRepair_1";
        private static readonly int DataHash = "ZDOData".GetStableHashCode();
        private const int RepairLimit = 128;
        private static readonly Dictionary<ZRpc, Link> links = new Dictionary<ZRpc, Link>();
        private static readonly HashSet<ZDO> distinct = new HashSet<ZDO>(new ReferenceComparer());
        private static ConditionalWeakTable<ZDO, Invalid> invalid = new ConditionalWeakTable<ZDO, Invalid>();
        private static readonly List<ZDOID> repairs = new List<ZDOID>();
        private static Harmony patches;
        private static Func<object, ZNetPeer> nativePeer;
        private static Func<object, ZDO, bool> sameData;
        private static Action<object, ZDOID> forget;
        private static Action<object> clearLedger;
        private static Func<ZDOMan, List<ZDO>> sendList;
        private static Func<ZDOMan, ZRpc, object> findPeer;
        private static Action<ZDOMan, ZDO, int> addPortal;
        private static object sending;
        private static ZRpc receiving;
        private static bool compact, forceAdded, filtering, hasInvalid, omitted;
        internal static bool Active { get; private set; }

        internal static void Start(Assembly fgn)
        {
            if (patches != null) { Active = true; return; }
            Bind();
            // Check both original and already-transformed bodies before any
            // capability is advertised. Unknown contracts keep full payloads.
            MethodInfo send = AccessTools.Method(typeof(ZDOMan), "SendZDOs");
            MethodInfo receive = AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData");
            RewriteSend(PatchProcessor.GetOriginalInstructions(send));
            RewriteSend(PatchProcessor.GetCurrentInstructions(send));
            RewriteReceive(PatchProcessor.GetOriginalInstructions(receive));
            RewriteReceive(PatchProcessor.GetCurrentInstructions(receive));
            MethodInfo compression = CompressionBridge.Validate(fgn);
            Harmony h = new Harmony(Plugin.Id + ".state-transfer");
            try
            {
                h.Patch(send, prefix: Hook("EnterSend", Priority.First),
                    transpiler: Hook("SendTranspiler", Priority.Last), finalizer: Hook("LeaveSend", Priority.Last));
                h.Patch(receive, prefix: Hook("EnterReceive", Priority.First),
                    transpiler: Hook("ReceiveTranspiler", Priority.Last), finalizer: Hook("LeaveReceive", Priority.Last));
                h.Patch(AccessTools.Method(typeof(ZDO), "Deserialize"), finalizer: Hook("Decoded", Priority.Last));
                h.Patch(AccessTools.Method(typeof(ZDOMan), "RemovePeer"), prefix: Hook("Disconnected", Priority.First));
                h.Patch(AccessTools.Method(typeof(ZDOMan), "AddForceSendZdos"), prefix: Hook("BeforeForced", Priority.First), postfix: Hook("AfterForced", Priority.Last));
                CompressionBridge.Install(h, compression);
                patches = h; Active = true;
            }
            catch
            {
                try { h.UnpatchSelf(); } catch { /* No capability was advertised. Preserve the installation error. */ }
                throw;
            }
        }
        private static HarmonyMethod Hook(string name, int priority)
        { return new HarmonyMethod(typeof(StateTransfer), name) { priority = priority }; }
        // The receive guard remains installed until process exit. A peer may
        // already have queued an owner-only record when a local mode stops.
        internal static void Stop() { Active = false; }
        internal static void Reset()
        {
            links.Clear(); invalid = new ConditionalWeakTable<ZDO, Invalid>(); hasInvalid = false;
            distinct.Clear(); repairs.Clear(); sending = null; receiving = null; compact = forceAdded = filtering = omitted = false;
        }
        internal static void Connect(ZNetPeer peer)
        {
            if (!Active || peer == null || peer.m_rpc == null || links.ContainsKey(peer.m_rpc)) return;
            Link link = new Link { Peer = peer }; links.Add(peer.m_rpc, link);
            peer.m_rpc.Register<ZPackage>(RepairName, Repair);
        }
        internal static void Enable(ZRpc rpc, bool supported)
        { Link link; if (rpc != null && links.TryGetValue(rpc, out link)) link.Supported = supported; }
        private static void Disconnected(ZNetPeer netPeer)
        { if (netPeer != null && netPeer.m_rpc != null) links.Remove(netPeer.m_rpc); }

        private static void EnterSend(object __0, out SendScope __state)
        {
            __state = new SendScope { Peer = sending, Compact = compact, Forced = forceAdded, Omitted = omitted };
            // Native sending is synchronous on the main thread. Reentrancy
            // uses the full native body and never borrows the outer scratch.
            sending = __0; compact = false; forceAdded = false; omitted = false;
            if (!Active || __state.Peer != null) return;
            ZNetPeer peer = nativePeer(__0); Link link;
            compact = peer != null && peer.m_rpc != null && links.TryGetValue(peer.m_rpc, out link) &&
                ReferenceEquals(link.Peer, peer) && link.Supported && !link.SendFailed;
        }
        private static Exception LeaveSend(Exception __exception, SendScope __state, object __0, ZDOMan __instance)
        {
            if (__exception != null && compact)
            {
                // Native updates its optimistic sent ledger/removes dirty IDs
                // before Invoke. Never reuse those entries after a failed send.
                try
                {
                    ZNetPeer peer = nativePeer(__0); Link link;
                    if (peer != null && peer.m_rpc != null && links.TryGetValue(peer.m_rpc, out link)) link.SendFailed = true;
                    clearLedger(__0);
                    if (peer != null) foreach (ZDO data in sendList(__instance))
                        if (data != null && data.IsValid()) __instance.ForceSendZDO(peer.m_uid, data.m_uid);
                }
                catch { /* Preserve the original native exception. */ }
            }
            sending = __state.Peer; compact = __state.Compact; forceAdded = __state.Forced; omitted = __state.Omitted;
            return __exception;
        }
        private static void Serialize(ZDO data, ZPackage body)
        {
            // This reads the OLD native sent revision, before SendZDOs writes
            // its new entry. Changed data still uses the original serializer,
            // including FiresGhettoNetworking/CommunityPatch hooks.
            Invalid error;
            if (compact && sending != null && (!hasInvalid || !invalid.TryGetValue(data, out error) || error.Id != data.m_uid) && sameData(sending, data))
            { omitted = true; return; }
            data.Serialize(body);
        }
        // Called only by FGN's existing compressor, inside this synchronous
        // native send. No second compression attempt or new packet format.
        internal static int CompressionMinimum(byte[] packet)
        {
            return compact && omitted && sending != null && packet != null && packet.Length >= 4 &&
                BitConverter.ToInt32(packet, 0) == DataHash ? 128 : 512;
        }
        private static void BeforeForced(List<ZDO> __1, out int __state) { __state = __1.Count; }
        private static void AfterForced(List<ZDO> __1, int __state)
        { if (sending != null && __1.Count > __state) forceAdded = true; }
        private static void Unique(List<ZDO> items)
        {
            if (!forceAdded || filtering || items.Count < 2) return;
            filtering = true;
            try
            {
                int kept = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    ZDO data = items[i];
                    if (data != null && distinct.Add(data)) items[kept++] = data;
                }
                if (kept != items.Count) items.RemoveRange(kept, items.Count - kept);
            }
            finally { distinct.Clear(); filtering = false; }
        }
        private static void EnterReceive(ZRpc rpc, out ZRpc __state)
        {
            __state = receiving;
            if (__state != null && repairs.Count != 0) FlushRepairs(__state);
            receiving = rpc;
        }
        private static Exception LeaveReceive(Exception __exception, ZRpc __state)
        {
            ZRpc source = receiving; receiving = __state;
            // Only failures allocate a repair envelope. Normal handoffs have
            // no extra message, cache entry, scan or timer.
            if (repairs.Count != 0)
            {
                FlushRepairs(source);
            }
            return __exception;
        }
        private static bool Accept(ZDO data, ZPackage body, uint revision, ZRpc source, ZDOID id)
        {
            bool broken = false; Invalid error;
            if (hasInvalid && data != null && invalid.TryGetValue(data, out error)) broken = error.Id == data.m_uid;
            if (body.Size() != 0)
            {
                if (broken && revision < data.DataRevision) return false;
                // Native advances revisions before Deserialize. Repair only
                // this previously failed equal-revision body; otherwise native
                // would skip it forever. Never roll a revision backwards.
                if (broken && revision == data.DataRevision)
                {
                    try
                    {
                        data.Deserialize(body); body.SetPos(0);
                        addPortal(ZDOMan.instance, data, data.GetPrefab());
                    }
                    // A full repair which is itself unusable must not create
                    // a request/reply loop. A later real update can retry.
                    catch { return false; }
                }
                return true;
            }
            // Same checks as native's owner-only branch, BEFORE any mutation.
            // The dictionary lookup was already done by the native receiver.
            if (data != null && data.DataRevision >= revision && !broken) return true;
            NeedRepair(source, id); return false;
        }
        private static void NeedRepair(ZRpc rpc, ZDOID id)
        {
            Link link;
            if (!ReferenceEquals(rpc, receiving) || !links.TryGetValue(rpc, out link) || !link.Supported || repairs.Contains(id)) return;
            if (repairs.Count == RepairLimit) FlushRepairs(rpc);
            repairs.Add(id);
        }
        private static void FlushRepairs(ZRpc rpc)
        {
            try
            {
                Link link;
                if (rpc == null || !links.TryGetValue(rpc, out link) || !link.Supported) return;
                ZPackage packet = new ZPackage(); packet.Write(repairs.Count);
                foreach (ZDOID id in repairs) packet.Write(id);
                rpc.Invoke(RepairName, packet);
            }
            catch (Exception error) { Plugin.Warn("State repair request failed: " + error.GetBaseException().Message); }
            finally { repairs.Clear(); }
        }
        private static Exception Decoded(ZDO __instance, Exception __exception)
        {
            if (__exception != null)
            {
                invalid.Remove(__instance); invalid.Add(__instance, new Invalid { Id = __instance.m_uid }); hasInvalid = true;
            }
            else if (hasInvalid) invalid.Remove(__instance);
            return __exception;
        }
        private static void Repair(ZRpc rpc, ZPackage packet)
        {
            Link link;
            if (packet == null || packet.Size() < sizeof(int) || !links.TryGetValue(rpc, out link) || !link.Supported || ZDOMan.instance == null) return;
            int count = packet.ReadInt();
            if (count < 1 || count > RepairLimit || packet.Size() != sizeof(int) + count * 12) return;
            object peer = findPeer(ZDOMan.instance, rpc); if (peer == null) return;
            for (int i = 0; i < count; i++)
            {
                ZDOID id = packet.ReadZDOID();
                if (ZDOMan.instance.GetZDO(id) == null) continue;
                // ForceSend alone does NOT override an optimistic native
                // ledger. Forget it only for a real requested repair.
                forget(peer, id); ZDOMan.instance.ForceSendZDO(link.Peer.m_uid, id);
            }
        }

        private static IEnumerable<CodeInstruction> SendTranspiler(IEnumerable<CodeInstruction> source) { return RewriteSend(source); }
        internal static List<CodeInstruction> RewriteSend(IEnumerable<CodeInstruction> source)
        {
            var output = new List<CodeInstruction>(); int body = 0, lists = 0;
            foreach (CodeInstruction item in source)
            {
                CodeInstruction copy = new CodeInstruction(item);
                MethodInfo method = copy.operand as MethodInfo;
                if (method != null && method.DeclaringType == typeof(ZDO) && method.Name == "Serialize")
                { copy.opcode = OpCodes.Call; copy.operand = typeof(StateTransfer).GetMethod("Serialize", BindingFlags.Static | BindingFlags.NonPublic); body++; }
                output.Add(copy);
                if (method != null && method.DeclaringType == typeof(ZDOMan) && method.Name == "CreateSyncList")
                {
                    FieldInfo list = typeof(ZDOMan).GetField("m_tempToSync", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (list == null || list.FieldType != typeof(List<ZDO>)) throw new NotSupportedException("Native send list changed.");
                    output.Add(new CodeInstruction(OpCodes.Ldarg_0)); output.Add(new CodeInstruction(OpCodes.Ldfld, list));
                    output.Add(new CodeInstruction(OpCodes.Call, typeof(StateTransfer).GetMethod("Unique", BindingFlags.Static | BindingFlags.NonPublic))); lists++;
                }
            }
            if (body != 1 || lists != 1) throw new NotSupportedException("Native ordered state sender changed.");
            return output;
        }
        private static IEnumerable<CodeInstruction> ReceiveTranspiler(IEnumerable<CodeInstruction> source, ILGenerator generator)
        { return RewriteReceive(source, generator); }
        internal static List<CodeInstruction> RewriteReceive(IEnumerable<CodeInstruction> source, ILGenerator generator = null)
        {
            var output = new List<CodeInstruction>(); foreach (CodeInstruction item in source) output.Add(new CodeInstruction(item));
            int get = -1, state = -1, package = -1, loop = -1, id = -1;
            for (int i = 0; i < output.Count; i++)
            {
                MethodInfo method = output[i].operand as MethodInfo;
                if (method == null) continue;
                if (method.DeclaringType == typeof(ZPackage) && method.Name == "ReadUInt") state = i + 1;
                if (method.DeclaringType == typeof(ZPackage) && method.Name == "ReadPackage" && method.GetParameters()[0].ParameterType.IsByRef) package = i - 1;
                if (method.DeclaringType == typeof(ZPackage) && method.Name == "ReadZDOID") { loop = i - 1; id = i + 1; }
                if (method.DeclaringType == typeof(ZDOMan) && method.Name == "GetZDO" && state >= 0 && package >= 0) { get = i + 1; break; }
            }
            if (get < 0 || state < 0 || package < 0 || loop < 0 || id < 0 || !output[get].IsStloc() || !output[state].IsStloc() || !output[id].IsStloc())
                throw new NotSupportedException("Native ordered state receiver changed.");
            if (generator == null) return output; // Validate without allocating branch labels.
            Label next = generator.DefineLabel(); output[loop].labels.Add(next);
            object bodyLocal = output[package].operand;
            if (output[package].opcode != OpCodes.Ldloca && output[package].opcode != OpCodes.Ldloca_S)
                throw new NotSupportedException("Native nested state body changed.");
            var checks = new List<CodeInstruction> { Load(output[get]), new CodeInstruction(OpCodes.Ldloc, bodyLocal), Load(output[state]),
                new CodeInstruction(OpCodes.Ldarg_1), Load(output[id]), new CodeInstruction(OpCodes.Call, typeof(StateTransfer).GetMethod("Accept", BindingFlags.Static | BindingFlags.NonPublic)),
                new CodeInstruction(OpCodes.Brfalse, next) };
            output.InsertRange(get + 1, checks); return output;
        }
        private static CodeInstruction Load(CodeInstruction store)
        {
            if (store.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
            if (store.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
            if (store.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
            if (store.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
            return new CodeInstruction(OpCodes.Ldloc, store.operand);
        }
        private static void Bind()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type peer = typeof(ZDOMan).GetNestedType("ZDOPeer", BindingFlags.NonPublic);
            FieldInfo entries = peer == null ? null : peer.GetField("m_zdos", flags);
            FieldInfo wire = peer == null ? null : peer.GetField("m_peer", flags);
            if (entries == null || wire == null || wire.FieldType != typeof(ZNetPeer)) throw new NotSupportedException("Native peer ledger changed.");
            Type info = entries.FieldType.GetGenericArguments()[1]; FieldInfo revision = info.GetField("m_dataRevision", flags);
            MethodInfo lookup = entries.FieldType.GetMethod("TryGetValue"), remove = entries.FieldType.GetMethod("Remove", new[] { typeof(ZDOID) });
            if (revision == null || revision.FieldType != typeof(uint) || lookup == null || remove == null) throw new NotSupportedException("Native peer state revision changed.");
            nativePeer = FieldReader.Create<object, ZNetPeer>(wire);
            DynamicMethod check = new DynamicMethod("TMLSameNativeState", typeof(bool), new[] { typeof(object), typeof(ZDO) }, typeof(StateTransfer).Module, true);
            ILGenerator il = check.GetILGenerator(); LocalBuilder snapshot = il.DeclareLocal(info); Label missing = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, peer); il.Emit(OpCodes.Ldfld, entries);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldfld, typeof(ZDO).GetField("m_uid", flags)); il.Emit(OpCodes.Ldloca, snapshot);
            il.Emit(OpCodes.Callvirt, lookup); il.Emit(OpCodes.Brfalse, missing); il.Emit(OpCodes.Ldloca, snapshot); il.Emit(OpCodes.Ldfld, revision);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, typeof(ZDO).GetProperty("DataRevision", flags).GetGetMethod(true)); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
            il.MarkLabel(missing); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            sameData = (Func<object, ZDO, bool>)check.CreateDelegate(typeof(Func<object, ZDO, bool>));
            DynamicMethod erase = new DynamicMethod("TMLForgetNativeState", typeof(void), new[] { typeof(object), typeof(ZDOID) }, typeof(StateTransfer).Module, true);
            il = erase.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, peer); il.Emit(OpCodes.Ldfld, entries);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, remove); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret);
            forget = (Action<object, ZDOID>)erase.CreateDelegate(typeof(Action<object, ZDOID>));
            DynamicMethod clear = new DynamicMethod("TMLResetNativeLedger", typeof(void), new[] { typeof(object) }, typeof(StateTransfer).Module, true);
            il = clear.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, peer); il.Emit(OpCodes.Ldfld, entries);
            il.Emit(OpCodes.Callvirt, entries.FieldType.GetMethod("Clear")); il.Emit(OpCodes.Ret);
            clearLedger = (Action<object>)clear.CreateDelegate(typeof(Action<object>));
            sendList = FieldReader.Create<ZDOMan, List<ZDO>>(typeof(ZDOMan).GetField("m_tempToSync", BindingFlags.Instance | BindingFlags.NonPublic));
            MethodInfo find = typeof(ZDOMan).GetMethod("FindPeer", flags, null, new[] { typeof(ZRpc) }, null);
            if (find == null || find.ReturnType != peer) throw new NotSupportedException("Native peer lookup changed.");
            DynamicMethod locate = new DynamicMethod("TMLFindNativePeer", typeof(object), new[] { typeof(ZDOMan), typeof(ZRpc) }, typeof(StateTransfer).Module, true);
            il = locate.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, find); il.Emit(OpCodes.Ret);
            findPeer = (Func<ZDOMan, ZRpc, object>)locate.CreateDelegate(typeof(Func<ZDOMan, ZRpc, object>));
            addPortal = (Action<ZDOMan, ZDO, int>)Delegate.CreateDelegate(typeof(Action<ZDOMan, ZDO, int>), typeof(ZDOMan).GetMethod("AddIfPortal", flags));
        }
    }
}

