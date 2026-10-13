using System;
using System.Collections.Generic;
using TooManyLags;
using TooManyLags.GridHandoffs;

internal static class IdentifiedGridHandoffTests
{
    private static int checks;
    private static void Check(string label, bool value) { checks++; if (!value) throw new Exception(label); }
    private sealed class World
    {
        internal readonly Dictionary<long, ZNetPeer> Peers = new Dictionary<long, ZNetPeer>();
        internal readonly Dictionary<ZDOID, ZDO> Data = new Dictionary<ZDOID, ZDO>();
        internal readonly FgnOwnerHandoff Handoff;
        internal double Time; internal int Sends, Queries; internal long Token; internal bool ThrowSend;
        internal bool Matched = true;
        internal World()
        {
            Peers[10] = new ZNetPeer(); Peers[20] = new ZNetPeer(); Peers[30] = new ZNetPeer();
            Handoff = new FgnOwnerHandoff(99, delegate { return Matched; }, FindPeer, FindData,
                delegate(ZNetPeer p, ZDOID id, long target, uint revision, long token)
                { Sends++; Token = token; if (ThrowSend) throw new Exception("uncertain send"); },
                delegate { Queries++; }, delegate { return Time; });
        }
        private ZNetPeer FindPeer(long id) { ZNetPeer p; return Peers.TryGetValue(id, out p) ? p : null; }
        private ZDO FindData(ZDOID id) { ZDO d; return Data.TryGetValue(id, out d) ? d : null; }
        internal ZDO Add() { ZDO d = new ZDO { m_uid = new ZDOID(1), Owner = 10, OwnerRevision = 7 }; Data.Add(d.m_uid, d); return d; }
        internal bool Reply(ZDO d, bool present, long owner, uint revision)
        { return Handoff.Observed(Peers[10].m_rpc, d.m_uid, Token, true, present, owner, revision); }
    }
    public static void Main()
    {
        FgnOwnerHandoff.CooperativeClient = delegate { return true; };
        try
        {
            World w = new World(); ZDO d = w.Add();
            w.Matched = false;
            Check("verified TML endpoint does not wait for the separate FGN version greeting", w.Handoff.Request(d, 20) == 1 && w.Sends == 1);
            Check("separate FGN greeting cannot discard the active identified transaction", w.Handoff.Pending(d) && w.Handoff.Reserved(d));
            w = new World(); d = w.Add();
            Check("identified commit starts", w.Handoff.Request(d, 20) == 1 && w.Sends == 1 && w.Token > 0);
            long first = w.Token;
            for (int i = 0; i < 10000; i++) w.Handoff.Request(d, 20);
            Check("no repeating commit or status within deadline", w.Sends == 1 && w.Queries == 0);
            w.Time = 2; Check("missing reply queries status only", w.Handoff.Pending(d) && w.Queries == 1 && w.Sends == 1);
            w.Handoff.Pending(d); Check("same tick has no duplicate status", w.Queries == 1);
            Check("unprocessed reply cannot release an uncertain commit", !w.Handoff.Observed(w.Peers[10].m_rpc, d.m_uid, first, false, true, 10, 7) && w.Handoff.Pending(d));
            Check("untagged legacy busy result cannot settle identified commit", LegacyBusy(w, d) && w.Handoff.Pending(d));
            Check("wrong token rejected", !w.Handoff.Observed(w.Peers[10].m_rpc, d.m_uid, first + 1, true, true, 10, 7));
            Check("wrong source rejected", !w.Handoff.Observed(w.Peers[20].m_rpc, d.m_uid, first, true, true, 10, 7));
            Check("definite rejection settles without owner write", w.Reply(d, true, 10, 7) && !w.Handoff.Pending(d) && w.Handoff.Reserved(d) && d.Owner == 10 && d.OwnerRevision == 7);
            w.Time = 3;
            Check("duplicate result cannot extend settled retry deadline", !w.Reply(d, true, 10, 7) && !w.Handoff.Pending(d));
            w.Time = 7.99; w.Handoff.Request(d, 20); Check("rejection backoff retained", w.Sends == 1);
            w.Time = 8; w.Handoff.Request(d, 20); Check("next attempt uses fresh token", w.Sends == 2 && w.Token > first);
            Check("late rejected attempt cannot release newer attempt", !w.Handoff.Observed(w.Peers[10].m_rpc, d.m_uid, first, true, true, 10, 7) && w.Handoff.Pending(d));
            Check("not-owner snapshot waits for actual native state", w.Reply(d, true, 30, 8) && w.Handoff.Pending(d));
            w.Time = 600; w.Handoff.Request(d, 99); Check("reconciliation never overwrites or recommits stale owner", w.Sends == 2 && d.Owner == 10 && d.OwnerRevision == 7);
            d.Owner = 30; d.OwnerRevision = 8;
            Check("actual corrected state releases old transaction", !w.Handoff.Pending(d) && !w.Handoff.Received(d));

            w = new World(); d = w.Add(); w.Handoff.Request(d, 20);
            Check("accepted snapshot is not native receipt", w.Reply(d, true, 20, 8) && w.Handoff.Pending(d) && !w.Handoff.Received(d));
            d.Owner = 20; Check("half tuple still protected", w.Handoff.Pending(d) && w.Handoff.Transitioning(d));
            d.OwnerRevision = 8; Check("actual committed tuple completes", !w.Handoff.Pending(d) && w.Handoff.Received(d));

            // A new source can receive Commit before native ownership replication.
            w = new World(); d = w.Add(); w.Handoff.Request(d, 20);
            Check("stale-source response is identified", w.Reply(d, true, 30, 6) && w.Handoff.Pending(d));
            w.Time = .5; w.Handoff.Request(d, 20);
            Check("stale source requests status promptly without resending the commit", w.Queries == 1 && w.Sends == 1 && d.Owner == 10 && d.OwnerRevision == 7);
            Check("source catches up with actual current native tuple", w.Reply(d, true, 10, 7) && !w.Handoff.Pending(d));
            w.Handoff.Request(d, 20);
            Check("caught-up source retries without an unrelated six-second use veto", w.Sends == 2 && w.Token > 0 && w.Handoff.Pending(d));

            w = new World(); d = w.Add(); w.Handoff.Request(d, 20); first = w.Token;
            w.Reply(d, true, 30, 6); w.Time = .5; w.Handoff.Request(d, 99);
            Check("policy change during source repair never executes an uncertain second commit", w.Sends == 1 && w.Queries == 1 && w.Handoff.Pending(d));
            w.Reply(d, true, 10, 7); w.Handoff.Request(d, 99);
            Check("repaired source can follow current policy with a fresh fenced command", w.Sends == 2 && w.Token > first);
            Check("old repair status cannot release current policy command", !w.Handoff.Observed(w.Peers[10].m_rpc, d.m_uid, first, true, true, 10, 7) && w.Handoff.Pending(d));

            w = new World(); d = w.Add(); w.Handoff.Request(d, 20); w.Reply(d, true, 30, 6);
            d.Owner = 30; d.OwnerRevision = 8;
            Check("newer native owner discards source-repair reservation without overwriting it", !w.Handoff.Pending(d) && !w.Handoff.Reserved(d) && d.Owner == 30 && d.OwnerRevision == 8);

            w = new World(); d = w.Add(); w.Handoff.Request(d, 20);
            Check("gone is a terminal no-commit, not steal consent", w.Reply(d, false, 0, 0) && !w.Handoff.Pending(d) && d.Owner == 10 && d.OwnerRevision == 7);
            w = new World(); d = w.Add(); w.ThrowSend = true; w.Handoff.Request(d, 20); w.Time = 2;
            Check("uncertain send queried without retry", w.Handoff.Pending(d) && w.Queries == 1 && w.Sends == 1);
            Check("fenced no-commit status releases gameplay", w.Reply(d, true, 10, 7) && !w.Handoff.Pending(d));
            w = new World(); d = w.Add(); w.Handoff.Request(d, 20); ZRpc old = w.Peers[10].m_rpc; w.Peers[10] = new ZNetPeer();
            Check("old connection status rejected", !w.Handoff.Observed(old, d.m_uid, w.Token, true, true, 10, 7));
            Check("disconnect/reconnect drops old transaction", !w.Handoff.Pending(d));
            w = new World(); d = w.Add(); w.Handoff.Request(d, 20); w.Handoff.Forget(d);
            Check("unloaded node ignores late report", !w.Reply(d, true, 20, 8));
            w = new World(); d = w.Add(); w.Handoff.Request(d, 20); ZDOID oldId = d.m_uid;
            d.m_uid = new ZDOID(2); w.Data[d.m_uid] = d;
            Check("pooled reference cannot graft old ID report", !w.Handoff.Observed(w.Peers[10].m_rpc, oldId, w.Token, true, true, 20, 8));
            Check("new ID cannot inherit old transaction token", !w.Reply(d, true, 20, 8));
            FgnOwnerHandoff.CooperativeClient = delegate { return false; };
            w = new World(); d = w.Add(); Check("unsupported source receives no speculative commit", w.Handoff.Request(d, 20) == 0 && w.Sends == 0);

            CommitFence fence = new CommitFence();
            Check("first command executes", fence.Begin(10));
            Check("duplicate command cannot execute", !fence.Begin(10));
            Check("old command cannot execute", !fence.Begin(9));
            Check("new command executes", fence.Begin(11));
            fence.Settle(12); Check("uncertain command fenced by status", fence.Decided(12) && !fence.Begin(12));
            Check("later command remains usable", fence.Begin(13));
            Check("invalid identifiers never execute", !fence.Begin(0) && !fence.Begin(-1) && !fence.Decided(0));
            for (long i = 14; i < 100000; i++) if (!fence.Begin(i)) throw new Exception("monotonic fence");
            Check("history bounded to one scalar", fence.Decided(99999) && !fence.Begin(100));
            Greeting();
            Console.WriteLine("PASS: " + checks + " identified handoff/fencing assertions; no live Unity or transport execution.");
        }
        finally { FgnOwnerHandoff.CooperativeClient = null; }
    }
    private static void Greeting()
    {
        CapabilityGreeting greeting = new CapabilityGreeting();
        Check("inactive player-simulation/unsupported server sends no grid greeting", !greeting.Waiting(0) && !greeting.Due(0));
        Check("advertisement is not a server acknowledgement", !greeting.Welcome(false));
        Check("first valid advertisement requests capability immediately", greeting.Waiting(0) && greeting.Due(0) && greeting.Sent);
        for (int i = 0; i < 10000; i++) if (greeting.Due(1)) throw new Exception("greeting frame flood");
        Check("duplicate advertisements cannot confirm a dropped Hello", !greeting.Welcome(false) && greeting.Waiting(1));
        Check("missing acknowledgement retries only after two seconds", !greeting.Due(1.99) && greeting.Due(2));
        Check("server confirmation stops greeting work", greeting.Welcome(true) && !greeting.Waiting(2));
        for (int i = 0; i < 10000; i++) if (greeting.Due(i)) throw new Exception("confirmed greeting retry");
        Check("delayed advertisement cannot reopen a confirmed session", greeting.Welcome(false) && !greeting.Waiting(100));

        greeting = new CapabilityGreeting(); int attempts = 0;
        greeting.Welcome(false);
        for (int i = 0; i < 1000; i++) if (greeting.Due(i)) attempts++;
        Check("a nonresponding advertised endpoint has a bounded 30-second join window", attempts == 15 && !greeting.Waiting(30));
        greeting.Welcome(false);
        Check("late duplicate advertisement does not reset an expired retry window", !greeting.Due(1001) && !greeting.Waiting(1001));
        Check("late actual acknowledgement is still accepted", greeting.Welcome(true) && !greeting.Waiting(1001));

        greeting = new CapabilityGreeting();
        Check("old server header-only offer starts negotiation", !greeting.Welcome(null) && greeting.Due(0));
        Check("old server second header-only Welcome confirms the response", greeting.Welcome(null) && !greeting.Waiting(0));
        greeting = new CapabilityGreeting();
        Check("new connection gets independent fresh greeting state", !greeting.Sent && !greeting.Welcome(false) && greeting.Due(0));
    }
    private static bool LegacyBusy(World w, ZDO d) { w.Handoff.Result(w.Peers[10].m_rpc, d.m_uid, false, "held"); return true; }
}
