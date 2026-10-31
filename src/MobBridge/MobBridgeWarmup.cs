#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NativeMobPreviewAudit;

namespace TooManyLags
{
    // Move one-time delegate/JIT creation away from the first ownership change.
    // This only prepares existing code. It never executes simulation or writes
    // actor/network state.
    internal static class MobBridgeWarmup
    {
        private static FieldInfo[] fields;
        private static MethodBase[] methods;
        private static int field, method;
        private static bool active, failed;

        internal static void Begin()
        {
            if (active || failed) return;
            try
            {
                fields = MobBridgeNativeActor.WarmFields();
                methods = new MethodBase[]
                {
                    typeof(MobBridgeNativeActor).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0],
                    typeof(MobBridgeAnimation).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0],
                    typeof(MobBridgeSimulation).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0],
                    typeof(MobBridgeHit).GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                    typeof(MobBridgeSkin).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0],
                    typeof(MobBridgeEffects).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0]
                };
                active = true;
            }
            catch (Exception error)
            {
                failed = true;
                Plugin.Warn("Mob bridge warmup unavailable; runtime setup retained: " + error.GetBaseException().Message);
            }
        }

        internal static void Cancel()
        {
            active = false; fields = null; methods = null; field = method = 0;
        }

        internal static void Pump()
        {
            if (!active) return;
            try
            {
                if (field < fields.Length) MobBridgeNativeActor.WarmField(fields[field++]);
                if (method < methods.Length)
                {
                    MethodBase value = methods[method++];
                    if (value != null && !value.IsGenericMethodDefinition)
                        RuntimeHelpers.PrepareMethod(value.MethodHandle);
                }
                if (field >= fields.Length && method >= methods.Length)
                {
                    active = false; fields = null; methods = null;
                }
            }
            catch (Exception)
            {
                // Unsupported Mono preparation must not block the real path.
                field++; method++;
            }
        }
    }
}
#endif
