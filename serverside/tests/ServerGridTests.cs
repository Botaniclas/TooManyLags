using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class ServerGridTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static List<GridPeer> Peers(params GridPeer[] peers) { return new List<GridPeer>(peers); }
    private static GridPeer Player(long uid, int heading = 0, bool fast = false, int x = 0)
    { return new GridPeer(uid, new Cell(x, 0), heading * 45f, -1, fast); }
    private static AuthorityGrid Grid(GridCoveragePattern pattern, bool outer = false, float inner = 0, float forward = .2f, float cooldown = 0)
    { return new AuthorityGrid(99, 2, pattern, true, inner, 3, false, true, forward, null, 30, cooldown, true, outer); }
    private static bool Orange(int x, int z, int heading)
    {
        // Independent discrete row tables, rotated only in quarter turns.
        for (int turn = 0; turn < heading / 2; turn++) { int old = x; x = -z; z = old; }
        return (heading & 1) == 0 ? z == 2 && Math.Abs(x) <= 1 : (x == 1 && z == 2) || (x == 2 && z == 1) || (x == 2 && z == 2);
    }
    private static void Masks()
    {
        foreach (GridCoveragePattern pattern in new[] { GridCoveragePattern.Square3x3, GridCoveragePattern.Rounded5x5, GridCoveragePattern.Rounded7x7 })
        for (int heading = 0; heading < 8; heading++)
        {
            var grid = Grid(pattern); var peers = Peers(Player(1, heading)); grid.Update(peers, 0);
            bool rounded = pattern != GridCoveragePattern.Square3x3;
            Check("default mask keeps only nine red and three optional orange cells", grid.CachedCells == (rounded ? 12 : 9) && grid.Changes.Count == 9);
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
            {
                var cell = new Cell(x, z); bool red = Math.Abs(x) <= 1 && Math.Abs(z) <= 1;
                bool orange = rounded && Orange(x, z, heading);
                Check("full rear red and no yellow match independent mask", grid.Candidate(cell) == (red || orange ? 1 : 0) && grid.Owner(cell) == (red ? 1 : 0));
            }
            grid.Update(peers, .2f); Check("normal forward timer still matures while standing", grid.Changes.Count == (rounded ? 3 : 0));
            int reads = grid.NeighborReads, updates = grid.PlayerUpdates, visits = grid.TimerVisits;
            for (int i = 0; i < 1000; i++) grid.Update(peers, i + 1);
            Check("stable full-rear mask has no repeated field work", grid.NeighborReads == reads && grid.PlayerUpdates == updates && grid.TimerVisits == visits);

            grid = Grid(pattern); grid.Update(Peers(Player(1, heading, true)), 0);
            Check("fast forward preserves geometry and becomes immediate", grid.Changes.Count == (rounded ? 12 : 9));
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
                Check("fast mask has no extra yellow or missing rear cells", grid.Owner(new Cell(x, z)) == ((Math.Abs(x) <= 1 && Math.Abs(z) <= 1) || (rounded && Orange(x, z, heading)) ? 1 : 0));
        }
        var all = Grid(GridCoveragePattern.Rounded7x7, true); all.Update(Peers(Player(1)), 0);
        Check("yellow can be enabled without rear cutouts", all.CachedCells == 37 && all.Candidate(new Cell(0, -3)) == 1 && all.Owner(new Cell(0, -3)) == 0);
        all.Update(Peers(Player(1)), 3); Check("enabled yellow retains outer timer", all.Owner(new Cell(0, -3)) == 1);
        var uniform = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7, false, 0, 3, false, true, .2f, null, 30, 0, true, false);
        uniform.Update(Peers(Player(1, fast: true)), 0); uniform.Update(Peers(Player(1)), 2);
        Check("full-grid mode ignores directional yellow and speed switches", uniform.CachedCells == 37 && uniform.Owner(new Cell(0, -3)) == 1);
    }
    private static void Transitions()
    {
        var grid = Grid(GridCoveragePattern.Rounded7x7, inner: 2, forward: 5);
        var cell = new Cell(0, 2);
        grid.Update(Peers(Player(1)), 0); grid.Update(Peers(Player(1, fast: true)), .1f);
        Check("speed change accelerates pending orange in same cell and heading", grid.Owner(cell) == 1 && grid.Wait(cell) == 0 && grid.FastForward(1));
        Check("orange override is exactly zero even with a nonzero red timer", grid.Owner(new Cell(0, 0)) == 0);
        int reads = grid.NeighborReads, updates = grid.PlayerUpdates;
        for (int i = 0; i < 1000; i++) grid.Update(Peers(Player(1, fast: true)), i + 1);
        Check("stable fast mode performs no field rebuilds", grid.NeighborReads == reads && grid.PlayerUpdates == updates);
        grid.Update(Peers(Player(1)), 1002); Check("slowing changes only orange timing, not settled ownership", grid.Owner(cell) == 1 && grid.Wait(cell) == 5 && !grid.FastForward(1));
        grid.Update(Peers(Player(1, fast: true), Player(2)), 1003);
        Check("mixed-speed overlap goes directly to server using immediate request", grid.Owner(cell) == 99 && grid.Wait(cell) == 0);
        grid.Update(Peers(), 1004); grid.Update(Peers(), 1010);
        Check("all speed counters and departed cells retire", grid.CachedCells == 0 && !grid.FastForward(1));
        grid.Update(Peers(Player(3)), 1011); Check("later normal user inherits no stale fast counters", grid.Wait(cell) == 5 && grid.Owner(cell) == 0);
        grid.Update(Peers(Player(3, fast: true, heading: 2)), 1011.1f);
        Check("simultaneous heading and speed update replaces old orange", grid.Owner(new Cell(2, 0)) == 3 && grid.Candidate(cell) == 0);

        // A second nearby player stays visible to the grid while motionless.
        grid = Grid(GridCoveragePattern.Rounded7x7);
        grid.Update(Peers(Player(1), Player(2, x: 4)), 0);
        grid.Update(Peers(Player(1, fast: true), Player(2, x: 2)), .1f);
        Check("moving player still considers standing peer coverage", grid.Owner(new Cell(1, 0)) == 99 && grid.Owner(new Cell(-1, 0)) == 1);

        grid = Grid(GridCoveragePattern.Rounded7x7, cooldown: 1);
        grid.Update(Peers(Player(1), Player(2)), 0);
        grid.Update(Peers(Player(1, fast: true)), .1f);
        Check("speed activation cannot bypass existing server overlap hold", grid.Owner(new Cell(0, 0)) == 99);
        grid.Update(Peers(Player(1, fast: true)), 1.1f);
        Check("cooldown expiry alone never submits an ownership change", grid.Owner(new Cell(0, 0)) == 99);
        grid.Update(Peers(Player(1, heading: 2, fast: true)), 1.2f);
        Check("fresh heading after overlap hold can acquire the cell", grid.Owner(new Cell(0, 0)) == 1);
    }
    private static Controller Setup(ConfigFile config, out ZDO character, out ZDO forward, out ZDO rear, out ZDO yellow)
    {
        Time.unscaledTime = 0;
        ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        var root = new GameObject(); root.Add(new ZNetView()); root.Add(new Pickable()); ZNetScene.instance.Prefabs.Add(1, root);
        var peer = new ZNetPeer { m_uid = 1, m_characterID = new ZDOID(1) }; ZNet.instance.Peers.Add(peer);
        character = new ZDO { Owner = 1 }; ZDOMan.instance.Data.Add(peer.m_characterID, character);
        var controller = new Controller(new Settings(config), 99);
        forward = Add(controller, 0, 8); rear = Add(controller, 0, -4); yellow = Add(controller, 0, 12);
        return controller;
    }
    private static ZDO Add(Controller controller, float x, float z)
    {
        var data = new ZDO { Owner = 99, Prefab = 1, Position = new Vector3(x, 0, z) };
        controller.Add(data, new GameObject().Add(new ZNetView { Data = data })); return data;
    }
    private static void Tick(Controller controller, float time) { Time.unscaledTime = time; controller.Tick(time); controller.Service(time); }
    private static void NativeSpeed()
    {
        var config = new ConfigFile(); var settings = new Settings(config);
        Check("server-return and yellow-off are production defaults", !settings.Keep && !settings.IncludeOuter && settings.FastMovementSpeed == 4);
        ZDO character, forward, rear, yellow; var controller = Setup(config, out character, out forward, out rear, out yellow);
        Tick(controller, 0);
        Check("rear object is immediately local while orange waits and yellow stays server-owned", rear.Owner == 1 && forward.Owner == 99 && yellow.Owner == 99);
        character.Velocity = new Vector3(0, 0, 4); Tick(controller, .05f);
        Check("native speed promotes actual orange object without another movement or timer", forward.Owner == 1);
        var grid = ((AuthorityWorld)typeof(Controller).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)).Grid;
        Check("speed override starts at exactly four metres per second", grid.FastForward(1));
        character.Velocity = new Vector3(0, 0, 3); Tick(controller, .06f);
        Check("exactly three metres per second retains fast mode", grid.FastForward(1));
        character.Velocity = new Vector3(0, 0, 2.99f); Tick(controller, .07f);
        Check("below three metres per second releases fast mode", !grid.FastForward(1));
        character.Velocity = new Vector3(0, 0, 3.99f); Tick(controller, .08f);
        Check("subthreshold movement cannot start fast mode", !grid.FastForward(1));
        character.Velocity = new Vector3(0, 0, 4); Tick(controller, .09f);
        int reads = forward.OwnerReads, writes = forward.Writes;
        for (int i = 0; i < 1000; i++) { character.Velocity = new Vector3(0, 0, i % 2 == 0 ? 3.2f : 4.1f); Tick(controller, .1f + i * .1f); }
        Check("hysteresis prevents threshold jitter from repeating object work", forward.OwnerReads == reads && forward.Writes == writes && yellow.Owner == 99);
        character.Position = new Vector3(40, 0, 0); character.Velocity = Vector3.zero; Tick(controller, 101); Tick(controller, 105);
        Check("default exit returns managed objects to server", forward.Owner == 99 && rear.Owner == 99);
        controller.Remove(forward); controller.Remove(rear); controller.Remove(yellow); ZNet.instance.Peers.Clear(); Tick(controller, 106);
        Check("disconnect and unload clean runtime indexes", controller.TrackedObjects == 0 && controller.WaitingCells == 0);

        config.Values["KeepOwnershipOnCellExit"] = true; controller = Setup(config, out character, out forward, out rear, out yellow); Tick(controller, 0);
        Check("opting into retention never cuts out rear red", rear.Owner == 1);
        character.Position = new Vector3(40, 0, 0); Tick(controller, 1); Tick(controller, 5);
        Check("explicit retention still preserves vacated owner", rear.Owner == 1);

        config = new ConfigFile(); config.Values["FastMovementThresholdMetersPerSecond"] = 0f;
        controller = Setup(config, out character, out forward, out rear, out yellow); character.Velocity = new Vector3(0, 0, 20); Tick(controller, 0);
        Check("disabled speed boost reads no native velocity", character.VelocityReads == 0 && forward.Owner == 99);
        Tick(controller, .2f); Check("disabled speed boost keeps normal orange deadline", forward.Owner == 1);
        config = new ConfigFile(); config.Values["ForwardTransitionDelaySeconds"] = 0f;
        controller = Setup(config, out character, out forward, out rear, out yellow); Tick(controller, 0);
        Check("already immediate orange needs no speed read", character.VelocityReads == 0 && forward.Owner == 1);

        foreach (Vector3 velocity in new[] { new Vector3(0, 20, 0), new Vector3(Single.NaN, 0, 10), new Vector3(Single.PositiveInfinity, 0, 0) })
        {
            controller = Setup(new ConfigFile(), out character, out forward, out rear, out yellow); character.Velocity = velocity; Tick(controller, 0);
            Check("vertical or invalid velocity cannot trigger fast orange", forward.Owner == 99);
        }
    }
    private static int Main()
    {
        try { Masks(); Transitions(); NativeSpeed(); Console.WriteLine("PASS: " + checks + " full-rear/yellow-off masks, speed acceleration, hysteresis, stable-object cost and server-return assertions. Managed fixtures; no live gameplay claim."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
