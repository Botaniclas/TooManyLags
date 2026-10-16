using System;

namespace BotanicOwnerDebug
{
    internal enum OwnerKind { Unknown, Unowned, Server, Player }
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
            if (key.Kind == OwnerKind.Server) return new Rgb(1f, 1f, 1f);
            if (key.Kind == OwnerKind.Unowned) return new Rgb(.18f, .18f, .18f);
            if (key.Kind == OwnerKind.Unknown) return new Rgb(.22f, .3f, .4f);
            if (paletteSeed != seed) { Reset(); paletteSeed = seed; }
            int slot;
            if (!slots.TryGetValue(key.Uid, out slot))
            {
                slot = slots.Count % palette.Length;
                slots.Add(key.Uid, slot);
            }
            int offset = (int)((uint)seed % 6);
            return palette[(slot / 6) * 6 + (slot + offset) % 6];
        }
        // Six distinct hues, then six fixed intermediate hues. No game RNG.
        private static readonly Rgb[] palette = {
            new Rgb(1,0,0), new Rgb(1,1,0), new Rgb(0,1,0),
            new Rgb(0,1,1), new Rgb(0,0,1), new Rgb(1,0,1),
            new Rgb(1,.45f,0), new Rgb(.55f,1,0), new Rgb(0,1,.45f),
            new Rgb(0,.55f,1), new Rgb(.55f,0,1), new Rgb(1,0,.45f)
        };
        private static readonly System.Collections.Generic.Dictionary<long, int> slots =
            new System.Collections.Generic.Dictionary<long, int>();
        private static int paletteSeed;
        internal static void Reset() { slots.Clear(); }
    }
}
