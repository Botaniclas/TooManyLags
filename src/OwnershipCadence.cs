using System;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // A constant-time gate on the existing native ReleaseZDOS call. Its native
    // timer and release behavior stay untouched; Authority has no own Update.
    internal sealed class OwnershipCadence
    {
        internal readonly float Interval;
        private float next, previous;
        private bool started;
        internal OwnershipCadence(float interval, float minimum = .1f)
        {
            Interval = Single.IsNaN(interval) || Single.IsInfinity(interval) ? IntervalConfig.Default : Math.Max(minimum, Math.Min(5f, interval));
        }
        internal bool Due(float now)
        {
            if (Single.IsNaN(now) || Single.IsInfinity(now)) return false;
            bool due = !started || now < previous || now >= next;
            previous = now;
            if (due) { started = true; next = now + Interval; }
            return due;
        }
    }

    [HarmonyPatch(typeof(ZDOMan), "ReleaseZDOS")]
    internal static class CyclePatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        { if (Plugin.Controller != null) Plugin.Controller.Poll(Time.unscaledTime); }
    }
}
