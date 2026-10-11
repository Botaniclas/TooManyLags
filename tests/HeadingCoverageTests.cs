using System;
using System.Collections.Generic;
#if PLAYER_SIMULATION
using TooManyLags.PlayerSimulation;
#else
using TooManyLags;
#endif

internal static class HeadingCoverageTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static List<GridPeer> Input(float yaw, long uid = 1)
    { return new List<GridPeer> { GridPeer.Covered(uid, new Cell(0, 0), AuthorityGrid.HeadingCoverage(yaw)) }; }
    private static AuthorityGrid Grid(GridCoveragePattern pattern, bool forward)
    { return new AuthorityGrid(99, 2, pattern, true, 0, 3, true, forward, .2f); }
    public static int Main()
    {
        try
        {
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
