using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using TooManyLags;
using HarmonyLib;

internal static class FgnSessionSettingsTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static ConfigEntry<bool> Entry(ConfigFile file, string name, bool value)
    { return file.Bind("Test", name, value, "Preserved metadata: " + name); }
    private static ConfigFile Setup(string path, bool active, bool enabledAI)
    {
        if (File.Exists(path)) File.Delete(path);
        ConfigFile file = new ConfigFile(path, false); file.SaveOnConfigSet = false;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority = Entry(file, "SSS", false);
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership = Entry(file, "Broad", true);
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective = Entry(file, "Selective", true);
        FiresGhettoNetworkMod.StationRouter.ConfigEnabled = Entry(file, "Stations", false);
        FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled = Entry(file, "Creatures", enabledAI);
        Entry(file, "Unrelated", false);
        file.Save(); file.SaveOnConfigSet = true;
        FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive = active;
        FiresGhettoNetworkMod.CreatureOwnership.ServerOwnsCreatures = true;
        FiresGhettoNetworkMod.HelmOwnership.ServerOwnsShips = true;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts = 0;
        FiresGhettoNetworkMod.ServerClientUtils.IsDedicatedServerDetected = true;
        FiresGhettoNetworkMod.ValheimCommunityPatchCompat.SchedulesSceneObjects = false;
#if FGN_165
        FiresGhettoNetworkMod.ZSyncHookGate.Calls = 0;
        FiresGhettoNetworkMod.ZSyncHookGate.Attached = active;
        FiresGhettoNetworkMod.ZSyncHookGate.OtherFeature = false;
#endif
        return file;
    }
    private static void Run(string path)
    {
        Assembly assembly = typeof(FiresGhettoNetworkMod.FiresGhettoNetworkMod).Assembly;
        foreach (bool active in new[] { false, true })
        {
            ConfigFile original = Setup(path, active, false);
            string saved = File.ReadAllText(path);
            ConfigEntry<bool> oldSSS = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority;
            Check(FgnSessionSettings.Apply(assembly, original, true, true, false) == !active, "activation result");
            Check(FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive, "actual simulation ready");
            Check(FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts == (active ? 0 : 1), "native initializer only when needed");
#if FGN_165
            Check(FiresGhettoNetworkMod.ZSyncHookGate.Calls == (active ? 0 : 1) && FiresGhettoNetworkMod.ZSyncHookGate.Attached,
                "new simulation refreshes native OwnerSync/rescue after readiness; active simulation is not refreshed");
#endif
            Check(FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority.Value, "SSS runtime on");
            Check(!FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership.Value, "broad runtime off");
            Check(!FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective.Value, "selective runtime off");
            Check(FiresGhettoNetworkMod.StationRouter.ConfigEnabled.Value, "station routing on");
            Check(FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled.Value, "AI balancing on");
            Check(!FiresGhettoNetworkMod.CreatureOwnership.ServerOwnsCreatures && !FiresGhettoNetworkMod.HelmOwnership.ServerOwnsShips, "stale ownership modes cleared");
            Check(!oldSSS.Value && original[new ConfigDefinition("Test", "Broad")].BoxedValue.Equals(true), "original entries unchanged");
            Check(original.SaveOnConfigSet, "original saving policy unchanged");
            Check(File.ReadAllText(path) == saved, "override did not save anything");
            original.Save(); Check(File.ReadAllText(path) == saved, "explicit FGN Save still preserves original values");
            original.Reload(); Check(FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority.Value, "saved reload does not undo session override");
            Check(FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority.Description == oldSSS.Description, "description retained");
            FgnSessionSettings.Apply(assembly, original, true, true, false);
            Check(FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts == (active ? 0 : 1), "repeat application cannot start twice");
#if FGN_165
            Check(FiresGhettoNetworkMod.ZSyncHookGate.Calls == (active ? 0 : 1), "repeat application does not refresh native gate again");
#endif
        }
        foreach (bool nativeAI in new[] { false, true })
        {
            ConfigFile original = Setup(path, false, nativeAI);
            ConfigEntry<bool> oldAI = FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled;
            FgnSessionSettings.Apply(assembly, original, true, false, false);
            Check(ReferenceEquals(oldAI, FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled) && oldAI.Value == nativeAI, "disabled addon AI leaves native balancing choice unchanged");
        }
        ConfigFile client = Setup(path, false, false);
        ConfigEntry<bool> oldClientSSS = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority;
        ConfigEntry<bool> oldClientStations = FiresGhettoNetworkMod.StationRouter.ConfigEnabled;
        Check(!FgnSessionSettings.Apply(assembly, client, false, true, true), "client never starts simulation");
        Check(ReferenceEquals(oldClientSSS, FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority) && !oldClientSSS.Value, "client SSS untouched");
        Check(ReferenceEquals(oldClientStations, FiresGhettoNetworkMod.StationRouter.ConfigEnabled), "client station setting untouched");
        Check(FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts == 0 && !FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive, "no client simulation initializer or completion flag");
        Check(!FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership.Value && !FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective.Value, "client AI bridge sees disabled broad/selective options");
        foreach (bool useAI in new[] { false, true })
        {
            ConfigFile original = Setup(path, false, false); string saved = File.ReadAllText(path);
            var broad = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership;
            var selective = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective;
            var creatures = FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled;
            IDisposable restore = FgnSessionSettings.ApplyClient(assembly, original, useAI);
            Check(!FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership.Value && !FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective.Value, "server-directed client session overrides ownership gates");
            Check(FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled.Value == useAI && (useAI || ReferenceEquals(creatures, FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled)), "server AI off preserves native client balancing config");
            restore.Dispose(); restore.Dispose();
            Check(ReferenceEquals(broad, FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership) && ReferenceEquals(selective, FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective) && ReferenceEquals(creatures, FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled), "leaving restores original runtime references idempotently");
            Check(File.ReadAllText(path) == saved && original.SaveOnConfigSet, "client flags never overwrite saved settings");
        }
        ConfigFile changedClient = Setup(path, false, false);
        var oldBroadClient = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership;
        IDisposable changedRestore = FgnSessionSettings.ApplyClient(assembly, changedClient, true);
        var foreign = Entry(changedClient, "OtherPolicy", false); FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled = foreign;
        changedRestore.Dispose();
        Check(ReferenceEquals(FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled, foreign) && ReferenceEquals(FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership, oldBroadClient), "cleanup preserves a later override from another mod");
        foreach (int blocked in new[] { 0, 1, 2 })
        {
            ConfigFile original = Setup(path, false, false);
            string saved = File.ReadAllText(path);
            ConfigEntry<bool> oldBroad = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership;
            FiresGhettoNetworkMod.ValheimCommunityPatchCompat.SchedulesSceneObjects = blocked == 0;
            FiresGhettoNetworkMod.ServerClientUtils.IsDedicatedServerDetected = blocked != 2;
            bool failed = false;
            try { FgnSessionSettings.Apply(assembly, original, true, true, blocked == 1); }
            catch (NotSupportedException) { failed = true; }
            Check(failed, "native conflict/dedicated detection fails closed");
            Check(ReferenceEquals(oldBroad, FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership), "blocked startup changes no runtime entries");
            Check(FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts == 0 && !FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive, "blocked startup does not enable simulation");
            Check(File.ReadAllText(path) == saved, "blocked startup changes no saved values");
        }
        ConfigFile missing = Setup(path, false, false);
        FiresGhettoNetworkMod.StationRouter.ConfigEnabled = null;
        ConfigEntry<bool> untouched = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership;
        bool rejected = false;
        try { FgnSessionSettings.Apply(assembly, missing, true, true, false); }
        catch (NotSupportedException) { rejected = true; }
        Check(rejected && ReferenceEquals(untouched, FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership), "uninitialized config is rejected before any replacement");
        foreach (bool active in new[] { false, true })
        {
            ConfigFile original = Setup(path, active, true);
            string saved = File.ReadAllText(path);
            var fgn = FiresGhettoNetworkMod.FiresGhettoNetworkMod.Harmony;
            MethodInfo target = typeof(SettingsTarget).GetMethod("Run");
            fgn.Patch(target, prefix: new HarmonyMethod(typeof(FiresGhettoNetworkMod.ServerAuthorityPatches), "Prefix"));
            fgn.Patch(target, postfix: new HarmonyMethod(typeof(FiresGhettoNetworkMod.SssQuietPass), "Postfix"));
            fgn.Patch(target, postfix: new HarmonyMethod(typeof(FiresGhettoNetworkMod.NetworkKeep), "Postfix"));
            var other = new Harmony("test.othermod");
            other.Patch(target, postfix: new HarmonyMethod(typeof(SettingsTarget), "Other"));
            Check(!FgnSessionSettings.Apply(assembly, original, true, true, true, true), "player-simulation mode never starts simulation, even with a scene scheduler");
            Check(!FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority.Value && !FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive && FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts == 0, "already active and inactive simulation both become inactive");
#if FGN_165
            Check(FiresGhettoNetworkMod.ZSyncHookGate.Calls == (active ? 1 : 0) && !FiresGhettoNetworkMod.ZSyncHookGate.Attached,
                "stopping simulation refreshes native gate only after clearing readiness");
#endif
            Patches remaining = Harmony.GetPatchInfo(target);
            Check(remaining.Prefixes.Count == 0 && remaining.Postfixes.Count == 2, "simulation and optional quiet-pass hooks removed; network and other mod hooks preserved");
            Check(!FiresGhettoNetworkMod.CreatureOwnership.ServerOwnsCreatures && !FiresGhettoNetworkMod.HelmOwnership.ServerOwnsShips, "server creature/ship mode flags reset");
            FgnSessionSettings.CreatureBalancing(assembly, original, false);
            Check(!FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled.Value, "fallback policy disables duplicate native creature balancing");
            original.Save(); Check(File.ReadAllText(path) == saved, "saved config unchanged after disabling simulation and balancing");
            fgn.UnpatchSelf(); other.UnpatchSelf();
        }
        ConfigFile normalSimulation = Setup(path, true, true);
        var nativePatches = FiresGhettoNetworkMod.FiresGhettoNetworkMod.Harmony;
        MethodInfo normalTarget = typeof(SettingsTarget).GetMethod("Run");
        nativePatches.Patch(normalTarget, prefix: new HarmonyMethod(typeof(FiresGhettoNetworkMod.ServerAuthorityPatches), "Prefix"));
        nativePatches.Patch(normalTarget, postfix: new HarmonyMethod(typeof(FiresGhettoNetworkMod.SssQuietPass), "Postfix"));
        FgnSessionSettings.Apply(assembly, normalSimulation, true, true, false);
        Check(Harmony.GetPatchInfo(normalTarget).Prefixes.Count == 1 && Harmony.GetPatchInfo(normalTarget).Postfixes.Count == 1,
            "normal simulation mode retains authority and quiet-pass hooks");
        Check(FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive, "normal simulation mode remains active");
        nativePatches.UnpatchSelf();
        foreach (string installer in new[] { "UnknownSimulationGroup", "MissingSimulationGroup", "ForeignQuietPass" })
        {
            ConfigFile original = Setup(path, true, true);
            string saved = File.ReadAllText(path);
            var oldSSS = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority;
            var fgn = FiresGhettoNetworkMod.FiresGhettoNetworkMod.Harmony;
            MethodInfo target = typeof(SettingsTarget).GetMethod("Run");
            fgn.Patch(target, prefix: new HarmonyMethod(typeof(FiresGhettoNetworkMod.ServerAuthorityPatches), "Prefix"));
            bool refused = false;
            try { FgnSimulationStartup.Validate(typeof(FiresGhettoNetworkMod.FiresGhettoNetworkMod).GetMethod(installer, BindingFlags.Static | BindingFlags.NonPublic)); }
            catch (NotSupportedException) { refused = true; }
            Check(refused, "unknown, incomplete and foreign-namespace installers remain rejected: " + installer);
            Check(Harmony.GetPatchInfo(target).Prefixes.Count == 1, "validation failure leaves existing patches intact");
            Check(ReferenceEquals(oldSSS, FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority) &&
                FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive && File.ReadAllText(path) == saved, "validation failure changes no configuration or simulation flag");
            fgn.UnpatchSelf();
        }
#if FGN_165
        ConfigFile otherSync = Setup(path, true, true);
        FiresGhettoNetworkMod.ZSyncHookGate.OtherFeature = true;
        FgnSessionSettings.Apply(assembly, otherSync, true, true, false, true);
        Check(FiresGhettoNetworkMod.ZSyncHookGate.Attached && FiresGhettoNetworkMod.ZSyncHookGate.Calls == 1,
            "stopping simulation preserves OwnerSync needed by an independent transform feature");
#endif
        ManualSetup(path, assembly);
        ZNet.instance = new object();
        bool liveBlocked = false;
        try { FgnSessionSettings.Apply(assembly, Setup(path, false, true), true, true, false, true); }
        catch (NotSupportedException) { liveBlocked = true; }
        ZNet.instance = null; Check(liveBlocked, "switching simulation while a world exists is refused");
        File.Delete(path);
        Console.WriteLine("PASS: " + checks + " startup session assertions using real BepInEx.ConfigFile and FGN startup doubles; saved values, repeated init, disabled AI, client scope and safety blocks checked. No Unity session.");
    }
    private static ConfigFile ManualConfig(string path, bool noSimulation)
    {
        ConfigFile config = Setup(path, !noSimulation, !noSimulation);
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority.Value = !noSimulation;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership.Value = false;
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective.Value = false;
        FiresGhettoNetworkMod.StationRouter.ConfigEnabled.Value = true;
        FiresGhettoNetworkMod.CreatureOwnership.ServerOwnsCreatures = false;
        FiresGhettoNetworkMod.HelmOwnership.ServerOwnsShips = false;
        config.Save(); return config;
    }
    private static void ManualSetup(string path, Assembly assembly)
    {
        foreach (bool noSimulation in new[] { false, true })
        {
            ConfigFile config = ManualConfig(path, noSimulation);
            string saved = File.ReadAllText(path);
            var authority = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority;
            var balancing = FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled;
            var native = FiresGhettoNetworkMod.FiresGhettoNetworkMod.Harmony;
            MethodInfo target = typeof(SettingsTarget).GetMethod("Run");
            native.Patch(target, postfix: new HarmonyMethod(typeof(FiresGhettoNetworkMod.SssQuietPass), "Postfix"));
            Check(!FgnSessionSettings.Apply(assembly, config, true, true, false, noSimulation, false), "manual mode never invokes native simulation initialization");
            FgnSessionSettings.CreatureBalancing(assembly, config, noSimulation, false);
            Check(ReferenceEquals(authority, FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority) &&
                ReferenceEquals(balancing, FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled) && balancing.Value == !noSimulation,
                "manual mode preserves authority and balancing entry references and values");
            Check(FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive == !noSimulation &&
                FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts == 0 && Harmony.GetPatchInfo(target).Postfixes.Count == 1,
                "manual mode preserves native flags and patches, including quiet-pass");
            Check(File.ReadAllText(path) == saved && config.SaveOnConfigSet, "manual mode never writes config or changes its save policy");
            native.UnpatchSelf();
        }
        foreach (string invalid in new[] { "SSS", "Ready", "Stations", "Broad", "Selective", "CreatureMode", "ShipMode", "Balance" })
        {
            ConfigFile config = ManualConfig(path, false);
            switch (invalid)
            {
                case "SSS": FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority.Value = false; break;
                case "Ready": FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive = false; break;
                case "Stations": FiresGhettoNetworkMod.StationRouter.ConfigEnabled.Value = false; break;
                case "Broad": FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership.Value = true; break;
                case "Selective": FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective.Value = true; break;
                case "CreatureMode": FiresGhettoNetworkMod.CreatureOwnership.ServerOwnsCreatures = true; break;
                case "ShipMode": FiresGhettoNetworkMod.HelmOwnership.ServerOwnsShips = true; break;
                case "Balance": FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled.Value = false; break;
            }
            string saved = File.ReadAllText(path);
            var authority = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority;
            bool rejected = false;
            try { FgnSessionSettings.Apply(assembly, config, true, true, false, false, false); }
            catch (NotSupportedException) { rejected = true; }
            Check(rejected, "unsupported manual setup is rejected: " + invalid);
            Check(ReferenceEquals(authority, FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority) &&
                FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts == 0 && File.ReadAllText(path) == saved,
                "rejected manual setup is not automatically repaired or saved");
        }
        ConfigFile client = ManualConfig(path, false);
        string clientSaved = File.ReadAllText(path);
        var broad = FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership;
        Check(FgnSessionSettings.ApplyClient(assembly, client, true, false) == null && ReferenceEquals(broad,
            FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership) && File.ReadAllText(path) == clientSaved,
            "server-selected manual setup also leaves client config references unchanged");
        FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership.Value = true;
        bool blocked = false;
        try { FgnSessionSettings.ApplyClient(assembly, client, true, false); }
        catch (NotSupportedException) { blocked = true; }
        Check(blocked && broad.Value, "invalid manual client setup is rejected without overriding its values");
    }
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request) {
            string candidate = Path.Combine(args[1], new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        try { Run(args[0]); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

namespace FiresGhettoNetworkMod
{
#if FGN_165
    internal static class ZSyncHookGate
    {
        internal static int Calls;
        internal static bool Attached, OtherFeature;
        internal static void Refresh(string reason) { Calls++; Attached = OtherFeature || ServerAuthorityPatches.SimulationActive; }
    }
#endif
    internal static class FiresGhettoNetworkMod
    {
        public static ConfigEntry<bool> ConfigEnableServerAuthority, ConfigEnableServerOwnership, ConfigEnableServerOwnershipSelective;
        internal static int Starts;
        internal static Harmony Harmony { get; private set; }
        static FiresGhettoNetworkMod() { Harmony = new Harmony("com.Fire.FiresGhettoNetworkMod"); }
        private static void ApplyServerSideSimulationPatches()
        {
            if (!ConfigEnableServerAuthority.Value || ConfigEnableServerOwnership.Value || ConfigEnableServerOwnershipSelective.Value ||
                !StationRouter.ConfigEnabled.Value || CreatureOwnership.ServerOwnsCreatures || HelmOwnership.ServerOwnsShips)
                throw new Exception("Native initialization received unsafe settings.");
            Starts++;
            TryPatchAll(typeof(ShipFixesGroup)); TryPatchAll(typeof(ServerShipSimulationPatches)); TryPatchAll(typeof(ServerAuthorityPatches));
#if FGN_165
            TryPatchAll(typeof(SssQuietPass));
#endif
            TryPatchAll(typeof(ServerStabilityPatches)); TryPatchAll(typeof(MonsterAIPatches)); TryPatchAll(typeof(ServerOwnershipPatches)); TryPatchAll(typeof(ServerOwnershipPatchesV3));
        }
        private static void TryPatchAll(Type type) { }
        private static void UnknownSimulationGroup()
        {
            TryPatchAll(typeof(ShipFixesGroup)); TryPatchAll(typeof(ServerShipSimulationPatches)); TryPatchAll(typeof(ServerAuthorityPatches));
            TryPatchAll(typeof(ServerStabilityPatches)); TryPatchAll(typeof(MonsterAIPatches)); TryPatchAll(typeof(ServerOwnershipPatches)); TryPatchAll(typeof(ServerOwnershipPatchesV3));
            TryPatchAll(typeof(NetworkKeep));
        }
        private static void MissingSimulationGroup()
        {
            TryPatchAll(typeof(ShipFixesGroup)); TryPatchAll(typeof(ServerShipSimulationPatches)); TryPatchAll(typeof(ServerAuthorityPatches));
            TryPatchAll(typeof(ServerStabilityPatches)); TryPatchAll(typeof(MonsterAIPatches)); TryPatchAll(typeof(ServerOwnershipPatches));
            TryPatchAll(typeof(SssQuietPass));
        }
        private static void ForeignQuietPass()
        {
            TryPatchAll(typeof(ShipFixesGroup)); TryPatchAll(typeof(ServerShipSimulationPatches)); TryPatchAll(typeof(ServerAuthorityPatches));
            TryPatchAll(typeof(ServerStabilityPatches)); TryPatchAll(typeof(MonsterAIPatches)); TryPatchAll(typeof(ServerOwnershipPatches)); TryPatchAll(typeof(ServerOwnershipPatchesV3));
            TryPatchAll(typeof(Foreign.SssQuietPass));
        }
    }
    internal static class StationRouter { public static ConfigEntry<bool> ConfigEnabled; }
    internal static class CreatureOwnership { public static ConfigEntry<bool> ConfigEnabled; internal static bool ServerOwnsCreatures; }
    internal static class HelmOwnership { internal static bool ServerOwnsShips; }
    internal static class ServerAuthorityPatches { internal static bool SimulationActive; public static bool Prefix() { return false; } }
    internal static class SssQuietPass { public static void Postfix() { } }
    internal static class ShipFixesGroup { }
    internal static class ServerShipSimulationPatches { }
    internal static class ServerStabilityPatches { }
    internal static class MonsterAIPatches { }
    internal static class ServerOwnershipPatches { }
    internal static class ServerOwnershipPatchesV3 { }
    internal static class NetworkKeep { public static void Postfix() { } }
    internal static class ServerClientUtils { public static bool IsDedicatedServerDetected { get; set; } }
    internal static class ValheimCommunityPatchCompat
    {
        public static bool SchedulesSceneObjects { get; set; }
        public static void Detect() { }
    }
}
namespace Foreign { internal static class SssQuietPass { } }
internal static class ZNet { internal static object instance; }
internal static class ZNetScene { internal static readonly object instance = null; }
internal static class SettingsTarget
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void Run() { }
    public static void Other() { }
}
