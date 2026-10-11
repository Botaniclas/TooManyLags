using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace TooManyLags
{
    // Startup-only transaction. Stop only the optional combat module, never
    // the plugin or its collision/streaming guards. No configuration is changed.
    internal sealed class MobPolicyHandoff : IDisposable
    {
        private const string Owner = "botanic.pup.patch.fgn.aiownership";
        private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private Action restore;

        internal static MobPolicyHandoff Begin(bool requested, ConfigFile config, Type policy, Assembly network, System.Version version)
        {
            MobPolicyHandoff handoff = new MobPolicyHandoff();
            if (!requested) return handoff;
            bool patched = HasPatches();
            if (policy == null)
            {
                if (patched) throw new NotSupportedException("Existing mob policy has no supported handoff contract.");
                return handoff;
            }
            FieldInfo enabled = policy.GetField("Enabled", Flags);
            if (enabled == null || enabled.FieldType != typeof(bool))
                throw new NotSupportedException("Existing mob policy state is unavailable.");
            bool wasEnabled = (bool)enabled.GetValue(null);
            if (!wasEnabled && !patched) return handoff;

            MethodInfo stop = policy.GetMethod("Stop", Flags, null, Type.EmptyTypes, null);
            MethodInfo install = policy.GetMethod("Install", Flags, null, new[] { typeof(ConfigFile), typeof(Assembly), typeof(System.Version) }, null);
            if (config == null || stop == null || stop.ReturnType != typeof(void) || install == null || install.ReturnType != typeof(void))
                throw new NotSupportedException("Existing mob policy cannot be safely suspended and restored.");
            Action suspend = (Action)Delegate.CreateDelegate(typeof(Action), stop);
            Action<ConfigFile, Assembly, System.Version> resume = (Action<ConfigFile, Assembly, System.Version>)Delegate.CreateDelegate(typeof(Action<ConfigFile, Assembly, System.Version>), install);
            if (wasEnabled) handoff.restore = delegate
            {
                resume(config, network, version);
                if (!(bool)enabled.GetValue(null)) throw new InvalidOperationException("Previous mob policy could not be restored.");
            };
            try
            {
                suspend();
                if ((bool)enabled.GetValue(null) || HasPatches())
                    throw new InvalidOperationException("Existing mob policy did not release its ownership hooks.");
                return handoff;
            }
            catch
            {
                // Do not install a second policy if stopping the first one failed.
                // A still-active policy needs no reinstall (duplicate transpilers).
                if (!(bool)enabled.GetValue(null) && !HasPatches()) handoff.Dispose();
                throw;
            }
        }

        private static bool HasPatches()
        {
            foreach (MethodBase method in Harmony.GetAllPatchedMethods())
            {
                Patches patches = Harmony.GetPatchInfo(method);
                if (patches != null && patches.Owners.Contains(Owner)) return true;
            }
            return false;
        }
        internal void Commit() { restore = null; }
        public void Dispose()
        {
            Action resume = restore; restore = null;
            if (resume != null) resume();
        }
    }
}
