using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsStandalone
{
    // Vanilla Ship.UpdateOwner moves ownership back onboard when its local
    // owner is on shore. Do not fight that native policy with grid handoffs.
    internal static class ShipTransferGuard
    {
        private static readonly Func<Ship, List<Player>> passengers = Bind();
        private static Func<Ship, List<Player>> Bind()
        {
            var field = AccessTools.Field(typeof(Ship), "m_players");
            if (field == null || field.IsStatic || field.FieldType != typeof(List<Player>))
                throw new NotSupportedException("Native ship passenger contract changed.");
            return FieldReader.Create<Ship, List<Player>>(field);
        }
        internal static void Validate() { if (passengers == null) throw new NotSupportedException("Native ship passenger reader unavailable."); }
        internal static bool Allows(Ship ship, long target)
        {
            if (!ship) return true;
            bool occupied = false;
            foreach (Player player in passengers(ship))
            {
                if (!player) continue;
                long owner = player.GetOwner();
                if (owner == target) return true;
                if (owner != 0) occupied = true;
            }
            return !occupied;
        }
    }
}
