using System;
using System.Collections.Generic;

namespace TooManyLags.PlayerSimulation
{
    internal struct RegionPeer : IEquatable<RegionPeer>
    {
        internal long Uid;
        internal Cell Reference, Character;
        internal bool ReferenceValid, CharacterValid;
        internal RegionPeer(long uid, Cell reference, bool referenceValid, Cell character, bool characterValid)
        { Uid = uid; Reference = reference; ReferenceValid = referenceValid; Character = character; CharacterValid = characterValid; }
        public bool Equals(RegionPeer other)
        { return Uid == other.Uid && ReferenceValid == other.ReferenceValid && CharacterValid == other.CharacterValid && Reference.Equals(other.Reference) && Character.Equals(other.Character); }
        public override bool Equals(object other) { return other is RegionPeer && Equals((RegionPeer)other); }
        public override int GetHashCode() { return Uid.GetHashCode(); }
    }

    // A conservative absence test, never an ownership certificate. Native
    // PointInsideActiveArea reaches at most 1.5 native 64m zones from its
    // reference zone centre. +/-2 zone boxes cover it, the largest supported
    // fine footprint, and the exact +/-96m boundary. Extra positives are safe.
    // Character and reference centres can differ during teleport/packet delay.
    internal sealed class RegionContentionGate
    {
        private readonly List<RegionPeer> previous = new List<RegionPeer>();
        private readonly HashSet<long> shared = new HashSet<long>();
        private readonly HashSet<Cell> zones = new HashSet<Cell>();
        private bool uncertain;
        internal int Rebuilds, PairTests;
        internal int SharedPlayers { get { return shared.Count; } }
        internal HashSet<Cell> ZoneKeys { get { return zones; } }
        internal int CachedZones { get { return zones.Count; } }
        internal int CachedPeers { get { return previous.Count; } }
        internal bool Shared(long uid) { return shared.Contains(uid); }
        internal bool Contains(Cell fine, float size)
        { return uncertain || zones.Contains(Cell.Zone(fine, size)); }
        internal bool Update(List<RegionPeer> peers)
        {
            bool changed = peers.Count != previous.Count;
            if (!changed) for (int i = 0; i < peers.Count; i++) if (!peers[i].Equals(previous[i])) { changed = true; break; }
            if (!changed) return false;
            previous.Clear(); previous.AddRange(peers); shared.Clear(); zones.Clear(); uncertain = false; Rebuilds++;
            // An invalid reference cannot establish absence. Retain normal
            // grid handling until that ready connection supplies valid data.
            foreach (RegionPeer peer in peers) if (!peer.ReferenceValid) uncertain = true;
            for (int i = 0; i < peers.Count; i++) for (int j = i + 1; j < peers.Count; j++)
            {
                PairTests++;
                RegionPeer a = peers[i], b = peers[j];
                if (uncertain || Near(a.Reference, b.Reference) ||
                    (a.CharacterValid && Near(a.Character, b.Reference)) ||
                    (b.CharacterValid && Near(a.Reference, b.Character)) ||
                    (a.CharacterValid && b.CharacterValid && Near(a.Character, b.Character)))
                { shared.Add(a.Uid); shared.Add(b.Uid); }
            }
            if (uncertain) foreach (RegionPeer peer in peers) shared.Add(peer.Uid);
            foreach (RegionPeer peer in peers) if (Shared(peer.Uid))
            {
                if (peer.ReferenceValid) Add(peer.Reference);
                if (peer.CharacterValid && (!peer.ReferenceValid || !peer.Character.Equals(peer.Reference))) Add(peer.Character);
            }
            return true;
        }
        private static bool Near(Cell a, Cell b) { return Math.Abs(a.X - b.X) <= 4 && Math.Abs(a.Z - b.Z) <= 4; }
        private void Add(Cell center)
        { for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++) zones.Add(new Cell(center.X + x, center.Z + z)); }
        internal void Clear() { previous.Clear(); shared.Clear(); zones.Clear(); uncertain = false; }
    }
}
