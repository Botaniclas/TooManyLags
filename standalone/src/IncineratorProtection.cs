using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsStandalone
{
    // Imported native-state guard from addon 1.0.45. No simulated burning
    // timer: the owner's field covers the coroutine and delayed StopAOE.
    internal static class IncineratorProtection
    {
        private static readonly Func<Incinerator, bool> read = Bind();
        internal static bool Active;
        internal static bool Supported { get { return read != null; } }
        private static Func<Incinerator, bool> Bind()
        {
            try { return FieldReader.Create<Incinerator, bool>(typeof(Incinerator).GetField("isInUse", BindingFlags.Instance | BindingFlags.NonPublic)); }
            catch (Exception) { return null; }
        }
        internal static bool Busy(Incinerator station) { return station && read != null && read(station); }
        internal static bool Allow(Container inventory, long sender, string response)
        {
            if (!Active || !inventory) return true;
            ZNetView view = inventory.m_rootObjectOverride ? inventory.m_rootObjectOverride : inventory.GetComponentInParent<ZNetView>(true);
            if (!view || !view.IsValid() || !view.IsOwner() || sender == view.GetZDO().GetOwner()) return true;
            Incinerator station = view.GetComponent<Incinerator>();
            if (!station || station.m_container != inventory || !Busy(station)) return true;
            // Refuse this request through the native response instead of
            // changing IsInUse, which the burning coroutine itself checks.
            view.InvokeRPC(sender, response, false); return false;
        }
    }

    [HarmonyPatch(typeof(Container), "RPC_RequestOpen")]
    internal static class IncineratorOpenPatch
    { [HarmonyPrefix] private static bool Prefix(Container __instance, long __0, bool __runOriginal) { return __runOriginal && IncineratorProtection.Allow(__instance, __0, "RPC_OpenResponse"); } }
    [HarmonyPatch(typeof(Container), "RPC_RequestStack")]
    internal static class IncineratorStackPatch
    { [HarmonyPrefix] private static bool Prefix(Container __instance, long __0, bool __runOriginal) { return __runOriginal && IncineratorProtection.Allow(__instance, __0, "RPC_StackResponse"); } }
    [HarmonyPatch(typeof(Container), "RPC_RequestTakeAll")]
    internal static class IncineratorTakeAllPatch
    { [HarmonyPrefix] private static bool Prefix(Container __instance, long __0, bool __runOriginal) { return __runOriginal && IncineratorProtection.Allow(__instance, __0, "RPC_TakeAllResponse"); } }
}
