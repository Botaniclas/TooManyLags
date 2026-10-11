using System;
using System.Collections.Generic;
#if PLAYER_SIMULATION
using TooManyLags.PlayerSimulation;
#else
using TooManyLags;
#endif

internal static class LiveGridTimingTests
{
    private static int checks;
    private static void Check(bool test, string message) { checks++; if (!test) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            Cell cell = new Cell(0, 0);
            List<GridPeer> peers = new List<GridPeer> { new GridPeer(11, cell) };
            AuthorityGrid grid = new AuthorityGrid(999, 2, GridCoveragePattern.Square3x3);
            grid.Update(peers, 0);
            Check(grid.Candidate(cell) == 11 && grid.Owner(cell) == 0 && grid.Remaining(cell, 0) == 2, "starts original request");
            int reads = grid.NeighborReads, updates = grid.PlayerUpdates;
            grid.ConfigureDelays(1, 0, .2f, 3);
            grid.Update(peers, .1f);
            Check(grid.Remaining(cell, .1f) > 1.8f && grid.Owner(cell) == 0, "retains pending deadline while stationary");
            Check(grid.NeighborReads == reads && grid.PlayerUpdates == updates, "command does not rebuild stationary coverage");
            grid.Update(peers, 2);
            Check(grid.Owner(cell) == 11, "original request completes normally");
            grid.Remember(cell, 11); grid.ConfigureDelays(3, 0, .2f, 3);
            Check(grid.IsRemembered(cell, 11), "retains completed-field receipt");
            Cell next = new Cell(4, 0); peers[0] = new GridPeer(11, next);
            grid.Update(peers, 2.1f);
            Check(grid.Remaining(next, 2.1f) > 2.9f, "new request uses changed delay");
            AuthorityGrid directional = new AuthorityGrid(999, 2, GridCoveragePattern.Rounded7x7, true, 0, 3, true, true, .2f);
            directional.Update(new List<GridPeer> { new GridPeer(11, cell) }, 0);
            Check(directional.Owner(cell) == 11, "inner field starts immediately");
            directional.ConfigureDelays(2, 1, .5f, 4);
            Check(directional.Wait(cell) == 1 && directional.Owner(cell) == 11, "inner change preserves settled owner");
            Cell far = new Cell(10, 0);
            directional.Update(new List<GridPeer> { new GridPeer(11, far) }, 1);
            Check(directional.Owner(far) == 0 && directional.Remaining(far, 1) == 1, "new directional inner request uses live delay");
            Console.WriteLine("PASS: " + checks + " live grid timing/cache/pending deadline checks."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
