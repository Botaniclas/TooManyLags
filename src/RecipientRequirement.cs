namespace TooManyLags.GridHandoffs
{
    // Stored inline in the existing loaded-object node; no separate cache entry.
    internal struct RecipientRequirement
    {
        internal long Owner;
        internal uint Revision, State;

        internal bool Matches(long owner, uint revision)
        { return Owner != 0 && Owner == owner && Revision == revision; }

        internal void Include(long owner, uint revision, uint state)
        {
            // A later server observation must not lower the source's state floor.
            if (!Matches(owner, revision) || state > State) State = state;
            Owner = owner; Revision = revision;
        }
    }
}
