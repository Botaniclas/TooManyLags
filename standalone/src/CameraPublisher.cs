using System;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsStandalone
{
    internal sealed class CameraPublisher
    {
        private readonly OwnershipCadence cadence;
        private readonly CameraPrediction prediction = new CameraPrediction();
        private ZDO previous;
        private ZNet network;
        private ZDOMan world;
        private Vector3 previousPosition;
        internal CameraPublisher(float interval) { cadence = new OwnershipCadence(interval, .05f); }
        internal void Configure(float interval)
        {
            cadence.Configure(interval, .05f);
            prediction.Reset();
        }
        internal void Sample(float now)
        {
            if (!cadence.Due(now)) return;
            ZNet current = ZNet.instance;
            if (!current || current.IsDedicated() || ZNet.IsSinglePlayer || ZDOMan.instance == null)
            { Reset(); return; }
            // Do not advertise a locally configured angle before the host's
            // authenticated welcome supplies the session angle.
            if (!current.IsServer() && (Plugin.Network == null || !Plugin.Network.Confirmed)) { Reset(); return; }
            Player player = Player.m_localPlayer;
            if (!player || (!current.IsServer() && current.GetServerPeer() == null)) { Reset(); return; }
            ZNetView view = player.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.IsOwner()) { Reset(); return; }
            ZDO data = view.GetZDO();
            if (data == null || !data.IsValid() || data.m_uid.IsNone() || !ReferenceEquals(ZDOMan.instance.GetZDO(data.m_uid), data)) { Reset(); return; }
            bool fresh = !ReferenceEquals(previous, data) || !ReferenceEquals(network, current) || !ReferenceEquals(world, ZDOMan.instance);
            if (fresh) prediction.Reset();
            if (CameraHeading.Prediction > 0f)
            {
                Vector3 position = data.GetPosition();
                float dx = position.x - previousPosition.x, dy = position.y - previousPosition.y, dz = position.z - previousPosition.z;
                if (dx > 16f || dx < -16f || dy > 16f || dy < -16f || dz > 16f || dz < -16f) prediction.Reset();
                previousPosition = position;
            }
            // Horizontal projection ignores pitch and roll. Missing/vertical
            // camera direction restores the server's character-heading fallback.
            GameCamera camera = GameCamera.instance;
            int heading = -1;
            if (camera && !GameCamera.InFreeFly())
            {
                Vector3 forward = camera.transform.forward;
                heading = prediction.Sample(forward.x, forward.z, now, CameraHeading.Overlap, CameraHeading.Prediction);
            }
            else prediction.Reset();
            if (fresh) ZDOExtraData.AddSessionHash(CameraHeading.Key);
            if (fresh || data.GetInt(CameraHeading.Key, -1) != heading)
            {
                data.Set(CameraHeading.Key, heading);
                ZDOMan.instance.ForceSendZDO(data.m_uid);
                // A listen host has no incoming packet for its own camera.
                if (current.IsServer() && Plugin.Controller != null) Plugin.Controller.CameraChanged(data);
            }
            previous = data; network = current; world = ZDOMan.instance;
        }
        internal void Reset(bool invalidate = false)
        {
            if (invalidate && previous != null && ReferenceEquals(network, ZNet.instance) && ReferenceEquals(world, ZDOMan.instance) &&
                previous.IsValid() && previous.GetOwner() == ZDOMan.GetSessionID() && ReferenceEquals(world.GetZDO(previous.m_uid), previous) && previous.GetInt(CameraHeading.Key, -1) != -1)
            {
                previous.Set(CameraHeading.Key, -1);
                world.ForceSendZDO(previous.m_uid);
                if (network.IsServer() && Plugin.Controller != null) Plugin.Controller.CameraChanged(previous);
            }
            prediction.Reset(); previous = null; network = null; world = null;
        }
    }
    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class CameraSamplePatch
    {
        // The native camera has finished positioning/rotation/shake. Sampling
        // runs last among Harmony postfixes and writes only changed coverage.
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
