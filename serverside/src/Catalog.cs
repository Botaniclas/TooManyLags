using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    [Flags]
    internal enum Groups
    {
        None = 0, Doors = 1, Seats = 2, Beds = 4, Workstations = 8, Wards = 16,
        Pickables = 32, Collectors = 64, Feasts = 128, MapTables = 256,
        RefillStations = 512, ArcheryTargets = 1024, LocalMarkers = 2048,
        TamedAnimals = 4096, LooseItems = 8192, Fish = 16384, StaticBuildings = 32768, Containers = 65536, Ships = 131072, Carts = 262144, HostileMobs = 524288
    }
    internal sealed class Catalog
    {
        private readonly Groups enabled;
        private readonly Func<int, bool> reserved;
        private readonly Dictionary<int, Groups> cache = new Dictionary<int, Groups>();
        internal Catalog(Groups enabled, Func<int, bool> reserved = null) { this.enabled = enabled; this.reserved = reserved; }
        internal Groups Get(int hash)
        {
            Groups group;
            if (cache.TryGetValue(hash, out group)) return group;
            GameObject root = ZNetScene.instance ? ZNetScene.instance.GetPrefab(hash) : null;
            if (!root) return Groups.None;
            group = reserved != null && reserved(hash) ? Groups.None : Classify(root);
            if ((group & (Groups.TamedAnimals | Groups.HostileMobs)) != 0) group &= enabled;
            else if ((group & ~enabled) != 0) group = Groups.None;
            cache.Add(hash, group); return group;
        }
        private static Groups Classify(GameObject root)
        {
            ZNetView view = root.GetComponent<ZNetView>();
            if (!view || !view.m_persistent || view.m_distant || root.GetComponent<BossStone>()) return Groups.None;
            Character actor = root.GetComponent<Character>();
            if (actor)
            {
                if (actor is Player || !root.GetComponent<MonsterAI>() || actor.GetFaction() == Character.Faction.Players ||
                    !MobileControls(root, view, Groups.TamedAnimals)) return Groups.None;
                Groups creature = root.GetComponent<Tameable>() ? Groups.TamedAnimals : Groups.None;
                if (actor is Humanoid && root.GetComponent<MonsterAI>().GetType() == typeof(MonsterAI)) creature |= Groups.HostileMobs;
                return creature;
            }
            if (root.GetComponent<ItemDrop>()) return MobileControls(root, view, Groups.LooseItems) ? Groups.LooseItems : Groups.None;
            if (root.GetComponent<Fish>()) return MobileControls(root, view, Groups.Fish) ? Groups.Fish : Groups.None;
            if (root.GetComponent<Ship>()) return NativeShip(root, view) ? Groups.Ships : Groups.None;
            if (root.GetComponent<Vagon>()) return NativeCart(root, view) ? Groups.Carts : Groups.None;
            if (root.GetComponentInChildren<ZSyncTransform>(true) || root.GetComponent<Incinerator>() || root.GetComponent<TombStone>()) return Groups.None;
            Container[] containers = root.GetComponentsInChildren<Container>(true);
            if (containers.Length > 1 || (containers.Length == 1 && (containers[0].GetType() != typeof(Container) ||
                containers[0].m_privacy != Container.PrivacySetting.Public || containers[0].GetComponentInParent<ZNetView>(true) != view ||
                (containers[0].m_rootObjectOverride && containers[0].m_rootObjectOverride != view)))) return Groups.None;
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true)) if (!body.isKinematic || body.useGravity) return Groups.None;
            if (root.GetComponent<Rigidbody>()) return Groups.None;
            Groups host = root.GetComponent<Smelter>() || root.GetComponent<CookingStation>() || root.GetComponent<ShieldGenerator>() ? Groups.RefillStations :
                root.GetComponent<MapTable>() ? Groups.MapTables : Groups.None;
            Groups result = host;
            foreach (MonoBehaviour control in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!control || !(control is Interactable)) continue;
                // Child interactables with a different network identity are independent objects.
                if (control.GetComponentInParent<ZNetView>(true) != view) continue;
                Type type = control.GetType();
                if (type == typeof(Door)) result |= Groups.Doors;
                else if (type == typeof(Container)) result |= Groups.Containers;
                else if (type == typeof(Chair) || type == typeof(Barber)) result |= Groups.Seats;
                else if (type == typeof(Bed)) result |= Groups.Beds;
                else if (type == typeof(CraftingStation)) result |= Groups.Workstations;
                else if (type == typeof(PrivateArea)) result |= Groups.Wards;
                else if (type == typeof(Pickable) || type == typeof(PickableItem)) result |= Groups.Pickables;
                else if (type == typeof(Beehive) || type == typeof(SapCollector)) result |= Groups.Collectors;
                else if (type == typeof(Feast)) result |= Groups.Feasts;
                else if (type == typeof(Fireplace) || type == typeof(CookingStation) || type == typeof(ShieldGenerator) || type == typeof(Turret)) result |= Groups.RefillStations;
                else if (type == typeof(ArcheryTarget)) result |= Groups.ArcheryTargets;
                else if (type == typeof(Teleport) || type == typeof(Vegvisir) || type == typeof(RuneStone) || type == typeof(WayStone) || type == typeof(Ladder)) result |= Groups.LocalMarkers;
                else if (type == typeof(Switch) && host != Groups.None) { }
                else return Groups.None;
            }
            if ((result & Groups.Containers) != 0 && result != Groups.Containers) return Groups.None;
            WearNTear wear = root.GetComponent<WearNTear>();
            if (result == Groups.None && root.GetComponent<Piece>() && wear && wear.m_staticPosition) return Groups.StaticBuildings;
            return result;
        }
        internal string DescribePrefab(string name)
        {
            int hash = name.GetStableHashCode();
            GameObject root = ZNetScene.instance.GetPrefab(hash);
            if (!root) return "GRID prefab " + name + ": missing";
            ZNetView view = root.GetComponent<ZNetView>();
            Groups group = Classify(root);
            var controls = new List<string>();
            foreach (MonoBehaviour part in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!part || !(part is Interactable)) continue;
                controls.Add(part.GetType().Name);
                if (controls.Count == 8) break;
            }
            return "GRID prefab " + name + ": native=" + group + "; enabled=" + (group != Groups.None && (group & ~enabled) == 0) +
                "; reserved=" + (reserved != null && reserved(hash)) + "; view=" + (bool)view +
                "; persistent=" + (view && view.m_persistent) + "; distant=" + (view && view.m_distant) +
                "; rootBody=" + (bool)root.GetComponent<Rigidbody>() + "; bodies=" + root.GetComponentsInChildren<Rigidbody>(true).Length +
                "; moving=" + (bool)root.GetComponentInChildren<ZSyncTransform>(true) + "; controls=" + String.Join(",", controls.ToArray());
        }
        private static bool MobileControls(GameObject root, ZNetView view, Groups group)
        {
            foreach (MonoBehaviour control in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!control || !(control is Interactable) || control.GetComponentInParent<ZNetView>(true) != view) continue;
                Type type = control.GetType();
                if (group == Groups.TamedAnimals ? type != typeof(Tameable) && type != typeof(Sadle) : group == Groups.LooseItems ? type != typeof(ItemDrop) && type != typeof(Fish) : type != typeof(Fish)) return false;
            }
            return true;
        }
        private static bool NativeShip(GameObject root, ZNetView view)
        {
            Ship ship = root.GetComponent<Ship>();
            if (ship.GetType() != typeof(Ship) || !ship.m_shipControlls ||
                ship.m_shipControlls.GetType() != typeof(ShipControlls) || ship.m_shipControlls.m_ship != ship ||
                !root.GetComponent<Rigidbody>() || !root.GetComponent<ZSyncTransform>()) return false;
            Container[] containers = root.GetComponentsInChildren<Container>(true);
            if (containers.Length > 1) return false;
            foreach (Container container in containers)
                if (container.GetType() != typeof(Container) || container.m_privacy != Container.PrivacySetting.Public ||
                    (container.m_rootObjectOverride ? container.m_rootObjectOverride : container.GetComponent<ZNetView>()) != view) return false;
            foreach (MonoBehaviour control in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!control || !(control is Interactable) || control.GetComponentInParent<ZNetView>(true) != view) continue;
                Type type = control.GetType();
                if (type != typeof(ShipControlls) && type != typeof(Container) && type != typeof(Chair) && type != typeof(Ladder)) return false;
            }
            return true;
        }
        private static bool NativeCart(GameObject root, ZNetView view)
        {
            Vagon cart = root.GetComponent<Vagon>();
            Container[] containers = root.GetComponentsInChildren<Container>(true);
            if (cart.GetType() != typeof(Vagon) || !root.GetComponent<Rigidbody>() || !root.GetComponent<ZSyncTransform>() ||
                containers.Length != 1 || cart.m_container != containers[0] || containers[0].m_wagon != cart || containers[0].GetType() != typeof(Container) ||
                containers[0].m_privacy != Container.PrivacySetting.Public ||
                (containers[0].m_rootObjectOverride ? containers[0].m_rootObjectOverride : containers[0].GetComponent<ZNetView>()) != view) return false;
            foreach (MonoBehaviour control in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!control || !(control is Interactable) || control.GetComponentInParent<ZNetView>(true) != view) continue;
                Type type = control.GetType();
                if (type != typeof(Vagon) && type != typeof(Container) && type != typeof(Chair)) return false;
            }
            return true;
        }
        internal static bool Idle(ZDO data)
        { return data.GetLong(ZDOVars.s_user, 0) == 0 && data.GetInt(ZDOVars.s_inUse) == 0 && String.IsNullOrEmpty(data.GetString(ZDOVars.s_attachJointHash, "")); }
        internal static bool Mobile(Groups group) { return (group & (Groups.TamedAnimals | Groups.HostileMobs | Groups.LooseItems | Groups.Fish | Groups.Ships | Groups.Carts)) != 0; }
        internal static bool Pickup(Groups group) { return (group & (Groups.LooseItems | Groups.Fish)) != 0; }
        internal static bool LocalOnly(Groups group)
        { return group != Groups.None && (group & ~(Groups.Seats | Groups.Workstations | Groups.LocalMarkers)) == 0; }
        internal static bool NeedsGrace(Groups group) { return !LocalOnly(group) && (group & ~(Groups.Pickables | Groups.Containers | Groups.Ships | Groups.Carts)) != 0; }
        internal static bool Terminal(ZNetView view)
        {
            Pickable pick = view.GetComponent<Pickable>();
            if (pick && pick.GetPicked() && pick.m_respawnTimeMinutes <= 0 && !pick.m_hideWhenPicked) return true;
            PickableItem item = view.GetComponent<PickableItem>();
            return item && (bool)pickedItem.GetValue(item);
        }
        private static readonly System.Reflection.FieldInfo pickedItem = HarmonyLib.AccessTools.Field(typeof(PickableItem), "m_picked");
        internal static bool FinishedPick(ZNetView view)
        { Pickable pick = view.GetComponent<Pickable>(); return pick ? pick.GetPicked() : Terminal(view); }
        internal static bool PickRequest(int method)
        { return requests[Groups.Pickables].Contains(method); }
        private static readonly HashSet<int> common = Hashes("RPC_Damage", "RPC_Repair", "RPC_Remove", "RPC_ClearCachedSupport", "RPC_SetSnow");
        private static readonly int use = "UseDoor".GetStableHashCode(), bed = "SetOwner".GetStableHashCode(),
            toggle = "ToggleEnabled".GetStableHashCode(), permission = "TogglePermitted".GetStableHashCode();
        private static readonly Dictionary<Groups, HashSet<int>> requests = new Dictionary<Groups, HashSet<int>>
        {
            { Groups.Pickables, Hashes("RPC_Pick", "Pick") },
            { Groups.Collectors, Hashes("RPC_Extract") },
            { Groups.Feasts, Hashes("RPC_TryEat") },
            { Groups.MapTables, Hashes("MapData") },
            { Groups.RefillStations, Hashes("RPC_AddOre", "RPC_AddFuel", "RPC_EmptyProcessed", "RPC_AddItem", "RPC_RemoveDoneItem", "RPC_AddFuelAmount", "RPC_SetFuelAmount", "RPC_SetFuel", "RPC_Attack", "RPC_ToggleOn", "RPC_AddAmmo", "RPC_SetTarget") },
            { Groups.ArcheryTargets, Hashes("RPC_DropArrows") },
            { Groups.TamedAnimals, Hashes("Command", "SetName", "AddSaddle", "RequestControl", "ReleaseControl", "Controls", "RemoveSaddle") },
            { Groups.HostileMobs, Hashes("Alert", "OnNearProjectileHit", "SetAggravated", "RPC_Heal", "RPC_AddNoise", "RPC_AddAdrenaline", "RPC_SetTamed", "RPC_Stagger") },
            { Groups.LooseItems, Hashes("RPC_RequestOwn") },
            { Groups.Fish, Hashes("RequestPickup") },
            { Groups.Containers, Hashes("RPC_RequestOpen") },
            { Groups.Ships, Hashes("RPC_RequestOpen", "RequestControl", "ReleaseControl", "Forward", "Backward", "Stop", "Rudder") },
            { Groups.Carts, Hashes("RPC_RequestOpen", "RPC_RequestOwn") }
        };
        private static readonly HashSet<int> possible = AllRequests();
        internal static bool OwnerRequest(Groups group, int method)
        {
            if (common.Contains(method) || ((group & Groups.Doors) != 0 && method == use) ||
                ((group & Groups.Beds) != 0 && method == bed) || ((group & Groups.Wards) != 0 && (method == toggle || method == permission))) return true;
            foreach (var pair in requests) if ((group & pair.Key) != 0 && pair.Value.Contains(method)) return true;
            return false;
        }
        internal static bool PossibleRequest(int method)
        { return possible.Contains(method); }
        private static HashSet<int> AllRequests()
        {
            var result = new HashSet<int>(common); result.UnionWith(new[] { use, bed, toggle, permission });
            foreach (var pair in requests) result.UnionWith(pair.Value); return result;
        }
        private static HashSet<int> Hashes(params string[] names)
        { var result = new HashSet<int>(); foreach (string name in names) result.Add(name.GetStableHashCode()); return result; }
    }
}
