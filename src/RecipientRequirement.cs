namespace TooManyLags.GridHandoffs
{
    // Reuses the existing weak object record; no extra tracking collection.
    internal sealed class RecipientRequirement
    {
        internal long Owner;
        internal uint Revision, State;

        internal bool Matches(long owner, uint revision)
        { return Owner == owner && Revision == revision; }

        internal void Include(long owner, uint revision, uint state)
        {
            // A later server observation must not lower the source's state floor.
            if (!Matches(owner, revision) || state > State) State = state;
            Owner = owner; Revision = revision;
        }
    }
}
