using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed partial class Controller
    {
        private ApplyResult ApplyContainer(Node node, long target, long owner)
        {
            if (!node.Container || node.Container.m_privacy != Container.PrivacySetting.Public)
                return ApplyResult.Dropped;
            // Owner imports and the native reply can arrive separately. Do not
            // certify the cell or hand it onward while that transaction settles.
            if (inventory.Transitioning(node.Data) || inventory.Pending(node.Data))
            { Wait(node); return ApplyResult.Dropped; }
            if (owner == target)
            {
                inventory.Forget(node.Data);
                node.Managed = true; node.ManagedOwner = target; return ApplyResult.Done;
            }
            int participants = world.Grid.Participants(node.Cell);
            // A server field can outlive its overlap during the cooldown.
            // That alone must not reclaim a sole nearby player's inventory.
            if (target == server && participants == 1) return ApplyResult.Dropped;
            if (!node.View.gameObject.activeInHierarchy || !(node.Cart ? CartIdle(node.Data) : node.Ship ? ShipIdle(node.Data) : Catalog.Idle(node.Data)) ||
                !Available(target, node.Data.GetPosition()) || node.Data.OwnerRevision == System.UInt16.MaxValue)
            { Wait(node); return ApplyResult.Dropped; }
            if (owner == server)
            {
                if (node.Container.IsInUse() || inventory.Acquire(node.Data, node.View, node.Container) != 2)
                { Wait(node); return ApplyResult.Dropped; }
            }
            else if ((participants > 0 || node.Ship || node.Cart) && owner != 0 && Available(owner, node.Data.GetPosition()))
            {
                // RequestOpen performs the source-side native in-use check AND
                // commits to the headless server itself. A different player can
                // receive it only after that exact native transaction settles.
                // Never send a synthetic OpenResponse to a player's GUI.
                // Vehicles keep the native source-use check outside coverage,
                // including their local cargo and cart-joint checks.
                int result = inventory.Acquire(node.Data, node.View, node.Container);
                if (result != 2) { Wait(node); return ApplyResult.Dropped; }
            }
            else
            {
                // No player covers this chest, or its former owner has left.
                // Outside all grids there is no usable/open nearby inventory.
                inventory.Forget(node.Data);
            }
            if (node.Data.GetOwner() != target)
            {
                if (target != server) ZDOMan.instance.ForceSendZDO(target, node.Data.m_uid);
                Assign(node, target);
            }
            if (node.Data.GetOwner() != target) { Wait(node); return ApplyResult.Dropped; }
            inventory.Forget(node.Data);
            node.Managed = true; node.ManagedOwner = target; return ApplyResult.Done;
        }

        internal bool ExpectsInventoryReply(ZDO data)
        { return inventory.Pending(data); }

        internal void InventoryReply(ZDO data, long sender, bool granted)
        {
            Node node;
            if (data == null || !nodes.TryGetValue(data, out node) || !node.Container) return;
            if (inventory.Response(node.Container, sender, granted))
            {
                if (node.InGrid) { world.Dismiss(node); Wait(node); }
            }
            else if (granted && inventory.Received(data)) changed.Add(node);
        }
    }
}
