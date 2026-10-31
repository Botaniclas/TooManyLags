using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.PupCompatibility
{
    // Bind native contracts once. No version/hash lock or per-tick reflection.
    internal sealed class NativePup
    {
        private readonly MonoBehaviour pup;
        private readonly Func<object, bool> manifest, rejected;
        private readonly Func<object, ZNet> loaded;
        private readonly Func<Vector2Int, bool> records, remote, restores;
        private readonly Func<Vector3, float, bool> exact, transition;
        private readonly CellReadiness readiness;
        private readonly Func<GameObject, Component> attach;
        private readonly Func<Component, bool> step;
        private readonly Action<Component, int> invalidate;
        private readonly Func<object, int> coverage, zones;
        private readonly Func<int> recordCount, remoteCount, restoreCount, transitionCount;
        private readonly Func<object, System.Collections.Generic.HashSet<Vector2Int>> activeBodies;
        internal readonly Type GuardType;
        internal readonly MethodInfo ReadyMethod, MobMethod, PreserveMethod, RepairMethod, RetainMethod;
        internal readonly bool NativeDedicatedReadiness, NativeBodyRetention;
        [ThreadStatic] internal static bool ForceWait;

        internal NativePup(MonoBehaviour instance)
        {
            pup = instance;
            Type type = instance.GetType();
            manifest = FieldReader.Create<object, bool>(Field(type, "multiplayerManifestReceived"));
            rejected = FieldReader.Create<object, bool>(Field(type, "multiplayerRejected"));
            loaded = FieldReader.Create<object, ZNet>(Field(type, "loadedZNet"));
            coverage = FieldReader.Create<object, int>(Field(type, "collisionCoverageRevision"));
            zones = FieldReader.Create<object, int>(Field(type, "zoneGuardRevision"));
            records = Contains(type, "records"); remote = Contains(type, "multiplayerRemoteCells");
            restores = Contains(type, "restoreSupportCells");
            recordCount = Count(type, "records"); remoteCount = Count(type, "multiplayerRemoteCells");
            restoreCount = Count(type, "restoreSupportCells"); transitionCount = Count(type, "zoneTransitions");
            exact = Bind<Func<Vector3, float, bool>>(type, "PrepareExactCollision", pup);
            transition = Bind<Func<Vector3, float, bool>>(type, "IsZoneTransitionNear", pup);
            readiness = CellReadiness.Create(pup);
            attach = Bind<Func<GameObject, Component>>(type, "AttachZoneBodyGuard", pup);
            ReadyMethod = Method(type, "IsZoneReplacementReady");
            MobMethod = Method(type, "PrepareMobCollision");
            PreserveMethod = Method(type, "PreserveDependentSupport");
            RepairMethod = Method(type, "RepairDependentSupport");
            GuardType = type.GetNestedType("ZoneBodyGuard", BindingFlags.Public | BindingFlags.NonPublic);
            if (GuardType == null || !typeof(Component).IsAssignableFrom(GuardType))
                throw new MissingMemberException("PUP.ZoneBodyGuard");
            step = Open<Func<Component, bool>>(Method(GuardType, "Step"));
            activeBodies = FieldReader.Create<object, System.Collections.Generic.HashSet<Vector2Int>>(Field(type, "activeBodyCollisionCells"));
            RetainMethod = Method(type, "ShouldRetainActorCollisionCell");
            FieldInfo bodyCells = Field(type, "activeBodyCollisionCells");
            NativeBodyRetention = ReadsField(RetainMethod, bodyCells) && RetainsBody(RetainMethod, bodyCells);
            MethodInfo prepared = type.GetMethod("HasPreparedDedicatedCollision", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Vector2Int), typeof(Vector3), typeof(float) }, null);
            NativeDedicatedReadiness = prepared != null && prepared.ReturnType == typeof(bool) && Calls(ReadyMethod, prepared);
#if !TESTING
            if (!NativeDedicatedReadiness && !LegacyDedicatedSkip(ReadyMethod))
                throw new NotSupportedException("PUP dedicated readiness contract changed.");
#endif
            // The original Step remains the owner of all freeze/release writes.
            MethodInfo nativeStep = Method(GuardType, "Step");
            if (!Calls(nativeStep, ReadyMethod) || !Calls(nativeStep, typeof(Rigidbody).GetProperty("isKinematic").GetSetMethod()))
                throw new NotSupportedException("PUP native body readiness/freeze contract changed.");
            FieldInfo revision = Field(GuardType, "coverage");
            DynamicMethod setter = new DynamicMethod("BotanicInvalidateNativeGuard", typeof(void),
                new[] { typeof(Component), typeof(int) }, typeof(NativePup).Module, true);
            ILGenerator il = setter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, GuardType); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, revision); il.Emit(OpCodes.Ret);
            invalidate = (Action<Component, int>)setter.CreateDelegate(typeof(Action<Component, int>));
        }

        internal bool Active { get { return pup && pup.isActiveAndEnabled && ZNet.instance && !ZNet.IsSinglePlayer; } }
        internal bool SessionReady { get { return !Active || (ZNet.instance.IsServer() ?
            ReferenceEquals(loaded(pup), ZNet.instance) : manifest(pup) || rejected(pup)); } }
        internal int Coverage { get { return coverage(pup); } }
        internal int Zones { get { return zones(pup); } }
        internal bool Empty { get { return SessionReady && recordCount() == 0 && remoteCount() == 0 && restoreCount() == 0 && transitionCount() == 0; } }

        internal bool Relevant(Vector3 point, float radius)
        {
            if (!Finite(point.x) || !Finite(point.z) || !Finite(radius) || radius < 0f || radius > 100000000f ||
                Mathf.Abs(point.x) > 100000000f || Mathf.Abs(point.z) > 100000000f) return true;
            int minX = Mathf.FloorToInt((point.x - radius) / 64f), maxX = Mathf.FloorToInt((point.x + radius) / 64f);
            int minZ = Mathf.FloorToInt((point.z - radius) / 64f), maxZ = Mathf.FloorToInt((point.z + radius) / 64f);
            if ((long)maxX - minX > 64 || (long)maxZ - minZ > 64) return true;
            for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    if (records(cell) || remote(cell) || restores(cell)) return true;
                }
            return false;
        }

        internal bool ReadyPoint(Vector3 point, float radius)
        {
            try { return ReadyPointCore(point, radius); }
            catch (Exception error) { Plugin.Warn("Native collision readiness pending: " + error.Message); return false; }
        }
        internal bool PreparedPoint(Vector3 point, float radius, bool nativeProved)
        {
            try { return ReadyPointCore(point, radius, false, nativeProved); }
            catch (Exception error) { Plugin.Warn("Native collision readiness pending: " + error.Message); return false; }
        }
        private bool ReadyPointCore(Vector3 point, float radius, bool prepare = true, bool nativeProved = false)
        {
            if (!Active) return true;
            if (!SessionReady || !Finite(point.x) || !Finite(point.y) || !Finite(point.z) ||
                !Finite(radius) || radius < 0f || radius > 56f || Mathf.Abs(point.x) > 100000000f || Mathf.Abs(point.z) > 100000000f) return false;
            if ((transitionCount() != 0 || restoreCount() != 0) && transition(point, radius)) return false;
            if (!Relevant(point, radius)) return true;
            // Native dedicated readiness skips its local record cells: Exact also
            // prepares/verifies their physics proxies and renews native retention.
            return (!prepare || exact(point, radius)) && readiness.Ready(point, radius, nativeProved);
        }

        internal bool ReadyBounds(Bounds bounds)
        {
            Vector3 center = bounds.center, extents = bounds.extents;
            float radius = extents.magnitude + 0.1f;
            if (radius <= 56f) return ReadyPoint(center, radius);
            if (!Finite(radius) || extents.x < 0f || extents.y < 0f || extents.z < 0f ||
                bounds.size.x > 131072f || bounds.size.z > 131072f) return false;
            // The native readiness query only covers a 3x3 cell neighborhood.
            // Tile exceptionally large modded bounds instead of truncating them.
            int nx = Mathf.CeilToInt(bounds.size.x / 32f), nz = Mathf.CeilToInt(bounds.size.z / 32f);
            nx = Mathf.Max(1, nx); nz = Mathf.Max(1, nz);
            if ((long)nx * nz > 4096) return false;
            Vector3 min = bounds.min;
            for (int x = 0; x < nx; x++) for (int z = 0; z < nz; z++)
            {
                float sx = Mathf.Min(32f, bounds.size.x - x * 32f), sz = Mathf.Min(32f, bounds.size.z - z * 32f);
                Vector3 point = new Vector3(min.x + x * 32f + sx * 0.5f, center.y, min.z + z * 32f + sz * 0.5f);
                if (!ReadyPoint(point, Mathf.Sqrt(sx * sx + sz * sz) * 0.5f + 0.1f)) return false;
            }
            return true;
        }

        internal Component Existing(GameObject root) { return root ? root.GetComponent(GuardType) : null; }
        internal Component Attach(GameObject root) { return root ? attach(root) : null; }
        internal bool ForceStep(Component guard, bool wait)
        {
            if (!guard) return false;
            bool previous = ForceWait;
            try { ForceWait = previous || wait; invalidate(guard, -1); return step(guard); }
            finally { ForceWait = previous; }
        }
        internal bool NeededByNativeBody(Vector2Int cell) { return activeBodies(pup).Contains(cell); }
        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        private static bool ReadsField(MethodInfo method, FieldInfo field)
        {
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
                if (instruction.opcode == OpCodes.Ldfld && field.Equals(instruction.operand)) return true;
            return false;
        }
        private static bool Calls(MethodInfo method, MethodInfo target)
        {
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && target.Equals(instruction.operand)) return true;
            return false;
        }
        private static bool RetainsBody(MethodInfo method, FieldInfo field)
        {
            var body = PatchProcessor.GetOriginalInstructions(method);
            MethodInfo contains = field.FieldType.GetMethod("Contains", new[] { typeof(Vector2Int) });
            for (int i = 0; i + 3 < body.Count; i++)
            {
                if (body[i].opcode != OpCodes.Ldfld || !field.Equals(body[i].operand) ||
                    body[i + 1].opcode != OpCodes.Ldarg_1 || !contains.Equals(body[i + 2].operand)) continue;
                CodeInstruction next = body[i + 3];
                if (next.opcode == OpCodes.Ret) return true;
                if ((next.opcode != OpCodes.Brtrue && next.opcode != OpCodes.Brtrue_S) || !(next.operand is Label)) return false;
                Label label = (Label)next.operand;
                for (int j = i + 4; j + 1 < body.Count; j++)
                    if (body[j].labels.Contains(label)) return body[j].opcode == OpCodes.Ldc_I4_1 && body[j + 1].opcode == OpCodes.Ret;
                return false;
            }
            return false;
        }
        private static bool LegacyDedicatedSkip(MethodInfo method)
        {
            // Recognize the old direct jump to the inner cell-loop increment.
            // A future unknown dedicated proof must not be replaced as legacy.
            var body = PatchProcessor.GetOriginalInstructions(method);
            MethodInfo dedicated = typeof(ZNet).GetMethod("IsDedicated", Type.EmptyTypes);
            for (int i = 0; i + 1 < body.Count; i++)
            {
                if (!dedicated.Equals(body[i].operand)) continue;
                CodeInstruction branch = body[i + 1];
                if ((branch.opcode != OpCodes.Brtrue && branch.opcode != OpCodes.Brtrue_S) || !(branch.operand is Label)) return false;
                Label label = (Label)branch.operand;
                for (int j = i + 2; j + 2 < body.Count; j++)
                    if (body[j].labels.Contains(label))
                        return body[j].opcode.Name.StartsWith("ldloc", StringComparison.Ordinal) &&
                            body[j + 1].opcode == OpCodes.Ldc_I4_1 && body[j + 2].opcode == OpCodes.Add;
                return false;
            }
            return false;
        }
        private static FieldInfo Field(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static MethodInfo Method(Type type, string name)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }
        private static T Bind<T>(Type type, string name, object target) where T : class
        { return (T)(object)Delegate.CreateDelegate(typeof(T), target, Method(type, name)); }
        private Func<Vector2Int, bool> Contains(Type type, string name)
        {
            FieldInfo field = Field(type, name);
            MethodInfo contains = field.FieldType.GetMethod("ContainsKey", new[] { typeof(Vector2Int) });
            if (contains == null) throw new MissingMethodException(field.FieldType.FullName, "ContainsKey");
            DynamicMethod method = new DynamicMethod("BotanicNativeContains_" + name, typeof(bool),
                new[] { type, typeof(Vector2Int) }, typeof(NativePup).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, contains); il.Emit(OpCodes.Ret);
            return (Func<Vector2Int, bool>)method.CreateDelegate(typeof(Func<Vector2Int, bool>), pup);
        }
        private Func<int> Count(Type type, string name)
        {
            FieldInfo field = Field(type, name);
            DynamicMethod method = new DynamicMethod("BotanicNativeCount_" + name, typeof(int), new[] { type }, typeof(NativePup).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Callvirt, field.FieldType.GetProperty("Count").GetGetMethod()); il.Emit(OpCodes.Ret);
            return (Func<int>)method.CreateDelegate(typeof(Func<int>), pup);
        }
        private static T Open<T>(MethodInfo target) where T : class
        {
            DynamicMethod method = new DynamicMethod("BotanicNative_" + target.Name, target.ReturnType,
                new[] { typeof(Component) }, typeof(NativePup).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, target.DeclaringType);
            il.Emit(OpCodes.Call, target); il.Emit(OpCodes.Ret);
            return (T)(object)method.CreateDelegate(typeof(T));
        }
    }
}
