using System;
using TooManyLagsStandalone;
using UnityEngine;
internal static class CameraRuntimeTests
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    private static void Yaw(float value)
    { double angle = value * Math.PI / 180; GameCamera.instance.transform.forward = new Vector3 { x = (float)Math.Sin(angle), z = (float)Math.Cos(angle) }; }
    private static PlayerPeer Peer(long uid, long id, float z)
    {
        var data = new ZDO { m_uid = new ZDOID(id), Owner = uid, Position = new Vector3 { z = z }, Coverage = 0 };
        ZDOMan.instance.Objects.Add(data.m_uid, data); return new PlayerPeer { Uid = uid, Character = data, Reference = data.Position };
    }
    private static void Poll(float now) { Time.unscaledTime = now; Plugin.Controller.Poll(now); }
    private static void Sample(CameraPublisher publisher, float now) { Time.unscaledTime = now; publisher.Sample(now); }
    public static int Main()
    {
        try
        {
            ZNet.instance = new ZNet { Server = true }; ZDOMan.instance = new ZDOMan();
            Plugin.Network = new Network(); Plugin.Network.Modded.Add(11); Plugin.Network.Modded.Add(22);
            PlayerPeer host = Peer(11, 1, 0), remote = Peer(22, 2, 24);
            Plugin.Network.Current.Add(host); Plugin.Network.Current.Add(remote);
            Player.m_localPlayer = new Player { View = new ZNetView { Data = host.Character } };
            GameCamera.instance = new GameCamera(); Yaw(0);
            var door = new ZDO { m_uid = new ZDOID(3), Prefab = 1, Owner = 22, Position = new Vector3 { x = 4.1f, z = -3.9f } };
            ZDOMan.instance.Objects.Add(door.m_uid, door);
            Plugin.Controller = new AuthorityController(new Settings()); Poll(0);
            Check(Plugin.Controller.Handoffs.Requests == 0, "rear object has no initial request");
            var publisher = new CameraPublisher(.05f); Sample(publisher, 0);
            Check(ZDOMan.instance.Forced == 1 && host.Character.Coverage == 0, "listen host publishes without requiring a server peer");
            Yaw(8); Sample(publisher, .05f);
            Check(host.Character.Coverage == 8 && ZDOMan.instance.Forced == 2, "changed camera publishes adjacent union with native priority");
            Check(Plugin.Controller.Mobs.Ticks == 1, "camera callback queues rather than reenters controller");
            Poll(.05f);
            Check(Plugin.Controller.Mobs.Ticks == 2 && Plugin.Controller.Handoffs.Requested == 11 && door.Owner == 11, "local host camera wakes next poll and new red object request without one-second cadence wait");
            for (int i = 1; i <= 10; i++) { Yaw(8 + i); Sample(publisher, .05f + i * .051f); Poll(.05f + i * .051f); }
            Check(ZDOMan.instance.Forced == 2 && Plugin.Controller.Mobs.Ticks == 2, "unchanged overlap causes no additional priority sends or event passes");
            remote.Character.Coverage = 8; Time.unscaledTime = .6f; Plugin.Controller.Changed(remote.Character);
            Poll(.6f); Check(Plugin.Controller.Mobs.Ticks == 3, "remote camera receipt wakes next native poll");
            Plugin.Controller.Changed(remote.Character); Poll(.61f);
            Check(Plugin.Controller.Mobs.Ticks == 3, "duplicate remote receipt does not wake");
            Plugin.Network.Modded.Remove(22); Poll(1.6f); int before = Plugin.Controller.Mobs.Ticks;
            remote.Character.Coverage = 2; Plugin.Controller.Changed(remote.Character); Poll(1.61f);
            Check(Plugin.Controller.Mobs.Ticks == before, "unmodified client cannot wake camera cadence with stale data");
            publisher.Reset(true); int forced = ZDOMan.instance.Forced; Poll(1.62f);
            Check(host.Character.Coverage == -1 && Plugin.Controller.Mobs.Ticks == before + 1, "camera invalidation wakes body fallback on listen host");
            publisher.Reset(true); Check(ZDOMan.instance.Forced == forced, "repeated invalidation sends nothing");
            Plugin.Controller.Stop(); before = Plugin.Controller.Mobs.Ticks; host.Character.Coverage = 8;
            Plugin.Controller.CameraChanged(host.Character); Poll(2); Check(Plugin.Controller.Mobs.Ticks == before, "stopped/world-reset controller cannot be awakened");
            Plugin.Controller = null; ZNet.instance.Server = false; ZNet.instance.ServerPeer = new object();
            Plugin.Network.Confirmed = false; forced = ZDOMan.instance.Forced;
            publisher = new CameraPublisher(.05f); Sample(publisher, 2.5f);
            Check(ZDOMan.instance.Forced == forced, "remote camera waits for authenticated host angle");
            Plugin.Network.Confirmed = true;
            publisher = new CameraPublisher(.05f); Yaw(0); Sample(publisher, 3); forced = ZDOMan.instance.Forced;
            Sample(publisher, 3.02f); Check(ZDOMan.instance.Forced == forced, "client sampling obeys minimum interval");
            Yaw(8); Sample(publisher, 3.06f); Check(ZDOMan.instance.Forced == forced + 1 && host.Character.Coverage == 8, "remote client also prioritizes real direction changes");
            GameCamera.Free = true; Sample(publisher, 3.12f); Check(host.Character.Coverage == -1, "free camera restores body fallback");
            host.Character.Owner = 22; forced = ZDOMan.instance.Forced; GameCamera.Free = false; Yaw(90); Sample(publisher, 3.18f);
            Check(ZDOMan.instance.Forced == forced, "foreign-owned player cannot publish");
            host.Character.Owner = 11; ZNet.IsSinglePlayer = true; Sample(publisher, 3.24f);
            Check(ZDOMan.instance.Forced == forced, "single-player samples remain dormant");
            ZNet.IsSinglePlayer = false; GameCamera.Free = false;
            CameraHeading.Configure(0); Check(CameraHeading.Coverage(20) == 0, "zero overlap keeps the nearest single direction");
            CameraHeading.Configure(45); Check(CameraHeading.Coverage(0) == 8, "maximum overlap chooses a pair at the direction center");
            CameraHeading.Configure(30); Check(CameraHeading.Coverage(20) == 8, "default overlap remains unchanged");
            CameraHeading.Configure(30, .1f); publisher.Reset(); Yaw(0); Sample(publisher, 4);
            Yaw(20); Sample(publisher, 4.051f);
            Check(host.Character.Coverage == 32, "production publisher sends a three-direction prediction");
            forced = ZDOMan.instance.Forced; Sample(publisher, 4.102f);
            Check(ZDOMan.instance.Forced == forced, "unchanged held prediction adds no priority send");
            Sample(publisher, 4.2f);
            Check(host.Character.Coverage == 8 && ZDOMan.instance.Forced == forced + 1, "prediction expires once after rotation stops");
            forced = ZDOMan.instance.Forced;
            for (int i = 1; i <= 1000; i++) Sample(publisher, 4.2f + i * .051f);
            Check(ZDOMan.instance.Forced == forced, "idle prediction creates no repeated network work");
            host.Character.Position.x += 100f; Yaw(40); Sample(publisher, 55.3f);
            Check(host.Character.Coverage == 1, "large position jump resets predictive history");
            CameraHeading.Configure(30);
            Console.WriteLine("PASS: " + checks + " production camera publisher/controller assertions: listen host, remote receipt, zero-delay red fields, optional-client fallback and lifecycle. Managed transport/handoff/Unity doubles."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
