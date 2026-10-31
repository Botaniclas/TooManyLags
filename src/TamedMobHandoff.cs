using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Use the identified grid transaction and native FGN state/damage forwarding.
    // Only its blanket "tamed" veto is relaxed, inside this authenticated commit.
    internal static class TamedMobHandoff
    {
        internal static Action<ZDO, long> FlightTransfer = null;
        private static ZDO committing;
        private static readonly Func<Humanoid, Attack> attack = FieldReader.Create<Humanoid, Attack>(AccessTools.Field(typeof(Humanoid), "m_currentAttack"));
        internal static void Install(Harmony harmony, Assembly fgn)
        {
            MethodInfo veto = AccessTools.Method(fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true), "Veto", new[] { typeof(ZDO), typeof(long) });
            if (veto == null || veto.ReturnType != typeof(string)) throw new NotSupportedException("FGN animal handoff veto unavailable.");
            harmony.Patch(veto, postfix: new HarmonyMethod(typeof(TamedMobHandoff), "AfterVeto"));
        }
        internal static bool Ready(ZNetView view)
        {
            if (!view || !view.IsValid() || !view.m_persistent || view.m_distant || !view.gameObject.activeInHierarchy || view.transform.parent != null) return false;
            ZDO data = view.GetZDO(); Character actor = view.GetComponent<Character>();
            if (data == null || !data.IsValid() || !data.Persistent || !data.GetBool(ZDOVars.s_tamed) || !actor || actor is Player ||
                FriendlyCreatures.IsFriendly(actor) || actor.IsDead() || !actor.isActiveAndEnabled || actor.IsAttached() || actor.IsRiding() ||
                actor.InAttack() || actor.IsStaggering() || data.GetLong(ZDOVars.s_user, 0L) != 0L) return false;
            Humanoid human = actor as Humanoid; Attack current = human ? attack(human) : null;
            if (current != null && !current.IsDone()) return false;
            Tameable tame = view.GetComponent<Tameable>();
            return (!tame || (!ObjectCatalog.IsSummoned(tame) && !tame.HaveRider() &&
                (!tame.m_commandable || String.IsNullOrEmpty(data.GetString(ZDOVars.s_follow)))));
        }
        internal static void Commit(Action<ZRpc, ZDOID, long> native, ZRpc rpc, ZDOID id, long target)
        {
            ZDO data = ZDOMan.instance.GetZDO(id);
            ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            if (data == null || !data.IsOwner() || !Ready(view)) return;
            Flush(view);
            ZDO previous = committing; committing = data;
            try { native(rpc, id, target); }
            finally { committing = previous; }
        }
        internal static void Flush(ZNetView view)
        { ZSyncTransform sync = view ? view.GetComponent<ZSyncTransform>() : null; if (sync && view.IsOwner()) sync.SyncNow(); }
        private static void AfterVeto(ZDO __0, ref string __result)
        {
            if (__result != "tamed" || !ReferenceEquals(committing, __0) || __0 == null || !__0.IsOwner()) return;
            ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(__0) : null;
            if (Ready(view)) __result = null;
        }
    }
}
