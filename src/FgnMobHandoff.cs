using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Only live, identified mob requests. FGN still performs the final veto,
    // source-owner commit and damage forwarding; no second request runs in parallel.
    internal static class FgnMobHandoff
    {
        private sealed class Request
        {
            internal ZDO Data; internal ZRpc Rpc; internal long Token, Source, Destination;
            internal uint Revision; internal bool Accepted;
        }
        private sealed class Commit
        {
            internal ZRpc Rpc; internal ZDO Data; internal long Token;
            internal ZDOID Target; internal bool Replied;
        }
        private sealed class Fence { internal long Last; }
        private static ConditionalWeakTable<ZRpc, Fence> fences = new ConditionalWeakTable<ZRpc, Fence>();
        private static readonly Dictionary<ZDOID, Request> pending = new Dictionary<ZDOID, Request>();
        private static readonly List<ZDOID> scratch = new List<ZDOID>();
        private static Commit commit;
        private static long nextToken;
        internal static bool Completing { get; private set; }
        private const string RequestRpc = "TooManyLags_AI_Commit", ResultRpc = "TooManyLags_AI_CommitResult";

        internal static void Reset() { pending.Clear(); scratch.Clear(); fences = new ConditionalWeakTable<ZRpc, Fence>(); commit = null; Completing = false; }
        internal static int Count { get { return pending.Count; } }
        internal static void Connect(ZNetPeer peer)
        {
            peer.m_rpc.Register<ZDOID, long, ZPackage>(RequestRpc, delegate(ZRpc rpc, ZDOID id, long token, ZPackage payload)
            { try { ReceiveCommit(peer, id, token, payload); } catch (Exception error) { FgnCombatOwnership.HandoffFailure(error); } });
            peer.m_rpc.Register<ZDOID, long, ZPackage>(ResultRpc, delegate(ZRpc rpc, ZDOID id, long token, ZPackage payload)
            { try { ReceiveResult(peer, id, token, payload); } catch (Exception error) { FgnCombatOwnership.HandoffFailure(error); } });
        }
        internal static bool Waiting(ZDO data)
        {
            Request request;
            if (data == null || !pending.TryGetValue(data.m_uid, out request)) return false;
            if (!ReferenceEquals(request.Data, data) || !FgnCombatOwnership.HandoffSource(data, request.Source, request.Rpc) ||
                data.GetOwner() != request.Source || data.OwnerRevision != request.Revision)
            { Forget(data.m_uid); return false; }
            return true;
        }
        private static void Forget(ZDOID id)
        { pending.Remove(id); if (FgnCombatOwnership.Fgn != null) FgnCombatOwnership.Fgn.ClearPending(id); }

        internal static void Send(ZRpc rpc, string name, object[] args)
        {
            ZDO data = name == "FGN_HandOff" && args.Length == 2 && ZDOMan.instance != null ? ZDOMan.instance.GetZDO((ZDOID)args[0]) : null;
            ZDOID target;
            if (data == null || !FgnCombatOwnership.ShouldOverride(data.m_uid) || !FgnCombatOwnership.ReadTarget(data, out target))
            { rpc.Invoke(name, args); return; }
            if (!FgnCombatOwnership.TargetAllowsOwnership(data)) return;
            if (Waiting(data)) return;
            Request request = new Request { Data = data, Rpc = rpc, Source = data.GetOwner(),
                Revision = data.OwnerRevision, Destination = (long)args[1], Token = ++nextToken };
            pending[data.m_uid] = request;
            ZPackage payload = new ZPackage(); payload.Write(request.Revision); payload.Write(request.Destination); payload.Write(target);
            try { rpc.Invoke(RequestRpc, data.m_uid, request.Token, payload); }
            catch { Forget(data.m_uid); throw; }
        }

        private static void ReceiveCommit(ZNetPeer peer, ZDOID id, long token, ZPackage payload)
        {
            if (token <= 0 || id.IsNone() || !FgnCombatOwnership.HandoffServer(peer)) return;
            uint revision; long destination; ZDOID target;
            try { revision = payload.ReadUInt(); destination = payload.ReadLong(); target = payload.ReadZDOID(); }
            catch (Exception) { return; }
            Fence fence = fences.GetOrCreateValue(peer.m_rpc);
            if (token <= fence.Last) return; // Ordered per-connection requests commit at most once.
            fence.Last = token;
            ZDO data = ZDOMan.instance.GetZDO(id);
            Commit previous = commit;
            Commit current = new Commit { Rpc = peer.m_rpc, Data = data, Token = token, Target = target };
            commit = current;
            try
            {
                if (data == null || !data.IsValid() || data.GetOwner() != ZDOMan.GetSessionID() ||
                    data.OwnerRevision != revision || revision >= UInt16.MaxValue || !FgnCombatOwnership.ShouldOverride(id))
                { Reply(current, id, false, "source changed"); return; }
                // Refresh from the real local AI, including a target change which
                // was queued before this commit. The server receives it before the reply.
                if (!FgnCombatOwnership.TargetAllowsOwnership(data)) { Reply(current, id, false, "non-player target"); return; }
                if (!FgnCombatOwnership.HandoffObserve(data)) { Reply(current, id, false, "not ready"); return; }
                FgnCombatOwnership.Fgn.Commit(peer.m_rpc, id, destination);
                if (!current.Replied) Reply(current, id, false, "unprocessed");
            }
            catch (Exception) { if (!current.Replied) Reply(current, id, false, "unprocessed"); }
            finally
            {
                try { FgnTargetBoundary.CommitFinished(data, target); }
                finally { commit = previous; }
            }
        }
        internal static bool StaleTarget(ZDO data, ZDOID target)
        { return MobSourceHandoff.IsLocalCommit(data) ? MobSourceHandoff.LocalTarget != target : commit != null && ReferenceEquals(commit.Data, data) && commit.Target != target; }
        internal static bool AuthorizesBoss(ZDOID id)
        {
            return commit != null && commit.Data != null && commit.Data.m_uid == id && commit.Data.IsValid() &&
                commit.Data.GetOwner() == ZDOMan.GetSessionID() && FgnCombatOwnership.ShouldOverride(id);
        }
        internal static void SendResult(ZRpc rpc, string name, object[] args)
        {
            if (name == "FGN_HandOffResult" && args.Length == 3 && ZDOMan.instance != null && MobSourceHandoff.IsLocalCommit(ZDOMan.instance.GetZDO((ZDOID)args[0]))) return;
            if (commit == null || name != "FGN_HandOffResult" || args.Length != 3 ||
                !ReferenceEquals(commit.Rpc, rpc) || commit.Data == null || commit.Data.m_uid != (ZDOID)args[0])
            { rpc.Invoke(name, args); return; }
            Reply(commit, (ZDOID)args[0], (bool)args[1], (string)args[2]);
        }
        private static void Reply(Commit current, ZDOID id, bool accepted, string reason)
        {
            current.Replied = true;
            ZDO data = current.Data; ZPackage payload = new ZPackage();
            payload.Write(accepted); payload.Write(reason ?? "");
            payload.Write(data != null && data.IsValid() ? data.GetOwner() : 0L);
            payload.Write(data != null && data.IsValid() ? (uint)data.OwnerRevision : 0U);
            current.Rpc.Invoke(ResultRpc, id, current.Token, payload);
        }
        private static void ReceiveResult(ZNetPeer peer, ZDOID id, long token, ZPackage payload)
        {
            Request request;
            if (!FgnCombatOwnership.Enabled || !ZNet.instance || !ZNet.instance.IsDedicated() ||
                !pending.TryGetValue(id, out request) || request.Accepted || request.Token != token || !ReferenceEquals(peer.m_rpc, request.Rpc) ||
                !FgnCombatOwnership.HandoffSource(request.Data, request.Source, request.Rpc)) return;
            bool accepted; string reason; long owner; uint revision;
            try { accepted = payload.ReadBool(); reason = payload.ReadString(); owner = payload.ReadLong(); revision = payload.ReadUInt(); }
            catch (Exception) { return; }
            if (accepted && (owner != request.Destination || revision != request.Revision + 1U)) return;
            if (!accepted && (owner != request.Source || revision != request.Revision))
            { Forget(id); return; } // Source has changed; never interpret an obsolete veto as a new request's result.
            double before = Time.realtimeSinceStartupAsDouble, existing;
            bool hadCooldown = FgnCombatOwnership.Fgn.Cooldowns.TryGetValue(id, out existing);
            if (!accepted) pending.Remove(id);
            else request.Accepted = true;
            // Keep FGN statistics and attack-end retry handling. Strip only the
            // new six-second rejection cooldown for this authenticated request.
            Completing = true;
            try { FgnCombatOwnership.Fgn.Result(peer.m_rpc, id, accepted, reason); }
            finally { Completing = false; }
            double until;
            if (!accepted && FgnCombatOwnership.Fgn.Cooldowns.TryGetValue(id, out until) &&
                until >= before + 6.0 && until <= Time.realtimeSinceStartupAsDouble + 6.0 && (!hadCooldown || until != existing))
            {
                if (hadCooldown) FgnCombatOwnership.Fgn.Cooldowns[id] = existing;
                else FgnCombatOwnership.Fgn.Cooldowns.Remove(id);
            }
            if (accepted) AfterReplication();
            else if (reason == "attacking") FgnCombatOwnership.HandoffAttackRejected(id);
            else if (reason == "AI target changed") FgnCombatOwnership.HandoffRetry(request.Data);
        }
        // Called only by network replication and existing FGN passes. Empty path
        // is a count check; no per-actor or per-physics-tick work is added.
        internal static void AfterReplication()
        {
            if (pending.Count == 0 || !ZNet.instance || !ZNet.instance.IsDedicated()) return;
            try { Prune(); } catch (Exception error) { FgnCombatOwnership.HandoffFailure(error); }
        }
        private static void Prune()
        {
            scratch.Clear(); foreach (ZDOID id in pending.Keys) scratch.Add(id);
            foreach (ZDOID id in scratch)
            {
                Request request;
                if (!pending.TryGetValue(id, out request)) continue;
                ZDO data = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(id);
                if (!ReferenceEquals(data, request.Data) || !Waiting(data))
                {
                    Forget(id);
                    if (data != null && data.IsValid()) FgnCombatOwnership.HandoffChanged(data);
                }
            }
            scratch.Clear();
        }

        internal static List<CodeInstruction> RewriteSend(IEnumerable<CodeInstruction> source)
        { return Rewrite(source, "Send"); }
        internal static List<CodeInstruction> RewriteResult(IEnumerable<CodeInstruction> source)
        { return Rewrite(source, "SendResult"); }
        private static List<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source, string replacement)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(); int count = 0;
            MethodInfo invoke = AccessTools.Method(typeof(ZRpc), "Invoke", new[] { typeof(string), typeof(object[]) });
            foreach (CodeInstruction original in source)
            {
                CodeInstruction item = new CodeInstruction(original);
                if (invoke.Equals(item.operand)) { item.opcode = OpCodes.Call; item.operand = AccessTools.Method(typeof(FgnMobHandoff), replacement); count++; }
                result.Add(item);
            }
            if (count != 1) throw new NotSupportedException("FGN mob handoff RPC site changed: " + replacement);
            return result;
        }
    }
}
