using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace TooManyLags.PlayerSimulation
{
    internal static class FgnRpcIntegration
    {
        private static FieldInfo handler;
        private static Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData> original;
        internal static void Install(Harmony harmony, Assembly assembly)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            handler = assembly.GetType("FiresGhettoNetworkMod.VanillaAccess", true).GetField("HandleRoutedRpc", flags);
            if (handler == null || handler.FieldType != typeof(Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>))
                throw new NotSupportedException("FiresGhettoNetworking's local RPC delegate changed.");
            original = (Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>)handler.GetValue(null);
            MethodInfo process = assembly.GetType("FiresGhettoNetworkMod.RoutedRpcManager", true).GetMethod("ProcessRoutedRPC", flags);
            Type station = assembly.GetType("FiresGhettoNetworkMod.StationRouter", true);
            MethodInfo route = station.GetMethod("TryRoute", flags, null, new[] { typeof(ZRoutedRpc), typeof(ZRoutedRpc.RoutedRPCData) }, null);
            MethodInfo deliver = station.GetMethod("Deliver", flags, null, new[] { typeof(ZRoutedRpc), typeof(ZDO), typeof(ZRoutedRpc.RoutedRPCData), typeof(long) }, null);
            bool compactDeliver = deliver == null;
            if (compactDeliver) deliver = station.GetMethod("Deliver", flags, null, new[] { typeof(ZDO), typeof(ZRoutedRpc.RoutedRPCData), typeof(long) }, null);
            if (original == null || process == null || process.ReturnType != typeof(void) || route == null || deliver == null || route.ReturnType != typeof(bool) || deliver.ReturnType != typeof(bool))
                throw new NotSupportedException("FiresGhettoNetworking's station routing contract changed.");
            Rewrite(PatchProcessor.GetOriginalInstructions(process)); Rewrite(PatchProcessor.GetCurrentInstructions(process));
            harmony.Patch(process, transpiler: new HarmonyMethod(typeof(FgnRpcIntegration), "Transpiler"));
            harmony.Patch(route, prefix: new HarmonyMethod(typeof(FgnRpcIntegration), "Station") { priority = Priority.First });
            harmony.Patch(deliver, prefix: new HarmonyMethod(typeof(FgnRpcIntegration), compactDeliver ? "HeldStationCurrent" : "HeldStation") { priority = Priority.First });
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source) { return Rewrite(source); }
        private static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source)
        { return TooManyLags.FgnRpcIntegration.Rewrite(source, handler, AccessTools.Method(typeof(FgnRpcIntegration), "Dispatch")); }
        private static void Dispatch(ZRoutedRpc rpc, ZRoutedRpc.RoutedRPCData packet)
        { if (RequestRouter.Handle(rpc, packet, true)) original(rpc, packet); }
        private static bool Station(ZRoutedRpc __0, ZRoutedRpc.RoutedRPCData __1, bool __runOriginal, ref bool __result)
        {
            if (!__runOriginal || RequestRouter.Handle(__0, __1, false)) return true;
            __result = true; return false;
        }
        private static bool HeldStation(ZRoutedRpc __0, ZRoutedRpc.RoutedRPCData __2, long __3, bool __runOriginal, ref bool __result)
        {
            if (!__runOriginal || RequestRouter.Handle(__0, __2, __3 == ZDOMan.GetSessionID())) return true;
            __result = true; return false;
        }
        private static bool HeldStationCurrent(ZRoutedRpc.RoutedRPCData __1, long __2, bool __runOriginal, ref bool __result)
        { return HeldStation(ZRoutedRpc.instance, __1, __2, __runOriginal, ref __result); }
    }
}
