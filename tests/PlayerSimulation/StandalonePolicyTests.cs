using System;
using System.Collections.Generic;
using TooManyLags.PlayerSimulation;

internal static class StandalonePolicyTests
{
    private static int checks;
    private static void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
    private static List<GridPeer> Peers(params GridPeer[] peers) { return new List<GridPeer>(peers); }
    private static GridPeer Player(long uid, int x = 0, int z = 0, float yaw = 0) { return new GridPeer(uid, new Cell(x, z), yaw); }
    private static void LatencyAndStanding()
    {
        var ping = new Dictionary<long, float> { { 1, 100 }, { 2, 20 }, { 3, 80 } };
        var grid = new AuthorityGrid(0, 0, GridCoveragePattern.Rounded7x7, true, 0, 3, true, true, .2f, delegate(long uid) { return ping[uid]; }, 30);
        var people = Peers(Player(1), Player(2), Player(3)); grid.Update(people, 0); grid.Update(people, 3);
        foreach (Cell cell in grid.CoveredCells) Check(grid.Owner(cell) == 2, "overlap uses the best player, never a server sentinel");
        int reads = grid.NeighborReads, updates = grid.PlayerUpdates;
        for (int i = 0; i < 10000; i++) grid.Update(people, 4 + i * .1f);
        Check(grid.NeighborReads == reads && grid.PlayerUpdates == updates, "stationary peers do not rebuild cells");
        ping[1] = 15; grid.RanksChanged(); grid.Update(people, 1005); Check(grid.Owner(new Cell(0, 0)) == 2, "small ping advantage keeps incumbent");
        ping[1] = 0; ping[2] = 80; grid.RanksChanged(); grid.Update(people, 1006); Check(grid.Owner(new Cell(0, 0)) == 1, "significant improvement changes candidate without movement");
        ping[3] = 40; people.RemoveAt(0); grid.Update(people, 1007); grid.Update(people, 1010); Check(grid.Owner(new Cell(0, 0)) == 3, "departure selects the best remaining contender");
        people.Clear(); grid.Update(people, 1011); Check(!grid.HasPeers && grid.CachedCells == 0, "vacated policies retire with keep ownership");
        var host = new AuthorityGrid(0, 0, GridCoveragePattern.Square3x3, false, 0, 3, true, false, .2f, delegate(long uid) { return uid == 99 ? 0 : 70; });
        host.Update(Peers(Player(99), Player(1)), 0); Check(host.Owner(new Cell(0, 0)) == 99, "listen-host session UID participates as a real player");
        var unknown = new AuthorityGrid(0, 0, GridCoveragePattern.Square3x3, false, 0, 3, true, false, .2f, delegate(long uid) { return Single.PositiveInfinity; });
        unknown.Update(Peers(Player(8), Player(5)), 0); Check(unknown.Owner(new Cell(0, 0)) == 5, "unknown ping is not zero and selection is deterministic");
        var extreme = new AuthorityGrid(0, 0, GridCoveragePattern.Square3x3, false, 0, 3, true, false, .2f, delegate(long uid) { return uid == Int64.MinValue ? 0 : 80; });
        extreme.Update(Peers(Player(Int64.MinValue), Player(Int64.MaxValue)), 0); Check(extreme.Owner(new Cell(0, 0)) == Int64.MinValue, "zero sentinel cannot exclude a valid signed native player UID");
    }
    private static void HandoffRaces()
    {
        for (ushort revision = 0; revision < 2000; revision++)
        {
            var state = new HandoffTransaction(revision + 1, 1, 2, revision, 0);
            Check(!state.Observe(1, revision), "unchanged source is pending");
            state.Prepared(false, 0); Check(state.Phase == HandoffPhase.Prepare && state.Next == .5f, "unready receiver cannot commit");
            state.Prepared(true, 1); Check(state.Phase == HandoffPhase.Commit, "ready receiver authorizes only the commit request");
            Check(!state.Observe(2, revision) && state.Phase == HandoffPhase.Commit, "owner half receipt stays pending");
            Check(!state.Observe(1, (ushort)(revision + 1)), "revision half receipt stays pending");
            state.Block(2); Check(state.Phase == HandoffPhase.Blocked, "busy source never grants ownership");
            state.Wake(3); Check(state.Phase == HandoffPhase.Prepare, "use end requires fresh receiver readiness");
            state.Prepared(true, 3); Check(state.Observe(2, (ushort)(revision + 1)) && state.Phase == HandoffPhase.Complete, "exact native receipt completes");
            var stale = new HandoffTransaction(revision + 5000, 1, 2, revision, 0);
            Check(stale.Observe(3, (ushort)(revision + 1)) && stale.Phase == HandoffPhase.Obsolete, "third-owner change invalidates old permission");
            stale.Prepared(true, 100); Check(stale.Phase == HandoffPhase.Obsolete, "late readiness cannot resurrect stale request");
        }
        bool overflow = false; try { new HandoffTransaction(1, 1, 2, UInt16.MaxValue, 0); } catch (ArgumentException) { overflow = true; }
        Check(overflow, "revision overflow is refused");
    }
    public static int Main()
    {
        try { LatencyAndStanding(); HandoffRaces(); Console.WriteLine("PASS: " + checks + " standalone latency, listen-host, standing-cache and handoff-race assertions."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
