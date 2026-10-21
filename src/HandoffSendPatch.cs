using System.Collections.Generic;
using HarmonyLib;

namespace TooManyLags
{
    [HarmonyPatch(typeof(ZDOMan), "AddForceSendZdos")]
    internal static class HandoffSendPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        private static void Before(List<ZDO> __1, out int __state) { __state = __1.Count; }
        [HarmonyPostfix, HarmonyPriority(Priority.First)]
        private static void After(ZDOMan __instance, List<ZDO> __1, int __state, bool __runOriginal)
        {
            if (__runOriginal && Plugin.Controller != null) Plugin.Controller.PrioritizeForced(__instance, __1, __1.Count - __state);
        }
    }
}
