using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Stage one: preserve the target-selection -> next-attack boundary. Native
    // commit/readiness/damage routing remain in charge; no mid-action takeover.
    internal static class FgnTargetBoundary
    {
        private sealed class Intent
        {
            internal BaseAI Ai;
            internal ZDO Data;
            internal ZDOMan World;
            internal ZNet Network;
            internal long Source, Token;
            internal uint Revision;
            internal ZDOID Target;
            internal double Until;
            internal bool Holding;
#if TML_DEBUG_SYSTEM
            internal bool Solo;
#endif
        }

        private const int ProbeMode = 8, ReadyMode = 9, UnavailableMode = 10;
        private const string TargetRpc = "TooManyLags_AI_BoundaryTarget", DecisionRpc = "TooManyLags_AI_BoundaryDecision";
        private const double MaximumHoldSeconds = 0.25;
        private static ConditionalWeakTable<BaseAI, Intent> intents = new ConditionalWeakTable<BaseAI, Intent>();
        private static Func<Humanoid, Attack> readAttack;
        private static bool installed, confirmed;
        private static long nextToken;
#if !TML_RELEASE || TML_TEST_METRICS
        internal static long Changes, HeldStarts, ExpiredHolds;
#endif

        internal static void Install(Harmony harmony)
        {
            FieldInfo attack = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
            MethodInfo start = AccessTools.Method(typeof(Humanoid), "StartAttack", new[] { typeof(Character), typeof(bool) });
            MethodInfo update = AccessTools.Method(typeof(MonsterAI), "UpdateAI", new[] { typeof(float) });
            MethodInfo damage = AccessTools.Method(typeof(Character), "RPC_Damage");
            if (attack == null || attack.FieldType != typeof(Attack) || start == null || start.ReturnType != typeof(bool) ||
                update == null || update.ReturnType != typeof(bool) || damage == null)
                throw new NotSupportedException("Native target/attack boundary contract unavailable.");
            readAttack = FieldReader.Create<Humanoid, Attack>(attack);
            harmony.Patch(start, prefix: Hook("BeforeAttack"));
            harmony.Patch(update, finalizer: Hook("AfterAi"));
            harmony.Patch(damage, finalizer: Hook("AfterDamage"));
            installed = true; Reset();
        }

        private static HarmonyMethod Hook(string name) { return new HarmonyMethod(AccessTools.Method(typeof(FgnTargetBoundary), name)); }
        private static bool Available
        { get { return installed && FgnCombatOwnership.Enabled && ZNet.instance && (ZNet.instance.IsDedicated() || confirmed); } }

        internal static void Reset()
        {
            intents = new ConditionalWeakTable<BaseAI, Intent>(); confirmed = false;
#if !TML_RELEASE || TML_TEST_METRICS
            Changes = HeldStarts = ExpiredHolds = 0;
#endif
            // Tokens stay monotonic across world resets on one live connection.
        }
        internal static void Probe(ZNetPeer peer)
        { if (installed && FgnCombatOwnership.Enabled && peer != null && peer.IsReady()) peer.m_rpc.Invoke("TooManyLags_AI_Mode", ProbeMode); }
        internal static bool Mode(ZNetPeer peer, int mode, bool active)
        {
            if (!installed || !ZNet.instance) return false;
            if (ZNet.instance.IsDedicated())
            {
                if (mode != ProbeMode) return false;
                peer.m_rpc.Invoke("TooManyLags_AI_Mode", active ? ReadyMode : UnavailableMode); return true;
            }
            if (mode != ReadyMode && mode != UnavailableMode) return false;
            if (peer != null && peer.IsReady() && peer.m_server && ReferenceEquals(peer, ZNet.instance.GetServerPeer()))
            { confirmed = mode == ReadyMode; if (!confirmed) intents = new ConditionalWeakTable<BaseAI, Intent>(); }
            return true;
        }
        internal static void Connect(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;
            peer.m_rpc.Register<ZDOID, ZDOID, ZPackage>(TargetRpc, delegate(ZRpc rpc, ZDOID id, ZDOID target, ZPackage payload)
            {
                if (!FgnCombatOwnership.Enabled || !ZNet.instance || !ZNet.instance.IsDedicated() ||
                    !peer.IsReady() || peer.m_server) return;
                long revision, token;
                try { revision = payload.ReadLong(); token = payload.ReadLong(); }
                catch (Exception) { return; }
                try
                {
                    if (token <= 0 || !FgnCombatOwnership.BoundaryTarget(peer, id, target, revision, token)) return;
                    ZDO data = ZDOMan.instance.GetZDO(id);
                    bool pending = data != null && data.GetOwner() == peer.m_uid &&
                        (FgnMobHandoff.Waiting(data) || FgnCombatOwnership.Fgn.Pending(id, Time.realtimeSinceStartupAsDouble));
                    ZPackage reply = new ZPackage(); reply.Write(revision); reply.Write(token); reply.Write(pending);
                    peer.m_rpc.Invoke(DecisionRpc, id, target, reply);
                }
                catch (Exception error) { FgnCombatOwnership.HandoffFailure(error); }
            });
            peer.m_rpc.Register<ZDOID, ZDOID, ZPackage>(DecisionRpc, delegate(ZRpc rpc, ZDOID id, ZDOID target, ZPackage payload)
            {
                if (!confirmed || !FgnCombatOwnership.HandoffServer(peer)) return;
                long revision, token; bool pending;
                try { revision = payload.ReadLong(); token = payload.ReadLong(); pending = payload.ReadBool(); }
                catch (Exception) { return; }
                try
                {
                    ZDO data = ZDOMan.instance.GetZDO(id);
                    ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
                    BaseAI ai = view ? view.GetComponent<BaseAI>() : null; Intent item;
                    if (!ai || !intents.TryGetValue(ai, out item) || !Current(item) || item.Token != token ||
                        item.Revision != revision || item.Target != target) return;
                    if (!pending) item.Holding = false;
                }
                catch (Exception error) { FgnCombatOwnership.HandoffFailure(error); }
            });
        }

        internal static void Changed(BaseAI ai, Character value)
        {
            if (!Available || !FgnCombatOwnership.Watching(ai)) return;
            ZNetView view = ai.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) return;
            ZDO data = view.GetZDO(); Player player = value as Player;
            if (data.GetBool(ZDOVars.s_tamed) || data.GetLong(ZDOVars.s_user, 0L) != 0L) return;
            ZNetView playerView = player ? player.GetComponent<ZNetView>() : null;
            ZDO playerData = playerView && playerView.IsValid() ? playerView.GetZDO() : null;
            ZDOID target = player && !player.IsDead() ? player.GetZDOID() : ZDOID.None;
            Intent item = intents.GetOrCreateValue(ai);
            item.Ai = ai; item.Data = data; item.World = ZDOMan.instance; item.Network = ZNet.instance;
            item.Source = data.GetOwner(); item.Revision = data.OwnerRevision; item.Target = target; item.Token = 0;
#if TML_DEBUG_SYSTEM
            item.Solo = false;
#endif
            item.Until = Time.realtimeSinceStartupAsDouble + MaximumHoldSeconds;
            // A local player target cannot require moving ownership elsewhere.
            // A lost/non-player target cannot start a new player attack anyway.
            item.Holding = player && !player.IsDead() && playerData != null && playerData.IsValid() &&
                playerData.GetOwner() != 0L && playerData.GetOwner() != item.Source;
#if !TML_RELEASE || TML_TEST_METRICS
            Changes++;
#endif
        }
        internal static bool Send(BaseAI ai, ZDO data, ZDOID target, ZNetPeer server)
        {
            if (!Available || ZNet.instance.IsDedicated() || !confirmed || server == null || !server.IsReady()) return false;
            // Bootstrap/adoption/self-target reports need neither a local barrier
            // nor a reply. Keep their original packet and allocate no intent here.
            Intent item;
            if (!intents.TryGetValue(ai, out item) || !item.Holding
#if TML_DEBUG_SYSTEM
                || item.Solo
#endif
            ) return false;
            if (!ReferenceEquals(item.Data, data) || item.Target != target || !Current(item) ||
                Time.realtimeSinceStartupAsDouble >= item.Until)
            { item.Holding = false; return false; }
            item.Token = ++nextToken;
            ZPackage payload = new ZPackage(); payload.Write((long)data.OwnerRevision); payload.Write(item.Token);
            server.m_rpc.Invoke(TargetRpc, data.m_uid, target, payload); return true;
        }
        internal static void ServerObserved(BaseAI ai)
        { Intent item; if (ai && intents.TryGetValue(ai, out item)) item.Holding = false; }
#if TML_DEBUG_SYSTEM
        internal static bool HoldForSolo(MonsterAI ai)
        {
            if (!Available || !FgnCombatOwnership.Watching(ai)) return false;
            Humanoid actor = ai.GetComponent<Humanoid>();
            Player target = ai.GetTargetCreature() as Player;
            if (!actor || Busy(actor)) return false;
            ZNetView view = ai.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) return false;
            ZDO data = view.GetZDO();
            if (data.GetBool(ZDOVars.s_tamed) || data.GetLong(ZDOVars.s_user, 0L) != 0L) return false;
            Intent item = intents.GetOrCreateValue(ai);
            item.Ai = ai; item.Data = data; item.World = ZDOMan.instance; item.Network = ZNet.instance;
            // The diagnostic destination is synthetic. Idle AI and a real
            // non-player target are valid boundaries too; keep their real target.
            item.Source = data.GetOwner(); item.Revision = data.OwnerRevision;
            item.Target = target && !target.IsDead() ? target.GetZDOID() : ZDOID.None; item.Token = 0;
            item.Until = Time.realtimeSinceStartupAsDouble + MaximumHoldSeconds; item.Holding = item.Solo = true;
            return true;
        }
#endif
        internal static void CommitFinished(ZDO data, ZDOID target)
        {
            ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            BaseAI ai = view ? view.GetComponent<BaseAI>() : null; Intent item;
            if (ai && intents.TryGetValue(ai, out item) && ReferenceEquals(item.Data, data) && item.Target == target) item.Holding = false;
        }
        internal static bool FreshTarget(BaseAI ai)
        {
            Intent item;
            return ai && intents.TryGetValue(ai, out item) && Current(item)
#if TML_DEBUG_SYSTEM
                && !item.Solo
#endif
                ;
        }
        internal static bool Busy(Character actor)
        {
            if (!actor) return true;
            if (actor.InAttack()) return true;
            Humanoid human = actor as Humanoid;
            Attack attack = human && readAttack != null ? readAttack(human) : null;
            return attack != null && !attack.IsDone();
        }
        private static bool Current(Intent item)
        {
            return item.Data != null && item.Data.IsValid() && ReferenceEquals(item.World, ZDOMan.instance) &&
                ReferenceEquals(item.Network, ZNet.instance) && ReferenceEquals(item.World.GetZDO(item.Data.m_uid), item.Data) &&
                item.Data.GetOwner() == item.Source && item.Source == ZDOMan.GetSessionID() && item.Data.OwnerRevision == item.Revision;
        }
        private static bool BeforeAttack(Humanoid __instance, ref bool __result)
        {
            if (!Available) return true;
            try
            {
                BaseAI ai = __instance.GetComponent<BaseAI>(); Intent item;
                if (!ai || !intents.TryGetValue(ai, out item) || !item.Holding) return true;
                if (!Current(item) || !FgnCombatOwnership.Watching(ai)) { item.Holding = false; return true; }
                if (Time.realtimeSinceStartupAsDouble >= item.Until)
                { item.Holding = false;
#if !TML_RELEASE || TML_TEST_METRICS
                    ExpiredHolds++;
#endif
                    return true; }
                // Do not cancel an existing action/chain or use this gate as proof
                // that an unfinished Attack is safe to migrate.
                if (Busy(__instance)) return true;
#if !TML_RELEASE || TML_TEST_METRICS
                HeldStarts++;
#endif
                __result = false; return false;
            }
            catch (Exception error) { FgnCombatOwnership.HandoffFailure(error); return true; }
        }
        private static Exception AfterAi(MonsterAI __instance, bool __result, Exception __exception)
        {
            if (__exception == null && Available)
            {
                if (__result) FgnCombatOwnership.CompletedAi(__instance);
                FgnTargetEvents.Flush(__instance);
            }
            return __exception;
        }
        private static Exception AfterDamage(Character __instance, Exception __exception)
        {
            if (__exception == null && Available && FgnTargetEvents.HasPending)
                try { FgnTargetEvents.Flush(__instance.GetComponent<BaseAI>()); }
                catch (Exception error) { FgnCombatOwnership.HandoffFailure(error); }
            return __exception;
        }
        internal static string Describe(BaseAI ai)
        {
            Intent item;
            if (!ai || !intents.TryGetValue(ai, out item)) return "; boundary=no local intent";
            return "; boundary=" + (item.Holding && Current(item) ? "waiting (bounded)" : "released") +
                "; boundary token=" + item.Token
#if !TML_RELEASE || TML_TEST_METRICS
                + "; held attack starts=" + HeldStarts + "; expired holds=" + ExpiredHolds
#endif
                ;
        }
    }
}
