using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace TooManyLagsStandalone
{
    internal static class NativeContracts
    {
        internal static void Validate()
        {
            ShipTransferGuard.Validate();
            string[] zdo = { "Deserialize", "InternalSetPosition", "SetPrefab", "SetOwner", "SetOwnerInternal" };
            foreach (string method in zdo) Required(typeof(ZDO), method);
            foreach (string method in new[] { "ReleaseZDOS", "RPC_ZDOData", "HandleDestroyedZDO", "FindObjects" }) Required(typeof(ZDOMan), method);
            foreach (string method in new[] { "RouteRPC", "HandleRoutedRPC" }) Required(typeof(ZRoutedRpc), method);
            foreach (string method in new[] { "RPC_OpenResponse", "RPC_StackResponse", "RPC_TakeAllResponse", "SetInUse", "IsInUse" }) Required(typeof(Container), method);
            Required(typeof(ZNet), "OnNewConnection"); Required(typeof(ZNet), "Shutdown"); Required(typeof(ZNetScene), "AddInstance");
            Required(typeof(ZNetView), "ResetZDO"); Required(typeof(ZNetView), "OnDestroy");
            Required(typeof(InventoryGui), "Hide"); Required(typeof(InventoryGui), "CloseContainer");
            Required(typeof(Fermenter), "DelayedTap"); Required(typeof(OfferingBowl), "DelayedSpawnBoss");
            if (AccessTools.Field(typeof(InventoryGui), "m_currentContainer") == null) throw new NotSupportedException("Native inventory GUI ownership guard unavailable.");
            var target = AccessTools.Field(typeof(MonsterAI), "m_targetCreature");
            if (target == null || target.FieldType != typeof(Character)) throw new NotSupportedException("Native monster target contract changed.");
            var attack = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
            var requester = AccessTools.Field(typeof(Vagon), "m_useRequester");
            if (attack == null || attack.IsStatic || attack.FieldType != typeof(Attack) ||
                requester == null || requester.IsStatic || requester.FieldType != typeof(Humanoid))
                throw new NotSupportedException("Native pending attack/cart use contract changed.");
            var find = AccessTools.Method(typeof(ZDOMan), "FindObjects");
            Type[] args = { typeof(Vector2s), typeof(List<ZDO>), typeof(HashSet<ZoneSystem.SectorIndex>) };
            ParameterInfo[] actual = find.GetParameters(); if (actual.Length != args.Length) throw new NotSupportedException("Native sector lookup changed.");
            for (int i = 0; i < args.Length; i++) if (actual[i].ParameterType != args[i]) throw new NotSupportedException("Native sector lookup changed.");
        }
        private static void Required(Type type, string method) { if (AccessTools.Method(type, method) == null) throw new NotSupportedException("Native method unavailable: " + type.Name + "." + method); }
    }
}
