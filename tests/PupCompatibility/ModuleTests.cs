using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Guard = TooManyLags.PupCompatibility.Plugin;

namespace TooManyLags
{
    internal static class Plugin
    {
        internal static bool Diagnostics = false;
        internal static int Reports;
        internal static void Warn(string message) { }
        internal static void Report(string message) { Reports++; }
    }
}

internal static class PupCompatibilityRuntimeTests
{
    private static int checks;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static bool Owned(string owner)
    { return Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains(owner)); }
    private static void OtherPrefix() { }
    public static int Main()
    {
        try
        {
            ZNet.instance = new ZNet(); ZNet.IsSinglePlayer = false;
            FakePup pup = new GameObject().Add(new FakePup());
            var network = typeof(FiresGhettoNetworkMod.OwnershipHandoffPatches).Assembly;
            foreach (bool requested in new[] { false, true })
            {
                Check(Guard.Start(requested, null, network, false) == null, "missing PUP installs nothing");
                Check(Guard.Start(requested, pup, null, false) == null, "missing network dependency installs nothing");
                Check(Guard.Start(requested, pup, network, true) == null, "external PupPatch keeps its guard responsibility");
            }
            Check(Guard.Start(false, pup, network, false) == null, "disabled switch installs nothing with both dependencies");
            Check(Guard.Native == null && !Owned(Guard.Id) && !Owned(Guard.Id + ".streaming") && !Owned(Guard.Id + ".fgn"), "inactive gates leave no native adapter or guard hooks");
            Check(pup.ExactCalls == 0, "inactive gates never query or prepare PUP collisions");
            var external = new Harmony("pup.integration.other-owner");
            external.Patch(AccessTools.Method(typeof(WearNTear), "UpdateSupport"), prefix: new HarmonyMethod(typeof(PupCompatibilityRuntimeTests), "OtherPrefix"));
            Guard module = Guard.Start(true, pup, network, false);
            try
            {
                Check(module != null && Guard.Native != null && Owned(Guard.Id), "valid dependencies install the building and body supplements");
                Check(Owned(Guard.Id + ".streaming") && Owned(Guard.Id + ".fgn"), "streaming and ownership-change supplements are installed");
                Check(ReferenceEquals(module, Guard.Start(true, pup, network, false)), "repeated startup does not duplicate guards");
                Check(!Owned(Guard.Id + ".fgn.aiownership"), "existing TML mob policy is not duplicated by the embedded patch");
                var stream = new GameObject().Add(new ServerZoneStreamBlock()); stream.Run();
                Check(stream.Initialized && !stream.enabled && stream.NativeCalls == 0, "FGN retains streaming instead of PUP's incompatible writer");
                Check(Owned(external.Id), "unrelated Harmony patches remain active");
            }
            finally { if (module != null) module.Dispose(); }
            Check(Guard.Native == null && !Owned(Guard.Id) && !Owned(Guard.Id + ".streaming") && !Owned(Guard.Id + ".fgn"), "disposal removes all embedded guards and readiness state");
            Check(Owned(external.Id), "disposal removes only this module's patches");
            module.Dispose(); Check(Guard.Native == null, "repeated disposal is inert");
            var unpatchedStream = new GameObject().Add(new ServerZoneStreamBlock()); unpatchedStream.Run();
            Check(unpatchedStream.NativeCalls == 1 && unpatchedStream.enabled, "streaming hook is removed on disposal");
            int reports = TooManyLags.Plugin.Reports;
            Check(Guard.Start(true, new GameObject().Add(new MonoBehaviour()), network, false) == null, "changed PUP contract is rejected without crashing startup");
            Check(TooManyLags.Plugin.Reports == reports + 1 && Guard.Native == null && !Owned(Guard.Id), "failed startup reports once and retains no partial core patches");
            module = Guard.Start(true, pup, typeof(object).Assembly, false);
            try
            {
                Check(module != null && Owned(Guard.Id) && !Owned(Guard.Id + ".fgn"), "unsupported optional FGN snap retains the core PUP guards");
                Check(TooManyLags.Plugin.Reports == reports + 2, "unsupported FGN supplement is reported once");
            }
            finally { if (module != null) module.Dispose(); external.UnpatchSelf(); }
            Check(!Owned(Guard.Id) && !Owned(Guard.Id + ".streaming") && Guard.Native == null, "partial optional startup also disposes cleanly");
            Console.WriteLine("PASS: " + checks + " optional PUP module gate/lifecycle/streaming assertions; absent/disabled paths install no guards.");
            RuntimeTests.Main();
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
