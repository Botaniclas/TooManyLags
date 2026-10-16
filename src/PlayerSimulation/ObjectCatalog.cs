using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags.PlayerSimulation
{
    [Flags]
    internal enum Kinds
    {
        None = 0, Doors = 1, Seats = 2, Beds = 4, Workstations = 8, Smelters = 16,
        CookingStations = 32, Fireplaces = 64, Beehives = 128, SapCollectors = 256,
        Wards = 512, MapTables = 1024, Wisplights = 2048, TamedAnimals = 4096,
        Containers = 8192, ItemStands = 16384, Signs = 32768, Portals = 65536,
        Fermenters = 131072, Incinerators = 262144, Defenses = 524288,
        ShieldGenerators = 1048576, Pickables = 2097152, Offerings = 4194304,
        Vehicles = 8388608, OtherInteractables = 16777216, LooseItems = 134217728
    }

    internal sealed class ObjectCatalog
    {
        private readonly Dictionary<int, Kinds> prefabs = new Dictionary<int, Kinds>();
        private readonly Kinds enabled;
        private readonly HashSet<int> looseDrops = new HashSet<int>();
        private readonly HashSet<int> nativeSafe = new HashSet<int>();
        internal bool IsLooseDrop(int hash) { return looseDrops.Contains(hash); }
        internal bool NativeTransferSafe(int hash) { Prefab(hash); return nativeSafe.Contains(hash); }
        private static readonly Dictionary<Type, Kinds> groups = new Dictionary<Type, Kinds>
        {
            { typeof(Door), Kinds.Doors }, { typeof(Chair), Kinds.Seats }, { typeof(Barber), Kinds.Seats },
            { typeof(Bed), Kinds.Beds }, { typeof(CraftingStation), Kinds.Workstations },
            { typeof(StationExtension), Kinds.Workstations }, { typeof(Smelter), Kinds.Smelters },
            { typeof(CookingStation), Kinds.CookingStations }, { typeof(Fireplace), Kinds.Fireplaces },
            { typeof(Beehive), Kinds.Beehives }, { typeof(SapCollector), Kinds.SapCollectors },
            { typeof(PrivateArea), Kinds.Wards }, { typeof(MapTable), Kinds.MapTables },
            { typeof(WispSpawner), Kinds.Wisplights }, { typeof(Container), Kinds.Containers },
            { typeof(ItemStand), Kinds.ItemStands }, { typeof(ArmorStand), Kinds.ItemStands },
            { typeof(Sign), Kinds.Signs }, { typeof(TeleportWorld), Kinds.Portals }, { typeof(Teleport), Kinds.Portals },
            { typeof(Fermenter), Kinds.Fermenters }, { typeof(Incinerator), Kinds.Incinerators },
            { typeof(Trap), Kinds.Defenses }, { typeof(Turret), Kinds.Defenses },
            { typeof(ShieldGenerator), Kinds.ShieldGenerators }, { typeof(Pickable), Kinds.Pickables },
            { typeof(PickableItem), Kinds.Pickables }, { typeof(Feast), Kinds.Pickables },
            { typeof(OfferingBowl), Kinds.Offerings }, { typeof(Vagon), Kinds.Vehicles }, { typeof(ShipControlls), Kinds.Vehicles }
        };
        private static readonly Dictionary<Type, Kinds> types = new Dictionary<Type, Kinds>();
        // One ZDO can contain several controls. Prefer its specific function over
        // auxiliary switches/containers (e.g. the incinerator's own inventory).
        private static readonly Kinds[] priority =
        {
            Kinds.Incinerators, Kinds.Vehicles, Kinds.Fermenters, Kinds.ItemStands, Kinds.Containers,
            Kinds.Defenses, Kinds.ShieldGenerators, Kinds.Smelters, Kinds.CookingStations, Kinds.Fireplaces,
            Kinds.Doors, Kinds.Seats, Kinds.Beds, Kinds.Workstations, Kinds.Beehives, Kinds.SapCollectors,
            Kinds.Wards, Kinds.MapTables, Kinds.Wisplights, Kinds.Signs, Kinds.Portals, Kinds.Pickables, Kinds.Offerings
        };
        internal ObjectCatalog(Kinds enabled) { this.enabled = enabled; }
        // Native staff companions use these unsummoning rules. Starts-tamed
        // also describes ordinary hens; following can be cleared by commands.
        // Inspect each instance, never cache this exception by prefab hash.
        internal static bool IsSummoned(Tameable animal)
        {
            return animal && (animal.m_unsummonDistance > 0f || animal.m_unsummonOnOwnerLogoutSeconds > 0f);
        }
        // Optional live-instance classification for diagnostics. The dedicated
        // coordinator uses registered prefab metadata and never loads peer areas.
        internal Kinds Instance(int hash, ZNetView view)
        {
            if (!view || !view.IsValid() || !view.m_persistent || view.m_distant) return Kinds.None;
            Kinds kind;
            if (prefabs.TryGetValue(hash, out kind)) return kind;
            if (view.GetComponent<ItemDrop>()) looseDrops.Add(hash);
            kind = Classify(view.gameObject); if ((kind & enabled) == 0) kind = Kinds.None;
            if (NativeOwnership.SafePrefab(view.gameObject, kind)) nativeSafe.Add(hash);
            prefabs.Add(hash, kind); return kind;
        }
        internal Kinds Prefab(int hash)
        {
            Kinds kind;
            if (prefabs.TryGetValue(hash, out kind)) return kind;
            GameObject root = ZNetScene.instance ? ZNetScene.instance.GetPrefab(hash) : null;
            if (!root) return Kinds.None; // Retry missing modded prefabs after registration.
            kind = Classify(root); if ((kind & enabled) == 0) kind = Kinds.None;
            if (NativeOwnership.SafePrefab(root, kind)) nativeSafe.Add(hash);
            prefabs.Add(hash, kind); return kind;
        }
        internal static Kinds Classify(GameObject root)
        {
            ZNetView view = root.GetComponent<ZNetView>();
            if (!view || !view.m_persistent || view.m_distant) return Kinds.None;
            if (root.GetComponent<ItemDrop>()) return Kinds.LooseItems;
            Character actor = root.GetComponent<Character>();
            if (actor)
                // Candidate only. The actual instance's s_tamed flag is checked
                // by the coordinator; no species/component list.
                return actor is Player ? Kinds.None : Kinds.TamedAnimals;
            Kinds result = Kinds.None;
            bool interactive = false;
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component) continue;
                Container container = component as Container;
                ZNetView ownerView = container && container.m_rootObjectOverride ? container.m_rootObjectOverride : component.GetComponentInParent<ZNetView>();
                if (ownerView != view) continue;
                Type type = component.GetType(); Kinds kind;
                interactive |= component is Interactable;
                if (!types.TryGetValue(type, out kind))
                { for (Type current = type; current != null && kind == Kinds.None; current = current.BaseType) groups.TryGetValue(current, out kind); types.Add(type, kind); }
                result |= kind;
            }
            if (!interactive) return Kinds.None;
            foreach (Kinds kind in priority) if ((result & kind) != 0) return kind;
            return Kinds.OtherInteractables;
        }
        internal static bool OwnerRequest(Kinds kind, int hash)
        {
            if (kind != Kinds.TamedAnimals && common.Contains(hash)) return true;
            HashSet<int> methods;
            return requests.TryGetValue(kind, out methods) && methods.Contains(hash);
        }
        internal static bool RefillRequest(Kinds kind, int hash)
        {
            if (kind == Kinds.Smelters) return hash == addOre || hash == addFuel;
            if (kind == Kinds.CookingStations) return hash == addItem || hash == addFuel;
            if (kind == Kinds.Fireplaces) return hash == addFuel || hash == addFuelAmount;
            if (kind == Kinds.Fermenters) return hash == addItem;
            if (kind == Kinds.ShieldGenerators) return hash == addFuel;
            return kind == Kinds.Defenses && hash == addAmmo;
        }
        internal static bool IsRefillMethod(int hash)
        { return hash == addOre || hash == addFuel || hash == addItem || hash == addFuelAmount || hash == addAmmo; }
        internal static bool PotentialOwnerRequest(int hash) { return ownerMethods.Contains(hash); }
        private static readonly int addOre = "RPC_AddOre".GetStableHashCode(), addFuel = "RPC_AddFuel".GetStableHashCode(),
            addItem = "RPC_AddItem".GetStableHashCode(), addFuelAmount = "RPC_AddFuelAmount".GetStableHashCode(), addAmmo = "RPC_AddAmmo".GetStableHashCode();
        private static readonly HashSet<int> common = Hashes("RPC_Damage", "RPC_Repair", "RPC_Remove", "RPC_ClearCachedSupport", "RPC_SetSnow");
        // Existing owner-targeted requests only. Responses and broadcasts are
        // never redirected. Inventories and stations use source-side consent.
        private static readonly Dictionary<Kinds, HashSet<int>> requests = BuildRequests();
        private static readonly HashSet<int> ownerMethods = AllRequests();
        private static HashSet<int> AllRequests()
        { var result = new HashSet<int>(common); foreach (HashSet<int> methods in requests.Values) result.UnionWith(methods); return result; }
        private static Dictionary<Kinds, HashSet<int>> BuildRequests()
        {
            var result = new Dictionary<Kinds, HashSet<int>>();
            Add(result, Kinds.Doors, "UseDoor"); Add(result, Kinds.Beds, "SetOwner");
            Add(result, Kinds.Beehives, "RPC_Extract"); Add(result, Kinds.SapCollectors, "RPC_Extract");
            Add(result, Kinds.Smelters, "RPC_AddOre", "RPC_AddFuel", "RPC_EmptyProcessed");
            Add(result, Kinds.CookingStations, "RPC_AddItem", "RPC_AddFuel", "RPC_RemoveDoneItem");
            Add(result, Kinds.Fireplaces, "RPC_AddFuel", "RPC_AddFuelAmount", "RPC_SetFuelAmount", "RPC_ToggleOn");
            Add(result, Kinds.Wards, "ToggleEnabled", "TogglePermitted"); Add(result, Kinds.MapTables, "MapData");
            Add(result, Kinds.Containers, "RPC_RequestOpen", "RPC_RequestStack", "RPC_RequestTakeAll", "RPC_Discovered", "discovered");
            Add(result, Kinds.ItemStands, "RPC_RequestOwn", "RPC_DropItem", "RPC_DropItemByName", "RPC_DestroyAttachment");
            Add(result, Kinds.Portals, "RPC_SetTag", "RPC_SetConnected");
            Add(result, Kinds.Fermenters, "RPC_AddItem", "RPC_Tap");
            Add(result, Kinds.Incinerators, "RPC_RequestIncinerate", "RPC_RequestOpen", "RPC_RequestStack", "RPC_RequestTakeAll");
            Add(result, Kinds.Defenses, "RPC_RequestStateChange", "RPC_AddAmmo", "RPC_SetTarget");
            Add(result, Kinds.ShieldGenerators, "RPC_AddFuel", "RPC_SetFuel", "RPC_Attack", "RPC_HitNow");
            Add(result, Kinds.Pickables, "RPC_Pick", "Pick", "RPC_RequestOwn", "RPC_TryEat");
            Add(result, Kinds.LooseItems, "RPC_RequestOwn");
            Add(result, Kinds.Offerings, "RPC_SpawnBoss");
            Add(result, Kinds.Vehicles, "RequestControl", "ReleaseControl", "Controls", "RPC_RequestOwn", "RPC_RequestOpen", "RPC_RequestStack", "RPC_RequestTakeAll");
            Add(result, Kinds.TamedAnimals, "Command", "SetName", "AddSaddle", "RequestControl", "ReleaseControl", "RemoveSaddle", "Controls",
                "RPC_Damage", "RPC_Heal", "RPC_Stagger", "RPC_AddNoise", "RPC_AddAdrenaline", "RPC_SetTamed", "Alert", "OnNearProjectileHit", "SetAggravated");
            return result;
        }
        private static void Add(Dictionary<Kinds, HashSet<int>> groups, Kinds kind, params string[] names)
        {
            groups.Add(kind, Hashes(names));
        }
        private static HashSet<int> Hashes(params string[] names)
        { var result = new HashSet<int>(); foreach (string name in names) result.Add(name.GetStableHashCode()); return result; }
    }
}
