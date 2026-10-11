using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.PlayerSimulation
{
    internal static class NativeEvents
    {
        private static readonly HashSet<ZDO> receipts = new HashSet<ZDO>();
        internal static void Changed(ZDO data)
        {
            try { if (Plugin.Controller != null) Plugin.Controller.Changed(data); if (Plugin.Local != null) Plugin.Local.Observe(data); }
            catch (Exception error) { Plugin.Fault(error); }
        }
        internal static void Import(ZDO data)
        {
            try {
                if (Plugin.Local != null) Plugin.Local.Observe(data, false);
                if ((Plugin.Controller != null && Plugin.Controller.Handoffs.Pending(data.m_uid)) || (Plugin.Local != null && Plugin.Local.NeedsFinalImport(data))) receipts.Add(data);
            } catch (Exception error) { Plugin.Fault(error); }
        }
        internal static void ServerChanged(ZDO data)
        { try { if (Plugin.Controller != null) Plugin.Controller.Changed(data); } catch (Exception error) { Plugin.Fault(error); } }
        internal static void CompleteImports()
        { foreach (ZDO data in receipts) Changed(data); receipts.Clear(); }
        internal static void Reset() { receipts.Clear(); RequestRouter.Reset(); MobPolicy.ResetPrefabs(); }
    }
    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    internal static class ConnectionPatch
    { [HarmonyPostfix] private static void Postfix(ZNetPeer peer) { if (Plugin.Network != null) Plugin.Network.Register(peer); } }
    [HarmonyPatch(typeof(ZNet), "Shutdown")]
    internal static class ShutdownPatch
    { [HarmonyPrefix] private static void Prefix() { NativeEvents.Reset(); Plugin.ResetWorld(); } }
    [HarmonyPatch(typeof(ZDO), "Deserialize")]
    internal static class DeserializePatch
    { [HarmonyPostfix] private static void Postfix(ZDO __instance) { NativeEvents.Changed(__instance); } }
    [HarmonyPatch(typeof(ZDO), "InternalSetPosition")]
    internal static class PositionPatch
    { [HarmonyPostfix] private static void Postfix(ZDO __instance) { NativeEvents.ServerChanged(__instance); } }
    [HarmonyPatch(typeof(ZDO), "SetPrefab")]
    internal static class PrefabPatch
    { [HarmonyPostfix] private static void Postfix(ZDO __instance) { NativeEvents.ServerChanged(__instance); } }
    [HarmonyPatch(typeof(ZDO), "SetOwner")]
    internal static class OwnerPatch
    { [HarmonyPostfix] private static void Postfix(ZDO __instance) { NativeEvents.Changed(__instance); } }
    [HarmonyPatch(typeof(ZDO), "SetOwnerInternal")]
    internal static class ImportOwnerPatch
    { [HarmonyPostfix] private static void Postfix(ZDO __instance) { NativeEvents.Import(__instance); } }
    [HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
    internal static class FinishImportPatch
    { [HarmonyPostfix] private static void Postfix() { NativeEvents.CompleteImports(); } }
    [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
    internal static class DestroyPatch
    { [HarmonyPrefix] private static void Prefix(ZDOID uid) { if (Plugin.Controller != null) Plugin.Controller.Destroyed(uid); } }
    [HarmonyPatch(typeof(ZNetScene), "AddInstance")]
    internal static class InstancePatch
    { [HarmonyPostfix] private static void Postfix(ZNetView nview) { if (Plugin.Local != null) Plugin.Local.Instance(nview); } }
    [HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
    internal static class RoutePatch
    { [HarmonyPrefix] private static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData __0) { return RequestRouter.Handle(__instance, __0, false); } }
    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    internal static class DispatchPatch
    { [HarmonyPrefix] private static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData __0) { return RequestRouter.Handle(__instance, __0, true); } }
    [HarmonyPatch(typeof(ZRoutedRpc), "InvokeRoutedRPC", new[] { typeof(long), typeof(ZDOID), typeof(string), typeof(object[]) })]
    internal static class RequestPatch
    { [HarmonyPrefix] private static void Prefix(ZDOID __1, string __2) { if (Plugin.Local != null && !__1.IsNone()) Plugin.Local.Outgoing(__1, __2.GetStableHashCode()); } }
    [HarmonyPatch(typeof(Container), "RPC_OpenResponse")]
    internal static class OpenResponsePatch
    { [HarmonyPostfix] private static void Postfix(Container __instance) { if (Plugin.Local != null) Plugin.Local.Response(__instance); } }
    [HarmonyPatch(typeof(Container), "RPC_StackResponse")]
    internal static class StackResponsePatch
    { [HarmonyPostfix] private static void Postfix(Container __instance) { if (Plugin.Local != null) Plugin.Local.Response(__instance); } }
    [HarmonyPatch(typeof(Container), "RPC_TakeAllResponse")]
    internal static class TakeResponsePatch
    { [HarmonyPostfix] private static void Postfix(Container __instance) { if (Plugin.Local != null) Plugin.Local.Response(__instance); } }
    [HarmonyPatch(typeof(Container), "SetInUse")]
    internal static class InventoryUsePatch
    {
        [HarmonyPrefix] private static void Prefix(Container __instance, out bool __state) { __state = __instance.IsInUse(); }
        [HarmonyPostfix] private static void Postfix(Container __instance, bool __state) { if (__state && !__instance.IsInUse() && Plugin.Local != null) Plugin.Local.Response(__instance); }
    }
    [HarmonyPatch(typeof(InventoryGui), "Hide")]
    internal static class InventoryHidePatch
    {
        [HarmonyPrefix] private static void Prefix(InventoryGui __instance, out Container __state) { __state = InventoryAccess.Current(__instance); }
        [HarmonyPostfix] private static void Postfix(Container __state) { if (__state && Plugin.Local != null) Plugin.Local.Response(__state); }
    }
    [HarmonyPatch(typeof(InventoryGui), "CloseContainer")]
    internal static class InventoryClosePatch
    {
        [HarmonyPrefix] private static void Prefix(InventoryGui __instance, out Container __state) { __state = InventoryAccess.Current(__instance); }
        [HarmonyPostfix] private static void Postfix(Container __state) { if (__state && Plugin.Local != null) Plugin.Local.Response(__state); }
    }
    internal static class InventoryAccess
    { internal static readonly Func<InventoryGui, Container> Current = FieldReader.Create<InventoryGui, Container>(AccessTools.Field(typeof(InventoryGui), "m_currentContainer")); }
}
