using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BridgeWeaponNativeAudit
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static string[] paths;
    private static Assembly game, core, addon;
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static FieldInfo Field(Type type, string name)
    {
        for (; type != null; type = type.BaseType)
        { FieldInfo field = type.GetField(name, All | BindingFlags.DeclaredOnly); if (field != null) return field; }
        throw new Exception("Missing field " + name);
    }
    private static void Set(object owner, string name, object value) { Field(owner.GetType(), name).SetValue(owner, value); }
    private static object New(Type type) { return FormatterServices.GetUninitializedObject(type); }
    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string file = new AssemblyName(args.Name).Name + ".dll";
        foreach (string path in paths) if (File.Exists(Path.Combine(path, file))) return Assembly.LoadFrom(Path.Combine(path, file));
        return null;
    }
    private static MethodDefinition Method(TypeDefinition type, string name) { return type.Methods.Single(m => m.Name == name); }
    private static ILProcessor Reset(MethodDefinition method)
    {
        method.ImplAttributes = Mono.Cecil.MethodImplAttributes.IL | Mono.Cecil.MethodImplAttributes.Managed;
        method.Body = new Mono.Cecil.Cil.MethodBody(method); return method.Body.GetILProcessor();
    }
    private static void Constant(MethodDefinition method, float value)
    { ILProcessor il = Reset(method); il.Emit(OpCodes.Ldc_R4, value); il.Emit(OpCodes.Ret); }
    private static void Prepare(string managed, string sandbox)
    {
        using (AssemblyDefinition copy = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "UnityEngine.CoreModule.dll")))
        {
            ModuleDefinition module = copy.MainModule;
            TypeDefinition obj = module.Types.Single(t => t.FullName == "UnityEngine.Object");
            MethodDefinition init = obj.Methods.SingleOrDefault(m => m.Name == ".cctor");
            if (init != null) Reset(init).Emit(OpCodes.Ret);
            TypeDefinition transform = module.Types.Single(t => t.FullName == "UnityEngine.Transform");
            TypeDefinition vector = module.Types.Single(t => t.FullName == "UnityEngine.Vector3");
            FieldDefinition root = new FieldDefinition("__auditTransform", Mono.Cecil.FieldAttributes.Public, transform); obj.Fields.Add(root);
            FieldDefinition position = new FieldDefinition("__auditPosition", Mono.Cecil.FieldAttributes.Public, vector); transform.Fields.Add(position);
            ILProcessor il = Reset(Method(module.Types.Single(t => t.FullName == "UnityEngine.Component"), "get_transform"));
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, root); il.Emit(OpCodes.Ret);
            il = Reset(Method(transform, "get_position")); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, position); il.Emit(OpCodes.Ret);
            il = Reset(Method(transform, "get_forward"));
            VariableDefinition forward = new VariableDefinition(vector); il.Body.Variables.Add(forward); il.Body.InitLocals = true;
            il.Emit(OpCodes.Ldloca, forward); il.Emit(OpCodes.Ldc_R4, 1f); il.Emit(OpCodes.Stfld, vector.Fields.Single(f => f.Name == "z"));
            il.Emit(OpCodes.Ldloc, forward); il.Emit(OpCodes.Ret);
            il = Reset(Method(obj, "op_Implicit")); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un); il.Emit(OpCodes.Ret);
            il = Reset(Method(obj, "op_Equality")); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
            il = Reset(Method(obj, "op_Inequality")); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
            Constant(Method(module.Types.Single(t => t.FullName == "UnityEngine.Time"), "get_time"), 100f);
            TypeDefinition random = module.Types.Single(t => t.FullName == "UnityEngine.Random");
            FieldDefinition counter = new FieldDefinition("__auditCounter", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static, module.TypeSystem.Int32); random.Fields.Add(counter);
            MethodDefinition range = random.Methods.Single(m => m.Name == "Range" && m.Parameters[0].ParameterType.FullName == "System.Int32");
            il = Reset(range);
            il.Emit(OpCodes.Ldsfld, counter); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, counter);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Rem); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ret);
            copy.Write(Path.Combine(sandbox, "UnityEngine.CoreModule.dll"));
        }
        using (AssemblyDefinition copy = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "assembly_valheim.dll")))
        {
            foreach (string name in new[] { "Character", "Player", "MonsterAI", "Humanoid" })
            {
                MethodDefinition init = copy.MainModule.Types.Single(t => t.Name == name).Methods.SingleOrDefault(m => m.Name == ".cctor");
                if (init != null) Reset(init).Emit(OpCodes.Ret);
            }
            TypeDefinition character = copy.MainModule.Types.Single(t => t.Name == "Character");
            FieldDefinition attackFlag = new FieldDefinition("__auditAttack", Mono.Cecil.FieldAttributes.Public, copy.MainModule.TypeSystem.Boolean); character.Fields.Add(attackFlag);
            ILProcessor il = Reset(Method(character, "InAttack")); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, attackFlag); il.Emit(OpCodes.Ret);
            Constant(Method(character, "GetRadius"), .5f);
            Constant(Method(character, "GetHealth"), 40f); Constant(Method(character, "GetMaxHealth"), 100f);
            Constant(Method(copy.MainModule.Types.Single(t => t.Name == "BaseAI"), "GetAltitude"), 3f);
            TypeDefinition humanoid = copy.MainModule.Types.Single(t => t.Name == "Humanoid");
            il = Reset(Method(humanoid, "InAttack")); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, attackFlag); il.Emit(OpCodes.Ret);
            il = Reset(Method(humanoid, "EquipItem")); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, humanoid.Fields.Single(f => f.Name == "m_rightItem")); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il = Reset(Method(humanoid, "UnequipItem")); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, humanoid.Fields.Single(f => f.Name == "m_rightItem")); il.Emit(OpCodes.Ret);
            copy.Write(Path.Combine(sandbox, "assembly_valheim.dll"));
        }
    }

    private sealed class FinallyProbe { public int Count; }
    private static int FinallyRoot(FinallyProbe probe, int mode)
    { try { if (mode == 1) throw new InvalidOperationException(); return mode + 7; } finally { probe.Count++; } }

    private static void FinallyChecks()
    {
        Type policyType = addon.GetType("NativeMobPreviewAudit.ForkPolicy", true);
        object policy = Activator.CreateInstance(policyType, true);
        MethodInfo root = typeof(BridgeWeaponNativeAudit).GetMethod("FinallyRoot", All);
        ((HashSet<MethodInfo>)Field(policyType, "Copies").GetValue(policy)).Add(root);
        ((HashSet<FieldInfo>)Field(policyType, "Writes").GetValue(policy)).Add(typeof(FinallyProbe).GetField("Count"));
        ((HashSet<MethodBase>)Field(policyType, "Reads").GetValue(policy)).Add(typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes));
        Type forkType = addon.GetType("NativeMobPreviewAudit.NativeAiFork", true);
        object fork = Activator.CreateInstance(forkType, All, null, new[] { policy }, null);
        Func<FinallyProbe, int, int> run = (Func<FinallyProbe, int, int>)forkType.GetMethod("Bind", All).MakeGenericMethod(typeof(Func<FinallyProbe, int, int>)).Invoke(fork, new object[] { root });
        FinallyProbe probe = new FinallyProbe();
        Check(run(probe, 0) == 7 && probe.Count == 1, "copied finally failed on early return");
        bool threw = false; try { run(probe, 1); } catch (InvalidOperationException) { threw = true; }
        Check(threw && probe.Count == 2, "copied finally failed on exception");
    }

    private static object Vector(float x, float z)
    { object result = Activator.CreateInstance(core.GetType("UnityEngine.Vector3", true)); Set(result, "x", x); Set(result, "z", z); return result; }
    private static object Rotation()
    { object result = Activator.CreateInstance(core.GetType("UnityEngine.Quaternion", true)); Set(result, "w", 1f); return result; }
    private static object Pose(object actor, float z)
    { object root = New(core.GetType("UnityEngine.Transform", true)); Set(root, "__auditPosition", Vector(0f, z)); Set(actor, "__auditTransform", root); return root; }
    private static object Weapon(Type itemType, string name, float range, float min, bool priority)
    {
        object item = Activator.CreateInstance(itemType); object shared = Activator.CreateInstance(Field(itemType, "m_shared").FieldType);
        Set(item, "m_shared", shared);
        Set(shared, "m_name", name); Set(shared, "m_itemType", Enum.Parse(Field(shared.GetType(), "m_itemType").FieldType, "OneHandedWeapon"));
        Set(shared, "m_aiTargetType", Enum.Parse(Field(shared.GetType(), "m_aiTargetType").FieldType, "Enemy"));
        Set(shared, "m_aiAttackRange", range); Set(shared, "m_aiAttackRangeMin", min); Set(shared, "m_aiPrioritized", priority);
        Set(shared, "m_aiWhenWalking", true); Set(shared, "m_aiWhenSwiming", true); Set(shared, "m_aiMaxHealthPercentage", 1f);
        Set(shared, "m_aiWhenFlying", false); Set(shared, "m_aiMinHealthPercentage", 0f);
        Set(shared, "m_aiAttackInterval", 5f); Set(item, "m_lastAttackTime", 10f); return item;
    }
    private static void RunSelection()
    {
        Type humanoid = game.GetType("Humanoid", true), monster = game.GetType("MonsterAI", true);
        Type itemType = game.GetType("ItemDrop+ItemData", true), contextType = addon.GetType("NativeMobPreviewAudit.SteeringContext", true);
        Type stateType = addon.GetType("NativeMobPreviewAudit.MobBridgeWeaponState", true);
        Type adapters = addon.GetType("NativeMobPreviewAudit.NativeSteeringAdapters", true);
        Type planType = addon.GetType("NativeMobPreviewAudit.NativeSteeringPlan", true);
        object plan = Activator.CreateInstance(planType, true), fork = Field(planType, "Fork").GetValue(plan);
        MethodInfo native = humanoid.GetMethod("EquipBestWeapon", All);
        Type delegateType = typeof(Action<,,,,>).MakeGenericType(humanoid, game.GetType("Character"), game.GetType("StaticTarget"), game.GetType("Character"), game.GetType("Character"));
        Delegate copied = (Delegate)fork.GetType().GetMethod("Bind", All).MakeGenericMethod(delegateType).Invoke(fork, new object[] { native });
        FieldInfo random = core.GetType("UnityEngine.Random", true).GetField("__auditCounter");
        foreach (string list in new[] { "optimalWeapons", "outofRangeWeapons", "allWeapons" })
            Field(humanoid, list).SetValue(null, Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType)));
        object actor = New(humanoid), target = New(game.GetType("Character", true)), ai = New(monster);
        object root = Pose(actor, 0f), targetRoot = Pose(target, 2f);
        Set(ai, "__auditTransform", root);
        Set(actor, "m_baseAI", ai); Set(ai, "m_character", actor); Set(actor, "m_swimTimer", 1f);
        object inventory = New(game.GetType("Inventory", true));
        IList items = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        Set(inventory, "m_inventory", items); Set(actor, "m_inventory", inventory);
        object context = New(contextType);
        Set(context, "Actor", actor); Set(context, "Ai", ai); Set(context, "Plan", plan);
        Set(context, "Root", root); Set(context, "TargetRoot", targetRoot); Set(context, "Target", target);
        Set(context, "ShadowPosition", Vector(0f, 0f)); Set(context, "ShadowRotation", Rotation()); Set(context, "NativeTime", 100f);
        FieldInfo current = Field(adapters, "Current");
        for (int scenario = 0; scenario < 14; ++scenario)
        for (int seed = 0; seed < 8; ++seed)
        {
            items.Clear(); object first = Weapon(itemType, "first", 3f, 0f, false), second = Weapon(itemType, "second", 3f, 0f, false);
            items.Add(first); items.Add(second); Set(actor, "m_rightItem", first);
            Set(actor, "m_flying", false); Set(actor, "m_swimTimer", 1f); Set(context, "ShadowFlying", false); Set(context, "ShadowSwimming", false);
            object shared = Field(itemType, "m_shared").GetValue(second); object hurt = null, friend = null;
            if (scenario == 1) Set(shared, "m_aiPrioritized", true);
            if (scenario == 2) { Set(Field(itemType, "m_shared").GetValue(first), "m_aiAttackRange", .2f); Set(shared, "m_aiAttackRange", .2f); }
            if (scenario == 3) { Set(first, "m_lastAttackTime", 99f); Set(second, "m_lastAttackTime", 99f); }
            if (scenario == 4) Set(shared, "m_aiAttackRangeMin", 4f);
            if (scenario == 5) { Set(shared, "m_aiPrioritizedIfAngleCheckValid", true); Set(shared, "m_aiAttackMaxAngle", 30f); }
            if (scenario == 6) { Set(shared, "m_aiPrioritizedIfAngleCheckValid", true); Set(shared, "m_aiAttackMaxAngle", 30f); Set(shared, "m_aiInvertAngleCheck", true); }
            if (scenario == 7) { Set(shared, "m_aiTargetType", Enum.Parse(Field(shared.GetType(), "m_aiTargetType").FieldType, "FriendHurt")); hurt = target; }
            if (scenario == 8) { Set(shared, "m_aiTargetType", Enum.Parse(Field(shared.GetType(), "m_aiTargetType").FieldType, "Friend")); friend = target; }
            if (scenario == 9) { items.Clear(); }
            if (scenario == 10) { Set(actor, "m_flying", true); Set(context, "ShadowFlying", true); Set(shared, "m_aiWhenFlying", true); Set(shared, "m_aiWhenFlyingAltitudeMin", 1f); Set(shared, "m_aiWhenFlyingAltitudeMax", 4f); }
            if (scenario == 11) { Set(actor, "m_swimTimer", 0f); Set(context, "ShadowSwimming", true); Set(shared, "m_aiWhenSwiming", false); }
            if (scenario == 12) { Set(shared, "m_aiMaxHealthPercentage", .3f); }
            if (scenario == 13) { Set(first, "m_lastAttackTime", 99f); Set(shared, "m_aiPrioritized", true); }
            if (items.Count == 0) Set(actor, "m_rightItem", null);
            object weapons = Activator.CreateInstance(stateType, All, null, new[] { actor }, null); Set(context, "Weapons", weapons);
            object saved = Field(humanoid, "m_rightItem").GetValue(actor);
            random.SetValue(null, seed); native.Invoke(actor, new[] { target, null, hurt, friend });
            object expected = humanoid.GetMethod("GetCurrentWeapon", All).Invoke(actor, null); int expectedCalls = (int)random.GetValue(null);
            if (scenario == 1 || scenario == 13 || scenario == 10) Check(ReferenceEquals(expected, second), "native scenario did not exercise weapon reselection: " + scenario +
                "; eligible=" + game.GetType("BaseAI").GetMethod("CanUseAttack", All).Invoke(ai, new[] { second }) +
                "; flying=" + Field(humanoid, "m_flying").GetValue(actor));
            Set(actor, "m_rightItem", saved); random.SetValue(null, seed); current.SetValue(null, context);
            try { copied.DynamicInvoke(actor, target, null, hurt, friend); } finally { current.SetValue(null, null); }
            Check(ReferenceEquals(Field(stateType, "Current").GetValue(weapons), expected), "native/fake weapon choice differs: " + scenario + "/" + seed);
            Check((int)random.GetValue(null) == expectedCalls, "native/fake selection consumes different random calls");
            Check(ReferenceEquals(Field(humanoid, "m_rightItem").GetValue(actor), saved), "fake selection equipped a live item");
            Check((float)Field(itemType, "m_lastAttackTime").GetValue(second) == (scenario == 3 ? 99f : 10f), "fake selection changed a live cooldown");
        }
        // Exercise the real weapon checkpoint against reversed destination
        // inventory order and a different game-time epoch.
        items.Clear(); object a = Weapon(itemType, "A", 3f, 0f, false), b = Weapon(itemType, "B", 3f, 0f, false);
        items.Add(a); items.Add(b); Set(actor, "m_rightItem", b); Set(a, "m_lastAttackTime", 10f); Set(b, "m_lastAttackTime", 99f);
        Type packageType = game.GetType("ZPackage", false) ?? AppDomain.CurrentDomain.GetAssemblies().Select(s => s.GetType("ZPackage", false)).First(t => t != null);
        object packet = Activator.CreateInstance(packageType);
        stateType.GetMethod("Write", All).Invoke(null, new[] { packet, actor, (object)100f });
        items.Clear(); items.Add(b); items.Add(a); Set(actor, "m_rightItem", a); Set(a, "m_lastAttackTime", 0f); Set(b, "m_lastAttackTime", 0f);
        packageType.GetMethod("SetPos", All).Invoke(packet, new object[] { 0 });
        stateType.GetMethod("Read", All).Invoke(null, new[] { packet, actor, (object)200f });
        Check(ReferenceEquals(items[0], a) && ReferenceEquals(items[1], b), "weapon checkpoint did not restore native candidate order");
        Check(ReferenceEquals(humanoid.GetMethod("GetCurrentWeapon", All).Invoke(actor, null), b), "weapon checkpoint did not restore selected attack");
        Check((float)Field(itemType, "m_lastAttackTime").GetValue(a) == 110f && (float)Field(itemType, "m_lastAttackTime").GetValue(b) == 199f,
            "weapon checkpoint did not preserve each candidate cooldown across clock epochs");
        // A later replicated attack animation must not suppress an earlier fake
        // decision. Its logical attack state is still the captured source state.
        Set(actor, "m_flying", false); Set(actor, "m_swimTimer", 1f); Set(actor, "m_rightItem", a);
        Set(a, "m_lastAttackTime", 10f); Set(b, "m_lastAttackTime", 10f); Set(Field(itemType, "m_shared").GetValue(b), "m_aiPrioritized", true);
        Set(context, "ShadowFlying", false); Set(context, "ShadowSwimming", false); Set(context, "ShadowInAttack", false);
        object isolated = Activator.CreateInstance(stateType, All, null, new[] { actor }, null); Set(context, "Weapons", isolated);
        Set(actor, "__auditAttack", true); current.SetValue(null, context);
        try { copied.DynamicInvoke(actor, target, null, null, null); } finally { current.SetValue(null, null); Set(actor, "__auditAttack", false); }
        Check(ReferenceEquals(Field(stateType, "Current").GetValue(isolated), b), "future replicated attack animation blocked earlier fake selection");
    }

    private static object Accessor(Type owner, Type value, string name, string direction)
    {
        return addon.GetType("NativeMobPreviewAudit.FieldAccess", true).GetMethod(direction, All)
            .MakeGenericMethod(owner, value).Invoke(null, new object[] { Field(owner, name) });
    }
    private static void VisualAttackSelection()
    {
        Type humanoid = game.GetType("Humanoid", true), monster = game.GetType("MonsterAI", true);
        Type itemType = game.GetType("ItemDrop+ItemData", true), contextType = addon.GetType("NativeMobPreviewAudit.SteeringContext", true);
        Type planType = addon.GetType("NativeMobPreviewAudit.NativeSteeringPlan", true), adapters = addon.GetType("NativeMobPreviewAudit.NativeSteeringAdapters", true);
        object actor = New(humanoid), ai = New(monster), context = New(contextType), plan = New(planType);
        object inventory = New(game.GetType("Inventory", true));
        IList items = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        object weapon = Weapon(itemType, "bite", 3f, 0f, true); items.Add(weapon);
        Set(inventory, "m_inventory", items); Set(actor, "m_inventory", inventory); Set(actor, "m_rightItem", weapon);
        Type attackType = game.GetType("Attack", true); object attack = New(attackType);
        Set(attack, "m_attackAnimation", "bite"); Set(attack, "m_attackRandomAnimations", 3);
        Set(Field(itemType, "m_shared").GetValue(weapon), "m_attack", attack);
        Set(context, "Actor", actor); Set(context, "Ai", ai); Set(context, "Plan", plan);
        Set(context, "VisualAttacks", true); Set(context, "ShadowCanMove", true); Set(context, "NativeTime", 100f);
        Type stateType = addon.GetType("NativeMobPreviewAudit.MobBridgeWeaponState", true);
        Set(context, "Weapons", Activator.CreateInstance(stateType, All, null, new[] { actor }, null));
        Set(context, "attackAgeRead", Accessor(humanoid, typeof(float), "m_timeSinceLastAttack", "Reader"));
        Set(context, "attackAgeWrite", Accessor(humanoid, typeof(float), "m_timeSinceLastAttack", "Writer"));
        Type mapType = Field(contextType, "aiWrite").FieldType;
        IDictionary map = (IDictionary)Activator.CreateInstance(mapType);
        map.Add("m_timeSinceAttacking", Accessor(monster, typeof(float), "m_timeSinceAttacking", "Writer")); Set(context, "aiWrite", map);
        Type checker = Field(planType, "CanAttack").FieldType;
        var probe = new System.Reflection.Emit.DynamicMethod("AllowAttack", typeof(bool), new[] { game.GetType("BaseAI", true), itemType });
        var il = probe.GetILGenerator(); il.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_1); il.Emit(System.Reflection.Emit.OpCodes.Ret);
        Set(plan, "CanAttack", probe.CreateDelegate(checker));
        MethodInfo request = adapters.GetMethod("IgnoreAttack", All); FieldInfo current = Field(adapters, "Current");
        FieldInfo random = core.GetType("UnityEngine.Random", true).GetField("__auditCounter");
        current.SetValue(null, context);
        try
        {
            random.SetValue(null, 1);
            Check((bool)request.Invoke(null, new object[] { ai, null, false }), "visual attack request rejected valid native selection");
            Check((string)Field(contextType, "AttackTrigger").GetValue(context) == "bite1" && (int)random.GetValue(null) == 2,
                "visual variant differs from native Random.Range-selected trigger");
            Check(!(bool)request.Invoke(null, new object[] { ai, null, false }) && (int)random.GetValue(null) == 2,
                "active visual attack restarted or consumed another random draw");
            Set(context, "ShadowInAttack", false); Set(context, "ShadowAttackPending", true);
            Check(!(bool)request.Invoke(null, new object[] { ai, null, false }), "pending visual trigger was duplicated");
            Set(context, "ShadowAttackPending", false); Set(context, "ShadowCanMove", false);
            Check(!(bool)request.Invoke(null, new object[] { ai, null, false }), "frozen visual character started an attack");
            Set(context, "ShadowCanMove", true); Set(attack, "m_attackChainLevels", 3); Set(context, "NextAttackChain", 2);
            Set(actor, "m_timeSinceLastAttack", .1f);
            Check((bool)request.Invoke(null, new object[] { ai, null, false }) &&
                (string)Field(contextType, "AttackTrigger").GetValue(context) == "bite2" && (int)random.GetValue(null) == 2,
                "native chain precedence consumed random variant or lost chain index");
            Set(context, "ShadowInAttack", false); Set(actor, "m_timeSinceLastAttack", .3f);
            Check((bool)request.Invoke(null, new object[] { ai, null, false }) &&
                (string)Field(contextType, "AttackTrigger").GetValue(context) == "bite0", "expired chain did not restart at native chain zero");
            Check((float)Field(itemType, "m_lastAttackTime").GetValue(weapon) == 10f && ReferenceEquals(Field(humanoid, "m_rightItem").GetValue(actor), weapon),
                "visual attack modified real weapon cooldown/equipment");
        }
        finally { current.SetValue(null, null); }
    }

    private static void NativeMotorState()
    {
        Type humanoid = game.GetType("Humanoid", true);
        object actor = New(humanoid);
        Type native = addon.GetType("TooManyLags.MobBridgeNativeActor", true);
        FieldInfo[] fields = (FieldInfo[])native.GetMethod("StateFields", All).Invoke(null, null);
        object bodyA = New(Field(humanoid, "m_body").FieldType), bodyB = New(Field(humanoid, "m_body").FieldType);
        Set(actor, "m_body", bodyA); Set(actor, "m_flying", false); Set(actor, "m_swimTimer", 1f);
        Type snapshot = addon.GetType("NativeMobPreviewAudit.ShadowSnapshot`1", true).MakeGenericType(humanoid);
        object state = snapshot.GetMethod("Create", All).Invoke(null, new object[] { actor, fields, 0 });
        object other = New(humanoid);
        Set(other, "m_body", bodyB); Set(other, "m_flying", true); Set(other, "m_swimTimer", 3f);
        object second = snapshot.GetMethod("Create", All).Invoke(null, new object[] { other, fields, 0 });
        Set(other, "m_swimTimer", 8f);
        snapshot.GetMethod("CaptureReal", All).Invoke(second, null);
        try
        {
            snapshot.GetMethod("InstallShadow", All).Invoke(second, null);
            Check(ReferenceEquals(Field(humanoid, "m_body").GetValue(other), bodyB) &&
                (float)Field(humanoid, "m_swimTimer").GetValue(other) == 3f,
                "cached snapshot constructor reused another actor or captured values");
            Check(ReferenceEquals(Field(humanoid, "m_body").GetValue(actor), bodyA) &&
                (float)Field(humanoid, "m_swimTimer").GetValue(actor) == 1f,
                "constructing/installing another cached snapshot mutated the first actor");
        }
        finally { snapshot.GetMethod("RestoreReal", All).Invoke(second, null); }
        Check((float)Field(humanoid, "m_swimTimer").GetValue(other) == 8f, "cached snapshot failed to restore latest real state");
        bool duplicateRejected = false;
        try { snapshot.GetMethod("Create", All).Invoke(null, new object[] { actor, new[] { fields[0], fields[0] }, 0 }); }
        catch (TargetInvocationException error) { duplicateRejected = error.InnerException is NotSupportedException; }
        Check(duplicateRejected, "constructor cache bypassed per-snapshot duplicate field validation");
        for (int step = 0; step < 4; ++step)
        {
            Set(actor, "m_body", bodyB); Set(actor, "m_flying", false); Set(actor, "m_swimTimer", 9f);
            snapshot.GetMethod("CaptureReal", All).Invoke(state, null);
            try
            {
                snapshot.GetMethod("InstallShadow", All).Invoke(state, null);
                Check(ReferenceEquals(Field(humanoid, "m_body").GetValue(actor), bodyA), "native snapshot installed live body instead of private body");
                Check((bool)Field(humanoid, "m_flying").GetValue(actor) == (step > 0), "native flying shadow did not persist between motor ticks");
                Set(actor, "m_flying", true); Set(actor, "m_swimTimer", .1f);
                snapshot.GetMethod("CaptureShadow", All).Invoke(state, null);
                if (step == 3) throw new InvalidOperationException("injected motor failure");
            }
            catch (InvalidOperationException) { }
            finally { snapshot.GetMethod("RestoreReal", All).Invoke(state, null); }
            Check(ReferenceEquals(Field(humanoid, "m_body").GetValue(actor), bodyB) &&
                !(bool)Field(humanoid, "m_flying").GetValue(actor) && (float)Field(humanoid, "m_swimTimer").GetValue(actor) == 9f,
                "native motor transaction leaked shadow state into authoritative actor");
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            string sandbox = Path.Combine(Path.GetDirectoryName(typeof(BridgeWeaponNativeAudit).Assembly.Location), "weapon-native-seams"); Directory.CreateDirectory(sandbox);
            Prepare(args[1], sandbox);
            paths = new[] { sandbox, args[1], args[2], Path.GetDirectoryName(args[0]) }; AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            core = Assembly.LoadFrom(Path.Combine(sandbox, "UnityEngine.CoreModule.dll")); game = Assembly.LoadFrom(Path.Combine(sandbox, "assembly_valheim.dll")); addon = Assembly.LoadFrom(args[0]);
            FinallyChecks(); RunSelection(); NativeMotorState(); VisualAttackSelection();
            Console.WriteLine("PASS: " + checks + " native/fake weapon selection, random-call parity, isolated equipment/cooldowns and copied-finally checks.");
            Console.WriteLine("LIMIT: native selection IL with declared Unity clock/pose/random-sequence, attack-state, health/altitude and equipment seams; no Unity physics or live RNG execution.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
