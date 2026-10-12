namespace TooManyLags.GridHandoffs
{
    // One ordered command stream per authenticated connection. A status request
    // also fences an uncertain/unsent commit, so it cannot execute afterwards.
    internal sealed class CommitFence
    {
        private long decided;
        internal bool Begin(long token)
        { if (token <= decided || token <= 0) return false; decided = token; return true; }
        internal void Settle(long token) { if (token > decided) decided = token; }
        internal bool Decided(long token) { return token > 0 && token <= decided; }
    }
}
