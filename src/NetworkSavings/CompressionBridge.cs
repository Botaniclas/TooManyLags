using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TooManyLags.NetworkSavings
{
    internal static class CompressionBridge
    {
        internal static MethodInfo Validate(Assembly fgn)
        {
            Type type = fgn.GetType("FiresGhettoNetworkMod.CompressionPolicy", false);
            MethodInfo method = type == null ? null : type.GetMethod("TryCompress", BindingFlags.Instance | BindingFlags.NonPublic);
            // The compact feature is optional. Do not silently ship a new
            // codec to peers when their compression contract is unknown.
            if (method == null) throw new NotSupportedException("FGN's bounded packet compression contract is unavailable.");
            Rewrite(PatchProcessor.GetOriginalInstructions(method)); Rewrite(PatchProcessor.GetCurrentInstructions(method));
            return method;
        }
        internal static void Install(Harmony harmony, MethodInfo method)
        {
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(CompressionBridge), "Transpiler") { priority = Priority.Last });
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source) { return Rewrite(source); }
        internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source)
        {
            var result = new List<CodeInstruction>(); int changed = 0;
            foreach (CodeInstruction item in source)
            {
                CodeInstruction copy = new CodeInstruction(item);
                if (copy.opcode == OpCodes.Ldc_I4 && Object.Equals(copy.operand, 512))
                {
                    copy.opcode = OpCodes.Ldarg_1; copy.operand = null; result.Add(copy);
                    result.Add(new CodeInstruction(OpCodes.Call, typeof(StateTransfer).GetMethod("CompressionMinimum", BindingFlags.Static | BindingFlags.NonPublic))); changed++;
                }
                else result.Add(copy);
            }
            if (changed != 1) throw new NotSupportedException("FGN's packet compression threshold changed.");
            return result;
        }
    }
}
