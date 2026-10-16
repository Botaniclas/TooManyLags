using System;
using System.Collections.Generic;
using TooManyLagsStandalone;

internal static class GridTimingConfigTests
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }
    public static int Main()
    {
        try
        {
            var grid = new AuthorityGrid(0, 2f);
            var peers = new List<GridPeer> { new GridPeer(11, new Cell(0, 0)) };
            var centre = new Cell(0, 0);
            grid.Update(peers, 0f);
            Check(grid.Owner(centre) == 0, "Original full-grid request waits for its delay.");
            grid.ConfigureDelays(.2f, 0f, .2f, 3f);
            grid.Update(peers, .3f);
            Check(grid.Owner(centre) == 0, "Live timing does not truncate a pending field deadline.");
            grid.Update(peers, 2f);
            Check(grid.Owner(centre) == 11, "Original request completes at its original deadline.");
            peers[0] = new GridPeer(11, new Cell(20, 0));
            grid.Update(peers, 3f);
            Check(grid.Owner(new Cell(20, 0)) == 0, "New field still observes its updated delay.");
            grid.Update(peers, 3.21f);
            Check(grid.Owner(new Cell(20, 0)) == 11, "New field uses the live delay.");
            int updates = grid.PlayerUpdates, reads = grid.NeighborReads;
            for (int i = 0; i < 10000; i++) grid.Update(peers, 4f + i * .05f);
            Check(grid.PlayerUpdates == updates && grid.NeighborReads == reads, "Standing after live tuning adds no field requests or neighbour reads.");
            Console.WriteLine("PASS: " + checks + " live field-timer preservation and standing-work assertions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
