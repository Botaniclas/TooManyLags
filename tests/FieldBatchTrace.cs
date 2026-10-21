using System;
using System.Collections.Generic;
using System.IO;
#if PLAYER_SIMULATION
using TooManyLags.PlayerSimulation;
#else
using TooManyLags;
#endif

internal static class SchedulingTrace
{
    private static AuthorityWorld world;
    private static Node[] nodes;
    private static float now;
    private sealed class Node : AuthorityNode
    {
        internal int Id, Calls, Writes, Callback;
        internal long Owner = 7;
        internal bool Ready = true;
        internal override long CurrentOwner { get { return Owner; } }
        internal override ApplyResult Apply(long target)
        {
            Calls++; Node other = nodes[(Id + 1) % nodes.Length]; int callback = Callback; Callback = 0;
            switch (callback)
            {
                case 1: world.Remove(this); break;
                case 2: world.Changed(other, now); break;
                case 3: Advance(other, now); break;
                case 4: world.Remove(other); world.Add(other, now); break;
                case 5: world.Move(this, new Cell(Cell.X + 1, Cell.Z), now); break;
                case 6: world.Dismiss(other); break;
            }
            if (!Ready) return ApplyResult.Pending;
            if (target != 0) { if (Owner != target) { Owner = target; Writes++; } Managed = true; ManagedOwner = target; }
            return ApplyResult.Done;
        }
    }
    private static void Advance(Node node, float at)
    {
#if BEFORE_SCHEDULER
        if (!node.Removed) node.NotBefore = Math.Min(node.NotBefore, at);
#else
        world.Advance(node, at);
#endif
    }
    public static void Main(string[] args)
    {
        using (var output = new BinaryWriter(File.Create(args[0])))
        foreach (bool keep in new[] { true, false }) foreach (bool directional in new[] { true, false })
        {
            var random = new Random(63123); now = 0;
            world = new AuthorityWorld(99, 2, GridCoveragePattern.Rounded7x7, keep, directional, 0, 3, true, .2f);
#if PLAYER_SIMULATION
            world.AfterGridUpdate = null;
#endif
            nodes = new Node[24]; var peers = new List<GridPeer>();
            for (int i = 0; i < nodes.Length; i++) { nodes[i] = new Node { Id = i, Cell = new Cell(random.Next(-4, 5), random.Next(-4, 5)) }; world.Add(nodes[i], now); }
            for (int step = 0; step < 10000; step++)
            {
                now += .05f; if (step != 0 && step % 377 == 0) now -= 3;
                if (step % 11 == 0)
                {
                    peers.Clear(); int count = random.Next(13);
                    for (int uid = 1; uid <= count; uid++) peers.Add(GridPeer.Covered(uid, new Cell(random.Next(-3, 4), random.Next(-3, 4)), random.Next(AuthorityGrid.CoverageCount)));
                }
                Node node = nodes[random.Next(nodes.Length)];
                switch (random.Next(13))
                {
                    case 0: world.Changed(node, now); break;
                    case 1: world.Received(node, now); break;
                    case 2: world.Move(node, new Cell(random.Next(-4, 5), random.Next(-4, 5)), now); break;
                    case 3: Advance(node, now); break;
                    case 4: world.Remove(node); break;
                    case 5: world.Add(node, now); break;
                    case 6: world.Dismiss(node); break;
                    case 7: node.Ready = !node.Ready; break;
                    case 8: node.Owner = random.Next(1, 5); world.Received(node, now); break;
                    case 9: node.Callback = random.Next(1, 7); break;
                    case 10: world.Grid.ConfigureDelays(2, random.Next(3) * .1f, .2f, random.Next(4)); break;
                    case 11: world.Changed(node, now, false); break;
                }
                world.Tick(peers, now);
                output.Write(now); output.Write(world.PendingCount); output.Write(world.BucketCount);
                output.Write(world.Grid.CachedCells); output.Write(world.Grid.RememberedCells); output.Write(world.Grid.DeferredCells);
                foreach (Node item in nodes)
                {
                    output.Write(item.Owner); output.Write(item.Managed); output.Write(item.ManagedOwner);
                    output.Write(item.Removed); output.Write(item.NotBefore); output.Write(item.Cell.X); output.Write(item.Cell.Z);
                    output.Write(item.Calls); output.Write(item.Writes); output.Write(item.Callback);
                    output.Write(world.Grid.Owner(item.Cell)); output.Write(world.Grid.Candidate(item.Cell));
                }
            }
        }
        Console.WriteLine("40,000 deterministic event steps written; both exit modes and full/directional grids, replies, rollback, callbacks and live delays.");
    }
}

