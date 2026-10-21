using System;
using System.Collections.Generic;
using TooManyLagsStandalone;
internal static class GridBatchEquivalence
{
    private sealed class Node : AuthorityNode
    {
        internal long Owner = 9; internal int Writes; internal bool Block;
        internal Node(Cell cell) { Cell = cell; }
        internal override long CurrentOwner { get { return Owner; } }
        internal override ApplyResult Apply(long target)
        {
            if (Block) return ApplyResult.Pending;
            if (Owner != target) { Owner = target; Writes++; }
            Managed = true; ManagedOwner = target; return ApplyResult.Done;
        }
    }
    private static void Main()
    {
        ulong digest = 14695981039346656037UL;
        foreach (bool keep in new[] { false, true }) foreach (bool directional in new[] { false, true })
        {
            var random = new Random(8713); var nodes = new List<Node>();
            var world = new AuthorityWorld(99, 2, GridCoveragePattern.Rounded7x7, keep, directional, 0, 3, true, .2f);
            for (int n = 0; n < 512; n++) { var node = new Node(new Cell(random.Next(-8, 9), random.Next(-8, 9))); nodes.Add(node); world.Add(node, 0); }
            for (int frame = 0; frame < 600; frame++)
            {
                float now = frame * .1f;
                var peers = new List<GridPeer>();
                if (frame < 570) peers.Add(new GridPeer(1, new Cell(frame / 40 % 6 - 3, frame / 70 % 4 - 2), (frame / 13 % 8) * 45));
                if (frame % 150 < 95) peers.Add(new GridPeer(2, new Cell(frame / 45 % 6 - 3, 1), (frame / 19 % 8) * 45));
                foreach (Node node in nodes) node.Block = frame % 17 < 3;
                int index = frame % nodes.Count;
                if (frame % 7 == 0) world.Move(nodes[index], new Cell(random.Next(-8, 9), random.Next(-8, 9)), now);
                if (frame % 11 == 0) world.Changed(nodes[index], now);
                if (frame % 23 == 0) world.Received(nodes[index], now);
                if (frame % 41 == 0) { world.Remove(nodes[index]); world.Add(nodes[index], now); }
                world.Tick(peers, now);
                foreach (Node node in nodes) unchecked
                { digest ^= (ulong)(node.Owner + node.Writes * 101L); digest *= 1099511628211UL; digest ^= (ulong)BitConverter.ToInt32(BitConverter.GetBytes(node.NotBefore), 0); digest *= 1099511628211UL; }
            }
        }
        Console.WriteLine(digest.ToString("X16"));
    }
}
