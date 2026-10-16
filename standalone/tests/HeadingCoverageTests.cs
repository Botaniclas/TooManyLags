using System;
using System.Collections.Generic;
using TooManyLagsStandalone;

internal static class HeadingCoverageTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static List<GridPeer> Input(float yaw, long uid = 1)
    { return new List<GridPeer> { GridPeer.Covered(uid, new Cell(0, 0), AuthorityGrid.HeadingCoverage(yaw)) }; }
    private static AuthorityGrid Grid(GridCoveragePattern pattern, bool forward)
    { return new AuthorityGrid(99, 2, pattern, true, 0, 3, true, forward, .2f); }
    private static void ConfigurableAngles()
    {
        foreach (float overlap in new[] { 0f, .1f, 10f, 30f, 44.9f, 45f })
        for (float yaw = -720; yaw <= 720; yaw += .5f)
        {
            int coverage = AuthorityGrid.HeadingCoverage(yaw, overlap), count = 0;
            float half = (45f + overlap) * .5f;
            for (int heading = 0; heading < 8; heading++)
            {
                float delta = ((yaw - heading * 45f) % 360f + 540f) % 360f - 180f;
                bool expected = overlap == 0f || overlap == 45f ? delta >= -half && delta < half : Math.Abs(delta) <= half;
                bool actual = coverage < 8 ? coverage == heading : coverage - 8 == heading || ((coverage - 7) & 7) == heading;
                Check(actual == expected, "independent configurable-angle membership, including endpoints and wrap");
                if (actual) count++;
            }
            Check(count >= 1 && count <= 2 && (overlap != 0f || count == 1) && (overlap != 45f || count == 2), "0 selects one and 45 selects two, with no missing/third direction");
        }
        foreach (float bad in new[] { Single.NaN, Single.PositiveInfinity })
            Check(AuthorityGrid.HeadingCoverage(20, bad) == AuthorityGrid.HeadingCoverage(20, 30), "invalid angle retains default");
        Check(AuthorityGrid.HeadingCoverage(20, -1) == AuthorityGrid.HeadingCoverage(20, 0), "negative overlap clamps to zero");
        Check(AuthorityGrid.HeadingCoverage(0, 90) == AuthorityGrid.HeadingCoverage(0, 45), "larger overlap clamps to two directions");
    }
    private static void TripleCoverage()
    {
        foreach (GridCoveragePattern pattern in Enum.GetValues(typeof(GridCoveragePattern))) foreach (bool forward in new[] { false, true })
        for (int heading = 0; heading < 8; heading++)
        {
            var parts = new[] { Grid(pattern, forward), Grid(pattern, forward), Grid(pattern, forward) };
            for (int i = 0; i < 3; i++) parts[i].Update(new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), (heading + i) & 7) }, 0);
            var combined = Grid(pattern, forward);
            var input = new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), 16 + heading) };
            combined.Update(input, 0); int cells = 0;
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
            {
                var cell = new Cell(x, z); bool covered = false; float delay = Single.PositiveInfinity;
                foreach (var part in parts) if (part.Candidate(cell) != 0) { covered = true; delay = Math.Min(delay, part.Wait(cell)); }
                Check(combined.Candidate(cell) == (covered ? 1 : 0), "predictive triple has one request per player, never a false overlap");
                if (!covered) continue;
                cells++; Check(combined.Wait(cell) == delay, "predictive union retains fastest field tier");
                Check(combined.Owner(cell) == (delay == 0 ? 1 : 0), "predictive inner tier remains immediate");
            }
            Check(combined.CachedCells == cells, "predictive fields are deduplicated");
            combined.Update(input, 3); int updates = combined.PlayerUpdates, reads = combined.NeighborReads, visits = combined.TimerVisits;
            for (int tick = 1; tick <= 100; tick++) combined.Update(input, 3 + tick * .05f);
            Check(combined.PlayerUpdates == updates && combined.NeighborReads == reads && combined.TimerVisits == visits, "unchanged predictive coverage has no repeated field work");
            input.Add(GridPeer.Covered(2, new Cell(0, 0), 16 + heading)); combined.Update(input, 9); combined.Update(input, 12);
            Check(combined.Owner(new Cell(0, 0)) == 99, "only a real second player creates server ownership");
            input.Clear(); combined.Update(input, 13); combined.Update(input, 16);
            Check(combined.CachedCells == 0 && combined.RememberedCells == 0, "predictive masks retain no departed world cells");
        }
    }
    private static void RedPredictionCoverage()
    {
        foreach (GridCoveragePattern pattern in Enum.GetValues(typeof(GridCoveragePattern)))
        foreach (bool forward in new[] { false, true }) foreach (float inner in new[] { 0f, .7f })
        for (int actual = 0; actual < 16; actual++) for (int side = 0; side < 2; side++)
        {
            int first = actual & 7, extra = side == 0 ? (first + (actual < 8 ? 1 : 2)) & 7 : (first + 7) & 7;
            var baseline = new AuthorityGrid(99, 2, pattern, true, inner, 3, true, forward, .2f);
            var predicted = new AuthorityGrid(99, 2, pattern, true, inner, 3, true, forward, .2f);
            var addedDirection = Grid(pattern, forward);
            baseline.Update(new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), actual) }, 0);
            addedDirection.Update(new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), extra) }, 0);
            var input = new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), 24 + side * 16 + actual) };
            predicted.Update(input, 0); int cells = 0;
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
            {
                var cell = new Cell(x, z);
                bool existing = baseline.Candidate(cell) != 0, added = addedDirection.Candidate(cell) != 0;
                Check(predicted.Candidate(cell) == (existing || added ? 1 : 0), "red forecast union counts the player once");
                if (!existing && !added) continue;
                cells++; float delay = existing ? baseline.Wait(cell) : inner;
                Check(predicted.Wait(cell) == delay, "actual tiers stay unchanged; only new forecast cells use configured inner delay");
                Check(predicted.Owner(cell) == (delay == 0 ? 1 : 0), "red forecast is immediate exactly when inner delay is zero");
            }
            Check(predicted.CachedCells == cells, "red forecast deduplicates fields");
            predicted.Update(input, 3);
            int updates = predicted.PlayerUpdates, reads = predicted.NeighborReads, visits = predicted.TimerVisits;
            for (int tick = 1; tick <= 100; tick++) predicted.Update(input, 3 + tick * .05f);
            Check(predicted.PlayerUpdates == updates && predicted.NeighborReads == reads && predicted.TimerVisits == visits, "stable red forecast performs no repeated cell work");
            input.Add(GridPeer.Covered(2, new Cell(0, 0), 24 + side * 16 + actual));
            predicted.Update(input, 9); predicted.Update(input, 12);
            Check(predicted.Owner(new Cell(0, 0)) == 99, "red forecast keeps real-player overlap ownership");
            input.Clear(); predicted.Update(input, 13); predicted.Update(input, 16);
            Check(predicted.CachedCells == 0 && predicted.RememberedCells == 0, "red forecast clears departed world cells");
        }
        for (int coverage = 24; coverage < AuthorityGrid.CoverageCount; coverage++)
        {
            var full = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7, false, 0, 3, true);
            full.Update(new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), coverage) }, 0);
            Check(full.CachedCells == 37 && full.Wait(new Cell(0, 0)) == 2, "prediction leaves nondirectional full-grid geometry and delay unchanged");
        }
    }
    private static void FullRearCoverage()
    {
        foreach (GridCoveragePattern pattern in Enum.GetValues(typeof(GridCoveragePattern)))
        for (int heading = 0; heading < 8; heading++)
        {
            // Exercise production KeepOwnershipOnCellExit wiring, not a test-only flag.
            var world = new AuthorityWorld(99, 2, pattern, false, true, 0, 3, true, .2f);
            var grid = world.Grid;
            grid.Update(new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), heading) }, 0);
            int radius = pattern == GridCoveragePattern.Rounded7x7 ? 3 : pattern == GridCoveragePattern.Rounded5x5 ? 2 : 1;
            int expected = radius == 3 ? 37 : radius == 2 ? (heading % 2 == 0 ? 21 : 22) : 9;
            Check(grid.CachedCells == expected, "keep=false includes the complete rounded footprint, including the existing forward diagonal tip");
            for (int x = -radius; x <= radius; x++) for (int z = -radius; z <= radius; z++)
            {
                var cell = new Cell(x, z);
                bool inside = radius == 1 || x * x + z * z <= radius * (radius + 1);
                if (inside) Check(grid.Candidate(cell) == 1, "keep=false cuts no rear field at any direction");
                if (Math.Abs(x) <= 1 && Math.Abs(z) <= 1)
                    Check(grid.Wait(cell) == 0 && grid.Owner(cell) == 1, "keep=false keeps the entire inner 3x3 red, including behind");
                if (heading == 0 && inside)
                {
                    float delay = Math.Abs(x) <= 1 && Math.Abs(z) <= 1 ? 0 :
                        radius > 1 && z == 2 && Math.Abs(x) <= 1 ? .2f : 3;
                    Check(grid.Wait(cell) == delay, "north-facing timers match the sketch: red 3x3, orange row ahead, yellow remainder");
                }
            }
            var retainedWorld = new AuthorityWorld(99, 2, pattern, true, true, 0, 3, true, .2f);
            retainedWorld.Grid.Update(new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), 0) }, 0);
            Check(retainedWorld.Grid.Candidate(new Cell(0, -1)) == 0, "keep=true retains the requested rear cutout");
            grid.Update(new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), heading) }, 3);
            int updates = grid.PlayerUpdates, reads = grid.NeighborReads, visits = grid.TimerVisits;
            for (int tick = 1; tick <= 100; tick++)
                grid.Update(new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), heading) }, 3 + tick * .05f);
            Check(grid.PlayerUpdates == updates && grid.NeighborReads == reads && grid.TimerVisits == visits, "complete rear coverage adds no stationary field work");
        }
    }
    public static int Main()
    {
        try
        {
            ConfigurableAngles();
            for (int heading = 0; heading < 8; heading++) foreach (int sign in new[] { -1, 1 }) foreach (int wrap in new[] { -720, 0, 720 })
            {
                float start = heading * 45 + wrap;
                int pair = 8 + (sign > 0 ? heading : (heading + 7) & 7);
                Check(AuthorityGrid.HeadingCoverage(start) == heading, "direction center uses one mask");
                Check(AuthorityGrid.HeadingCoverage(start + sign * 7.49f) == heading, "one mask before next direction becomes visible");
                Check(AuthorityGrid.HeadingCoverage(start + sign * 7.5f) == pair, "new direction joins at 7.5 in either direction");
                Check(AuthorityGrid.HeadingCoverage(start + sign * 22.5f) == pair, "both directions at original boundary");
                Check(AuthorityGrid.HeadingCoverage(start + sign * 37.5f) == pair, "old direction retained through 37.5");
                Check(AuthorityGrid.HeadingCoverage(start + sign * 37.51f) == ((heading + sign + 8) & 7), "old direction leaves after 37.5 including wrap");
            }
            Check(AuthorityGrid.HeadingCoverage(Single.NaN) == -1 && AuthorityGrid.HeadingCoverage(Single.PositiveInfinity) == -1, "invalid heading rejected");
            for (float yaw = -720; yaw <= 720; yaw += .5f)
            {
                int coverage = AuthorityGrid.HeadingCoverage(yaw), count = 0;
                for (int heading = 0; heading < 8; heading++)
                {
                    float delta = ((yaw - heading * 45f) % 360f + 540f) % 360f - 180f;
                    bool expected = Math.Abs(delta) <= 37.5f;
                    bool actual = coverage < 8 ? coverage == heading : (coverage - 8) == heading || ((coverage - 7) & 7) == heading;
                    Check(actual == expected, "independent +/-37.5 degree membership reference, including wrap and fast jumps");
                    if (actual) count++;
                }
                Check(count >= 1 && count <= 2, "no gap or third direction");
            }
            foreach (GridCoveragePattern pattern in Enum.GetValues(typeof(GridCoveragePattern))) foreach (bool forward in new[] { false, true })
            {
                int maximum = 0;
                for (int heading = 0; heading < 8; heading++)
                {
                    var left = Grid(pattern, forward); var right = Grid(pattern, forward); var union = Grid(pattern, forward);
                    left.Update(new List<GridPeer> { new GridPeer(1, new Cell(0, 0), heading * 45f) }, 0);
                    right.Update(new List<GridPeer> { new GridPeer(1, new Cell(0, 0), ((heading + 1) & 7) * 45f) }, 0);
                    var input = new List<GridPeer> { GridPeer.Covered(1, new Cell(0, 0), 8 + heading) }; union.Update(input, 0); int cells = 0;
                    for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
                    {
                        Cell cell = new Cell(x, z); bool a = left.Candidate(cell) != 0, b = right.Candidate(cell) != 0;
                        Check(union.Candidate(cell) == (a || b ? 1 : 0), "union has each cell exactly once, never a false second player");
                        if (!a && !b) continue;
                        cells++; float delay = a && b ? Math.Min(left.Wait(cell), right.Wait(cell)) : a ? left.Wait(cell) : right.Wait(cell);
                        Check(union.Wait(cell) == delay, "inner/forward priorities retained");
                        Check(union.Owner(cell) == (delay == 0 ? 1 : 0), "zero-delay union remains immediate");
                    }
                    Check(union.CachedCells == cells, "union stores no duplicate policies"); maximum = Math.Max(maximum, cells);
                    union.Update(input, 3); int updates = union.PlayerUpdates, reads = union.NeighborReads, visits = union.TimerVisits;
                    for (int tick = 1; tick <= 1000; tick++) union.Update(input, 3 + tick * .05f);
                    Check(union.PlayerUpdates == updates && union.NeighborReads == reads && union.TimerVisits == visits, "stationary union performs no cell or timer work after deadlines");
                    input.Add(GridPeer.Covered(2, new Cell(0, 0), 8 + heading)); union.Update(input, 54); union.Update(input, 57);
                    Check(union.Owner(new Cell(0, 0)) == 99, "a real second player still creates server overlap");
                    input.RemoveAt(1); union.Update(input, 58);
                    Check(union.Owner(new Cell(0, 0)) == 1, "remaining inner field returns normally");
                    input.Clear(); union.Update(input, 59); union.Update(input, 62);
                    Check(!union.HasPeers && union.CachedCells == 0 && union.RememberedCells == 0, "departed coverage releases all world-cell state");
                }
                Console.WriteLine("Coverage limit: " + pattern + "; forward=" + forward + "; adjacent union=" + maximum + " cells.");
            }
            TripleCoverage();
            RedPredictionCoverage();
            FullRearCoverage();
            var transitions = Grid(GridCoveragePattern.Rounded7x7, true);
            transitions.Update(Input(0), 0); transitions.Update(Input(7.49f), .1f);
            Check(transitions.PlayerUpdates == 1, "no work within unchanged single coverage");
            transitions.Update(Input(7.5f), .2f); int readsAtEntry = transitions.NeighborReads;
            transitions.Update(Input(22.5f), .3f); transitions.Update(Input(37.5f), .4f);
            Check(transitions.PlayerUpdates == 2 && transitions.NeighborReads == readsAtEntry, "middle of overlap makes no new field request");
            transitions.Update(Input(37.51f), .5f);
            Check(transitions.PlayerUpdates == 3, "leaving overlap is one real update");
            transitions.Update(Input(37.5f), .6f); transitions.Update(Input(7.5f), .7f);
            Check(transitions.PlayerUpdates == 4, "reverse turn retains same symmetric overlap");
            transitions.Update(Input(7.49f), .8f);
            Check(transitions.PlayerUpdates == 5, "reverse turn retires outside widened sector");
            transitions.Update(Input(200), .9f);
            Check(transitions.PlayerUpdates == 6, "large fast turn applies directly without stepping through intermediate headings");
            Console.WriteLine("PASS: " + checks + " +/-37.5-degree coverage, 30-degree overlap, union timing, real-player overlap, idle-cost and unload assertions."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
