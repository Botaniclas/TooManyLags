namespace TooManyLags.GridHandoffs
{
    // Join-only retries. No world/object history and no sends after confirmation.
    internal sealed class CapabilityGreeting
    {
        private bool offered, started, confirmed;
        private double next, until, last;
        internal bool Sent { get { return started; } }
        internal bool Welcome(bool? acknowledgement)
        {
            if (confirmed || (acknowledgement ?? Sent)) { confirmed = true; return true; }
            offered = true; return false;
        }
        internal bool Waiting(double now) { return offered && !confirmed && (!started || now < until); }
        internal bool Due(double now)
        {
            if (!offered || confirmed) return false;
            if (!started || now < last) { started = true; next = now; until = now + 30; }
            last = now;
            if (now >= until || now < next) return false;
            next = now + 2;
            return true;
        }
    }
}
