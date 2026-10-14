using System;
using System.Reflection;
using UnityEngine;

namespace TooManyLags
{
    // Native state spans the burning coroutine AND its delayed StopAOE.
    // Read only during existing use/handoff checks, never on a new timer.
    internal static class IncineratorProtection
    {
        private static readonly Func<Incinerator, bool> read = Bind();
        internal static bool Active = false;
        internal static Func<ZNetPeer, bool> Client = null;
        internal static bool Supported { get { return read != null; } }
        private static Func<Incinerator, bool> Bind()
        {
            try { return FieldReader.Create<Incinerator, bool>(typeof(Incinerator).GetField("isInUse", BindingFlags.Instance | BindingFlags.NonPublic)); }
            catch (Exception) { return null; }
        }
        internal static bool Busy(Incinerator station) { return station && read != null && read(station); }
        internal static bool Allow(Container inventory, long sender, string response)
        {
            if (!Active || !inventory) return true;
            ZNetView view = inventory.m_rootObjectOverride ? inventory.m_rootObjectOverride : inventory.GetComponentInParent<ZNetView>(true);
            if (!view || !view.IsValid() || !view.IsOwner() || sender == view.GetZDO().GetOwner()) return true;
            Incinerator station = view.GetComponent<Incinerator>();
            if (!station || station.m_container != inventory || !Busy(station)) return true;
            // A native refusal, not a delayed opening or a change to IsInUse:
            // the coroutine itself checks Container.IsInUse and must stay native.
            view.InvokeRPC(sender, response, false); return false;
        }
    }
}
