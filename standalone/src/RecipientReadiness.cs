using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLagsStandalone
{
    // Probes exist only for actual transfers/delivery failures, and only when
    // gameplay needs delivery. Ordinary ownership changes allocate no entry.
    internal sealed class RecipientReadiness
    {
        private sealed class Proof
        {
            internal ZDO Data; internal ZNetPeer Peer; internal long Owner, Token;
            internal ushort Revision; internal uint Minimum;
            internal float Next; internal bool Ready;
        }
        private readonly AuthorityController controller;
        private readonly Dictionary<ZDOID, Proof> proofs = new Dictionary<ZDOID, Proof>();
        private long sequence;
        internal RecipientReadiness(AuthorityController controller) { this.controller = controller; }
        internal bool Ready(ZDO data)
        {
            uint minimum; long owner = data.GetOwner();
            if (!controller.TryRecipientState(data, owner, out minimum) || !Plugin.Network.Capable(owner)) return true;
            if (!controller.Available(owner, data.GetPosition())) return false;
            if (owner == ZDOMan.GetSessionID() && Plugin.Local != null)
                return Plugin.Local.Ready(data.m_uid, owner, data.OwnerRevision, minimum);
            ZNetPeer peer = ZNet.instance.GetPeer(owner); Proof proof;
            if (!proofs.TryGetValue(data.m_uid, out proof) || !ReferenceEquals(proof.Data, data) ||
                !ReferenceEquals(proof.Peer, peer) || proof.Owner != owner || proof.Revision != data.OwnerRevision)
            { proof = new Proof { Data = data, Peer = peer, Owner = owner, Revision = data.OwnerRevision, Minimum = minimum, Token = ++sequence }; proofs[data.m_uid] = proof; }
            if (minimum > proof.Minimum) { proof.Minimum = minimum; proof.Ready = false; proof.Next = 0; proof.Token = ++sequence; }
            if (proof.Ready) return true;
            if (Time.unscaledTime >= proof.Next)
            {
                // Retries keep their identity so RTT above the retry interval
                // cannot invalidate every reply. Only new requirements/unload
                // retire the previous proof token.
                proof.Next = Time.unscaledTime + .5f;
                ZDOMan.instance.ForceSendZDO(owner, data.m_uid);
                Plugin.Network.Send(owner, Message.Ready, delegate(ZPackage p)
                { p.Write(proof.Token); p.Write(data.m_uid); p.Write(owner); p.Write(proof.Revision); p.Write(proof.Minimum); });
            }
            return false;
        }
        internal void Receive(long sender, Message message, ZPackage packet)
        {
            long token = packet.ReadLong(); ZDOID id = packet.ReadZDOID(); Proof proof;
            if (!proofs.TryGetValue(id, out proof) || proof.Token != token || proof.Owner != sender ||
                !ReferenceEquals(proof.Peer, ZNet.instance.GetPeer(sender))) return;
            if (message == Message.ReadyLost) { proof.Ready = false; proof.Next = 0; proof.Token = ++sequence; }
            else proof.Ready = packet.ReadBool();
            RequestRouter.Flush(id, Time.unscaledTime);
        }
        internal void Forget(ZDOID id) { proofs.Remove(id); }
        internal void Reset() { proofs.Clear(); }
    }
}
