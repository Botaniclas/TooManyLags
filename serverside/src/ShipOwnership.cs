using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed partial class Controller
    {
        private static bool ShipIdle(ZDO data)
        { return data.GetInt(ZDOVars.s_inUse) == 0; }

        private static Vector3 GridPosition(Node node)
        { return node.Ship ? node.Data.GetPosition() + node.Data.GetRotation() * node.ShipAnchor : node.Data.GetPosition(); }

        private bool FindShip(ZDO data, out Node node)
        {
            node = null;
            return shipCount != 0 && data != null && nodes.TryGetValue(data, out node) && node.Group == Groups.Ships &&
                !node.Removed && !node.Released && node.Ship && node.View && node.View.IsValid() && ReferenceEquals(node.View.GetZDO(), data);
        }
        internal bool ManagesShip(ZDO data) { Node node; return FindShip(data, out node); }

        // Reuse the backend's existing two-second boat timer. Only a blocked,
        // genuinely overlapping server decision is retried; settled boats and
        // solo/vacant fields get no inventory query or additional scheduler.
        internal bool ShipTimer(ZDO data)
        {
            Node node; if (!FindShip(data, out node)) return false;
            System.Collections.Generic.HashSet<Node> bucket;
            if (!node.Managed && node.InGrid && world.Grid.Owner(node.Cell) == server && world.Grid.Participants(node.Cell) > 1 &&
                waiting.TryGetValue(node.Cell, out bucket) && bucket.Contains(node)) world.Received(node, Time.unscaledTime);
            return true;
        }

        internal void ShipRotated(ZDO data)
        { if (ManagesShip(data)) PositionChanged(data); Node node; if (mobs.Count != 0 && mobs.TryGetValue(data, out node)) MobPoseChanged(node); }

        private ApplyResult ApplyShip(Node node, long target, long owner)
        {
            // Native helm use and network ownership are independent. Keep the
            // driver, but let shared fields simulate the boat on the server.
            // Cargo use, unlike helm use, vetoes an automatic handoff.
            if (!node.Ship || !ShipIdle(node.Data) || !node.View.gameObject.activeInHierarchy ||
                !Available(target, node.Data.GetPosition()))
            { Wait(node); return ApplyResult.Dropped; }
            if (owner != target && (target == server || (node.Container && owner != server)) && !MobileAreaReady(node))
            { Wait(node); return ApplyResult.Dropped; }
            if (node.Container) return ApplyContainer(node, target, owner);
            if (owner != target)
            {
                if (node.Data.OwnerRevision == UInt16.MaxValue) { Wait(node); return ApplyResult.Dropped; }
                if (target != server) ZDOMan.instance.ForceSendZDO(target, node.Data.m_uid);
                Assign(node, target);
            }
            node.Managed = true; node.ManagedOwner = target; return ApplyResult.Done;
        }
    }

    internal static class ShipOwnership
    {
        private static readonly FieldInfo impact = AccessTools.Field(typeof(Ship), "m_lastWaterImpactTime");
        internal static void Validate()
        {
            if (impact == null || impact.FieldType != typeof(float) || impact.IsStatic)
                throw new NotSupportedException("Native ship water-impact protection changed.");
        }
        // Preserve the backend's water-impact grace when server physics takes over.
        internal static void Prepare(Ship ship) { impact.SetValue(ship, Time.time); }
    }

    [HarmonyPatch(typeof(ZDO), "SetRotation", new[] { typeof(Quaternion) })]
    internal static class ShipRotationChanged
    {
        [HarmonyPrefix] private static void Prefix(ZDO __instance, out Quaternion? __state)
        { __state = Plugin.Current != null && Plugin.Current.WatchesForecastTurn(__instance) ? (Quaternion?)__instance.GetRotation() : null; }
        [HarmonyPostfix] private static void Postfix(ZDO __instance, Quaternion? __state)
        {
            try
            {
                if (Plugin.Current == null) return;
                Plugin.Current.ShipRotated(__instance);
                if (__state.HasValue && !__state.Value.Equals(__instance.GetRotation())) Plugin.Current.ForecastMobTurned(__instance);
            }
            catch (Exception error) { Plugin.Fault(error); }
        }
    }
}
