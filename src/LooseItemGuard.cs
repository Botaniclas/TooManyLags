using System;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Consult only on an actual automatic handoff. Native pickup, merging and
    // item physics stay native; no polling, leases or world/player scans.
    internal static class LooseItemGuard
    {
        internal static bool Busy(ItemDrop item)
        {
            if (!item) return false;
            if (item.IsInvoking("PickupUpdate") || item.IsInvoking("EatUpdate")) return true;
            Player player = Player.m_localPlayer;
            if (!player || player.IsDead() || player.IsTeleporting() || !item.m_autoPickup || item.IsPiece() || !Native.Enabled()) return false;
            float range = Native.Range(player);
            if (Single.IsNaN(range) || Single.IsInfinity(range) || range < 0f) return true;
            Vector3 origin = player.transform.position; origin.y += 1f;
            Vector3 offset = item.transform.position - origin;
            return offset.sqrMagnitude <= range * range;
        }
        internal static bool Busy(ZNetView view)
        { return view && Busy(view.GetComponent<ItemDrop>()); }
        private static class Native
        {
            internal static readonly Func<Player, float> Range = FieldReader.Create<Player, float>(AccessTools.Field(typeof(Player), "m_autoPickupRange"));
            internal static readonly Func<bool> Enabled = EnableReader();
            private static Func<bool> EnableReader()
            {
                var field = AccessTools.Field(typeof(Player), "m_enableAutoPickup");
                if (field == null || !field.IsStatic || field.FieldType != typeof(bool)) throw new NotSupportedException("Native automatic item pickup setting changed.");
                DynamicMethod method = new DynamicMethod("TML_AutoPickupEnabled", typeof(bool), Type.EmptyTypes, typeof(LooseItemGuard).Module, true);
                ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ret);
                return (Func<bool>)method.CreateDelegate(typeof(Func<bool>));
            }
        }
    }
}
