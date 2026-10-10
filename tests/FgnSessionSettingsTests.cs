using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using TooManyLags;

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
        File.Delete(path);
        Console.WriteLine("PASS: " + checks + " startup session assertions using real BepInEx.ConfigFile and FGN startup doubles; saved values, repeated init, disabled AI, client scope and safety blocks checked. No Unity session.");
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
    internal static class FiresGhettoNetworkMod
    {
        public static ConfigEntry<bool> ConfigEnableServerAuthority, ConfigEnableServerOwnership, ConfigEnableServerOwnershipSelective;
        internal static int Starts;
        private static void ApplyServerSideSimulationPatches()
        {
            if (!ConfigEnableServerAuthority.Value || ConfigEnableServerOwnership.Value || ConfigEnableServerOwnershipSelective.Value ||
                !StationRouter.ConfigEnabled.Value || CreatureOwnership.ServerOwnsCreatures || HelmOwnership.ServerOwnsShips)
                throw new Exception("Native initialization received unsafe settings.");
            Starts++;
        }
    }
    internal static class StationRouter { public static ConfigEntry<bool> ConfigEnabled; }
    internal static class CreatureOwnership { public static ConfigEntry<bool> ConfigEnabled; internal static bool ServerOwnsCreatures; }
    internal static class HelmOwnership { internal static bool ServerOwnsShips; }
    internal static class ServerAuthorityPatches { internal static bool SimulationActive; }
    internal static class ServerClientUtils { public static bool IsDedicatedServerDetected { get; set; } }
    internal static class ValheimCommunityPatchCompat
    {
        public static bool SchedulesSceneObjects { get; set; }
        public static void Detect() { }
    }
}
