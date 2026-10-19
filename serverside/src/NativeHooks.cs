using System;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
    internal static class CharacterArrived
    { [HarmonyPostfix] private static void Postfix() { Plugin.ScheduleStatus(); } }
    [HarmonyPatch(typeof(ZNetScene), "AddInstance")]
    internal static class Added
    { [HarmonyPostfix] private static void Postfix(ZDO __0, ZNetView __1) { try { if (Plugin.Current != null || Plugin.EnsureWorld()) Plugin.Current.Add(__0, __1); } catch (Exception error) { Plugin.Fault(error); } } }
    [HarmonyPatch(typeof(ZNetView), "ResetZDO")]
    internal static class Unloaded
    { [HarmonyPrefix] private static void Prefix(ZNetView __instance) { try { if (Plugin.Current != null) Plugin.Current.Remove(__instance.GetZDO()); } catch (Exception error) { Plugin.Fault(error); } } }
    [HarmonyPatch(typeof(ZNetView), "OnDestroy")]
    internal static class Destroyed
    { [HarmonyPrefix] private static void Prefix(ZNetView __instance) { try { if (Plugin.Current != null) Plugin.Current.Remove(__instance.GetZDO()); } catch (Exception error) { Plugin.Fault(error); } } }
    [HarmonyPatch(typeof(ZNet), "Shutdown")]
    internal static class Shutdown
    { [HarmonyPrefix] private static void Prefix() { Plugin.Reset(); } }
    [HarmonyPatch(typeof(ZDO), "SetOwnerInternal")]
    internal static class OwnershipChanged
    { [HarmonyPostfix] private static void Postfix(ZDO __instance, long __0) { try { if (Plugin.Current != null) Plugin.Current.OwnerAssigned(__instance, __0); } catch (Exception error) { Plugin.Fault(error); } } }
    [HarmonyPatch(typeof(ZDO), "InternalSetPosition")]
    internal static class ObjectMoved
    {
        [HarmonyPrefix] private static void Prefix(ZDO __instance, out Vector3? __state)
        { __state = Plugin.Current != null && Plugin.Current.HasWaitingForecast ? (Vector3?)__instance.GetPosition() : null; }
        [HarmonyPostfix] private static void Postfix(ZDO __instance, Vector3? __state)
        {
            try
            {
                if (Plugin.Current == null) return;
                Plugin.Current.PositionChanged(__instance);
                if (__state.HasValue && !__state.Value.Equals(__instance.GetPosition())) Plugin.Current.ForecastTargetChanged(__instance, __state.Value);
            }
            catch (Exception error) { Plugin.Fault(error); }
        }
    }
    [HarmonyPatch(typeof(ZDO), "Set", new[] { typeof(int), typeof(bool) })]
    internal static class AnimalFlagChanged
    { [HarmonyPostfix] private static void Postfix(ZDO __instance, int __0) { if (__0 != ZDOVars.s_tamed && __0 != ZDOVars.s_alert && __0 != ZDOVars.s_haveTargetHash && __0 != ZDOVars.s_piece && __0 != ZDOVars.s_attachJointHash) return; try { if (Plugin.Current != null) Plugin.Current.StateChanged(__instance); } catch (Exception error) { Plugin.Fault(error); } } }
    [HarmonyPatch(typeof(ZDO), "Set", new[] { typeof(int), typeof(long) })]
    internal static class AnimalUserChanged
    { [HarmonyPostfix] private static void Postfix(ZDO __instance, int __0) { if (__0 != ZDOVars.s_user) return; try { if (Plugin.Current != null) Plugin.Current.StateChanged(__instance); } catch (Exception error) { Plugin.Fault(error); } } }
    [HarmonyPatch(typeof(ZDO), "Set", new[] { typeof(int), typeof(string) })]
    internal static class AnimalFollowChanged
    { [HarmonyPostfix] private static void Postfix(ZDO __instance, int __0) { if (__0 != ZDOVars.s_follow && __0 != ZDOVars.s_attachJointHash) return; try { if (Plugin.Current != null) Plugin.Current.StateChanged(__instance); } catch (Exception error) { Plugin.Fault(error); } } }
    [HarmonyPatch(typeof(Fish), "OnHooked")]
    internal static class FishHooked
    { [HarmonyPrefix] private static void Prefix(Fish __instance) { try { ZNetView view = __instance.GetComponent<ZNetView>(); if (Plugin.Current != null && view && view.IsValid()) Plugin.Current.PinPickup(view.GetZDO()); } catch (Exception error) { Plugin.Fault(error); } } }
    [HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
    internal static class Routed
    { [HarmonyPrefix] private static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData __0) { try { return RequestRouter.Handle(__instance, __0); } catch (Exception error) { Plugin.Fault(error); return false; } } }
    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    internal static class Dispatched
    { [HarmonyPrefix] private static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData __0) { try { return RequestRouter.Handle(__instance, __0); } catch (Exception error) { Plugin.Fault(error); return false; } } }

    internal static class RequestRouter
    {
        private static readonly Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData> local = AccessTools.MethodDelegate<Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>>(AccessTools.Method(typeof(ZRoutedRpc), "HandleRoutedRPC"));
        private static readonly Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData> remote = AccessTools.MethodDelegate<Action<ZRoutedRpc, ZRoutedRpc.RoutedRPCData>>(AccessTools.Method(typeof(ZRoutedRpc), "RouteRPC"));
        private static bool forwarding;
        internal static bool Handle(ZRoutedRpc rpc, ZRoutedRpc.RoutedRPCData packet)
        {
            Controller controller = Plugin.Current;
            if (forwarding || controller == null || packet == null || packet.m_targetPeerID == 0 || packet.m_targetZDO.IsNone()) return true;
            if (packet.m_methodHash == cartDenied)
            {
                controller.CartDenied(ZDOMan.instance.GetZDO(packet.m_targetZDO), packet.m_targetPeerID);
                return true;
            }
            if (packet.m_methodHash == controlReply)
            {
                ZDO control = ZDOMan.instance.GetZDO(packet.m_targetZDO);
                if (controller.ExpectsControlReply(control, packet.m_targetPeerID))
                    controller.ControlReply(control, new ZPackage(packet.m_parameters.GetArray()).ReadBool(), packet.m_targetPeerID);
                return true;
            }
            if (packet.m_methodHash == inventoryReply && packet.m_targetPeerID == ZDOMan.GetSessionID())
            {
                ZDO chest = ZDOMan.instance.GetZDO(packet.m_targetZDO);
                if (controller.ExpectsInventoryReply(chest))
                    controller.InventoryReply(chest, packet.m_senderPeerID, new ZPackage(packet.m_parameters.GetArray()).ReadBool());
                return true;
            }
            if (!Catalog.PossibleRequest(packet.m_methodHash)) return true;
            ZDO data = ZDOMan.instance.GetZDO(packet.m_targetZDO);
            if (!controller.Request(data, packet.m_methodHash)) return true;
            if (packet.m_methodHash == pickupItem || packet.m_methodHash == pickupFish) controller.PinPickup(data, packet.m_senderPeerID);
            if (packet.m_methodHash == controlRequest) controller.PinControl(data, packet.m_senderPeerID);
            controller.SourceRequest(data, packet.m_senderPeerID);
            long owner = data.GetOwner();
            if (owner == packet.m_targetPeerID || !controller.Available(owner, data.GetPosition())) return true;
            if (controller.FinishedRequest(data, packet.m_methodHash)) return false;
            // Submit just this native object before the original command on the same connection.
            if (owner != ZDOMan.GetSessionID() && !NativeSnapshot.Send(data, owner)) return false;
            var copy = new ZRoutedRpc.RoutedRPCData { m_msgID = packet.m_msgID, m_senderPeerID = packet.m_senderPeerID,
                m_targetPeerID = owner, m_targetZDO = packet.m_targetZDO, m_methodHash = packet.m_methodHash, m_parameters = new ZPackage(packet.m_parameters.GetArray()) };
            Dispatch(rpc, copy, owner == ZDOMan.GetSessionID());
            return false;
        }
        private static readonly int pickupItem = "RPC_RequestOwn".GetStableHashCode(), pickupFish = "RequestPickup".GetStableHashCode(), controlRequest = "RequestControl".GetStableHashCode(), controlReply = "RequestRespons".GetStableHashCode();
        private static readonly int inventoryReply = "RPC_OpenResponse".GetStableHashCode(), cartDenied = "RPC_RequestDenied".GetStableHashCode();
        internal static void Dispatch(ZRoutedRpc rpc, ZRoutedRpc.RoutedRPCData packet, bool onServer)
        {
            bool previous = forwarding; forwarding = true;
            try { if (onServer) local(rpc, packet); else remote(rpc, packet); }
            finally { forwarding = previous; }
        }
    }
}
