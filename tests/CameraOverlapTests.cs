using System;
using System.Collections.Generic;
using TooManyLags;

internal static class CameraOverlapTests
{
    private static int checks;
    private static void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
    private static List<GridPeer> Peers(params GridPeer[] peers) { return new List<GridPeer>(peers); }
    private static GridPeer Peer(long uid, int x, int z, int heading)
    { return GridPeer.Covered(uid, new Cell(x, z), heading); }
    private sealed class Node : AuthorityNode
    {
        internal long Owner;
        internal int Writes;
        internal override long CurrentOwner { get { return Owner; } }
        internal override ApplyResult Apply(long target)
        {
            if (target != 0 && Owner != target) { Owner = target; Writes++; }
            Managed = target != 0; ManagedOwner = target;
            return ApplyResult.Done;
        }
    }
    private static AuthorityWorld World(GridCoveragePattern pattern, bool keep, float buffer = 1)
    { return new AuthorityWorld(99, 2, pattern, keep, true, 0, 3, true, .2f, buffer); }
    private static Node Add(AuthorityWorld world, Cell cell)
    { var node = new Node { Cell = cell }; world.Add(node, 0); return node; }
    private static void Matrix()
    {
        int[] fx = { 0, 1, 1, 1, 0, -1, -1, -1 }, fz = { 1, 1, 0, -1, -1, -1, 0, 1 };
        foreach (GridCoveragePattern pattern in Enum.GetValues(typeof(GridCoveragePattern)))
        foreach (bool keep in new[] { true, false }) for (int h = 0; h < 8; h++)
        {
            var world = World(pattern, keep); var cell = new Cell(fx[h], fz[h]); var node = Add(world, cell);
            var a = Peer(1, 0, 0, h); var b = Peer(2, 2 * fx[h], 2 * fz[h], (h + 4) & 7);
            var away = Peer(2, 2 * fx[h], 2 * fz[h], h);
            world.Tick(Peers(a, b), 0); world.Tick(Peers(a, b), 10);
            Check(node.Owner == 99 && node.Writes == 1, "mature overlap owns server");
            // Start long after the original takeover: an acquisition-only
            // cooldown would fail to prevent this camera flicker.
            world.Tick(Peers(a, away), 100);
            Check(node.Owner == 99 && world.Grid.Candidate(cell) == 1 && world.Grid.Remaining(cell, 100) == 1,
                "direction-only overlap loss starts one-second release");
            var newRed = new Cell(3 * fx[h], 3 * fz[h]);
            Check(world.Grid.Owner(newRed) != 0 && world.Grid.Owner(newRed) == world.Grid.Candidate(newRed) && world.Grid.Remaining(newRed, 100) == 0,
                "new red field resolves immediately, including new overlaps");
            int writes = node.Writes;
            world.Tick(Peers(a, b), 100.5f); world.Tick(Peers(a, b), 102);
            Check(node.Owner == 99 && node.Writes == writes && world.Grid.Remaining(cell, 102) == 0,
                "return cancels without an intermediate object write");
            world.Tick(Peers(a, away), 103); world.Tick(Peers(a, away), 103.99f);
            Check(node.Owner == 99, "second genuine loss gets its own buffer");
            world.Tick(Peers(a, away), 104);
            Check(node.Owner == 1 && node.Writes == writes + 1, "stable separation releases exactly once");
            world.Tick(Peers(a, away), 110);
            int visits = world.Grid.TimerVisits, updates = world.Grid.PlayerUpdates, attempts = world.Attempts;
            for (int i = 0; i < 1000; i++) world.Tick(Peers(a, away), 110 + i * .05f);
            Check(world.Grid.TimerVisits == visits && world.Grid.PlayerUpdates == updates && world.Attempts == attempts,
                "settled stationary fields do not revisit timers or objects");
            world.Remove(node); world.Tick(Peers(), 170); world.Tick(Peers(), 175);
            Check(world.Grid.CachedCells == 0 && world.Grid.RememberedCells == 0 && world.Grid.DeferredCells == 0 &&
                world.BucketCount == 0 && world.PendingCount == 0, "unload removes all field/hold/cache state");
        }
    }
    private static void Exceptions()
    {
        var a = Peer(1, 0, 0, 0); var b = Peer(2, 0, 2, 4); var away = Peer(2, 0, 2, 0);
        var cell = new Cell(0, 1);
        foreach (bool keep in new[] { true, false }) foreach (int mode in new[] { 0, 1, 2 })
        {
            var world = World(GridCoveragePattern.Rounded7x7, keep, mode == 0 ? 0 : 1); var node = Add(world, cell);
            world.Tick(Peers(a, b), 0);
            world.Tick(mode == 0 ? Peers(a, away) : mode == 1 ? Peers(a, Peer(2, 50, 0, 0)) : Peers(a), 1);
            Check(node.Owner == 1, "disabled buffer, movement and disconnect retain immediate red release");
        }
        var w = World(GridCoveragePattern.Rounded7x7, true); var n = Add(w, cell);
        w.Tick(Peers(a, b), 0); w.Tick(Peers(a, away), 10);
        w.Tick(Peers(a, away, Peer(3, 0, 2, 4)), 10.2f);
        Check(n.Owner == 99 && w.Grid.Candidate(cell) == 99 && w.Grid.Remaining(cell, 10.2f) == 0,
            "different third player also cancels the pending release");
        w.Tick(Peers(a, away, Peer(3, 0, 2, 0)), 10.3f); w.Tick(Peers(a, away), 10.4f);
        w.Tick(Peers(a, away), 11.3f);
        Check(n.Owner == 1, "three-player bookkeeping releases after the new last overlap ends");

        w = World(GridCoveragePattern.Rounded7x7, true); n = Add(w, cell);
        w.Tick(Peers(a, b), 0); w.Tick(Peers(a, away), 10);
        w.Grid.ConfigureOverlapRelease(0); w.Tick(Peers(a, away), 10.5f);
        Check(n.Owner == 99, "live setting preserves existing hold deadline");
        w.Tick(Peers(a, away), 11); w.Tick(Peers(a, b), 12); w.Tick(Peers(a, away), 13);
        Check(n.Owner == 1 && w.Grid.Remaining(cell, 13) == 0, "live zero disables subsequent holds");

        w = World(GridCoveragePattern.Rounded7x7, true); n = Add(w, cell);
        w.Tick(Peers(a, b), 0); w.Tick(Peers(a, away), 10); w.Tick(Peers(), 10.1f);
        Check(w.Grid.CachedCells == 0 && w.PendingCount == 0 && n.Owner == 99,
            "fully vacant keep-on-exit cells retire immediately without ownership return");
        w.Tick(Peers(a), 10.2f);
        Check(n.Owner == 1, "recycled policy has no stale hold");

        w = World(GridCoveragePattern.Rounded7x7, false); n = Add(w, cell);
        w.Tick(Peers(a, b), 0); w.Tick(Peers(a, away), 10); w.Tick(Peers(a, away), 5);
        Check(n.Owner == 99 && w.Grid.Remaining(cell, 5) == 1, "backwards clock resets the finite hold");
        w.Tick(Peers(a, away), 6); Check(n.Owner == 1, "clock reset cannot strand a release");

        var full = new AuthorityWorld(99, 1, GridCoveragePattern.Square3x3, true, false, 0, 3, false, .2f, 20);
        var fullNode = Add(full, new Cell(0, 0)); full.Tick(Peers(a, Peer(2, 0, 0, 4)), 0);
        full.Tick(Peers(a, Peer(2, 0, 0, 4)), 1); full.Tick(Peers(a), 2); full.Tick(Peers(a), 3);
        Check(fullNode.Owner == 1, "full grid is unaffected");

        // Small real camera changes shrink an adjacent-mask union; this must
        // debounce exactly like a large turn, without dropping current coverage.
        w = World(GridCoveragePattern.Rounded7x7, true); n = Add(w, new Cell(-1, 0));
        var adjacent = Peer(2, 0, 0, AuthorityGrid.HeadingCoverage(37));
        var afterBoundary = Peer(2, 0, 0, AuthorityGrid.HeadingCoverage(38));
        w.Tick(Peers(a, adjacent), 0); w.Tick(Peers(a, afterBoundary), 10);
        Check(n.Owner == 99 && w.Grid.Remaining(n.Cell, 10) == 1, "adjacent camera-union boundary starts buffer");
        w.Tick(Peers(a, adjacent), 10.1f); w.Tick(Peers(a, adjacent), 12);
        Check(n.Owner == 99 && n.Writes == 1, "one-degree camera return avoids an intermediate owner write");
    }
    private static void WorkReduction()
    {
        var a = Peer(1, 0, 0, 0); var b = Peer(2, 0, 2, 4); var away = Peer(2, 0, 2, 0);
        var cell = new Cell(0, 1);
        int[] writes = new int[2], attempts = new int[2];
        for (int mode = 0; mode < 2; mode++)
        {
            var world = World(GridCoveragePattern.Rounded7x7, true, mode); var node = Add(world, cell);
            world.Tick(Peers(a, b), 0); world.Tick(Peers(a, b), 10);
            int initialWrites = node.Writes, initialAttempts = world.Attempts;
            for (int i = 0; i < 200; i++)
            {
                world.Tick(Peers(a, away), 20 + i * .2f);
                world.Tick(Peers(a, b), 20 + i * .2f + .1f);
            }
            writes[mode] = node.Writes - initialWrites;
            attempts[mode] = world.Attempts - initialAttempts;
            Check(node.Owner == 99 && world.PendingCount == 0, "flicker trace retains final server owner and no object retry");
        }
        Check(writes[0] == 400 && writes[1] == 0 && attempts[0] == 400 && attempts[1] == 0,
            "200 brief turns avoid all 400 target-object writes/jobs");
        Console.WriteLine("WORK: 200 synthetic camera flickers, target field: 400 writes/jobs without buffer, 0 with buffer; not a live CPU benchmark.");
    }
    public static int Main()
    {
        try { Matrix(); Exceptions(); WorkReduction(); Console.WriteLine("PASS: " + checks + " camera overlap debounce, ownership writes, scope and cache lifecycle checks."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
