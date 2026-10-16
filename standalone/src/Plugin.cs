using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("1.1.0.0")]
namespace TooManyLagsStandalone
{
    [BepInPlugin(Id, "TooManyLags - Standalone", Version)]
    [BepInIncompatibility("botanic.toomanylags.fgnaddon")]
    [BepInIncompatibility("botanic.server.authority")]
    [BepInIncompatibility("botanic.server.buildings")]
    [BepInIncompatibility("com.Fire.FiresGhettoNetworkMod")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "botanic.toomanylags.standalone", Version = "1.1.0";
        internal static AuthorityController Controller;
        internal static Plugin Instance;
        internal static Network Network;
        internal static LocalAgent Local;
        internal static Settings Options;
        internal static bool HooksActive { get { return Local != null; } }
        internal static bool TargetSupport { get { return Instance && Instance.targetSupport; } }
        private Harmony harmony;
        private Harmony layoutHarmony;
        private CameraPublisher camera;
        private ConfigCommands commands;
        private bool targetSupport;
        private ZNet network;
        private ZDOMan world;
        private float nextNetwork;
        private void Awake()
        {
            Instance = this;
            try
            {
                ConfigLayout.Prepare(Config, Version);
                layoutHarmony = new Harmony(Id + ".configlayout");
                layoutHarmony.Patch(AccessTools.Method(typeof(ConfigFile), "Save"),
                    prefix: new HarmonyMethod(typeof(ConfigLayout), "BeforeSave"));
                Options = new Settings(Config);
                commands = new ConfigCommands(Config, Configure);
                CameraHeading.Configure(Options.CameraOverlap, Options.CameraPrediction);
                camera = new CameraPublisher(Options.CameraInterval);
                Network = new Network();
                NativeContracts.Validate();
                harmony = new Harmony(Id);
                harmony.PatchAll(typeof(Plugin).Assembly);
                // Install capability independently of local config. A client with
                // Enabled/EnableAIOwnership=false must still follow an enabled host.
                try
                {
                    TargetEvents.Install(harmony, delegate(BaseAI ai) { return Local != null && Local.Watched(ai); },
                        delegate(BaseAI ai) { if (Local != null) Local.TargetChanged(ai); }, MobFault);
                    targetSupport = true;
                }
                catch (Exception error) { MobFault(error); }
                ConfigConsole.Register();
                Logger.LogInfo("TooManyLags - Standalone " + Version + " ready. Host-controlled settings; optional client camera and safeguards; player-owned simulation.");
            }
            catch (Exception error) { Stop(); Logger.LogWarning("Standalone ownership inactive; native ownership retained. " + error.GetBaseException()); }
        }
        private void Update()
        {
            if (Network == null) return;
            if (!ZNet.instance || ZDOMan.instance == null || ZNet.IsSinglePlayer)
            { if (network || world != null) ResetWorld(); return; }
            float now = Time.unscaledTime;
            if (!ReferenceEquals(network, ZNet.instance) || !ReferenceEquals(world, ZDOMan.instance))
            {
                ResetWorld();
                network = ZNet.instance; world = ZDOMan.instance;
                if (network.IsServer())
                {
                    ApplySession(Options.Enabled, Options.Mobs, Options.CameraInterval, Options.CameraOverlap, Options.CameraPrediction);
                    if (Options.Enabled) Controller = new AuthorityController(Options);
                }
                nextNetwork = now;
            }
            if (now >= nextNetwork) { nextNetwork = now + .1f; Network.Poll(now); }
            if (TextEditingProtection.HasWork) TextEditingProtection.Poll();
            if (Local != null && Local.HasWork) Local.Tick(now);
        }
        internal static void ApplySession(bool enabled, bool mobs, float interval, float overlap, float prediction)
        {
            if (Options == null) return;
            Options.Session(enabled, mobs, interval, overlap, prediction);
            ApplyCameraSettings(interval, overlap, prediction);
            if (!enabled)
            {
                TextEditingProtection.Reset();
                if (Local != null) Local.Reset();
                Local = null; IncineratorProtection.Active = false;
                TargetEvents.Reset(); return;
            }
            if (Local == null)
            {
                Local = new LocalAgent();
                foreach (int hash in new[] { MobPolicy.TargetKey.Key, MobPolicy.TargetKey.Value, MobPolicy.WriterKey, MobPolicy.RevisionKey }) ZDOExtraData.AddSessionHash(hash);
            }
            Local.MobsEnabled = mobs && TargetSupport;
            IncineratorProtection.Active = true;
        }
        private void Configure(string key)
        {
            Options.ApplyLive(key);
            if (key == "EnableLogs") return;
            bool cameraSetting = key == "CameraHeadingCheckIntervalSeconds" || key == "CameraDirectionOverlapDegrees" || key == "CameraRotationPredictionSeconds";
            if (cameraSetting)
            {
                ApplyCameraSettings(Options.CameraInterval, Options.CameraOverlap, Options.CameraPrediction);
                if (Network != null) Network.BroadcastSettings();
            }
            if (Controller != null) Controller.Configure(key);
        }
        internal static string ConfigCommand(string command, bool server)
        { return Instance && Instance.commands != null ? Instance.commands.Execute(command, server) : "Standalone configuration is unavailable."; }
        internal static void SampleCamera() { if (HooksActive && Instance && Instance.camera != null) Instance.camera.Sample(Time.unscaledTime); }
        internal static void InvalidateCamera() { if (HooksActive && Instance && Instance.camera != null) Instance.camera.Reset(true); }
        internal static void ApplyCameraSettings(float interval, float overlap, float prediction)
        {
            CameraHeading.Configure(overlap, prediction);
            if (Instance && Instance.camera != null) Instance.camera.Configure(interval);
        }
        internal static void ApplyCameraOverlap(float degrees, float prediction = 0f) { CameraHeading.Configure(degrees, prediction); }
        internal static void Warn(string message) { if (Instance && Options != null && Options.Logs) Instance.Logger.LogWarning(message); }
        private void MobFault(Exception error)
        {
            targetSupport = false;
            if (Local != null) Local.MobsEnabled = false;
            Logger.LogWarning("Standalone mob ownership inactive: " + error.GetBaseException().Message);
        }
        internal static void Fault(Exception error)
        {
            if (Instance) Instance.Logger.LogWarning("Standalone ownership stopped; native ownership retained. " + error.GetBaseException());
            if (Controller != null) Controller.Stop();
        }
        private void Stop()
        {
            ResetWorld();
            if (harmony != null) harmony.UnpatchSelf();
            Network = null; targetSupport = false;
        }
        private void OnDestroy()
        {
            Stop();
            if (layoutHarmony != null) layoutHarmony.UnpatchSelf();
            ConfigLayout.Reset();
            if (Instance == this) Instance = null;
        }
        internal static void ResetWorld()
        {
            TextEditingProtection.Reset();
            if (Controller != null) Controller.Stop(); Controller = null;
            if (Local != null) Local.Reset(); Local = null;
            IncineratorProtection.Active = false;
            if (Network != null) Network.Reset();
            TargetEvents.Reset(); NativeEvents.Reset(); ConfigConsole.Reset();
            if (Options != null) Options.RestoreSession();
            if (Instance)
            {
                if (Instance.camera != null) Instance.camera.Reset();
                Instance.network = null; Instance.world = null;
                if (Options != null) ApplyCameraSettings(Options.CameraInterval, Options.CameraOverlap, Options.CameraPrediction);
            }
        }
    }
}
