using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TooManyLags.PupCompatibility
{
    // Read the same verified native completion state without Mathf.Max(params)
    // arrays. Used only for extra statics/destination queries, not native actors.
    internal abstract class CellReadiness
    {
        internal abstract bool Ready(Vector3 point, float radius, bool dedicatedProved = false);
        internal static CellReadiness Create(MonoBehaviour pup)
        {
            Type type = pup.GetType();
            Type remote = type.GetField("multiplayerRemoteCells", BindingFlags.Instance | BindingFlags.NonPublic).FieldType.GetGenericArguments()[1];
            Type fast = type.GetField("fastColliderCells", BindingFlags.Instance | BindingFlags.NonPublic).FieldType.GetGenericArguments()[1];
            Type row = type.GetField("records", BindingFlags.Instance | BindingFlags.NonPublic).FieldType.GetGenericArguments()[1].GetGenericArguments()[0];
            return (CellReadiness)Activator.CreateInstance(typeof(TypedCellReadiness<,,>).MakeGenericType(remote, fast, row), new object[] { pup });
        }
    }
    internal sealed class TypedCellReadiness<R, F, B> : CellReadiness where R : class where F : class
    {
        private readonly MonoBehaviour pup;
        private readonly Func<object, Dictionary<Vector2Int, List<B>>> records;
        private readonly Func<object, Dictionary<Vector2Int, R>> remotes;
        private readonly Func<object, Dictionary<Vector2Int, F>> fasts;
        private readonly Func<object, Dictionary<Vector2Int, GameObject>> roots;
        private readonly Func<object, Dictionary<Vector2Int, string>> ids;
        private readonly Func<object, Dictionary<GameObject, string>> validated;
        private readonly Func<object, Dictionary<Vector2Int, bool>> active;
        private readonly Func<object, Dictionary<Vector2Int, HashSet<string>>> loaded;
        private readonly Func<object, HashSet<Vector2Int>> physics, verified, worldRestores, vanillaRestores, loading;
        private readonly Func<object, int> queued;
        private readonly Func<R, string> remoteId;
        private readonly Func<F, string> fastId;
        private readonly Func<F, GameObject> fastRoot;
        private readonly Func<F, HashSet<string>> required;
        private readonly Func<Vector2Int, bool> pending;
        private readonly Func<Vector2Int, Vector3, float, bool> dedicatedPrepared;
        public TypedCellReadiness(MonoBehaviour value)
        {
            pup = value; Type type = pup.GetType();
            records = Read<Dictionary<Vector2Int, List<B>>>(type, "records");
            remotes = Read<Dictionary<Vector2Int, R>>(type, "multiplayerRemoteCells");
            fasts = Read<Dictionary<Vector2Int, F>>(type, "fastColliderCells");
            roots = Read<Dictionary<Vector2Int, GameObject>>(type, "clusters");
            ids = Read<Dictionary<Vector2Int, string>>(type, "cacheIds");
            validated = Read<Dictionary<GameObject, string>>(type, "validatedReplacementCacheIds");
            active = Read<Dictionary<Vector2Int, bool>>(type, "colliderCellActive");
            loaded = Read<Dictionary<Vector2Int, HashSet<string>>>(type, "fastLoadedSubclusters");
            physics = Read<HashSet<Vector2Int>>(type, "multiplayerRemotePhysicsReady");
            verified = Read<HashSet<Vector2Int>>(type, "verifiedRemoteReplacements");
            worldRestores = Read<HashSet<Vector2Int>>(type, "pendingWorldSaveRestores");
            vanillaRestores = Read<HashSet<Vector2Int>>(type, "multiplayerPendingVanillaRestore");
            loading = Read<HashSet<Vector2Int>>(type, "loadingCells");
            queued = Read<int>(type, "queuedColliderCount");
            remoteId = FieldReader.Create<R, string>(Field(typeof(R), "cacheId"));
            fastId = FieldReader.Create<F, string>(Field(typeof(F), "cacheId"));
            fastRoot = FieldReader.Create<F, GameObject>(Field(typeof(F), "root"));
            required = FieldReader.Create<F, HashSet<string>>(Field(typeof(F), "required"));
            pending = (Func<Vector2Int, bool>)Delegate.CreateDelegate(typeof(Func<Vector2Int, bool>), pup,
                type.GetMethod("HasPendingCollidersForCell", BindingFlags.Instance | BindingFlags.NonPublic));
            MethodInfo prepared = type.GetMethod("HasPreparedDedicatedCollision", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Vector2Int), typeof(Vector3), typeof(float) }, null);
            if (prepared != null)
                dedicatedPrepared = (Func<Vector2Int, Vector3, float, bool>)Delegate.CreateDelegate(typeof(Func<Vector2Int, Vector3, float, bool>), pup, prepared);
        }
        private static FieldInfo Field(Type type, string name)
        { return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
        private static Func<object, T> Read<T>(Type type, string name)
        { return FieldReader.Create<object, T>(Field(type, name)); }
        internal override bool Ready(Vector3 point, float radius, bool dedicatedProved = false)
        {
            int minX = Mathf.FloorToInt((point.x - radius) / 64f), maxX = Mathf.FloorToInt((point.x + radius) / 64f);
            int minZ = Mathf.FloorToInt((point.z - radius) / 64f), maxZ = Mathf.FloorToInt((point.z + radius) / 64f);
            Dictionary<Vector2Int, R> remoteCells = remotes(pup);
            Dictionary<Vector2Int, GameObject> clusters = roots(pup);
            Dictionary<Vector2Int, bool> flags = active(pup);
            for (int x = minX; x <= maxX; x++) for (int z = minZ; z <= maxZ; z++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(x * 64f - point.x, point.x - (x + 1) * 64f));
                float dz = Mathf.Max(0f, Mathf.Max(z * 64f - point.z, point.z - (z + 1) * 64f));
                if (dx * dx + dz * dz > radius * radius) continue;
                Vector2Int cell = new Vector2Int(x, z); R manifest;
                bool remote = remoteCells.TryGetValue(cell, out manifest);
                List<B> rows; string recipeId;
                bool local = records(pup).TryGetValue(cell, out rows);
                if (!remote && !local) continue;
                // Exact's pending check alone does not reject a local recipe
                // without a cache identifier, or a world-save restore gap.
                if (worldRestores(pup).Contains(cell) || vanillaRestores(pup).Contains(cell) ||
                    !local || rows == null || rows.Count == 0 || !ids(pup).TryGetValue(cell, out recipeId) ||
                    String.IsNullOrEmpty(recipeId) || recipeId == "RESTORE_PENDING" || (remote && recipeId != remoteId(manifest))) return false;
                // ReadyPoint already checks exact recipe/proxy preparation and
                // transitions. A dedicated local cell is covered by that proof.
                GameObject root; bool enabled;
                if (remote)
                {
                    string id;
                    if (!physics(pup).Contains(cell) || !verified(pup).Contains(cell) ||
                        !clusters.TryGetValue(cell, out root) || !root || !root.activeInHierarchy ||
                        !validated(pup).TryGetValue(root, out id) || id != remoteId(manifest)) return false;
                }
                else if (ZNet.instance.IsDedicated())
                {
                    // New native dedicated guards also prove prepared proxy
                    // coverage. Exact preparation alone must not replace that proof.
                    if (!dedicatedProved && dedicatedPrepared != null && !dedicatedPrepared(cell, point, radius)) return false;
                    continue;
                }
                else
                {
                    // Cells without local records/clusters are irrelevant.
                    if (!ids(pup).ContainsKey(cell) && !clusters.ContainsKey(cell)) continue;
                    F fast;
                    if (fasts(pup).TryGetValue(cell, out fast))
                    {
                        root = fastRoot(fast); string id; HashSet<string> parts;
                        if (!root || !root.activeInHierarchy || !ids(pup).TryGetValue(cell, out id) || id != fastId(fast) || !loaded(pup).TryGetValue(cell, out parts)) return false;
                        foreach (string part in required(fast)) if (!parts.Contains(part)) return false;
                    }
                    else if (!clusters.TryGetValue(cell, out root) || !root || !root.activeInHierarchy || loading(pup).Contains(cell) || (queued(pup) > 0 && pending(cell))) return false;
                }
                if (flags.TryGetValue(cell, out enabled) && !enabled) return false;
            }
            return true;
        }
    }
}
