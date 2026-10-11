using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using TooManyLags;

internal static class MobPolicyHandoffTests
{
    private static int checks;
    private static ConfigFile config;
    private static readonly Harmony core = new Harmony("botanic.pup.patch");
    private static readonly Harmony stream = new Harmony("botanic.pup.patch.streaming");
    private static readonly Harmony addon = new Harmony("botanic.toomanylags.fgnaddon.fgn.aiownership");
    private static readonly MethodInfo target = typeof(MobPolicyHandoffTests).GetMethod("Target");
    private static Assembly network;
    private static System.Version version;
    [MethodImpl(MethodImplOptions.NoInlining)] public static int Target() { return 1; }
    private static void CorePrefix() { }
    private static void StreamPrefix() { }
    private static void CombatPrefix() { }
    private static void AddonPrefix() { }
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static bool Owned(string owner)
    { Patches p = Harmony.GetPatchInfo(target); return p != null && p.Owners.Contains(owner); }
    private static void Guards()
    {
        Check(Owned("botanic.pup.patch") && Owned("botanic.pup.patch.streaming"), "collision/streaming hooks are preserved");
        Check(config[new ConfigDefinition("FGN AI Ownership", "EnableAIOwnership")].BoxedValue.Equals(true), "PupPatch setting stays enabled and unchanged");
    }
    private static MobPolicyHandoff Begin(bool requested, Type policy)
    { return MobPolicyHandoff.Begin(requested, config, policy, network, version); }
    private static void Setup(bool active)
    {
        addon.UnpatchSelf(); FixturePolicy.Harmony.UnpatchSelf(); FixturePolicy.Enabled = false;
        FixturePolicy.Starts = FixturePolicy.Stops = 0; FixturePolicy.ThrowStop = FixturePolicy.ThrowInstall = false;
        FixturePolicy.FailStop = FixturePolicy.KeepHooks = false;
        if (active) FixturePolicy.Install(config, network, version);
    }
    private static void Run(string path)
    {
        config = new ConfigFile(path, false); config.SaveOnConfigSet = false;
        config.Bind("FGN AI Ownership", "EnableAIOwnership", true, "Retain original setting.");
        config.Save(); string saved = File.ReadAllText(path);
        network = typeof(MobPolicyHandoffTests).Assembly; version = new System.Version(1, 5, 41);
        HarmonyMethod hook = new HarmonyMethod(typeof(MobPolicyHandoffTests), "AddonPrefix");
        core.Patch(target, prefix: new HarmonyMethod(typeof(MobPolicyHandoffTests), "CorePrefix"));
        stream.Patch(target, prefix: new HarmonyMethod(typeof(MobPolicyHandoffTests), "StreamPrefix"));
        try
        {
            foreach (bool simulation in new[] { false, true })
            {
                Setup(true);
                using (MobPolicyHandoff handoff = Begin(true, typeof(FixturePolicy)))
                {
                    Check(!FixturePolicy.Enabled && !Owned(FixturePolicy.Owner), "old combat removed before new ownership installs in either mode");
                    Guards(); addon.Patch(target, prefix: hook);
                    handoff.Commit();
                }
                Check(FixturePolicy.Stops == 1 && FixturePolicy.Starts == 1 && Owned(addon.Id) && !Owned(FixturePolicy.Owner), "successful takeover leaves one combat policy");
                Guards();

                Setup(true);
                using (MobPolicyHandoff handoff = Begin(true, typeof(FixturePolicy)))
                { Check(!FixturePolicy.Enabled, "failed normal/player setup starts with suspended combat"); }
                Check(FixturePolicy.Enabled && FixturePolicy.Starts == 2 && Owned(FixturePolicy.Owner), "uncommitted startup restores existing combat");
                Guards();

                Setup(true);
                try
                {
                    using (MobPolicyHandoff handoff = Begin(true, typeof(FixturePolicy)))
                    { throw new InvalidOperationException(simulation ? "simulation setup" : "combat setup"); }
                }
                catch (InvalidOperationException) { }
                Check(FixturePolicy.Enabled && FixturePolicy.Starts == 2, "throwing setup restores combat exactly once");
            }
            Setup(true);
            using (MobPolicyHandoff handoff = Begin(false, typeof(FixturePolicy))) { }
            Check(FixturePolicy.Enabled && FixturePolicy.Stops == 0 && FixturePolicy.Starts == 1, "disabled addon leaves existing combat untouched");
            Setup(false);
            using (MobPolicyHandoff handoff = Begin(true, typeof(FixturePolicy))) { }
            Check(FixturePolicy.Starts == 0 && FixturePolicy.Stops == 0, "inactive optional module is never enabled by rollback");
            using (MobPolicyHandoff handoff = Begin(true, null)) { handoff.Commit(); }
            Guards();

            Setup(false); FixturePolicy.Harmony.Patch(target, prefix: new HarmonyMethod(typeof(MobPolicyHandoffTests), "CombatPrefix"));
            using (MobPolicyHandoff handoff = Begin(true, typeof(FixturePolicy))) { }
            Check(!Owned(FixturePolicy.Owner) && FixturePolicy.Starts == 0, "inactive residual combat hooks removed without enabling disabled module");

            Setup(true); bool blocked = false;
            try { using (MobPolicyHandoff handoff = Begin(true, typeof(UnknownPolicy))) { } }
            catch (NotSupportedException) { blocked = true; }
            Check(blocked && Owned(FixturePolicy.Owner) && FixturePolicy.Stops == 0, "unsupported contract fails before mutation");
            blocked = false;
            try { using (MobPolicyHandoff handoff = Begin(true, null)) { } }
            catch (NotSupportedException) { blocked = true; }
            Check(blocked && Owned(FixturePolicy.Owner), "undiscoverable retained combat blocks second policy");

            foreach (bool leavesHooks in new[] { false, true })
            {
                Setup(true); FixturePolicy.FailStop = !leavesHooks; FixturePolicy.KeepHooks = leavesHooks; blocked = false;
                try { using (MobPolicyHandoff handoff = Begin(true, typeof(FixturePolicy))) { handoff.Commit(); } }
                catch (InvalidOperationException) { blocked = true; }
                Check(blocked && Owned(FixturePolicy.Owner) && FixturePolicy.Starts == 1, "incomplete stop blocks takeover without stacking old transpilers");
                Guards();
            }
            Setup(true); FixturePolicy.ThrowStop = true; blocked = false;
            try { using (MobPolicyHandoff handoff = Begin(true, typeof(FixturePolicy))) { } }
            catch (InvalidOperationException) { blocked = true; }
            Check(blocked && FixturePolicy.Enabled && FixturePolicy.Starts == 2, "stop failure after release restores previous module");
            Setup(true);
            MobPolicyHandoff failedRestore = Begin(true, typeof(FixturePolicy)); FixturePolicy.ThrowInstall = true; blocked = false;
            try { failedRestore.Dispose(); } catch (InvalidOperationException) { blocked = true; }
            failedRestore.Dispose();
            Check(blocked && FixturePolicy.Starts == 2, "restore failure propagates once, never loops or retries every frame");
            Guards(); Check(saved == File.ReadAllText(path), "saved config unchanged throughout takeover and rollback");
        }
        finally { addon.UnpatchSelf(); FixturePolicy.Harmony.UnpatchSelf(); core.UnpatchSelf(); stream.UnpatchSelf(); }
        Console.WriteLine("PASS: " + checks + " combat precedence assertions; actual Harmony hooks and BepInEx config, successful/disabled/failed startup and collision guards. No Unity session.");
    }
    public static int Main(string[] args)
    {
        string directory = Path.GetDirectoryName(typeof(MobPolicyHandoffTests).Assembly.Location);
        string configPath = args.Length > 0 ? args[0] : Path.Combine(directory, "mono-fixture.cfg");
        string corePath = args.Length > 1 ? args[1] : directory;
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
        { string p = Path.Combine(corePath, new AssemblyName(request.Name).Name + ".dll"); return File.Exists(p) ? Assembly.LoadFrom(p) : null; };
        try
        {
            // An embedded Mono fixture has no BepInEx preloader. Initialize its
            // paths inside the test folder before ConfigFile's global initializer.
            typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Invoke(null, new object[] { typeof(MobPolicyHandoffTests).Assembly.Location, directory, null, null });
            Run(configPath); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static class UnknownPolicy { internal static bool Enabled = true; }
    private static class FixturePolicy
    {
        internal const string Owner = "botanic.pup.patch.fgn.aiownership";
        internal static readonly Harmony Harmony = new Harmony(Owner);
        internal static bool Enabled, ThrowStop, FailStop, KeepHooks, ThrowInstall;
        internal static int Starts, Stops;
        internal static void Install(ConfigFile original, Assembly assembly, System.Version detected)
        {
            Starts++;
            if (!ReferenceEquals(original, config) || !ReferenceEquals(assembly, network) || detected != version)
                throw new Exception("Rollback did not reuse the original configuration and network contract.");
            if (ThrowInstall) return;
            Harmony.Patch(target, prefix: new HarmonyMethod(typeof(MobPolicyHandoffTests), "CombatPrefix")); Enabled = true;
        }
        private static void Stop()
        {
            Stops++;
            if (FailStop) return;
            Enabled = false;
            if (!KeepHooks) Harmony.UnpatchSelf();
            if (ThrowStop) throw new InvalidOperationException("Stop failed after release.");
        }
    }
}
