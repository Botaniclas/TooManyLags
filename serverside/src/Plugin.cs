using System;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("0.13.1.0")]
namespace TooManyLagsDedicatedAddon
{
    [BepInPlugin(Id, "TooManyLags - Server Side", Version)]
    [BepInDependency(Dependency)]
    [BepInIncompatibility("botanic.toomanylags.fgnaddon")]
    [BepInIncompatibility("botanic.toomanylags.standalone")]
    [BepInIncompatibility("botanic.server.authority")]
    [BepInIncompatibility("com.Fire.FiresGhettoNetworkMod")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "botanic.toomanylags.dedicatedaddon", Version = "0.13.1";
        internal const string Dependency = "MVP.Valheim_Serverside_Simulations";
        internal static Controller Current;
        internal static Plugin Instance;
        private Settings options;
        private ForkBridge fork;
        private Harmony harmony;
        private ZDOMan world;
        private float nextCheck;
        private int faults;
        private int statusReports;
        private bool mobTestStoppedByCommand;
        private void Awake()
        {
            Instance = this;
            options = new Settings(Config);
            if (!options.Enabled) { enabled = false; return; }
            try
            {
                PluginInfo dependency;
                if (!Chainloader.PluginInfos.TryGetValue(Dependency, out dependency)) throw new NotSupportedException("Dedicated Simulation is absent.");
                fork = new ForkBridge(dependency.Instance);
                NativeSnapshot.Validate();
                if ((options.Groups & Groups.HostileMobs) != 0) MobAttackAccess.Validate();
                harmony = new Harmony(Id);
                fork.Install(harmony, !options.RespectFireOwnership, (options.Groups & Groups.Ships) != 0);
                harmony.PatchAll(typeof(Plugin).Assembly);
                try { new Terminal.ConsoleCommand("tml_grid_status", "Write server grid, peer, registration and prefab diagnostics to the server log.", new Terminal.ConsoleEvent(delegate(Terminal.ConsoleEventArgs args)
                {
                    if (!ZNet.instance || !ZNet.instance.IsServer() || !ZNet.instance.IsDedicated()) return;
                    ReportRuntimeStatus();
                    if (args != null && args.Context) args.Context.AddString("GRID status written to the server BepInEx log.");
                })); }
                catch (Exception commandError) { Logger.LogWarning("Optional grid status command unavailable: " + commandError.Message); }
                if (options.MobOwnershipTest)
                {
                    Logger.LogWarning("Solo mob ownership test enabled. One nearby Hostile Mob will receive alternating guarded owner requests. Disable EnableMobOwnershipTest after testing.");
                    try { new Terminal.ConsoleCommand("tml_mob_test_stop", "Stop the solo mob ownership test for this server session.", new Terminal.ConsoleEvent(delegate(Terminal.ConsoleEventArgs args)
                    {
                        if (!ZNet.instance || !ZNet.instance.IsServer() || !ZNet.instance.IsDedicated()) return;
                        mobTestStoppedByCommand = true;
                        if (Current != null) Current.StopMobTest("server command");
                        if (args != null && args.Context) args.Context.AddString("Solo mob test stopped for this server session.");
                    })); }
                    catch (Exception commandError) { Logger.LogWarning("Optional test stop command unavailable: " + commandError.Message); }
                }
                Logger.LogInfo("Diagnostic build: two bounded GRID status reports after character login; manual server command: tml_grid_status.");
                Logger.LogInfo("Server-only addon ready: character-heading grid; native quick interactions; idle tamed animals; native pickup priority; server-owned static buildings with optional grid sharing. Public chests, boats and idle carts use grid ownership with native inventory checks; pulling and pending remote hitch requests retain priority; Hostile Mobs use guarded grid transfers and forward-cone overlap selection; text and unsupported creatures retain native handling. Experimental build.");
            }
            catch (Exception error)
            {
                Fault(error);
            }
        }
        private void Update()
        {
            if (harmony == null || !ZNet.instance || !ZNet.instance.IsServer() || !ZNet.instance.IsDedicated() || ZDOMan.instance == null || !ZNetScene.instance)
            { Reset(); return; }
            if (!EnsureWorld()) return;
            float now = Time.unscaledTime;
            try
            {
                Controller controller = Current;
                if (now >= nextCheck) { nextCheck = now + options.CheckInterval; controller.Tick(now); }
                if (ReferenceEquals(controller, Current)) controller.Service(now);
            }
            catch (Exception error) { Fault(error); }
        }
        internal static bool EnsureWorld()
        {
            Plugin plugin = Instance;
            if (plugin == null || plugin.harmony == null || !ZNet.instance || !ZNet.instance.IsServer() ||
                !ZNet.instance.IsDedicated() || ZDOMan.instance == null || !ZNetScene.instance) return false;
            if (Current != null && ReferenceEquals(plugin.world, ZDOMan.instance)) return true;
            Reset();
            if (!plugin.fork.Active) return false;
            plugin.world = ZDOMan.instance;
            Current = new Controller(plugin.options, ZDOMan.GetSessionID(), plugin.options.RespectFireOwnership ? (Func<int, bool>)plugin.fork.Reserved : null);
            if (plugin.options.MobOwnershipTest) Current.MobTestLog = delegate(string message) { plugin.Logger.LogInfo(message); };
            if (plugin.mobTestStoppedByCommand) Current.StopMobTest("server command");
            return true;
        }
        internal static void Fault(Exception error)
        {
            Plugin plugin = Instance;
            if (plugin == null) return;
            Harmony patcher = plugin.harmony;
            plugin.harmony = null;
            plugin.enabled = false;
            Reset();
            if (plugin.faults++ < 3) plugin.Logger.LogWarning("Addon stopped; native simulation retained. " + error);
            try { if (patcher != null) patcher.UnpatchSelf(); }
            catch (Exception cleanup) { plugin.Logger.LogWarning("Inactive addon hooks could not be removed: " + cleanup); }
        }
        internal static void Reset() { Current = null; if (Instance != null) Instance.world = null; }
        internal static void ScheduleStatus()
        {
            Plugin plugin = Instance; if (plugin == null) return;
            try
            {
                plugin.CancelInvoke("ReportRuntimeStatus");
                plugin.statusReports = 0;
                plugin.Invoke("ReportRuntimeStatus", 10f);
                plugin.Invoke("ReportRuntimeStatus", 40f);
            }
            catch (Exception error) { plugin.Logger.LogWarning("Optional grid status schedule unavailable: " + error.Message); }
        }
        private void ReportRuntimeStatus()
        {
            try
            {
                Logger.LogInfo("GRID status " + (++statusReports) + ": enabled=" + enabled + "; hooks=" + (harmony != null) +
                    "; network=" + (bool)ZNet.instance + "; server=" + (ZNet.instance && ZNet.instance.IsServer()) +
                    "; dedicated=" + (ZNet.instance && ZNet.instance.IsDedicated()) + "; backend=" + (fork != null && fork.Active) +
                    "; world=" + (ZDOMan.instance != null) + "; scene=" + (bool)ZNetScene.instance + "; controller=" + (Current != null));
                if (Current != null) foreach (string line in Current.DescribeRuntime()) Logger.LogInfo(line);
            }
            catch (Exception error) { Logger.LogWarning("GRID diagnostic unavailable: " + error); }
        }
        private void OnDestroy()
        {
            Reset();
            if (harmony != null) harmony.UnpatchSelf();
            Instance = null;
        }
    }
}

