using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TooManyLags
{
    internal static class OwnershipTranspiler
    {
        internal static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source, MethodInfo replacement)
        {
            MethodInfo persistent = AccessTools.PropertyGetter(typeof(ZDO), "Persistent");
            MethodInfo setOwner = AccessTools.Method(typeof(ZDO), "SetOwner", new[] { typeof(long) });
            Type mobPolicy = typeof(OwnershipTranspiler).Assembly.GetType(typeof(OwnershipTranspiler).Namespace + ".FgnCombatOwnership", false);
            MethodInfo mobSetOwner = mobPolicy == null ? null : AccessTools.Method(mobPolicy, "NativeSetOwner", new[] { typeof(ZDO), typeof(long) });
            List<CodeInstruction> result = new List<CodeInstruction>();
            int getters = 0, writes = 0, areaTests = 0;
            foreach (CodeInstruction original in source)
            {
                CodeInstruction item = new CodeInstruction(original);
                MethodInfo method = item.operand as MethodInfo;
                if (method != null && (item.opcode == OpCodes.Call || item.opcode == OpCodes.Callvirt))
                {
                    if (method.Equals(persistent))
                    {
                        getters++;
                        item.opcode = OpCodes.Call;
                        item.operand = replacement;
                    }
                    // The imported AI policy wraps exactly the same two native
                    // writes. Accept that precise wrapper in either patch order.
                    if (method.Equals(setOwner) || method.Equals(mobSetOwner)) writes++;
                    if (method.DeclaringType == typeof(ZNetScene) && method.Name == "InActiveArea") areaTests++;
                }
                result.Add(item);
            }
            if (getters != 1 || writes != 2 || areaTests != 2 || replacement == null ||
                replacement.ReturnType != typeof(bool) || replacement.GetParameters().Length != 1 ||
                replacement.GetParameters()[0].ParameterType != typeof(ZDO))
                throw new NotSupportedException("Native building ownership loop changed; feature not installed.");
            return result;
        }
    }
}
