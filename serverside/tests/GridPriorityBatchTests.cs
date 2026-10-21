using System;
using System.Collections.Generic;
using TooManyLagsDedicatedAddon;
internal static class GridPriorityBatchTests
{
    private static int checks;
    private static readonly List<int> order = new List<int>();
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    private sealed class Node : AuthorityNode
    {
        internal long Owner;
        internal int Calls, Tier;
        internal bool Blocked;
        internal Action Callback;
        internal Node(Cell cell) { Cell = cell; Owner = 99; }
        internal override long CurrentOwner { get { return Owner; } }
        internal override ApplyResult Apply(long target)
        {
            Calls++; order.Add(Tier); if (Callback != null) Callback();
            if (Blocked) return ApplyResult.Pending;
            Owner = target; ManagedOwner = target; Managed = true; return ApplyResult.Done;
        }
    }
    private static void Main()
    {
        foreach (bool keep in new[] { false, true })
        {
            var world = new AuthorityWorld(99, 0, GridCoveragePattern.Rounded7x7, keep, true, 0, 0, true, 0);
            var peers = new List<GridPeer> { new GridPeer(1, new Cell(0, 0), 0) };
            world.Tick(peers, 0);
            var cells = new Cell[4]; var found = new bool[4];
            for (int x = -6; x <= 6; x++) for (int z = -6; z <= 6; z++)
            {
                Cell cell = new Cell(x, z); int tier = world.Grid.WorkPriority(cell);
                if (!found[tier]) { found[tier] = true; cells[tier] = cell; }
            }
            foreach (bool present in found) Check(present, "all four priority lanes represented");
            var nodes = new List<Node>();
            // Deliberately enqueue in reverse priority, with dense same-cell work.
            for (int tier = 3; tier >= 0; tier--) for (int n = 0; n < 24; n++)
            {
                var node = new Node(cells[tier]) { Tier = tier }; nodes.Add(node); world.Add(node, 0);
            }
            order.Clear(); world.Tick(peers, 1);
            for (int i = 1; i < order.Count; i++) Check(order[i] >= order[i - 1], "urgent fields precede returns");
            foreach (Node node in nodes)
            {
                Check(node.Calls == 1, "all due jobs processed without quota");
                Check(node.Owner == world.Grid.Owner(node.Cell), "actual field target retained");
                Check(!node.IndexedBucket.PriorityCached, "cache cleared before callbacks");
            }
            Check(world.PendingCount == 0, "no artificial backlog");
            order.Clear(); world.Tick(peers, 2); Check(order.Count == 0, "stable fields do no repeated object work");
            Node moving = nodes[0]; world.Move(moving, cells[0], 2); moving.Tier = 0;
            Check(moving.IndexedBucket == nodes[nodes.Count - 1].IndexedBucket, "movement updates bucket reference");
            world.Tick(peers, 3); Check(moving.Owner == world.Grid.Owner(cells[0]), "moving object uses destination field");
            var blocker = new Node(cells[0]) { Blocked = true }; world.Add(blocker, 3);
            world.Tick(peers, 4); Check(world.PendingCount == 1, "pending guard remains pending");
            blocker.Blocked = false; world.Tick(peers, 5); Check(world.PendingCount == 0, "guard retry preserved");
            // A callback can remove a later snapshot entry safely.
            var victim = new Node(cells[3]); var remover = new Node(cells[0]);
            remover.Callback = delegate { world.Remove(victim); Check(!remover.IndexedBucket.PriorityCached, "callback never observes cached priority"); };
            world.Add(victim, 6); world.Add(remover, 6); world.Tick(peers, 6);
            Check(victim.Calls == 0 && victim.IndexedBucket == null, "removed snapshot job ignored and detached");
            world.Remove(remover); world.Remove(blocker);
            foreach (Node node in nodes) { world.Remove(node); Check(node.IndexedBucket == null, "unload releases bucket reference"); }
            Check(world.BucketCount == 0 && world.PendingCount == 0, "unload frees all object buckets");
            var exceptionNode = new Node(cells[0]); exceptionNode.Callback = delegate { throw new InvalidOperationException("probe"); };
            world.Add(exceptionNode, 7); try { world.Tick(peers, 7); } catch (InvalidOperationException) { }
            exceptionNode.Callback = null; order.Clear(); world.Tick(peers, 8);
            Check(order.Count == 1, "exception clears work snapshot before retry");
            world.Remove(exceptionNode);
        }
        Console.WriteLine("PASS: " + checks + " priority, batching, guard, callback, exception and unload assertions.");
    }
}
