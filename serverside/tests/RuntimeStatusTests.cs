using System;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class RuntimeStatusTests
{
    private static int checks;
    private static void Check(string label, bool condition) { checks++; if (!condition) throw new Exception(label); }
    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Run()
    {
        ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene { InstanceCount = 444 };
        int hash = "Pickable_Stone".GetStableHashCode();
        var prefab = new GameObject(); prefab.Add(new ZNetView()); prefab.Add(new Pickable()); ZNetScene.instance.Prefabs[hash] = prefab;
        var peer = new ZNetPeer { m_uid = 1, m_characterID = new ZDOID(1) }; ZNet.instance.Peers.Add(peer);
        var player = new ZDO { Owner = 1 }; ZDOMan.instance.Data[peer.m_characterID] = player;
        var data = new ZDO { Prefab = hash, Owner = 99 };
        var root = new GameObject(); var view = root.Add(new ZNetView { Data = data }); root.Add(new Pickable());
        var c = new Controller(new Settings(new ConfigFile()), 99); c.Add(data, view); c.Tick(0);
        int writes = data.Writes, tracked = c.TrackedObjects, waiting = c.WaitingCells;
        string status = String.Join("\n", c.DescribeRuntime());
        Check("native eligible object really transferred in fixture", data.Owner == 1);
        Check("status contains actual accepted players", status.Contains("acceptedPlayers=1"));
        Check("status distinguishes registration and tracking", status.Contains("registrations=1; validRegistrations=1; tracked=1"));
        Check("status includes native scene count without scanning scene", status.Contains("sceneInstances=444"));
        Check("status includes controller execution", status.Contains("ticks=1"));
        Check("status includes owner assignment calls", status.Contains("assignCalls=1"));
        Check("status includes native interface classification", status.Contains("GRID prefab Pickable_Stone: native=Pickables; enabled=True"));
        Check("status shows actual prefab persistence", status.Contains("persistent=True"));
        Check("status includes native action controls", status.Contains("controls=Pickable"));
        Check("unknown diagnostic prefabs are explicit", status.Contains("GRID prefab wood_door: missing"));
        Check("status includes individual applied object", status.Contains("group=Pickables; owner=1; field=1; candidate=1"));
        Check("status never changes ownership or registration", data.Writes == writes && c.TrackedObjects == tracked && c.WaitingCells == waiting);

        player.Owner = 99; c.Tick(1); status = String.Join("\n", c.DescribeRuntime());
        Check("owner mismatch is visible rather than hidden by ready message", status.Contains("acceptedPlayers=0") && status.Contains("characterOwner=99; accepted=False"));
        ZDOMan.instance.Data.Remove(peer.m_characterID); c.Tick(2); status = String.Join("\n", c.DescribeRuntime());
        Check("missing replicated player object is distinguished", status.Contains("characterFound=False; valid=False"));
        var config = new ConfigFile(); config.Values["Pickables"] = false;
        c = new Controller(new Settings(config), 99); c.Add(data, view); status = String.Join("\n", c.DescribeRuntime());
        Check("disabled group is distinguished from missing load events", status.Contains("validRegistrations=1; tracked=0") && status.Contains("native=Pickables; enabled=False"));
        prefab.GetComponent<ZNetView>().m_persistent = false;
        Check("prefab filter rejection has native details", String.Join("\n", c.DescribeRuntime()).Contains("native=None; enabled=False; reserved=False; view=True; persistent=False"));

        int before = Plugin.StatusSchedules;
        typeof(CharacterArrived).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        Check("native character announcement schedules bounded report hook", Plugin.StatusSchedules == before + 1);
        Console.WriteLine("PASS: " + checks + " read-only runtime diagnostics, peer rejection, prefab filters and native login-hook assertions. No live scene tested.");
    }
}
