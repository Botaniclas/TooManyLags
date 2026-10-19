using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace TooManyLagsDedicatedAddon
{
    [HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
    internal static class SourceReceive
    {
        [ThreadStatic] internal static long Peer;
        [HarmonyPrefix] private static void Prefix(ZRpc __0, out long __state)
        {
            __state = Peer; Peer = 0;
            Controller controller = Plugin.Current;
            if (controller == null || controller.PendingTransfers == 0) return;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (ReferenceEquals(peer.m_rpc, __0)) { Peer = peer.m_uid; break; }
        }
        [HarmonyFinalizer] private static void Finalizer(long __state) { Peer = __state; }
    }
    [HarmonyPatch(typeof(ZDO), "Deserialize")]
    internal static class SourceObject
    {
        [HarmonyPrefix] private static void Prefix(ZDO __instance, out ForecastReceiveState __state)
        {
            __state = new ForecastReceiveState();
            try { if (Plugin.Current != null) __state = Plugin.Current.CaptureForecastReceive(__instance); }
            catch (Exception error) { Plugin.Fault(error); }
        }
        [HarmonyPostfix] private static void Postfix(ZDO __instance, ForecastReceiveState __state)
        {
            try { if (Plugin.Current != null) { Plugin.Current.DataReceived(__instance, SourceReceive.Peer); Plugin.Current.ForecastReceived(__instance, __state); } }
            catch (Exception error) { Plugin.Fault(error); }
        }
    }
    internal static class NativeSnapshot
    {
        internal static void Validate()
        {
            var reads = new List<string>();
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData")))
            {
                MethodInfo method = instruction.operand as MethodInfo;
                if (method != null && method.DeclaringType == typeof(ZPackage) && method.Name.StartsWith("Read", StringComparison.Ordinal)) reads.Add(method.Name);
            }
            if (string.Join(",", reads.ToArray()) != "ReadInt,ReadZDOID,ReadZDOID,ReadUShort,ReadUInt,ReadLong,ReadVector3,ReadPackage")
                throw new NotSupportedException("Native ZDO receive envelope changed; targeted routing was not installed.");
            FieldInfo picked = AccessTools.Field(typeof(PickableItem), "m_picked");
            if (picked == null || picked.FieldType != typeof(bool)) throw new NotSupportedException("Native pickup completion contract changed.");
        }
        // Native RPC_ZDOData envelope; object contents remain serialized by Valheim.
        // Supported owner requests only; inventory bytes remain native data.
        // No inventory response, contents or text transaction is synthesized.
        internal static bool Send(ZDO data, long owner)
        {
            ZNetPeer peer = ZNet.instance.GetPeer(owner);
            if (peer == null || !peer.IsReady() || peer.m_rpc == null || !peer.m_rpc.IsConnected()) return false;
            ushort revision = data.OwnerRevision; uint version = data.DataRevision;
            var payload = new ZPackage(); data.Serialize(payload);
            if (!data.IsValid() || data.GetOwner() != owner || data.OwnerRevision != revision || data.DataRevision != version) return false;
            var packet = new ZPackage();
            packet.Write(0); packet.Write(data.m_uid); packet.Write(revision); packet.Write(version);
            packet.Write(owner); packet.Write(data.GetPosition()); packet.Write(payload); packet.Write(ZDOID.None);
            peer.m_rpc.Invoke("ZDOData", packet);
            return peer.m_rpc.IsConnected() && data.IsValid() && data.GetOwner() == owner && data.OwnerRevision == revision && data.DataRevision == version;
        }
        // Send the real new owner to both involved creature peers without
        // waiting for a periodic world-send pass. Not a source acknowledgement.
        internal static bool SendOwnerChange(ZDO data, long recipient)
        {
            ZNetPeer peer = ZNet.instance.GetPeer(recipient);
            if (peer == null || !peer.IsReady() || peer.m_rpc == null || !peer.m_rpc.IsConnected()) return false;
            long owner = data.GetOwner(); ushort revision = data.OwnerRevision; uint version = data.DataRevision;
            var payload = new ZPackage(); data.Serialize(payload);
            if (!data.IsValid() || data.GetOwner() != owner || data.OwnerRevision != revision || data.DataRevision != version) return false;
            var packet = new ZPackage(); packet.Write(0); packet.Write(data.m_uid); packet.Write(revision); packet.Write(version);
            packet.Write(owner); packet.Write(data.GetPosition()); packet.Write(payload); packet.Write(ZDOID.None);
            peer.m_rpc.Invoke("ZDOData", packet); return peer.m_rpc.IsConnected();
        }
    }
}
