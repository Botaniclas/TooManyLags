using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using TooManyLags;
using Vcp = ValheimCommunityPatch.APIReceiver;
using Fgn = FiresGhettoNetworkMod.ValheimCommunityPatchCompat;

internal static class CommunityPatchSceneTests
{
    private static int checks;
    private static CommunityPatchSceneCompatibility bridge;
    private static readonly Harmony vcp = new Harmony(CommunityPatchSceneCompatibility.Id);
    private static readonly Harmony other = new Harmony("test.unrelated");
    private static readonly Assembly own = typeof(CommunityPatchSceneTests).Assembly;
    private static readonly string[] fixes = { "SceneIdleSkipPatch", "SpawnQueueCachePatch", "ZoneDiffRemovalPatch", "RemoveSweepPacingPatch" };
    private static MethodInfo Scene(string name) { return AccessTools.DeclaredMethod(typeof(ZNetScene), name); }
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    private static void Reset(bool attach)
    {
        if (bridge != null) bridge.Dispose(); bridge = null;
        vcp.UnpatchSelf(); other.UnpatchSelf();
        Vcp.Reset(); ZNet.instance = null; ZNetScene.instance = null;
        FiresGhettoNetworkMod.ServerClientUtils.IsDedicatedServerDetected = true;
        if (attach)
        {
            Add("CreateDestroyObjects", typeof(ValheimCommunityPatch.Patches.Performance.SceneIdleSkipPatch), "CreateDestroyObjectsPrefix");
            Add("CreateObjectsSorted", typeof(ValheimCommunityPatch.Patches.Performance.SpawnQueueCachePatch), "CreateObjectsSortedPrefix");
            Add("RemoveObjects", typeof(ValheimCommunityPatch.Patches.Performance.ZoneDiffRemovalPatch), "RemoveObjectsPrefix");
            Add("RemoveObjects", typeof(ValheimCommunityPatch.Patches.Performance.RemoveSweepPacingPatch), "RemoveObjectsPrefix");
            Add("RemoveObjects", typeof(ValheimCommunityPatch.Patches.Correctness.RemoveObjectsNrePatch), "RemoveObjectsPrefix");
        }
        Fgn.Detect();
    }
    private static void Add(string target, Type type, string name)
    { vcp.Patch(Scene(target), prefix: new HarmonyMethod(type, name)); }
    private static bool Refused()
    {
        try { bridge = CommunityPatchSceneCompatibility.Start(own, own); return false; }
        catch (NotSupportedException) { return true; }
    }
    private static void Basic()
    {
        Reset(true);
        Check(Fgn.SchedulesSceneObjects, "uncoordinated VCP blocks simulation");
        for (int i = 0; i < 1000; i++) Fgn.Detect();
        var scene = new ZNetScene(); scene.CreateDestroyObjects();
        Check(scene.Created == 0 && scene.Removed == 0, "competing scheduler suppresses native work before handover");
        vcp.Patch(AccessTools.Method(typeof(TerrainComp), "Update"), prefix: new HarmonyMethod(typeof(Unknown), "Prefix"));
        vcp.Patch(AccessTools.Method(typeof(ZDO), "Serialize"), prefix: new HarmonyMethod(typeof(Unknown), "Prefix"));
        other.Patch(Scene("RemoveObjects"), postfix: new HarmonyMethod(typeof(Unknown), "Postfix"));
        bridge = CommunityPatchSceneCompatibility.Start(own, own);
        Fgn.Detect();
        Check(!Fgn.SchedulesSceneObjects, "FGN recognises disabled schedulers and list-based recovery");
        Check(Fgn.TerrainRecovery && Fgn.WriterConflict, "terrain and serialization conflict decisions preserved");
        Check(Vcp.Disables.Count == 4 && !Vcp.Disables.Contains("RemoveObjectsNrePatch"), "only four performance fixes stand down");
        Check(Harmony.GetPatchInfo(Scene("RemoveObjects")).Prefixes.Count == 3, "no VCP prefixes removed");
        scene.CreateDestroyObjects();
        Check(scene.Created == 1 && scene.Removed == 1 && scene.Recoveries == 1, "native creation and VCP orphan recovery both run");
        Check(Unknown.Postfixes == 1, "other mod's postfix still runs");
        for (int i = 0; i < 20; i++) { Fgn.Detect(); scene.CreateDestroyObjects(); }
        Check(!Fgn.SchedulesSceneObjects && Vcp.Disables.Count == 4, "repeat detection and scene passes do not repeat API work");
        Check(ReferenceEquals(bridge, CommunityPatchSceneCompatibility.Start(own, own)), "startup idempotent");
        Check(Vcp.Disables.Count == 4 && Vcp.RecoveryEnabled, "no repeated disable and no recovery change");
        bridge.Dispose(); bridge.Dispose(); bridge = null; Fgn.Detect();
        Check(Fgn.SchedulesSceneObjects, "disposal restores original detector");
        foreach (string fix in fixes) Check(Vcp.Off(fix), "disposal never re-enables a stale scheduler: " + fix);
    }
    private static void Rejections()
    {
        foreach (bool wrongTarget in new[] { false, true })
        {
            Reset(true);
            if (wrongTarget) Add("RemoveObjects", typeof(ValheimCommunityPatch.Patches.Performance.SceneIdleSkipPatch), "CreateDestroyObjectsPrefix");
            else Add("RemoveObjects", typeof(Unknown), "Prefix");
            Check(Refused(), "unknown/misplaced prefix refused");
            Check(Vcp.Disables.Count == 0, "unknown prefix caught before any session mutation");
            Fgn.Detect(); Check(Fgn.SchedulesSceneObjects, "unknown prefix retains safety block");
        }
        foreach (string fix in fixes)
        {
            foreach (int bad in new[] { 0, 5, 99 })
            {
                Reset(true); Vcp.States[fix] = bad;
                Check(Refused() && Vcp.Disables.Count == 0, "unknown/failed fix state rejected before mutation");
            }
            Reset(true); Vcp.CannotDisable = fix;
            Check(Refused() && Vcp.Disables.Count == 0, "non-disableable fix preflight rejected");
            Reset(true); Vcp.Denied = fix;
            Check(Refused(), "API refusal never grants compatibility");
            Fgn.Detect(); Check(Fgn.SchedulesSceneObjects, "partial API refusal leaves native safety detector active");
            Reset(true); Vcp.FalseSuccess = fix;
            Check(Refused(), "success with still-active fix refused");
            Fgn.Detect(); Check(Fgn.SchedulesSceneObjects, "dishonest/incomplete stand-down does not clear safety block");
        }
        Reset(true); Vcp.States["RemoveObjectsNrePatch"] = 5;
        Check(Refused() && Vcp.Disables.Count == 0, "failed recovery contract rejected before disabling any fix");
        Reset(true); Vcp.Version = 0;
        Check(Refused() && Vcp.Disables.Count == 0, "missing API version rejected");
        Reset(true); ZNet.instance = new object();
        Check(Refused() && Vcp.Disables.Count == 0, "live network blocks startup mutation");
        Reset(true); ZNetScene.instance = new object();
        Check(Refused() && Vcp.Disables.Count == 0, "live scene blocks startup mutation");
        Reset(true); FiresGhettoNetworkMod.ServerClientUtils.IsDedicatedServerDetected = false;
        Check(Refused() && Vcp.Disables.Count == 0, "client/local host rejected");
    }
    private static void StatesAndFuturePatches()
    {
        foreach (int state in new[] { 1, 2, 3, 4 })
        {
            Reset(true); foreach (string fix in fixes) Vcp.States[fix] = state;
            bridge = CommunityPatchSceneCompatibility.Start(own, own); Fgn.Detect();
            Check(!Fgn.SchedulesSceneObjects && Vcp.Disables.Count == 4, "all valid states become permanently inert: " + state);
            Add("RemoveObjects", typeof(Unknown), "Prefix"); Fgn.Detect();
            Check(Fgn.SchedulesSceneObjects, "new unknown prefix is not covered by cached permission");
            vcp.Unpatch(Scene("RemoveObjects"), AccessTools.Method(typeof(Unknown), "Prefix")); Fgn.Detect();
            Check(!Fgn.SchedulesSceneObjects, "known hook set recognised again after unknown prefix removal");
            Add("RemoveObjects", typeof(ValheimCommunityPatch.Patches.Performance.SceneIdleSkipPatch), "CreateDestroyObjectsPrefix");
            Fgn.Detect(); Check(Fgn.SchedulesSceneObjects, "known method on new target is still blocked");
        }
        Reset(false);
        Check(CommunityPatchSceneCompatibility.Start(own, own) == null && Vcp.Disables.Count == 0, "no scene prefixes requires no API call or detector hook");
        ZNet.instance = new object();
        Check(CommunityPatchSceneCompatibility.Start(own, null) == null, "absent optional mod never touches world or backend");
        Reset(true);
        bool missing = false;
        try { CommunityPatchSceneCompatibility.Start(own, typeof(string).Assembly); }
        catch (TypeLoadException) { missing = true; }
        Check(missing && Vcp.Disables.Count == 0, "missing contracts fail before mutation");
    }
    private static void CompleteSessionStartup()
    {
        Reset(true);
        string path = Path.Combine(Path.GetTempPath(), "tml-vcp-session-" + Guid.NewGuid().ToString("N") + ".cfg");
        try
        {
            var config = new ConfigFile(path, false) { SaveOnConfigSet = false };
            FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerAuthority = config.Bind("Test", "Simulation", false, "saved");
            FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnership = config.Bind("Test", "Broad", true, "saved");
            FiresGhettoNetworkMod.FiresGhettoNetworkMod.ConfigEnableServerOwnershipSelective = config.Bind("Test", "Selective", true, "saved");
            FiresGhettoNetworkMod.StationRouter.ConfigEnabled = config.Bind("Test", "Stations", false, "saved");
            FiresGhettoNetworkMod.CreatureOwnership.ConfigEnabled = config.Bind("Test", "Creatures", false, "saved");
            FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive = false;
            FiresGhettoNetworkMod.CreatureOwnership.ServerOwnsCreatures = FiresGhettoNetworkMod.HelmOwnership.ServerOwnsShips = true;
            config.Save(); string saved = File.ReadAllText(path);
            bool rejected = false;
            try { FgnSessionSettings.Apply(own, config, true, true, false); }
            catch (NotSupportedException) { rejected = true; }
            Check(rejected && FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts == 0, "real session setup rejects the original scene conflict");
            bridge = CommunityPatchSceneCompatibility.Start(own, own);
            Check(FgnSessionSettings.Apply(own, config, true, true, false), "production bridge and production session setup activate simulation together");
            Check(FiresGhettoNetworkMod.ServerAuthorityPatches.SimulationActive && FiresGhettoNetworkMod.ZSyncHookGate.Ready,
                "normal native initializer and transform gate both complete after stand-down");
            Check(FiresGhettoNetworkMod.FiresGhettoNetworkMod.Starts == 1 && FiresGhettoNetworkMod.ZSyncHookGate.Calls == 1,
                "initializer and sync gate run once");
            var scene = new ZNetScene(); scene.CreateDestroyObjects();
            Check(scene.Simulated == 1 && scene.Created == 1 && scene.Removed == 1 && scene.Recoveries == 1,
                "FGN simulation prefix can use native creation and retained VCP orphan recovery");
            Check(!FgnSessionSettings.Apply(own, config, true, true, false) && FiresGhettoNetworkMod.ZSyncHookGate.Calls == 1,
                "combined setup remains idempotent");
            config.Save(); Check(File.ReadAllText(path) == saved, "combined setup preserves original saved backend settings");
        }
        finally { FiresGhettoNetworkMod.FiresGhettoNetworkMod.Harmony.UnpatchSelf(); if (File.Exists(path)) File.Delete(path); }
    }
    public static int Main()
    {
        // BepInEx normally establishes these paths before plugin Awake. The
        // isolated embedded-Mono host deliberately does not boot BepInEx.
        typeof(BepInEx.Paths).GetMethod("SetExecutablePath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(null, new object[] { own.Location, AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.BaseDirectory, new string[0] });
        try { Basic(); Rejections(); StatesAndFuturePatches(); CompleteSessionStartup(); Reset(false); Console.WriteLine("PASS: " + checks + " CommunityPatch startup/Harmony/session integration assertions; no Unity world."); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static class Unknown
    {
        internal static int Postfixes;
        public static void Prefix() { }
        public static void Postfix() { Postfixes++; }
    }
}

namespace TooManyLags { internal static class Plugin { internal const string Id = "botanic.toomanylags.fgnaddon"; } }
internal static class ZNet { internal static object instance; }
internal sealed class ZDO { [MethodImpl(MethodImplOptions.NoInlining)] public void Serialize() { } }
internal sealed class TerrainComp { [MethodImpl(MethodImplOptions.NoInlining)] public void Update() { } }
internal sealed class ZNetScene
{
    internal static object instance;
    internal int Created, Removed, Recoveries, Simulated;
    [MethodImpl(MethodImplOptions.NoInlining)] public void CreateDestroyObjects()
    { int created = 0; CreateObjectsSorted(new List<ZDO>(), 100, ref created); RemoveObjects(new List<ZDO>(), new List<ZDO>()); }
    [MethodImpl(MethodImplOptions.NoInlining)] public void CreateObjectsSorted(List<ZDO> currentNearObjects, int maxCreatedPerFrame, ref int created) { Created++; created++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RemoveObjects(List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects) { Removed++; }
}
namespace FiresGhettoNetworkMod
{
    internal static class FiresGhettoNetworkMod
    {
        public static ConfigEntry<bool> ConfigEnableServerAuthority, ConfigEnableServerOwnership, ConfigEnableServerOwnershipSelective;
        internal static int Starts;
        internal static Harmony Harmony = new Harmony("test.fgn-simulation");
        private static void ApplyServerSideSimulationPatches()
        {
            if (ValheimCommunityPatchCompat.SchedulesSceneObjects || !ConfigEnableServerAuthority.Value || ConfigEnableServerOwnership.Value ||
                ConfigEnableServerOwnershipSelective.Value || !StationRouter.ConfigEnabled.Value || CreatureOwnership.ServerOwnsCreatures || HelmOwnership.ServerOwnsShips)
                throw new Exception("Native initializer received incomplete setup.");
            Starts++;
            Harmony.Patch(AccessTools.Method(typeof(ZNetScene), "CreateDestroyObjects"), prefix: new HarmonyMethod(typeof(ServerAuthorityPatches), "Prefix"));
        }
    }
    internal static class StationRouter { public static ConfigEntry<bool> ConfigEnabled; }
    internal static class CreatureOwnership { public static ConfigEntry<bool> ConfigEnabled; internal static bool ServerOwnsCreatures; }
    internal static class HelmOwnership { internal static bool ServerOwnsShips; }
    internal static class ZSyncHookGate
    {
        internal static int Calls;
        internal static bool Ready;
        internal static void Refresh(string why) { Calls++; Ready = ServerAuthorityPatches.SimulationActive; }
    }
    internal static class ServerAuthorityPatches
    {
        internal static bool SimulationActive;
        public static bool Prefix(ZNetScene __instance)
        {
            __instance.Simulated++; int count = 0;
            __instance.CreateObjectsSorted(new List<ZDO>(), 100, ref count);
            __instance.RemoveObjects(new List<ZDO>(), new List<ZDO>());
            return false;
        }
    }
    internal static class ServerClientUtils { public static bool IsDedicatedServerDetected { get; set; } }
    internal static class ValheimCommunityPatchCompat
    {
        public static bool SchedulesSceneObjects { get; private set; }
        internal static bool TerrainRecovery, WriterConflict;
        internal static void Detect()
        {
            SchedulesSceneObjects = HasPrefixFrom(AccessTools.Method(typeof(ZNetScene), "CreateDestroyObjects"), null) ||
                HasPrefixFrom(AccessTools.Method(typeof(ZNetScene), "CreateObjectsSorted"), null) || HasPrefixFrom(AccessTools.Method(typeof(ZNetScene), "RemoveObjects"), null);
            TerrainRecovery = HasPrefixFrom(AccessTools.Method(typeof(TerrainComp), "Update"), null);
            WriterConflict = HasPrefixFrom(AccessTools.Method(typeof(ZDO), "Serialize"), null);
        }
        private static bool HasPrefixFrom(MethodBase target, string patchTypeName)
        {
            Patches patches = Harmony.GetPatchInfo(target); if (patches == null) return false;
            foreach (Patch patch in patches.Prefixes)
                if (patch.owner == CommunityPatchSceneCompatibility.Id && (patchTypeName == null || patch.PatchMethod.DeclaringType.FullName == patchTypeName)) return true;
            return false;
        }
    }
}
namespace ValheimCommunityPatch
{
    public static class APIReceiver
    {
        internal static readonly Dictionary<string, int> States = new Dictionary<string, int>();
        internal static readonly List<string> Disables = new List<string>();
        private static readonly HashSet<string> disabled = new HashSet<string>();
        internal static int Version = 1;
        internal static string CannotDisable, Denied, FalseSuccess;
        internal static bool RecoveryEnabled { get { return !disabled.Contains("RemoveObjectsNrePatch"); } }
        internal static void Reset() { States.Clear(); Disables.Clear(); disabled.Clear(); Version = 1; CannotDisable = Denied = FalseSuccess = null; }
        internal static bool Off(string fix) { return disabled.Contains(fix); }
        public static int GetApiVersion() { return Version; }
        public static int GetFixState(string fix)
        {
            int value; if (!States.TryGetValue(fix, out value)) value = 1;
            return value == 2 || value == 4 || value == 5 ? value : disabled.Contains(fix) ? 3 : value;
        }
        public static bool CanDisableFix(string fix) { return CannotDisable != fix; }
        public static bool DisableFix(string fix, string reason)
        { Disables.Add(fix); if (Denied == fix) return false; if (FalseSuccess != fix) disabled.Add(fix); return true; }
    }
}
namespace ValheimCommunityPatch.Patches.Performance
{
    internal static class SceneIdleSkipPatch
    { public static bool CreateDestroyObjectsPrefix(ZNetScene __instance) { return Vcp.Off("SceneIdleSkipPatch"); } }
    internal static class SpawnQueueCachePatch
    { public static bool CreateObjectsSortedPrefix(ZNetScene __instance, List<ZDO> currentNearObjects, int maxCreatedPerFrame, ref int created, bool __runOriginal) { return __runOriginal && Vcp.Off("SpawnQueueCachePatch"); } }
    internal static class ZoneDiffRemovalPatch
    { public static bool RemoveObjectsPrefix(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects, bool __runOriginal) { return __runOriginal && Vcp.Off("ZoneDiffRemovalPatch"); } }
    internal static class RemoveSweepPacingPatch
    { public static bool RemoveObjectsPrefix() { return Vcp.Off("RemoveSweepPacingPatch"); } }
}
namespace ValheimCommunityPatch.Patches.Correctness
{
    internal static class RemoveObjectsNrePatch
    {
        public static bool RemoveObjectsPrefix(ZNetScene __instance, List<ZDO> currentNearObjects, List<ZDO> currentDistantObjects, bool __runOriginal)
        { if (!__runOriginal) return false; __instance.Recoveries++; __instance.Removed++; return false; }
    }
}
