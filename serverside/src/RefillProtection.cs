using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed partial class Controller
    {
        internal bool ProtectRefills { get { return options.RefillHold > 0f; } }
        private sealed class RefillState
        {
            private readonly ZDO data;
            private readonly bool fuel, queue, ammo;
            private readonly int[] slots;
            private readonly bool[] filled;
            private float lastFuel;
            private int lastQueue, lastAmmo;
            internal float Until;
            internal RefillState(ZDO data, ZNetView view)
            {
                this.data = data;
                int slotCount = 0;
                foreach (MonoBehaviour part in view.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!part || part.GetComponentInParent<ZNetView>(true) != view) continue;
                    if (part is Smelter) { fuel = true; queue = true; }
                    if (part is Fireplace || part is ShieldGenerator) fuel = true;
                    if (part is Turret) ammo = true;
                    CookingStation cooking = part as CookingStation;
                    if (cooking) { fuel = true; if (cooking.m_slots != null) slotCount = Math.Max(slotCount, cooking.m_slots.Length); }
                }
                if (slotCount != 0)
                {
                    slots = new int[slotCount]; filled = new bool[slotCount];
                    for (int i = 0; i < slotCount; i++) slots[i] = ("slot" + i).GetStableHashCode();
                }
                Read();
            }
            internal bool Read()
            {
                bool added = false;
                if (fuel) { float value = data.GetFloat(ZDOVars.s_fuel, 0f); added |= value > lastFuel; lastFuel = value; }
                if (queue) { int value = data.GetInt(ZDOVars.s_queued); added |= value > lastQueue; lastQueue = value; }
                if (ammo) { int value = data.GetInt(ZDOVars.s_ammo); added |= value > lastAmmo; lastAmmo = value; }
                if (slots != null) for (int i = 0; i < slots.Length; i++)
                { bool occupied = !String.IsNullOrEmpty(data.GetString(slots[i], "")); added |= occupied && !filled[i]; filled[i] = occupied; }
                return added;
            }
        }
        internal ZDO PrepareRefill(ZNetView view)
        {
            Node node; ZDO data = view && view.IsValid() ? view.GetZDO() : null;
            if (data == null || !nodes.TryGetValue(data, out node) || node.Refill == null || data.GetOwner() != server) return null;
            node.Refill.Read(); return data;
        }
        internal void RefillChanged(ZDO data)
        {
            Node node;
            if (options.RefillHold <= 0f || data == null || !nodes.TryGetValue(data, out node) || node.Refill == null || !data.IsValid() || !node.Refill.Read()) return;
            node.Refill.Until = Time.unscaledTime + options.RefillHold;
            grace.Remove(node);
            if (node.InGrid) { world.Dismiss(node); Wait(node); }
        }
    }
    [HarmonyPatch]
    internal static class NativeRefill
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> Targets()
        {
            foreach (Type type in new[] { typeof(Smelter), typeof(CookingStation), typeof(Fireplace), typeof(ShieldGenerator), typeof(Turret) })
            foreach (string name in type == typeof(Smelter) ? new[] { "RPC_AddOre", "RPC_AddFuel" } :
                type == typeof(CookingStation) ? new[] { "RPC_AddItem", "RPC_AddFuel" } :
                type == typeof(ShieldGenerator) ? new[] { "RPC_AddFuel" } :
                type == typeof(Turret) ? new[] { "RPC_AddAmmo" } : new[] { "RPC_AddFuel" })
            {
                MethodInfo method = AccessTools.DeclaredMethod(type, name);
                if (method == null) throw new NotSupportedException("Native refill method changed: " + type.Name + "." + name);
                yield return method;
            }
        }
        [HarmonyPrefix]
        private static void Prefix(MonoBehaviour __instance, out ZDO __state)
        {
            __state = null;
            try
            {
                if (Plugin.Current == null || !Plugin.Current.ProtectRefills) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (!view) view = __instance.GetComponentInParent<ZNetView>(true);
                __state = Plugin.Current.PrepareRefill(view);
            }
            catch (Exception error) { Plugin.Fault(error); }
        }
        [HarmonyPostfix]
        private static void Postfix(ZDO __state)
        { try { if (__state != null && Plugin.Current != null) Plugin.Current.RefillChanged(__state); } catch (Exception error) { Plugin.Fault(error); } }
    }
}
