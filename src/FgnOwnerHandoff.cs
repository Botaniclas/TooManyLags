using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TooManyLags
{
    // Source-owner commits. Identified production requests reconcile explicit
    // source snapshots; a timer can query status, never steal or repeat a commit.
    internal sealed class FgnOwnerHandoff
    {
        private const double RetrySeconds = 6.0;
        internal static Func<ZNetPeer, bool> CooperativeClient { get; set; }
        internal static Action<ZNetPeer, ZDOID, long, uint, long> IdentifiedSend { get; set; }
        internal static Action<ZNetPeer, ZDOID, long> IdentifiedStatus { get; set; }
        private static long nextToken;
        private sealed class Stamp
        {
            internal ZDOID Id;
            internal long Source, Target;
            internal uint Revision;
            internal ZNetPeer Peer;
            internal ZRpc Rpc;
            internal double RetryAt;
            internal bool InFlight;
            internal long Token;
            internal bool Reconciling;
            internal double StatusAt;
        }

        private sealed class IdentityComparer : IEqualityComparer<ZDO>
        {
            public bool Equals(ZDO left, ZDO right) { return System.Object.ReferenceEquals(left, right); }
            public int GetHashCode(ZDO value) { return RuntimeHelpers.GetHashCode(value); }
        }

        private readonly long server;
        private readonly Func<long, ZNetPeer> peer;
        private readonly Func<ZNetPeer, bool> matched;
        private readonly Func<ZDOID, ZDO> lookup;
        private readonly Action<ZNetPeer, ZDOID, long> invoke;
        private readonly Func<double> clock;
        private readonly Action<ZNetPeer, ZDOID, long, uint, long> identified;
        private readonly Action<ZNetPeer, ZDOID, long> status;
        private readonly Dictionary<ZDO, Stamp> pending = new Dictionary<ZDO, Stamp>(new IdentityComparer());

        internal FgnOwnerHandoff(Assembly fgn, long server)
            : this(server, Bind(fgn), FindPeer, FindData, Send, Now)
        { identified = IdentifiedSend; status = IdentifiedStatus; }

        internal FgnOwnerHandoff(long server, Func<ZNetPeer, bool> matched,
            Func<long, ZNetPeer> peer, Func<ZDOID, ZDO> lookup,
            Action<ZNetPeer, ZDOID, long, uint, long> identified,
            Action<ZNetPeer, ZDOID, long> status, Func<double> clock)
            : this(server, matched, peer, lookup, Send, clock)
        { this.identified = identified; this.status = status; }

        internal bool Supports(ZNetPeer source)
        { return identified == null || (CooperativeClient != null && CooperativeClient(source)); }

        // Test seam; production delegates bind once and use actual native peers.
        internal FgnOwnerHandoff(long server, Func<ZNetPeer, bool> matched,
            Func<long, ZNetPeer> peer, Func<ZDOID, ZDO> lookup,
            Action<ZNetPeer, ZDOID, long> invoke, Func<double> clock)
        {
            if (server == 0 || matched == null || peer == null || lookup == null || invoke == null || clock == null)
                throw new ArgumentException("An FGN handoff requires native peer and ZDO access.");
            this.server = server; this.matched = matched; this.peer = peer;
            this.lookup = lookup; this.invoke = invoke; this.clock = clock;
        }

        // 0 = no cooperative client source; 1 = waiting/veto/retry; 2 = the
        // requested source commit is actually present, with exactly one revision.
        internal int Request(ZDO data, long target)
        {
            if (!Current(data)) { Forget(data); return 0; }
            Stamp stamp;
            if (pending.TryGetValue(data, out stamp))
            {
                if (!data.m_uid.Equals(stamp.Id)) { pending.Remove(data); }
                else if (Committed(data, stamp))
                {
                    if (target == stamp.Target) return 2;
                    pending.Remove(data); // A new policy starts from the actual new owner.
                }
                else if (Transition(data, stamp)) return 1;
                else if (Unchanged(data, stamp))
                {
                    if (!Connected(stamp)) { pending.Remove(data); return 0; }
                    if (stamp.Reconciling) { Probe(stamp); return 1; }
                    if (!stamp.InFlight && target != stamp.Target && target != 0 &&
                        target != stamp.Source && DestinationReady(target))
                    {
                        // Change policy only after its single native response.
                        // FGN has no nonce: retrying an unanswered request could
                        // let its late veto release a later accepted transfer.
                        stamp.Target = target;
                    }
                    // Settle the original request even if policy changes in flight.
                    // A timeout never authorizes a forced overwrite.
                    Retry(stamp);
                    return 1;
                }
                else pending.Remove(data);
            }

            long source = data.GetOwner();
            if (source == 0 || source == server || target == 0 || target == source ||
                data.OwnerRevision >= UInt16.MaxValue) return 0;
            ZNetPeer sourcePeer = peer(source);
            if (!Ready(sourcePeer) || !matched(sourcePeer) || !Supports(sourcePeer)) return 0;
            stamp = Create(data, sourcePeer, target);
            pending.Add(data, stamp);
            Retry(stamp);
            return Committed(data, stamp) ? 2 : 1;
        }

        internal bool Pending(ZDO data)
        {
            Stamp stamp;
            if (data == null || !pending.TryGetValue(data, out stamp)) return false;
            if (!Current(data) || !data.m_uid.Equals(stamp.Id)) { pending.Remove(data); return false; }
            if (Committed(data, stamp)) return false;
            if (Transition(data, stamp)) return true;
            if (!Unchanged(data, stamp) || !Connected(stamp)) { pending.Remove(data); return false; }
            Probe(stamp);
            return stamp.InFlight || stamp.Reconciling;
        }

        internal bool Reserved(ZDO data)
        {
            Stamp stamp;
            if (data == null || !pending.TryGetValue(data, out stamp)) return false;
            if (!Current(data) || !data.m_uid.Equals(stamp.Id)) { pending.Remove(data); return false; }
            if (Committed(data, stamp)) return false;
            if (Transition(data, stamp)) return true;
            if (!Unchanged(data, stamp) || !Connected(stamp)) { pending.Remove(data); return false; }
            return true;
        }

        internal void Result(ZRpc rpc, ZDOID id, bool accepted, string reason)
        {
            if (accepted || !KnownOwnerVeto(reason)) return;
            ZDO data = lookup(id); Stamp stamp;
            if (data == null || !pending.TryGetValue(data, out stamp) || !stamp.InFlight ||
                stamp.Token != 0 || !data.m_uid.Equals(id) || !stamp.Id.Equals(id) ||
                !System.Object.ReferenceEquals(rpc, stamp.Rpc) || !Current(data) ||
                !Unchanged(data, stamp) || !Connected(stamp)) return;
            // Known native vetoes are reached only after source IsOwner passed.
            // Gameplay can run there; the grid policy keeps ownership reserved.
            stamp.InFlight = false;
            stamp.RetryAt = clock() + RetrySeconds;
        }

        internal bool Observed(ZRpc rpc, ZDOID id, long token, bool processed,
            bool present, long observedOwner, uint observedRevision)
        {
            ZDO data = lookup(id); Stamp stamp;
            if (!processed || token <= 0 || data == null || !pending.TryGetValue(data, out stamp) ||
                token != stamp.Token || !data.m_uid.Equals(id) || !stamp.Id.Equals(id) ||
                !ReferenceEquals(rpc, stamp.Rpc) || !Current(data) || !Connected(stamp)) return false;
            if (!stamp.InFlight && !stamp.Reconciling) return false;
            // The identified commit has definitely returned. It cannot execute
            // later: status messages never invoke it and the source fences tokens.
            stamp.InFlight = false;
            stamp.RetryAt = clock() + RetrySeconds;
            stamp.Reconciling = present && (data.GetOwner() != observedOwner || data.OwnerRevision != observedRevision);
            stamp.StatusAt = clock() + 2;
            return true;
        }

        private void Probe(Stamp stamp)
        {
            if (stamp.Token == 0 || status == null || (!stamp.InFlight && !stamp.Reconciling) || clock() < stamp.StatusAt) return;
            stamp.StatusAt = clock() + 2;
            try { status(stamp.Peer, stamp.Id, stamp.Token); } catch (Exception) { }
        }

        internal bool Received(ZDO data)
        {
            Stamp stamp;
            if (data == null || !pending.TryGetValue(data, out stamp)) return false;
            if (!Current(data) || !data.m_uid.Equals(stamp.Id)) { pending.Remove(data); return false; }
            if (Committed(data, stamp)) return true;
            if (!Unchanged(data, stamp) && !Transition(data, stamp)) pending.Remove(data);
            return false;
        }

        internal bool Transitioning(ZDO data)
        {
            Stamp stamp;
            if (data == null || !pending.TryGetValue(data, out stamp)) return false;
            if (!Current(data) || !data.m_uid.Equals(stamp.Id)) { pending.Remove(data); return false; }
            return Transition(data, stamp);
        }

        internal void Forget(ZDO data) { if (data != null) pending.Remove(data); }
        internal void Clear() { pending.Clear(); }

        private bool Current(ZDO data)
        {
            return data != null && data.m_uid != ZDOID.None && System.Object.ReferenceEquals(lookup(data.m_uid), data);
        }
        private static bool Unchanged(ZDO data, Stamp stamp)
        { return data.GetOwner() == stamp.Source && data.OwnerRevision == stamp.Revision; }
        private static bool Committed(ZDO data, Stamp stamp)
        { return data.GetOwner() == stamp.Target && data.OwnerRevision == stamp.Revision + 1U; }
        // Native receipt assigns owner and revision separately. Neither expected
        // first half is a commit; observer hooks must retain the original stamp.
        private static bool Transition(ZDO data, Stamp stamp)
        {
            return (data.GetOwner() == stamp.Source && data.OwnerRevision == stamp.Revision + 1U) ||
                (data.GetOwner() == stamp.Target && data.OwnerRevision == stamp.Revision);
        }
        private bool Connected(Stamp stamp)
        {
            ZNetPeer current = peer(stamp.Source);
            return System.Object.ReferenceEquals(current, stamp.Peer) && Ready(current) &&
                System.Object.ReferenceEquals(current.m_rpc, stamp.Rpc) && matched(current);
        }
        private static bool Ready(ZNetPeer value)
        { return value != null && value.IsReady() && value.m_rpc != null; }
        private bool DestinationReady(long target)
        { return target == server || (target != 0 && Ready(peer(target))); }
        private static Stamp Create(ZDO data, ZNetPeer source, long target)
        {
            return new Stamp
            {
                Id = data.m_uid, Source = data.GetOwner(), Target = target,
                Revision = data.OwnerRevision, Peer = source, Rpc = source.m_rpc
            };
        }
        private void Retry(Stamp stamp)
        {
            double now = clock();
            if (stamp.InFlight) { Probe(stamp); return; }
            if (stamp.Reconciling || stamp.RetryAt > now || !DestinationReady(stamp.Target)) return;
            if (identified != null)
            {
                if (nextToken == Int64.MaxValue) return;
                stamp.Token = ++nextToken; stamp.StatusAt = now + 2;
            }
            stamp.InFlight = true;
            // A failed send can have partially left the process. Retain the
            // pending record rather than turning an exception into steal consent.
            try
            {
                if (identified != null) identified(stamp.Peer, stamp.Id, stamp.Target, stamp.Revision, stamp.Token);
                else invoke(stamp.Peer, stamp.Id, stamp.Target);
            }
            catch (Exception) { }
        }
        private static bool KnownOwnerVeto(string reason)
        {
            switch (reason)
            {
                case "owner fighting": case "held": case "possessed":
                case "dead": case "boss": case "tamed": case "attacking":
                case "staggering": case "ridden": case "bad target": return true;
                default: return false;
            }
        }
        private static ZNetPeer FindPeer(long uid)
        { return ZNet.instance != null ? ZNet.instance.GetPeer(uid) : null; }
        private static ZDO FindData(ZDOID id)
        { return ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null; }
        private static void Send(ZNetPeer source, ZDOID id, long target)
        { source.m_rpc.Invoke("FGN_HandOff", id, target); }
        private static double Now() { return Time.realtimeSinceStartupAsDouble; }

        private static Func<ZNetPeer, bool> Bind(Assembly fgn)
        {
            if (fgn == null) throw new ArgumentNullException("fgn");
            Type gate = fgn.GetType("FiresGhettoNetworkMod.FgnVersionGate", true);
            Type handoff = fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true);
            MethodInfo check = Contract(gate, "IsMatched", typeof(bool), typeof(ZNetPeer));
            Contract(handoff, "OnNewConnection", typeof(void), typeof(ZNet), typeof(ZNetPeer));
            Contract(handoff, "OnHandOff", typeof(void), typeof(ZRpc), typeof(ZDOID), typeof(long));
            Contract(handoff, "OnResult", typeof(void), typeof(ZRpc), typeof(ZDOID), typeof(bool), typeof(string));
            Contract(handoff, "Veto", typeof(string), typeof(ZDO), typeof(long));
            return (Func<ZNetPeer, bool>)Delegate.CreateDelegate(typeof(Func<ZNetPeer, bool>), check);
        }

        private static MethodInfo Contract(Type type, string name, Type result, params Type[] arguments)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo method = type.GetMethod(name, flags, null, arguments, null);
            if (method == null || method.ReturnType != result || method.ContainsGenericParameters)
                throw new NotSupportedException("FGN native handoff contract changed: " + type.FullName + "." + name);
            return method;
        }
    }
}
