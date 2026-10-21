using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
#if PLAYER_SIMULATION
using TooManyLags.PlayerSimulation;
#else
using TooManyLags;
#endif

internal static class GridPriorityTests
{
    private static int checks;
    private sealed class Node : AuthorityNode
    {
        internal long Owner = 7;
        internal int Label, Calls;
        internal bool Ready = true, Throw;
        internal Action Callback;
        internal List<int> Log;
        internal override long CurrentOwner { get { return Owner; } }
        internal override ApplyResult Apply(long target)
        {
            Calls++; Log.Add(Label);
            if (Callback != null) Callback();
            if (Throw) throw new InvalidOperationException("expected test failure");
            if (!Ready) return ApplyResult.Pending;
            if (target != 0) Owner = target;
            Managed = true; ManagedOwner = Owner;
            return ApplyResult.Done;
        }
    }
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    private static AuthorityWorld World(bool keep)
    {
        var world = new AuthorityWorld(99, 2, GridCoveragePattern.Rounded7x7, keep, true, 0, 0, true, 0);
#if PLAYER_SIMULATION
        world.AfterGridUpdate = null;
#endif
        return world;
    }
    private static List<GridPeer> Peers(bool shared = false)
    {
        var peers = new List<GridPeer> { new GridPeer(1, new Cell(0, 0)) };
        if (shared) peers.Add(new GridPeer(2, new Cell(0, 0)));
        return peers;
    }
    private static Cell Position(int lane)
    { return lane == 0 ? new Cell(0, 0) : lane == 1 ? new Cell(0, 2) : lane == 2 ? new Cell(3, 0) : new Cell(20, 20); }
    private static Node Add(AuthorityWorld world, List<int> log, int lane)
    {
        var node = new Node { Label = lane, Cell = Position(lane), Log = log };
        world.Add(node, 0); return node;
    }
    private static void LanesEmpty(AuthorityWorld world)
    {
        var lanes = (IEnumerable)typeof(AuthorityWorld).GetField("work", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
        foreach (ICollection lane in lanes) Check(lane.Count == 0, "work snapshot releases node references after each pass");
    }
    private static void OrderingAndNoQuota()
    {
        foreach (bool keep in new[] { false, true }) foreach (bool shared in new[] { false, true })
        {
            var world = World(keep); var log = new List<int>();
            // Queue a dense background field first, deliberately opposite to
            // requested priority. No tier may acquire a new per-pass quota.
            for (int lane = 3; lane >= 0; lane--) for (int i = 0; i < 512; i++) Add(world, log, lane);
            world.Tick(Peers(shared), 0);
            Check(world.Grid.PriorityReads == 4, "2048 pending jobs evaluate priority once per field");
            Check(log.Count == 2048 && world.PendingCount == 0, "all 2048 due jobs run, including background jobs");
            for (int i = 0; i < log.Count; i++) Check(log[i] == i / 512, "red, forward, outer, vacated order is independent of insertion/owner");
            Check(world.Grid.WorkPriority(Position(0)) == 0, "red stays highest even with zero delays/shared ownership");
            Check(world.Grid.WorkPriority(Position(1)) == 1, "forward tier does not collapse into red when delay is zero");
            Check(world.Grid.WorkPriority(Position(2)) == 2, "outer tier precedes vacant work");
            Check(world.Grid.WorkPriority(Position(3)) == 3, "no live contributions is background work");
            LanesEmpty(world);
        }
    }
    private static void FieldBatchScope()
    {
        var world = World(false); var log = new List<int>();
        var first = Add(world, log, 0); first.Ready = false;
        var second = Add(world, log, 1); second.Ready = false;
        var again = Add(world, log, 0); again.Ready = false;
        first.Callback = delegate {
            Check(!first.IndexedBucket.PriorityCached && !second.IndexedBucket.PriorityCached,
                "field cache ends before native callbacks");
        };
        world.Tick(Peers(), 0);
        Check(world.Grid.PriorityReads == 2, "interleaved nodes share exactly one priority read per field");
        int before = world.Grid.PriorityReads;
        world.Tick(Peers(), .05f);
        Check(world.Grid.PriorityReads == before + 2, "blocked jobs use fresh priorities each pass");
        before = world.Grid.PriorityReads;
        Check(world.BatchPriority(first) == 0 && world.BatchPriority(second) == 1 && world.BatchPriority(again) == 0,
            "packet batch shares current field priorities across nonadjacent entries");
        Check(world.Grid.PriorityReads == before + 2, "packet batch evaluates each distinct field once");
        world.ClearPriorityBatch();
        Check(!first.IndexedBucket.PriorityCached && !second.IndexedBucket.PriorityCached,
            "packet batch releases flags and field references");
        first.Callback = null;
        world.Move(again, Position(2), .1f);
        Check(!ReferenceEquals(first.IndexedBucket, again.IndexedBucket), "mobile object attaches to its new field");
        world.Tick(new List<GridPeer> { new GridPeer(1, new Cell(20, 20)) }, .1f);
        Check(world.BatchPriority(first) == 3 && world.BatchPriority(again) == 3,
            "field changes cannot reuse stale red or forward priorities");
        world.ClearPriorityBatch();
        first.Throw = true;
        bool caught = false;
        try { world.Tick(Peers(), .2f); } catch (InvalidOperationException) { caught = true; }
        Check(caught && !first.IndexedBucket.PriorityCached && !again.IndexedBucket.PriorityCached,
            "native failure leaves no active priority cache");
        world.Remove(first); world.Remove(second); world.Remove(again);
        Check(first.IndexedBucket == null && second.IndexedBucket == null && again.IndexedBucket == null,
            "retired objects retain no field reference");
        Check(world.BucketCount == 0 && world.PendingCount == 0, "unload removes all indexed fields and jobs");
        var cached = (ICollection)typeof(AuthorityWorld).GetField("priorityBuckets", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
        Check(cached.Count == 0, "completed batches retain no unloaded field in the cache list");
    }
    private static void DeadlinesAndGuards()
    {
        var world = World(false); var log = new List<int>();
        world.Tick(Peers(), 0);
        var background = Add(world, log, 3);
        var outer = Add(world, log, 2);
        var forward = Add(world, log, 1);
        var future = Add(world, log, 0); future.NotBefore = 5;
        var guarded = Add(world, log, 0); guarded.Ready = false;
        world.Tick(Peers(), 0);
        Check(future.Calls == 0, "priority does not bypass a node deadline");
        Check(guarded.Calls == 1 && guarded.Owner == 7, "red still observes a blocking guard");
        Check(log.Count == 4 && log[0] == 0 && log[1] == 1 && log[2] == 2 && log[3] == 3, "blocked red does not stop lower tiers");
        Check(background.Calls == 1 && outer.Calls == 1 && forward.Calls == 1, "background is not starved by blocked foreground");
        guarded.Ready = true; world.Tick(Peers(), .05f);
        Check(guarded.Calls == 2 && world.PendingCount == 1, "guard resolution uses original next-pass cadence");
        world.Tick(Peers(), 5);
        Check(future.Calls == 1 && future.Owner == 1 && world.PendingCount == 0, "future red finishes at its existing deadline");
        LanesEmpty(world);
    }
    private static void ReturnChangesAndCallbacks()
    {
        var world = World(false); var log = new List<int>();
        var retired = Add(world, log, 3);
        world.Tick(Peers(), 0); log.Clear();
        world.Move(retired, Position(0), .1f);
        var stale = Add(world, log, 2);
        var trigger = Add(world, log, 0);
        Node added = null;
        trigger.Callback = delegate { world.Remove(stale); added = Add(world, log, 0); trigger.Callback = null; };
        world.Tick(Peers(), .1f);
        Check(retired.Owner == 1, "re-entered return work uses the current player field");
        Check(stale.Calls == 0 && stale.Removed, "callback retirement cannot execute a stale lower-priority snapshot");
        Check(added != null && added.Calls == 0 && world.PendingCount == 1, "callback-created jobs retain next-pass snapshot semantics");
        world.Tick(Peers(), .15f);
        Check(added.Calls == 1 && world.PendingCount == 0, "callback-created red job is not lost");
        world.Remove(retired); world.Remove(trigger); world.Remove(added);
        world.Tick(new List<GridPeer>(), 10);
        Check(world.BucketCount == 0 && world.PendingCount == 0, "unload leaves no object or pending cache");
        LanesEmpty(world);

        world = World(false); log = new List<int>();
        var broken = Add(world, log, 0); broken.Throw = true;
        Add(world, log, 3);
        bool caught = false;
        try { world.Tick(Peers(), 0); } catch (InvalidOperationException) { caught = true; }
        Check(caught, "native exception still reaches existing outer failure policy");
        LanesEmpty(world);
    }
    public static int Main()
    {
        try
        {
            OrderingAndNoQuota(); DeadlinesAndGuards(); ReturnChangesAndCallbacks(); FieldBatchScope();
            Console.WriteLine("PASS: " + checks + " grid priority, dense handoff, deadline, guard and callback checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
