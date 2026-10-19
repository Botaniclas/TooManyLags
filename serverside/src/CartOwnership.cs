using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed partial class Controller
    {
        // Vagon uses a BOOLEAN at this key, unlike ZSyncTransform's string.
        private static bool CartIdle(ZDO data)
        { return !data.GetBool(ZDOVars.s_attachJointHash) && data.GetInt(ZDOVars.s_inUse) == 0; }

        private ApplyResult ApplyCart(Node node, long target, long owner)
        {
            PruneUseRequests(node);
            if (!node.Cart || !node.Container || !CartIdle(node.Data) || node.Cart.InUse() ||
                (node.NativeUseRequests != null && node.NativeUseRequests.Count != 0))
            { Wait(node); return ApplyResult.Dropped; }
            // A remote source's native RequestOpen commits to the server first,
            // even for an onward player handoff. That physics step must be ready.
            if (owner != target && (target == server || owner != server) && !MobileAreaReady(node))
            { Wait(node); return ApplyResult.Dropped; }
            return ApplyContainer(node, target, owner);
        }

        internal void CartDenied(ZDO data, long requester)
        {
            Node node;
            if (data == null || !nodes.TryGetValue(data, out node) || node.Group != Groups.Carts ||
                node.NativeUseRequests == null || !node.NativeUseRequests.Remove(requester)) return;
            // The native denial clears this client's pending hitch request.
            // Keep the original denial and wake only this loaded object.
            changed.Add(node);
        }

        private void CartAttached(Node node, long owner)
        {
            // A replicated joint confirms completion of this owner's native
            // request. The joint itself now protects use until native detach.
            if (node.NativeUseRequests != null && node.Data.GetBool(ZDOVars.s_attachJointHash)) node.NativeUseRequests.Remove(owner);
        }
    }
}
