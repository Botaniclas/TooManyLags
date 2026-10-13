using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("1.0.24.0")]

namespace TooManyLags
{
    [BepInPlugin(Id, "TooManyLags - FiresGhettoNetworking Addon", "1.0.24")]
    [BepInDependency(FgnId)]
    [BepInIncompatibility("botanic.server.buildings")]
    [BepInIncompatibility("botanic.server.authority")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "botanic.toomanylags.fgnaddon", Version = "1.0.24", FgnId = "com.Fire.FiresGhettoNetworkMod", PupId = "com.pup.instancecombiner.safe";
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
        private bool gridProtocol;
        private bool configuredAI;
        private bool sessionStarted;
        private IDisposable clientOverrides;
        private MobPolicyHandoff clientTakeover;
        private void Awake()
        {
            instance = this;
            bool enabled = Config.Bind("01 - General", "Enabled", true, "Server only; restart required. Enable TooManyLags for this server session. Clients follow the server; their local value is ignored.").Value;
            bool enableAI = Config.Bind("09 - AI Mob Ownership", "EnableAIOwnership", true,
                "Server only; restart required. Enable target-based Hostile Mob ownership. Clients follow the server; their local value is ignored. Attacks block transfers until attack end.").Value;
            bool noSimulation = Config.Bind("01 - General", "NoServerSideSimulation", false,
                "Server only; restart required. Recommended for dedicated servers with a weak or already overloaded CPU. true: disable FiresGhettoNetworking Server-Side Simulation and keep simulation on players; shared fields prefer the player with lower latency. Functionality is limited: no server ownership, and server-owned building/default/refill options are ignored. false: full Server-Side Simulation mode.").Value;
            logs = Config.Bind("01 - General", "EnableLogs", false, "Local to this server/client. Live console: tml_config EnableLogs true/false. Enable extra diagnostic messages. Startup failures are always reported.").Value;
            Diagnostics = logs;
            bool buildings = Config.Bind("02 - Static Buildings", "ServerOwnedStaticBuildings", false, "Server only; restart required. Keep eligible non-interactive building pieces server-owned. Independent of the interactive grid. Ignored with NoServerSideSimulation=true.").Value;
            bool defaultInteractives = Config.Bind("03 - Default Ownership and Overrides", "ServerOwnedInteractivesByDefault", false,
                "Server only; restart required. true: server default outside player coverage, with grid transfers nearby. false: vanilla ownership when players are alone or far apart. KeepOwnershipOnCellExit preserves ownership after a field is vacated. Ignored with NoServerSideSimulation=true.").Value;
            bool refillStations = Config.Bind("03 - Default Ownership and Overrides", "ServerOwnedRefillStations", false,
                "Server only; restart required. Keep enabled refill stations server-owned, even inside player fields: smelters, cooking/fire stations, fermenters, shield generators and turrets. false: grid rules with 10 seconds of protection after observed refilling. Ignored with NoServerSideSimulation=true.").Value;
            float cellSize = Config.Bind("04 - Grid Settings", "GridCellSizeMeters", 4f, new ConfigDescription(
                "Server only; restart required. Cell size in metres: 4, 6.4, 8 or 16. 6.4 divides each vanilla zone into 10x10 cells. 4 metres requires Rounded5x5/Rounded7x7. Does not change vanilla/PUP loading distances.",
                new AcceptableValueList<float>(4f, 6.4f, 8f, 16f))).Value;
            ConfigEntry<string> patternConfig = Config.Bind("04 - Grid Settings", "GridCoveragePattern", "Rounded7x7", new ConfigDescription(
                "Server only; restart required. Base shape: Square3x3 is square; Rounded5x5/Rounded7x7 are rounded. With KeepOwnershipOnCellExit=true, directional mode omits central and far rear fields; false keeps the complete shape. Overlaps use the server, or a lower-latency player with NoServerSideSimulation=true.",
                new AcceptableValueList<string>("Rounded5x5", "Square3x3", "Rounded7x7")));
            string patternName = patternConfig.Value;
            if (cellSize == 4 && patternName == "Square3x3")
            {
                patternName = "Rounded5x5";
                patternConfig.Value = patternName;
            }
            GridCoveragePattern pattern = patternName == "Rounded7x7" ? GridCoveragePattern.Rounded7x7 : patternName == "Rounded5x5" ? GridCoveragePattern.Rounded5x5 : GridCoveragePattern.Square3x3;
            bool directional = Config.Bind("05 - Directional Grid Mode", "DirectionalGridMode", true,
                "Server only; restart required. true: camera-directed grid with the timers below and the overlap angle in section 08. false: full grid from section 06. Missing camera data uses character heading. Stationary players still count.").Value;
            float innerDelay = Config.Bind("05 - Directional Grid Mode", "InnerTransitionDelaySeconds", 0f, new ConfigDescription(
                "Server only; live console supported. Red: six inner fields with KeepOwnershipOnCellExit=true; a full 3x3 centre with false. Delay in seconds for new requests; 0 allowed. Handoff protection remains active.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float forwardDelay = Config.Bind("05 - Directional Grid Mode", "ForwardTransitionDelaySeconds", .2f, new ConfigDescription(
                "Server only; live console supported. Orange: one extra three-cell row ahead of red. Active only with 4-metre cells and Rounded5x5/Rounded7x7. Delay in seconds for new requests; 0 allowed. The farthest 7x7 row stays yellow.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float outerDelay = Config.Bind("05 - Directional Grid Mode", "OuterTransitionDelaySeconds", 3f, new ConfigDescription(
                "Server only; live console supported. Yellow: remaining active fields. Delay in seconds for new requests. With KeepOwnershipOnCellExit=true, white rear fields make no request; false covers the full shape. Overlaps use the earliest still-valid request deadline.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float overlapCooldown = Config.Bind("05 - Directional Grid Mode", "ServerOverlapCooldownSeconds", 1f, new ConfigDescription(
                "Server only; live console supported. After a directional-grid overlap assigns a field to the server, ignore new grid ownership decisions for this many seconds. Expiry alone causes no transfer; the next movement/heading update can request the field again. 0 disables. Empty fields/native release and usage protection remain unchanged. Ignored without Server-Side Simulation. New cooldowns use the updated value.", new AcceptableValueRange<float>(0f, 30f))).Value;
            float delay = Config.Bind("06 - Full Grid Mode", "TransitionDelaySeconds", 2f, new ConfigDescription(
                "Server only; live console supported. Only with DirectionalGridMode=false: one delay in seconds for new full-grid requests. Active use and safe handoffs may require more time.", new AcceptableValueRange<float>(1f, 30f))).Value;
            if (Single.IsNaN(delay) || Single.IsInfinity(delay)) delay = 2f;
            float interval = Config.Bind("04 - Grid Settings", IntervalConfig.Key, IntervalConfig.Default, new ConfigDescription(
                "Server only; live console supported. Check interval in seconds for movement and fallback heading. Received camera-coverage changes request an immediate pass. Only changed coverage requests fields; existing timers/handoffs also progress while standing.", new AcceptableValueRange<float>(.05f, 5f))).Value;
            if (Single.IsNaN(interval) || Single.IsInfinity(interval)) interval = IntervalConfig.Default;
            bool keepOnExit = Config.Bind("04 - Grid Settings", "KeepOwnershipOnCellExit", true,
                "Server only; restart required. true: keep ownership after everyone leaves. In directional mode, when one player remains, only red fields transfer automatically; orange/yellow wait for that player's next cell/heading change. Completed player fields are cached until relevant changes/unloading. false: complete directional footprint with a red 3x3 centre, normal departure transfers and no cache. Native release and pinned stations still apply.").Value;
            bool interactionProtection = Config.Bind("07 - Experimental Interaction Protection", "EnableExperimentalInteractionProtection", false,
                "Server only; restart required. Experimental extra protection introduced in 1.0.15: identified source handoffs, recipient-readiness checks and client relays during ownership changes. false uses the earlier handoff path. Existing inventory, refill and active-use protection stays enabled. Client value is ignored. Ignored with NoServerSideSimulation=true.").Value;
            Kinds kinds = Kinds.None;
            foreach (Kinds kind in Enum.GetValues(typeof(Kinds)))
                if (kind != Kinds.None && kind != Kinds.Incinerators && Config.Bind("10 - Object Categories", kind.ToString(), true,
                    ObjectDescription(kind)).Value) kinds |= kind;
            float cameraInterval = Config.Bind("08 - Camera Heading", "CameraHeadingCheckIntervalSeconds", .05f, new ConfigDescription(
                "Server only; live console supported. Camera check interval in seconds, sent to all clients. Client value is ignored. 0.05 allows up to 20 checks per second; only coverage changes are sent. Server grid timing is in section 04.", new AcceptableValueRange<float>(.05f, 5f))).Value;
            float cameraOverlap = Config.Bind("08 - Camera Heading", "CameraDirectionOverlapDegrees", 30f, new ConfigDescription(
                "Server only; live console supported. Base angular overlap of adjacent directions, sent to clients. 0: one base direction; 30: default; 45: two. Rotation prediction can add one direction. Character-heading fallback uses the same angle.", new AcceptableValueRange<float>(0f, 45f))).Value;
            float cameraPrediction = Config.Bind("08 - Camera Heading", "CameraRotationPredictionSeconds", .1f, new ConfigDescription(
                "Server only; live console supported. Predict camera rotation this many seconds ahead. Adds at most one adjacent direction; new fields use the inner/red delay. Keeps current coverage and handoff protection. 0 disables. Requires updated clients; character fallback is unchanged.", new AcceptableValueRange<float>(0f, .5f))).Value;
            CameraHeading.Configure(cameraOverlap, cameraPrediction);
            // Configuration ends here.
            configuredAI = enableAI;
            if (!Chainloader.PluginInfos.TryGetValue(FgnId, out networkPlugin) || !networkPlugin.Instance)
            { enabledMod = false; Logger.LogWarning("TooManyLags inactive: FiresGhettoNetworking is not loaded."); return; }
            // Clients install only the startup/settings endpoint until the
            // authenticated dedicated server tells them which modules to use.
            if (!Application.isBatchMode || !enabled) return;
            enabledMod = true;
            playerSimulation = noSimulation;
            try
            {
                bool activated = FgnSessionSettings.Apply(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config,
                    Application.isBatchMode, enableAI, Chainloader.PluginInfos.ContainsKey("dev.ontrigger.vpo"), playerSimulation);
                if (activated) Logger.LogWarning("TooManyLags enabled FiresGhettoNetworking Server-Side Simulation for this session. This can increase server CPU usage; its saved config is unchanged.");
            }
            catch (Exception error)
            { enabledMod = false; Logger.LogWarning("TooManyLags inactive: required FGN session settings could not be applied. " + error.GetBaseException()); return; }
            if (!playerSimulation && interactionProtection)
            {
                try { GridHandoffs.Protocol.Start(networkPlugin.Instance.GetType().Assembly); gridProtocol = true; }
                catch (Exception error)
                { GridHandoffs.Protocol.Stop(); Logger.LogWarning("Interactive ownership inactive: grid handoff safeguards unavailable. " + error.GetBaseException()); }
            }
            if (playerSimulation) return;
            if (interactionProtection && !gridProtocol) return;
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
            if (networkPlugin == null || !networkPlugin.Instance) return;
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
            if (!enabledMod) return;
            InitializeOwnership(Config, configuredAI, false);
        }
        private void InitializeOwnership(ConfigFile runtimeConfig, bool requested, bool client)
        {
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
                FgnCombatOwnership.Install(runtimeConfig, networkPlugin.Instance.GetType().Assembly, networkPlugin.Metadata.Version, allowed && !playerSimulation);
                FgnCombatStatus.Register();
                if (!String.IsNullOrEmpty(FgnCombatOwnership.StartupError)) Logger.LogWarning("TooManyLags AI ownership inactive: " + FgnCombatOwnership.StartupError);
                bool useMobs = allowed && (playerSimulation || FgnCombatOwnership.Enabled);
                bool playerMobsReady = false;
                if (playerSimulation && useMobs) FgnSessionSettings.CreatureBalancing(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config, false);
                try
                {
                    PlayerSimulation.Plugin.Initialize(runtimeConfig, networkPlugin.Instance.GetType().Assembly, playerSimulation, useMobs);
                    playerMobsReady = playerSimulation && useMobs && PlayerSimulation.Plugin.Local != null && PlayerSimulation.Plugin.Local.MobsEnabled;
                }
                catch (Exception error)
                {
                    PlayerSimulation.Plugin.Stop();
                    if (playerSimulation && useMobs) FgnSessionSettings.CreatureBalancing(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config, true);
                    Logger.LogWarning("Player-simulation ownership mode inactive; native ownership retained. " + error.GetBaseException());
                }
                if (takeover != null && (FgnCombatOwnership.Enabled || playerMobsReady))
                {
                    if (client) { clientTakeover = takeover; takeover = null; }
                    else takeover.Commit();
                }
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
        private void Update()
        {
            if (!Application.isBatchMode) ServerSession.Poll();
            if (!enabledMod) return;
            if (camera != null && gridProtocol) GridHandoffs.Protocol.ClientHello();
            PlayerSimulation.Plugin.Poll();
        }
        internal static int SessionFlags()
        {
            if (!instance || !instance.enabledMod) return 0;
            bool mobs = FgnCombatOwnership.Enabled || (instance.playerSimulation && PlayerSimulation.Plugin.Local != null && PlayerSimulation.Plugin.Local.MobsEnabled);
            return mobs ? 3 : 1;
        }
        internal static void ApplyServerSession(bool enabled, bool mobs)
        {
            if (!instance || Application.isBatchMode || instance.sessionStarted) return;
            instance.sessionStarted = true;
            if (!enabled) return;
            Assembly fgn = instance.networkPlugin.Instance.GetType().Assembly;
            instance.clientOverrides = FgnSessionSettings.ApplyClient(fgn, instance.networkPlugin.Instance.Config, mobs);
            // A disposable runtime copy changes no saved client values.
            ConfigFile runtimeConfig = new ConfigFile(instance.Config.ConfigFilePath, false) { SaveOnConfigSet = false };
            runtimeConfig.Bind("09 - AI Mob Ownership", "EnableAIOwnership", true, "").Value = mobs;
            instance.enabledMod = true;
            try { GridHandoffs.Protocol.Start(fgn); instance.gridProtocol = true; }
            catch (Exception error)
            { GridHandoffs.Protocol.Stop(); Report("Interactive handoff support unavailable: " + error.GetBaseException().Message); }
            try
            {
                instance.camera = new CameraPublisher(CameraInterval());
                instance.harmony = new Harmony(Id + ".camera");
                instance.harmony.CreateClassProcessor(typeof(CameraSamplePatch)).Patch();
                instance.harmony.CreateClassProcessor(typeof(CameraLossPatch)).Patch();
            }
            catch (Exception error)
            { instance.camera = null; instance.Cleanup(); Report("Camera publication inactive; character heading remains available. " + error.GetBaseException().Message); }
            instance.InitializeOwnership(runtimeConfig, mobs, true);
            FgnCombatOwnership.ConnectCurrentServer();
            ConfigConsole.RefreshCamera();
        }
        internal static void ClearClientSession()
        {
            if (!instance || Application.isBatchMode || (!instance.sessionStarted && !instance.enabledMod)) return;
            instance.enabledMod = instance.sessionStarted = false;
            GridHandoffs.Protocol.Stop(); instance.gridProtocol = false;
            FgnCombatOwnership.Stop(); PlayerSimulation.Plugin.Stop();
            if (instance.camera != null) instance.camera.Reset(true); instance.camera = null;
            instance.Cleanup(); instance.harmony = null; Native = null;
            if (instance.clientOverrides != null) { instance.clientOverrides.Dispose(); instance.clientOverrides = null; }
            if (instance.clientTakeover != null)
            {
                MobPolicyHandoff restore = instance.clientTakeover; instance.clientTakeover = null;
                try { restore.Dispose(); } catch (Exception error) { Report("Previous mob policy restore failed: " + error.GetBaseException().Message); }
            }
        }
        internal static void SampleCamera() { if (ConfigConsole.CameraConfigured && instance && instance.camera != null) instance.camera.Sample(Time.unscaledTime); }
        internal static void ApplyCameraInterval(float interval) { if (instance && instance.camera != null) instance.camera.Configure(interval); }
        internal static void ApplyCameraSettings(float interval, float overlap, float prediction = 0f)
        { CameraHeading.Configure(overlap, prediction); ApplyCameraInterval(interval); }
        internal static float CameraPredictionSeconds()
        {
            float seconds = instance ? instance.ConfigValue<float>("08 - Camera Heading", "CameraRotationPredictionSeconds") : .1f;
            return Single.IsNaN(seconds) || Single.IsInfinity(seconds) ? .1f : Math.Max(0f, Math.Min(.5f, seconds));
        }
        internal static float CameraOverlap()
        {
            float overlap = instance ? instance.ConfigValue<float>("08 - Camera Heading", "CameraDirectionOverlapDegrees") : 30f;
            return Single.IsNaN(overlap) || Single.IsInfinity(overlap) ? 30f : Math.Max(0f, Math.Min(45f, overlap));
        }
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
            else if (key == "CameraDirectionOverlapDegrees" || key == "CameraRotationPredictionSeconds")
            { CameraHeading.Configure(CameraOverlap(), CameraPredictionSeconds()); ConfigConsole.BroadcastCamera(CameraInterval()); }
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
            GridHandoffs.Protocol.Stop();
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

