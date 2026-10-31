using System;
using UnityEngine;

namespace TooManyLags
{
    // Publish the final single-object cutoff through the ordinary native
    // receiver immediately. Preserve serializers/compressors and revision rules;
    // do not drain unrelated forced world updates or add a second owner write.
    internal static class MobNativePublication
    {
        internal static void Send(ZDO data, ZNetPeer peer)
        {
            if (data == null || !data.IsValid() || peer == null || !peer.IsReady() || peer.m_rpc == null || data.OwnerRevision > UInt16.MaxValue) return;
            ZPackage body = new ZPackage(); data.Serialize(body);
            if (body.Size() > 65536) throw new InvalidOperationException("Mob cutoff native state exceeds 64 KiB.");
            ZPackage packet = new ZPackage(); packet.Write(0); packet.Write(data.m_uid);
            packet.Write((ushort)data.OwnerRevision); packet.Write(data.DataRevision); packet.Write(data.GetOwner()); packet.Write(data.GetPosition());
            packet.Write(body); packet.Write(ZDOID.None);
            peer.m_rpc.Invoke("ZDOData", packet);
        }
    }
}
