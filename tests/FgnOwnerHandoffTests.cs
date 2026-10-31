using System;
using System.Collections.Generic;
using TooManyLags;

internal static class FgnOwnerHandoffTests
{
    private static int checks;
    private static void Check(string label, bool result)
    { checks++; if (!result) throw new Exception(label); }

    private sealed class World
    {
        internal readonly Dictionary<long, ZNetPeer> Peers = new Dictionary<long, ZNetPeer>();
        internal readonly Dictionary<ZDOID, ZDO> Data = new Dictionary<ZDOID, ZDO>();
        internal double Time;
        internal int Sends, Rejections;
        internal long LastTarget;
        internal bool Matched = true, ThrowSend;
        internal readonly FgnOwnerHandoff Bridge;
        internal World()
        {
            Peers[10] = new ZNetPeer(); Peers[20] = new ZNetPeer();
            Bridge = new FgnOwnerHandoff(99, delegate(ZNetPeer ignored) { return Matched; },
                FindPeer, FindData, Send, delegate { return Time; });
            Bridge.Rejected = delegate { Rejections++; };
        }
        private ZNetPeer FindPeer(long id) { ZNetPeer peer; return Peers.TryGetValue(id, out peer) ? peer : null; }
        private ZDO FindData(ZDOID id) { ZDO data; return Data.TryGetValue(id, out data) ? data : null; }
        private void Send(ZNetPeer peer, ZDOID id, long target)
        { Sends++; LastTarget = target; if (ThrowSend) throw new InvalidOperationException("transport closed"); }
        internal ZDO Add(int id, long owner, uint revision)
        {
            ZDO result = new ZDO { m_uid = new ZDOID(id), Owner = owner, OwnerRevision = revision };
            Data.Add(result.m_uid, result); return result;
        }
        internal static void Commit(ZDO data, long owner, uint revision)
        { data.Owner = owner; data.OwnerRevision = revision; }
    }

    public static void Main()
    {
        World world = new World(); ZDO data = world.Add(1, 10, 7);
        Check("native request sent once", world.Bridge.Request(data, 20) == 1 && world.Sends == 1 && world.LastTarget == 20);
        Check("pending observed", world.Bridge.Pending(data) && !world.Bridge.Received(data));
        for (int i = 0; i < 10000; i++) world.Bridge.Request(data, 20);
        Check("same node deduplicated without a scan", world.Sends == 1);
        world.Time = 5.999;
        Check("retry backoff", world.Bridge.Request(data, 20) == 1 && world.Sends == 1);
        world.Time = 6;
        Check("timeout never duplicates unanswered RPC", world.Bridge.Request(data, 20) == 1 && world.Sends == 1 && data.Owner == 10 && data.OwnerRevision == 7);
        world.Time = 1000;
        Check("long timeout still no steal or duplicate", world.Bridge.Request(data, 20) == 1 && data.Owner == 10 && world.Sends == 1);
        World.Commit(data, 20, 8);
        Check("exact receipt pins", world.Bridge.Received(data) && !world.Bridge.Pending(data) && world.Bridge.Request(data, 20) == 2);
        Check("reservation query preserves completed receipt", !world.Bridge.Reserved(data) && world.Bridge.Received(data));
        int sends = world.Sends;
        world.Time = 2000;
        Check("pinned receipt causes no send", world.Bridge.Request(data, 20) == 2 && world.Sends == sends);
        World.Commit(data, 20, 9);
        Check("later revision invalidates receipt", !world.Bridge.Received(data));

        world = new World(); data = world.Add(11, 10, 7); world.Bridge.Request(data, 20);
        data.Owner = 20; world.Time = 1000;
        Check("owner-first native half retained", world.Bridge.Transitioning(data) && !world.Bridge.Received(data) && world.Bridge.Pending(data));
        Check("owner-first half has no consent/retry", world.Bridge.Request(data, 99) == 1 && world.Sends == 1);
        data.OwnerRevision = 8;
        Check("owner-first final native receipt", world.Bridge.Received(data) && !world.Bridge.Transitioning(data) && !world.Bridge.Pending(data) && world.Bridge.Request(data, 20) == 2);

        world = new World(); data = world.Add(12, 10, 7); world.Bridge.Request(data, 20);
        data.OwnerRevision = 8; world.Time = 1000;
        Check("revision-first native half retained", world.Bridge.Transitioning(data) && !world.Bridge.Received(data) && world.Bridge.Pending(data));
        Check("revision-first half has no consent/retry", world.Bridge.Request(data, 99) == 1 && world.Sends == 1);
        data.Owner = 20;
        Check("revision-first final native receipt", world.Bridge.Received(data) && !world.Bridge.Transitioning(data) && !world.Bridge.Pending(data) && world.Bridge.Request(data, 20) == 2);

        world = new World(); data = world.Add(13, 10, 7); world.Bridge.Request(data, 20);
        data.Owner = 30;
        Check("unrelated owner is not a native half", !world.Bridge.Transitioning(data) && !world.Bridge.Received(data) && !world.Bridge.Pending(data));
        world = new World(); data = world.Add(14, 10, 7); world.Bridge.Request(data, 20);
        data.OwnerRevision = 9;
        Check("unrelated revision is not a native half", !world.Bridge.Transitioning(data) && !world.Bridge.Pending(data) && !world.Bridge.Received(data));

        world = new World(); data = world.Add(2, 10, 4);
        world.Bridge.Request(data, 20);
        world.Time = 8;
        Check("changed policy settles original in flight", world.Bridge.Request(data, 99) == 1 && world.LastTarget == 20 && data.Owner == 10);
        World.Commit(data, 20, 5);
        Check("old policy commit remains a received proof", world.Bridge.Received(data));
        Check("new target is a new source request, not old receipt", world.Bridge.Request(data, 99) == 1 && world.LastTarget == 99 && !world.Bridge.Received(data));
        World.Commit(data, 99, 6);
        Check("second exact commit", world.Bridge.Received(data) && world.Bridge.Request(data, 99) == 2);

        world = new World(); data = world.Add(10, 10, 4);
        world.Peers[30] = new ZNetPeer(); world.Bridge.Request(data, 20);
        world.Peers.Remove(20); world.Time = 5;
        Check("departed destination not retried", world.Bridge.Request(data, 20) == 1 && world.Sends == 1);
        Check("changed destination retains backoff", world.Bridge.Request(data, 30) == 1 && world.Sends == 1);
        world.Time = 6;
        Check("ready changed destination cannot duplicate unanswered old RPC", world.Bridge.Request(data, 30) == 1 &&
            world.Sends == 1 && world.LastTarget == 20 && data.Owner == 10 && data.OwnerRevision == 4);
        World.Commit(data, 20, 5);
        Check("old outstanding destination remains exact receipt", world.Bridge.Received(data) && !world.Bridge.Pending(data));
        Check("departed actual owner enters native orphan path", world.Bridge.Request(data, 30) == 0);

        world = new World(); data = world.Add(15, 10, 4); world.Bridge.Request(data, 20);
        world.Bridge.Result(world.Peers[20].m_rpc, data.m_uid, false, "held");
        Check("wrong source result cannot release gameplay hold", world.Bridge.Pending(data));
        world.Bridge.Result(world.Peers[10].m_rpc, new ZDOID(150), false, "held");
        Check("wrong identity result cannot release gameplay hold", world.Bridge.Pending(data));
        world.Bridge.Result(world.Peers[10].m_rpc, data.m_uid, true, "");
        Check("positive result is not receipt", world.Bridge.Pending(data) && !world.Bridge.Received(data));
        world.Bridge.Result(world.Peers[10].m_rpc, data.m_uid, false, "not owner");
        Check("not-owner result is not current-owner proof", world.Bridge.Pending(data));
        world.Bridge.Result(world.Peers[10].m_rpc, data.m_uid, false, "gone");
        Check("gone result is not current-owner proof", world.Bridge.Pending(data));
        world.Bridge.Result(world.Peers[10].m_rpc, data.m_uid, false, "future veto");
        Check("unknown result remains pending", world.Bridge.Pending(data));
        world.Bridge.Result(world.Peers[10].m_rpc, data.m_uid, false, "owner fighting");
        Check("known busy veto releases transaction and notifies policy once", !world.Bridge.Pending(data) && !world.Bridge.Reserved(data) && !world.Bridge.Received(data) && world.Rejections == 1);
        world.Bridge.Result(world.Peers[10].m_rpc, data.m_uid, false, "held");
        for (int i = 0; i < 10000; i++) { world.Time = i; world.Bridge.Pending(data); world.Bridge.Reserved(data); }
        Check("settled rejection has no timed retry, status work or duplicate notification", world.Sends == 1 && world.Rejections == 1);
        world.Time = 0;
        Check("fresh explicit policy request has no six-second veto delay", world.Bridge.Request(data, 20) == 1 && world.Sends == 2 && world.Bridge.Pending(data));
        world.Time = 1000;
        Check("new unanswered retry never overlaps next request", world.Bridge.Request(data, 20) == 1 && world.Sends == 2);
        World.Commit(data, 20, 5);
        world.Bridge.Result(world.Peers[10].m_rpc, data.m_uid, false, "held");
        Check("late veto after committed ownership cannot unpin receipt", world.Bridge.Received(data) && !world.Bridge.Pending(data));

        foreach (string reason in new[] { "held", "possessed", "dead", "boss", "tamed", "attacking", "staggering", "ridden", "bad target" })
        {
            world = new World(); data = world.Add(16, 10, 4); world.Bridge.Request(data, 20);
            world.Bridge.Result(world.Peers[10].m_rpc, data.m_uid, false, reason);
            Check("known source-owner veto " + reason, !world.Bridge.Pending(data) && !world.Bridge.Reserved(data) && world.Rejections == 1);
        }
        world = new World(); data = world.Add(17, 10, 4); world.Bridge.Request(data, 20);
        world.Bridge.Result(world.Peers[10].m_rpc, data.m_uid, false, "held"); world.Peers.Remove(20);
        Check("known negative allows new ready policy after departed target", world.Bridge.Request(data, 99) == 1 && world.Sends == 2 && world.LastTarget == 99 && data.Owner == 10);

        world = new World(); data = world.Add(3, 10, 1);
        world.Bridge.Request(data, 20); World.Commit(data, 20, 3);
        Check("skipped revision is not source consent", !world.Bridge.Received(data) && !world.Bridge.Pending(data));
        world.Bridge.Request(data, 99); World.Commit(data, 10, 4);
        Check("wrong target is not consent", !world.Bridge.Received(data));

        world = new World(); data = world.Add(4, 10, 1); world.Bridge.Request(data, 20);
        ZDO replacement = new ZDO { m_uid = data.m_uid, Owner = 20, OwnerRevision = 2 };
        world.Data[data.m_uid] = replacement; World.Commit(data, 20, 2);
        Check("same ID different ZDO is not receipt", !world.Bridge.Received(data) && !world.Bridge.Pending(data));
        world.Data[data.m_uid] = data; world.Bridge.Request(data, 99);
        world.Data.Remove(data.m_uid); data.m_uid = new ZDOID(5); world.Data.Add(data.m_uid, data);
        World.Commit(data, 99, 3);
        Check("changed ZDO ID is not receipt", !world.Bridge.Received(data));

        world = new World(); data = world.Add(6, 10, 1); world.Bridge.Request(data, 20);
        world.Peers.Remove(10);
        Check("disconnected source clears pending", world.Bridge.Request(data, 20) == 0 && !world.Bridge.Pending(data));
        world.Peers[10] = new ZNetPeer(); world.Bridge.Request(data, 20);
        world.Peers[10] = new ZNetPeer();
        Check("reconnected source is not old connection", !world.Bridge.Pending(data));
        world.Bridge.Request(data, 20); world.Peers[10].m_rpc = new ZRpc();
        Check("replaced transport invalidates old request", !world.Bridge.Pending(data));

        world = new World(); data = world.Add(7, 10, 1); world.Matched = false;
        Check("protocol mismatch has no speculative RPC", world.Bridge.Request(data, 20) == 0 && world.Sends == 0);
        world.Matched = true; world.Peers[10].Ready = false;
        Check("not ready has no RPC", world.Bridge.Request(data, 20) == 0 && world.Sends == 0);
        world.Peers[10].Ready = true; world.Peers[10].m_rpc = null;
        Check("missing connection has no RPC", world.Bridge.Request(data, 20) == 0 && world.Sends == 0);

        world = new World(); data = world.Add(8, 10, UInt16.MaxValue);
        Check("wire revision wrap refused", world.Bridge.Request(data, 20) == 0 && world.Sends == 0);
        data.OwnerRevision = UInt16.MaxValue - 1U; world.Bridge.Request(data, 20);
        World.Commit(data, 20, UInt16.MaxValue);
        Check("last nonwrapping commit valid", world.Bridge.Received(data));

        world = new World(); data = world.Add(9, 10, 1); world.ThrowSend = true;
        Check("send exception is not steal consent", world.Bridge.Request(data, 20) == 1 && world.Bridge.Pending(data) && data.Owner == 10);
        world.Bridge.Forget(data);
        Check("node cleanup", !world.Bridge.Pending(data) && !world.Bridge.Received(data));
        world.ThrowSend = false; world.Bridge.Request(data, 20); world.Bridge.Clear();
        Check("world cleanup", !world.Bridge.Pending(data));
        data.Owner = 99;
        Check("server source uses caller native path", world.Bridge.Request(data, 20) == 0 && !world.Bridge.Pending(data));
        data.Owner = 0;
        Check("unowned source uses caller native path", world.Bridge.Request(data, 20) == 0);
        data.Owner = 10;
        Check("same owner needs no native callback", world.Bridge.Request(data, 10) == 0);
        Check("zero destination rejected", world.Bridge.Request(data, 0) == 0);
        Check("null ZDO rejected", world.Bridge.Request(null, 20) == 0);

        Console.WriteLine("PASS: " + checks + " FGN source-handoff state assertions; no live transport or client execution simulated.");
    }
}

// Test fixtures exercise the production state machine, not native FGN execution.
public struct ZDOID : IEquatable<ZDOID>
{
    public static readonly ZDOID None = new ZDOID(0);
    private readonly int value;
    public ZDOID(int value) { this.value = value; }
    public bool Equals(ZDOID other) { return value == other.value; }
    public override bool Equals(object other) { return other is ZDOID && Equals((ZDOID)other); }
    public override int GetHashCode() { return value; }
    public static bool operator ==(ZDOID left, ZDOID right) { return left.Equals(right); }
    public static bool operator !=(ZDOID left, ZDOID right) { return !left.Equals(right); }
}
public sealed class ZDO { public ZDOID m_uid; public long Owner; public uint OwnerRevision; public long GetOwner() { return Owner; } }
public sealed class ZRpc { public void Invoke(string name, params object[] arguments) { } }
public sealed class ZNetPeer { public bool Ready = true; public ZRpc m_rpc = new ZRpc(); public bool IsReady() { return Ready; } }
public sealed class ZNet { public static ZNet instance; public ZNetPeer GetPeer(long uid) { return null; } }
public sealed class ZDOMan { public static ZDOMan instance; public ZDO GetZDO(ZDOID id) { return null; } }
namespace UnityEngine { public static class Time { public static double realtimeSinceStartupAsDouble { get { return 0; } } } }
