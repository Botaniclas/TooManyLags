namespace TooManyLagsStandalone
{
    // Inline in an existing indexed object/AI record, not a world-wide cache.
    internal struct RecipientRequirement
    {
        internal long Owner;
        internal ushort Revision;
        internal uint State;
        internal bool Matches(long owner, ushort revision) { return Owner != 0 && Owner == owner && Revision == revision; }
        internal void Include(long owner, ushort revision, uint state)
        { if (!Matches(owner, revision) || state > State) State = state; Owner = owner; Revision = revision; }
    }
}
