using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("2.0.0.0")]

namespace TooManyLags
{
    [BepInPlugin(Id, "TooManyLags - FiresGhettoNetworking Addon", "2.0.0")]
    [BepInDependency(FgnId)]
    [BepInDependency(CommunityPatchSceneCompatibility.Id, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(PupId, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("botanic.pup.patch", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility("botanic.server.buildings")]
    [BepInIncompatibility("botanic.server.authority")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "botanic.toomanylags.fgnaddon", Version = "2.0.0", FgnId = "com.Fire.FiresGhettoNetworkMod", PupId = "com.pup.instancecombiner.safe";
        internal static AuthorityController Controller;
        internal static bool Diagnostics;
        private static Plugin instance;
        private Harmony harmony;
        private Harmony configHarmony;
        private Harmony layoutHarmony;
        private ConfigCommands commands;
        private bool logs;
        private bool enabledMod;
        private CameraPublisher camera;
        private PluginInfo networkPlugin;
        private bool playerSimulation;
        private bool autoConfigureFgn;
        private bool gridProtocol;
        private bool configuredAI;
        private bool mobVisualBridge;
        private bool sessionStarted;
        private IDisposable clientOverrides;
        private IDisposable communityPatchCompatibility;
        private MobPolicyHandoff clientTakeover;
        private PupCompatibility.Plugin pupCompatibility;
        private void PrepareConfiguration()
        {
            ConfigLayout.Prepare(Config, Version);
            layoutHarmony = new Harmony(Id + ".configlayout");
            layoutHarmony.Patch(AccessTools.Method(typeof(ConfigFile), "Save"),
                prefix: new HarmonyMethod(typeof(ConfigLayout), "BeforeSave"));
        }
        private void Awake()
        {
            PrepareConfiguration();
            instance = this;
            bool automaticFgn = Config.Bind("00 - General", "AutoConfigureFiresGhettoNetworking", true,
                "Server only; restart required. true (default): automatically apply the required FiresGhettoNetworking session settings and start/stop its Server-Side Simulation for an easy quick start. false: no automatic setup; configure FiresGhettoNetworking on the server and clients yourself for the TML features you use. TML ownership patches and handoff safeguards remain active. Clients follow this server setting.").Value;
            bool defaultInteractives = Config.Bind("01 - Simulation Settings", "ServerOwnedInteractivesByDefault", false,
                "Server only; restart required. Simulation settings and exit retention only distribute CPU load between server and clients. They do not affect perceived latency. true: server default for managed interactions/idle tamed animals; false: vanilla default. Requires Server-Side Simulation.").Value;
            bool buildings = Config.Bind("01 - Simulation Settings", "ServerOwnedStaticBuildingsByDefault", false,
                "Server only; restart required. true: server default for eligible non-interactive buildings and fixed world objects; false: vanilla default. PhysicsObjects separately controls movable world props and sources of physical successors. UseGridForStaticBuildings lets nearby clients share static simulation. Requires Server-Side Simulation.").Value;
            bool staticGrid = Config.Bind("01 - Simulation Settings", "UseGridForStaticBuildings", false,
                "Server only; restart required. Include eligible non-interactive buildings and fixed world objects in the grid, independently of ServerOwnedStaticBuildingsByDefault. That setting selects the default outside player fields; empty fields follow KeepOwnershipOnCellExit. PhysicsObjects uses interactive rules instead. PUP FPS clusters stay excluded; PUP FPS is optional. Requires Server-Side Simulation.").Value;
            bool enabled = Config.Bind("00 - General", "Enabled", true, "Server only; restart required. Enable TooManyLags for this server session. Clients follow the server; their local value is ignored. Handoff safeguards are always active with Server-Side Simulation. Later sections contain tuned defaults; adjust only when needed.").Value;
            bool enableAI = Config.Bind("03 - AI Mob Ownership", "EnableAIOwnership", true,
                "Server only; restart required. Enable target-based Hostile Mob ownership. Clients follow the server; their local value is ignored. Attacks block transfers until attack end.").Value;
            bool visualHandoff = Config.Bind("03 - AI Mob Ownership", "EnableMobHandoffVisualBridge", true,
                "Server only; restart required. Continue native enemy simulation locally during ownership handoffs, replay the recording more slowly, then blend into the new owner's simulation at matching progress. Nearby compatible observers can share the recording. Hitboxes, nameplates and attached effects follow the displayed enemy. Preview attacks are visual only. Timing adapts to latency and owner simulation speed; late updates can require waiting. Update server and all clients together. false: native presentation.").Value;
            bool noSimulation = Config.Bind("01 - Simulation Settings", "NoServerSideSimulation", false,
                "Server only; restart required. Recommended for dedicated servers with a weak or already overloaded CPU. true: disable FiresGhettoNetworking Server-Side Simulation and keep simulation on players; shared fields prefer the player with lower latency. Functionality is limited: no server ownership, and server-owned building/default/refill options are ignored. false: full Server-Side Simulation mode.").Value;
            logs = Config.Bind("09 - Logs", "EnableLogs", false, "Local to this server/client. Live console: tml_config EnableLogs true/false. Enables on-demand status commands, without continuous informational logging. Off skips optional diagnostic work; real errors and warnings are always reported.").Value;
            Diagnostics = logs;
            bool pupCompatibilityEnabled = Config.Bind("08 - PUP FPS Compatibility", "EnablePupCompatibility", true,
                "Local to this server/client; restart required. Built-in PUP FPS collision, building-stability and FiresGhettoNetworking handoff safeguards. Automatically inactive without the required mods or when a separate BotanicPupPatch supplies the guards. Inactive mode installs no guard hooks and does no runtime work. AI ownership remains controlled by section 03.").Value;
            bool refillStations = Config.Bind("02 - Default Ownership and Overrides", "ServerOwnedRefillStations", false,
                "Server only; restart required. Keep enabled refill stations server-owned, even inside player fields: smelters, cooking/fire stations, fermenters, shield generators and turrets. false: grid rules with 1 second of protection, renewed by each observed refill. Ignored with NoServerSideSimulation=true.").Value;
            bool defaultMobs = Config.Bind("02 - Default Ownership and Overrides", "ServerOwnedMobsByDefault", false,
                "Server only; restart required. Give idle tamed animals a server default outside player fields, even when the interactive default is off. Fixed mob spawners also default to the server and stay outside the grid; ambient spawning stays native. Hostile Mobs without a player target use guarded server handoffs (requires EnableAIOwnership); player targets keep normal target ownership. Friendly magic summons, bosses and active riding/following stay excluded. Requires Server-Side Simulation; can increase server CPU usage.").Value;
            float cellSize = Config.Bind("04 - Grid Settings", "GridCellSizeMeters", 4f, new ConfigDescription(
                "Server only; restart required. Defaults should work well. Larger grids are recommended for poor connections: they give handoffs more lead time, covering more objects. Cell size in metres: 4, 6.4, 8 or 16; 4 requires a rounded grid. Does not change vanilla/PUP loading distances.",
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
                "Server only; restart required. true: camera-directed grid with the timers below and the overlap angle in section 07. false: full grid from section 06. Missing camera data uses character heading. Stationary players still count.").Value;
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
            float interval = Config.Bind("04 - Grid Settings", IntervalConfig.Key, .1f, new ConfigDescription(
                "Server only; live console supported. Check interval in seconds for movement and fallback heading. Received camera-coverage changes request an immediate pass. Only changed coverage requests fields; existing timers/handoffs also progress while standing.", new AcceptableValueRange<float>(.05f, 5f))).Value;
            if (Single.IsNaN(interval) || Single.IsInfinity(interval)) interval = .1f;
            ConfigEntry<bool> retention = Config.Bind("01 - Simulation Settings", "KeepOwnershipOnCellExit", true,
                "Server only; restart required. true: keep vacated fields, gradually sharing CPU load between clients/server. false: return to server defaults; requires Server-Side Simulation and an interactive/static server default. false also keeps the full directional footprint. Ownership transfers use bandwidth; false can substantially increase traffic through extra server returns and later player claims, especially while moving through dense areas. Native region release still applies.");
            bool keepOnExit = retention.Value;
            Kinds kinds = Kinds.None;
            foreach (Kinds kind in Enum.GetValues(typeof(Kinds)))
                if (kind != Kinds.None && kind != Kinds.StaticBuildings && Config.Bind("10 - Object Categories", kind.ToString(), true,
                    ObjectDescription(kind)).Value) kinds |= kind;
            float cameraInterval = Config.Bind("07 - Camera Heading", "CameraHeadingCheckIntervalSeconds", .05f, new ConfigDescription(
                "Server only; live console supported. Camera check interval in seconds, sent to all clients. Client value is ignored. 0.05 allows up to 20 checks per second; only coverage changes are sent. Server grid timing is in section 04.", new AcceptableValueRange<float>(.05f, 5f))).Value;
            float cameraOverlap = Config.Bind("07 - Camera Heading", "CameraDirectionOverlapDegrees", 30f, new ConfigDescription(
                "Server only; live console supported. Base angular overlap of adjacent directions, sent to clients. 0: one base direction; 30: default; 45: two. Rotation prediction can add one direction. Character-heading fallback uses the same angle.", new AcceptableValueRange<float>(0f, 45f))).Value;
            float cameraPrediction = Config.Bind("07 - Camera Heading", "CameraRotationPredictionSeconds", .1f, new ConfigDescription(
                "Server only; live console supported. Predict camera rotation this many seconds ahead. Adds at most one adjacent direction; new fields use the inner/red delay. Keeps current coverage and handoff protection. 0 disables. Requires updated clients; character fallback is unchanged.", new AcceptableValueRange<float>(0f, .5f))).Value;
            CameraHeading.Configure(cameraOverlap, cameraPrediction);
            ConfigLayout.Finish(Config);
            // Configuration ends here.
            mobVisualBridge = visualHandoff;
            autoConfigureFgn = automaticFgn;
            PluginInfo pupGuard, pupPlugin, pupNetwork;
            bool externalPupGuard = (Chainloader.PluginInfos.TryGetValue("botanic.pup.patch", out pupGuard) && pupGuard.Instance) ||
                (Chainloader.PluginInfos.TryGetValue("niclas.pup.cartguard", out pupGuard) && pupGuard.Instance);
            pupCompatibility = PupCompatibility.Plugin.Start(pupCompatibilityEnabled,
                Chainloader.PluginInfos.TryGetValue(PupId, out pupPlugin) ? pupPlugin.Instance : null,
                Chainloader.PluginInfos.TryGetValue(FgnId, out pupNetwork) && pupNetwork.Instance ? pupNetwork.Instance.GetType().Assembly : null, externalPupGuard);
            configuredAI = enableAI;
            if (!Chainloader.PluginInfos.TryGetValue(FgnId, out networkPlugin) || !networkPlugin.Instance)
            { enabledMod = false; Logger.LogWarning("TooManyLags inactive: FiresGhettoNetworking is not loaded."); return; }
            // Clients install only the startup/settings endpoint until the
            // authenticated dedicated server tells them which modules to use.
            if (!Application.isBatchMode || !enabled) return;
            MobCollisionReadiness.Start(pupPlugin == null ? null : pupPlugin.Instance, pupCompatibilityEnabled);
            enabledMod = true;
            playerSimulation = noSimulation;
            if (!keepOnExit && !ConfigCommands.CanReturnOnExit(!playerSimulation, defaultInteractives, buildings))
            {
                keepOnExit = true;
                retention.Value = true;
                Logger.LogWarning("KeepOwnershipOnCellExit forced to true: cell returns require Server-Side Simulation and server-default ownership for interactive or static objects.");
            }
            try
            {
                bool sceneConflict = Chainloader.PluginInfos.ContainsKey("dev.ontrigger.vpo");
                PluginInfo communityPatch;
                if (autoConfigureFgn && !playerSimulation && !sceneConflict &&
                    Chainloader.PluginInfos.TryGetValue(CommunityPatchSceneCompatibility.Id, out communityPatch) && communityPatch.Instance)
                    communityPatchCompatibility = CommunityPatchSceneCompatibility.Start(networkPlugin.Instance.GetType().Assembly, communityPatch.Instance.GetType().Assembly);
                bool activated = FgnSessionSettings.Apply(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config,
                    Application.isBatchMode, enableAI, sceneConflict, playerSimulation, autoConfigureFgn);
                if (communityPatchCompatibility != null) Logger.LogWarning("TooManyLags coordinated CommunityPatch scene scheduling for this server session. FiresGhettoNetworking manages player areas; CommunityPatch's unload crash guard and unrelated fixes retain their settings.");
                if (activated) Logger.LogWarning("TooManyLags enabled FiresGhettoNetworking Server-Side Simulation for this session. This can increase server CPU usage; its saved config is unchanged.");
            }
            catch (Exception error)
            {
                if (communityPatchCompatibility != null) { communityPatchCompatibility.Dispose(); communityPatchCompatibility = null; }
                enabledMod = false; Logger.LogWarning("TooManyLags inactive: required FGN session settings could not be applied. " + error.GetBaseException()); return;
            }
            if (!playerSimulation)
            {
                try { GridHandoffs.Protocol.Start(networkPlugin.Instance.GetType().Assembly); gridProtocol = true; }
                catch (Exception error)
                { GridHandoffs.Protocol.Stop(); Logger.LogWarning("Interactive ownership inactive: grid handoff safeguards unavailable. " + error.GetBaseException()); }
            }
            if (playerSimulation) return;
            if (!gridProtocol) return;
            if (!buildings && !staticGrid && kinds == Kinds.None) return;
            try
            {
                PluginInfo fgn = networkPlugin;
                CompatibilityBridge bridge = new CompatibilityBridge(fgn.Instance);
                if (!bridge.Configured()) throw new NotSupportedException("Required FGN session settings are unavailable. " + bridge.State());
                // Bind required client protocol signatures before installing
                // any hooks; changed APIs are reported even when logs are off.
                new FgnOwnerHandoff(bridge.Assembly, 1L);
                Controller = new AuthorityController(kinds, delay, bridge, RuntimeFault, buildings, RequestFault, interval, defaultInteractives, refillStations, cellSize, pattern, keepOnExit, directional, innerDelay, outerDelay, forwardDelay, overlapCooldown, defaultMobs, staticGrid);
                OwnershipTranspiler.Rewrite(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS")), AccessTools.Method(typeof(OwnershipPatch), "Persistent"));
                PickableServerPatch.Rewrite(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(Pickable), "RPC_Pick")));
                harmony = new Harmony(Id);
                foreach (Type type in typeof(Plugin).Assembly.GetTypes())
                    if (type.Namespace == typeof(Plugin).Namespace && type != typeof(PhysicsObjects.GainPatch) && type != typeof(ConfigConnectionPatch) && type != typeof(ConfigShutdownPatch) && type != typeof(ConfigReadyPatch) && Attribute.IsDefined(type, typeof(HarmonyPatch)))
                        harmony.CreateClassProcessor(type).Patch();
                FgnRpcIntegration.Install(harmony, fgn.Instance.GetType().Assembly);
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
        private static void StopCombatState()
        {
#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
            MobBridgeProtocol.Stop();
#endif
            MobSourceHandoff.Stop(); MobObserverHandoff.Stop();
            MobCombatState.Stop();
            MobFlightHandoff.CombatTransfer = null;
            MobFlightHandoff.CombatTransferred = null;
            PlayerSimulation.LocalAgent.CombatForward = null;
        }
        private void InitializeOwnership(ConfigFile runtimeConfig, bool requested, bool client)
        {
            MobFlightHandoff.Start(networkPlugin.Instance.GetType().Assembly);
            MobCombatState.Start(networkPlugin.Instance.GetType().Assembly);
#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
            if (mobVisualBridge) MobBridgeProtocol.Start();
#endif
            MobFlightHandoff.CombatTransfer = MobCombatState.BeforeTransfer;
            MobFlightHandoff.CombatTransferred = MobCombatState.CompleteTransfer;
            PlayerSimulation.LocalAgent.CombatForward = MobCombatState.HandlesLateDamage;
            try { TextEditing.Protection.Start(); }
            catch (Exception error) { Report("Text editing protection unavailable: " + error.GetBaseException().Message); }
            // All plugin Awakes have completed: the optional combat module can
            // release its hooks before ours are installed, independent of load order.
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
                if (FgnCombatOwnership.Enabled)
                {
                    MobSourceHandoff.CommitLocal = FgnCombatOwnership.SourceCommit;
                    MobSourceHandoff.ValidateLocal = FgnCombatOwnership.SourceLocalReady;
                    MobSourceHandoff.ResolveDestination = FgnCombatOwnership.SourceDestination;
                    MobSourceHandoff.AcceptedServer = FgnCombatOwnership.SourceAccepted;
                    MobSourceHandoff.PublishFinal = MobNativePublication.Send;
                    MobSourceHandoff.RoundtripSeconds = null;
                    if (mobVisualBridge) MobSourceHandoff.RoundtripSeconds = delegate(ZNetPeer peer) {
#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
                        return peer == null ? 0f : MobBridgeLatency.Measure(peer, FgnCombatOwnership.Fgn);
#else
                        return peer == null || FgnCombatOwnership.Fgn == null ? 0f : FgnCombatOwnership.Fgn.Rtt(peer) / 1000f;
#endif
                    };
                    MobHandoffVisual.LatencyWindow = MobSourceHandoff.PresentationWindow;
                    MobSourceHandoff.PresentationTiming = MobHandoffVisual.TargetTiming;
                    MobSourceHandoff.Start();
                }
                MobObserverHandoff.Start(mobVisualBridge);
                FgnCombatStatus.Register();
#if TML_DEBUG_SYSTEM
                FgnSoloCombatTest.Register();
#endif
                if (!String.IsNullOrEmpty(FgnCombatOwnership.StartupError)) Logger.LogWarning("TooManyLags AI ownership inactive: " + FgnCombatOwnership.StartupError);
                bool useMobs = allowed && (playerSimulation || FgnCombatOwnership.Enabled);
                bool playerMobsReady = false;
                if (playerSimulation && useMobs) FgnSessionSettings.CreatureBalancing(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config, false, autoConfigureFgn);
                try
                {
                    PlayerSimulation.Plugin.Initialize(runtimeConfig, networkPlugin.Instance.GetType().Assembly, playerSimulation, useMobs);
                    playerMobsReady = playerSimulation && useMobs && PlayerSimulation.Plugin.Local != null && PlayerSimulation.Plugin.Local.MobsEnabled;
                }
                catch (Exception error)
                {
                    PlayerSimulation.Plugin.Stop();
                    if (playerSimulation && useMobs) FgnSessionSettings.CreatureBalancing(networkPlugin.Instance.GetType().Assembly, networkPlugin.Instance.Config, true, autoConfigureFgn);
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
#if TML_DEBUG_SYSTEM
            SoloNetworkLatency.Poll();
#endif
            if (!Application.isBatchMode) ServerSession.Poll();
            if (!enabledMod) return;
            if (camera != null && gridProtocol) GridHandoffs.Protocol.ClientHello();
            PlayerSimulation.Plugin.Poll();
            TextEditing.Protection.Poll();
#if TML_DEBUG_SYSTEM
            FgnSoloCombatTest.Poll();
#endif
        }
        private void LateUpdate()
        {
            if (gridProtocol) GridHandoffs.Protocol.FlushDeferred();
#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
            MobBridgeWarmup.Pump();
            MobBridgeVisual.Tick();
            MobBridgeProtocol.PollRate();
            if (MobBridgeProtocol.HasActive) MobBridgeProtocol.Maintain();
            MobBridgeCost.Flush();
#endif
            MobHandoffVisual.Tick(); MobObserverHandoff.Tick();
        }
        [System.Diagnostics.Conditional("TML_BRIDGE_DIAGNOSTICS")]
        internal static void TestLog(string line)
        {
#if TML_BRIDGE_DIAGNOSTICS
            if (instance) instance.Logger.LogInfo("MOB TEST: " + line);
#endif
        }
        internal static int SessionFlags()
        {
            if (!instance || !instance.enabledMod) return 0;
            bool mobs = FgnCombatOwnership.Enabled || (instance.playerSimulation && PlayerSimulation.Plugin.Local != null && PlayerSimulation.Plugin.Local.MobsEnabled);
            return (mobs ? 3 : 1) | (instance.autoConfigureFgn ? 0 : 4);
        }
        internal static int VisualSessionFlags()
        { return SessionFlags() | (instance && instance.enabledMod && instance.mobVisualBridge ? 8 : 0); }
        internal static void ApplyVisualSession(bool enabled)
        {
            if (instance && instance.enabledMod && instance.networkPlugin != null && instance.networkPlugin.Instance)
            {
#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
                if (enabled && MobBridgeProtocol.Prepare())
                { MobHandoffVisual.Stop(); if (!MobBridgeProtocol.Started) MobBridgeProtocol.Start(); }
                else
                { MobBridgeProtocol.Stop(); MobHandoffVisual.Start(instance.networkPlugin.Instance.GetType().Assembly, enabled); }
#else
                MobHandoffVisual.Start(instance.networkPlugin.Instance.GetType().Assembly, enabled);
#endif
                MobObserverHandoff.Start(enabled);
            }
        }
        internal static void ApplyServerSession(bool enabled, bool mobs, bool automaticFgn = true)
        {
            if (!instance || Application.isBatchMode || instance.sessionStarted) return;
            instance.sessionStarted = true;
            if (!enabled) return;
            instance.autoConfigureFgn = automaticFgn;
            Assembly fgn = instance.networkPlugin.Instance.GetType().Assembly;
            instance.clientOverrides = FgnSessionSettings.ApplyClient(fgn, instance.networkPlugin.Instance.Config, mobs, automaticFgn);
            // A disposable runtime copy changes no saved client values.
            ConfigFile runtimeConfig = new ConfigFile(instance.Config.ConfigFilePath, false) { SaveOnConfigSet = false };
            runtimeConfig.Bind("03 - AI Mob Ownership", "EnableAIOwnership", true, "").Value = mobs;
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
            MobFlightHandoff.Stop();
            StopCombatState();
            MobHandoffVisual.Stop();
            TextEditing.Protection.Stop();
            GridHandoffs.Protocol.Stop(); instance.gridProtocol = false;
            FgnCombatOwnership.Stop(); PlayerSimulation.Plugin.Stop();
            if (instance.camera != null) instance.camera.Reset(true); instance.camera = null;
            instance.Cleanup(); instance.harmony = null;
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
            float seconds = instance ? instance.ConfigValue<float>("07 - Camera Heading", "CameraRotationPredictionSeconds") : .1f;
            return Single.IsNaN(seconds) || Single.IsInfinity(seconds) ? .1f : Math.Max(0f, Math.Min(.5f, seconds));
        }
        internal static float CameraOverlap()
        {
            float overlap = instance ? instance.ConfigValue<float>("07 - Camera Heading", "CameraDirectionOverlapDegrees") : 30f;
            return Single.IsNaN(overlap) || Single.IsInfinity(overlap) ? 30f : Math.Max(0f, Math.Min(45f, overlap));
        }
        internal static float CameraInterval()
        {
            float interval = instance ? instance.ConfigValue<float>("07 - Camera Heading", "CameraHeadingCheckIntervalSeconds") : .05f;
            return Single.IsNaN(interval) || Single.IsInfinity(interval) ? .05f : Math.Max(.05f, Math.Min(5f, interval));
        }
        internal static void InvalidateCamera() { if (instance && instance.camera != null) instance.camera.Reset(true); }
        internal static void Warn(string message) { if (instance) instance.Logger.LogWarning(message); }
        internal static void Report(string message) { if (instance) instance.Logger.LogWarning(message); }
        internal static string ConfigCommand(string command, bool server)
        {
            if (!instance || instance.commands == null) return "TooManyLags live configuration is unavailable on this installation.";
            try { return instance.commands.Execute(command, server); }
            catch (Exception error) { return "Config request failed: " + error.GetBaseException().Message; }
        }
        internal static string OwnerStatus(long peer, string prefabFilter = null)
        {
            if (!Diagnostics) return "EnableLogs must be enabled on the server before requesting owner diagnostics.";
            string status;
            if (Controller == null) status = "SERVER owner status: object grid unavailable; check Enabled and NoServerSideSimulation.";
            else
            {
                try { status = Controller.OwnerStatus(peer, prefabFilter); }
                catch (Exception error) { status = "SERVER owner status unavailable: " + error.GetBaseException().Message; }
            }
            // Returned to the requesting console; never duplicated into logs.
            return status;
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
            {
                logs = Diagnostics = ConfigValue<bool>("09 - Logs", key);
                if (logs)
                {
                    FgnCombatStatus.Register();
                    if (ZNet.instance) foreach (ZNetPeer peer in ZNet.instance.GetPeers()) FgnCombatStatus.Connect(peer);
                }
            }
            else if (key == "CameraHeadingCheckIntervalSeconds")
            { ConfigConsole.BroadcastCamera(ConfigValue<float>("07 - Camera Heading", key)); }
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
            { if (options != null) options.HighPing = ConfigValue<int>("03 - AI Mob Ownership", key); }
            else if (key == "ServerFallbackRadius")
            { if (options != null) options.MobRadius = ConfigValue<float>("03 - AI Mob Ownership", key); }
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
                case Kinds.TamedAnimals: name = "Idle tamed animals; guarded clients required. Attacks, riding/following and friendly magic summons remain protected"; break;
                case Kinds.Containers: name = "Public chests and inventories"; break;
                case Kinds.ItemStands: name = "Item and armour stands"; break;
                case Kinds.Signs: name = "Signs"; break;
                case Kinds.Portals: name = "Portals"; break;
                case Kinds.Fermenters: name = "Fermenters"; break;
                case Kinds.Incinerators: name = "Incinerators; guarded clients required. Burning/open inventories discard automatic transfers; new covering player activity retries"; break;
                case Kinds.WorldMarkers: name = "Existing network roots of dungeon entrances and Vegvisir; server default only, local objects without a ZDO have no owner"; break;
                case Kinds.Defenses: name = "Traps and turrets"; break;
                case Kinds.ShieldGenerators: name = "Shield generators"; break;
                case Kinds.Pickables: name = "Pickable objects and feasts; excludes loose item drops"; break;
                case Kinds.LooseItems: name = "Loose item drops, including eggs. Uses the interactive default at native initial owner assignment. false keeps native ownership. Native pickup takes priority over automatic grid transfers"; break;
                case Kinds.PhysicsObjects: name = "Networked world physics props and destructible sources of physical successors, such as trees/logs. Excludes projectiles, visual debris and ordinary loot. Grid transfers of non-interactable bodies require sleep at the owner; vacant-cell server returns do not. Native spawning retains creator ownership. Server-Side Simulation and updated clients required"; break;
                case Kinds.Offerings: name = "Offering altars"; break;
                case Kinds.Vehicles: name = "Idle carts and vehicle controls; FiresGhettoNetworking-simulated ships excluded"; break;
                default: name = "Other compatible modded interactables"; break;
            }
            return (kind == Kinds.Doors ? "All categories below: server only; restart required. true: managed by TooManyLags; false: vanilla/FiresGhettoNetworking. Open inventories, refilling, riding, following and other active use remain protected.\n" : "") + name + ".";
        }
        private void RuntimeFault(Exception error) { Logger.LogWarning("TooManyLags grid inactive; native ownership retained. " + error.GetBaseException()); }
        private void RequestFault(Exception error) { Logger.LogWarning("Native object request failed; not replayed. " + error.GetBaseException()); }
        private void Cleanup() { if (harmony != null) harmony.UnpatchSelf(); }
        private void OnDestroy()
        {
            MobFlightHandoff.Stop();
            StopCombatState();
            MobHandoffVisual.Stop();
            MobCollisionReadiness.Stop();
            if (communityPatchCompatibility != null) { communityPatchCompatibility.Dispose(); communityPatchCompatibility = null; }
            TextEditing.Protection.Stop();
            if (pupCompatibility != null) { pupCompatibility.Dispose(); pupCompatibility = null; }
            GridHandoffs.Protocol.Stop();
            ConfigConsole.Reset(); if (configHarmony != null) configHarmony.UnpatchSelf(); commands = null;
            if (layoutHarmony != null) layoutHarmony.UnpatchSelf(); ConfigLayout.Reset();
            FgnCombatOwnership.Stop(); FgnCombatStatus.Reset();
            PlayerSimulation.Plugin.Stop();
            if (Controller != null) Controller.Reset(); Controller = null;
            if (camera != null) camera.Reset(true); camera = null;
            Cleanup(); if (ReferenceEquals(instance, this)) instance = null;
        }
    }
    [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
    internal static class OwnershipPatch
    {
        [HarmonyTranspiler, HarmonyPriority(Priority.First), HarmonyBefore(Plugin.FgnId)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        { return OwnershipTranspiler.Rewrite(source, AccessTools.Method(typeof(OwnershipPatch), "Persistent")); }
        internal static bool Persistent(ZDO data) { return Plugin.Controller == null ? data.Persistent : Plugin.Controller.Persistent(data); }
        internal static long NativeOwnerTarget(ZDO data, long owner)
        {
            try { return Plugin.Controller == null ? owner : Plugin.Controller.NativeOwnerTarget(data, owner); }
            catch (Exception error) { Plugin.Report("Initial server assignment retained native ownership: " + error.GetBaseException().Message); return owner; }
        }
    }
    [HarmonyPatch(typeof(ZNetScene), "CreateObject")]
    internal static class InitialMobLoadPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ZDO zdo)
        {
            if (zdo == null || zdo.GetOwner() != 0L) return;
            if (Plugin.Controller != null) Plugin.Controller.InitialMobLoad(zdo);
            FgnCombatOwnership.InitialMobLoad(zdo);
        }
    }
    [HarmonyPatch(typeof(ZNetScene), "AddInstance")]
    internal static class AddPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ZDO zdo, ZNetView nview) { if (Plugin.Controller != null) Plugin.Controller.Register(zdo, nview); }
    }
    [HarmonyPatch(typeof(ZNetScene), "OnZDODestroyed")]
    internal static class InitialDestroyedPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ZDO __0) { if (Plugin.Controller != null) Plugin.Controller.DataDestroyed(__0); }
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


