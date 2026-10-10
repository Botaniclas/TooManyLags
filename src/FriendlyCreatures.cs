namespace TooManyLags
{
    internal static class FriendlyCreatures
    {
        // Native player-allied creatures include magic roots without Tameable.
        // Ordinary taming keeps the creature's original faction.
        internal static bool IsFriendly(Character actor)
        { return actor && !actor.IsPlayer() && actor.GetFaction() == Character.Faction.Players; }
    }
}
