using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TooManyLags
{
    internal static class FgnRpcIntegration
    {
        private static FieldInfo handlerField;
        private static Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData> originalHandler;
        internal static void Install(Harmony harmony, Assembly fgn)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type access = fgn.GetType("FiresGhettoNetworkMod.VanillaAccess", true);
            handlerField = access.GetField("HandleRoutedRpc", flags);
            if (handlerField == null || handlerField.FieldType != typeof(Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>))
                throw new NotSupportedException("FGN local RPC delegate changed.");
            originalHandler = (Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>)handlerField.GetValue(null);
            MethodInfo process = fgn.GetType("FiresGhettoNetworkMod.RoutedRpcManager", true).GetMethod("ProcessRoutedRPC", flags);
            Type stations = fgn.GetType("FiresGhettoNetworkMod.StationRouter", true);
            MethodInfo route = stations.GetMethod("TryRoute", flags);
            MethodInfo deliver = stations.GetMethod("Deliver", flags, null, new[] { typeof(ZRoutedRpc), typeof(ZDO), typeof(ZRoutedRpc.RoutedRPCData), typeof(long) }, null);
            bool compactDeliver = deliver == null;
            if (compactDeliver) deliver = stations.GetMethod("Deliver", flags, null, new[] { typeof(ZDO), typeof(ZRoutedRpc.RoutedRPCData), typeof(long) }, null);
            MethodInfo result = fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true).GetMethod("OnResult", flags,
                null, new[] { typeof(ZRpc), typeof(ZDOID), typeof(bool), typeof(string) }, null);
            if (originalHandler == null || process == null || process.ReturnType != typeof(void) ||
                route == null || route.ReturnType != typeof(bool) || deliver == null || deliver.ReturnType != typeof(bool) ||
                result == null || result.ReturnType != typeof(void))
                throw new NotSupportedException("FGN RPC entry/held-station contracts changed.");
            Rewrite(PatchProcessor.GetOriginalInstructions(process), handlerField,
                AccessTools.Method(typeof(FgnRpcIntegration), "DispatchLocal"));
            Rewrite(PatchProcessor.GetCurrentInstructions(process), handlerField,
                AccessTools.Method(typeof(FgnRpcIntegration), "DispatchLocal"));
            harmony.Patch(process, transpiler: new HarmonyMethod(typeof(FgnRpcIntegration), "Transpiler"));
            harmony.Patch(route, prefix: new HarmonyMethod(typeof(FgnRpcIntegration), "Station") { priority = Priority.First });
            harmony.Patch(deliver, prefix: new HarmonyMethod(typeof(FgnRpcIntegration), compactDeliver ? "HeldStationCurrent" : "HeldStation") { priority = Priority.First });
            harmony.Patch(result, postfix: new HarmonyMethod(typeof(FgnRpcIntegration), "Result"));
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        { return Rewrite(source, handlerField, AccessTools.Method(typeof(FgnRpcIntegration), "DispatchLocal")); }
        internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source, FieldInfo field, MethodInfo replacement)
        {
            var result = new List<CodeInstruction>(); foreach (CodeInstruction item in source) result.Add(new CodeInstruction(item));
            int changed = 0;
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].opcode != OpCodes.Ldsfld || !field.Equals(result[i].operand)) continue;
                int call = -1;
                for (int j = i + 1; j < Math.Min(result.Count, i + 8); j++)
                {
                    MethodInfo invoke = result[j].operand as MethodInfo;
                    if (result[j].opcode == OpCodes.Callvirt && invoke != null && invoke.Name == "Invoke" && invoke.DeclaringType == field.FieldType) { call = j; break; }
                }
                if (call < 0) throw new NotSupportedException("FGN local delegate invocation shape changed.");
                result[i].opcode = OpCodes.Nop; result[i].operand = null;
                result[call].opcode = OpCodes.Call; result[call].operand = replacement; changed++;
            }
            if (changed == 0) throw new NotSupportedException("FGN local RPC delegate invocation missing.");
            return result;
        }
        private static void DispatchLocal(ZRoutedRpc rpc, ZRoutedRpc.RoutedRPCData data)
        {
            if (Plugin.Controller == null || !Plugin.Controller.Router.TryHandle(rpc, data, true)) originalHandler(rpc, data);
        }
        private static bool Station(ZRoutedRpc __0, ZRoutedRpc.RoutedRPCData __1, bool __runOriginal, ref bool __result)
        {
            if (!__runOriginal || Plugin.Controller == null || !Plugin.Controller.Router.TryHandle(__0, __1, false)) return true;
            __result = true; return false;
        }
        private static bool HeldStation(ZRoutedRpc __0, ZRoutedRpc.RoutedRPCData __2, long __3, bool __runOriginal, ref bool __result)
        {
            if (!__runOriginal || Plugin.Controller == null || !Plugin.Controller.Router.TryHandle(__0, __2, __3 == Plugin.Controller.Server)) return true;
            __result = true; return false;
        }
        private static bool HeldStationCurrent(ZRoutedRpc.RoutedRPCData __1, long __2, bool __runOriginal, ref bool __result)
        { return HeldStation(ZRoutedRpc.instance, __1, __2, __runOriginal, ref __result); }
        private static void Result(ZRpc __0, ZDOID __1, bool __2, string __3)
        {
            AuthorityController controller = Plugin.Controller;
            if (controller != null && controller.Cooperative != null) controller.Cooperative.Result(__0, __1, __2, __3);
        }
    }
}
