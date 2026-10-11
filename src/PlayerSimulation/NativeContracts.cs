using System;
using System.Collections.Generic;
using System.Reflection;

namespace TooManyLags.PlayerSimulation
{
    internal static class NativeContracts
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        internal static void Validate()
        {
            string[] zdo = { "Deserialize", "InternalSetPosition", "SetPrefab", "SetOwner", "SetOwnerInternal" };
            foreach (string method in zdo) Required(typeof(ZDO), method);
            foreach (string method in new[] { "ReleaseZDOS", "RPC_ZDOData", "HandleDestroyedZDO", "FindObjects" }) Required(typeof(ZDOMan), method);
            foreach (string method in new[] { "RouteRPC", "HandleRoutedRPC" }) Required(typeof(ZRoutedRpc), method);
            foreach (string method in new[] { "RPC_OpenResponse", "RPC_StackResponse", "RPC_TakeAllResponse", "SetInUse", "IsInUse" }) Required(typeof(Container), method);
            Required(typeof(ZNet), "OnNewConnection"); Required(typeof(ZNet), "Shutdown"); Required(typeof(ZNetScene), "AddInstance");
            Required(typeof(InventoryGui), "Hide"); Required(typeof(InventoryGui), "CloseContainer");
            if (typeof(InventoryGui).GetField("m_currentContainer", Flags) == null) throw new NotSupportedException("Native inventory GUI ownership guard unavailable.");
            var target = typeof(MonsterAI).GetField("m_targetCreature", Flags);
            if (target == null || target.FieldType != typeof(Character)) throw new NotSupportedException("Native monster target contract changed.");
            var find = typeof(ZDOMan).GetMethod("FindObjects", Flags);
            Type[] args = { typeof(Vector2s), typeof(List<ZDO>), typeof(HashSet<ZoneSystem.SectorIndex>) };
            ParameterInfo[] actual = find.GetParameters(); if (actual.Length != args.Length) throw new NotSupportedException("Native sector lookup changed.");
            for (int i = 0; i < args.Length; i++) if (actual[i].ParameterType != args[i]) throw new NotSupportedException("Native sector lookup changed.");
        }
        private static void Required(Type type, string method) { if (type.GetMethod(method, Flags) == null) throw new NotSupportedException("Native method unavailable: " + type.Name + "." + method); }
    }
}
