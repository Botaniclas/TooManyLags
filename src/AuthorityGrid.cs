using System;
using System.Collections.Generic;

namespace TooManyLags
{
    internal enum GridCoveragePattern { Square3x3, Rounded5x5, Rounded7x7 }
    internal enum FieldTier : byte { Outer, Inner, Forward }
    internal struct Cell : IEquatable<Cell>
    {
        internal int X, Z;
        internal Cell(int x, int z) { X = x; Z = z; }
        internal static bool At(float x, float z, out Cell cell)
        { return At(x, z, 8, out cell); }
        internal static bool At(float x, float z, float size, out Cell cell)
        {
            cell = default(Cell);
            int subdivisions = Subdivisions(size);
            if (subdivisions == 0) return false;
            if (Single.IsNaN(x) || Single.IsNaN(z) || Single.IsInfinity(x) || Single.IsInfinity(z)) return false;
            // 10/64 is exactly representable: never divide by the rounded
            // Single 6.4 value, which would misclassify native zone borders.
            double scale = subdivisions / 64d;
            double cx = Math.Floor(((double)x + 32d) * scale), cz = Math.Floor(((double)z + 32d) * scale);
            // Native sector index has a bounded world. No wrapped/aliased cells.
            // Preserve the same physical domain for either subdivision size.
            int limit = 256 * subdivisions;
            if (cx < -limit || cx >= limit || cz < -limit || cz >= limit) return false;
            cell = new Cell((int)cx, (int)cz); return true;
        }
        internal static int Subdivisions(float size)
        { return size == 4f ? 16 : size == 6.4f ? 10 : size == 8f ? 8 : size == 16f ? 4 : 0; }
        internal static double Edge(int coordinate, int subdivisions)
        { return coordinate * 64d / subdivisions - 32d; }
        internal static Cell Zone(Cell fine, float size)
        {
            if (size == 6.4f)
                return new Cell(Floor10(fine.X), Floor10(fine.Z));
            int shift = size == 4f ? 4 : size == 16f ? 2 : 3;
            return new Cell(fine.X >> shift, fine.Z >> shift);
        }
        private static int Floor10(int value)
        { return value >= 0 ? value / 10 : (value + 1) / 10 - 1; }
        public bool Equals(Cell other) { return X == other.X && Z == other.Z; }
        public override bool Equals(object other) { return other is Cell && Equals((Cell)other); }
        public override int GetHashCode() { unchecked { return X * 397 ^ Z; } }
    }

    internal struct GridPeer
    {
        internal long Uid;
        internal Cell Cell;
        internal float Yaw;
        internal int Coverage;
        internal GridPeer(long uid, Cell cell, float yaw = 0f, int coverage = -1) { Uid = uid; Cell = cell; Yaw = yaw; Coverage = coverage; }
        internal static GridPeer Covered(long uid, Cell cell, int coverage)
        { return new GridPeer(uid, cell, Single.NaN, coverage); }
    }

    // Pure selection state. No Unity queries, network writes or object iteration.
    internal sealed class AuthorityGrid
    {
        private sealed class Policy
        {
            internal long Candidate, Owner, Uids;
            internal int Count, FastCount, ForwardCount;
            internal float Since, Wait, Deadline, CooldownUntil;
            internal bool Deferred;
            internal Request First;
            // Most fields have one requester: no per-field list allocation.
            // Only actual overlaps need the additional, usually tiny list.
            internal List<Request> Others;
        }
        private struct Request
        {
            internal long Uid; internal float Since; internal FieldTier Tier;
            internal Request(long uid, float since, FieldTier tier) { Uid = uid; Since = since; Tier = tier; }
        }
        private sealed class PeerState { internal Cell Cell; internal float Yaw; internal int Heading; internal bool Refreshed; }
        private struct Offset { internal Cell Cell; internal FieldTier Tier; internal Offset(int x, int z, FieldTier tier) { Cell = new Cell(x, z); Tier = tier; } }
        private readonly Dictionary<Cell, Policy> policies = new Dictionary<Cell, Policy>();
        // Reuse empty policy objects without retaining a world cell or owner.
        // Only the allocation pool is capped; larger footprints/peer counts
        // remain supported without retaining departed world cells.
        private readonly Stack<Policy> reusable = new Stack<Policy>();
        private readonly Dictionary<long, PeerState> previous = new Dictionary<long, PeerState>();
        // Only loaded object buckets may certify a completed exclusive request.
        // Coverage stays separate: a remembered player is still an overlap peer.
        private readonly Dictionary<Cell, long> remembered = new Dictionary<Cell, long>();
        private readonly HashSet<long> seen = new HashSet<long>();
        private readonly List<long> departed = new List<long>();
        private readonly HashSet<Cell> dirty = new HashSet<Cell>(), pending = new HashSet<Cell>();
        private readonly List<Cell> due = new List<Cell>();
        internal readonly List<Cell> Changes = new List<Cell>();
        // Optional sparse object retry listener. Null during normal operation;
        // only real player updates invoke it, never timer expiry or standing.
        internal Action<Cell> Renewed = null;
        private readonly long server;
        internal const int CoverageCount = 56;
        private readonly Offset[][] footprints = new Offset[CoverageCount][];
        private readonly Dictionary<Cell, FieldTier>[] maskTypes = new Dictionary<Cell, FieldTier>[CoverageCount];
        internal readonly HashSet<Cell> Accelerations = new HashSet<Cell>();
        private readonly bool directional;
        private readonly bool discardVacant;
        private float innerDelay, outerDelay, forwardDelay, overlapCooldown;
        internal float Delay;
        private float lastTime, nextDue = Single.PositiveInfinity;
        private int deferredCells;
        internal int Rebuilds, NeighborReads, PlayerUpdates, TimerVisits;
        internal int PolicyAllocations, PriorityReads;
        // Test instrumentation is compiled out of the production DLL, including argument evaluation.
#if TML_RELEASE
        [System.Diagnostics.Conditional("TML_TEST_METRICS")]
#endif
        private static void Count(ref int counter, int amount) { counter += amount; }

        internal AuthorityGrid(long server, float delay, GridCoveragePattern pattern = GridCoveragePattern.Square3x3,
            bool directional = false, float innerDelay = 0f, float outerDelay = 3f, bool discardVacant = false,
            bool forward = false, float forwardDelay = .2f, float overlapCooldown = 0f, bool fullRear = false)
        {
            this.server = server; Delay = delay;
            this.directional = directional;
            this.discardVacant = discardVacant;
            this.innerDelay = SafeDelay(innerDelay, 0f); this.outerDelay = SafeDelay(outerDelay, 3f);
            this.forwardDelay = SafeDelay(forwardDelay, .2f);
            this.overlapCooldown = SafeDelay(overlapCooldown, 1f);
            int radius = pattern == GridCoveragePattern.Rounded7x7 ? 3 : pattern == GridCoveragePattern.Rounded5x5 ? 2 : 1;
            forward = directional && forward && radius > 1;
            int[] fx = { 0, 1, 1, 1, 0, -1, -1, -1 }, fz = { 1, 1, 0, -1, -1, -1, 0, 1 };
            for (int heading = 0; heading < 8; heading++)
            {
                var footprint = new List<Offset>(37);
                for (int x = -radius; x <= radius; x++) for (int z = -radius; z <= radius; z++)
                {
                    int front = x * fx[heading] + z * fz[heading];
                    int side = -x * fz[heading] + z * fx[heading];
                    bool diagonal = fx[heading] != 0 && fz[heading] != 0;
                    // One further three-cell row; diagonal rows use the same
                    // fixed-grid projection as the inner priority triangle.
                    // Rounded5x5 gains only its forward diagonal tip (2,2).
                    bool ahead = forward && (diagonal ?
                        (x == 2 * fx[heading] && (z == fz[heading] || z == 2 * fz[heading])) ||
                        (x == fx[heading] && z == 2 * fz[heading]) : front == 2 && Math.Abs(side) <= 1);
                    if (radius != 1 && x * x + z * z > radius * (radius + 1) && !ahead) continue;
                    // Retained ownership uses the rear cutout; return-on-exit keeps the full shape.
                    // The outer rear flanks in the user's second sketch stay.
                    if (directional && !fullRear && front < 0 && (front < -1 || Math.Abs(side) <= 1)) continue;
                    bool inner = false;
                    if (directional)
                    {
                        if (fullRear) inner = Math.Abs(x) <= 1 && Math.Abs(z) <= 1;
                        else if (fx[heading] == 0 || fz[heading] == 0) inner = front >= 0 && front <= 1 && Math.Abs(side) <= 1;
                        else inner = (x == 0 && z == 0) || (front == 0 && Math.Abs(x) == 1 && Math.Abs(z) == 1) ||
                            (x == fx[heading] && z == fz[heading]) || (x == fx[heading] && z == 0) || (x == 0 && z == fz[heading]);
                    }
                    footprint.Add(new Offset(x, z, inner ? FieldTier.Inner : ahead ? FieldTier.Forward : FieldTier.Outer));
                }
                footprints[heading] = footprint.ToArray();
                var types = new Dictionary<Cell, FieldTier>(footprint.Count);
                foreach (Offset offset in footprint) types.Add(offset.Cell, offset.Tier);
                maskTypes[heading] = types;
            }
            // Adjacent camera directions are one request per player/cell.
            // Build their unions once, retaining the inner/forward priorities.
            for (int span = 2; span <= 3; span++) for (int heading = 0; heading < 8; heading++)
            {
                int index = (span - 1) * 8 + heading, previousIndex = (span - 2) * 8 + heading;
                if (!directional) { footprints[index] = footprints[heading]; maskTypes[index] = maskTypes[heading]; continue; }
                var types = new Dictionary<Cell, FieldTier>(maskTypes[previousIndex]);
                foreach (Offset offset in footprints[(heading + span - 1) & 7])
                {
                    FieldTier old;
                    if (!types.TryGetValue(offset.Cell, out old) || offset.Tier == FieldTier.Inner || (old == FieldTier.Outer && offset.Tier == FieldTier.Forward))
                        types[offset.Cell] = offset.Tier;
                }
                var footprint = new List<Offset>(types.Count);
                foreach (var pair in types) footprint.Add(new Offset(pair.Key.X, pair.Key.Z, pair.Value));
                footprints[index] = footprint.ToArray(); maskTypes[index] = types;
            }
            // Prediction keeps the actual mask's tiers. Only cells added by
            // the forecast get the red/inner delay; compute this once.
            for (int actual = 0; actual < 16; actual++) for (int side = 0; side < 2; side++)
            {
                int index = 24 + side * 16 + actual;
                if (!directional) { footprints[index] = footprints[actual]; maskTypes[index] = maskTypes[actual]; continue; }
                int first = actual & 7;
                int extra = side == 0 ? (first + (actual < 8 ? 1 : 2)) & 7 : (first + 7) & 7;
                var types = new Dictionary<Cell, FieldTier>(maskTypes[actual]);
                foreach (Offset offset in footprints[extra])
                    if (!types.ContainsKey(offset.Cell)) types.Add(offset.Cell, FieldTier.Inner);
                var footprint = new List<Offset>(types.Count);
                foreach (var pair in types) footprint.Add(new Offset(pair.Key.X, pair.Key.Z, pair.Value));
                footprints[index] = footprint.ToArray(); maskTypes[index] = types;
            }
        }
        internal static int HeadingCoverage(float yaw, float overlapDegrees = 30f)
        {
            if (Single.IsNaN(yaw) || Single.IsInfinity(yaw)) return -1;
            yaw %= 360f; if (yaw < 0f) yaw += 360f;
            int heading = (int)(yaw / 45f); float phase = yaw - heading * 45f;
            if (Single.IsNaN(overlapDegrees) || Single.IsInfinity(overlapDegrees)) overlapDegrees = 30f;
            overlapDegrees = Math.Max(0f, Math.Min(45f, overlapDegrees));
            if (overlapDegrees == 0f) return phase < 22.5f ? heading : (heading + 1) & 7;
            float start = (45f - overlapDegrees) * .5f, end = (45f + overlapDegrees) * .5f;
            // At 45 degrees every angle selects an adjacent pair. Direction
            // centers use the following pair, avoiding a third boundary mask.
            return phase < start ? heading : phase <= end ? 8 + heading : (heading + 1) & 7;
        }
        private static float SafeDelay(float value, float fallback)
        { return Single.IsNaN(value) || Single.IsInfinity(value) ? fallback : Math.Max(0f, Math.Min(30f, value)); }
        // Command-only tuning: keep requests, deadlines, overlap coverage and
        // completed-field receipts. New requests use the new delays.
        internal void ConfigureDelays(float full, float inner, float forward, float outer)
        { Delay = SafeDelay(full, 2f); innerDelay = SafeDelay(inner, 0f); forwardDelay = SafeDelay(forward, .2f); outerDelay = SafeDelay(outer, 3f); }
        internal void ConfigureOverlapCooldown(float seconds)
        { overlapCooldown = SafeDelay(seconds, 1f); }
        private static float Yaw(float value, float fallback)
        { if (Single.IsNaN(value) || Single.IsInfinity(value)) return fallback; value %= 360f; return value < 0 ? value + 360f : value; }
        internal long Owner(Cell cell) { Policy p; return policies.TryGetValue(cell, out p) ? p.Owner : 0L; }
        internal long Candidate(Cell cell) { Policy p; return policies.TryGetValue(cell, out p) ? p.Candidate : 0L; }
        internal bool Covered(Cell cell) { Policy p; return policies.TryGetValue(cell, out p) && p.Count != 0; }
        // Current coverage, not owner or delay: shared red fields stay urgent,
        // and only completely vacated fields are background return work.
        internal int WorkPriority(Cell cell)
        {
            Count(ref PriorityReads, 1);
            Policy p;
            if (!policies.TryGetValue(cell, out p) || p.Count == 0) return 3;
            return !directional ? 2 : p.FastCount != 0 ? 0 : p.ForwardCount != 0 ? 1 : 2;
        }
        private float FieldWait(Policy policy)
        {
            if (!directional) return Delay;
            if (policy.ForwardCount != 0)
            {
                float wait = forwardDelay;
                if (policy.FastCount != 0) wait = Math.Min(wait, innerDelay);
                if (policy.Count > policy.FastCount + policy.ForwardCount) wait = Math.Min(wait, outerDelay);
                return wait;
            }
            if (policy.FastCount == 0) return outerDelay;
            return policy.FastCount == policy.Count ? innerDelay : Math.Min(innerDelay, outerDelay);
        }
        internal float Wait(Cell cell)
        { Policy p; return policies.TryGetValue(cell, out p) ? FieldWait(p) : directional ? outerDelay : Delay; }
        internal float Remaining(Cell cell, float now)
        { Policy p; return policies.TryGetValue(cell, out p) && p.Owner != p.Candidate ? Math.Max(0f, p.Deadline - now) : 0f; }
        internal bool IsRemembered(Cell cell, long uid)
        { long value; return uid != 0 && uid != server && remembered.TryGetValue(cell, out value) && value == uid; }
        internal void Remember(Cell cell, long uid)
        { if (uid != 0 && uid != server && Owner(cell) == uid && Candidate(cell) == uid) remembered[cell] = uid; }
        internal void Forget(Cell cell) { remembered.Remove(cell); }
        internal int RememberedCells { get { return remembered.Count; } }
        private void Contribute(long uid, PeerState peer, bool add, float now)
        {
            foreach (Offset offset in footprints[peer.Heading])
                Contribution(uid, new Cell(peer.Cell.X + offset.Cell.X, peer.Cell.Z + offset.Cell.Z), offset.Tier, add, now);
        }
        private void Contribution(long uid, Cell cell, FieldTier tier, bool add, float now)
        {
            Policy policy; Count(ref NeighborReads, 1);
            if (!policies.TryGetValue(cell, out policy))
            {
                if (reusable.Count != 0) policy = reusable.Pop();
                else { policy = new Policy(); Count(ref PolicyAllocations, 1); }
                policies.Add(cell, policy);
            }
            policy.Count += add ? 1 : -1; policy.Uids ^= uid;
            if (tier == FieldTier.Inner) policy.FastCount += add ? 1 : -1;
            else if (tier == FieldTier.Forward) policy.ForwardCount += add ? 1 : -1;
            if (add)
            {
                Request request = new Request(uid, now, tier);
                if (policy.Count == 1) policy.First = request;
                else { if (policy.Others == null) policy.Others = new List<Request>(1); policy.Others.Add(request); }
                long old; if (remembered.Count != 0 && remembered.TryGetValue(cell, out old) && old != uid) remembered.Remove(cell);
            }
            else if (policy.First.Uid == uid)
            {
                if (policy.Others != null && policy.Others.Count != 0)
                { int last = policy.Others.Count - 1; policy.First = policy.Others[last]; policy.Others.RemoveAt(last); }
                else policy.First = default(Request);
            }
            else if (policy.Others != null) for (int i = 0; i < policy.Others.Count; i++) if (policy.Others[i].Uid == uid) { policy.Others.RemoveAt(i); break; }
            dirty.Add(cell);
            if (add && Renewed != null) Renewed(cell);
        }
        private void Reclassify(long uid, Cell cell, FieldTier before, FieldTier after)
        {
            Policy policy = policies[cell]; Count(ref NeighborReads, 2);
            if (before == FieldTier.Inner) policy.FastCount--;
            else if (before == FieldTier.Forward) policy.ForwardCount--;
            if (after == FieldTier.Inner) policy.FastCount++;
            else if (after == FieldTier.Forward) policy.ForwardCount++;
            if (policy.First.Uid == uid) policy.First.Tier = after;
            else if (policy.Others != null) for (int i = 0; i < policy.Others.Count; i++) if (policy.Others[i].Uid == uid)
            { Request request = policy.Others[i]; request.Tier = after; policy.Others[i] = request; break; }
            dirty.Add(cell);
        }
        private void MoveContribution(long uid, PeerState old, Cell next, int heading, float now)
        {
            // Stable overlap with the same timer class does not touch policies.
            // These mask lookups use immutable maps prepared once per world.
            foreach (Offset offset in footprints[old.Heading])
            {
                Cell cell = new Cell(old.Cell.X + offset.Cell.X, old.Cell.Z + offset.Cell.Z);
                FieldTier tier;
                if (maskTypes[heading].TryGetValue(new Cell(cell.X - next.X, cell.Z - next.Z), out tier))
                {
                    if (Renewed != null) Renewed(cell);
                    if (tier != offset.Tier) Reclassify(uid, cell, offset.Tier, tier);
                    // A real update revisits deferred fields and expired server
                    // cooldowns, including unchanged parts of its footprint.
                    else if (deferredCells != 0 && policies[cell].Deferred) dirty.Add(cell);
                    continue;
                }
                Contribution(uid, cell, offset.Tier, false, now);
            }
            foreach (Offset offset in footprints[heading])
            {
                Cell cell = new Cell(next.X + offset.Cell.X, next.Z + offset.Cell.Z);
                if (maskTypes[old.Heading].ContainsKey(new Cell(cell.X - old.Cell.X, cell.Z - old.Cell.Z))) continue;
                Contribution(uid, cell, offset.Tier, true, now);
            }
        }
        private void Commit(Cell cell, Policy policy)
        {
            pending.Remove(cell);
            if (policy.Owner != policy.Candidate)
            {
                policy.Owner = policy.Candidate; Changes.Add(cell);
                if (directional && policy.Owner == server && policy.Count > 1 && overlapCooldown > 0f)
                {
                    policy.CooldownUntil = lastTime + overlapCooldown;
                    SetDeferred(policy, true);
                }
            }
            if (policy.Count == 0 && policy.Owner == 0 && policies.Remove(cell) && discardVacant && reusable.Count < 1024)
            {
                policy.Candidate = policy.Owner = policy.Uids = 0;
                policy.Count = policy.FastCount = policy.ForwardCount = 0;
                policy.Since = policy.Wait = policy.Deadline = 0;
                policy.CooldownUntil = 0f;
                policy.First = default(Request);
                if (policy.Others != null) policy.Others.Clear();
                reusable.Push(policy);
            }
        }
        private void Evaluate(Cell cell, float now)
        {
            Policy policy = policies[cell];
            // No physical ownership return is requested with keep-on-exit.
            // An empty logical policy therefore needs no retirement timer.
            // Loaded object certificates and in-flight native RPCs are separate.
            if (discardVacant && policy.Count == 0)
            { SetDeferred(policy, false); policy.Candidate = 0; Commit(cell, policy); return; }
            // Coverage bookkeeping stays current while new field decisions are
            // locked. Expiry alone never queues a timer or an object handoff:
            // only the next actual player update may evaluate this field again.
            if (policy.CooldownUntil != 0f)
            {
                if (policy.Count != 0 && now < policy.CooldownUntil)
                { policy.Candidate = server; pending.Remove(cell); return; }
                policy.CooldownUntil = 0f;
            }
            if (directional && discardVacant && policy.Count == 1 && policy.First.Tier != FieldTier.Inner &&
                (policy.Deferred || policy.Candidate == server))
            {
                PeerState remaining;
                if (previous.TryGetValue(policy.Uids, out remaining) && !remaining.Refreshed)
                {
                    // An overlap ending outside red preserves the settled
                    // owner and cancels its field timer. No departure job/RPC.
                    // Pending handoff safety remains owned by the native bridge.
                    SetDeferred(policy, true); policy.Candidate = policy.Owner;
                    policy.Wait = FieldWait(policy); pending.Remove(cell); return;
                }
            }
            SetDeferred(policy, false);
            long candidate = policy.Count > 1 ? server : policy.Count == 1 ? policy.Uids : 0;
            if (policy.Candidate != candidate)
            {
                // Merge a still-waiting request into an overlap; do not first
                // commit the lone player or restart its valid timer. Departure
                // to a lone player starts the normal transition grace again.
                bool merge = policy.Owner != policy.Candidate && policy.Candidate != 0 && candidate == server;
                if (!merge) policy.Since = now;
                policy.Candidate = candidate;
            }
            float wait = FieldWait(policy);
            if (wait < policy.Wait && policy.Candidate != 0) Accelerations.Add(cell);
            policy.Wait = wait;
            if (candidate == server && remembered.Count != 0) Forget(cell);
            // Settled or certified decisions need no request-deadline scan.
            if (policy.Owner == candidate || (remembered.Count != 0 && IsRemembered(cell, candidate)))
            { Commit(cell, policy); return; }
            policy.Deadline = policy.Since + wait;
            if (policy.Count != 0)
            {
                policy.Deadline = RequestDeadline(policy.First, policy.Since, now);
                if (policy.Others != null) foreach (Request request in policy.Others)
                    policy.Deadline = Math.Min(policy.Deadline, RequestDeadline(request, policy.Since, now));
            }
            if (now >= policy.Deadline) Commit(cell, policy);
            else { pending.Add(cell); nextDue = Math.Min(nextDue, policy.Deadline); }
        }
        private float RequestDeadline(Request request, float since, float now)
        { return Math.Max(Math.Min(request.Since, now), since) + (directional ? request.Tier == FieldTier.Inner ? innerDelay : request.Tier == FieldTier.Forward ? forwardDelay : outerDelay : Delay); }
        private void SetDeferred(Policy policy, bool value)
        {
            if (policy.Deferred == value) return;
            policy.Deferred = value; deferredCells += value ? 1 : -1;
        }
        internal void Update(List<GridPeer> peers, float now)
        {
            Changes.Clear(); Accelerations.Clear(); dirty.Clear(); seen.Clear(); departed.Clear();
            bool backwards = now < lastTime; lastTime = now;
            if (backwards)
            {
                nextDue = Single.PositiveInfinity;
                foreach (Cell cell in pending)
                {
                    Policy p = policies[cell]; p.Since = now; p.Deadline = now + p.Wait;
                    p.First.Since = now;
                    if (p.Others != null) for (int i = 0; i < p.Others.Count; i++) { Request request = p.Others[i]; request.Since = now; p.Others[i] = request; }
                    nextDue = Math.Min(nextDue, p.Deadline);
                }
                foreach (Policy p in policies.Values)
                    if (p.CooldownUntil != 0f) p.CooldownUntil = now + overlapCooldown;
            }
            bool changed = false;
            foreach (GridPeer peer in peers)
            {
                if (peer.Uid == 0 || peer.Uid == server || !seen.Add(peer.Uid)) continue;
                PeerState state;
                bool exists = previous.TryGetValue(peer.Uid, out state);
                if (exists) state.Refreshed = false;
                float yaw = Yaw(peer.Yaw, exists ? state.Yaw : 0f);
                int heading = !directional ? 0 : peer.Coverage >= 0 && peer.Coverage < CoverageCount ? peer.Coverage :
                    exists && (Single.IsNaN(peer.Yaw) || Single.IsInfinity(peer.Yaw)) ? state.Heading : ((int)((yaw + 22.5f) / 45f) & 7);
                if (!exists || !state.Cell.Equals(peer.Cell) || state.Heading != heading)
                {
                    if (exists) MoveContribution(peer.Uid, state, peer.Cell, heading, now);
                    else { state = new PeerState(); previous.Add(peer.Uid, state); }
                    state.Cell = peer.Cell; state.Yaw = yaw;
                    state.Heading = heading;
                    state.Refreshed = true;
                    if (!exists) Contribute(peer.Uid, state, true, now);
                    changed = true; Count(ref PlayerUpdates, 1);
                }
            }
            foreach (KeyValuePair<long, PeerState> peer in previous) if (!seen.Contains(peer.Key)) departed.Add(peer.Key);
            foreach (long uid in departed) { Contribute(uid, previous[uid], false, now); previous.Remove(uid); changed = true; }
            if (changed) { Count(ref Rebuilds, 1); foreach (Cell cell in dirty) Evaluate(cell, now); }
            if (pending.Count == 0) { nextDue = Single.PositiveInfinity; return; }
            if (now < nextDue) return;
            due.Clear(); foreach (Cell cell in pending) due.Add(cell);
            nextDue = Single.PositiveInfinity;
            foreach (Cell cell in due)
            {
                Policy policy; Count(ref TimerVisits, 1);
                if (!policies.TryGetValue(cell, out policy)) continue;
                if (now >= policy.Deadline) Commit(cell, policy);
                else nextDue = Math.Min(nextDue, policy.Deadline);
            }
            due.Clear();
        }
        internal bool Directional { get { return directional; } }
        internal bool HasPeers { get { return previous.Count != 0; } }
        internal int CachedCells { get { return policies.Count; } }
        internal int ReusablePolicies { get { return reusable.Count; } }
        internal int DeferredCells { get { return deferredCells; } }
    }

    internal abstract class AuthorityNode
    {
        internal Cell Cell;
        internal bool Managed, Removed;
        internal long ManagedOwner;
        internal long ConfirmedFieldOwner;
        internal AuthorityWorld.ObjectBucket IndexedBucket;
        internal float NotBefore;
        internal bool WaitingForFieldTimer;
        internal abstract long CurrentOwner { get; }
        // Done: safely applied/no-op. Pending: retry only this node next cycle.
        // Dropped: retain native ownership and await a relevant new event.
        internal abstract ApplyResult Apply(long target);
        internal virtual bool CanRemember(long fieldOwner)
        { return Managed && ManagedOwner == fieldOwner && CurrentOwner == fieldOwner; }
    }
    internal enum ApplyResult { Done, Pending, Dropped }

    // Indexed work scheduling. Stable cells never enumerate their object lists.
    internal sealed class AuthorityWorld
    {
        internal sealed class ObjectBucket
        {
            internal readonly HashSet<AuthorityNode> Nodes = new HashSet<AuthorityNode>();
            internal long Target;
            internal int Unconfirmed;
            internal bool PriorityCached;
            internal int Priority;
        }
        internal readonly AuthorityGrid Grid;
        internal Action PriorityLaneCompleted = null;
        private readonly Dictionary<Cell, ObjectBucket> buckets = new Dictionary<Cell, ObjectBucket>();
        private readonly HashSet<AuthorityNode> pending = new HashSet<AuthorityNode>();
        private readonly List<ObjectBucket> priorityBuckets = new List<ObjectBucket>();
        private readonly List<AuthorityNode>[] work = {
            new List<AuthorityNode>(), new List<AuthorityNode>(),
            new List<AuthorityNode>(), new List<AuthorityNode>() };
        private readonly bool keepOnExit;
        private readonly long server;
        private float lastTime;
        internal int Attempts;
        internal int SkippedCells;
        // Test instrumentation is compiled out of the production DLL, including argument evaluation.
#if TML_RELEASE
        [System.Diagnostics.Conditional("TML_TEST_METRICS")]
#endif
        private static void Count(ref int counter, int amount) { counter += amount; }

        internal AuthorityWorld(long server, float delay, GridCoveragePattern pattern = GridCoveragePattern.Square3x3, bool keepOnExit = true,
            bool directional = false, float innerDelay = 0f, float outerDelay = 3f, bool forward = false, float forwardDelay = .2f, float overlapCooldown = 0f)
        { Grid = new AuthorityGrid(server, delay, pattern, directional, innerDelay, outerDelay, keepOnExit, forward, forwardDelay, overlapCooldown, !keepOnExit); this.keepOnExit = keepOnExit; this.server = server; }
        internal void Add(AuthorityNode node, float now, bool schedule = true)
        {
            node.Removed = false;
            ObjectBucket bucket = Bucket(node.Cell);
            if (!bucket.Nodes.Add(node)) return;
            node.IndexedBucket = bucket;
            node.ConfirmedFieldOwner = 0;
            if (bucket.Target != 0) bucket.Unconfirmed++;
            if (keepOnExit) Grid.Forget(node.Cell);
            if (schedule) Queue(node, now); else node.Managed = false;
        }
        internal void Remove(AuthorityNode node)
        {
            Detach(node);
            node.Removed = true; node.Managed = false; pending.Remove(node);
        }
        internal void Move(AuthorityNode node, Cell next, float now, bool schedule = true)
        {
            if (node.Removed || node.Cell.Equals(next)) return;
            Detach(node);
            node.Cell = next;
            ObjectBucket bucket = Bucket(next); bucket.Nodes.Add(node);
            node.IndexedBucket = bucket;
            node.ConfirmedFieldOwner = 0;
            if (bucket.Target != 0) bucket.Unconfirmed++;
            if (keepOnExit) Grid.Forget(next);
            // Use the cell timer for mobile residence; genuine use/readiness
            // guards stay in Apply and cannot be bypassed by a zero timer.
            if (schedule) Queue(node, now + Grid.Wait(next)); else { pending.Remove(node); node.Managed = false; }
        }
        internal void Changed(AuthorityNode node, float now, bool schedule = true)
        {
            if (!node.Removed) { Invalidate(node); if (schedule) Queue(node, now + Grid.Wait(node.Cell)); else { pending.Remove(node); node.Managed = false; } }
        }
        internal void Received(AuthorityNode node, float now)
        {
            // Recognition must not shorten an existing animal residence or
            // other scheduled stability barrier. Ownership is already pinned
            // by the receipt; this is only its remaining maintenance job.
            if (!node.Removed) { Invalidate(node); Queue(node, Math.Max(now, node.NotBefore)); }
        }
        internal void RenewRequest(AuthorityNode node, float now)
        {
            // The field policy retains its own deadline. A fresh request may
            // retry a previously refused object without replaying the old job.
            if (!node.Removed) { Invalidate(node); Queue(node, now); }
        }
        internal void Tick(List<GridPeer> peers, float now)
        {
            if (peers.Count == 0 && !Grid.HasPeers && Grid.CachedCells == 0 && pending.Count == 0)
            { Grid.Changes.Clear(); Grid.Accelerations.Clear(); return; }
            if (now < lastTime) foreach (AuthorityNode node in pending) node.NotBefore = now + Grid.Wait(node.Cell);
            lastTime = now;
            Grid.Update(peers, now);
            foreach (Cell cell in Grid.Changes)
            {
                // An empty coverage decision is not an ownership-return job.
                // Drop only the field policy; loaded objects retain their owners.
                if (keepOnExit && Grid.Owner(cell) == 0) continue;
                ObjectBucket bucket;
                if (!buckets.TryGetValue(cell, out bucket)) continue;
                long target = Grid.Owner(cell);
                if (keepOnExit && Grid.IsRemembered(cell, target) && bucket.Target == target && bucket.Unconfirmed == 0)
                { Count(ref SkippedCells, 1); continue; }
                bucket.Target = keepOnExit && target != server ? target : 0;
                bucket.Unconfirmed = bucket.Target == 0 ? 0 : bucket.Nodes.Count;
                bool haveWait = false; float fieldWait = 0f;
                foreach (AuthorityNode node in bucket.Nodes)
                {
                    node.ConfirmedFieldOwner = 0;
                    // A changed/committed field supersedes its former deadline.
                    // Do not add another full delay after a shortened timer.
                    if (node.WaitingForFieldTimer) { node.WaitingForFieldTimer = false; node.NotBefore = now; }
                    if (pending.Add(node)) node.NotBefore = now;
                    else if (Grid.Directional)
                    {
                        if (!haveWait) { fieldWait = Grid.Wait(cell); haveWait = true; }
                        node.NotBefore = Math.Min(node.NotBefore, now + fieldWait);
                    }
                }
            }
            if (pending.Count == 0) return;
            // Four reusable lanes avoid sorting and per-object distance checks.
            // Snapshot first: native callbacks may add/remove pending jobs.
            ClearPriorityBatch();
            try
            {
                // The bucket reference avoids a dictionary lookup per node.
                // This batch ends before any native/mod callback can run.
                foreach (AuthorityNode node in pending) work[BatchPriority(node)].Add(node);
            }
            finally { ClearPriorityBatch(); }
            try
            {
                // Red, forward, outer, then vacated/default ownership. Every
                // due lane runs this pass: no new quota, timer or starvation.
                foreach (List<AuthorityNode> lane in work)
                {
                    foreach (AuthorityNode node in lane)
                    {
                        if (node.Removed || !pending.Contains(node)) continue;
                        if (Grid.Accelerations.Count != 0 && Grid.Accelerations.Contains(node.Cell))
                            node.NotBefore = Math.Min(node.NotBefore, now + Grid.Wait(node.Cell));
                        if (now < node.NotBefore) continue;
                        Count(ref Attempts, 1);
                        ApplyResult result = node.Apply(Grid.Owner(node.Cell));
                        if (result != ApplyResult.Pending) pending.Remove(node);
                        if (result == ApplyResult.Dropped) node.Managed = false;
                        else if (result == ApplyResult.Done && keepOnExit && !node.Removed) Confirm(node);
                    }
                    // Flush a red tail before beginning lower-priority work.
                    if (lane.Count != 0 && PriorityLaneCompleted != null) PriorityLaneCompleted();
                }
            }
            finally { foreach (List<AuthorityNode> lane in work) lane.Clear(); }
        }
        internal void Dismiss(AuthorityNode node) { Invalidate(node); pending.Remove(node); node.Managed = false; }
        // Parking a prerequisite must retain the established owner's native
        // release protection. This is not a refusal or an ownership reset.
        internal void Suspend(AuthorityNode node) { pending.Remove(node); }
        internal void WaitForFieldTimer(AuthorityNode node, float now)
        {
            node.WaitingForFieldTimer = true;
            node.NotBefore = now + Grid.Remaining(node.Cell, now);
        }
        internal int BatchPriority(AuthorityNode node)
        {
            ObjectBucket bucket = node.IndexedBucket;
            // A singleton has no repeated field work to amortise the cache.
            if (bucket == null || bucket.Nodes.Count == 1) return Grid.WorkPriority(node.Cell);
            if (!bucket.PriorityCached)
            {
                bucket.Priority = Grid.WorkPriority(node.Cell); bucket.PriorityCached = true;
                priorityBuckets.Add(bucket);
            }
            return bucket.Priority;
        }
        internal void ClearPriorityBatch()
        {
            foreach (ObjectBucket bucket in priorityBuckets) bucket.PriorityCached = false;
            priorityBuckets.Clear();
        }
        private void Queue(AuthorityNode node, float at) { node.WaitingForFieldTimer = false; node.NotBefore = at; pending.Add(node); }
        private void Invalidate(AuthorityNode node)
        {
            if (!keepOnExit) return;
            Grid.Forget(node.Cell);
            ObjectBucket bucket;
            if (buckets.TryGetValue(node.Cell, out bucket) && bucket.Target != 0 && node.ConfirmedFieldOwner == bucket.Target)
            { node.ConfirmedFieldOwner = 0; bucket.Unconfirmed++; }
        }
        private void Confirm(AuthorityNode node)
        {
            ObjectBucket bucket;
            if (!buckets.TryGetValue(node.Cell, out bucket) || bucket.Target == 0 ||
                node.ConfirmedFieldOwner == bucket.Target || !node.CanRemember(bucket.Target)) return;
            node.ConfirmedFieldOwner = bucket.Target; bucket.Unconfirmed--;
            Certify(node.Cell, bucket);
        }
        private void Certify(Cell cell, ObjectBucket bucket)
        { if (keepOnExit && bucket.Target != 0 && bucket.Unconfirmed == 0) Grid.Remember(cell, bucket.Target); }
        private void Detach(AuthorityNode node)
        {
            ObjectBucket bucket;
            if (!buckets.TryGetValue(node.Cell, out bucket) || !bucket.Nodes.Remove(node)) return;
            node.IndexedBucket = null;
            bool unconfirmed = bucket.Target != 0 && node.ConfirmedFieldOwner != bucket.Target;
            if (unconfirmed) bucket.Unconfirmed--;
            node.ConfirmedFieldOwner = 0;
            if (bucket.Nodes.Count == 0) { buckets.Remove(node.Cell); Grid.Forget(node.Cell); }
            else if (unconfirmed) Certify(node.Cell, bucket);
        }
        private ObjectBucket Bucket(Cell cell)
        {
            ObjectBucket result;
            if (!buckets.TryGetValue(cell, out result))
            {
                long target = Grid.Owner(cell);
                result = new ObjectBucket { Target = keepOnExit && target != server ? target : 0 };
                buckets.Add(cell, result);
            }
            return result;
        }
        internal int BucketCount { get { return buckets.Count; } }
        internal int PendingCount { get { return pending.Count; } }
    }
}
