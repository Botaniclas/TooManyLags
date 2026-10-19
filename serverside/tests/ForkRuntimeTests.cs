using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using TooManyLagsDedicatedAddon;

internal static class ForkRuntimeTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private static int Main()
    { try { Run(); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
    private static void Run()
    {
        var native = AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS");
        var prefix = AccessTools.Method(typeof(Valheim_Serverside.Features.Core.ZDOMan_ReleaseNearbyZDOS_Patch), "Prefix");
        var core = new Harmony(Plugin.Dependency + ".Core");
        var fire = new Harmony(Plugin.Dependency + ".FireControl");
        var addon = new Harmony("fixture.dedicatedaddon");
        try
        {
            bool rejected = false;
            try { new ForkBridge(new Valheim_Serverside.ServersidePlugin()); } catch (NotSupportedException) { rejected = true; }
            Check("missing core installation rejected", rejected);
            core.Patch(native, prefix: new HarmonyMethod(typeof(ForkRuntimeTests), "UnrelatedPrefix"));
            rejected = false;
            try { new ForkBridge(new Valheim_Serverside.ServersidePlugin()); } catch (NotSupportedException) { rejected = true; }
            Check("matching Harmony label cannot substitute a different backend method", rejected);
            core.UnpatchSelf();
            core.Patch(native, prefix: new HarmonyMethod(prefix));
            core.Patch(AccessTools.Method(typeof(Ship), "UpdateOwner"), prefix: new HarmonyMethod(typeof(Valheim_Serverside.Features.Core.Ship_UpdateOwner_Patch), "Prefix"));
            core.Patch(AccessTools.Method(typeof(ZRoutedRpc), "RouteRPC"), prefix: new HarmonyMethod(typeof(Valheim_Serverside.Features.Core.ZRoutedRpc_RouteRPC_Patch), "Prefix"));
            var bridge = new ForkBridge(new Valheim_Serverside.ServersidePlugin()); Check("active matching core under real feature Harmony ID accepted", bridge.Active);
            Check("inactive fire ownership creates no exclusions", !bridge.Reserved(11));
            fire.Patch(AccessTools.Method(typeof(ZDO), "SetOwnerInternal"), postfix: new HarmonyMethod(typeof(Valheim_Serverside.Features.FireControl.ZDO_SetOwnerInternal_Patch), "Postfix"));
            var fireBridge = new ForkBridge(new Valheim_Serverside.ServersidePlugin());
            Check("active fire ownership reuses dependency source detection", fireBridge.Reserved(11) && !fireBridge.Reserved(1));
            bridge.Install(addon);
            ZNet.instance = new ZNet(); ZDOMan.instance = new ZDOMan(); ZNetScene.instance = new ZNetScene();
            var root = new GameObject(); root.Add(new ZNetView()); root.Add(new Door()); ZNetScene.instance.Prefabs.Add(1, root);
            ZNet.instance.Peers.Add(new ZNetPeer { m_uid = 1, m_characterID = new ZDOID(1) });
            ZDOMan.instance.Data.Add(new ZDOID(1), new ZDO { Owner = 1 });
            var controller = new Controller(new Settings(new ConfigFile()), 99); Plugin.Current = controller; controller.Tick(0);
            ZDO initial = new ZDO { Prefab = 1 }, release = new ZDO { Prefab = 1, Owner = 1, User = -1 }, excluded = new ZDO { Prefab = 2 }, occupied = new ZDO { Prefab = 1, Owner = 7 };
            ZDOMan.instance.Assignments.AddRange(new[] { initial, release, excluded, occupied });
            ZDOMan.instance.RunAssignment(55);
            Check("detoured fork prefix uses grid recipient", initial.Owner == 1 && initial.Writes == 1);
            Check("zero release preserved exactly", release.Owner == 0 && release.Writes == 1);
            Check("excluded objects retain native server destination", excluded.Owner == 99 && excluded.Writes == 1);
            Check("already-owned native object is untouched", occupied.Owner == 7 && occupied.Writes == 0);
            addon.UnpatchSelf(); initial.Owner = 0; ZDOMan.instance.RunAssignment(55);
            Check("unpatching addon restores original fork behavior", initial.Owner == 99);
            var setOwner = AccessTools.Method(typeof(ZDO), "SetOwner");
            rejected = false;
            try { ForkAssignment.Rewrite(new[] { new CodeInstruction(OpCodes.Callvirt, setOwner) }); } catch (NotSupportedException) { rejected = true; }
            Check("changed assignment count rejected", rejected);

            fireBridge.Install(addon, true);
            var fireplace = new GameObject(); fireplace.Add(new ZNetView()); fireplace.Add(new Fireplace()); ZNetScene.instance.Prefabs.Add(11, fireplace);
            var fireConfig = new ConfigFile(); fireConfig.Values["RespectBackendFireOwnership"] = false;
            Time.unscaledTime = 0;
            controller = new Controller(new Settings(fireConfig), 99); Plugin.Current = controller;
            var flame = new ZDO { Prefab = 11, Owner = 99 };
            var rawFire = new ZDO { Prefab = 12, Owner = 1 };
            controller.Add(flame, new GameObject().Add(new ZNetView { Data = flame })); controller.Tick(0);
            Check("override permits eligible server-default fireplace in local grid", flame.Owner == 1 && controller.TrackedObjects == 1);
            Valheim_Serverside.Features.FireControl.Claimed.Clear(); Valheim_Serverside.Features.FireControl.Claimed.AddRange(new[] { flame, rawFire });
            Valheim_Serverside.Features.FireControl.RunTakeOver();
            Check("backend does not fight managed fireplace recipient", flame.Owner == 1);
            Check("unmanaged real fire retains native server takeover", rawFire.Owner == 99);
            int fireWrites = flame.Writes;
            for (int i = 0; i < 1000; i++) Valheim_Serverside.Features.FireControl.RunTakeOver();
            Check("repeated backend source receipts create no competing writes", flame.Writes == fireWrites);
            fireConfig.Values["RefillStations"] = false;
            Plugin.Current = new Controller(new Settings(fireConfig), 99); flame.Owner = 1;
            Valheim_Serverside.Features.FireControl.RunTakeOver();
            Check("disabled refill category restores native fire source takeover", flame.Owner == 99);
            Plugin.Current = new Controller(new Settings(new ConfigFile()), 99, fireBridge.Reserved); flame.Owner = 1;
            Valheim_Serverside.Features.FireControl.RunTakeOver();
            Check("default respects backend fire ownership even with bridge installed", flame.Owner == 99);
            Plugin.Current = null; flame.Owner = 1;
            Valheim_Serverside.Features.FireControl.RunTakeOver();
            Check("absent addon world retains native fire behavior", flame.Owner == 99);
            rejected = false;
            try { ForkFireOwnership.Rewrite(new[] { new CodeInstruction(OpCodes.Ret) }); } catch (NotSupportedException) { rejected = true; }
            Check("changed fire predicate call contract rejected", rejected);
            addon.UnpatchSelf(); flame.Owner = 1;
            Valheim_Serverside.Features.FireControl.RunTakeOver();
            Check("unpatching fully restores dependency fire takeover", flame.Owner == 99);
            core.UnpatchSelf();
            rejected = false;
            try { new ForkBridge(new Valheim_Serverside.ServersidePlugin()); } catch (NotSupportedException) { rejected = true; }
            Check("removed backend prefix still rejected", rejected);
        }
        finally { addon.UnpatchSelf(); fire.UnpatchSelf(); core.UnpatchSelf(); Plugin.Reset(); }
        Console.WriteLine("PASS: " + checks + " live Harmony fork-prefix interception, rollback and strict-contract assertions. Controlled fork/game fixtures.");
    }
    private static bool UnrelatedPrefix() { return true; }
}
