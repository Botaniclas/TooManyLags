using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.PlayerSimulation
{
    // Internal mode, not a second plugin or a dependency on Standalone.
    internal static class Plugin
    {
        internal const string Id = TooManyLags.Plugin.Id + ".playersimulation";
        internal static AuthorityController Controller;
        internal static Network Network;
        internal static LocalAgent Local;
        internal static Settings Options;
        internal static bool ServerMode;
        private static Harmony harmony;
        private static Harmony activeHarmony;
        private static Assembly fgn;
        private static bool hooks;
        private static ZNet network;
        private static ZDOMan world;
        private static float nextNetwork;
        internal static void Initialize(ConfigFile config, Assembly assembly, bool serverMode, bool mobs)
        {
            Options = new Settings(config, mobs); fgn = assembly; ServerMode = serverMode;
            NativeLatency.Bind(fgn); Network = new Network();
            harmony = new Harmony(TooManyLags.Plugin.Id + ".playersimulation");
            harmony.CreateClassProcessor(typeof(ConnectionPatch)).Patch();
            harmony.CreateClassProcessor(typeof(ShutdownPatch)).Patch();
            if (ZNet.instance) foreach (ZNetPeer peer in ZNet.instance.GetPeers()) Network.Register(peer);
            if (serverMode) Activate();
        }
        internal static void Activate()
        {
            if (hooks) return;
            NativeContracts.Validate(); Local = new LocalAgent { MobsEnabled = Options.Mobs };
            activeHarmony = new Harmony(TooManyLags.Plugin.Id + ".playersimulation.active");
            try
            {
            foreach (Type type in typeof(Plugin).Assembly.GetTypes())
                if (type.Namespace == typeof(Plugin).Namespace && type != typeof(ConnectionPatch) && type != typeof(ShutdownPatch) &&
                    Attribute.IsDefined(type, typeof(HarmonyPatch))) activeHarmony.CreateClassProcessor(type).Patch();
            if (Options.Mobs) TargetEvents.Install(activeHarmony, Local.Watched, Local.TargetChanged, MobFault);
            FgnRpcIntegration.Install(activeHarmony, fgn); hooks = true;
            }
            catch { Deactivate(); throw; }
        }
        internal static void Deactivate()
        { if (activeHarmony != null) activeHarmony.UnpatchSelf(); activeHarmony = null; if (Local != null) Local.Reset(); Local = null; TargetEvents.Reset(); hooks = false; }
        internal static void Poll()
        {
            if (Network == null || !ZNet.instance || ZDOMan.instance == null || ZNet.IsSinglePlayer) return;
            // The normal dedicated-server mode only answers the connection
            // handshake. No second player/object loop runs alongside SSS.
            if (ZNet.instance.IsServer() && !ServerMode) return;
            if (!ZNet.instance.IsServer() && !Network.NeedsPoll) return;
            float now = Time.unscaledTime;
            if (!ReferenceEquals(network, ZNet.instance) || !ReferenceEquals(world, ZDOMan.instance))
            {
                if (Local != null) Local.Reset(); TargetEvents.Reset(); NativeEvents.Reset();
                network = ZNet.instance; world = ZDOMan.instance;
                Controller = network.IsServer() && ServerMode ? new AuthorityController(Options) : null;
                nextNetwork = now;
                foreach (int hash in new[] { MobPolicy.TargetKey.Key, MobPolicy.TargetKey.Value, MobPolicy.WriterKey, MobPolicy.RevisionKey }) ZDOExtraData.AddSessionHash(hash);
            }
            if (now >= nextNetwork) { nextNetwork = now + .1f; Network.Poll(now); }
            if (Controller != null) Controller.Poll(now);
            if (Local != null && Local.HasWork) Local.Tick(now);
        }
        private static void MobFault(Exception error)
        { if (Local != null) Local.MobsEnabled = false; TooManyLags.Plugin.Report("Player-owned mob handoffs inactive: " + error.GetBaseException().Message); }
        internal static void Warn(string text) { TooManyLags.Plugin.Warn(text); }
        internal static void Fault(Exception error)
        { if (Controller != null) Controller.Stop(); TooManyLags.Plugin.Report("Player-owned handoffs inactive; native ownership retained. " + error.GetBaseException().Message); }
        internal static void ResetWorld()
        {
            if (Controller != null) Controller.Stop(); Controller = null;
            if (Local != null) Local.Reset(); if (Network != null) Network.Reset(); TargetEvents.Reset();
            network = null; world = null;
        }
        internal static void Stop()
        { ResetWorld(); Deactivate(); if (harmony != null) harmony.UnpatchSelf(); Network = null; }
    }
}
