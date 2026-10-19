using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class AdaptiveRefillTests
{
    private static int checks;
    private static void Check(string label, bool ok) { checks++; if (!ok) throw new Exception(label); }
    private static HashSet<Cell> Rotate(IEnumerable<Cell> points, int turns)
    {
        var result = new HashSet<Cell>();
        foreach (Cell point in points) { int x = point.X, z = point.Z; for (int i = 0; i < turns; i++) { int old = x; x = z; z = -old; } result.Add(new Cell(x, z)); }
        return result;
    }
    private static void Masks()
    {
        var north = new HashSet<Cell>();
        int[] widths = { 3, 5, 5, 3 };
        for (int row = 0; row < 4; row++) for (int x = -widths[row] / 2; x <= widths[row] / 2; x++) north.Add(new Cell(x, 2 - row));
        var diagonal = new HashSet<Cell>(north); diagonal.UnionWith(Rotate(north, 1)); diagonal.Add(new Cell(2, 2));
        for (int heading = 0; heading < 8; heading++) for (int speed = 0; speed < 2; speed++)
        {
            var grid = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7, true, 0, 3, true, true, .2f, null, 30, 0, true, false);
            var peers = new List<GridPeer> { new GridPeer(1, new Cell(0, 0), heading * 45, -1, speed != 0, true) };
            var red = Rotate((heading & 1) == 0 ? north : diagonal, heading / 2);
            var orange = Rotate((heading & 1) == 0 ? new[] { new Cell(-1, 3), new Cell(0, 3), new Cell(1, 3) } : new[] { new Cell(2, 3), new Cell(3, 3), new Cell(3, 2) }, heading / 2);
            grid.Update(peers, 0);
            Check("large shape has sixteen cardinal/twenty diagonal red cells plus three orange", red.Count == ((heading & 1) == 0 ? 16 : 20) && grid.CachedCells == red.Count + 3);
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
            {
                var cell = new Cell(x, z);
                Check("large shape matches independent row/quarter-turn reference", grid.Candidate(cell) == (red.Contains(cell) || orange.Contains(cell) ? 1 : 0) && grid.Owner(cell) == (red.Contains(cell) || (speed != 0 && orange.Contains(cell)) ? 1 : 0));
            }
            grid.Update(peers, .2f);
            int reads = grid.NeighborReads, updates = grid.PlayerUpdates;
            for (int i = 0; i < 1000; i++) grid.Update(peers, 1 + i);
            Check("stable large grid never repeats field work", reads == grid.NeighborReads && updates == grid.PlayerUpdates && grid.FastForward(1) == (speed != 0));
            grid.Update(new List<GridPeer>(), 1002); grid.Update(new List<GridPeer>(), 1006);
            Check("large mode retains no historical cells after departure", grid.CachedCells == 0 && !grid.LargeCoverage(1));
        }
        var shared = new AuthorityGrid(99, 2, GridCoveragePattern.Rounded7x7, true, 0, 3, true, true, .2f, null, 30, 1, true, false);
        shared.Update(new List<GridPeer> { new GridPeer(1, new Cell(0, 0), large: true), new GridPeer(2, new Cell(3, 0)) }, 0);
        Check("large coverage only shares actual intersecting cells", shared.Owner(new Cell(2, 0)) == 99 && shared.Owner(new Cell(-2, 0)) == 1 && shared.Owner(new Cell(4, 0)) == 2);
    }
    private static Controller Setup(ConfigFile config, out ZDO player)
    {
        Time.unscaledTime = 0; ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        var peer = new ZNetPeer { m_uid = 1, m_characterID = new ZDOID(1) }; ZNet.instance.Peers.Add(peer);
        player = new ZDO { Owner = 1 }; ZDOMan.instance.Data.Add(peer.m_characterID, player);
        Prefab(1, new Door()); Prefab(2, new Smelter(), new Switch()); Prefab(3, new CookingStation()); Prefab(4, new Turret()); Prefab(5, new ShieldGenerator()); Prefab(6, new Fireplace());
        var controller = new Controller(new Settings(config), 99); Plugin.Current = controller; return controller;
    }
    private static void Prefab(int hash, params MonoBehaviour[] parts)
    { var root = new GameObject(); root.Add(new ZNetView()); foreach (var part in parts) root.Add(part); ZNetScene.instance.Prefabs.Add(hash, root); }
    private static ZNetView Add(Controller controller, ZDO data, params MonoBehaviour[] parts)
    {
        var root = new GameObject(); var view = root.Add(new ZNetView { Data = data }); foreach (var part in parts) root.Add(part);
        ZDOMan.instance.Data.Add(data.m_uid, data); controller.Add(data, view); return view;
    }
    private static void Tick(Controller controller, float now) { Time.unscaledTime = now; controller.Tick(now); controller.Service(now); }
    private static AuthorityGrid Grid(Controller controller)
    { return ((AuthorityWorld)typeof(Controller).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller)).Grid; }
    private static void Latency()
    {
        var config = new ConfigFile(); ZDO player; var controller = Setup(config, out player);
        var socket = (FixtureSocket)ZNet.instance.Peers[0].m_socket; socket.Ping = 200;
        Tick(controller, 0); Check("exactly 200 ms activates large coverage with configurable default", Grid(controller).LargeCoverage(1) && Grid(controller).CachedCells == 19);
        int calls = socket.Reads;
        for (int i = 1; i < 100; i++) Tick(controller, i * .009f);
        Check("frequent player checks reuse one native ping query per second", socket.Reads == calls && Grid(controller).PlayerUpdates == 1);
        socket.Ping = 150; Tick(controller, 1); Check("150 ms retains wide hysteresis", Grid(controller).LargeCoverage(1));
        socket.Ping = 149; Tick(controller, 2); Check("below 150 ms restores normal twelve-cell coverage once", !Grid(controller).LargeCoverage(1) && Grid(controller).Candidate(new Cell(-2, 0)) == 0);
        socket.Ping = 199; Tick(controller, 3); Check("subthreshold ping cannot expand coverage", !Grid(controller).LargeCoverage(1));
        socket.Ping = 200; Tick(controller, 4); socket.Ping = 0; Tick(controller, 5);
        Check("unknown telemetry preserves last known large geometry", Grid(controller).LargeCoverage(1));
        ZNet.instance.Peers.Clear(); Tick(controller, 6); Tick(controller, 10);
        Check("disconnect clears adaptive cell/cache state", Grid(controller).CachedCells == 0 && !Grid(controller).LargeCoverage(1));
        config.Values["LargeGridPingThresholdMs"] = 0; controller = Setup(config, out player); socket = (FixtureSocket)ZNet.instance.Peers[0].m_socket; socket.Ping = 999;
        Tick(controller, 0); Check("disabled expansion performs no native latency query", !Grid(controller).LargeCoverage(1) && socket.Reads == 0);
        config.Values["LargeGridPingThresholdMs"] = 400; controller = Setup(config, out player); socket = (FixtureSocket)ZNet.instance.Peers[0].m_socket; socket.Ping = 399;
        Tick(controller, 0); socket.Ping = 400; Tick(controller, 1); Check("custom ping threshold applies", Grid(controller).LargeCoverage(1));
        config.Values["DirectionalGridMode"] = false; controller = Setup(config, out player); socket = (FixtureSocket)ZNet.instance.Peers[0].m_socket; socket.Ping = 999;
        Tick(controller, 0); Check("uniform grids preserve their explicit shape without ping sampling", !Grid(controller).LargeCoverage(1) && socket.Reads == 0);
    }
    private static ConfigFile LocalConfig()
    { var config = new ConfigFile(); config.Values["LargeGridPingThresholdMs"] = 0; return config; }
    private static void Refills()
    {
        var config = LocalConfig(); ZDO player; var controller = Setup(config, out player);
        var data = new ZDO { Owner = 99, Prefab = 2 }; var smelter = new Smelter(); Add(controller, data, smelter, new Switch());
        smelter.RPC_AddFuel(1); Tick(controller, 0);
        Check("successful native server refill blocks immediate grid delegation", data.Owner == 99 && controller.WaitingCells == 1);
        Time.unscaledTime = .8f; smelter.RPC_AddOre(1, "ore", false); player.Rotation = new Quaternion { Yaw = 45 }; Tick(controller, 1.1f);
        Check("repeated native refill restarts one-second protection", data.Owner == 99);
        Tick(controller, 1.81f); Check("expiry alone never replays blocked ownership transfer", data.Owner == 99);
        player.Rotation = new Quaternion { Yaw = 90 }; Tick(controller, 1.9f); Check("new heading after protection can delegate immediately", data.Owner == 1);
        int reads = data.RefillReads, writes = data.Writes;
        for (int i = 0; i < 1000; i++) Tick(controller, 2 + i * .01f);
        Check("stable station has no periodic refill checks or repeated ownership work", data.RefillReads == reads && data.Writes == writes);
        var peer = new ZNetPeer { m_uid = 2, m_characterID = new ZDOID(2) }; ZNet.instance.Peers.Add(peer); ZDOMan.instance.Data.Add(peer.m_characterID, new ZDO { Owner = 2 });
        Time.unscaledTime = 13; data.Set(ZDOVars.s_queued, 2); controller.DataReceived(data, 1); Tick(controller, 13);
        Check("native received client refill also blocks overlap takeover", data.Owner == 1 && controller.PendingTransfers == 0);
        Tick(controller, 14.1f); Check("client refill expiry does not create a timer handoff", data.Owner == 1);
        player.Rotation = new Quaternion { Yaw = 135 }; Tick(controller, 14.2f); controller.Service(14.31f);
        Check("fresh overlap activity resumes existing protected player-to-server handoff", data.Owner == 99);
        controller.Remove(data); data.Set(ZDOVars.s_queued, 3); controller.RefillChanged(data); Tick(controller, 15);
        Check("unload removes refill state and blocked-cell references", controller.TrackedObjects == 0 && controller.WaitingCells == 0 && controller.PendingTransfers == 0);

        controller = Setup(LocalConfig(), out player); data = new ZDO { Owner = 99, Prefab = 2 }; smelter = new Smelter { Accept = false }; Add(controller, data, smelter, new Switch()); smelter.RPC_AddFuel(1); Tick(controller, 0);
        Check("rejected or full station insert creates no protection", data.Owner == 1);
        controller = Setup(LocalConfig(), out player); data = new ZDO { Owner = 99, Prefab = 2 }; data.Set(ZDOVars.s_fuel, 10f); smelter = new Smelter(); Add(controller, data, smelter, new Switch()); data.Set(ZDOVars.s_fuel, 0f); smelter.RPC_AddFuel(1); Tick(controller, 0);
        Check("local prefix refresh detects an insert after unobserved automatic fuel consumption", data.Owner == 99);
        controller = Setup(LocalConfig(), out player); data = new ZDO { Owner = 99, Prefab = 2 }; data.Set(ZDOVars.s_fuel, 10f); Add(controller, data, new Smelter(), new Switch()); data.Set(ZDOVars.s_fuel, 9f); controller.DataReceived(data, 1); Tick(controller, 0);
        Check("ordinary fuel consumption never restarts refill protection", data.Owner == 1);

        controller = Setup(LocalConfig(), out player); data = new ZDO { Owner = 99, Prefab = 3 }; var cooking = new CookingStation(); Add(controller, data, cooking); cooking.RPC_AddItem(1, "RawMeat", false); Tick(controller, 0);
        Check("native cooking empty-to-filled slot blocks delegation", data.Owner == 99);
        controller = Setup(LocalConfig(), out player); data = new ZDO { Owner = 99, Prefab = 3 }; data.Set("slot0".GetStableHashCode(), "RawMeat"); Add(controller, data, new CookingStation()); data.Set("slot0".GetStableHashCode(), "CookedMeat"); controller.DataReceived(data, 1); Tick(controller, 0);
        Check("ordinary cooking conversion is not a new insert", data.Owner == 1);
        controller = Setup(LocalConfig(), out player); data = new ZDO { Owner = 99, Prefab = 4 }; var turret = new Turret(); Add(controller, data, turret); turret.RPC_AddAmmo(1, "Bolt"); Tick(controller, 0);
        Check("native ammo increase protects turret ownership", data.Owner == 99);
        controller = Setup(LocalConfig(), out player); data = new ZDO { Owner = 99, Prefab = 5 }; var shield = new ShieldGenerator(); Add(controller, data, shield); shield.RPC_AddFuel(1); Tick(controller, 0);
        Check("nested shield add/set RPC preserves one protection deadline", data.Owner == 99 && controller.WaitingCells == 1);
        Check("actual shield fuel and attack request names are routed", controller.Request(data, "RPC_SetFuel".GetStableHashCode()) && controller.Request(data, "RPC_Attack".GetStableHashCode()));
        controller = Setup(LocalConfig(), out player); data = new ZDO { Owner = 99, Prefab = 6 }; var fire = new Fireplace(); Add(controller, data, fire); fire.RPC_AddFuel(1); Tick(controller, 0);
        Check("eligible fireplace fuel insertion protects ownership", data.Owner == 99);
        config = LocalConfig(); config.Values["RefillOwnershipProtectionSeconds"] = 0f; controller = Setup(config, out player); data = new ZDO { Owner = 99, Prefab = 2 }; smelter = new Smelter(); Add(controller, data, smelter, new Switch()); smelter.RPC_AddFuel(1); Tick(controller, 0);
        Check("zero disables only additional refill window", data.Owner == 1 && controller.WaitingCells == 0);
    }
    private static int Main()
    {
        var harmony = new Harmony("fixture.adaptive.refill");
        try
        {
            new PatchClassProcessor(harmony, typeof(NativeRefill)).Patch();
            Masks(); Latency(); Refills();
            Console.WriteLine("PASS: " + checks + " adaptive grid masks/ping cache and native/received refill protection assertions. Real Harmony refill hooks with managed fixtures; no live gameplay claim."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { harmony.UnpatchSelf(); Plugin.Reset(); }
    }
}
