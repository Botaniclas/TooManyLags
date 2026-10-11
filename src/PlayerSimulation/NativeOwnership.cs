using UnityEngine;

namespace TooManyLags.PlayerSimulation
{
    // Vanilla peers cannot consent to handoffs or expose local inventory/AI
    // state. Restrict coordinator writes to known non-consumptive interactions.
    internal static class NativeOwnership
    {
        internal static bool SafePrefab(GameObject root, Kinds kind)
        {
            if (kind != Kinds.Doors && kind != Kinds.Seats && kind != Kinds.Beds && kind != Kinds.Workstations &&
                kind != Kinds.Wards && kind != Kinds.Signs && kind != Kinds.Portals) return false;
            if (root.GetComponent<Character>() || root.GetComponentsInChildren<Container>(true).Length != 0) return false;
            bool interactive = false;
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!component || !(component is Interactable)) continue;
                interactive = true;
                System.Type type = component.GetType();
                if (type != typeof(Door) && type != typeof(Chair) && type != typeof(Barber) && type != typeof(Bed) &&
                    type != typeof(CraftingStation) && type != typeof(PrivateArea) && type != typeof(Sign) &&
                    type != typeof(TeleportWorld) && type != typeof(Teleport)) return false;
            }
            return interactive;
        }
        internal static bool Idle(ZDO data)
        {
            return data.GetLong(ZDOVars.s_user, 0) == 0 && data.GetInt(ZDOVars.s_inUse) == 0 && !data.GetBool(ZDOVars.s_attachJointHash);
        }
    }
}
