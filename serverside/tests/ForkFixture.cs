namespace Valheim_Serverside
{
    public sealed class ServersidePlugin : BepInEx.BaseUnityPlugin
    {
        private static bool installed = true;
        public static bool IsInstalled() { return installed; }
    }
}
namespace Valheim_Serverside.Features
{
    public static class FireControl
    {
        private static bool IsSource(int prefab, out bool fire) { fire = prefab == 12; return prefab == 11 || fire; }
        internal static readonly System.Collections.Generic.List<ZDO> Claimed = new System.Collections.Generic.List<ZDO>();
        internal static void RunTakeOver() { TakeOver(); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void TakeOver()
        { foreach (ZDO data in Claimed) { bool fire; if (IsSource(data.GetPrefab(), out fire)) data.SetOwner(99); } }
        public static class ZDO_SetOwnerInternal_Patch { private static void Postfix() { } }
    }
    public static class Core
    {
        public static class Ship_UpdateOwner_Patch
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            private static bool Prefix(ref Ship __instance)
            { __instance.GetComponent<ZNetView>().GetZDO().SetOwner(99); return false; }
        }
        public static class ZRoutedRpc_RouteRPC_Patch
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            private static void Prefix(ZRoutedRpc.RoutedRPCData __0)
            {
                if (__0.m_methodHash != "RequestRespons".GetStableHashCode()) return;
                bool granted = new ZPackage(__0.m_parameters.GetArray()).ReadBool();
                ZDO data = ZDOMan.instance.GetZDO(__0.m_targetZDO);
                if (granted && data != null) data.SetOwner(__0.m_targetPeerID);
            }
        }
        public static class ZDOMan_ReleaseNearbyZDOS_Patch
        {
            private static bool Prefix(ZDOMan __instance, UnityEngine.Vector3 refPosition, long uid)
            {
                foreach (ZDO data in __instance.Assignments)
                {
                    if (data.User == -1) data.SetOwner(0L);
                    else if (data.Owner == 0) data.SetOwner(99L);
                }
                return false;
            }
        }
    }
}
