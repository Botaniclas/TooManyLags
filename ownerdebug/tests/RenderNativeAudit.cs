using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;

// Executes the actual Unity managed dispatcher and production postfix under
// Valheim Mono. No camera/world creation or graphics/physics calls are needed.
internal static class RenderNativeAudit
{
    private static string[] paths;
    private static int checks, callbacks, foreignCalls;
    private static void Check(bool value, string message)
    { ++checks; if (!value) throw new Exception(message); }
    private static void Observe() { ++callbacks; }
    private static void Foreign() { ++foreignCalls; }
    private static bool AnimatorHash(string __0, ref int __result) { __result = __0 == null ? 0 : __0.GetHashCode(); return false; }
    private static void Set(object instance, string field, object value) { AccessTools.Field(instance.GetType(), field).SetValue(instance, value); }
    private static void PhaseIdentity(Assembly tml, Assembly game, MethodInfo readPhase)
    {
        // Disposable managed native containers only. No Unity GameObjects, physics or RPC.
        Type tests = tml.GetType("TooManyLags.FgnSoloCombatTest", true);
        Type sessionType = tests.GetNestedType("Session", BindingFlags.NonPublic);
        FieldInfo active = AccessTools.Field(tests, "session");
        FieldInfo worldSlot = AccessTools.Field(game.GetType("ZDOMan", true), "s_instance");
        object previous = active.GetValue(null), previousWorld = worldSlot.GetValue(null);
        Type visual = tml.GetType("TooManyLags.MobBridgeVisual", true);
        var tracks = (System.Collections.IList)AccessTools.Field(visual, "tracks").GetValue(null);
        object track = null;
        try
        {
            object world = FormatterServices.GetUninitializedObject(worldSlot.FieldType);
            worldSlot.SetValue(null, world);
            object data = FormatterServices.GetUninitializedObject(game.GetType("ZDO", true));
            Set(data, "<OwnerRevision>k__BackingField", (ushort)7);
            object session = FormatterServices.GetUninitializedObject(sessionType);
            Set(session, "Data", data); Set(session, "World", world); Set(session, "Pending", true);
            Set(session, "Revision", (uint)7); active.SetValue(null, session);
            Check((int)readPhase.Invoke(null, new[] { data }) == 0, "Exact prepared owner/revision reports old owner");
            Set(data, "<OwnerRevision>k__BackingField", (ushort)8);
            Check((int)readPhase.Invoke(null, new[] { data }) == 1, "Received ownership revision reports new owner before poll completion");
            Set(session, "Pending", false); Set(session, "Completed", 1);
            Check((int)readPhase.Invoke(null, new[] { data }) == 1, "Completed transfer keeps new-owner phase");
            object other = FormatterServices.GetUninitializedObject(data.GetType());
            Check((int)readPhase.Invoke(null, new[] { other }) == -1, "Different ZDO identity excluded");
            track = FormatterServices.GetUninitializedObject(visual.GetNestedType("Track", BindingFlags.NonPublic));
            Set(track, "Data", data); tracks.Add(track);
            MethodInfo presenting = AccessTools.Method(readPhase.DeclaringType, "Presenting");
            Check((bool)presenting.Invoke(null, new[] { data }), "Existing private TML bridge lookup binds through read-only cached delegate");
            Check((int)readPhase.Invoke(null, new[] { data }) == 2, "Actual active bridge track overrides owner with fake playback");
            Set(track, "Takeover", FormatterServices.GetUninitializedObject(tml.GetType("TooManyLags.MobBridgeTakeover", true)));
            Check((int)readPhase.Invoke(null, new[] { data }) == 3, "Actual takeover object selects smoothing phase");
            active.SetValue(null, null);
            Check((int)readPhase.Invoke(null, new[] { data }) == -1, "Stopped test cannot colour leftover visual track");
            Check((bool)presenting.Invoke(null, new[] { data }), "Normal owner overlay still snapshots an active non-test presentation");
            tracks.Remove(track); track = null;
            Check(!(bool)presenting.Invoke(null, new[] { data }), "Finished bridge disables snapshot work immediately");
        }
        finally { if (track != null) tracks.Remove(track); active.SetValue(null, previous); worldSlot.SetValue(null, previousWorld); }
    }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string path in paths)
        { string file = Path.Combine(path, name); if (File.Exists(file)) return Assembly.LoadFrom(file); }
        return null;
    }
    private static Delegate Listener(Type type)
    {
        var method = new DynamicMethod("RenderPoseListener", typeof(void),
            type.GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), typeof(RenderNativeAudit).Module, true);
        var code = method.GetILGenerator();
        code.Emit(OpCodes.Call, typeof(RenderNativeAudit).GetMethod("Observe", BindingFlags.NonPublic | BindingFlags.Static));
        code.Emit(OpCodes.Ret);
        return method.CreateDelegate(type);
    }
    public static int Main(string[] args)
    {
        try
        {
            paths = new[] { Path.GetDirectoryName(args[0]), args[1], args[2], Path.GetDirectoryName(args[3]) };
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Assembly addon = Assembly.LoadFrom(args[0]);
            Type camera = Assembly.LoadFrom(Path.Combine(args[1], "UnityEngine.CoreModule.dll")).GetType("UnityEngine.Camera", true);
            Type hook = addon.GetType("BotanicOwnerDebug.RenderHooks", true);
            Type restoreHook = addon.GetType("BotanicOwnerDebug.RenderRestoreHooks", true);
            MethodInfo dispatch = camera.GetMethod("FireOnPreRender", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo finish = camera.GetMethod("FireOnPostRender", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo listeners = camera.GetField("onPreRender", BindingFlags.Public | BindingFlags.Static);
            Check(dispatch != null && listeners != null, "Installed Unity dispatcher/field missing");
            Delegate saved = (Delegate)listeners.GetValue(null);
            var owner = new Harmony("test.ownerdebug.native.render");
            var foreign = new Harmony("test.ownerdebug.foreign.render");
            try
            {
                foreign.Patch(dispatch, postfix: new HarmonyMethod(typeof(RenderNativeAudit), "Foreign"));
                for (int cycle = 0; cycle < 2; ++cycle)
                {
                    Delegate first = Listener(listeners.FieldType), second = Listener(listeners.FieldType);
                    listeners.SetValue(null, first);
                    owner.CreateClassProcessor(hook).Patch();
                    owner.CreateClassProcessor(restoreHook).Patch();
                    Check(Harmony.GetPatchInfo(finish).Prefixes.Count(p => p.PatchMethod.DeclaringType == restoreHook && p.priority == Priority.First) == 1,
                        "Native camera restoration hook installs once before post-render listeners");
                    Delegate combined = Delegate.Combine(first, second);
                    listeners.SetValue(null, combined);
                    var patches = Harmony.GetPatchInfo(dispatch);
                    Check(patches.Postfixes.Count(p => p.PatchMethod.DeclaringType == hook) == 1,
                        "Production postfix was not installed exactly once on actual Unity dispatcher");
                    Check(patches.Postfixes.Single(p => p.PatchMethod.DeclaringType == hook).priority == Priority.Last,
                        "Production render postfix lost last priority");
                    int before = callbacks, beforeForeign = foreignCalls;
                    dispatch.Invoke(null, new object[] { null });
                    finish.Invoke(null, new object[] { null });
                    Check(callbacks == before + 2 && foreignCalls == beforeForeign + 1,
                        "Production postfix interrupted native dispatcher listeners/foreign hook");
                    Check(Object.ReferenceEquals(listeners.GetValue(null), combined) &&
                        ((Delegate)listeners.GetValue(null)).GetInvocationList().Length == 2,
                        "Production postfix rewrote camera listeners");
                    owner.UnpatchSelf();
                    Check(!Harmony.GetPatchInfo(finish).Owners.Contains(owner.Id), "Native restoration hook removed on disable");
                    patches = Harmony.GetPatchInfo(dispatch);
                    Check(!patches.Postfixes.Any(p => p.PatchMethod.DeclaringType == hook) &&
                        patches.Owners.Contains("test.ownerdebug.foreign.render"), "Cleanup removed foreign render hook");
                    dispatch.Invoke(null, new object[] { null });
                    Check(callbacks == before + 4 && foreignCalls == beforeForeign + 2,
                        "Native dispatcher did not survive production hook teardown");
                }
            }
            finally { owner.UnpatchSelf(); foreign.UnpatchSelf(); listeners.SetValue(null, saved); }
            Assembly game = Assembly.LoadFrom(Path.Combine(args[1], "assembly_valheim.dll"));
            var menu = new Harmony("test.ownerdebug.native.menu");
            var seams = new Harmony("test.ownerdebug.native.seams");
            Type animator = Assembly.LoadFrom(Path.Combine(args[1], "UnityEngine.AnimationModule.dll")).GetType("UnityEngine.Animator", true);
            seams.Patch(AccessTools.Method(animator, "StringToHash", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(RenderNativeAudit), "AnimatorHash"));
            string[] hookNames = { "MenuInputHooks", "MenuCursorHooks", "MenuScrollHooks", "MenuControllerHooks", "MenuEscapeHooks" };
            MethodInfo[] targets = {
                AccessTools.Method(game.GetType("Player", true), "TakeInput"),
                AccessTools.Method(game.GetType("GameCamera", true), "UpdateMouseCapture"),
                AccessTools.Method(Assembly.LoadFrom(Path.Combine(args[1], "assembly_utils.dll")).GetType("ZInput", true), "GetMouseScrollWheel"),
                AccessTools.Method(game.GetType("PlayerController", true), "TakeInput"),
                AccessTools.Method(game.GetType("Menu", true), "Update")
            };
            try
            {
                for (int i = 0; i < hookNames.Length; i++)
                {
                    Type menuHook = addon.GetType("BotanicOwnerDebug." + hookNames[i], true);
                    menu.CreateClassProcessor(menuHook).Patch();
                    var info = Harmony.GetPatchInfo(targets[i]);
                    Check(info.Postfixes.Concat(info.Prefixes).Count(p => p.PatchMethod.DeclaringType == menuHook) == 1,
                        "Actual native menu hook installs once: " + hookNames[i]);
                }
            }
            finally { try { menu.UnpatchSelf(); } finally { seams.UnpatchSelf(); } }
            foreach (MethodInfo target in targets)
            {
                var info = Harmony.GetPatchInfo(target);
                Check(info == null || !info.Owners.Contains(menu.Id), "Menu hook cleanup: " + target.Name);
            }
            Type integration = addon.GetType("BotanicOwnerDebug.TmlDebug", true);
            MethodInfo bind = AccessTools.Method(integration, "Bind");
            MethodInfo readPhase = AccessTools.Method(integration, "Phase");
            PropertyInfo installed = AccessTools.Property(integration, "Installed"), supported = AccessTools.Property(integration, "Supported");
            bind.Invoke(null, new object[] { null });
            Check(!(bool)installed.GetValue(null, null) && !(bool)supported.GetValue(null, null), "Missing optional TML remains safe");
            Assembly tml = Assembly.LoadFrom(args[3]);
            bind.Invoke(null, new object[] { tml });
            Check((bool)installed.GetValue(null, null) && (bool)supported.GetValue(null, null), "Actual compiled TML API binds through cached delegates");
            Check((int)readPhase.Invoke(null, new object[] { null }) == -1, "Actual compiled TML rejects absent test mob");
            PhaseIdentity(tml, game, readPhase);
            bind.Invoke(null, new object[] { addon });
            Check((bool)installed.GetValue(null, null) && !(bool)supported.GetValue(null, null), "Installed older API-less plugin disables phase mode safely");
            bind.Invoke(null, new object[] { null });
            Console.WriteLine("PASS: " + checks + " actual Unity render-dispatcher hook/install/order/cleanup checks under Valheim Mono.");
            Console.WriteLine("LIMIT: managed dispatcher and menu-hook installation only; Animator hash seam for native Player static initialisation. No Unity world, skinning, GPU or interactive menu.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
