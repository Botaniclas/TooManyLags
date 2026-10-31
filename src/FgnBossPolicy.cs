using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Only authoritative player-target events evaluate distance. Weak ZDO keys
    // retain no unloaded world areas; owner changes preserve a same-target decision.
    internal static class FgnBossPolicy
    {
        private sealed class Decision { internal ZDOID Target; internal bool Server; }
        private static ConditionalWeakTable<ZDO, Decision> decisions = new ConditionalWeakTable<ZDO, Decision>();
        private static bool configured;
        private static float radiusSquared;
#if !TML_RELEASE || TML_TEST_METRICS
        internal static int Evaluations;
#endif
        internal static void Configure(bool enabled, float radius)
        { configured = enabled; radiusSquared = radius * radius; }
        internal static void Reset()
        {
            decisions = new ConditionalWeakTable<ZDO, Decision>();
#if !TML_RELEASE || TML_TEST_METRICS
            Evaluations = 0;
#endif
        }
        internal static bool IsBoss(ZDO data)
        { Character actor = FgnCombatOwnership.CreatureActor(data); return actor && actor.IsBoss(); }
        internal static void Record(ZDO data, ZDOID target)
        {
            if (!configured || !FgnCombatOwnership.Enabled || !ZNet.instance || !ZNet.instance.IsDedicated() ||
                data == null || !data.IsValid() || !IsBoss(data)) return;
            Decision decision;
            if (decisions.TryGetValue(data, out decision) && decision.Target == target) return;
            if (decision == null) { decision = new Decision(); decisions.Add(data, decision); }
            decision.Target = target;
            decision.Server = !target.IsNone() && FgnCombatOwnership.Fgn.Simulation() && TwoNearby(data);
        }
        internal static bool WantsServer(ZDO data)
        {
            Decision decision;
            return configured && FgnCombatOwnership.Fgn.Simulation() && data != null &&
                decisions.TryGetValue(data, out decision) && decision.Server;
        }
        private static bool TwoNearby(ZDO data)
        {
#if !TML_RELEASE || TML_TEST_METRICS
            Evaluations++;
#endif
            int count = 0; Vector3 origin = data.GetPosition();
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (!FgnCombatOwnership.Candidate(peer, origin) || peer.m_characterID.IsNone()) continue;
                ZDO player = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (player == null || !player.IsValid() || player.GetOwner() != peer.m_uid ||
                    player.GetFloat(ZDOVars.s_health, 1f) <= 0f) continue;
                if ((player.GetPosition() - origin).sqrMagnitude <= radiusSquared && ++count >= 2) return true;
            }
            return false;
        }
        internal static void Install(Harmony harmony, Assembly assembly)
        {
            MethodInfo scan = AccessTools.Method(assembly.GetType("FiresGhettoNetworkMod.CreatureOwnership", true), "ScanPrefabs");
            if (scan == null || scan.ReturnType != typeof(void) || !scan.IsStatic || scan.GetParameters().Length != 0)
                throw new NotSupportedException("FGN boss prefab scan changed.");
            RewriteScan(PatchProcessor.GetOriginalInstructions(scan));
            harmony.Patch(scan, transpiler: new HarmonyMethod(AccessTools.Method(typeof(FgnBossPolicy), "RewriteScan")));
        }
        // Include bosses in the existing FGN scan; unmodified/legacy source
        // clients still have FGN's original final boss veto, preserving safety.
        private static bool ScanBoss(Character actor) { return actor.IsBoss() && !FgnCombatOwnership.Enabled; }
        private static bool VetoBoss(Character actor)
        { return actor.IsBoss() && !FgnMobHandoff.AuthorizesBoss(actor.GetZDOID()); }
        internal static List<CodeInstruction> RewriteScan(IEnumerable<CodeInstruction> source)
        { return Rewrite(source, "ScanBoss"); }
        internal static List<CodeInstruction> RewriteVeto(IEnumerable<CodeInstruction> source)
        { return Rewrite(source, "VetoBoss"); }
        private static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source, string wrapper)
        {
            MethodInfo native = AccessTools.Method(typeof(Character), "IsBoss"); int count = 0;
            List<CodeInstruction> result = new List<CodeInstruction>();
            foreach (CodeInstruction original in source)
            {
                CodeInstruction item = new CodeInstruction(original);
                if (native.Equals(item.operand))
                { item.opcode = OpCodes.Call; item.operand = AccessTools.Method(typeof(FgnBossPolicy), wrapper); count++; }
                result.Add(item);
            }
            if (count != 1) throw new NotSupportedException("FGN boss predicate changed: " + wrapper);
            return result;
        }
    }
}
