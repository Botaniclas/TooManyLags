using System;
using System.Collections.Generic;
#if PLAYER_SIMULATION
using TooManyLags.PlayerSimulation;
#else
using TooManyLags;
#endif

internal static class GridSchedulingTests
{
    private static int checks;
    private static readonly Cell Outer = new Cell(2, 0);
    private sealed class Node : AuthorityNode
    {
        internal long Owner = 7;
        internal int Calls;
        internal bool Ready = true;
        internal Action OnApply;
        internal override long CurrentOwner { get { return Owner; } }
        internal override ApplyResult Apply(long target)
        {
            Calls++; if (OnApply != null) OnApply();
            if (!Ready) return ApplyResult.Pending;
            if (target != 0) { Owner = target; Managed = true; ManagedOwner = target; }
            return ApplyResult.Done;
        }
    }
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    private static AuthorityWorld World(bool keep = true)
    { return new AuthorityWorld(99, 2, GridCoveragePattern.Rounded7x7, keep, true, 0, 3, true, .2f); }
    private static List<GridPeer> Peers(int x = 0)
    { return new List<GridPeer> { new GridPeer(1, new Cell(x, 0)) }; }
    private static Node Settled(AuthorityWorld world, List<GridPeer> peers)
    {
        var node = new Node { Cell = Outer }; world.Add(node, 0);
        world.Tick(peers, 0); world.Tick(peers, 3); return node;
    }
    private static void FutureOnly(bool keep)
    {
        var world = World(keep); var peers = Peers(); var nodes = new List<Node>();
        for (int i = 0; i < 1000; i++) { var node = new Node { Cell = Outer }; nodes.Add(node); world.Add(node, 0); }
        world.Tick(peers, 0); world.Tick(peers, 3);
        foreach (Node node in nodes) world.Changed(node, 4);
        int attempts = world.Attempts, visits = world.PendingVisits;
        for (int poll = 0; poll < 59; poll++) world.Tick(peers, 4 + poll * .05f);
        Check(world.Attempts == attempts && world.PendingVisits == visits && world.PendingCount == 1000,
            "future-only jobs neither run nor traverse their snapshot");
        world.Tick(peers, 7);
        Check(world.Attempts == attempts + 1000 && world.PendingCount == 0, "every due job runs at its original deadline");
        visits = world.PendingVisits;
        for (int poll = 0; poll < 1000; poll++) world.Tick(peers, 8 + poll * .05f);
        Check(world.PendingVisits == visits, "settled work remains idle");
        foreach (Node node in nodes) world.Remove(node);
        Check(world.PendingCount == 0 && world.BucketCount == 0 && world.Grid.RememberedCells == 0,
            "unload releases all node and certificate references");
        Console.WriteLine("FUTURE keep=" + keep + ": 1000 jobs, 59 polls, 0 pending visits before due; 1000 due jobs completed.");
    }
    private static void ReceiptsAndReplies()
    {
        var world = World(); var peers = Peers(); var node = Settled(world, peers);
        world.Changed(node, 4); world.Tick(peers, 4); int calls = node.Calls, visits = world.PendingVisits;
        world.Received(node, 4.1f); world.Tick(peers, 4.1f);
        Check(node.Calls == calls && node.NotBefore == 7 && world.PendingVisits == visits,
            "ordinary receipt preserves an existing residence barrier");
        world.Advance(node, 4.2f); world.Tick(peers, 4.2f);
        Check(node.Calls == calls + 1 && world.PendingCount == 0, "ready reply wakes the job before its previous deadline");
        world.Advance(node, 4.3f); world.Tick(peers, 4.3f);
        Check(node.Calls == calls + 1, "reply cannot recreate completed work");
        world.Received(node, 4.4f); world.Tick(peers, 4.4f);
        Check(node.Calls == calls + 2, "a new real receipt wakes new maintenance immediately");
        world.Changed(node, 5); world.Tick(peers, 5); world.Dismiss(node);
        world.Advance(node, 5.1f); world.Tick(peers, 5.1f);
        Check(world.PendingCount == 0 && node.Calls == calls + 2, "discarded work remains discarded after a late reply");
        world.Remove(node); world.Advance(node, 5.2f); world.Tick(peers, 5.2f);
        Check(world.PendingCount == 0 && world.BucketCount == 0, "reply cannot resurrect an unloaded node");
    }
    private static void AccelerationAndNewWork()
    {
        var world = World(); var peers = Peers(); var node = Settled(world, peers);
        world.Changed(node, 4); world.Tick(peers, 4); int calls = node.Calls;
        world.Tick(Peers(1), 4.1f);
        Check(node.Calls == calls + 1 && world.PendingCount == 0, "outer-to-red acceleration bypasses the future-only gate");

        world = World(); peers = Peers(); node = Settled(world, peers);
        world.Changed(node, 4); world.Tick(peers, 4); calls = node.Calls;
        var fresh = new Node { Cell = new Cell(0, 0) }; world.Add(fresh, 4.1f); world.Tick(peers, 4.1f);
        Check(fresh.Calls == 1 && node.Calls == calls && world.PendingCount == 1, "new due work wakes without executing future jobs");
        world.Remove(node); world.Tick(peers, 4.2f);
        Check(world.PendingCount == 0, "removing the last waiting node retires its gate");
        world.Changed(fresh, 5); world.Tick(peers, 5);
        Check(fresh.Calls == 2, "fresh work is not held by a removed node's deadline");
    }
    private static void GuardsAndCallbacks()
    {
        var world = World(); var peers = Peers(); var blocked = Settled(world, peers);
        blocked.Ready = false; world.Changed(blocked, 4); world.Tick(peers, 7); int calls = blocked.Calls;
        for (int poll = 1; poll <= 100; poll++) world.Tick(peers, 7 + poll * .05f);
        Check(blocked.Calls == calls + 100, "due but blocked handoffs retain every existing retry");
        blocked.Ready = true; world.Tick(peers, 12.1f);
        Check(world.PendingCount == 0 && blocked.Calls == calls + 101, "guard release progresses at the next existing pass");

        world = World(); peers = Peers(); var parent = new Node { Cell = new Cell(0, 0) };
        var child = new Node { Cell = Outer }; world.Add(child, 0, false); world.Add(parent, 0);
        parent.OnApply = delegate { parent.OnApply = null; world.Changed(child, 0); };
        world.Tick(peers, 0); int visits = world.PendingVisits;
        world.Tick(peers, .1f);
        Check(child.Calls == 0 && world.PendingCount == 1 && world.PendingVisits == visits,
            "callback-queued future work retains its deadline after the active snapshot");
        world.Tick(peers, 3); Check(child.Calls == 1 && world.PendingCount == 0, "callback-queued work eventually completes");
        child.Ready = false; child.OnApply = delegate { world.Remove(child); };
        world.Changed(child, 4); world.Tick(peers, 7); world.Tick(peers, 8);
        Check(child.Removed && world.PendingCount == 0, "a pending return cannot retain a node removed inside its callback");
    }
    private static void ClockAndLiveTiming()
    {
        var world = World(); var peers = Peers(); var node = Settled(world, peers);
        world.Changed(node, 4); world.Tick(peers, 4); int calls = node.Calls;
        world.Tick(peers, 1); world.Tick(peers, 3.99f);
        Check(node.Calls == calls && node.NotBefore == 4, "clock rollback rebases both the barrier and gate");
        world.Tick(peers, 4); Check(node.Calls == calls + 1, "rollback cannot strand a future node");

        world = World(); peers = Peers(); node = Settled(world, peers);
        world.Changed(node, 4); world.Tick(peers, 4); calls = node.Calls;
        world.Grid.ConfigureDelays(2, 0, .2f, 0);
        world.Tick(peers, 4.1f); world.Tick(peers, 6.99f);
        Check(node.Calls == calls && node.NotBefore == 7, "live tuning does not shorten already scheduled work");
        world.Tick(peers, 7); world.Changed(node, 8); world.Tick(peers, 8);
        Check(node.Calls == calls + 2, "subsequent work uses live delay settings");
    }
#if PLAYER_SIMULATION
    private static void InterestCallbackWake()
    {
        var world = World(); var peers = Peers(); var node = Settled(world, peers);
        world.Changed(node, 4); world.Tick(peers, 4); int calls = node.Calls;
        world.AfterGridUpdate = delegate { world.AfterGridUpdate = null; world.Advance(node, 4.1f); };
        world.Tick(peers, 4.1f);
        Check(node.Calls == calls + 1 && world.PendingCount == 0, "interest-sync callback wakes work before the gate is tested");
    }
#endif
    private static void PoolLifetime(bool keep)
    {
        var world = World(keep); var peers = new List<GridPeer>();
        for (int visit = 0; visit < 2000; visit++)
        {
            float now = visit * 8; peers.Clear();
            for (int player = 1; player <= 12; player++)
                peers.Add(GridPeer.Covered(player, new Cell((visit % 180) * 20 + player * 8, (visit / 180) * 20), visit % AuthorityGrid.CoverageCount));
            world.Tick(peers, now); world.Tick(peers, now + 3);
            Check(world.Grid.CachedCells <= 12 * 98 && world.Grid.ReusablePolicies <= 1024, "only active coverage and a capped pool remain");
            peers.Clear(); world.Tick(peers, now + 4); world.Tick(peers, now + 7);
            Check(world.Grid.CachedCells == 0 && world.Grid.DeferredCells == 0 && world.Grid.RememberedCells == 0,
                "retired policies retain no visited cell, deferred state or certificate");
        }
        Check(world.Grid.PolicyAllocations <= 12 * 98, "policy allocation is bounded independently of travel and exit mode");
        peers.Add(new GridPeer(81, new Cell(0, 0))); peers.Add(new GridPeer(82, new Cell(0, 0)));
        world.Tick(peers, 16000); world.Tick(peers, 16003); peers.Clear();
        world.Tick(peers, 16004); world.Tick(peers, 16007); peers.Add(new GridPeer(83, new Cell(0, 0)));
        world.Tick(peers, 16008);
        Check(world.Grid.Owner(new Cell(0, 0)) == 83 && world.Grid.Candidate(Outer) == 83 && world.Grid.Owner(Outer) == 0,
            "reused overlap storage cannot retain its owners, shared flags or timers");
        world.Tick(peers, 16011); Check(world.Grid.Owner(Outer) == 83, "reused outer policy waits for its new deadline");
        Console.WriteLine("POOL keep=" + keep + ": 2000 areas / 12 players, allocations=" + world.Grid.PolicyAllocations + "; cap=1024.");
    }
    public static void Main()
    {
        FutureOnly(true); FutureOnly(false); ReceiptsAndReplies(); AccelerationAndNewWork();
        GuardsAndCallbacks(); ClockAndLiveTiming(); PoolLifetime(true); PoolLifetime(false);
#if PLAYER_SIMULATION
        InterestCallbackWake();
#endif
        Console.WriteLine("PASS: " + checks + " scheduling, readiness wake, callback, clock, pool and lifecycle checks.");
    }
}
