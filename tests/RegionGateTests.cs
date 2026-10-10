using System;
using System.Collections.Generic;
using TooManyLags;

internal static class RegionGateTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static RegionPeer Peer(long uid, int x, int z)
    { return new RegionPeer(uid, new Cell(x, z), true, new Cell(x, z), true); }
    private static bool NativeArea(int x, int z, int cx, int cz, int mode)
    {
        int dx = x - cx * 64, dz = z - cz * 64, limit = mode == 0 ? 64 : 96;
        return Math.Abs(dx) <= limit && Math.Abs(dz) <= limit && (mode != 2 || dx * dx + dz * dz < 112 * 112);
    }
    private static void Bounds()
    {
        var gate = new RegionContentionGate();
        var peers = new List<RegionPeer> { Peer(1, 0, 0), Peer(2, 0, 0) };
        for (int dx = -6; dx <= 6; dx++) for (int dz = -6; dz <= 6; dz++)
        {
            peers[1] = Peer(2, dx, dz); gate.Update(peers);
            for (int mode = 0; mode < 3; mode++) for (int x = -96; x <= 96; x += 16) for (int z = -96; z <= 96; z += 16)
            {
                if (NativeArea(x, z, 0, 0, mode) && NativeArea(x, z, dx, dz, mode))
                    Check("actual native active-area intersection never bypassed", gate.Shared(1) && gate.Shared(2));
                foreach (int size in new[] { 4, 8, 16 })
                {
                    Cell cell; Check("native sample maps to valid fine cell", Cell.At(x, z, size, out cell));
                    if (gate.Shared(1) && NativeArea(x, z, 0, 0, mode))
                        Check("shared native samples stay managed including boundary", gate.Contains(cell, size));
                }
            }
        }
        peers[0] = Peer(1, -100, -100); peers[1] = Peer(2, -99, -99); gate.Update(peers);
        foreach (int size in new[] { 4, 8, 16 }) for (int x = -96; x <= 96; x += 4) for (int z = -96; z <= 96; z += 4)
        {
            Cell cell; Cell.At(-6400 + x, -6400 + z, size, out cell);
            Check("negative subdivision rounds down at region boundaries", gate.Contains(cell, size));
        }
        foreach (int size in new[] { 4, 8, 16 }) foreach (GridCoveragePattern pattern in new[] { GridCoveragePattern.Square3x3, GridCoveragePattern.Rounded5x5, GridCoveragePattern.Rounded7x7 })
        {
            if (size == 4 && pattern == GridCoveragePattern.Square3x3) continue;
            foreach (float edge in new[] { -31.99f, 31.99f }) for (int heading = 0; heading < 8; heading++)
            {
                peers[0] = Peer(1, 0, 0); peers[1] = Peer(2, 1, 1); gate.Update(peers);
                Cell center; Cell.At(edge, edge, size, out center);
                var grid = new AuthorityGrid(99, 0, pattern, true, 0, 0, true, size == 4, 0);
                grid.Update(new List<GridPeer> { new GridPeer(1, center, heading * 45) }, 0);
                foreach (Cell cell in grid.Changes) Check("every supported directional footprint is inside conservative character box", gate.Contains(cell, size));
            }
        }
    }
    private static void Lifecycle()
    {
        var gate = new RegionContentionGate(); var peers = new List<RegionPeer>();
        Check("initial empty gate needs no rebuild", !gate.Update(peers));
        peers.Add(Peer(1, 0, 0)); gate.Update(peers); Check("one player native", !gate.Shared(1) && gate.CachedZones == 0);
        peers.Add(Peer(2, 20, 20)); gate.Update(peers); Check("far regions native", gate.SharedPlayers == 0 && gate.CachedZones == 0);
        peers[1] = Peer(2, 1, 0); gate.Update(peers); Check("adjacent zones participate", gate.SharedPlayers == 2);
        int builds = gate.Rebuilds, pairs = gate.PairTests;
        for (int i = 0; i < 100000; i++) Check("unchanged regions never repeat pairs", !gate.Update(peers));
        Check("no standing pair evaluations", gate.Rebuilds == builds && gate.PairTests == pairs);
        peers[1] = new RegionPeer(2, new Cell(20, 20), true, new Cell(1, 0), true); gate.Update(peers);
        Check("teleporting character/reference mismatch stays conservative", gate.Shared(1) && gate.Shared(2));
        peers[1] = new RegionPeer(2, new Cell(1, 0), true, default(Cell), false); gate.Update(peers);
        Check("ready loading peer still considered", gate.SharedPlayers == 2);
        peers[1] = new RegionPeer(2, default(Cell), false, new Cell(20, 20), true); gate.Update(peers);
        Check("invalid reference cannot prove absence", gate.Shared(1) && gate.Shared(2) && gate.Contains(new Cell(800, 800), 8));
        peers.Clear(); gate.Update(peers); Check("disconnect releases all live region entries", gate.CachedPeers == 0 && gate.CachedZones == 0 && !gate.Contains(new Cell(800, 800), 8));
        for (int step = 0; step < 10000; step++)
        {
            peers.Clear(); for (int i = 0; i < 12; i++) peers.Add(new RegionPeer(i + 1, new Cell(step + i, step), true, new Cell(step + i, step + 1), true));
            gate.Update(peers); Check("walking never retains previous regions", gate.CachedZones <= 600 && gate.CachedPeers == 12);
        }
        pairs = gate.PairTests; peers[0] = Peer(1, 12000, 12000); gate.Update(peers);
        Check("12 peers evaluate exactly 66 pairs on a region change", gate.PairTests - pairs == 66);
        gate.Clear(); Check("world unload releases every cached region", gate.CachedPeers == 0 && gate.CachedZones == 0 && gate.SharedPlayers == 0);
    }
    public static void Main()
    {
        try { Bounds(); Lifecycle(); Console.WriteLine("PASS: " + checks + " native-region broad-phase/boundary/lifecycle assertions."); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
}
