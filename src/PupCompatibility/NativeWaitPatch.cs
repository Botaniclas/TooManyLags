using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace TooManyLags.PupCompatibility
{
    [HarmonyPatch]
    internal static class NativeWaitPatch
    {
        private static MethodBase TargetMethod() { return Plugin.Native.ReadyMethod; }
        [HarmonyPrefix] private static bool Prefix(Vector3 __0, float __1, ref bool __result)
        {
            if (NativePup.ForceWait) { __result = false; return false; }
            // Native readiness skips local record cells on a dedicated server.
            // Its mob fallback handles this later; carts/items have no fallback.
            // Complete the existing query rather than adding another body guard.
            if (Plugin.Native != null && Plugin.Native.Active && ZNet.instance.IsDedicated() && !Plugin.Native.NativeDedicatedReadiness)
            { __result = Plugin.Native.ReadyPoint(__0, __1); return false; }
            return true;
        }
        [HarmonyPostfix] private static void Postfix(Vector3 __0, float __1, bool __runOriginal, ref bool __result)
        {
            NativePup native = Plugin.Native;
            if (__result && native != null && native.Active && native.NativeDedicatedReadiness && ZNet.instance.IsDedicated())
                // Preserve recipe/manifest/restore safeguards without bypassing
                // native readiness or synchronous work in its deferred body queue.
                __result = native.PreparedPoint(__0, __1, __runOriginal);
        }
    }
    [HarmonyPatch]
    internal static class NativeRetentionPatch
    {
        [HarmonyPrepare] private static bool Prepare()
        { return Plugin.Native != null && !Plugin.Native.NativeBodyRetention; }
        private static MethodBase TargetMethod() { return Plugin.Native.RetainMethod; }
        [HarmonyPostfix] private static void Postfix(Vector2Int __0, ref bool __result)
        {
            // Reuse PUP's existing owner-body cells. Native activation already
            // respects them; native complete-cluster unloading otherwise does not.
            if (!__result && Plugin.Native != null && Plugin.Native.Active)
                __result = Plugin.Native.NeededByNativeBody(__0);
        }
    }
}
