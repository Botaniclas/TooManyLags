using UnityEngine;

namespace BotanicOwnerDebug
{
    internal static class DebugCategories
    {
        private static readonly System.Collections.Generic.HashSet<System.Type> functions = new System.Collections.Generic.HashSet<System.Type> {
            typeof(Door), typeof(Chair), typeof(Barber), typeof(Bed), typeof(CraftingStation), typeof(StationExtension),
            typeof(Smelter), typeof(CookingStation), typeof(Fireplace), typeof(Beehive), typeof(SapCollector), typeof(PrivateArea),
            typeof(MapTable), typeof(WispSpawner), typeof(Container), typeof(ItemStand), typeof(ArmorStand), typeof(Sign),
            typeof(TeleportWorld), typeof(Fermenter), typeof(Incinerator), typeof(Trap), typeof(Turret), typeof(ShieldGenerator),
            typeof(Pickable), typeof(PickableItem), typeof(Feast), typeof(OfferingBowl), typeof(Vagon), typeof(ShipControlls)
        };
        internal const int Interactive = 1, Static = 2, Physics = 4, Tamed = 8, Hostile = 16, Loose = 32, All = 63;
        internal static int Classify(ZNetView view)
        {
            if (!view || !view.m_persistent || view.GetComponent<Projectile>()) return 0;
            if (view.GetComponent<ItemDrop>()) return Loose;
            Character actor = view.GetComponent<Character>();
            if (actor) return actor is Player || view.m_distant ? 0 : actor.IsTamed() ? Tamed : Hostile;
            if (BuildingVisuals.Interactive(view))
            {
                if (view.m_distant) return 0;
                bool marker = false, function = false;
                foreach (MonoBehaviour component in view.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!component) continue;
                    Container container = component as Container;
                    ZNetView owner = container && container.m_rootObjectOverride ? container.m_rootObjectOverride : component.GetComponentInParent<ZNetView>(true);
                    if (owner != view) continue;
                    marker |= component is Teleport || component is Vegvisir;
                    for (System.Type type = component.GetType(); type != null && !function; type = type.BaseType) function |= functions.Contains(type);
                }
                return marker && !function ? 0 : Interactive;
            }
            GameObject root = view.gameObject;
            if (BodyRoot(root) || FixedSource(root)) return Physics;
            if (root.GetComponentsInChildren<Rigidbody>(true).Length != 0) return 0;
            WearNTear wear = root.GetComponent<WearNTear>(); Destructible destructible = root.GetComponent<Destructible>();
            bool building = wear && wear.m_staticPosition;
            bool world = root.GetComponent<TreeBase>() || root.GetComponent<MineRock>() || root.GetComponent<MineRock5>() ||
                (destructible && destructible.m_ttl <= 0f);
            if ((!building && !world) || (wear && !wear.m_staticPosition)) return 0;
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component || component is Interactable || component is Character || component is BaseAI ||
                    component is ItemDrop || component is Container || component is ZSyncTransform || component is TreeLog ||
                    component is SpawnArea || component is CreatureSpawner || component is SpawnSystem ||
                    (component is Destructible && ((Destructible)component).m_ttl > 0f) ||
                    (component is ZNetView && component != view) || component.GetComponentInParent<ZNetView>(true) != view) return 0;
            }
            return Static;
        }
        // Read-only equivalents of TML PhysicsObjects' exact body/successor policy.
        private static bool BodyRoot(GameObject root)
        {
            if (!root || root.GetComponent<Projectile>() || root.GetComponent<ItemDrop>() || root.GetComponent<Character>() ||
                root.GetComponent<Ship>() || root.GetComponent<Vagon>()) return false;
            ZNetView view = root.GetComponent<ZNetView>();
            if (!view || !view.m_persistent || view.m_distant) return false;
            Rigidbody body = root.GetComponent<Rigidbody>(); ZSyncTransform sync = root.GetComponent<ZSyncTransform>();
            if (!body || !sync || !sync.m_syncPosition || !sync.m_syncRotation || !sync.m_syncBodyVelocity ||
                root.GetComponentsInChildren<Rigidbody>(true).Length != 1 || root.GetComponentInChildren<Joint>(true)) return false;
            foreach (ZNetView child in root.GetComponentsInChildren<ZNetView>(true)) if (child != view) return false;
            return true;
        }
        private static bool Successor(GameObject root, int depth)
        {
            if (!root || depth > 8 || root.GetComponent<Projectile>() || root.GetComponent<ItemDrop>() || root.GetComponent<Character>()) return false;
            if (BodyRoot(root)) return true;
            TreeBase tree = root.GetComponent<TreeBase>();
            if (tree && tree.m_logPrefab && !ReferenceEquals(tree.m_logPrefab, root) && Successor(tree.m_logPrefab, depth + 1)) return true;
            Destructible destructible = root.GetComponent<Destructible>();
            return destructible && destructible.m_ttl <= 0f && destructible.m_spawnWhenDestroyed &&
                !ReferenceEquals(destructible.m_spawnWhenDestroyed, root) && Successor(destructible.m_spawnWhenDestroyed, depth + 1);
        }
        private static bool FixedSource(GameObject root)
        {
            if (root.GetComponentInChildren<Rigidbody>(true)) return false;
            TreeBase tree = root.GetComponent<TreeBase>(); Destructible destructible = root.GetComponent<Destructible>();
            return (tree && Successor(tree.m_logPrefab, 0)) ||
                (destructible && destructible.m_ttl <= 0f && Successor(destructible.m_spawnWhenDestroyed, 0));
        }
    }
}
