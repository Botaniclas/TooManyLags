using System;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    internal sealed class CameraPublisher
    {
        private readonly OwnershipCadence cadence;
        private ZDO previous;
        private ZNet network;
        private ZDOMan world;
        internal CameraPublisher(float interval) { cadence = new OwnershipCadence(interval, .05f); }
        internal void Sample(float now)
        {
            if (!cadence.Due(now)) return;
            ZNet current = ZNet.instance;
            if (!current || current.IsDedicated() || ZNet.IsSinglePlayer || ZDOMan.instance == null)
            { Reset(); return; }
            Player player = Player.m_localPlayer;
            if (!player || current.GetServerPeer() == null) { Reset(); return; }
            ZNetView view = player.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner()) { Reset(); return; }
            ZDO data = view.GetZDO();
            if (data == null || !data.IsValid() || data.m_uid.IsNone() || !ReferenceEquals(ZDOMan.instance.GetZDO(data.m_uid), data)) { Reset(); return; }
            // Horizontal projection ignores pitch and roll. Missing/vertical
            // camera direction restores the server's character-heading fallback.
            GameCamera camera = GameCamera.instance;
            int heading = -1;
            if (camera && !GameCamera.InFreeFly())
            {
                Vector3 forward = camera.transform.forward;
                heading = CameraHeading.Sector(forward.x, forward.z);
            }
            bool fresh = !ReferenceEquals(previous, data) || !ReferenceEquals(network, current) || !ReferenceEquals(world, ZDOMan.instance);
            if (fresh) ZDOExtraData.AddSessionHash(CameraHeading.Key);
            if (fresh || data.GetInt(CameraHeading.Key, -1) != heading) data.Set(CameraHeading.Key, heading);
            previous = data; network = current; world = ZDOMan.instance;
        }
        internal void Reset(bool invalidate = false)
        {
            if (invalidate && previous != null && ReferenceEquals(network, ZNet.instance) && ReferenceEquals(world, ZDOMan.instance) &&
                previous.IsValid() && previous.GetOwner() == ZDOMan.GetSessionID() && ReferenceEquals(world.GetZDO(previous.m_uid), previous))
                previous.Set(CameraHeading.Key, -1);
            previous = null; network = null; world = null;
        }
    }
    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class CameraSamplePatch
    {
        // The native camera has finished positioning/rotation/shake. Sampling
        // runs last among Harmony postfixes and writes only changed sectors.
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix() { Plugin.SampleCamera(); }
    }
    [HarmonyPatch(typeof(GameCamera), "OnDestroy")]
    internal static class CameraLossPatch
    {
        [HarmonyPrefix]
        private static void Prefix(GameCamera __instance)
        { if (GameCamera.instance == __instance) Plugin.InvalidateCamera(); }
    }
}
