using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLags
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
        Vehicles = 8388608, OtherInteractables = 16777216, WorldMarkers = 33554432,
        StaticBuildings = 67108864, LooseItems = 134217728
    }

    internal sealed class ObjectCatalog
    {
        private readonly Dictionary<int, Kinds> prefabs = new Dictionary<int, Kinds>();
        // Prefab-only eligibility: stationary, permanent refill, incinerator,
        // and default-only world markers. No scene-loading scan here.
        private readonly Dictionary<int, ulong> nativeDefaults = new Dictionary<int, ulong>();
        private readonly Kinds enabled;
        private readonly HashSet<int> looseDrops = new HashSet<int>();
        internal bool IsLooseDrop(int hash) { return looseDrops.Contains(hash); }
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
            { typeof(Sign), Kinds.Signs }, { typeof(TeleportWorld), Kinds.Portals },
            { typeof(Teleport), Kinds.WorldMarkers }, { typeof(Vegvisir), Kinds.WorldMarkers },
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
            Kinds.Wards, Kinds.MapTables, Kinds.Wisplights, Kinds.Signs, Kinds.Portals, Kinds.Pickables, Kinds.Offerings, Kinds.WorldMarkers
        };
        internal ObjectCatalog(Kinds enabled) { this.enabled = enabled; }
        // Separate from the live-instance cache: a template with runtime-added
        // components must never poison the existing grid's classification.
        internal bool NativeDefault(int hash, ZNetScene scene)
        { return (NativeDefaultFlags(hash, scene) & 1) != 0; }
        internal bool NativeRefillDefault(int hash, ZNetScene scene)
        { return (NativeDefaultFlags(hash, scene) & 2) != 0; }
        internal bool NativeIncineratorDefault(int hash, ZNetScene scene)
        { return (NativeDefaultFlags(hash, scene) & 4) != 0; }
        internal bool NativeWorldDefault(int hash, ZNetScene scene)
        { return (NativeDefaultFlags(hash, scene) & 8) != 0; }
        internal Kinds NativeDefaultKind(int hash, ZNetScene scene)
        { return (Kinds)(NativeDefaultFlags(hash, scene) >> 8); }
        internal bool NativeInventoryDefault(int hash, ZNetScene scene)
        { return (NativeDefaultFlags(hash, scene) & 16) != 0; }
        private ulong NativeDefaultFlags(int hash, ZNetScene scene)
        {
            ulong flags; if (nativeDefaults.TryGetValue(hash, out flags)) return flags;
            GameObject prefab = scene.GetPrefab(hash); if (!prefab) return 0;
            Kinds kind = Classify(prefab);
            bool eligible = kind != Kinds.None && (kind & enabled) != 0 && kind != Kinds.TamedAnimals && kind != Kinds.Vehicles &&
                Stationary(prefab, kind);
            if (eligible && kind == Kinds.Incinerators)
            {
                Incinerator station = prefab.GetComponent<Incinerator>();
                eligible = IncineratorProtection.Active && IncineratorProtection.Supported && station && station.m_container &&
                    (station.m_container.m_rootObjectOverride ? station.m_container.m_rootObjectOverride : station.m_container.GetComponentInParent<ZNetView>(true)) == prefab.GetComponent<ZNetView>();
            }
            bool hasInventory = false;
            if (eligible)
            {
                int inventories = 0; ZNetView root = prefab.GetComponent<ZNetView>();
                foreach (Container inventory in prefab.GetComponentsInChildren<Container>(true))
                    if ((inventory.m_rootObjectOverride ? inventory.m_rootObjectOverride : inventory.GetComponentInParent<ZNetView>(true)) == root &&
                        (++inventories > 1 || inventory.m_privacy != Container.PrivacySetting.Public)) { eligible = false; break; }
                hasInventory = inventories != 0;
            }
            flags = eligible ? 1UL | ((ulong)(uint)kind << 8) : 0UL;
            if (eligible && kind == Kinds.Incinerators) flags |= 4;
            if (eligible && kind == Kinds.WorldMarkers) flags |= 8;
            if (eligible && hasInventory) flags |= 16;
            if (eligible && (kind == Kinds.Smelters || kind == Kinds.CookingStations || kind == Kinds.Fireplaces ||
                kind == Kinds.Fermenters || kind == Kinds.ShieldGenerators || (kind == Kinds.Defenses && prefab.GetComponentInChildren<Turret>(true)))) flags |= 2;
            nativeDefaults.Add(hash, flags); return flags;
        }
        // Structural eligibility only. Use protection belongs to the actual
        // owner, not an inactive prefab or a first-load server placeholder.
        internal static bool InitialRoot(GameObject root, Kinds expected)
        {
            Container inventory;
            return InitialRoot(root, expected, out inventory);
        }
        internal static bool InitialRoot(GameObject root, Kinds expected, out Container inventory)
        {
            inventory = null;
            if (expected == Kinds.None || expected == Kinds.TamedAnimals || expected == Kinds.Vehicles || expected == Kinds.WorldMarkers ||
                root.transform.parent != null || Classify(root) != expected || !Stationary(root, expected)) return false;
            ZNetView view = root.GetComponent<ZNetView>(); int count = 0; Container found = null;
            foreach (Container candidate in root.GetComponentsInChildren<Container>(true))
                if ((candidate.m_rootObjectOverride ? candidate.m_rootObjectOverride : candidate.GetComponentInParent<ZNetView>(true)) == view)
                {
                    if (++count > 1 || candidate.m_privacy != Container.PrivacySetting.Public) return false;
                    found = candidate;
                }
            inventory = found;
            return true;
        }
        internal static bool Stationary(GameObject root, Kinds kind)
        {
            if (kind == Kinds.Vehicles || kind == Kinds.TamedAnimals || kind == Kinds.LooseItems) return false;
            if (!root.GetComponentInChildren<Rigidbody>(true)) return true;
            // Native doors animate a kinematic child; their networked building
            // root stays fixed. Do not extend this exception to movable roots,
            // synchronized physics, gravity, dynamic bodies or other categories.
            if (kind != Kinds.Doors || !root.GetComponent<Door>() || root.GetComponent<Rigidbody>() || root.GetComponentInChildren<ZSyncTransform>(true)) return false;
            WearNTear wear = root.GetComponent<WearNTear>();
            if (!wear || !wear.m_staticPosition) return false;
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
                if (!body.isKinematic || body.useGravity) return false;
            return true;
        }
        // Native staff companions use these unsummoning rules. Starts-tamed
        // also describes ordinary hens; following can be cleared by commands.
        // Inspect each instance, never cache this exception by prefab hash.
        internal static bool IsSummoned(Tameable animal)
        {
            return animal && (animal.m_unsummonDistance > 0f || animal.m_unsummonOnOwnerLogoutSeconds > 0f);
        }
        // Classify the first loaded, initialized instance of each prefab. A
        // stored template is not a live network hierarchy and can return None
        // even though the real instance owns valid Interactable components.
        internal Kinds Instance(int hash, ZNetView view)
        {
            if (!view || !view.IsValid() || !view.m_persistent || view.m_distant) return Kinds.None;
            Kinds kind;
            if (prefabs.TryGetValue(hash, out kind)) return kind;
            if (view.GetComponent<ItemDrop>()) looseDrops.Add(hash);
            kind = Classify(view.gameObject); if ((kind & enabled) == 0 || kind == Kinds.WorldMarkers) kind = Kinds.None;
            prefabs.Add(hash, kind); return kind;
        }
        internal static Kinds Classify(GameObject root)
        {
            ZNetView view = root.GetComponent<ZNetView>();
            if (!view || !view.m_persistent) return Kinds.None;
            // Keep moving item drops separate from stationary harvestables.
            if (root.GetComponent<ItemDrop>()) return Kinds.LooseItems;
            Character actor = root.GetComponent<Character>();
            if (actor)
                // Candidate only. The actual instance's s_tamed flag is checked
                // by Register/DataChanged/Controllable; no species/component list.
                return actor is Player || view.m_distant ? Kinds.None : Kinds.TamedAnimals;
            Kinds result = Kinds.None;
            bool interactive = false;
            MonoBehaviour[] components = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour component in components)
            {
                if (!component) continue;
                Container container = component as Container;
                // Prefab assets are inactive; the root lookup must match the
                // includeInactive component enumeration above.
                ZNetView ownerView = container && container.m_rootObjectOverride ? container.m_rootObjectOverride : component.GetComponentInParent<ZNetView>(true);
                if (ownerView != view) continue;
                Type type = component.GetType(); Kinds kind;
                interactive |= component is Interactable;
                if (!types.TryGetValue(type, out kind))
                { for (Type current = type; current != null && kind == Kinds.None; current = current.BaseType) groups.TryGetValue(current, out kind); types.Add(type, kind); }
                result |= kind;
            }
            if (!interactive) return StaticBuildings.Safe(root, components) ? Kinds.StaticBuildings : Kinds.None;
            // Local world interactions do not send requests to their ZDO owner.
            // Only their existing, persistent network root gets a server default;
            // do not manage an entire location or another child ZNetView.
            if (view.m_distant) return result == Kinds.WorldMarkers ? Kinds.WorldMarkers : Kinds.None;
            foreach (Kinds kind in priority) if ((result & kind) != 0) return kind;
            return Kinds.OtherInteractables;
        }
        internal static bool OwnerRequest(Kinds kind, int hash)
        {
            if (kind != Kinds.TamedAnimals && common.Contains(hash)) return true;
            HashSet<int> methods;
            return requests.TryGetValue(kind, out methods) && methods.Contains(hash);
        }
        internal static bool AnyOwnerRequest(int hash)
        { return allRequests.Contains(hash); }
        internal static bool RefillRequest(Kinds kind, int hash)
        {
            if (kind == Kinds.Smelters) return hash == addOre || hash == addFuel;
            if (kind == Kinds.CookingStations) return hash == addItem || hash == addFuel;
            if (kind == Kinds.Fireplaces) return hash == addFuel || hash == addFuelAmount;
            if (kind == Kinds.Fermenters) return hash == addItem;
            if (kind == Kinds.ShieldGenerators) return hash == addFuel;
            return kind == Kinds.Defenses && hash == addAmmo;
        }
        private static readonly int addOre = "RPC_AddOre".GetStableHashCode(), addFuel = "RPC_AddFuel".GetStableHashCode(),
            addItem = "RPC_AddItem".GetStableHashCode(), addFuelAmount = "RPC_AddFuelAmount".GetStableHashCode(), addAmmo = "RPC_AddAmmo".GetStableHashCode();
        private static readonly HashSet<int> common = Hashes("RPC_Damage", "RPC_Repair", "RPC_Remove", "RPC_ClearCachedSupport", "RPC_SetSnow");
        // Existing owner-targeted requests only. Responses and broadcasts are
        // never redirected. Public inventories use native source-side consent;
        // station ownership stays on the server after the initial handoff.
        private static readonly HashSet<int> allRequests = new HashSet<int>();
        private static readonly Dictionary<Kinds, HashSet<int>> requests = BuildRequests();
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
            HashSet<int> methods = Hashes(names); groups.Add(kind, methods); allRequests.UnionWith(methods); allRequests.UnionWith(common);
        }
        private static HashSet<int> Hashes(params string[] names)
        { var result = new HashSet<int>(); foreach (string name in names) result.Add(name.GetStableHashCode()); return result; }
    }
}
