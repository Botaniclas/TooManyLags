using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TooManyLags
{
    // Startup only. VCP keeps its Harmony prefixes when its official API turns
    // a fix off. FGN's presence check must distinguish these inert prefixes
    // from an active scheduler. Never change its other compatibility decisions.
    internal sealed class CommunityPatchSceneCompatibility : IDisposable
    {
        internal const string Id = "MidnightsFX.ValheimCommunityPatch";
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private const string Performance = "ValheimCommunityPatch.Patches.Performance.";
        private static CommunityPatchSceneCompatibility current;
        private readonly Assembly community, backend;
        private readonly Dictionary<MethodBase, HashSet<MethodInfo>> targets = new Dictionary<MethodBase, HashSet<MethodInfo>>();
        private readonly Harmony harmony = new Harmony(Plugin.Id + ".communitypatch");
        private MethodInfo predicate;
        private Func<MethodBase, string, bool> nativePresence;

        private CommunityPatchSceneCompatibility(Assembly backend, Assembly community)
        { this.backend = backend; this.community = community; }

        internal static CommunityPatchSceneCompatibility Start(Assembly backend, Assembly community)
        {
            if (community == null) return null;
            if (!ReferenceEquals(ZNet.instance, null) || !ReferenceEquals(ZNetScene.instance, null))
                throw new NotSupportedException("CommunityPatch simulation compatibility must be selected before loading a world; restart the server.");
            Type detection = backend.GetType("FiresGhettoNetworkMod.ServerClientUtils", true);
            PropertyInfo dedicated = detection.GetProperty("IsDedicatedServerDetected", Static);
            if (dedicated == null || dedicated.PropertyType != typeof(bool) || !(bool)dedicated.GetValue(null, null))
                throw new NotSupportedException("CommunityPatch simulation compatibility is dedicated-server only.");
            if (current != null)
            {
                if (current.backend != backend || current.community != community)
                    throw new NotSupportedException("CommunityPatch simulation compatibility is already bound to another plugin.");
                return current;
            }
            var bridge = new CommunityPatchSceneCompatibility(backend, community);
            Type compatibility = backend.GetType("FiresGhettoNetworkMod.ValheimCommunityPatchCompat", true);
            MethodInfo predicate = Method(compatibility, "HasPrefixFrom", typeof(bool), typeof(MethodBase), typeof(string));
            MethodInfo detect = Method(compatibility, "Detect", typeof(void));
            bridge.predicate = predicate;
            bridge.nativePresence = (Func<MethodBase, string, bool>)Delegate.CreateDelegate(typeof(Func<MethodBase, string, bool>), predicate);
            MethodInfo create = AccessTools.DeclaredMethod(typeof(ZNetScene), "CreateDestroyObjects", Type.EmptyTypes);
            MethodInfo sorted = AccessTools.DeclaredMethod(typeof(ZNetScene), "CreateObjectsSorted");
            MethodInfo remove = AccessTools.DeclaredMethod(typeof(ZNetScene), "RemoveObjects", new[] { typeof(List<ZDO>), typeof(List<ZDO>) });
            if (create == null || sorted == null || remove == null)
                throw new NotSupportedException("Native scene scheduling contract changed.");
            var installed = new List<KeyValuePair<MethodBase, MethodInfo>>();
            foreach (MethodBase target in new[] { create, sorted, remove })
            {
                Patches patches = Harmony.GetPatchInfo(target);
                if (patches == null) continue;
                foreach (Patch patch in patches.Prefixes)
                    if (patch.owner == Id) installed.Add(new KeyValuePair<MethodBase, MethodInfo>(target, patch.PatchMethod));
            }
            if (installed.Count == 0) return null;
            bool usesPredicate = false;
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(detect))
                if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, predicate)) usesPredicate = true;
            if (!usesPredicate) throw new NotSupportedException("FiresGhettoNetworking's scene detection contract changed.");

            string[] fixes = { "SceneIdleSkipPatch", "SpawnQueueCachePatch", "ZoneDiffRemovalPatch", "RemoveSweepPacingPatch" };
            MethodInfo[] prefixes = {
                Method(community.GetType(Performance + fixes[0], true), "CreateDestroyObjectsPrefix", typeof(bool), typeof(ZNetScene)),
                Method(community.GetType(Performance + fixes[1], true), "CreateObjectsSortedPrefix", typeof(bool), typeof(ZNetScene), typeof(List<ZDO>), typeof(int), typeof(int).MakeByRefType(), typeof(bool)),
                Method(community.GetType(Performance + fixes[2], true), "RemoveObjectsPrefix", typeof(bool), typeof(ZNetScene), typeof(List<ZDO>), typeof(List<ZDO>), typeof(bool)),
                Method(community.GetType(Performance + fixes[3], true), "RemoveObjectsPrefix", typeof(bool))
            };
            // Unlike the schedulers, this guard stamps the near/distant lists
            // supplied by FGN and runs vanilla removal with orphan recovery.
            // It never reconstructs a ring around the headless reference point.
            MethodInfo recovery = Method(community.GetType("ValheimCommunityPatch.Patches.Correctness.RemoveObjectsNrePatch", true),
                "RemoveObjectsPrefix", typeof(bool), typeof(ZNetScene), typeof(List<ZDO>), typeof(List<ZDO>), typeof(bool));
            bridge.targets.Add(create, new HashSet<MethodInfo> { prefixes[0] });
            bridge.targets.Add(sorted, new HashSet<MethodInfo> { prefixes[1] });
            bridge.targets.Add(remove, new HashSet<MethodInfo> { prefixes[2], prefixes[3], recovery });
            foreach (var patch in installed)
            {
                if (patch.Value.Module.Assembly != community || !bridge.targets[patch.Key].Contains(patch.Value))
                    throw new NotSupportedException("Unknown CommunityPatch scene prefix; FiresGhettoNetworking's safety block remains active.");
            }

            Type api = community.GetType("ValheimCommunityPatch.APIReceiver", true);
            var version = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), Method(api, "GetApiVersion", typeof(int)));
            var state = (Func<string, int>)Delegate.CreateDelegate(typeof(Func<string, int>), Method(api, "GetFixState", typeof(int), typeof(string)));
            var canDisable = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), Method(api, "CanDisableFix", typeof(bool), typeof(string)));
            var disable = (Func<string, string, bool>)Delegate.CreateDelegate(typeof(Func<string, string, bool>), Method(api, "DisableFix", typeof(bool), typeof(string), typeof(string)));
            if (version() < 1) throw new NotSupportedException("CommunityPatch's fix-control API is unavailable.");
            // Preflight the complete set before any irreversible session switch.
            foreach (string fix in fixes)
                if (!canDisable(fix) || !KnownState(state(fix)))
                    throw new NotSupportedException("CommunityPatch cannot hand over scene scheduling: " + fix);
            if (!KnownState(state("RemoveObjectsNrePatch")))
                throw new NotSupportedException("CommunityPatch's object-unload recovery contract is unavailable.");

            foreach (string fix in fixes)
            {
                if (!disable(fix, "TooManyLags uses FiresGhettoNetworking's dedicated-server scene scheduling; the object-unload crash-recovery setting is preserved."))
                    throw new NotSupportedException("CommunityPatch refused the scheduling handover: " + fix);
                int status = state(fix);
                if (status != 2 && status != 3 && status != 4)
                    throw new NotSupportedException("CommunityPatch scene scheduler is not inactive: " + fix);
            }
            current = bridge;
            try
            {
                // Recompile Detect's call sites, rather than detouring the small
                // predicate that an earlier native startup may have inlined.
                bridge.harmony.Patch(detect, transpiler: new HarmonyMethod(typeof(CommunityPatchSceneCompatibility), "RewriteDetection"));
                return bridge;
            }
            catch { bridge.Dispose(); throw; }
        }

        private static bool KnownState(int value) { return value >= 1 && value <= 4; }
        private static MethodInfo Method(Type type, string name, Type result, params Type[] args)
        {
            MethodInfo method = type.GetMethod(name, Static, null, args, null);
            if (method == null || method.ReturnType != result)
                throw new NotSupportedException("CommunityPatch scene compatibility contract changed: " + type.FullName + "." + name);
            return method;
        }

        private static IEnumerable<CodeInstruction> RewriteDetection(IEnumerable<CodeInstruction> source)
        {
            int changed = 0;
            foreach (CodeInstruction instruction in source)
            {
                var copy = new CodeInstruction(instruction);
                if (copy.opcode == OpCodes.Call && Equals(copy.operand, current.predicate))
                { copy.operand = AccessTools.Method(typeof(CommunityPatchSceneCompatibility), "HasActivePrefix"); changed++; }
                yield return copy;
            }
            if (changed == 0) throw new NotSupportedException("FiresGhettoNetworking's scene detection no longer uses the expected predicate.");
        }

        private static bool HasActivePrefix(MethodBase target, string patchTypeName)
        {
            var bridge = current;
            bool found = bridge.nativePresence(target, patchTypeName);
            HashSet<MethodInfo> allowed;
            if (!found || patchTypeName != null || !bridge.targets.TryGetValue(target, out allowed)) return found;
            Patches patches = Harmony.GetPatchInfo(target);
            if (patches == null) return found;
            foreach (Patch patch in patches.Prefixes)
                if (patch.owner == Id && (patch.PatchMethod.Module.Assembly != bridge.community || !allowed.Contains(patch.PatchMethod))) return true;
            return false;
        }

        public void Dispose()
        {
            if (!ReferenceEquals(current, this)) return;
            // Harmony rebuilds surviving transpilers during unpatching. Keep
            // their startup context alive until this transpiler is removed.
            harmony.UnpatchSelf(); current = null;
            // VCP's API stand-down is session-long. Never reactivate a scheduler
            // that stopped tracking the world; restart restores its normal setup.
        }
    }
}
