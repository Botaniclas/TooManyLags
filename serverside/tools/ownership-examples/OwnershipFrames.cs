// Documentation only. Compiles unchanged production grid source; no Unity,
// installed profiles, save files, networking or runtime-source edits.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using G = TooManyLagsDedicatedAddon;

internal static class OwnershipFrames
{
    private const int Width = 50, Height = 50, Server = 99;
    private const float End = 300f, CellSize = 4f, Interval = .1f;
    private sealed class Waypoint
    {
        internal float T, X, Z;
        internal Waypoint(float t, float x, float z) { T=t; X=x; Z=z; }
    }
    private static readonly Waypoint[][] Paths = BuildPaths();
    private static Waypoint W(float t,float x,float z) { return new Waypoint(t,x,z); }
    private static Waypoint[][] BuildPaths()
    {
        var anchors = new[] {
            new[] { W(3,10,37), W(9,10,37), W(20,7,43), W(38,18,42), W(58,16,31), W(76,7,29), W(91,9,37), W(112,20,29), W(125,24,25),
                W(129,23,26), W(134,25,27), W(139,26,25), W(144,24,23), W(149,22,24), W(154,24,26), W(159,26,26), W(164,25,24), W(169,23,25),
                W(190,16,32), W(202,6,31), W(219,7,40), W(233,17,39), W(244,15,34), W(258,10,35), W(268,10,37), W(300,10,37) },
            new[] { W(14,45,32), W(32,40,40), W(47,32,41), W(62,33,30), W(78,43,29), W(94,42,36), W(116,29,29), W(126,25,25),
                W(130,27,26), W(136,26,23), W(141,24,24), W(147,24,27), W(152,27,25), W(158,26,24), W(164,25,26), W(170,27,27),
                W(188,36,28), W(201,43,27), W(216,42,39), W(233,34,38), W(245,33,32), W(258,38,32), W(269,38,36), W(300,38,36) },
            new[] { W(28,32,5), W(48,40,12), W(64,39,20), W(81,28,20), W(99,27,10), W(117,25,22), W(127,25,24),
                W(132,23,24), W(137,24,22), W(143,26,23), W(148,25,25), W(153,23,26), W(158,24,24), W(164,26,24), W(170,25,23),
                W(188,31,15), W(204,41,14), W(217,40,9), W(238,28,9), W(251,27,18), W(268,35,18), W(285,32,12), W(300,32,12) }
        };
        var result = new Waypoint[anchors.Length][];
        for(int player=0;player<anchors.Length;player++)
        {
            // Fixed independent seeds: natural-looking detours, yet exactly
            // the same movement in every ownership-mode comparison.
            var random=new Random(733+(player+1)*2027);
            var path=new List<Waypoint>();path.Add(anchors[player][0]);
            for(int j=0;j<anchors[player].Length-1;j++)
            {
                Waypoint a=anchors[player][j],b=anchors[player][j+1];
                int steps=(a.X==b.X&&a.Z==b.Z)?1:Math.Max(1,(int)Math.Ceiling((b.T-a.T)/4));
                for(int k=1;k<steps;k++)
                {
                    float f=k/(float)steps;
                    float x=a.X+(b.X-a.X)*f+(float)(random.NextDouble()*1.6-.8);
                    float z=a.Z+(b.Z-a.Z)*f+(float)(random.NextDouble()*1.6-.8);
                    path.Add(W(a.T+(b.T-a.T)*f,Math.Max(4,Math.Min(45,x)),Math.Max(4,Math.Min(45,z))));
                }
                path.Add(b);
            }
            result[player]=path.ToArray();
        }
        return result;
    }

    private static int Index(int x, int z) { return z * Width + x; }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static int[][][] Masks(G.AuthorityGrid grid)
    {
        var all = (Array)typeof(G.AuthorityGrid).GetField("footprints", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(grid);
        var result = new int[all.Length][][];
        for (int i = 0; i < all.Length; i++)
        {
            var offsets = (Array)all.GetValue(i); result[i] = new int[offsets.Length][];
            for (int j = 0; j < offsets.Length; j++)
            {
                object offset = offsets.GetValue(j);
                Type type = offset.GetType();
                object cell = type.GetField("Cell", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(offset);
                result[i][j] = new[] {
                    (int)cell.GetType().GetField("X", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cell),
                    (int)cell.GetType().GetField("Z", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cell),
                    Convert.ToInt32(type.GetField("Tier", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(offset)) };
            }
        }
        return result;
    }
    private static int ActualMask(G.AuthorityGrid grid, long player)
    {
        var peers = (IDictionary)typeof(G.AuthorityGrid).GetField("previous", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(grid);
        object state = peers[player];
        return (int)state.GetType().GetField("Heading", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
    }
    private static Dictionary<string, object> Position(int player, float t)
    {
        var path = Paths[player - 1]; if (t < path[0].T) return null;
        float x = path[0].X, z = path[0].Z, yaw = 0f, speed = 0f;
        for (int i = 0; i < path.Length - 1; i++)
        {
            var a = path[i]; var b = path[i + 1];
            if (t < a.T) break;
            float dx = b.X - a.X, dz = b.Z - a.Z;
            if (dx != 0 || dz != 0) yaw = (float)(Math.Atan2(dx, dz) * 180 / Math.PI);
            if (t <= b.T)
            {
                float f = (t - a.T) / (b.T - a.T); x = a.X + dx * f; z = a.Z + dz * f;
                speed = (float)Math.Sqrt(dx * dx + dz * dz) * CellSize / (b.T - a.T);
                break;
            }
            x = b.X; z = b.Z;
        }
        int cx = (int)Math.Floor(x + .5f), cz = (int)Math.Floor(z + .5f);
        return new Dictionary<string, object> { {"id", player}, {"x", x}, {"z", z}, {"cx", cx}, {"cz", cz}, {"yaw", yaw}, {"speed", speed} };
    }
    private static string Phase(float t)
    {
        if (t < 3) return "Initial server ownership";
        if (t < 14) return "Player 1 is alone at the forge";
        if (t < 28) return "Player 2 arrives from storage";
        if (t < 120) return "Three players explore different parts of the base";
        if (t < 173) return "Meeting: each player keeps moving differently";
        if (t < 285) return "Players continue moving through their work areas";
        return "Later: forge, storage and farm";
    }
    private static Dictionary<string, object> Run(bool keep)
    {
        // Same parameters as Controller's AuthorityWorld constructor and defaults:
        // all rear cells, no yellow outer cells, nearest character sector,
        // 0 / 0.2 / 3-second delays, 1-second server overlap hold.
        var grid = new G.AuthorityGrid(Server, 2f, G.GridCoveragePattern.Rounded7x7,
            true, 0f, 3f, keep, true, .2f, null, 30f, 1f, true, false);
        var masks = Masks(grid);
        for (int heading = 0; heading < 8; heading++)
        {
            int inner = 0, forward = 0;
            Check(masks[heading].Length == 12, "Normal footprint is not twelve cells.");
            foreach (int[] cell in masks[heading])
            {
                if (cell[2] == (int)G.FieldTier.Inner) inner++;
                if (cell[2] == (int)G.FieldTier.Forward) forward++;
                Check(cell[2] != (int)G.FieldTier.Outer, "Yellow outer cell enabled.");
            }
            Check(inner == 9 && forward == 3, "Normal red/orange count differs from defaults.");
            Check(Array.Exists(masks[heading], c => c[0] == 0 && c[1] == -1), "Full red rear cell missing.");
        }
        int[] owners = new int[Width * Height]; for (int i = 0; i < owners.Length; i++) owners[i] = Server;
        var frames = new List<object>(); var peers = new List<G.GridPeer>();
        int overlaps = 0, retained = 0, fastTicks = 0;
        for (int tick = 0; tick <= (int)(End / Interval); tick++)
        {
            float now = tick * Interval; peers.Clear(); var people = new List<Dictionary<string, object>>();
            for (int id = 1; id <= 3; id++)
            {
                var pos = Position(id, now); if (pos == null) continue;
                people.Add(pos);
                float threshold = grid.FastForward(id) ? 3f : 4f;
                bool fast = (float)pos["speed"] >= threshold;
                pos["fast"] = fast; pos["large"] = false;
                // All example players are below the 200-ms adaptive-size threshold.
                peers.Add(new G.GridPeer(id, new G.Cell((int)pos["cx"], (int)pos["cz"]), (float)pos["yaw"], -1, fast, false));
            }
            grid.Update(peers, now);
            foreach (G.Cell cell in grid.Changes)
            {
                long owner = grid.Owner(cell);
                if (owner == 0 && keep) continue;
                if (owner == 0) owner = Server;
                if (cell.X >= 0 && cell.X < Width && cell.Z >= 0 && cell.Z < Height) owners[Index(cell.X, cell.Z)] = (int)owner;
                grid.Remember(cell, owner);
            }
            var cover = new int[Width * Height];
            foreach (var pos in people)
            {
                int mask = ActualMask(grid, (int)pos["id"]); pos["heading"] = mask;
                int cx = (int)pos["cx"], cz = (int)pos["cz"], inner = 0, forward = 0;
                var cells = new List<int[]>();
                foreach (int[] offset in masks[mask])
                {
                    int x = cx + offset[0], z = cz + offset[1];
                    cells.Add(new[] { x, z, offset[2] });
                    if (offset[2] == (int)G.FieldTier.Inner || offset[2] == (int)G.FieldTier.ImmediateForward) inner++;
                    if (offset[2] == (int)G.FieldTier.Forward) forward++;
                    if (x >= 0 && x < Width && z >= 0 && z < Height) cover[Index(x, z)]++;
                }
                Check(cells.Count == 12, "Snapshot mask differs from the normal Server Side grid.");
                Check(inner == ((bool)pos["fast"] ? 12 : 9) && forward == ((bool)pos["fast"] ? 0 : 3), "Speed-selected timers differ from controller defaults.");
                if ((bool)pos["fast"]) fastTicks++;
                pos["coverage"] = cells;
            }
            bool shared = false, trail = false;
            for (int i = 0; i < owners.Length; i++)
            {
                if (cover[i] > 1 && owners[i] == Server) shared = true;
                if (cover[i] == 0 && owners[i] != Server) trail = true;
                if (!keep && cover[i] == 0 && now >= 296f) Check(owners[i] == Server, "A settled empty field did not return.");
            }
            if (shared) overlaps++; if (trail) retained++;
            if (tick % 10 == 0) frames.Add(new Dictionary<string, object> { {"time", now}, {"phase", Phase(now)}, {"owners", (int[])owners.Clone()}, {"players", people} });
        }
        Check(overlaps > 0, "Server overlap was not demonstrated.");
        Check(!keep || retained > 0, "Retained ownership was not demonstrated.");
        Check(fastTicks > 0, "Speed boost was not demonstrated.");
        Check(owners[Index(10, 37)] == 1 && owners[Index(38, 36)] == 2 && owners[Index(32, 12)] == 3, "Work areas did not settle.");
        return new Dictionary<string, object> { {"mode", keep ? 2 : 3}, {"frames", frames}, {"serverOverlapTicks", overlaps}, {"retainedTicks", retained}, {"fastTicks", fastTicks} };
    }
    public static int Main(string[] args)
    {
        var modes = new List<object> { Run(false), Run(true) };
        var serializer = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 * 1024 };
        File.WriteAllText(args[0], serializer.Serialize(new Dictionary<string, object> { {"width", Width}, {"height", Height}, {"modes", modes} }));
        Console.WriteLine("PASS: two production Server Side grids; 12-cell full-rear masks, speed hysteresis, server overlaps, settled returns, retention and final work areas.");
        return 0;
    }
}
