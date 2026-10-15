using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.PupCompatibility
{
    // Optional lifecycle, independent of ownership sessions. No Unity component,
    // update loop or dependency polling is created for this module.
    internal sealed class Plugin : IDisposable
    {
        internal const string Id = "botanic.toomanylags.fgnaddon.pup";
        internal const string PupId = "com.pup.instancecombiner.safe", FgnId = "com.Fire.FiresGhettoNetworkMod";
        internal static NativePup Native;
        private static Plugin instance;
        private Harmony core, streaming, takeover;
        private bool supportInitialized, takeoverInitialized, disposed;

        internal static Plugin Start(bool requested, MonoBehaviour pup, Assembly network, bool externalGuard)
        {
            // Keep all native bindings, caches and Harmony work behind this
            // one-time gate. External PupPatch remains the sole guard owner.
            if (!requested || !pup || network == null || externalGuard) return null;
            if (instance != null) return instance;
            Plugin module = new Plugin();
            try
            {
                Native = new NativePup(pup);
                Statics.Bounds = SupportBounds.Create(); module.supportInitialized = true;
                StructuralTranspiler.Rewrite(PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(WearNTear), "UpdateWear")),
                    AccessTools.Field(typeof(WearNTear), "m_noSupportWear"), AccessTools.Method(typeof(Statics), "Evaluate"), AccessTools.Method(typeof(Statics), "Supported"));
                module.core = new Harmony(Id);
                foreach (Type type in new[] { typeof(Statics.WearPatch), typeof(Statics.SupportPatch), typeof(Statics.PreservePatch),
                    typeof(Statics.RepairPatch), typeof(NativeWaitPatch), typeof(NativeRetentionPatch) })
                    module.core.CreateClassProcessor(type).Patch();
                module.streaming = new Harmony(Id + ".streaming");
                PupServerStreaming.Install(module.streaming, pup.GetType().Assembly, true);
                module.takeover = new Harmony(Id + ".fgn");
                try
                {
                    module.takeoverInitialized = true;
                    FgnTakeover.Install(module.takeover, network);
                }
                catch (Exception error)
                {
                    module.takeover.UnpatchSelf(); FgnTakeover.Reset();
                    TooManyLags.Plugin.Report("PUP ownership-change supplement unavailable; collision/stability guards remain active. " + error.GetBaseException().Message);
                }
                // The target-ownership module is already part of TooManyLags;
                // never install a second copy from the standalone patch.
                instance = module;
                return module;
            }
            catch (Exception error)
            {
                module.Dispose();
                TooManyLags.Plugin.Report("Built-in PUP compatibility inactive; native PUP retained. " + error.GetBaseException().Message);
                return null;
            }
        }

        internal static void Warn(string message) { TooManyLags.Plugin.Warn(message); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (takeover != null) takeover.UnpatchSelf();
            if (core != null) core.UnpatchSelf();
            if (streaming != null) streaming.UnpatchSelf();
            if (takeoverInitialized) FgnTakeover.Reset();
            if (supportInitialized) { Statics.Reset(); Statics.Bounds = null; }
            Native = null;
            if (ReferenceEquals(instance, this)) instance = null;
        }
    }
}
