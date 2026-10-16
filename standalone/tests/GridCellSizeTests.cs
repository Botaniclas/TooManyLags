using System;
using System.Collections.Generic;
#if PLAYER_SIMULATION
using TooManyLagsStandalone;
#else
using TooManyLagsStandalone;
#endif

internal static class GridCellSizeTests
{
    private static int checks;
    private static void Check(string label, bool value)
    { checks++; if (!value) throw new Exception(label); }

    private static void FractionalZones()
    {
        for (int zone = -256; zone < 256; zone++)
        {
            var cells = new HashSet<Cell>();
            for (int x = 0; x < 10; x++) for (int z = 0; z < 10; z++)
            {
                float px = (float)(zone * 64d - 32d + (x + .5d) * 6.4d);
                float pz = (float)(zone * 64d - 32d + (z + .5d) * 6.4d);
                Cell fine;
                Check("fractional cell centre remains valid throughout native domain", Cell.At(px, pz, 6.4f, out fine));
                Check("exactly ten fine cells per native axis", fine.Equals(new Cell(zone * 10 + x, zone * 10 + z)));
                Check("signed floor division maps to the native zone", Cell.Zone(fine, 6.4f).Equals(new Cell(zone, zone)));
                Check("cell centre agrees with exact cached bounds", px >= Cell.Edge(fine.X, 10) && px < Cell.Edge(fine.X + 1, 10) &&
                    pz >= Cell.Edge(fine.Z, 10) && pz < Cell.Edge(fine.Z + 1, 10));
                cells.Add(fine);
            }
            Check("native zone contains exactly one hundred distinct cells", cells.Count == 100);
            Cell border;
            float edge = zone * 64f - 32f;
            Check("native border stays exact without rounded 6.4 division", Cell.At(edge, edge, 6.4f, out border) && border.Equals(new Cell(zone * 10, zone * 10)));
            if (zone > -256)
                Check("predecessor of native border belongs to prior zone", Cell.At(edge - .01f, edge - .01f, 6.4f, out border) &&
                    border.Equals(new Cell(zone * 10 - 1, zone * 10 - 1)) && Cell.Zone(border, 6.4f).Equals(new Cell(zone - 1, zone - 1)));
        }
        Cell cell;
        Check("fractional origin is the fifth cell", Cell.At(0, 0, 6.4f, out cell) && cell.Equals(new Cell(5, 5)));
        Check("unchanged upper world boundary remains exclusive", !Cell.At(16352, 0, 6.4f, out cell));
        Check("unchanged lower world boundary remains inclusive", Cell.At(-16416, 0, 6.4f, out cell) && cell.Equals(new Cell(-2560, 5)));
        Check("outside lower world domain rejected", !Cell.At(-16417, 0, 6.4f, out cell));
        foreach (float size in new[] { 0f, 5f, 6f, 6.40001f, -6.4f, Single.NaN, Single.PositiveInfinity })
            Check("unsupported fractional sizes rejected", !Cell.At(0, 0, size, out cell));
    }

    private static void LegacyEquivalence()
    {
        var random = new Random(645198);
        foreach (int size in new[] { 4, 8, 16 })
            for (int i = 0; i < 40000; i++)
            {
                float x = (float)(random.NextDouble() * 32767d - 16416d);
                float z = (float)(random.NextDouble() * 32767d - 16416d);
                var old = new Cell((int)Math.Floor(((double)x + 32d) / size), (int)Math.Floor(((double)z + 32d) / size));
                Cell current;
                Check("existing subdivisions retain identical coordinates", Cell.At(x, z, size, out current) && current.Equals(old));
                int shift = size == 4 ? 4 : size == 16 ? 2 : 3;
                Check("existing native zone mapping retains shifts", Cell.Zone(current, size).Equals(new Cell(old.X >> shift, old.Z >> shift)));
            }
    }

    private static void Contention()
    {
        var gate = new RegionContentionGate();
        var peers = new List<RegionPeer> { new RegionPeer(1, new Cell(-1, 0), true, new Cell(-1, 0), true),
            new RegionPeer(2, new Cell(0, 0), true, new Cell(0, 0), true) };
        gate.Update(peers);
        for (int x = -40; x <= 40; x++) for (int z = -40; z <= 40; z++)
        {
            var fine = new Cell(x, z);
            int zx = (int)Math.Floor(x / 10d), zz = (int)Math.Floor(z / 10d);
            bool expected = (Math.Abs(zx + 1) <= 2 || Math.Abs(zx) <= 2) && Math.Abs(zz) <= 2;
            Check("fractional coarse gate agrees with independent signed zone oracle", gate.Contains(fine, 6.4f) == expected);
#if !PLAYER_SIMULATION
            Check("cached native zone gives identical contention result", gate.ContainsZone(Cell.Zone(fine, 6.4f)) == expected);
#endif
        }
        int rebuilds = gate.Rebuilds;
        for (int i = 0; i < 1000; i++) gate.Update(peers);
        Check("stationary fractional grid adds no region rebuilds", gate.Rebuilds == rebuilds);
        peers.Clear(); gate.Update(peers);
        Check("unloaded/disconnected region cache is emptied", gate.CachedZones == 0 && gate.CachedPeers == 0 && !gate.Contains(new Cell(0, 0), 6.4f));
    }

    public static int Main()
    {
        try { FractionalZones(); LegacyEquivalence(); Contention(); Console.WriteLine("PASS: " + checks + " cell-size assertions; exact 6.4-metre/native-zone boundaries, cached mapping, legacy equivalence and cleanup."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
