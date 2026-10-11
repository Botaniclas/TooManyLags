using System;
using System.Collections.Generic;
using TooManyLags;

internal static class ServerOverlapCooldownTests
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
    private static AuthorityWorld World(GridCoveragePattern pattern, bool keep, float cooldown = 1)
    { return new AuthorityWorld(99, 2, pattern, keep, true, 0, 3, true, .2f, cooldown); }
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
            world.Tick(Peers(a, b), 0);
            Check(node.Owner == 99 && node.Writes == 1, "overlap starts server ownership and cooldown");
            world.Tick(Peers(a, away), .1f);
            Check(node.Owner == 99 && world.Grid.Candidate(cell) == 99 && world.Grid.Remaining(cell, .1f) == 0,
                "camera-away field request is ignored without a release timer");
            world.Tick(Peers(a, b), .2f); world.Tick(Peers(a, away), .9f);
            Check(node.Owner == 99 && node.Writes == 1, "camera away/back cannot bypass or restart the original lock");
            world.Tick(Peers(a, away), 10);
            Check(node.Owner == 99 && world.Grid.Candidate(cell) == 99 && world.Grid.Remaining(cell, 10) == 0,
                "expiry by itself never requests a player return");
            world.Tick(Peers(a, Peer(2, 2 * fx[h], 2 * fz[h], (h + 1) & 7)), 11);
            Check(node.Owner == 99, "activity of a peer outside the field does not update it");
            // A's next real update still covers the target at the same inner
            // tier. The expired cooldown must revisit these common cells too.
            var changedA = Peer(1, 0, 0, 8 + h);
            var far = Peer(2, 50, 50, h);
            world.Tick(Peers(a, far), 12);
            world.Tick(Peers(changedA, far), 13);
            Check(node.Owner == 1 && node.Writes == 2, "first relevant post-expiry activity evaluates current coverage");
            world.Tick(Peers(changedA, far), 20);
            int visits = world.Grid.TimerVisits, updates = world.Grid.PlayerUpdates, attempts = world.Attempts;
            for (int i = 0; i < 1000; i++) world.Tick(Peers(changedA, far), 20 + i * .05f);
            Check(world.Grid.TimerVisits == visits && world.Grid.PlayerUpdates == updates && world.Attempts == attempts,
                "settled stationary fields add no timer or object work");
            world.Remove(node); world.Tick(Peers(), 80); world.Tick(Peers(), 85);
            Check(world.Grid.CachedCells == 0 && world.Grid.RememberedCells == 0 && world.Grid.DeferredCells == 0 &&
                world.BucketCount == 0 && world.PendingCount == 0, "unload clears cooldowns and every field/cache reference");
        }
    }
    private static void EventDrivenExpiry()
    {
        var a = Peer(1, 0, 0, 0); var b = Peer(2, 0, 2, 4); var away = Peer(2, 0, 2, 0);
        var cell = new Cell(0, 1); var world = World(GridCoveragePattern.Rounded7x7, true); var node = Add(world, cell);
        world.Tick(Peers(a, b), 0); world.Tick(Peers(a, away), .1f); world.Tick(Peers(a, away), 10);
        int visits = world.Grid.TimerVisits, attempts = world.Attempts;
        for (int i = 0; i < 1000; i++) world.Tick(Peers(a, away), 10 + i * .05f);
        Check(node.Owner == 99 && node.Writes == 1 && world.Grid.TimerVisits == visits && world.Attempts == attempts,
            "expired ignored request stays on server with zero ongoing timer/object work");
        world.Tick(Peers(Peer(1, 0, 0, 8), away), 70);
        Check(node.Owner == 1 && node.Writes == 2, "same-tier covered field responds to next real heading event");

        world = World(GridCoveragePattern.Rounded7x7, true); node = Add(world, cell);
        world.Tick(Peers(a, b), 0); world.Tick(Peers(a, b), 100); world.Tick(Peers(a, away), 101);
        Check(node.Owner == 1 && world.Grid.Remaining(cell, 101) == 0,
            "old server acquisition does not create a new cooldown when overlap later disappears");

        world = World(GridCoveragePattern.Rounded7x7, true, 5); node = Add(world, cell);
        world.Tick(Peers(a, b), 0); world.Tick(Peers(a, away), 1); world.Tick(Peers(a, b), 4.9f);
        world.Tick(Peers(a, away), 5);
        Check(node.Owner == 1, "noop server re-evaluation inside cooldown never extends acquisition deadline");

        world = World(GridCoveragePattern.Rounded7x7, false); node = Add(world, new Cell(2, 0));
        var outerB = Peer(2, 4, 0, 0); var outerAway = Peer(2, 4, 0, 2);
        world.Tick(Peers(a, outerB), 0);
        Check(world.Grid.Candidate(node.Cell) == 99 && world.Grid.Owner(node.Cell) == 0,
            "outer overlap first waits for normal takeover timer");
        world.Tick(Peers(a, outerB), 3); world.Tick(Peers(a, outerAway), 3.1f);
        Check(node.Owner == 99 && world.Grid.Candidate(node.Cell) == 99 && world.Grid.Remaining(node.Cell, 3.1f) == 0,
            "cooldown starts at timer-driven server commit, not at request creation");
        world.Tick(Peers(Peer(1, 0, 0, 8), outerAway), 4);
        Check(node.Owner == 99 && world.Grid.Candidate(node.Cell) == 1 && world.Grid.Remaining(node.Cell, 4) == 3,
            "post-expiry activity uses the ordinary outer-field delay");
    }
    private static void LargeBucketWork()
    {
        var a = Peer(1, 0, 0, 0); var b = Peer(2, 0, 2, 4); var away = Peer(2, 0, 2, 0);
        var world = World(GridCoveragePattern.Rounded7x7, true); var cell = new Cell(0, 1);
        var nodes = new Node[10000]; for (int i = 0; i < nodes.Length; i++) nodes[i] = Add(world, cell);
        world.Tick(Peers(a, b), 0); int attempts = world.Attempts;
        for (int i = 0; i < 12; i++)
        { world.Tick(Peers(a, away), .01f + i * .05f); world.Tick(Peers(a, b), .02f + i * .05f); }
        world.Tick(Peers(a, away), .9f); world.Tick(Peers(a, away), 10);
        Check(world.Attempts == attempts && world.PendingCount == 0,
            "10000-object bucket performs no job for ignored camera updates or cooldown expiry");
        foreach (Node node in nodes) Check(node.Owner == 99 && node.Writes == 1, "every object retains server through flicker");
        world.Tick(Peers(Peer(1, 0, 0, 8), away), 11);
        Check(world.Attempts == attempts + 10000, "first genuine update after expiry schedules the bucket exactly once");
        foreach (Node node in nodes) { Check(node.Owner == 1 && node.Writes == 2, "one transfer per object after genuine activity"); world.Remove(node); }
        world.Tick(Peers(), 12); Check(world.Grid.CachedCells == 0 && world.BucketCount == 0 && world.PendingCount == 0,
            "large bucket unload clears all state");
    }
    private static void ScopeAndLifecycle()
    {
        var a = Peer(1, 0, 0, 0); var b = Peer(2, 0, 2, 4); var away = Peer(2, 0, 2, 0);
        var cell = new Cell(0, 1);
        foreach (bool keep in new[] { true, false })
        {
            var world = World(GridCoveragePattern.Rounded7x7, keep, 0); var node = Add(world, cell);
            world.Tick(Peers(a, b), 0); world.Tick(Peers(a, away), .1f);
            Check(node.Owner == 1, "zero cooldown keeps native grid timing");

            world = World(GridCoveragePattern.Rounded7x7, keep); node = Add(world, cell);
            world.Tick(Peers(a, b), 0); world.Tick(Peers(a), .1f); world.Tick(Peers(a), 10);
            Check(node.Owner == 99 && world.Grid.Remaining(cell, 10) == 0,
                "disconnect updates coverage but cannot create an expiry-only return");
            world.Tick(Peers(Peer(1, 0, 0, 8)), 11);
            Check(node.Owner == 1, "next remaining-player heading update uses cleaned disconnect coverage");

            world = World(GridCoveragePattern.Rounded7x7, keep); node = Add(world, cell);
            world.Tick(Peers(a, b), 0); world.Tick(Peers(a, Peer(2, 50, 50, 0)), .1f);
            world.Tick(Peers(Peer(1, 0, 0, 8), Peer(2, 50, 50, 0)), 1);
            Check(node.Owner == 1, "movement requests after expiry can transfer the field normally");

            world = World(GridCoveragePattern.Rounded7x7, keep); node = Add(world, cell);
            world.Tick(Peers(a, b), 0); world.Tick(Peers(), .1f); world.Tick(Peers(), 4);
            Check(world.Grid.CachedCells == 0 && world.Grid.DeferredCells == 0 && node.Owner == 99,
                "fully empty fields retire despite cooldown; no new ownership-return job");
            world.Tick(Peers(a), 5);
            Check(node.Owner == 1, "recycled field policy has no old cooldown");
        }
        var w = World(GridCoveragePattern.Rounded7x7, true); var n = Add(w, cell);
        w.Tick(Peers(a, b), 0); w.Tick(Peers(a, away, Peer(3, 0, 2, 4)), .1f);
        w.Tick(Peers(a, away, Peer(3, 0, 2, 4)), 10); w.Tick(Peers(Peer(1, 0, 0, 8), away, Peer(3, 0, 2, 4)), 11);
        Check(n.Owner == 99 && n.Writes == 1, "third-player coverage recorded during lock survives expiry");
        w.Tick(Peers(Peer(1, 0, 0, 8), away), 12);
        Check(n.Owner == 1, "current three-player coverage yields correct owner after actual departure");

        w = World(GridCoveragePattern.Rounded7x7, true); n = Add(w, cell);
        w.Tick(Peers(a, b), 0); w.Grid.ConfigureOverlapCooldown(0); w.Tick(Peers(a, away), .1f);
        Check(n.Owner == 99, "live setting retains already-created acquisition cooldown");
        w.Tick(Peers(Peer(1, 0, 0, 8), away), 1);
        w.Tick(Peers(a, b), 2); w.Tick(Peers(a, away), 2.1f);
        Check(n.Owner == 1 && n.Writes == 4, "live zero disables future acquisition cooldowns");

        w = World(GridCoveragePattern.Rounded7x7, true); n = Add(w, cell);
        w.Tick(Peers(a, b), 10); w.Tick(Peers(a, away), 10.1f); w.Tick(Peers(a, away), 5);
        w.Tick(Peers(Peer(1, 0, 0, 8), away), 5.5f);
        Check(n.Owner == 99, "clock reset restarts a finite cooldown without an automatic return");
        w.Tick(Peers(a, away), 6); Check(n.Owner == 1, "post-reset expiry responds to actual activity");

        var full = new AuthorityWorld(99, 1, GridCoveragePattern.Square3x3, true, false, 0, 3, false, .2f, 20);
        var fullNode = Add(full, new Cell(0, 0)); full.Tick(Peers(a, Peer(2, 0, 0, 4)), 0);
        full.Tick(Peers(a, Peer(2, 0, 0, 4)), 1); full.Tick(Peers(a), 2); full.Tick(Peers(a), 3);
        Check(fullNode.Owner == 1, "full grid is unaffected");

        w = World(GridCoveragePattern.Rounded7x7, true); n = Add(w, new Cell(-1, 0));
        var adjacent = Peer(2, 0, 0, AuthorityGrid.HeadingCoverage(37));
        var boundary = Peer(2, 0, 0, AuthorityGrid.HeadingCoverage(38));
        w.Tick(Peers(a, adjacent), 0); w.Tick(Peers(a, boundary), .1f);
        w.Tick(Peers(a, adjacent), .2f); w.Tick(Peers(a, boundary), .3f); w.Tick(Peers(a, boundary), 10);
        Check(n.Owner == 99 && n.Writes == 1, "one-degree camera flicker stays on server after lock expiry until another event");
        w.Tick(Peers(Peer(1, 0, 0, 15), boundary), 11);
        Check(n.Owner == 1, "overlapping camera union can revisit the same-tier expired field");
    }
    public static int Main()
    {
        try { Matrix(); EventDrivenExpiry(); ScopeAndLifecycle(); LargeBucketWork(); Console.WriteLine("PASS: " + checks + " server-overlap cooldown, activity-only expiry, scope, coverage and lifecycle checks."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
