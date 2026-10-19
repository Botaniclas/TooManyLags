using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class MobGridTests
{
    private static int checks;
    private static void Check(string label, bool ok) { checks++; if (!ok) throw new Exception(label); }
    private static void Tick(Controller c, float at) { Time.unscaledTime = at; c.Tick(at); c.Service(at); }
    private static void Service(Controller c, float at) { Time.unscaledTime = at; c.Service(at); }
    private static GameObject Creature(bool friendly = false, bool tameable = false)
    {
        var root = new GameObject(); root.Add(new ZNetView());
        var weapon = new GameObject(); var drop = weapon.Add(new ItemDrop());
        drop.m_itemData.m_shared.m_attack = new Attack { m_attackAnimation = "bite" };
        drop.m_itemData.m_shared.m_secondaryAttack = new Attack { m_attackAnimation = "spray", m_loopingAttack = true };
        root.Add(new Humanoid { NativeFaction = friendly ? Character.Faction.Players : Character.Faction.Animals, m_defaultItems = new[] { weapon } });
        root.Add(new MonsterAI()); root.Add(new ZSyncAnimation()); if (tameable) root.Add(new Tameable()); return root;
    }
    private static Controller Reset(ConfigFile cfg = null)
    {
        // Existing scenarios explicitly exercise the optional nearest-player
        // mode. Production defaults are checked separately by ConfigProbe.
        cfg = cfg ?? new ConfigFile();
        if (!cfg.Values.ContainsKey("ServerOwnedWhenMultiplePlayersInMobCone")) cfg.Values["ServerOwnedWhenMultiplePlayersInMobCone"] = false;
        if (!cfg.Values.ContainsKey("EnableMobAttackForecast")) cfg.Values["EnableMobAttackForecast"] = false;
        Physics.Hits.Clear(); Physics.Saturated = false; Physics.Queries = 0; Time.SimulatedTime = null; Character.All.Clear();
        Time.unscaledTime = 0; ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
        ZNetScene.instance.Prefabs[20] = Creature(); ZNetScene.instance.Prefabs[21] = Creature(true);
        ZNetScene.instance.Prefabs[22] = Creature(false, true);
        ZNetScene.instance.Prefabs[23] = Creature(); ZNetScene.instance.Prefabs[23].GetComponent<Humanoid>().NativeBoss = true;
        var c = new Controller(new Settings(cfg), 99); Plugin.Current = c; return c;
    }
    private static ZNetPeer Player(long uid, float x, float z, float y = 0)
    {
        var peer = new ZNetPeer { m_uid = uid, m_characterID = new ZDOID(uid) }; ZNet.instance.Peers.Add(peer);
        ZDOMan.instance.Data[peer.m_characterID] = new ZDO { Owner = uid }; Move(peer, x, z, y); return peer;
    }
    private static void Move(ZNetPeer peer, float x, float z, float y = 0)
    { peer.Position = new Vector3(x, y, z); ZDOMan.instance.Data[peer.m_characterID].Position = peer.Position; }
    private static ZNetView Mob(Controller c, long owner = 99, int prefab = 20)
    {
        var root = Creature(prefab == 21, prefab == 22); var view = root.GetComponent<ZNetView>();
        root.GetComponent<Humanoid>().NativeBoss = prefab == 23;
        view.Data = new ZDO { Prefab = prefab, Owner = owner };
        ZNetScene.instance.Instances[view.Data] = view;
        Character.All.Add(root.GetComponent<Humanoid>());
        ZDOMan.instance.Data[view.Data.m_uid] = view.Data; c.Add(view.Data, view); return view;
    }
    private static void Trigger(ZNetView view, string name = "bite", long source = 0)
    { view.GetComponent<ZSyncAnimation>().Trigger(source == 0 ? view.Data.Owner : source, name); }
    private static void EndRemote(Controller c, ZNetView view, float at)
    { view.GetComponent<Humanoid>().Attacking = false; Service(c, at); Service(c, at + .025f); }
    private static void Selection()
    {
        var c = Reset(); var first = Player(1, 0, 3); var view = Mob(c); Tick(c, 0);
        Check("idle server mob delegates immediately to a sole grid player", view.Data.Owner == 1 && c.PendingMobTransfers == 0);
        Check("first delegation sends the native new-owner snapshot immediately", first.m_rpc.Sent.Count == 1);
        Check("native initial assignment cannot bypass the live first-attack guard", c.NativeDestination(view.Data, 99) == 99);
        int guardReads = c.MobGuardChecks, coneReads = c.MobConeChecks, ownerReads = view.Data.OwnerReads;
        for (int i = 1; i < 200; i++) Tick(c, i * .1f);
        Check("settled solo mob has no repeated attack checks or owner reads", c.MobGuardChecks == guardReads && view.Data.OwnerReads == ownerReads);
        Check("solo mob never performs a cone search", c.MobConeChecks == coneReads && c.SharedMobCount == 0);
        var second = Player(2, 1, 3); Tick(c, 21);
        Check("overlap chooses the nearest player ahead instead of server", view.Data.Owner == 1 && c.SharedMobCount == 1);
        Move(first, 3, 3); Tick(c, 22); Service(c, 22.11f);
        Check("player motion within the same cell can change the cone winner", view.Data.Owner == 2);
        Check("old and new owners receive actual changed-owner snapshots", first.m_rpc.Sent.Count >= 2 && second.m_rpc.Sent.Count >= 1);
        view.Data.SetRotation(new Quaternion { Yaw = 180 }); Tick(c, 23); Service(c, 23.11f);
        Check("mob turning away returns shared ownership to server", view.Data.Owner == 99);
        view.Data.SetRotation(new Quaternion { Yaw = 0 }); Tick(c, 24);
        Check("server source immediately delegates when a forward player returns", view.Data.Owner == 2);
        Move(second, 30, 0); Tick(c, 25); Tick(c, 26.1f); Service(c, 26.22f);
        Check("leaving overlap restores the actual solo field owner", view.Data.Owner == 1 && c.SharedMobCount == 0);
        Move(first, 40, 0); Tick(c, 27); Tick(c, 31); Service(c, 31.2f);
        Check("vacant mob fields return to default server ownership", view.Data.Owner == 99);

        c = Reset(); Player(1, 4, 4); Player(2, -2, -2); view = Mob(c); Tick(c, 0);
        Check("exact 45 degree cone edge included", view.Data.Owner == 1);
        c = Reset(); Player(1, 4.01f, 4); Player(2, -2, -2); view = Mob(c); Tick(c, 0);
        Check("player just beyond cone angle excluded", view.Data.Owner == 99);
        var wide = new ConfigFile(); wide.Values["GridCellSizeMeters"] = 16f;
        c = Reset(wide); Player(1, 0, 8); Player(2, 0, -2); view = Mob(c); Tick(c, 0); Tick(c, .25f);
        Check("exact configured range included once overlap matures", view.Data.Owner == 1);
        c = Reset(wide); Player(1, 0, 8.01f); Player(2, 0, -2); view = Mob(c); Tick(c, 0); Tick(c, .25f);
        Check("player beyond range excluded", view.Data.Owner == 99);
        c = Reset(); Player(1, 0, 1, 9); Player(2, 0, -2); view = Mob(c); Tick(c, 0);
        Check("range uses actual 3D distance rather than an infinite-height column", view.Data.Owner == 99);
        c = Reset(); first = Player(1, 0, 2); Player(2, 0, 3); ZDOMan.instance.Data[first.m_characterID].Health = 0; view = Mob(c); Tick(c, 0);
        Check("dead player is not a cone recipient", view.Data.Owner == 2);
        c = Reset(); first = Player(1, 0, 2); second = Player(2, 0, 2); view = Mob(c, 2); Tick(c, 0);
        Check("equal-distance tie retains the current eligible owner", view.Data.Owner == 2 && c.PendingMobTransfers == 0);
        var cfg = new ConfigFile(); cfg.Values["HostileMobConeRangeMeters"] = 2f;
        c = Reset(cfg); Player(1, 0, 3); Player(2, 0, -2); view = Mob(c); Tick(c, 0);
        Check("cone radius config affects shared selection", view.Data.Owner == 99);
    }
    private static void Guards()
    {
        var c = Reset(); Player(1, 0, 2); var view = Mob(c); var actor = view.GetComponent<Humanoid>();
        actor.m_currentAttack = new Attack { Done = false }; Tick(c, 0);
        Check("unfinished server Attack blocks even before Animator enters attack", view.Data.Owner == 99 && c.PendingMobTransfers == 1);
        actor.Attacking = true; Service(c, .025f);
        Check("active server animation continues to block", view.Data.Owner == 99);
        actor.Attacking = false; actor.m_currentAttack.Done = true; Service(c, .05f);
        Check("server attack completion delegates on next due check without another grid interval", view.Data.Owner == 1 && c.PendingMobTransfers == 0);
        Player(2, 0, -2); Tick(c, 1); // owner is already nearest: no transfer work.
        Check("same cone owner needs no attack guard", c.PendingMobTransfers == 0);
        actor.Attacking = true; Trigger(view); view.Data.SetRotation(new Quaternion { Yaw = 180 }); Tick(c, 2);
        Check("shared mob is not reassigned during a remote attack", view.Data.Owner == 1 && c.PendingMobTransfers == 1);
        int coneReads = c.MobConeChecks;
        for (int i = 1; i <= 20; i++) Service(c, 2 + i * .025f);
        Check("waiting attack guard does not repeat unchanged cone/player searches", c.MobConeChecks == coneReads);
        EndRemote(c, view, 2.6f);
        Check("completed observed remote attack transfers after one idle step", view.Data.Owner == 2 && c.PendingMobTransfers == 0);

        c = Reset(); Player(1, 0, 3); Player(2, 0, -3); view = Mob(c, 1); actor = view.GetComponent<Humanoid>();
        view.Data.Alert = true; view.Data.SetRotation(new Quaternion { Yaw = 180 }); Trigger(view); Tick(c, 0);
        Check("trigger before Animator evaluation does not release ownership", view.Data.Owner == 1 && c.PendingMobTransfers == 1);
        actor.Attacking = true; Service(c, .025f); EndRemote(c, view, .05f);
        Check("startup observation catches next-frame animation and does not park forever", view.Data.Owner == 2);

        c = Reset(); Player(1, 0, 3); Player(2, 0, -3); Player(3, 2, 0); view = Mob(c, 1); actor = view.GetComponent<Humanoid>();
        view.Data.Alert = true; view.Data.SetRotation(new Quaternion { Yaw = 180 }); actor.Attacking = true; Trigger(view); Tick(c, 0);
        view.Data.SetOwner(3); Tick(c, .025f); Trigger(view, "bite", 1); actor.Attacking = false; Service(c, .05f); Service(c, .1f);
        Check("native owner revision invalidates old animation completion and old-sender triggers", view.Data.Owner == 3);
        actor.Attacking = true; Trigger(view, "bite", 3); Service(c, .15f); EndRemote(c, view, .2f);
        Check("new owner's actual observed attack establishes fresh permission history", view.Data.Owner == 2);

        c = Reset(); Player(1, 0, 3); Player(2, 0, -3); view = Mob(c, 1); actor = view.GetComponent<Humanoid>();
        view.Data.Alert = true; view.Data.SetRotation(new Quaternion { Yaw = 180 }); actor.Attacking = true; Trigger(view); Tick(c, 0);
        actor.Attacking = false; Service(c, .025f);
        actor.Attacking = true; Trigger(view); Service(c, .05f);
        Check("immediate second attack cancels the idle handoff window", view.Data.Owner == 1);
        EndRemote(c, view, .075f);
        Check("second real attack completion releases the newest selection", view.Data.Owner == 2);

        c = Reset(); Player(1, 0, 3); Player(2, 0, -3); view = Mob(c, 1); actor = view.GetComponent<Humanoid>();
        actor.Attacking = true; Trigger(view); view.Data.SetRotation(new Quaternion { Yaw = 180 }); Tick(c, 0);
        Move(ZNet.instance.GetPeer(2), 0, 3); Tick(c, .1f); EndRemote(c, view, .15f);
        Check("cone is revalidated after waiting instead of assigning the departed candidate", view.Data.Owner == 99);

        c = Reset(); Player(1, 0, 3); Player(2, 0, -3); view = Mob(c, 1); actor = view.GetComponent<Humanoid>();
        actor.Attacking = true; Trigger(view, "spray"); view.Data.SetRotation(new Quaternion { Yaw = 180 }); view.Data.Alert = true; Tick(c, 0);
        actor.Attacking = false; Service(c, .05f); int guardReads = c.MobGuardChecks;
        for (int i = 1; i < 100; i++) Service(c, i);
        Check("looping attack never expires into forced transfer", view.Data.Owner == 1 && c.PendingMobTransfers == 1);
        Check("unknown loop end parks without permanent polling", c.MobGuardChecks == guardReads);
        Trigger(view, "attack_abort"); Service(c, 100); Service(c, 100.025f);
        Check("native loop abort plus idle releases guard", view.Data.Owner == 2);

        c = Reset(); Player(1, 0, 3); Player(2, 0, -3); view = Mob(c, 1); view.Data.Alert = true;
        view.Data.SetRotation(new Quaternion { Yaw = 180 }); Tick(c, 0); guardReads = c.MobGuardChecks;
        for (int i = 1; i < 100; i++) Service(c, i);
        Check("late-loaded combat without attack history never forces an idle guess", view.Data.Owner == 1 && c.PendingMobTransfers == 1);
        Check("unknown combat history is event-parked", c.MobGuardChecks == guardReads);
        actor = view.GetComponent<Humanoid>(); actor.Attacking = true; Trigger(view, "bite", 2); Service(c, 100);
        Check("non-owner trigger cannot manufacture source permission", view.Data.Owner == 1 && c.MobGuardChecks == guardReads);
        Trigger(view); Service(c, 100.025f); EndRemote(c, view, 100.05f);
        Check("new real owner attack establishes history and resumes transfer", view.Data.Owner == 2);

        Tick(c, 101); guardReads = c.MobGuardChecks; int cones = c.MobConeChecks, writes = view.Data.Writes;
        for (int i = 1; i <= 500; i++) Tick(c, 101 + i * .1f);
        Check("settled shared mob does not continually recheck attacks", c.MobGuardChecks == guardReads);
        Check("standing players and unchanged mob do not repeat cone evaluation or ownership writes", c.MobConeChecks == cones && view.Data.Writes == writes);

        c = Reset(); Player(1, 0, 3); view = Mob(c); view.GetComponent<MonsterAI>().Charging = true; Tick(c, 0);
        Check("server charging is protected", view.Data.Owner == 99);
        view.GetComponent<MonsterAI>().Charging = false; view.GetComponent<Humanoid>().Staggering = true; Service(c, .025f);
        Check("server stagger remains protected", view.Data.Owner == 99);
        view.GetComponent<Humanoid>().Staggering = false; Service(c, .05f); Check("finished stagger releases pending first delegation", view.Data.Owner == 1);
    }
    private static ConfigFile SharedDefaults(bool wide = false)
    {
        var cfg = new ConfigFile(); cfg.Values["ServerOwnedWhenMultiplePlayersInMobCone"] = true;
        if (wide) cfg.Values["GridCellSizeMeters"] = 16f;
        return cfg;
    }
    private static void MultiplePlayersAndBosses()
    {
        var defaults = new Settings(new ConfigFile());
        Check("production multiple-player server selection defaults on", defaults.MobConeServerWhenMultiple);
        Check("ordinary and boss range defaults are independent", defaults.MobConeRange == 8f && defaults.BossMobConeRange == 10f);
        var c = Reset(SharedDefaults()); Player(1, 0, 2); Player(2, 0, 4); var view = Mob(c); Tick(c, 0);
        Check("farther second cone player forces server even after nearer player was selected", view.Data.Owner == 99 && view.Data.Writes == 0);
        c = Reset(SharedDefaults()); Player(2, 0, 4); Player(1, 0, 2); view = Mob(c); Tick(c, 0);
        Check("multiple-player rule is independent of player iteration order", view.Data.Owner == 99);
        c = Reset(SharedDefaults()); Player(1, 0, 2); Player(2, 0, -2); view = Mob(c); Tick(c, 0);
        Check("one eligible forward player still receives shared mob", view.Data.Owner == 1);
        c = Reset(SharedDefaults()); Player(1, 0, -2); Player(2, 1, -2); view = Mob(c); Tick(c, 0);
        Check("no eligible forward player still uses server", view.Data.Owner == 99);
        c = Reset(SharedDefaults()); Player(1, 0, 2); var second = Player(2, 0, 3);
        ZDOMan.instance.Data[second.m_characterID].Health = 0; view = Mob(c); Tick(c, 0);
        Check("dead cone player does not force multiple-player rule", view.Data.Owner == 1);
        c = Reset(SharedDefaults(true)); Player(1, 0, 2); Player(2, 0, 8.01f); view = Mob(c); Tick(c, 0);
        Check("out-of-range cone player is not counted", view.Data.Owner == 1);
        c = Reset(SharedDefaults()); Player(1, 0, 2); second = Player(2, 3, 1); view = Mob(c); Tick(c, 0);
        Move(second, 1, 3); Tick(c, 1); Service(c, 1.11f);
        Check("same-cell second-player entry changes shared mob to server", view.Data.Owner == 99);
        Move(second, 3, 1); Tick(c, 2);
        Check("same-cell cone exit restores sole cone player", view.Data.Owner == 1);

        c = Reset(SharedDefaults()); Player(1, 0, 2); second = Player(2, 3, 1); view = Mob(c, 1); Tick(c, 0);
        var actor = view.GetComponent<Humanoid>(); actor.Attacking = true; Trigger(view); Move(second, 1, 3); Tick(c, 1);
        Check("new multi-player server decision cannot bypass active attack guard", view.Data.Owner == 1 && c.PendingMobTransfers == 1);
        EndRemote(c, view, 1.03f);
        Check("observed completion commits server without an intermediate player assignment", view.Data.Owner == 99 && view.Data.Writes == 1);

        c = Reset(SharedDefaults()); Player(1, 0, 2); second = Player(2, 0, -2); view = Mob(c, 2); Tick(c, 0);
        Move(second, 0, 3); Service(c, .11f);
        Check("commit recounts current cone players rather than using the old one-player result", view.Data.Owner == 99);
        c = Reset(SharedDefaults()); Player(1, 0, 2); Player(2, 0, 20); view = Mob(c); Tick(c, 0);
        Check("multi-player cone rule does not override an exclusive grid field", view.Data.Owner == 1);

        c = Reset(SharedDefaults(true)); Player(1, 0, 9); Player(2, 0, -2); view = Mob(c, 99, 23); Tick(c, 0);
        Check("native boss flag gives default ten-metre cone", view.Data.Owner == 1);
        c = Reset(SharedDefaults(true)); Player(1, 0, 9); Player(2, 0, -2); view = Mob(c); Tick(c, 0);
        Check("ordinary mob keeps eight-metre cone in the same scene", view.Data.Owner == 99);
        c = Reset(SharedDefaults(true)); Player(1, 0, 10); Player(2, 0, -2); view = Mob(c, 99, 23); Tick(c, 0);
        Check("boss exact ten-metre edge included", view.Data.Owner == 1);
        c = Reset(SharedDefaults(true)); Player(1, 0, 10.01f); Player(2, 0, -2); view = Mob(c, 99, 23); Tick(c, 0);
        Check("player beyond boss distance excluded", view.Data.Owner == 99);
        c = Reset(SharedDefaults(true)); Player(1, 0, 9); Player(2, 0, 9.5f); view = Mob(c, 99, 23); Tick(c, 0);
        Check("multiple-player rule also applies inside the larger boss cone", view.Data.Owner == 99);
        var cfg = SharedDefaults(true); cfg.Values["BossMobConeRangeMeters"] = 6f;
        c = Reset(cfg); Player(1, 0, 7); Player(2, 0, -2); view = Mob(c, 99, 23); Tick(c, 0);
        Check("boss radius can be changed independently", view.Data.Owner == 99);
        c = Reset(cfg); Player(1, 0, 7); Player(2, 0, -2); view = Mob(c); Tick(c, 0);
        Check("boss radius override leaves ordinary radius intact", view.Data.Owner == 1);
    }
    private static void Lifecycle()
    {
        var c = Reset(); var first = Player(1, 0, 2); var view = Mob(c, 99, 21); Tick(c, 0);
        Check("friendly player summons remain excluded", view.Data.Owner == 99 && c.TrackedObjects == 0);
        view = Mob(c, 99, 22); Tick(c, .1f); Check("untamed tameable starts with hostile grid policy", view.Data.Owner == 1);
        view.Data.Set(ZDOVars.s_tamed, true); view.Data.SetOwner(99); Tick(c, .2f);
        Check("native tame flag switches to existing tame policy", view.Data.Owner == 1);
        view.Data.Set(ZDOVars.s_alert, true); view.Data.SetOwner(99); Tick(c, .3f);
        Check("tamed combat still keeps existing native protection", view.Data.Owner == 99);
        view.Data.Set(ZDOVars.s_tamed, false); view.Data.Set(ZDOVars.s_alert, false); Tick(c, .4f);
        Check("untaming reactivates hostile grid policy", view.Data.Owner == 1);
        c = Reset(); Player(1, 0, -2); Player(2, 0, 2); view = Mob(c, 1); view.GetComponent<Humanoid>().Attacking = true; Trigger(view); Tick(c, 0);
        Check("test creates one pending mob handoff", c.PendingMobTransfers == 1);
        c.Remove(view.Data); Service(c, 1); Trigger(view);
        Check("unload removes guard scheduler and cone references", c.TrackedObjects == 0 && c.PendingMobTransfers == 0 && c.SharedMobCount == 0);
        c = Reset(); first = Player(1, 0, -2); Player(2, 0, 2); view = Mob(c, 1); view.GetComponent<Humanoid>().Attacking = true; Trigger(view); Tick(c, 0);
        view.Data.SetOwner(0); Tick(c, .1f); Service(c, 1);
        Check("native zero-owner release cancels guard and is not overridden", view.Data.Owner == 0 && c.PendingMobTransfers == 0);
        c = Reset(); Player(1, 0, -2); Player(2, 0, 2); view = Mob(c, 1); view.GetComponent<Humanoid>().Attacking = true; Trigger(view); Tick(c, 0);
        ZNet.instance.Peers.Clear(); Tick(c, .1f); int reads = c.MobGuardChecks; Service(c, 2);
        Check("last player disconnect drains pending mob work", c.PendingMobTransfers == 0 && c.SharedMobCount == 0 && c.MobGuardChecks == reads);
        var cfg = new ConfigFile(); cfg.Values["HostileMobs"] = false; c = Reset(cfg); Player(1, 0, 2); view = Mob(c); Tick(c, 0);
        Check("hostile category can be disabled independently", view.Data.Owner == 99 && c.TrackedObjects == 0);
        cfg = new ConfigFile(); cfg.Values["TamedAnimals"] = false; c = Reset(cfg); Player(1, 0, 2); view = Mob(c, 99, 22); Tick(c, 0);
        Check("disabled tame category does not disable wild counterpart", view.Data.Owner == 1);
        view.Data.Set(ZDOVars.s_tamed, true); view.Data.SetOwner(99); Tick(c, .1f);
        Check("tamed counterpart stays unmanaged when tame category disabled", view.Data.Owner == 99);
    }
    private static ItemDrop.ItemData.SharedData Ability(Controller c, float cooldown = 1f, bool second = false)
    {
        var shared = ZNetScene.instance.Prefabs[20].GetComponent<Humanoid>().m_defaultItems[0].GetComponent<ItemDrop>().m_itemData.m_shared;
        shared.m_aiAttackInterval = cooldown; shared.m_aiAttackRange = 4; shared.m_aiAttackMaxAngle = 20;
        shared.m_secondaryAttack = null;
        if (second)
        {
            var item = new GameObject(); item.Add(new ItemDrop()).m_itemData.m_shared = new ItemDrop.ItemData.SharedData {
                m_aiAttackInterval = cooldown, m_aiAttackRange = 4, m_aiAttackMaxAngle = 20, m_attack = new Attack { m_attackAnimation = "kick" } };
            ZNetScene.instance.Prefabs[20].GetComponent<Humanoid>().m_randomWeapon = new[] { item };
        }
        return shared;
    }
    private static Controller ForecastSetup(bool enabled = true)
    {
        var cfg = new ConfigFile(); cfg.Values["EnableMobAttackForecast"] = enabled;
        Controller c = Reset(cfg); Player(1, 0, 3); Player(2, 0, -3);
        var target = new GameObject(); target.Add(new ZNetView { Data = ZDOMan.instance.Data[ZNet.instance.Peers[1].m_characterID] });
        target.Add(new Player { NativeFaction = Character.Faction.Players }); Physics.Hits.Add(target.Add(new Collider()));
        Character.All.Add(target.GetComponent<Player>());
        target.GetComponent<ZNetView>().Data.Prefab = 24; ZNetScene.instance.Prefabs[24] = target;
        ZNetScene.instance.Instances[target.GetComponent<ZNetView>().Data] = target.GetComponent<ZNetView>();
        return c;
    }
    private static ZNetView ForecastSource(Controller c)
    {
        var view = Mob(c, 1); view.Data.SetRotation(new Quaternion { Yaw = 180 });
        view.GetComponent<Humanoid>().Attacking = true; Trigger(view); Tick(c, 0); return view;
    }
    private static void Forecast()
    {
        Check("forecast production default on", new Settings(new ConfigFile()).MobAttackForecast);
        var c = ForecastSetup(); Ability(c); var view = ForecastSource(c);
        Check("ability events observed before geometry or a completed attack", c.MobAbilityObservations == 1 && c.MobForecastChecks == 0 && c.MobForecastQueries == 0);
        EndRemote(c, view, .8f);
        Check("known cooling ability permits handoff without geometry", view.Data.Owner == 2 && c.MobForecastQueries == 0);
        c = ForecastSetup(); Ability(c); view = ForecastSource(c); EndRemote(c, view, .85f);
        Check("100ms plus 50ms cached RTT shorten one-second cooldown", view.Data.Owner == 1 && c.MobForecastQueries == 1 && c.HasWaitingForecast);
        Check("a ready nearby player avoids both a creature-list sweep and a physics query", c.MobForecastCharacterSweeps == 0 && c.MobForecastPhysicsQueries == 0);
        int geometry = c.MobForecastQueries, guard = c.MobGuardChecks;
        for (int i = 0; i < 1000; i++) Service(c, 1 + i * .02f);
        Check("geometry veto parks without repeated due polling", c.MobForecastQueries == geometry && c.MobGuardChecks == guard);
        view.Data.SetRotation(new Quaternion { Yaw = 150 }); Service(c, 22);
        Check("actual mob rotation wakes a geometry-blocked handoff", c.MobForecastQueries > geometry);
        geometry = c.MobForecastQueries;
        view.Data.SetRotation(view.Data.Rotation); Service(c, 22.1f);
        Check("unchanged rotation does not repeat physics query", c.MobForecastQueries == geometry);

        c = ForecastSetup(); Ability(c, 0); view = ForecastSource(c); EndRemote(c, view, .1f);
        geometry = c.MobForecastQueries; ZDO turningTarget = Physics.Hits[0].GetComponent<ZNetView>().Data;
        turningTarget.SetRotation(new Quaternion { Yaw = 80 }); Service(c, .2f);
        Check("target-only turning does not wake another mob's unchanged attack cone", c.MobForecastQueries == geometry && c.HasWaitingForecast);
        var turningReceipt = c.CaptureForecastReceive(turningTarget); turningTarget.Rotation = new Quaternion { Yaw = 100 };
        c.ForecastReceived(turningTarget, turningReceipt); Service(c, .3f);
        Check("native target rotation import also avoids an irrelevant geometry evaluation", c.MobForecastQueries == geometry && c.HasWaitingForecast);
        view.Data.SetRotation(new Quaternion { Yaw = 179 }); Service(c, .4f);
        Check("turning while a ready attack still fits does not bypass the forecast", c.MobForecastQueries > geometry && c.HasWaitingForecast && view.Data.Owner == 1);
        view.Data.InternalSetPosition(new Vector3(2, 0, 0)); Service(c, .5f);
        Check("actual mob movement out of attack alignment releases the parked handoff", view.Data.Owner == 2 && !c.HasWaitingForecast);

        c = ForecastSetup(); Ability(c, 0); view = ForecastSource(c); EndRemote(c, view, .1f);
        view.GetComponent<Humanoid>().Attacking = true; Trigger(view); geometry = c.MobForecastQueries;
        view.Data.InternalSetPosition(new Vector3(3, 0, 0)); Service(c, .2f);
        Check("motion during a renewed attack cannot waive its native busy guard", view.Data.Owner == 1 && c.MobForecastQueries == geometry);

        c = ForecastSetup(); Ability(c, 0); view = ForecastSource(c); EndRemote(c, view, .1f);
        geometry = c.MobForecastQueries; ZDO targetData = Physics.Hits[0].GetComponent<ZNetView>().Data;
        targetData.InternalSetPosition(targetData.Position); Service(c, .2f);
        Check("unchanged target position creates no geometry job", c.MobForecastQueries == geometry);
        targetData.InternalSetPosition(new Vector3(3, 0, -3)); Service(c, .3f);
        Check("real target movement wakes model without a new grid interval", view.Data.Owner == 2 && c.MobForecastQueries > geometry);
        c = ForecastSetup(); Ability(c, 0); view = ForecastSource(c); EndRemote(c, view, .1f);
        targetData = Physics.Hits[0].GetComponent<ZNetView>().Data; Physics.Hits.Clear(); Character.All.Remove(ZNetScene.instance.Instances[targetData].GetComponent<Player>()); ZNetScene.instance.Instances.Remove(targetData); c.Remove(targetData); Service(c, .2f);
        Check("unmanaged target unload wakes a blocked prediction", view.Data.Owner == 2 && !c.HasWaitingForecast);
        c = ForecastSetup(); Ability(c, 0); view = ForecastSource(c); EndRemote(c, view, .1f);
        geometry = c.MobForecastQueries; var receipt = c.CaptureForecastReceive(view.Data);
        c.ForecastReceived(view.Data, receipt); Service(c, .2f);
        Check("unchanged native import does not wake prediction", c.MobForecastQueries == geometry);
        receipt = c.CaptureForecastReceive(view.Data); view.Data.Rotation = new Quaternion { Yaw = 150 };
        c.DataReceived(view.Data, 1); c.ForecastReceived(view.Data, receipt); Service(c, .3f);
        Check("direct native rotation import also wakes forecast", view.Data.Owner == 2 && c.MobForecastQueries > geometry);
        c = ForecastSetup(); Ability(c, 0); view = ForecastSource(c); EndRemote(c, view, .1f);
        geometry = c.MobForecastQueries;
        var far = new GameObject(); var farView = far.Add(new ZNetView { Data = new ZDO { Prefab = 24, Position = new Vector3(200, 0, 200) } });
        far.Add(new Player()); ZNetScene.instance.Instances[farView.Data] = farView;
        farView.Data.InternalSetPosition(new Vector3(210, 0, 200)); Service(c, .2f);
        Check("faraway actor movement cannot wake local forecast geometry", c.MobForecastQueries == geometry);
        Move(ZNet.instance.Peers[0], 40, 3); Tick(c, .3f); geometry = c.MobForecastQueries;
        Move(ZNet.instance.Peers[0], 41, 3); Tick(c, .4f);
        Check("faraway player sample cannot defeat the blocked-geometry cache", c.MobForecastQueries == geometry);
        targetData = Physics.Hits[0].GetComponent<ZNetView>().Data;
        targetData.InternalSetPosition(new Vector3(200, 0, 200)); Service(c, .5f);
        Check("teleport departure considers the old nearby position", !c.HasWaitingForecast);
        c = ForecastSetup(); Ability(c, 0); view = ForecastSource(c); Physics.Hits[0].GetComponent<Player>().RadiusUnavailable = true; EndRemote(c, view, .1f);
        Check("unready target capsule conservatively blocks only its forecast", view.Data.Owner == 1 && c.HasWaitingForecast);
        targetData = Physics.Hits[0].GetComponent<ZNetView>().Data; targetData.InternalSetPosition(new Vector3(1, 0, -3)); Service(c, .2f);
        Check("pre-Awake target movement does not stop the addon", view.Data.Owner == 1 && c.HasWaitingForecast);

        c = ForecastSetup(); Ability(c, 1, true); view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("an unobserved alternate ability remains ready", view.Data.Owner == 1 && c.HasWaitingForecast);
        Time.unscaledTime = .3f; view.GetComponent<Humanoid>().Attacking = true; Trigger(view, "kick"); geometry = c.MobForecastQueries; Service(c, .3f);
        Check("renewed actual attack blocks before forecast geometry", c.MobForecastQueries == geometry && !c.HasWaitingForecast);
        EndRemote(c, view, .35f);
        Check("observing the alternate ability updates its own cooldown", view.Data.Owner == 2 && c.MobAbilityObservations == 2);
        c = ForecastSetup(); Ability(c);
        var unarmed = new GameObject(); var fallback = unarmed.Add(new ItemDrop());
        fallback.m_itemData.m_shared = new ItemDrop.ItemData.SharedData { m_aiAttackInterval = 2, m_aiAttackRange = 4, m_aiAttackMaxAngle = 20, m_attack = new Attack { m_attackAnimation = "bare" } };
        ZNetScene.instance.Prefabs[20].GetComponent<Humanoid>().m_unarmedWeapon = fallback;
        view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("native unarmed fallback also starts as a ready alternative", view.Data.Owner == 1);
        Time.unscaledTime = .3f; view.GetComponent<Humanoid>().Attacking = true; Trigger(view, "bare"); Service(c, .3f); EndRemote(c, view, .4f);
        Check("unarmed ability signals update their own cooldown through the same observer", view.Data.Owner == 2 && c.MobAbilityObservations == 2);
        var loopActor = ZNetScene.instance.Prefabs[20].GetComponent<Humanoid>();
        loopActor.m_defaultItems[0].GetComponent<ItemDrop>().m_itemData.m_shared.m_attack.m_loopingAttack = true;
        fallback.m_itemData.m_shared.m_attack.m_attackAnimation = "bite";
        Check("a colliding unarmed trigger cannot weaken a known looping-attack guard", new MobAttackProfile(loopActor).Triggers["bite"]);

        c = ForecastSetup(); Ability(c); ((FixtureSocket)ZNet.instance.Peers[0].m_socket).Ping = 0;
        view = ForecastSource(c); EndRemote(c, view, .05f);
        Check("missing RTT never invents remaining client cooldown", view.Data.Owner == 1 && c.HasWaitingForecast);
        c = ForecastSetup(); Ability(c); view = ForecastSource(c); Time.unscaledTime = .5f; Trigger(view); Service(c, .5f); EndRemote(c, view, 1);
        Check("repeated observed use refreshes cooldown immediately", view.Data.Owner == 2);
        Check("ability observations reuse one-second native ping cache", ((FixtureSocket)ZNet.instance.Peers[0].m_socket).Reads == 1);

        c = ForecastSetup(); var shared = Ability(c); var actor = ZNetScene.instance.Prefabs[20].GetComponent<Humanoid>();
        actor.m_randomWeapon = new[] { actor.m_defaultItems[0] };
        view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("same weapon referenced twice keeps a single shared cooldown", view.Data.Owner == 2);
        c = ForecastSetup(); shared = Ability(c, 1, true);
        ZNetScene.instance.Prefabs[20].GetComponent<Humanoid>().m_randomWeapon[0].GetComponent<ItemDrop>().m_itemData.m_shared.m_attack.m_attackAnimation = "bite";
        view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("ambiguous animation cannot cool multiple possible weapons", view.Data.Owner == 1 && c.MobAbilityObservations == 0);

        c = ForecastSetup(); Ability(c); view = ForecastSource(c); Trigger(view, "unknown", 1); Trigger(view, "bite", 2);
        Check("unknown triggers and non-owner packets cannot refresh cooldowns", c.MobAbilityObservations == 1);
        view.Data.SetOwner(3); Player(3, 3, 0); Tick(c, .1f);
        view.GetComponent<Humanoid>().Attacking = false; Service(c, .2f); Service(c, .4f);
        Check("owner revision clears previous source cooldown assumptions", view.Data.Owner == 3 && c.HasWaitingForecast);

        c = ForecastSetup(); Ability(c); view = ForecastSource(c); Time.SimulatedTime = .01f;
        EndRemote(c, view, 50);
        Check("cooldowns use native game time rather than elapsed unscaled polling time", view.Data.Owner == 2 && c.MobForecastQueries == 0);
        c = ForecastSetup(); Ability(c); view = ForecastSource(c); Time.SimulatedTime = -1;
        EndRemote(c, view, .1f);
        Check("backwards game clock invalidates inferred cooldowns", view.Data.Owner == 1 && c.HasWaitingForecast);

        c = ForecastSetup(); Ability(c); Character.All.Clear(); ZNetScene.instance.Instances.Clear(); Physics.Hits.Clear(); view = ForecastSource(c); Physics.Saturated = true; EndRemote(c, view, .9f);
        Check("saturated native broad phase is unknown rather than clear", view.Data.Owner == 1 && c.HasWaitingForecast && c.MobForecastPhysicsQueries == 1);
        c.Remove(view.Data); Service(c, 10);
        Check("unload removes model waiting state", !c.HasWaitingForecast && c.PendingMobTransfers == 0);
        c = ForecastSetup(); Ability(c); view = ForecastSource(c); EndRemote(c, view, .9f); ZNet.instance.Peers.Clear(); Tick(c, 2);
        Check("last-peer departure clears forecast waits", !c.HasWaitingForecast && c.PendingMobTransfers == 0);

        c = ForecastSetup(); shared = Ability(c, 0); shared.m_aiAttackRange = 0;
        view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("out-of-range target does not block handoff", view.Data.Owner == 2);
        c = ForecastSetup(); Ability(c, 0); Physics.Hits[0].GetComponent<Player>().Dead = true;
        view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("dead geometry target is ignored", view.Data.Owner == 2);
        c = ForecastSetup(); Ability(c, 0); Physics.Hits[0].GetComponent<Player>().NativeFaction = Character.Faction.Animals;
        view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("same-faction creature is not an enemy attack target", view.Data.Owner == 2);
        c = ForecastSetup(); shared = Ability(c, 0); shared.m_aiTargetType = ItemDrop.ItemData.AiTarget.Friend;
        Physics.Hits[0].GetComponent<Player>().NativeFaction = Character.Faction.Animals;
        view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("friend abilities can block when an eligible friend is nearby", view.Data.Owner == 1);

        c = ForecastSetup(); Ability(c, 0); Physics.Hits.Clear(); Character.All.Clear(); ZNetScene.instance.Instances.Clear();
        var building = new GameObject(); building.transform.position = new Vector3(0, 0, -3);
        building.Add(new StaticTarget { Surface = new Vector3(0, 0, -2), Center = new Vector3(0, 0, -3) }); Physics.Hits.Add(building.Add(new Collider()));
        view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("building attacks are included even with no player collider in the cone", view.Data.Owner == 1);
        view.GetComponent<MonsterAI>().m_attackPlayerObjects = false;
        c.ForecastTargetChanged(view.Data); Service(c, .2f);
        Check("native no-building-attack policy skips static targets", view.Data.Owner == 2);
        c = ForecastSetup(); Ability(c, 0); Physics.Hits.Clear(); Character.All.Clear(); ZNetScene.instance.Instances.Clear();
        var largeRoot = new GameObject(); largeRoot.Add(new ZNetView { Data = new ZDO { Position = new Vector3(0, 0, -10) } });
        Character.All.Add(largeRoot.Add(new Character { NativeFaction = Character.Faction.PlayerSpawned, Radius = 7 }));
        view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("large native AI target radius is not lost outside a physical capsule query", view.Data.Owner == 1 && c.HasWaitingForecast && c.MobForecastPhysicsQueries == 0);

        c = ForecastSetup(); shared = Ability(c); var profile = new MobForecastProfile(ZNetScene.instance.Prefabs[20].GetComponent<Humanoid>());
        var ability = profile.Abilities[0]; var origin = new Vector3(); var forward = new Vector3(0, 0, 1);
        double radians = 24 * Math.PI / 180;
        Check("normal angle includes additional five degrees", MobForecastProfile.Contains(ability, origin, forward, new Vector3((float)Math.Sin(radians)*3, 0, (float)Math.Cos(radians)*3), 0));
        radians = 26 * Math.PI / 180;
        Check("angle beyond padding excluded", !MobForecastProfile.Contains(ability, origin, forward, new Vector3((float)Math.Sin(radians)*3, 0, (float)Math.Cos(radians)*3), 0));
        Check("half-metre reach padding and native target radius included", MobForecastProfile.Contains(ability, origin, forward, new Vector3(0, 0, 5), .5f));
        Check("beyond padded reach excluded", !MobForecastProfile.Contains(ability, origin, forward, new Vector3(0, 0, 5.01f), .5f));
        shared.m_aiInvertAngleCheck = true; shared.m_aiAttackMaxAngle = 45;
        ability = new MobForecastProfile(ZNetScene.instance.Prefabs[20].GetComponent<Humanoid>()).Abilities[0]; radians = 41 * Math.PI / 180;
        Check("inverted angle expands allowed region instead of shrinking it", MobForecastProfile.Contains(ability, origin, forward, new Vector3((float)Math.Sin(radians)*3, 0, (float)Math.Cos(radians)*3), 0));
        Check("inverted cone still excludes its inner angle", !MobForecastProfile.Contains(ability, origin, forward, new Vector3(0, 0, 3), 0));
        shared.m_aiInvertAngleCheck = false; shared.m_aiAttackRangeMin = 3;
        ability = new MobForecastProfile(ZNetScene.instance.Prefabs[20].GetComponent<Humanoid>()).Abilities[0];
        Check("selection-only minimum range cannot manufacture a safe handoff", MobForecastProfile.Contains(ability, origin, forward, new Vector3(0, 0, .1f), 0));

        c = ForecastSetup(false); Ability(c, 0); view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("forecast switch restores original guards without model observation or geometry", view.Data.Owner == 2 && c.MobAbilityObservations == 0 && c.MobForecastQueries == 0);
        c = ForecastSetup(); Ability(c, 0); view = Mob(c); view.Data.SetRotation(new Quaternion { Yaw = 180 }); Tick(c, 0);
        Check("actual idle server source delegates without a speculative forecast", view.Data.Owner == 2 && c.MobForecastChecks == 0);
        c = ForecastSetup(); shared = Ability(c); shared.m_aiAttackRange = Single.NaN; view = ForecastSource(c); EndRemote(c, view, .1f);
        Check("unsupported metadata is not interpreted as a safe attack-free interval", view.Data.Owner == 1 && c.MobForecastQueries == 0);
    }
    private static void IdleHandoffContinuity()
    {
        var c = Reset(); var peer = Player(1, 0, 3); var view = Mob(c); Tick(c, 0);
        view.Data.Alert = true; c.StateChanged(view.Data); Move(peer, 40, 0); Tick(c, 1); Tick(c, 5); Service(c, 5.2f);
        Check("our verified idle delegation preserves enough history to return a chasing mob without requiring its first attack", view.Data.Owner == 99 && c.PendingMobTransfers == 0);

        c = Reset(); Player(1, 0, 3); view = Mob(c); Tick(c, 0); view.Data.Alert = true; c.StateChanged(view.Data);
        Player(2, 40, 0); view.Data.SetOwner(2); Tick(c, 1); Service(c, 1.2f);
        Check("an external owner change still discards the previous idle handoff evidence", view.Data.Owner == 2 && c.PendingMobTransfers == 1);

        c = Reset(); peer = Player(1, 0, 3); view = Mob(c); Tick(c, 0); view.Data.Alert = true; c.StateChanged(view.Data);
        view.GetComponent<Humanoid>().Attacking = true; Trigger(view, "spray"); Move(peer, 40, 0); Tick(c, 1); Tick(c, 5);
        view.GetComponent<Humanoid>().Attacking = false; Service(c, 5.2f);
        Check("a new loop after our delegation retains its unknown-end guard", view.Data.Owner == 1 && c.PendingMobTransfers == 1);

        c = Reset(); peer = Player(1, 0, 3); view = Mob(c); Tick(c, 0); view.Data.Alert = true; c.StateChanged(view.Data);
        Trigger(view); Move(peer, 40, 0); Tick(c, 1); Tick(c, 5); Service(c, 5.2f);
        Check("a new trigger with no observed animation cannot reuse the previous idle handoff", view.Data.Owner == 1 && c.PendingMobTransfers == 1);

        c = Reset(); peer = Player(1, 0, 3); view = Mob(c); Tick(c, 0); view.Data.Alert = true; c.StateChanged(view.Data);
        view.GetComponent<Humanoid>().Attacking = true; Move(peer, 40, 0); Tick(c, 1); Tick(c, 5);
        view.GetComponent<Humanoid>().Attacking = false; Service(c, 5.2f);
        Check("an attack animation without its trigger also invalidates carried idle evidence", view.Data.Owner == 1 && c.PendingMobTransfers == 1);

        var cfg = new ConfigFile(); cfg.Values["EnableMobOwnershipTest"] = true; cfg.Values["EnableMobAttackForecast"] = true;
        c = Reset(cfg); peer = Player(1, 0, 3); Ability(c, 0);
        var playerRoot = new GameObject(); playerRoot.Add(new Player { NativeFaction = Character.Faction.Players });
        var playerView = playerRoot.Add(new ZNetView { Data = ZDOMan.instance.Data[peer.m_characterID] }); playerView.Data.Prefab = 24;
        ZNetScene.instance.Prefabs[24] = playerRoot; ZNetScene.instance.Instances[playerView.Data] = playerView; Character.All.Add(playerRoot.GetComponent<Player>());
        view = Mob(c); view.Data.Alert = true; Tick(c, 0); Service(c, .11f);
        Check("a potential first attack still reaches the real forecast after a known idle delegation", view.Data.Owner == 1 && c.HasWaitingForecast && c.MobForecastQueries == 1 && c.MobTestTransfers == 0);
        playerView.Data.InternalSetPosition(new Vector3(3, 0, 3)); Service(c, .2f);
        Check("the same pending request resumes on target movement without needing an actual first attack", view.Data.Owner == 99 && !c.HasWaitingForecast && c.MobTestTransfers == 1);

        c = Reset(); Player(1, 0, 3); Player(2, 0, -3); view = Mob(c, 1); view.Data.Alert = true;
        view.GetComponent<Humanoid>().Attacking = true; Trigger(view); view.Data.SetRotation(new Quaternion { Yaw = 180 }); Tick(c, 0); EndRemote(c, view, .1f);
        view.Data.SetRotation(new Quaternion { Yaw = 0 }); Tick(c, 1); Service(c, 1.2f);
        Check("a completed guarded player-to-player handoff also preserves idle observation continuity", view.Data.Owner == 1 && c.PendingMobTransfers == 0);
    }
    private static void SoloDiagnostic()
    {
        var c = Reset(); Player(1, 0, 3); var view = Mob(c); Tick(c, 0);
        int writes = view.Data.Writes; for (int i = 1; i < 30; i++) Tick(c, i);
        Check("disabled solo test never selects a mob or introduces an ownership job", !c.MobTestActive && c.MobTestTransfers == 0 && view.Data.Writes == writes);
        var cfg = new ConfigFile(); cfg.Values["EnableMobOwnershipTest"] = true;
        c = Reset(cfg); Player(1, 0, 3); view = Mob(c); var other = Mob(c);
        other.Data.InternalSetPosition(new Vector3(3, 0, 0));
        var messages = new System.Collections.Generic.List<string>(); c.MobTestLog = messages.Add;
        Tick(c, 0);
        Check("solo test selects exactly one near eligible mob and preserves its native idle gate", c.MobTestActive && view.Data.Owner == 1 && other.Data.Owner == 1 && c.PendingMobTransfers == 1);
        Tick(c, .11f);
        Check("solo test's first player-to-server transfer uses the real guarded commit", view.Data.Owner == 99 && c.MobTestTransfers == 1 && other.Data.Owner == 1);
        Tick(c, .9f);
        Check("normal solo grid activity cannot undo the diagnostic's completed server ownership hold", view.Data.Owner == 99 && c.MobTestTransfers == 1);
        Tick(c, 1.2f);
        Check("the next diagnostic direction returns the mob through the real server-to-player path", view.Data.Owner == 1 && c.MobTestTransfers == 2);
        view.GetComponent<Humanoid>().Attacking = true; Trigger(view); Tick(c, 2.3f); Tick(c, 4); Tick(c, 5);
        Check("blocked diagnostic request is not reversed or reissued while a real attack continues", view.Data.Owner == 1 && c.MobTestTransfers == 2 && c.DescribeMobTest().Contains("requests=3") && c.DescribeMobTest().Contains("pending=True"));
        EndRemote(c, view, 5.1f);
        Check("observed remote attack completion resumes the same pending diagnostic request", view.Data.Owner == 99 && c.MobTestTransfers == 3);
        Check("diagnostic logs requests and actual successful commits", messages.Exists(s => s.Contains("request 3")) && messages.Exists(s => s.Contains("completed 3")));
        Player(2, 0, -3); Tick(c, 6);
        Check("a second player stops solo ownership overrides", !c.MobTestActive && c.DescribeMobTest().Contains("stopped=True"));

        cfg = new ConfigFile(); cfg.Values["EnableMobOwnershipTest"] = true; c = Reset(cfg); Player(1, 0, 3); view = Mob(c); Tick(c, 0);
        view.Data.Health = 0; Tick(c, .2f);
        Check("selected mob death stops the diagnostic", !c.MobTestActive && c.DescribeMobTest().Contains("stopped=True"));
        var replacement = Mob(c); Tick(c, 2);
        Check("stopped test does not silently select another mob", !c.MobTestActive && replacement.Data.Owner == 1);

        cfg = new ConfigFile(); cfg.Values["EnableMobOwnershipTest"] = true; c = Reset(cfg); var peer = Player(1, 0, 3); view = Mob(c); Tick(c, 0);
        Move(peer, 100, 100); Tick(c, 1);
        Check("leaving the test fight stops its override", !c.MobTestActive);
        cfg = new ConfigFile(); cfg.Values["EnableMobOwnershipTest"] = true; c = Reset(cfg); Player(1, 0, 3); view = Mob(c); Tick(c, 0); c.Remove(view.Data); Tick(c, .2f);
        Check("selected mob unload stops the test without retaining its node", !c.MobTestActive);
        cfg = new ConfigFile(); cfg.Values["EnableMobOwnershipTest"] = true; c = Reset(cfg); Player(1, 0, 3); view = Mob(c); Tick(c, 0); ZNet.instance.Peers.Clear(); Tick(c, .2f);
        Check("disconnect clears the diagnostic and normal pending transfers", !c.MobTestActive && c.PendingMobTransfers == 0);
        cfg = new ConfigFile(); cfg.Values["EnableMobOwnershipTest"] = true; c = Reset(cfg); Player(1, 0, 3); view = Mob(c); Tick(c, 0); Tick(c, 300);
        Check("diagnostic automatically stops after five minutes", !c.MobTestActive);
        cfg = new ConfigFile(); cfg.Values["EnableMobOwnershipTest"] = true; c = Reset(cfg); Player(1, 0, 3); view = Mob(c); Tick(c, 0); c.StopMobTest("fixture command"); Tick(c, .2f);
        Check("manual stop resumes normal policy rather than forcing an owner reset", !c.MobTestActive && view.Data.Owner == 1 && c.PendingMobTransfers == 0);

        cfg = new ConfigFile(); cfg.Values["EnableMobOwnershipTest"] = true; cfg.Values["EnableMobAttackForecast"] = true;
        c = Reset(cfg); Player(1, 0, 3); Ability(c, 0); view = Mob(c, 1); view.GetComponent<Humanoid>().Attacking = true; Trigger(view); Tick(c, 0);
        // A real native player target inside the ready padded cone.
        var target = new GameObject(); target.Add(new Player { NativeFaction = Character.Faction.Players });
        var targetView = target.Add(new ZNetView { Data = ZDOMan.instance.Data[ZNet.instance.Peers[0].m_characterID] });
        targetView.Data.Prefab = 24; ZNetScene.instance.Prefabs[24] = target; ZNetScene.instance.Instances[targetView.Data] = targetView; Character.All.Add(target.GetComponent<Player>());
        EndRemote(c, view, .1f);
        c.ForecastTargetChanged(targetView.Data, null, true); Service(c, .2f);
        Check("solo diagnostic never disables the optional ability forecast", view.Data.Owner == 1 && c.HasWaitingForecast && c.MobTestTransfers == 0);
    }
    private static int Main()
    {
        var harmony = new Harmony("tml.server.mob.fixture");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ZDO), "SetOwnerInternal"), postfix: new HarmonyMethod(typeof(OwnershipChanged), "Postfix"));
            harmony.Patch(AccessTools.Method(typeof(ZDO), "InternalSetPosition"), prefix: new HarmonyMethod(typeof(ObjectMoved), "Prefix"), postfix: new HarmonyMethod(typeof(ObjectMoved), "Postfix"));
            harmony.Patch(AccessTools.Method(typeof(ZDO), "SetRotation"), prefix: new HarmonyMethod(typeof(ShipRotationChanged), "Prefix"), postfix: new HarmonyMethod(typeof(ShipRotationChanged), "Postfix"));
            harmony.Patch(AccessTools.Method(typeof(ZDO), "Set", new[] { typeof(int), typeof(bool) }), postfix: new HarmonyMethod(typeof(AnimalFlagChanged), "Postfix"));
            harmony.Patch(AccessTools.Method(typeof(ZSyncAnimation), "RPC_SetTrigger"), prefix: new HarmonyMethod(typeof(MobAnimationTrigger), "Prefix"));
            harmony.Patch(AccessTools.Method(typeof(ZDO), "Deserialize"), prefix: new HarmonyMethod(typeof(SourceObject), "Prefix"), postfix: new HarmonyMethod(typeof(SourceObject), "Postfix"));
            MobAttackAccess.Validate(); Selection(); Guards(); Lifecycle(); MultiplePlayersAndBosses(); Forecast(); SoloDiagnostic(); IdleHandoffContinuity();
            Console.WriteLine("PASS: " + checks + " production hostile grid/cone/attack/event/lifecycle assertions. No live combat safety claim."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { harmony.UnpatchSelf(); Plugin.Current = null; }
    }
}
