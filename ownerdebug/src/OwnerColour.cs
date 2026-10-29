using System;

namespace BotanicOwnerDebug
{
    internal enum OwnerKind { Unknown, Unowned, Server, Player, OldOwner, NewOwner, Playback, Correction }
    internal struct OwnerKey : IEquatable<OwnerKey>
    {
        internal OwnerKind Kind; internal long Uid;
        internal static OwnerKey Resolve(bool known, long owner, long server)
        {
            if (!known) return new OwnerKey { Kind = OwnerKind.Unknown };
            if (owner == 0) return new OwnerKey { Kind = OwnerKind.Unowned };
            return new OwnerKey { Kind = owner == server && server != 0 ? OwnerKind.Server : OwnerKind.Player, Uid = owner };
        }
        public bool Equals(OwnerKey other) { return Kind == other.Kind && Uid == other.Uid; }
        public override bool Equals(object other) { return other is OwnerKey && Equals((OwnerKey)other); }
        public override int GetHashCode() { return (int)Kind * 397 ^ Uid.GetHashCode(); }
    }
    internal struct Rgb { internal float R, G, B; internal Rgb(float r, float g, float b) { R = r; G = g; B = b; } }
    internal static class OwnerColour
    {
        internal static Rgb Get(OwnerKey key, int seed)
        {
            if (key.Kind == OwnerKind.OldOwner) return new Rgb(.2f, .55f, 1f);
            if (key.Kind == OwnerKind.NewOwner) return new Rgb(.15f, 1f, .25f);
            if (key.Kind == OwnerKind.Playback) return new Rgb(1f, 0f, 0f);
            if (key.Kind == OwnerKind.Correction) return new Rgb(1f, .65f, .05f);
            if (key.Kind == OwnerKind.Server) return new Rgb(1f, 1f, 1f);
            if (key.Kind == OwnerKind.Unowned) return new Rgb(.18f, .18f, .18f);
            if (key.Kind == OwnerKind.Unknown) return new Rgb(.22f, .3f, .4f);
            if (paletteSeed != seed) { Reset(); paletteSeed = seed; }
            if (!shuffled)
            {
                for (int i = 0; i < first.Length; i++) first[i] = i;
                var random = new System.Random(Guid.NewGuid().GetHashCode() ^ seed);
                for (int i = first.Length - 1; i > 0; i--)
                { int j = random.Next(i + 1), value = first[i]; first[i] = first[j]; first[j] = value; }
                shuffled = true;
            }
            int slot;
            if (!slots.TryGetValue(key.Uid, out slot))
            {
                slot = slots.Count % palette.Length;
                slots.Add(key.Uid, slot);
            }
            // Shuffle only the requested first-four pool. Keep assigned slots
            // stable and never consume gameplay/Unity RNG.
            int offset = (int)((uint)seed % 8);
            return palette[slot < 4 ? first[slot] : 4 + (slot - 4 + offset) % 8];
        }
        // Purple, green, blue and yellow reserved for the first four players.
        // Later assignments retain twelve distinct colours. No game RNG.
        private static readonly Rgb[] palette = {
            new Rgb(.55f,0,1), new Rgb(0,1,0), new Rgb(0,0,1), new Rgb(1,1,0),
            new Rgb(1,0,0), new Rgb(0,1,1),
            new Rgb(1,.45f,0), new Rgb(.55f,1,0), new Rgb(0,1,.45f),
            new Rgb(0,.55f,1), new Rgb(1,0,1), new Rgb(1,0,.45f)
        };
        private static readonly System.Collections.Generic.Dictionary<long, int> slots =
            new System.Collections.Generic.Dictionary<long, int>();
        private static int paletteSeed;
        private static readonly int[] first = new int[4];
        private static bool shuffled;
        internal static void Reset() { slots.Clear(); shuffled = false; }
    }
}
