using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.PupCompatibility
{
    internal static class PupServerStreaming
    {
        internal static bool Install(Harmony harmony, Assembly pupAssembly, bool fgnLoaded)
        {
            if (!fgnLoaded) return false;
            Type type = pupAssembly.GetType("ServerZoneStreamBlock", false);
            if (type == null || !typeof(Behaviour).IsAssignableFrom(type)) return false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            MethodInfo target = type.GetMethod("Initialize", flags, null, Type.EmptyTypes, null);
            FieldInfo initialized = type.GetField("initialized", flags);
            if (target == null || target.ReturnType != typeof(void) || initialized == null ||
                initialized.FieldType != typeof(bool)) return false;
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(PupServerStreaming), "Prefix"));
            return true;
        }

        private static bool Prefix(Behaviour __instance, ref bool ___initialized)
        {
            // FGN already owns SendZDOs. Do not enter PUP's incompatible patch or
            // its failing UnpatchSelf recovery. No changes to cluster networking.
            ___initialized = true;
            __instance.enabled = false;
            return false;
        }
    }
}
