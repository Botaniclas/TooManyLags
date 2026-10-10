using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("1.0.7.0")]

namespace TooManyLags
{
    [BepInPlugin(Id, "TooManyLags - FGN Addon", "1.0.7")]
    [BepInDependency(FgnId)]
    [BepInIncompatibility("botanic.server.buildings")]
    [BepInIncompatibility("botanic.server.authority")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "botanic.toomanylags.fgnaddon", Version = "1.0.7", FgnId = "com.Fire.FiresGhettoNetworkMod", PupId = "com.pup.instancecombiner.safe";
        internal static AuthorityController Controller;
        internal static NativePup Native;
        internal static bool Diagnostics;
        private static Plugin instance;
        private Harmony harmony;
        private bool logs;
        private bool enabledMod;
        private CameraPublisher camera;
        private PluginInfo networkPlugin;
        private void Awake()
        {
            instance = this;
            bool enabled = Config.Bind("01 - General", "Enabled", true, "Enable TooManyLags on the dedicated server and clients. Required FiresGhettoNetworking settings are overridden for this session, including Server-Side Simulation on the dedicated server. This can increase server CPU usage. Restart after config changes.").Value;
            logs = Config.Bind("01 - General", "EnableLogs", false, "Enable extra diagnostic messages. Startup failures are always reported.").Value;
            Diagnostics = logs;
            bool buildings = Config.Bind("02 - Static Buildings", "ServerOwnedStaticBuildings", false, "Keep eligible non-interactive building pieces server-owned. Independent of the interactive grid.").Value;
            bool defaultInteractives = Config.Bind("03 - Default Ownership and Overrides", "ServerOwnedInteractivesByDefault", false,
                "true: server default outside player coverage, with grid transfers nearby. false: vanilla ownership when players are alone or far apart. KeepOwnershipOnCellExit preserves ownership after a field is vacated.").Value;
            bool refillStations = Config.Bind("03 - Default Ownership and Overrides", "ServerOwnedRefillStations", false,
                "Keep enabled refill stations server-owned, even inside player fields: smelters, cooking/fire stations, fermenters, shield generators and turrets. false: grid rules with 10 seconds of protection after observed refilling.").Value;
            int cellSize = Config.Bind("04 - Grid Settings", "GridCellSizeMeters", 4, new ConfigDescription(
                "Cell size in metres: 4, 8 or 16. 4 metres requires Rounded5x5/Rounded7x7. Does not change vanilla/PUP loading distances.",
                new AcceptableValueList<int>(4, 8, 16))).Value;
            string patternName = Config.Bind("04 - Grid Settings", "GridCoveragePattern", "Rounded7x7", new ConfigDescription(
                "Base shape: Square3x3 is square; Rounded5x5/Rounded7x7 are rounded. Directional mode omits central and far rear fields. Fields covered by multiple players belong to the server.",
                new AcceptableValueList<string>(cellSize == 4 ? new[] { "Rounded5x5", "Rounded7x7" } : new[] { "Rounded5x5", "Square3x3", "Rounded7x7" }))).Value;
            GridCoveragePattern pattern = patternName == "Rounded7x7" ? GridCoveragePattern.Rounded7x7 : patternName == "Rounded5x5" ? GridCoveragePattern.Rounded5x5 : GridCoveragePattern.Square3x3;
            bool directional = Config.Bind("05 - Directional Grid Mode", "DirectionalGridMode", true,
                "true: camera-directed grid with the three timers below. false: full grid from section 06. Clients publish their camera's eight heading sectors. Missing camera data falls back to character heading. Stationary players still count towards overlaps.").Value;
            float innerDelay = Config.Bind("05 - Directional Grid Mode", "InnerTransitionDelaySeconds", 0f, new ConfigDescription(
                "Red: six inner fields, your three-cell row and the row ahead. Delay in seconds; 0 allowed. Handoff protection remains active.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float forwardDelay = Config.Bind("05 - Directional Grid Mode", "ForwardTransitionDelaySeconds", .2f, new ConfigDescription(
                "Orange: one extra three-cell row ahead of red. Active only with 4-metre cells and Rounded5x5/Rounded7x7. Delay in seconds; 0 allowed. The farthest 7x7 row stays yellow.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float outerDelay = Config.Bind("05 - Directional Grid Mode", "OuterTransitionDelaySeconds", 3f, new ConfigDescription(
                "Yellow: remaining active fields. Delay in seconds. White rear fields make no request. Overlaps use the earliest still-valid request deadline.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float delay = Config.Bind("06 - Full Grid Mode", "TransitionDelaySeconds", 2f, new ConfigDescription(
                "Only with DirectionalGridMode=false: one delay in seconds for the full grid. Active use and safe handoffs may require more time.", new AcceptableValueRange<float>(1f, 30f))).Value;
            if (Single.IsNaN(delay) || Single.IsInfinity(delay)) delay = 2f;
            float interval = Config.Bind("04 - Grid Settings", IntervalConfig.Key, IntervalConfig.Default, new ConfigDescription(
                "Check interval in seconds for cell changes and, in directional mode, changes between eight 45-degree heading sectors. Only changes request fields. Existing timers/handoffs also progress while standing.", new AcceptableValueRange<float>(.1f, 5f))).Value;
            if (Single.IsNaN(interval) || Single.IsInfinity(interval)) interval = IntervalConfig.Default;
            bool keepOnExit = Config.Bind("04 - Grid Settings", "KeepOwnershipOnCellExit", true,
                "true: keep ownership after everyone leaves. In directional mode, when one player remains, only red fields transfer automatically; orange/yellow wait for that player's next cell/heading change. Completed player fields are cached until relevant changes/unloading. false: normal departure transfers, no cache. Native release and pinned stations still apply.").Value;
            Kinds kinds = Kinds.None;
            foreach (Kinds kind in Enum.GetValues(typeof(Kinds)))
                if (kind != Kinds.None && kind != Kinds.Incinerators && Config.Bind("07 - Object Categories", kind.ToString(), true,
                    ObjectDescription(kind)).Value) kinds |= kind;
            float cameraInterval = Config.Bind("08 - Camera Heading", "CameraHeadingCheckIntervalSeconds", .1f, new ConfigDescription(
                "Client camera check interval in seconds. Minimum 0.05 allows up to 20 checks per second. Only sector changes update player data; no extra camera RPC. Server grid timing is set in section 04.", new AcceptableValueRange<float>(.05f, 5f))).Value;
            if (!enabled) return;
            enabledMod = true;
            if (!Chainloader.PluginInfos.TryGetValue(FgnId, out networkPlugin) || !networkPlugin.Instance)
            { enabledMod = false; Logger.LogWarning("TooManyLags inactive: FiresGhettoNetworking is not loaded."); return; }
            try
            {
                bool enableAI = Config.Bind("09 - AI Mob Ownership", "EnableAIOwnership", true,
                    "Use the mob's active player target for FGN ownership. Requires two eligible nearby players. Attacks block transfers and retry after attack end. Install the same version on server and clients; restart required.").Value;
                bool activated = FgnSessionSettings.Apply(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config,
                    Application.isBatchMode, enableAI, Chainloader.PluginInfos.ContainsKey("dev.ontrigger.vpo"));
                if (activated) Logger.LogWarning("TooManyLags enabled FGN Server-Side Simulation for this session. This can increase server CPU usage; the saved FGN config is unchanged.");
            }
            catch (Exception error)
            { enabledMod = false; Logger.LogWarning("TooManyLags inactive: required FGN session settings could not be applied. " + error.GetBaseException()); return; }
            if (!Application.isBatchMode)
            {
                try
                {
                    camera = new CameraPublisher(cameraInterval);
                    harmony = new Harmony(Id + ".camera");
                    harmony.CreateClassProcessor(typeof(CameraSamplePatch)).Patch();
                    harmony.CreateClassProcessor(typeof(CameraLossPatch)).Patch();
                }
                catch (Exception error)
                { camera = null; Cleanup(); Logger.LogWarning("Camera publication inactive; character heading remains available. " + error.GetBaseException()); }
                return;
            }
            if (!buildings && kinds == Kinds.None) return;
            try
            {
                PluginInfo fgn = networkPlugin;
                CompatibilityBridge bridge = new CompatibilityBridge(fgn.Instance);
                if (!bridge.Configured()) throw new NotSupportedException("Required FGN session settings are unavailable. " + bridge.State());
                // Bind required client protocol signatures before installing
                // any hooks; changed APIs are reported even when logs are off.
                new FgnOwnerHandoff(bridge.Assembly, 1L);
                Controller = new AuthorityController(kinds, delay, bridge, RuntimeFault, buildings, RequestFault, interval, defaultInteractives, refillStations, cellSize, pattern, keepOnExit, directional, innerDelay, outerDelay, forwardDelay);
                OwnershipTranspiler.Rewrite(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS")), AccessTools.Method(typeof(OwnershipPatch), "Persistent"));
                PickableServerPatch.Rewrite(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Pickable), "RPC_Pick")));
                harmony = new Harmony(Id); harmony.PatchAll(typeof(Plugin).Assembly);
                FgnRpcIntegration.Install(harmony, fgn.Instance.GetType().Assembly);
                if (logs) Logger.LogInfo("TooManyLags grid ready: cellSize=" + cellSize + "; pattern=" + pattern + "; check=" + interval + "; cameraDirected=" + directional + "; inner=" + innerDelay + "; forward=" + forwardDelay + "; outer=" + outerDelay + "; fullGridDelay=" + delay + "; defaultInteractives=" + defaultInteractives + "; keepOnExit=" + keepOnExit + "; refillStations=" + refillStations + "; " + bridge.State());
            }
            catch (Exception error)
            {
                if (Controller != null) Controller.Reset(); Controller = null; Cleanup();
                Logger.LogWarning("TooManyLags object grid inactive; native ownership retained. " + error.GetBaseException());
            }
        }
        private void Start()
        {
            if (!enabledMod || networkPlugin == null || !networkPlugin.Instance) return;
            // All plugin Awakes have completed: optional PUP and duplicate
            // policies are now visible without introducing a soft dependency.
            try
            {
                PluginInfo pup;
                Native = new NativePup(Chainloader.PluginInfos.TryGetValue(PupId, out pup) && pup.Instance ? pup.Instance : null);
            }
            catch (Exception error)
            { Native = null; Logger.LogWarning("PUP readiness unavailable; AI server fallback stays blocked while PUP is present. " + error.GetBaseException().Message); }
            try
            {
                bool duplicate = ExistingMobPolicy();
                FgnCombatOwnership.Install(Config, networkPlugin.Instance.GetType().Assembly, networkPlugin.Metadata.Version, !duplicate);
                FgnCombatStatus.Register();
                if (!String.IsNullOrEmpty(FgnCombatOwnership.StartupError)) Logger.LogWarning("TooManyLags AI ownership inactive: " + FgnCombatOwnership.StartupError);
            }
            catch (Exception error)
            { FgnCombatOwnership.Stop(); Logger.LogWarning("TooManyLags AI ownership inactive: " + error.GetBaseException()); }
        }
        private static bool ExistingMobPolicy()
        {
            PluginInfo pup;
            if (Chainloader.PluginInfos.TryGetValue("botanic.pup.patch", out pup) && pup.Instance)
            {
                Type policy = pup.Instance.GetType().Assembly.GetType("BotanicPupPatch.FgnCombatOwnership", false);
                FieldInfo active = policy == null ? null : policy.GetField("Enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (active != null && active.FieldType == typeof(bool) && (bool)active.GetValue(null)) return true;
            }
            Patches patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS"));
            return patches != null && patches.Owners.Contains("botanic.pup.patch.fgn.aiownership");
        }
        internal static void SampleCamera() { if (instance && instance.camera != null) instance.camera.Sample(Time.unscaledTime); }
        internal static void InvalidateCamera() { if (instance && instance.camera != null) instance.camera.Reset(true); }
        internal static void Warn(string message) { if (Diagnostics && instance) instance.Logger.LogWarning(message); }
        private static string ObjectDescription(Kinds kind)
        {
            string name;
            switch (kind)
            {
                case Kinds.Doors: name = "Doors and gates"; break;
                case Kinds.Seats: name = "Seats and barber stations"; break;
                case Kinds.Beds: name = "Beds"; break;
                case Kinds.Workstations: name = "Crafting stations and upgrades"; break;
                case Kinds.Smelters: name = "Smelters, kilns, windmills and similar stations"; break;
                case Kinds.CookingStations: name = "Cooking stations"; break;
                case Kinds.Fireplaces: name = "Fireplaces and refillable light sources"; break;
                case Kinds.Beehives: name = "Beehives"; break;
                case Kinds.SapCollectors: name = "Sap collectors"; break;
                case Kinds.Wards: name = "Wards"; break;
                case Kinds.MapTables: name = "Cartography tables"; break;
                case Kinds.Wisplights: name = "Wisp fountains"; break;
                case Kinds.TamedAnimals: name = "Idle tamed animals; friendly magic summons excluded; riding and following remain protected"; break;
                case Kinds.Containers: name = "Public chests and inventories; open inventories remain protected"; break;
                case Kinds.ItemStands: name = "Item and armour stands"; break;
                case Kinds.Signs: name = "Signs"; break;
                case Kinds.Portals: name = "Portals"; break;
                case Kinds.Fermenters: name = "Fermenters"; break;
                case Kinds.Defenses: name = "Traps and turrets"; break;
                case Kinds.ShieldGenerators: name = "Shield generators"; break;
                case Kinds.Pickables: name = "Pickable objects and feasts; excludes loose item drops"; break;
                case Kinds.Offerings: name = "Offering altars"; break;
                case Kinds.Vehicles: name = "Idle carts and vehicle controls; active users and ships managed by FiresGhettoNetworking are excluded"; break;
                default: name = "Other compatible modded interactables"; break;
            }
            return name + ". true: managed by TooManyLags; false: vanilla/FiresGhettoNetworking.";
        }
        private void RuntimeFault(Exception error) { if (logs) Logger.LogWarning("TooManyLags grid inactive; native ownership retained. " + error.GetBaseException()); }
        private void RequestFault(Exception error) { if (logs) Logger.LogWarning("Native object request failed; not replayed. " + error.GetBaseException()); }
        private void Cleanup() { if (harmony != null) harmony.UnpatchSelf(); }
        private void OnDestroy()
        {
            FgnCombatOwnership.Stop(); FgnCombatStatus.Reset();
            if (Controller != null) Controller.Reset(); Controller = null;
            if (camera != null) camera.Reset(true); camera = null;
            Native = null; Cleanup(); if (ReferenceEquals(instance, this)) instance = null;
        }
    }
    [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
    internal static class OwnershipPatch
    {
        [HarmonyTranspiler, HarmonyPriority(Priority.First), HarmonyBefore(Plugin.FgnId)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        { return OwnershipTranspiler.Rewrite(source, AccessTools.Method(typeof(OwnershipPatch), "Persistent")); }
        internal static bool Persistent(ZDO data) { return Plugin.Controller == null ? data.Persistent : Plugin.Controller.Persistent(data); }
    }
    [HarmonyPatch(typeof(ZNetScene), "AddInstance")]
    internal static class AddPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZDO zdo, ZNetView nview) { if (Plugin.Controller != null) Plugin.Controller.Register(zdo, nview); }
    }
    [HarmonyPatch]
    internal static class RemovePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { yield return AccessTools.Method(typeof(ZNetView), "ResetZDO"); yield return AccessTools.Method(typeof(ZNetView), "OnDestroy"); }
        [HarmonyPrefix]
        private static void Prefix(ZNetView __instance) { if (Plugin.Controller != null) Plugin.Controller.Unregister(__instance); }
    }
    [HarmonyPatch]
    internal static class ShutdownPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { yield return AccessTools.Method(typeof(ZNetScene), "Shutdown"); yield return AccessTools.Method(typeof(ZNet), "Shutdown"); }
        [HarmonyPrefix]
        private static void Prefix() { if (Plugin.Controller != null) Plugin.Controller.Reset(); }
    }
    [HarmonyPatch(typeof(ZNetScene), "OnDestroy")]
    internal static class SceneDestroyedPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZNetScene __instance)
        { if (Plugin.Controller != null) Plugin.Controller.SceneDestroyed(__instance); }
    }
    [HarmonyPatch(typeof(ZDO), "InternalSetPosition")]
    internal static class PositionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZDO __instance) { if (Plugin.Controller != null) Plugin.Controller.Position(__instance); }
    }
    [HarmonyPatch(typeof(ZDO), "Deserialize")]
    internal static class DataPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZDO __instance) { if (Plugin.Controller != null) Plugin.Controller.DataChanged(__instance); }
    }
    [HarmonyPatch]
    internal static class OwnerPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { yield return AccessTools.Method(typeof(ZDO), "SetOwnerInternal"); yield return AccessTools.PropertySetter(typeof(ZDO), "OwnerRevision"); }
        [HarmonyPostfix]
        private static void Postfix(ZDO __instance, MethodBase __originalMethod) { if (Plugin.Controller != null) Plugin.Controller.OwnerChanged(__instance, __originalMethod.Name == "SetOwnerInternal"); }
    }
    [HarmonyPatch(typeof(ZDO), "Set", new[] { typeof(int), typeof(bool) })]
    internal static class TamedFlagPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZDO __instance, int __0) { if (__0 == ZDOVars.s_tamed && Plugin.Controller != null) Plugin.Controller.DataChanged(__instance); }
    }
    [HarmonyPatch(typeof(Container), "RPC_OpenResponse")]
    internal static class InventoryResponsePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Container __instance, long __0, bool __1) { if (Plugin.Controller != null) Plugin.Controller.InventoryResponse(__instance, __0, __1); }
    }
    [HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
    internal static class SentPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyAfter(Plugin.FgnId)]
        private static void Postfix() { if (Plugin.Controller != null) Plugin.Controller.Router.Flush(); }
    }
    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    internal static class LocalRequestPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyBefore(Plugin.FgnId)]
        private static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData __0, bool __runOriginal)
        { return __runOriginal && (Plugin.Controller == null || !Plugin.Controller.Router.TryHandle(__instance, __0, true)); }
    }
    [HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
    internal static class RemoteRequestPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First), HarmonyBefore(Plugin.FgnId)]
        private static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData __0, bool __runOriginal)
        { return __runOriginal && (Plugin.Controller == null || !Plugin.Controller.Router.TryHandle(__instance, __0, false)); }
    }
}
