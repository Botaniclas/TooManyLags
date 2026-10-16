using System;

namespace TooManyLagsStandalone
{
    internal enum HandoffPhase { Prepare, Commit, Reconciling, Blocked, Complete, Obsolete }
    // No timeout authorizes an ownership write. Only the real source commits.
    internal sealed class HandoffTransaction
    {
        internal readonly long Token, Source, Target;
        internal readonly ushort Revision;
        internal HandoffPhase Phase;
        internal float Next, Started;
        internal HandoffTransaction(long token, long source, long target, ushort revision, float now)
        {
            if (token <= 0 || source == 0 || target == 0 || source == target || revision == UInt16.MaxValue) throw new ArgumentException("Invalid handoff identity.");
            Token = token; Source = source; Target = target; Revision = revision; Started = Next = now;
        }
        internal bool Observe(long owner, ushort revision)
        {
            if (owner == Target && revision == Revision + 1) { Phase = HandoffPhase.Complete; return true; }
            // Import writes owner/revision separately. Half receipts cannot
            // complete, invalidate or authorize another concurrent transfer.
            if ((owner == Source && revision == Revision) || (owner == Target && revision == Revision) || (owner == Source && revision == Revision + 1)) return false;
            Phase = HandoffPhase.Obsolete; return true;
        }
        internal bool MatchesSource(long owner, ushort revision) { return owner == Source && revision == Revision; }
        internal void Prepared(bool ready, float now) { if (Phase != HandoffPhase.Prepare) return; if (ready) Phase = HandoffPhase.Commit; Next = now + (ready ? 0 : .5f); }
        internal void Block(float now) { if (Phase == HandoffPhase.Commit) { Phase = HandoffPhase.Blocked; Next = now + 5f; } }
        internal void Wake(float now) { if (Phase == HandoffPhase.Blocked) { Phase = HandoffPhase.Prepare; Next = now; } }
    }
}
