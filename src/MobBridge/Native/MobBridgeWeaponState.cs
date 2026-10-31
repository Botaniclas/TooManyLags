#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NativeMobPreviewAudit
{
    // Selection state belongs to this preview. Never equip a live item or
    // update its cooldown while running copied native AI.
    internal sealed class MobBridgeWeaponState
    {
        internal readonly List<ItemDrop.ItemData> Items = new List<ItemDrop.ItemData>();
        internal readonly List<ItemDrop.ItemData> Optimal = new List<ItemDrop.ItemData>();
        internal readonly List<ItemDrop.ItemData> OutOfRange = new List<ItemDrop.ItemData>();
        internal readonly List<ItemDrop.ItemData> All = new List<ItemDrop.ItemData>();
        private readonly Dictionary<ItemDrop.ItemData, float> cooldowns = new Dictionary<ItemDrop.ItemData, float>();
        internal readonly ItemDrop.ItemData Fallback;
        internal ItemDrop.ItemData Current;

        internal MobBridgeWeaponState(Humanoid actor)
        {
            foreach (ItemDrop.ItemData item in actor.GetInventory().GetAllItems())
                if (item.IsWeapon()) { Items.Add(item); cooldowns[item] = item.m_lastAttackTime; }
            Fallback = actor.m_unarmedWeapon ? actor.m_unarmedWeapon.m_itemData : null;
            if (Fallback != null) cooldowns[Fallback] = Fallback.m_lastAttackTime;
            Current = actor.GetCurrentWeapon();
            if (Current != null && !cooldowns.ContainsKey(Current))
                throw new NotSupportedException("Equipped mob weapon is absent from its inventory/fallback.");
        }

        internal float LastAttack(ItemDrop.ItemData item)
        {
            float result;
            if (!cooldowns.TryGetValue(item, out result)) throw new NotSupportedException("Uncaptured mob weapon cooldown.");
            return result;
        }

        internal void Attacked(float time) { if (Current != null) cooldowns[Current] = time; }

        private static string Key(ItemDrop.ItemData item)
        {
            return (item.m_dropPrefab ? item.m_dropPrefab.name : "") + "|" + item.m_shared.m_name + "|" +
                ((int)item.m_shared.m_itemType).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                item.m_quality.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                item.m_variant.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static float Age(ItemDrop.ItemData item, float now)
        {
            float age = now - item.m_lastAttackTime;
            if (Single.IsNaN(age) || Single.IsInfinity(age)) throw new NotSupportedException("Invalid mob weapon cooldown.");
            return age;
        }

        internal static void Write(ZPackage packet, Humanoid actor, float now)
        {
            MobBridgeWeaponState state = new MobBridgeWeaponState(actor);
            if (state.Items.Count > 256) throw new NotSupportedException("Mob weapon checkpoint exceeds capacity.");
            packet.Write((short)state.Items.Count);
            foreach (ItemDrop.ItemData item in state.Items) { packet.Write(Key(item)); packet.Write(Age(item, now)); }
            packet.Write(state.Fallback != null);
            if (state.Fallback != null) { packet.Write(Key(state.Fallback)); packet.Write(Age(state.Fallback, now)); }
            int selected = state.Current == null ? -1 : ReferenceEquals(state.Current, state.Fallback) ? -2 : state.Items.IndexOf(state.Current);
            packet.Write(selected);
        }

        internal static void Read(ZPackage packet, Humanoid actor, float now)
        {
            MobBridgeWeaponState state = new MobBridgeWeaponState(actor);
            int count = packet.ReadShort();
            if (count < 0 || count > 256 || count != state.Items.Count)
                throw new NotSupportedException("Destination mob weapon inventory differs from checkpoint.");
            ItemDrop.ItemData[] ordered = new ItemDrop.ItemData[count];
            float[] ages = new float[count];
            HashSet<ItemDrop.ItemData> used = new HashSet<ItemDrop.ItemData>();
            for (int i = 0; i < count; ++i)
            {
                string key = packet.ReadString(); ages[i] = ReadAge(packet);
                foreach (ItemDrop.ItemData candidate in state.Items)
                    if (!used.Contains(candidate) && Key(candidate) == key) { ordered[i] = candidate; used.Add(candidate); break; }
                if (ordered[i] == null) throw new NotSupportedException("Destination mob weapon identity differs from checkpoint.");
            }
            bool fallback = packet.ReadBool(); float fallbackAge = 0f;
            if (fallback)
            {
                string key = packet.ReadString(); fallbackAge = ReadAge(packet);
                if (state.Fallback == null || Key(state.Fallback) != key)
                    throw new NotSupportedException("Destination unarmed weapon differs from checkpoint.");
            }
            else if (state.Fallback != null) throw new NotSupportedException("Unexpected destination unarmed weapon.");
            int selected = packet.ReadInt();
            if (selected < -2 || selected >= count || (selected == -2 && !fallback) || (selected == -1 && fallback))
                throw new InvalidOperationException("Invalid equipped mob weapon index.");
            // Match native enumeration order as well as cooldowns: Random.Range
            // chooses an index, so identical sets in different orders diverge.
            List<ItemDrop.ItemData> inventory = actor.GetInventory().GetAllItems();
            int next = 0;
            for (int i = 0; i < inventory.Count; ++i) if (inventory[i].IsWeapon()) inventory[i] = ordered[next++];
            for (int i = 0; i < count; ++i) ordered[i].m_lastAttackTime = now - ages[i];
            if (fallback) state.Fallback.m_lastAttackTime = now - fallbackAge;
            ItemDrop.ItemData desired = selected == -2 ? state.Fallback : selected == -1 ? null : ordered[selected];
            if (!ReferenceEquals(actor.GetCurrentWeapon(), desired))
            {
                if (desired == null || ReferenceEquals(desired, state.Fallback)) actor.UnequipItem(actor.GetCurrentWeapon(), false);
                else if (!actor.EquipItem(desired, false)) throw new NotSupportedException("Destination mob weapon could not be restored.");
            }
        }

        private static float ReadAge(ZPackage packet)
        {
            float age = packet.ReadSingle();
            if (Single.IsNaN(age) || Single.IsInfinity(age)) throw new InvalidOperationException("Invalid mob weapon cooldown age.");
            return age;
        }
    }
}
#endif
