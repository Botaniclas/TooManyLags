using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using UnityEngine;

namespace TooManyLags
{
    // Optional direct PUP readiness adapter. No physics/support hooks or guards.
    // Construct with null only when PUP is absent; validation failure leaves the
    // plugin's adapter null and therefore denies optional server fallback.
    internal sealed class NativePup
    {
        internal const string Hash = "47DEADE1F80117821462E69D95752F666AFF6AA8752988852D0315B0E4ABA04B";
        private readonly MonoBehaviour pup;
        private readonly Func<object, bool> manifest, rejected;
        private readonly Func<object, ZNet> loaded;
        private readonly Func<Vector2Int, bool> records, remote, restores;
        private readonly Func<Vector3, float, bool> exact, transition;
        private readonly CellReadiness readiness;
        private readonly Func<int> restoreCount, transitionCount;

        internal NativePup(MonoBehaviour instance)
        {
            pup = instance;
            if (ReferenceEquals(instance, null)) return;
            Type type = instance.GetType();
#if !TESTING
            using (SHA256 sha = SHA256.Create())
            using (Stream input = File.OpenRead(type.Assembly.Location))
                if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "") != Hash)
                    throw new NotSupportedException("PUP readiness requires the verified PUP FPS 1.0.34 DLL.");
#endif
            manifest = FieldReader.Create<object, bool>(Field(type, "multiplayerManifestReceived"));
            rejected = FieldReader.Create<object, bool>(Field(type, "multiplayerRejected"));
            loaded = FieldReader.Create<object, ZNet>(Field(type, "loadedZNet"));
            records = Contains(type, "records"); remote = Contains(type, "multiplayerRemoteCells");
            restores = Contains(type, "restoreSupportCells");
            restoreCount = Count(type, "restoreSupportCells"); transitionCount = Count(type, "zoneTransitions");
            exact = Bind<Func<Vector3, float, bool>>(type, "PrepareExactCollision", pup);
            transition = Bind<Func<Vector3, float, bool>>(type, "IsZoneTransitionNear", pup);
            readiness = CellReadiness.Create(pup);
        }

        internal bool Present { get { return !ReferenceEquals(pup, null); } }
        internal bool Active { get { return pup && pup.isActiveAndEnabled && ZNet.instance && !ZNet.IsSinglePlayer; } }
        internal bool SessionReady { get { return !Active || (ZNet.instance.IsServer() ?
            ReferenceEquals(loaded(pup), ZNet.instance) : manifest(pup) || rejected(pup)); } }

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
            catch (Exception error) { Plugin.Warn("PUP collision readiness pending: " + error.Message); return false; }
        }
        private bool ReadyPointCore(Vector3 point, float radius)
        {
            if (!Present) return true;
            if (!Active || !SessionReady || !Finite(point.x) || !Finite(point.y) || !Finite(point.z) ||
                !Finite(radius) || radius < 0f || radius > 56f || Mathf.Abs(point.x) > 100000000f || Mathf.Abs(point.z) > 100000000f) return false;
            if ((transitionCount() != 0 || restoreCount() != 0) && transition(point, radius)) return false;
            if (!Relevant(point, radius)) return true;
            // Native dedicated readiness skips local record cells. Exact also
            // prepares/verifies their physics proxies and renews native retention.
            return exact(point, radius) && readiness.Ready(point, radius);
        }

        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
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
            DynamicMethod method = new DynamicMethod("TooManyLagsNativeContains_" + name, typeof(bool),
                new[] { type, typeof(Vector2Int) }, typeof(NativePup).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, contains); il.Emit(OpCodes.Ret);
            return (Func<Vector2Int, bool>)method.CreateDelegate(typeof(Func<Vector2Int, bool>), pup);
        }
        private Func<int> Count(Type type, string name)
        {
            FieldInfo field = Field(type, name);
            DynamicMethod method = new DynamicMethod("TooManyLagsNativeCount_" + name, typeof(int), new[] { type }, typeof(NativePup).Module, true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Callvirt, field.FieldType.GetProperty("Count").GetGetMethod()); il.Emit(OpCodes.Ret);
            return (Func<int>)method.CreateDelegate(typeof(Func<int>), pup);
        }
    }
}
