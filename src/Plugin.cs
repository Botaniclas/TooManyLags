using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("1.0.13.0")]

namespace TooManyLags
{
    [BepInPlugin(Id, "TooManyLags - FiresGhettoNetworking Addon", "1.0.13")]
    [BepInDependency(FgnId)]
    [BepInIncompatibility("botanic.server.buildings")]
    [BepInIncompatibility("botanic.server.authority")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "botanic.toomanylags.fgnaddon", Version = "1.0.13", FgnId = "com.Fire.FiresGhettoNetworkMod", PupId = "com.pup.instancecombiner.safe";
        internal static AuthorityController Controller;
        internal static NativePup Native;
        internal static bool Diagnostics;
        private static Plugin instance;
        private Harmony harmony;
        private Harmony configHarmony;
        private ConfigCommands commands;
        private bool logs;
        private bool enabledMod;
        private CameraPublisher camera;
        private PluginInfo networkPlugin;
        private bool playerSimulation;
        private void Awake()
        {
            instance = this;
            bool enabled = Config.Bind("01 - General", "Enabled", true, "Server and each client; restart required. Enable TooManyLags on this installation. Required FiresGhettoNetworking settings are applied for this session.").Value;
            bool noSimulation = Config.Bind("01 - General", "NoServerSideSimulation", false,
                "Server only; restart required. Recommended for dedicated servers with a weak or already overloaded CPU. true: disable FiresGhettoNetworking Server-Side Simulation and keep simulation on players; shared fields prefer the player with lower latency. Functionality is limited: no server ownership, and server-owned building/default/refill options are ignored. false: full Server-Side Simulation mode.").Value;
            logs = Config.Bind("01 - General", "EnableLogs", false, "Local to this server/client. Live console: tml_config EnableLogs true/false. Enable extra diagnostic messages. Startup failures are always reported.").Value;
            Diagnostics = logs;
            bool buildings = Config.Bind("02 - Static Buildings", "ServerOwnedStaticBuildings", false, "Server only; restart required. Keep eligible non-interactive building pieces server-owned. Independent of the interactive grid. Ignored with NoServerSideSimulation=true.").Value;
            bool defaultInteractives = Config.Bind("03 - Default Ownership and Overrides", "ServerOwnedInteractivesByDefault", false,
                "Server only; restart required. true: server default outside player coverage, with grid transfers nearby. false: vanilla ownership when players are alone or far apart. KeepOwnershipOnCellExit preserves ownership after a field is vacated. Ignored with NoServerSideSimulation=true.").Value;
            bool refillStations = Config.Bind("03 - Default Ownership and Overrides", "ServerOwnedRefillStations", false,
                "Server only; restart required. Keep enabled refill stations server-owned, even inside player fields: smelters, cooking/fire stations, fermenters, shield generators and turrets. false: grid rules with 10 seconds of protection after observed refilling. Ignored with NoServerSideSimulation=true.").Value;
            int cellSize = Config.Bind("04 - Grid Settings", "GridCellSizeMeters", 4, new ConfigDescription(
                "Server only; restart required. Cell size in metres: 4, 8 or 16. 4 metres requires Rounded5x5/Rounded7x7. Does not change vanilla/PUP loading distances.",
                new AcceptableValueList<int>(4, 8, 16))).Value;
            ConfigEntry<string> patternConfig = Config.Bind("04 - Grid Settings", "GridCoveragePattern", "Rounded7x7", new ConfigDescription(
                "Server only; restart required. Base shape: Square3x3 is square; Rounded5x5/Rounded7x7 are rounded. Directional mode omits central and far rear fields. Overlaps use the server, or a lower-latency player with NoServerSideSimulation=true.",
                new AcceptableValueList<string>("Rounded5x5", "Square3x3", "Rounded7x7")));
            string patternName = patternConfig.Value;
            if (cellSize == 4 && patternName == "Square3x3")
            {
                patternName = "Rounded5x5";
                patternConfig.Value = patternName;
            }
            GridCoveragePattern pattern = patternName == "Rounded7x7" ? GridCoveragePattern.Rounded7x7 : patternName == "Rounded5x5" ? GridCoveragePattern.Rounded5x5 : GridCoveragePattern.Square3x3;
            bool directional = Config.Bind("05 - Directional Grid Mode", "DirectionalGridMode", true,
                "Server only; restart required. true: camera-directed grid with the timers below. Each direction covers +/-37.5 degrees, giving adjacent directions 30 degrees of overlap. false: full grid from section 06. Missing camera data uses character heading. Stationary players still count.").Value;
            float innerDelay = Config.Bind("05 - Directional Grid Mode", "InnerTransitionDelaySeconds", 0f, new ConfigDescription(
                "Server only; live console supported. Red: six inner fields, your three-cell row and the row ahead. Delay in seconds for new requests; 0 allowed. Handoff protection remains active.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float forwardDelay = Config.Bind("05 - Directional Grid Mode", "ForwardTransitionDelaySeconds", .2f, new ConfigDescription(
                "Server only; live console supported. Orange: one extra three-cell row ahead of red. Active only with 4-metre cells and Rounded5x5/Rounded7x7. Delay in seconds for new requests; 0 allowed. The farthest 7x7 row stays yellow.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float outerDelay = Config.Bind("05 - Directional Grid Mode", "OuterTransitionDelaySeconds", 3f, new ConfigDescription(
                "Server only; live console supported. Yellow: remaining active fields. Delay in seconds for new requests. White rear fields make no request. Overlaps use the earliest still-valid request deadline.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float overlapCooldown = Config.Bind("05 - Directional Grid Mode", "ServerOverlapCooldownSeconds", 1f, new ConfigDescription(
                "Server only; live console supported. After a directional-grid overlap assigns a field to the server, ignore new grid ownership decisions for this many seconds. Expiry alone causes no transfer; the next movement/heading update can request the field again. 0 disables. Empty fields/native release and usage protection remain unchanged. Ignored without Server-Side Simulation. New cooldowns use the updated value.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float delay = Config.Bind("06 - Full Grid Mode", "TransitionDelaySeconds", 2f, new ConfigDescription(
                "Server only; live console supported. Only with DirectionalGridMode=false: one delay in seconds for new full-grid requests. Active use and safe handoffs may require more time.", new AcceptableValueRange<float>(1f, 30f))).Value;
            if (Single.IsNaN(delay) || Single.IsInfinity(delay)) delay = 2f;
            float interval = Config.Bind("04 - Grid Settings", IntervalConfig.Key, IntervalConfig.Default, new ConfigDescription(
                "Server only; live console supported. Check interval in seconds for movement and fallback heading. Received camera-coverage changes request an immediate pass. Only changed coverage requests fields; existing timers/handoffs also progress while standing.", new AcceptableValueRange<float>(.05f, 5f))).Value;
            if (Single.IsNaN(interval) || Single.IsInfinity(interval)) interval = IntervalConfig.Default;
            bool keepOnExit = Config.Bind("04 - Grid Settings", "KeepOwnershipOnCellExit", true,
                "Server only; restart required. true: keep ownership after everyone leaves. In directional mode, when one player remains, only red fields transfer automatically; orange/yellow wait for that player's next cell/heading change. Completed player fields are cached until relevant changes/unloading. false: normal departure transfers, no cache. Native release and pinned stations still apply.").Value;
            Kinds kinds = Kinds.None;
            foreach (Kinds kind in Enum.GetValues(typeof(Kinds)))
                if (kind != Kinds.None && kind != Kinds.Incinerators && Config.Bind("10 - Object Categories", kind.ToString(), true,
                    ObjectDescription(kind)).Value) kinds |= kind;
            float cameraInterval = Config.Bind("08 - Camera Heading", "CameraHeadingCheckIntervalSeconds", .05f, new ConfigDescription(
                "Server only; live console supported. Camera check interval in seconds, sent to all clients. Client value is ignored. 0.05 allows up to 20 checks per second; only coverage changes are sent. Server grid timing is in section 04.", new AcceptableValueRange<float>(.05f, 5f))).Value;
            if (!enabled) return;
            enabledMod = true;
            playerSimulation = Application.isBatchMode && noSimulation;
            if (!Chainloader.PluginInfos.TryGetValue(FgnId, out networkPlugin) || !networkPlugin.Instance)
            { enabledMod = false; Logger.LogWarning("TooManyLags inactive: FiresGhettoNetworking is not loaded."); return; }
            try
            {
                bool enableAI = Config.Bind("09 - AI Mob Ownership", "EnableAIOwnership", true,
                    "Server and all clients; use the same value; restart required. Use the mob's active player target for FiresGhettoNetworking ownership. Requires two eligible nearby players. Attacks block transfers and retry after attack end. Install the same version on server and clients.").Value;
                bool activated = FgnSessionSettings.Apply(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config,
                    Application.isBatchMode, enableAI, Chainloader.PluginInfos.ContainsKey("dev.ontrigger.vpo"), playerSimulation);
                if (activated) Logger.LogWarning("TooManyLags enabled FiresGhettoNetworking Server-Side Simulation for this session. This can increase server CPU usage; its saved config is unchanged.");
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
            if (playerSimulation) return;
            if (!buildings && kinds == Kinds.None) return;
            try
            {
                PluginInfo fgn = networkPlugin;
                CompatibilityBridge bridge = new CompatibilityBridge(fgn.Instance);
                if (!bridge.Configured()) throw new NotSupportedException("Required FGN session settings are unavailable. " + bridge.State());
                // Bind required client protocol signatures before installing
                // any hooks; changed APIs are reported even when logs are off.
                new FgnOwnerHandoff(bridge.Assembly, 1L);
                Controller = new AuthorityController(kinds, delay, bridge, RuntimeFault, buildings, RequestFault, interval, defaultInteractives, refillStations, cellSize, pattern, keepOnExit, directional, innerDelay, outerDelay, forwardDelay, overlapCooldown);
                OwnershipTranspiler.Rewrite(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS")), AccessTools.Method(typeof(OwnershipPatch), "Persistent"));
                PickableServerPatch.Rewrite(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Pickable), "RPC_Pick")));
                harmony = new Harmony(Id);
                foreach (Type type in typeof(Plugin).Assembly.GetTypes())
                    if (type.Namespace == typeof(Plugin).Namespace && type != typeof(ConfigConnectionPatch) && type != typeof(ConfigShutdownPatch) && type != typeof(ConfigReadyPatch) && Attribute.IsDefined(type, typeof(HarmonyPatch)))
                        harmony.CreateClassProcessor(type).Patch();
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
            try
            {
                commands = new ConfigCommands(Config, ApplyLiveSetting);
                ConfigConsole.Register();
                configHarmony = new Harmony(Id + ".config");
                configHarmony.CreateClassProcessor(typeof(ConfigConnectionPatch)).Patch();
                configHarmony.CreateClassProcessor(typeof(ConfigShutdownPatch)).Patch();
                configHarmony.CreateClassProcessor(typeof(ConfigReadyPatch)).Patch();
            }
            catch (Exception error)
            {
                if (configHarmony != null) configHarmony.UnpatchSelf();
                commands = null; Logger.LogWarning("Live configuration unavailable: " + error.GetBaseException().Message);
            }
            // All plugin Awakes have completed: the optional combat module can
            // release its hooks before ours are installed, independent of load order.
            try
            {
                PluginInfo pup;
                Native = new NativePup(Chainloader.PluginInfos.TryGetValue(PupId, out pup) && pup.Instance ? pup.Instance : null);
            }
            catch (Exception error)
            { Native = null; Logger.LogWarning("PUP readiness unavailable; AI server fallback stays blocked while PUP is present. " + error.GetBaseException().Message); }
            MobPolicyHandoff takeover = null;
            try
            {
                ConfigEntry<bool> ai;
                bool requested = Config.TryGetEntry<bool>(new ConfigDefinition("09 - AI Mob Ownership", "EnableAIOwnership"), out ai) && ai.Value;
                bool allowed = requested;
                try
                {
                    PluginInfo guard;
                    bool found = Chainloader.PluginInfos.TryGetValue("botanic.pup.patch", out guard) && guard.Instance;
                    takeover = MobPolicyHandoff.Begin(requested, found ? guard.Instance.Config : null,
                        found ? guard.Instance.GetType().Assembly.GetType("BotanicPupPatch.FgnCombatOwnership", false) : null,
                        networkPlugin.Instance.GetType().Assembly, networkPlugin.Metadata.Version);
                }
                catch (Exception error)
                { allowed = false; Logger.LogWarning("TooManyLags mob takeover unavailable. " + error.GetBaseException().Message); }
                FgnCombatOwnership.Install(Config, networkPlugin.Instance.GetType().Assembly, networkPlugin.Metadata.Version, allowed && !playerSimulation);
                FgnCombatStatus.Register();
                if (!String.IsNullOrEmpty(FgnCombatOwnership.StartupError)) Logger.LogWarning("TooManyLags AI ownership inactive: " + FgnCombatOwnership.StartupError);
                bool useMobs = allowed && (playerSimulation || FgnCombatOwnership.Enabled);
                bool playerMobsReady = false;
                if (playerSimulation && useMobs) FgnSessionSettings.CreatureBalancing(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config, false);
                try
                {
                    PlayerSimulation.Plugin.Initialize(Config, networkPlugin.Instance.GetType().Assembly, playerSimulation, useMobs);
                    playerMobsReady = playerSimulation && useMobs && PlayerSimulation.Plugin.Local != null && PlayerSimulation.Plugin.Local.MobsEnabled;
                }
                catch (Exception error)
                {
                    PlayerSimulation.Plugin.Stop();
                    if (playerSimulation && useMobs) FgnSessionSettings.CreatureBalancing(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config, true);
                    Logger.LogWarning("Player-simulation ownership mode inactive; native ownership retained. " + error.GetBaseException());
                }
                if (takeover != null && (FgnCombatOwnership.Enabled || playerMobsReady)) takeover.Commit();
            }
            catch (Exception error)
            { FgnCombatOwnership.Stop(); PlayerSimulation.Plugin.Stop(); Logger.LogWarning("TooManyLags optional ownership mode inactive: " + error.GetBaseException()); }
            finally
            {
                if (takeover != null)
                    try { takeover.Dispose(); }
                    catch (Exception error) { Logger.LogWarning("Previous mob ownership restore failed; native ownership retained. " + error.GetBaseException()); }
            }
        }
        private void Update() { if (enabledMod) PlayerSimulation.Plugin.Poll(); }
        internal static void SampleCamera() { if (ConfigConsole.CameraConfigured && instance && instance.camera != null) instance.camera.Sample(Time.unscaledTime); }
        internal static void ApplyCameraInterval(float interval) { if (instance && instance.camera != null) instance.camera.Configure(interval); }
        internal static float CameraInterval()
        {
            float interval = instance ? instance.ConfigValue<float>("08 - Camera Heading", "CameraHeadingCheckIntervalSeconds") : .05f;
            return Single.IsNaN(interval) || Single.IsInfinity(interval) ? .05f : Math.Max(.05f, Math.Min(5f, interval));
        }
        internal static void InvalidateCamera() { if (instance && instance.camera != null) instance.camera.Reset(true); }
        internal static void Warn(string message) { if (Diagnostics && instance) instance.Logger.LogWarning(message); }
        internal static void Report(string message) { if (instance) instance.Logger.LogWarning(message); }
        internal static string ConfigCommand(string command, bool server)
        {
            if (!instance || instance.commands == null) return "TooManyLags live configuration is unavailable on this installation.";
            try { return instance.commands.Execute(command, server); }
            catch (Exception error) { return "Config request failed: " + error.GetBaseException().Message; }
        }
        private T ConfigValue<T>(string section, string key)
        {
            ConfigEntry<T> entry;
            if (!Config.TryGetEntry<T>(new ConfigDefinition(section, key), out entry)) throw new InvalidOperationException("Missing config entry: " + key);
            return entry.Value;
        }
        private void ApplyLiveSetting(string key)
        {
            PlayerSimulation.Settings options = PlayerSimulation.Plugin.Options;
            if (key == "EnableLogs")
            { logs = Diagnostics = ConfigValue<bool>("01 - General", key); if (options != null) options.Logs = logs; }
            else if (key == "CameraHeadingCheckIntervalSeconds")
            { ConfigConsole.BroadcastCamera(ConfigValue<float>("08 - Camera Heading", key)); }
            else if (key == IntervalConfig.Key)
            {
                float interval = ConfigValue<float>("04 - Grid Settings", key);
                if (Controller != null) Controller.ConfigureInterval(interval);
                if (options != null) options.Interval = interval;
                if (PlayerSimulation.Plugin.Controller != null) PlayerSimulation.Plugin.Controller.ConfigureInterval(interval);
            }
            else if (key == "ServerFallbackPingMs")
            { if (options != null) options.HighPing = ConfigValue<int>("09 - AI Mob Ownership", key); }
            else if (key == "ServerFallbackRadius")
            { if (options != null) options.MobRadius = ConfigValue<float>("09 - AI Mob Ownership", key); }
            else if (key == "ServerOverlapCooldownSeconds")
            { if (Controller != null) Controller.ConfigureOverlapCooldown(ConfigValue<float>("05 - Directional Grid Mode", key)); }
            else
            {
                float full = ConfigValue<float>("06 - Full Grid Mode", "TransitionDelaySeconds");
                float inner = ConfigValue<float>("05 - Directional Grid Mode", "InnerTransitionDelaySeconds");
                float forward = ConfigValue<float>("05 - Directional Grid Mode", "ForwardTransitionDelaySeconds");
                float outer = ConfigValue<float>("05 - Directional Grid Mode", "OuterTransitionDelaySeconds");
                if (Controller != null) Controller.ConfigureDelays(full, inner, forward, outer);
                if (options != null) { options.Full = full; options.Inner = inner; options.Forward = forward; options.Outer = outer; }
                if (PlayerSimulation.Plugin.Controller != null) PlayerSimulation.Plugin.Controller.ConfigureDelays(full, inner, forward, outer);
            }
        }
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
                case Kinds.TamedAnimals: name = "Idle tamed animals; friendly magic summons excluded"; break;
                case Kinds.Containers: name = "Public chests and inventories"; break;
                case Kinds.ItemStands: name = "Item and armour stands"; break;
                case Kinds.Signs: name = "Signs"; break;
                case Kinds.Portals: name = "Portals"; break;
                case Kinds.Fermenters: name = "Fermenters"; break;
                case Kinds.Defenses: name = "Traps and turrets"; break;
                case Kinds.ShieldGenerators: name = "Shield generators"; break;
                case Kinds.Pickables: name = "Pickable objects and feasts; excludes loose item drops"; break;
                case Kinds.Offerings: name = "Offering altars"; break;
                case Kinds.Vehicles: name = "Idle carts and vehicle controls; FiresGhettoNetworking-simulated ships excluded"; break;
                default: name = "Other compatible modded interactables"; break;
            }
            return (kind == Kinds.Doors ? "All categories below: server only; restart required. true: managed by TooManyLags; false: vanilla/FiresGhettoNetworking. Open inventories, refilling, riding, following and other active use remain protected.\n" : "") + name + ".";
        }
        private void RuntimeFault(Exception error) { if (logs) Logger.LogWarning("TooManyLags grid inactive; native ownership retained. " + error.GetBaseException()); }
        private void RequestFault(Exception error) { if (logs) Logger.LogWarning("Native object request failed; not replayed. " + error.GetBaseException()); }
        private void Cleanup() { if (harmony != null) harmony.UnpatchSelf(); }
        private void OnDestroy()
        {
            ConfigConsole.Reset(); if (configHarmony != null) configHarmony.UnpatchSelf(); commands = null;
            FgnCombatOwnership.Stop(); FgnCombatStatus.Reset();
            PlayerSimulation.Plugin.Stop();
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
