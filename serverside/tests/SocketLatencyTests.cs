using System;
using System.Reflection;
using BepInEx.Configuration;
using Steamworks;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class SocketLatencyTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static Controller Setup(ISocket socket, out ZDO player)
    {
        ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene(); Time.unscaledTime = 0;
        SteamNetworkingSockets.Calls = SteamGameServerNetworkingSockets.Calls = 0;
        SteamGameServerNetworkingSockets.Throw = SteamGameServerNetworkingSockets.Fail = false; SteamGameServerNetworkingSockets.Ping = 50;
        ZNet.instance.Peers.Add(new ZNetPeer { m_uid = 1, m_characterID = new ZDOID(1), m_socket = socket });
        player = new ZDO { Owner = 1 }; ZDOMan.instance.Data[new ZDOID(1)] = player;
        var prefab = new GameObject(); prefab.Add(new ZNetView()); prefab.Add(new Door()); ZNetScene.instance.Prefabs[10] = prefab;
        var c = new Controller(new Settings(new ConfigFile()), 99); Plugin.Current = c; return c;
    }
    private static void Tick(Controller c, float now) { Time.unscaledTime = now; c.Tick(now); c.Service(now); }
    private static ZDO Door(Controller c, long owner)
    {
        var data = new ZDO { Prefab = 10, Owner = owner }; var root = new GameObject();
        var view = root.Add(new ZNetView { Data = data }); root.Add(new Door()); c.Add(data, view); return data;
    }
    private static AuthorityGrid Grid(Controller c)
    { return ((AuthorityWorld)typeof(Controller).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c)).Grid; }
    private static void Run()
    {
        ZDO player; var steam = new ZSteamSocket(new HSteamNetConnection(123)); var c = Setup(steam, out player); var door = Door(c, 99);
        SteamGameServerNetworkingSockets.Ping = 200;
        Tick(c, 0);
        Check("server grid survives uninitialized Steam client", door.Owner == 1 && Plugin.Current == c);
        Check("Steam client API is never touched", SteamNetworkingSockets.Calls == 0);
        Check("GameServer API reads actual private native connection", SteamGameServerNetworkingSockets.Calls == 1 && SteamGameServerNetworkingSockets.LastHandle == steam.ActualHandle);
        Check("server Steam RTT still activates adaptive grid", Grid(c).LargeCoverage(1));
        for (int i = 1; i < 20; i++) Tick(c, i * .04f);
        Check("native Steam read remains once per second", SteamGameServerNetworkingSockets.Calls == 1);
        SteamGameServerNetworkingSockets.Ping = 149; Tick(c, 1);
        Check("valid server telemetry keeps existing shrink hysteresis", !Grid(c).LargeCoverage(1));

        c = Setup(steam, out player); door = Door(c, 99); SteamGameServerNetworkingSockets.Throw = true;
        Tick(c, 0);
        Check("missing GameServer telemetry cannot stop local delegation", door.Owner == 1 && Grid(c).HasPeers);
        for (int i = 1; i < 20; i++) Tick(c, i * .04f);
        Check("failed samples are cached instead of retried every player pass", SteamGameServerNetworkingSockets.Calls == 1);
        ZNet.instance.Peers.Add(new ZNetPeer { m_uid = 2, m_characterID = new ZDOID(2), m_socket = steam });
        ZDOMan.instance.Data[new ZDOID(2)] = new ZDO { Owner = 2 }; Tick(c, .9f); Tick(c, 1.1f);
        Check("unknown RTT never bypasses existing client-to-server safety", door.Owner == 1 && c.PendingTransfers == 0);
        SteamGameServerNetworkingSockets.Throw = false; player.Rotation = new Quaternion { Yaw = 45 }; Tick(c, 2.2f);
        Check("fresh activity retries protected transfer after telemetry recovers", c.PendingTransfers == 1 && door.Owner == 1);
        c.Service(2.31f);
        Check("recovered RTT permits normal guarded server handoff", door.Owner == 99 && c.PendingTransfers == 0);

        c = Setup(steam, out player); SteamGameServerNetworkingSockets.Ping = 200; Tick(c, 0);
        SteamGameServerNetworkingSockets.Fail = true; Tick(c, 1);
        Check("API result failure preserves previous large coverage", Grid(c).LargeCoverage(1));
        c = Setup(new ZSteamSocket(HSteamNetConnection.Invalid), out player); door = Door(c, 99); Tick(c, 0);
        Check("invalid Steam handle does not call native API or stop grid", SteamGameServerNetworkingSockets.Calls == 0 && SteamNetworkingSockets.Calls == 0 && door.Owner == 1);

        var other = new FixtureSocket { Ping = 230 }; c = Setup(other, out player); Tick(c, 0);
        Check("other socket transports keep their native quality measurement", other.Reads == 1 && Grid(c).LargeCoverage(1));
        Check("other socket telemetry never touches Steam APIs", SteamGameServerNetworkingSockets.Calls == 0 && SteamNetworkingSockets.Calls == 0);
        other.ThrowQuality = true; Tick(c, 1);
        Check("other transport exceptions are isolated and retain adaptive size", Grid(c).HasPeers && Grid(c).LargeCoverage(1));
        int calls = other.Reads; Tick(c, 1.1f);
        Check("other transport failures also respect cache interval", other.Reads == calls);
        Console.WriteLine("PASS: " + checks + " dedicated Steam API selection, exception isolation, cached failures, transport fallback and retained handoff-safety assertions. No live Steam session tested.");
    }
    private static int Main() { try { Run(); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
}
