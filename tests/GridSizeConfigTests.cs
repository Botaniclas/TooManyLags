using System;
using System.IO;
using BepInEx.Configuration;
using TooManyLags.PlayerSimulation;

namespace TooManyLags.PlayerSimulation
{
    // Geometry settings do not depend on the live object catalogue.
    [Flags] internal enum Kinds { None = 0, Doors = 1, Incinerators = 2 }
}

internal static class GridSizeConfigTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    public static int Main(string[] args)
    {
        try
        {
            foreach (string saved in new[] { "4", "6.4", "8", "16" })
            {
                string path = Path.Combine(args[0], "geometry-" + Guid.NewGuid().ToString("N") + ".cfg");
                File.WriteAllText(path, "[04 - Grid Settings]\n# Setting type: Int32\nGridCellSizeMeters = " + saved + "\nGridCoveragePattern = Rounded7x7\n");
                var config = new ConfigFile(path, false);
                var size = config.Bind("04 - Grid Settings", "GridCellSizeMeters", 4f, new ConfigDescription("Server only; restart required.", new AcceptableValueList<float>(4f, 6.4f, 8f, 16f)));
                float expected = Single.Parse(saved, System.Globalization.CultureInfo.InvariantCulture);
                Check("legacy integer config and fractional value read without reset", size.Value == expected);
                Check("mode without Server-Side Simulation reads the actual float binding", new Settings(config, false).CellSize == expected);
                config.Save();
                var reopened = new ConfigFile(path, false);
                Check("cell size round-trips through BepInEx", reopened.Bind("04 - Grid Settings", "GridCellSizeMeters", 4f, "").Value == expected);
            }
            Console.WriteLine("PASS: " + checks + " real BepInEx cell-size config/legacy round-trip and player-simulation settings assertions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
