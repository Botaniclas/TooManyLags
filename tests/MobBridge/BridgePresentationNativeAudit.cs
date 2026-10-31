using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BridgePresentationNativeAudit
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance |
        BindingFlags.Public | BindingFlags.NonPublic;
    private static string[] paths;
    private static int checks;
    private static int ownershipCallbacks = 0;
    // Uninitialized Unity instances have no native instance-ID service in this
    // headless host. Only key hashing is replaced; field/finalizer/cleanup code
    // still executes from the actual addon and game assemblies.
    private sealed class UninitializedObjectComparer<T> : System.Collections.Generic.IEqualityComparer<T> where T : class
    {
        public bool Equals(T a, T b) { return Object.ReferenceEquals(a, b); }
        public int GetHashCode(T value) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value); }
    }

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string path in paths)
        {
            string candidate = Path.Combine(path, name);
            if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
        }
        return null;
    }

    private static bool Patched(MethodInfo method, Type declaring)
    {
        Patches patches = Harmony.GetPatchInfo(method);
        return patches != null &&
            (patches.Prefixes.Any(p => p.PatchMethod.DeclaringType == declaring) ||
             patches.Postfixes.Any(p => p.PatchMethod.DeclaringType == declaring) ||
             patches.Finalizers.Any(p => p.PatchMethod.DeclaringType == declaring));
    }

    private static Delegate Callback(Type type)
    {
        MethodInfo signature = type.GetMethod("Invoke");
        var method = new System.Reflection.Emit.DynamicMethod("TestCallback", signature.ReturnType,
            signature.GetParameters().Select(p => p.ParameterType).ToArray(), typeof(BridgePresentationNativeAudit).Module, true);
        var il = method.GetILGenerator();
        if (signature.ReturnType != typeof(void))
        {
            if (signature.ReturnType.IsValueType)
            {
                var value = il.DeclareLocal(signature.ReturnType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloca, value);
                il.Emit(System.Reflection.Emit.OpCodes.Initobj, signature.ReturnType);
                il.Emit(System.Reflection.Emit.OpCodes.Ldloc, value);
            }
            else il.Emit(System.Reflection.Emit.OpCodes.Ldnull);
        }
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        return method.CreateDelegate(type);
    }

    private static object LoopNode(Type system, Type phase, Array children, Delegate callback)
    {
        object value = Activator.CreateInstance(system);
        system.GetField("type").SetValue(value, phase);
        system.GetField("subSystemList").SetValue(value, children);
        if (callback != null) system.GetField("updateDelegate").SetValue(value, callback);
        return value;
    }
    private static void EffectLoopChecks(Type effects, Assembly game)
    {
        Type system = effects.GetMethod("Insert", All).GetParameters()[0].ParameterType.GetElementType();
        Type late = system.Assembly.GetType("UnityEngine.PlayerLoop.PreLateUpdate", true);
        Type particle = late.GetNestedType("ParticleSystemBeginUpdateAll", All);
        Type constraint = late.GetNestedType("ConstraintManagerUpdate", All);
        Delegate foreign = Callback(system.GetField("updateDelegate").FieldType);
        foreach (bool particleFirst in new[] { false, true })
        {
            Array phases = Array.CreateInstance(system, 3);
            phases.SetValue(LoopNode(system, typeof(BridgePresentationNativeAudit), null, foreign), 0);
            phases.SetValue(LoopNode(system, particleFirst ? particle : constraint, null, null), 1);
            phases.SetValue(LoopNode(system, particleFirst ? constraint : particle, null, null), 2);
            object[] args = { LoopNode(system, late, phases, null) };
            Check((bool)effects.GetMethod("Insert", All).Invoke(null, args), "effect loop phase insertion failed");
            Array added = (Array)system.GetField("subSystemList").GetValue(args[0]);
            Check(added.Length == 4 && (Type)system.GetField("type").GetValue(added.GetValue(1)) == effects.GetNestedType("ParticleAnchorUpdate", All),
                "effect anchor update does not precede first particle/constraint phase");
            Check((bool)effects.GetMethod("Insert", All).Invoke(null, args) &&
                ((Array)system.GetField("subSystemList").GetValue(args[0])).Length == 4, "warm loop insertion duplicates callbacks");
            effects.GetMethod("RemoveLoop", All).Invoke(null, args);
            Array removed = (Array)system.GetField("subSystemList").GetValue(args[0]);
            Check(removed.Length == 3 && Object.ReferenceEquals(system.GetField("updateDelegate").GetValue(removed.GetValue(0)), foreign),
                "effect loop teardown replaced another plugin's callback");
            Check((Type)system.GetField("type").GetValue(removed.GetValue(1)) == (particleFirst ? particle : constraint),
                "effect loop teardown reordered native phases");
        }
        Type zdoType = game.GetType("ZDO", true);
        object data = FormatterServices.GetUninitializedObject(zdoType);
        var epochs = (System.Collections.IDictionary)effects.GetField("expected", All).GetValue(null);
        effects.GetMethod("Expect", All).Invoke(null, new[] { data, (object)7U, (object)9U });
        object id = zdoType.GetField("m_uid", All).GetValue(data);
        effects.GetMethod("Finished", All).Invoke(null, new[] { id, (object)7U, (object)9U, (object)(byte)1 });
        Check(epochs.Count == 1, "effect tags stop when matching ends, before visual correction finishes");
        effects.GetMethod("Finished", All).Invoke(null, new[] { id, (object)7U, (object)10U, (object)(byte)2 });
        Check(epochs.Count == 1, "stale final notice removes a newer effect epoch");
        effects.GetMethod("Finished", All).Invoke(null, new[] { id, (object)7U, (object)9U, (object)(byte)2 });
        Check(epochs.Count == 0, "final visual completion leaks effect tagging epoch");
        object sync = FormatterServices.GetUninitializedObject(game.GetType("ZSyncTransform", true));
        foreach (string method in new[] { "BeforeSync", "BeforeOwnerSync" })
        {
            object[] args = { sync, null };
            effects.GetMethod(method, All).Invoke(null, args);
            Check(args[1] == null, "idle effects hook touches Unity or opens a native pose scope");
        }
        FieldInfo callback = effects.GetField("OwnershipSynced", All);
        Check(callback.GetValue(null) == null, "normal session activates ownership sync work");
        MethodInfo after = effects.GetMethod("AfterSync", All);
        Check(after.Invoke(null, new[] { sync, null, null }) == null, "idle native sync finalizer failed");
        MethodInfo invoke = callback.FieldType.GetMethod("Invoke");
        var thunk = new System.Reflection.Emit.DynamicMethod("OwnershipCallback", typeof(void),
            invoke.GetParameters().Select(p => p.ParameterType).ToArray(), typeof(BridgePresentationNativeAudit).Module, true);
        var code = thunk.GetILGenerator();
        FieldInfo count = typeof(BridgePresentationNativeAudit).GetField("ownershipCallbacks", All);
        code.Emit(System.Reflection.Emit.OpCodes.Ldsfld, count); code.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_1);
        code.Emit(System.Reflection.Emit.OpCodes.Add); code.Emit(System.Reflection.Emit.OpCodes.Stsfld, count); code.Emit(System.Reflection.Emit.OpCodes.Ret);
        callback.SetValue(null, thunk.CreateDelegate(callback.FieldType));
        try
        {
            int before = ownershipCallbacks;
            after.Invoke(null, new[] { sync, null, null });
            Check(ownershipCallbacks == before + 1, "armed ownership callback not reached through existing native sync finalizer");
            var failure = new InvalidOperationException("native sync failure");
            Check(Object.ReferenceEquals(after.Invoke(null, new object[] { sync, null, failure }), failure) && ownershipCallbacks == before + 1,
                "failed native sync starts ownership correction or replaces its exception");
        }
        finally { callback.SetValue(null, null); }
        Type visual = effects.Assembly.GetType("TooManyLags.MobBridgeVisual", true);
        Type syncType = game.GetType("ZSyncTransform", true);
        MethodInfo reader = effects.Assembly.GetType("TooManyLags.FieldReader", true).GetMethods(All)
            .Single(m => m.Name == "Create" && m.IsGenericMethodDefinition);
        object getter = reader.MakeGenericMethod(syncType, typeof(bool)).Invoke(null, new object[] { syncType.GetField("m_wasOwner", All) });
        FieldInfo getterField = visual.GetField("syncWasOwner", All), pendingField = visual.GetField("ownershipSync", All);
        getterField.SetValue(null, getter);
        Type trackType = visual.GetNestedType("Track", All), stateType = visual.GetNestedType("OwnershipBlend", All);
        object track = Activator.CreateInstance(trackType, true), state = Activator.CreateInstance(stateType, true);
        stateType.GetField("Waiting", All).SetValue(state, true); stateType.GetField("Sync", All).SetValue(state, sync);
        trackType.GetField("Ownership", All).SetValue(track, state);
        object invalid = FormatterServices.GetUninitializedObject(zdoType); zdoType.GetField("m_prefab", All).SetValue(invalid, -1);
        trackType.GetField("Data", All).SetValue(track, invalid);
        object comparer = Activator.CreateInstance(typeof(UninitializedObjectComparer<>).MakeGenericType(syncType), true);
        var pending = (System.Collections.IDictionary)Activator.CreateInstance(pendingField.FieldType, new[] { comparer });
        pending.Add(sync, track); pendingField.SetValue(null, pending);
        callback.SetValue(null, Delegate.CreateDelegate(callback.FieldType, visual.GetMethod("OwnershipSynced", All)));
        try
        {
            syncType.GetField("m_wasOwner", All).SetValue(sync, false);
            visual.GetMethod("OwnershipSynced", All).Invoke(null, new[] { sync });
            Check(pending.Count == 1 && (bool)stateType.GetField("Waiting", All).GetValue(state),
                "ownership watcher completes before native owner pose reset");
            syncType.GetField("m_wasOwner", All).SetValue(sync, true);
            visual.GetMethod("OwnershipSynced", All).Invoke(null, new[] { sync });
            Check(pendingField.GetValue(null) == null && callback.GetValue(null) == null && !(bool)stateType.GetField("Waiting", All).GetValue(state),
                "invalid successor leaves per-sync watcher armed");
            pending.Add(sync, track); pendingField.SetValue(null, pending);
            callback.SetValue(null, Delegate.CreateDelegate(callback.FieldType, visual.GetMethod("OwnershipSynced", All)));
            visual.GetMethod("Stop", All).Invoke(null, null);
            Check(pendingField.GetValue(null) == null && callback.GetValue(null) == null, "session shutdown retains ownership sync watcher");
        }
        finally { pendingField.SetValue(null, null); callback.SetValue(null, null); }
    }

    private static void NativeAccessChecks(Type native, Assembly game)
    {
        object actor = FormatterServices.GetUninitializedObject(game.GetType("Humanoid", true));
        string[,] accessors = {
            { "writeWalk", "m_walk", "" }, { "writeFlying", "m_flying", "" },
            { "writeLook", "m_lookDir", "" }, { "writeYaw", "m_lookYaw", "" },
            { "writeAnimFrame", "m_cachedAnimHashFrame", "" },
            { "writeHitTime", "m_hitWorldTime", "readHitTime" },
            { "writeGroundTimer", "m_groundForceTimer", "readGroundTimer" },
            { "writeCurrentHash", "m_cachedCurrentAnimHash", "" },
            { "writeNextHash", "m_cachedNextAnimHash", "" },
            { "writeAnyHash", "m_cachedNextOrCurrentAnimHash", "" }
        };
        for (int i = 0; i < accessors.GetLength(0); ++i)
        {
            FieldInfo field = AccessTools.Field(actor.GetType(), accessors[i, 1]);
            object value = field.FieldType == typeof(bool) ? (object)true : field.FieldType == typeof(int) ?
                (object)117 : field.FieldType == typeof(float) ? (object)1.375f : Activator.CreateInstance(field.FieldType);
            if (field.FieldType.Name == "Vector3" || field.FieldType.Name == "Quaternion")
            {
                field.FieldType.GetField("x").SetValue(value, .125f);
                field.FieldType.GetField("y").SetValue(value, -.25f);
                field.FieldType.GetField("z").SetValue(value, .5f);
                if (field.FieldType.Name == "Quaternion") field.FieldType.GetField("w").SetValue(value, .75f);
            }
            ((Delegate)native.GetField(accessors[i, 0], All).GetValue(null)).DynamicInvoke(actor, value);
            Check(Object.Equals(field.GetValue(actor), value), "typed motor writer differs from native field: " + field.Name);
            if (accessors[i, 2].Length != 0)
                Check(Object.Equals(((Delegate)native.GetField(accessors[i, 2], All).GetValue(null)).DynamicInvoke(actor), value),
                    "typed motor reader differs from native field: " + field.Name);
        }
        FieldInfo velocity = AccessTools.Field(actor.GetType(), "m_currentVel");
        object nativeVelocity = Activator.CreateInstance(velocity.FieldType);
        velocity.FieldType.GetField("x").SetValue(nativeVelocity, -13f);
        velocity.FieldType.GetField("y").SetValue(nativeVelocity, 2.5f);
        velocity.FieldType.GetField("z").SetValue(nativeVelocity, .25f);
        velocity.SetValue(actor, nativeVelocity);
        Check(Object.Equals(((Delegate)native.GetField("readVelocity", All).GetValue(null)).DynamicInvoke(actor), velocity.GetValue(actor)),
            "typed native velocity reader changed value");
        object session = FormatterServices.GetUninitializedObject(native);
        native.GetField("callbacks", All).SetValue(session, native.GetMethod("CreateCallbacks", All).Invoke(null, new[] { actor }));
        FieldInfo[] fields = (FieldInfo[])native.GetMethod("DelegateFields", All).Invoke(null, null);
        object[] originals = fields.Select(f => (object)Callback(f.FieldType)).ToArray();
        for (int i = 0; i < fields.Length; ++i) fields[i].SetValue(actor, originals[i]);
        for (int pass = 0; pass < 2; ++pass)
        {
            if (pass == 1)
                for (int i = 0; i < fields.Length; ++i)
                { originals[i] = i % 2 == 0 ? null : Callback(fields[i].FieldType); fields[i].SetValue(actor, originals[i]); }
            native.GetMethod("CaptureCallbacks", All).Invoke(session, null);
            native.GetMethod("ClearCallbacks", All).Invoke(session, null);
            foreach (FieldInfo field in fields) Check(field.GetValue(actor) == null, "native callback remained active: " + field.Name);
            native.GetMethod("RestoreCallbacks", All).Invoke(session, null);
            for (int i = 0; i < fields.Length; ++i)
                Check(Object.ReferenceEquals(fields[i].GetValue(actor), originals[i]), "callback restore lost latest native delegate: " + fields[i].Name);
        }
        Check(fields.Length != 0, "native callback isolation test covered no callback fields");
        MotorAccessBenchmark(native, actor);
    }

    private static void MotorAccessBenchmark(Type native, object actor)
    {
        // Measure field dispatch only against the actual native Humanoid class.
        // No DynamicInvoke/reflection inside the optimized loop, as in Step.
        FieldInfo reader = native.GetField("readHitTime", All), writer = native.GetField("writeHitTime", All);
        FieldInfo value = AccessTools.Field(actor.GetType(), "m_hitWorldTime");
        var method = new System.Reflection.Emit.DynamicMethod("MotorFieldLoop", typeof(void),
            new[] { typeof(object), typeof(int) }, typeof(BridgePresentationNativeAudit).Module, true);
        var il = method.GetILGenerator();
        var target = il.DeclareLocal(actor.GetType()); var index = il.DeclareLocal(typeof(int));
        var loop = il.DefineLabel(); var condition = il.DefineLabel();
        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0); il.Emit(System.Reflection.Emit.OpCodes.Castclass, actor.GetType()); il.Emit(System.Reflection.Emit.OpCodes.Stloc, target);
        il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0); il.Emit(System.Reflection.Emit.OpCodes.Stloc, index);
        il.Emit(System.Reflection.Emit.OpCodes.Br, condition); il.MarkLabel(loop);
        il.Emit(System.Reflection.Emit.OpCodes.Ldsfld, writer); il.Emit(System.Reflection.Emit.OpCodes.Ldloc, target);
        il.Emit(System.Reflection.Emit.OpCodes.Ldsfld, reader); il.Emit(System.Reflection.Emit.OpCodes.Ldloc, target);
        il.Emit(System.Reflection.Emit.OpCodes.Callvirt, reader.FieldType.GetMethod("Invoke"));
        il.Emit(System.Reflection.Emit.OpCodes.Ldc_R4, .02f); il.Emit(System.Reflection.Emit.OpCodes.Add);
        il.Emit(System.Reflection.Emit.OpCodes.Callvirt, writer.FieldType.GetMethod("Invoke"));
        il.Emit(System.Reflection.Emit.OpCodes.Ldloc, index); il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_1);
        il.Emit(System.Reflection.Emit.OpCodes.Add); il.Emit(System.Reflection.Emit.OpCodes.Stloc, index);
        il.MarkLabel(condition); il.Emit(System.Reflection.Emit.OpCodes.Ldloc, index); il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
        il.Emit(System.Reflection.Emit.OpCodes.Blt, loop); il.Emit(System.Reflection.Emit.OpCodes.Ret);
        var typed = (Action<object, int>)method.CreateDelegate(typeof(Action<object, int>));
        typed(actor, 1);
        for (int pass = 0; pass < 3; ++pass)
        {
            const int count = 50000;
            value.SetValue(actor, 0f);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < count; ++i) value.SetValue(actor, (float)value.GetValue(actor) + .02f);
            watch.Stop(); double reflection = watch.Elapsed.TotalMilliseconds; object expected = value.GetValue(actor);
            value.SetValue(actor, 0f); watch.Restart(); typed(actor, count); watch.Stop();
            Check(Object.Equals(value.GetValue(actor), expected), "typed native field loop changes floating point accumulation");
            Console.WriteLine("TIMING: native field read/write; iterations=" + count + "; reflectionMs=" +
                reflection.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "; typedMs=" +
                watch.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            string sandbox = Path.Combine(Path.GetDirectoryName(
                typeof(BridgePresentationNativeAudit).Assembly.Location), "presentation-static-seam");
            Directory.CreateDirectory(sandbox);
            string gameCopy = Path.Combine(sandbox, "assembly_valheim.dll");
            using (AssemblyDefinition copy = AssemblyDefinition.ReadAssembly(
                Path.Combine(args[1], "assembly_valheim.dll")))
            {
                foreach (string name in new[] { "Character", "Player", "MonsterAI", "Humanoid", "ZSyncAnimation" })
                {
                    MethodDefinition init = copy.MainModule.Types.Single(t => t.Name == name)
                        .Methods.Single(m => m.Name == ".cctor");
                    init.Body.Instructions.Clear();
                    init.Body.ExceptionHandlers.Clear();
                    init.Body.Variables.Clear();
                    init.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                }
                copy.Write(gameCopy);
            }
            paths = new[] { sandbox, args[1], args[2], Path.GetDirectoryName(args[0]) };
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            Assembly game = Assembly.LoadFrom(gameCopy);
            Assembly addon = Assembly.LoadFrom(args[0]);
            Type visual = addon.GetType("TooManyLags.MobBridgeVisual", true);
            Type character = game.GetType("Character", true);
            Type hud = game.GetType("EnemyHud", true);
            MethodInfo top = character.GetMethod("GetTopPoint", All);
            MethodInfo update = hud.GetMethod("UpdateHuds", All);
            MethodInfo damage = character.GetMethod("Damage", All, null, new[] { game.GetType("HitData", true) }, null);
            MethodInfo beforeHit = visual.GetMethod("BeforeHit", All);
            MethodInfo beforeHud = visual.GetMethod("BeforeHud", All);
            MethodInfo afterHud = visual.GetMethod("AfterHud", All);
            MethodInfo beforeTop = visual.GetMethod("BeforeTopPoint", All);
            MethodInfo hitObject = game.GetType("Projectile", true).GetMethod("FindHitObject", All);
            MethodInfo beforeHitObject = visual.GetMethod("BeforeHitObject", All);
            MethodInfo weakSpot = character.GetMethod("FindWeakSpotIndex", All);
            MethodInfo beforeWeakSpot = visual.GetMethod("BeforeWeakSpot", All);
            MethodInfo doAttack = game.GetType("MonsterAI", true).GetMethod("DoAttack", All);
            Type protocol = addon.GetType("TooManyLags.MobBridgeProtocol", true);
            MethodInfo afterAttack = protocol.GetMethod("AfterAttack", All);
            Type cadence = addon.GetType("NativeMobPreviewAudit.NativeCadence", true);
            object continuous = Activator.CreateInstance(cadence, All, null, new object[] { 0d, 1d, true }, null);
            object bounded = Activator.CreateInstance(cadence, All, null, new object[] { 0d, 1d, false }, null);
            Check(cadence.GetMethod("Take", All).Invoke(continuous, new object[] { 1.2d }).ToString() == "Produced" &&
                cadence.GetMethod("Take", All).Invoke(bounded, new object[] { 1.2d }).ToString() == "Expired",
                "continuous bridge decisions still expire at initial L or changed legacy cadence");
            Check(cadence.GetMethod("Take", All).Invoke(continuous, new object[] { 1.23d }).ToString() == "Produced",
                "externally scheduled fake AI still enforces a second fixed cadence");
            Type native = addon.GetType("TooManyLags.MobBridgeNativeActor", true);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(native.TypeHandle);
            Check(true, "native movement/root-motion delegate initialization");
            NativeAccessChecks(native, game);
            Check(top != null && update != null && damage != null && beforeHit != null &&
                beforeHud != null && afterHud != null && beforeTop != null,
                "native presentation hook contract unavailable");
            Harmony hooks = new Harmony("TooManyLags.bridge-presentation-audit");
            try
            {
                hooks.Patch(damage, prefix: new HarmonyMethod(beforeHit));
                hooks.Patch(update, prefix: new HarmonyMethod(beforeHud),
                    finalizer: new HarmonyMethod(afterHud));
                hooks.Patch(top, prefix: new HarmonyMethod(beforeTop));
                hooks.Patch(hitObject, prefix: new HarmonyMethod(beforeHitObject));
                hooks.Patch(weakSpot, prefix: new HarmonyMethod(beforeWeakSpot));
                hooks.Patch(doAttack, postfix: new HarmonyMethod(afterAttack));
            }
            catch
            {
                hooks.UnpatchSelf();
                throw;
            }
            Check(Patched(damage, visual), "Character.Damage fake-pose router missing");
            Check(Patched(update, visual), "EnemyHud.UpdateHuds fake anchor scope missing");
            Check(Patched(top, visual), "Character.GetTopPoint fake anchor override missing");
            Check(Patched(hitObject, visual) && Patched(doAttack, protocol), "hit-object/attack decision native hooks missing");
            Check(Patched(weakSpot, visual), "native weak-spot proxy remapping hook missing");
            hooks.UnpatchSelf();
            Check(!Patched(hitObject, visual) && !Patched(doAttack, protocol), "new scoped native hooks leaked");
            Check(!Patched(weakSpot, visual), "weak-spot proxy hook leaked after bridge shutdown");
            Check(!Patched(damage, visual) && !Patched(update, visual) && !Patched(top, visual),
                "presentation hooks leaked after bridge shutdown");
            native.GetMethod("EnsureHooks", All).Invoke(null, null);
            try
            {
                foreach (string name in new[] { "ZDO", "ZNetView", "ZSyncAnimation", "Character", "Humanoid", "EffectList", "RandomIdle", "AnimSetTrigger", "StateController" })
                    Check(game.GetType(name, true).GetMethods(All).Any(m => Patched(m, native)), "native recording isolation hook missing: " + name);
                FieldInfo[] motionFields = (FieldInfo[])native.GetMethod("StateFields", All).Invoke(null, null);
                foreach (string name in new[] { "m_body", "m_collider", "m_eye", "m_lastPos", "m_lazyPos", "m_flying", "m_swimTimer", "m_lastGroundCollider", "m_lowestContactCollider", "m_lastGroundBody", "m_lastAttachBody", "m_deepSnowWalkObj" })
                    Check(motionFields.Any(f => f.Name == name), "native motion snapshot omits " + name);
            }
            finally { native.GetMethod("ReleaseHooks", All).Invoke(null, null); }
            Check(!Patched(damage, native) && !Patched(game.GetType("ZNetView", true).GetMethod("IsOwner", All), native),
                "native recording hooks leaked after shutdown");
            visual.GetMethod("PrepareSession", All).Invoke(null, null);
            Type effectPresentation = addon.GetType("TooManyLags.MobBridgeEffects", true);
            try
            {
                foreach (string method in new[] { "CustomFixedUpdate", "CustomLateUpdate", "SyncNow" })
                    Check(Patched(game.GetType("ZSyncTransform", true).GetMethod(method, All), effectPresentation),
                        "FX native pose scope hook missing: " + method);
                Check(Patched(game.GetType("EffectList", true).GetMethod("Create", All), effectPresentation),
                    "new attached FX creation hook failed to install");
                Check(Patched(game.GetType("ZNetScene", true).GetMethod("AddInstance", All), effectPresentation),
                    "late network FX hook failed to install");
                Check(Patched(character.GetMethod("SetupContinuousEffect", All), effectPresentation),
                    "water/slide/flight native FX update scope failed to install");
                EffectLoopChecks(effectPresentation, game);
                native.GetMethod("ReleaseHooks", All).Invoke(null, null);
                visual.GetMethod("Stop", All).Invoke(null, null);
                Check(Patched(damage, native) && Patched(damage, visual),
                    "finishing a preview rebuilt retained native/presentation hooks");
                visual.GetMethod("PrepareSession", All).Invoke(null, null);
                Check(Patched(damage, native) && Patched(weakSpot, visual), "warm session preparation lost hooks");
            }
            finally { visual.GetMethod("EndSession", All).Invoke(null, null); }
            Check(!Patched(game.GetType("EffectList", true).GetMethod("Create", All), effectPresentation) &&
                !Patched(game.GetType("ZSyncTransform", true).GetMethod("SyncNow", All), effectPresentation),
                "effect presentation hooks leak after world shutdown");
            Check(!Patched(damage, native) && !Patched(damage, visual), "world session shutdown retained hooks");
            Check(Object.ReferenceEquals(native.GetMethod("StateFields", All).Invoke(null, null),
                native.GetMethod("StateFields", All).Invoke(null, null)), "native field metadata rebuilt per preview");
            Type solo = addon.GetType("TooManyLags.FgnSoloCombatTest", false);
            if (solo != null)
            {
                solo.GetMethod("EnsureLocalHooks", All).Invoke(null, null);
                try
                {
                    solo.GetMethod("ClearLocal", All).Invoke(null, new object[] { false });
                    Check(Patched(game.GetType("MonsterAI", true).GetMethod("UpdateAI", All), solo),
                        "completed test transfer removed the session's AI hooks");
                }
                finally { solo.GetMethod("ClearLocal", All).Invoke(null, new object[] { true }); }
                Check(!Patched(game.GetType("MonsterAI", true).GetMethod("UpdateAI", All), solo), "stopped test retained AI hooks");
            }
            else Check(addon.GetType("TooManyLags.SoloNetworkLatency", false) == null, "release retains artificial test latency");
            object idleAi = FormatterServices.GetUninitializedObject(game.GetType("MonsterAI", true));
            object[] targetArguments = new object[] { idleAi, null, .05f, false, false };
            Check((bool)protocol.GetMethod("BeforeTarget", All).Invoke(null, targetArguments),
                "idle target hook does not return before Unity/actor access");
            protocol.GetMethod("BeforeRandom", All).Invoke(null, new object[] { idleAi, .05f, null });
            protocol.GetMethod("AfterAttack", All).Invoke(null, new object[] { idleAi, true });
            Check(true, "empty clock hooks bypass Unity calls");
            object session = FormatterServices.GetUninitializedObject(native);
            object ownView = FormatterServices.GetUninitializedObject(game.GetType("ZNetView", true));
            object otherView = FormatterServices.GetUninitializedObject(game.GetType("ZNetView", true));
            object ownActor = FormatterServices.GetUninitializedObject(game.GetType("Humanoid", true));
            object otherActor = FormatterServices.GetUninitializedObject(game.GetType("Humanoid", true));
            native.GetField("View", All).SetValue(session, ownView); native.GetField("Actor", All).SetValue(session, ownActor);
            FieldInfo active = native.GetField("current", All);
            Check((bool)native.GetMethod("BeforeDataWrite", All).Invoke(null, null), "inactive recorder blocks normal ZDO writes");
            active.SetValue(null, session);
            try
            {
                object[] ownerArgs = new object[] { ownView, false };
                Check(!(bool)native.GetMethod("BeforeOwner", All).Invoke(null, ownerArgs) && (bool)ownerArgs[1], "own native motor lacks scoped authority");
                Check((bool)native.GetMethod("BeforeOwner", All).Invoke(null, new object[] { otherView, false }), "native scope grants authority to unrelated view");
                Check(!(bool)native.GetMethod("BeforeRpc", All).Invoke(null, new[] { ownView }) &&
                    (bool)native.GetMethod("BeforeRpc", All).Invoke(null, new[] { otherView }), "native scope RPC ownership filter failed");
                Check(!(bool)native.GetMethod("BeforeDataWrite", All).Invoke(null, null), "native motor can write replicated data");
                Check(!(bool)native.GetMethod("BeforeOwnAction", All).Invoke(null, new[] { ownActor }) &&
                    (bool)native.GetMethod("BeforeOwnAction", All).Invoke(null, new[] { otherActor }), "native scope changes unrelated combat actor");
                object[] attackArgs = new object[] { ownActor, true };
                Check(!(bool)native.GetMethod("BeforeAttack", All).Invoke(null, attackArgs) && !(bool)attackArgs[1], "later real attack enters earlier native motor");
            }
            finally { active.SetValue(null, null); }
            Type animationType = addon.GetType("TooManyLags.MobBridgeAnimation", true);
            object preview = FormatterServices.GetUninitializedObject(animationType);
            FieldInfo parameters = animationType.GetField("parameters", All);
            parameters.SetValue(preview, Activator.CreateInstance(parameters.FieldType));
            object template = FormatterServices.GetUninitializedObject(game.GetType("Attack", true));
            game.GetType("Attack", true).GetField("m_toggleFlying", All).SetValue(template, true);
            animationType.GetField("attack", All).SetValue(preview, template);
            Type receiverType = addon.GetType("TooManyLags.MobBridgeAnimationRoot", true);
            object receiver = FormatterServices.GetUninitializedObject(receiverType);
            receiverType.GetField("Recording", All).SetValue(receiver, preview);
            receiverType.GetMethod("Hit", All).Invoke(receiver, null);
            Check((bool)animationType.GetField("FlightEvent", All).GetValue(preview), "visual Hit lost local native flight-toggle state");
            receiverType.GetMethod("Land", All).Invoke(receiver, null);
            Check(!(bool)animationType.GetField("FlightEvent", All).GetValue(preview), "visual Land did not update private flight state");
            receiverType.GetMethod("Jump", All).Invoke(receiver, null);
            Check((bool)animationType.GetField("JumpRequested", All).GetValue(preview), "visual Jump did not request private native jump");
            receiverType.GetMethod("Stop", All).Invoke(receiver, new object[] { null });
            Check((bool)animationType.GetField("stopped", All).GetValue(preview), "visual Stop did not retain native attack movement stop");
            object privateAnimator = FormatterServices.GetUninitializedObject(game.GetType("Character", true).GetField("m_animator", All).FieldType);
            object otherAnimator = FormatterServices.GetUninitializedObject(privateAnimator.GetType());
            animationType.GetField("Animator", All).SetValue(preview, privateAnimator);
            FieldInfo animationScope = animationType.GetField("Current", All);
            Check((bool)native.GetMethod("BeforeStateController", All).Invoke(null, new[] { privateAnimator }), "inactive preview blocks real state-controller effects");
            animationScope.SetValue(null, preview);
            try
            {
                Check(!(bool)native.GetMethod("BeforeStateController", All).Invoke(null, new[] { privateAnimator }) &&
                    (bool)native.GetMethod("BeforeStateController", All).Invoke(null, new[] { otherAnimator }), "private state controller can affect live children/effects");
                object triggerBehaviour = FormatterServices.GetUninitializedObject(game.GetType("AnimSetTrigger", true));
                Check(!(bool)native.GetMethod("BeforeBehaviourEnter", All).Invoke(null, new[] { triggerBehaviour, privateAnimator }) &&
                    (bool)native.GetMethod("BeforeBehaviourEnter", All).Invoke(null, new[] { triggerBehaviour, otherAnimator }),
                    "state trigger adapter changes unrelated Animator");
            }
            finally { animationScope.SetValue(null, null); }
            Console.WriteLine("PASS: " + checks +
                " actual game presentation hooks install, target EnemyHud.UpdateHuds/Character.GetTopPoint and uninstall.");
            Console.WriteLine("LIMIT: disposable game copy omits Character/Player/MonsterAI/Humanoid/ZSyncAnimation Unity-only static initializers; no Unity HUD or world execution.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
