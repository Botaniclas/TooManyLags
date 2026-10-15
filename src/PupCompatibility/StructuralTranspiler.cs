using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TooManyLags.PupCompatibility
{
    internal static class StructuralTranspiler
    {
        // Replace only the condition enclosing UpdateSupport + HaveSupport.
        // The native rain, snow, fire, biome, event and damage code stays intact.
        internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source,
            FieldInfo field, MethodInfo decision, MethodInfo supportDecision)
        {
            List<CodeInstruction> code = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in source) code.Add(new CodeInstruction(instruction));
            int index = -1;
            for (int i = 1; i + 1 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldfld || !field.Equals(code[i].operand)) continue;
                if (index >= 0 || code[i - 1].opcode != OpCodes.Ldarg_0 ||
                    (code[i + 1].opcode != OpCodes.Brfalse && code[i + 1].opcode != OpCodes.Brfalse_S))
                    throw new NotSupportedException("Unexpected native structural wear condition.");
                index = i;
            }
            if (index < 0 || decision == null || decision.ReturnType != typeof(bool) ||
                decision.GetParameters().Length != 1 || decision.GetParameters()[0].ParameterType != field.DeclaringType)
                throw new NotSupportedException("Native structural wear condition missing.");
            bool support = false, minimum = false;
            Label target = (Label)code[index + 1].operand;
            int end = code.FindIndex(index + 2, delegate(CodeInstruction instruction) { return instruction.labels.Contains(target); });
            if (end < 0) throw new NotSupportedException("Structural branch target missing.");
            for (int i = index + 2; i < end; i++)
            {
                MethodInfo method = code[i].operand as MethodInfo;
                if (method == null) continue;
                support |= method.Name == "UpdateSupport";
                if (method.Name == "HaveSupport")
                {
                    if (minimum || supportDecision == null || supportDecision.ReturnType != typeof(bool) ||
                        supportDecision.GetParameters().Length != 1 || supportDecision.GetParameters()[0].ParameterType != field.DeclaringType)
                        throw new NotSupportedException("Unexpected structural damage decision.");
                    minimum = true;
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = supportDecision;
                }
            }
            if (!support || !minimum) throw new NotSupportedException("Structural condition no longer encloses support and damage decision.");
            code[index].opcode = OpCodes.Call;
            code[index].operand = decision;
            return code;
        }
    }
}
