using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
namespace FiresGhettoNetworkMod
{
    internal static class HelmOwnership {internal static bool ServerOwnsShips=false;internal static ConfigEntry<bool> ConfigEnabled=new ConfigEntry<bool>(true);}
    internal static class VanillaAccess
    {
        internal static readonly Action<ZRoutedRpc,ZRoutedRpc.RoutedRPCData> HandleRoutedRpc=AccessTools.MethodDelegate<Action<ZRoutedRpc,ZRoutedRpc.RoutedRPCData>>(AccessTools.Method(typeof(ZRoutedRpc),"HandleRoutedRPC"));
        internal static readonly Action<ZRoutedRpc,ZRoutedRpc.RoutedRPCData> RouteRpc=AccessTools.MethodDelegate<Action<ZRoutedRpc,ZRoutedRpc.RoutedRPCData>>(AccessTools.Method(typeof(ZRoutedRpc),"RouteRPC"));
    }
    internal static class StationRouter
    {
        internal static ConfigEntry<bool> ConfigEnabled=new ConfigEntry<bool>(true);
        internal static int Routes,Deliveries;
        [MethodImpl(MethodImplOptions.NoInlining)] internal static bool TryRoute(ZRoutedRpc rpc,ZRoutedRpc.RoutedRPCData data){Routes++;return false;}
        [MethodImpl(MethodImplOptions.NoInlining)] internal static bool Deliver(ZRoutedRpc rpc,ZDO data,ZRoutedRpc.RoutedRPCData request,long owner){Deliveries++;return true;}
    }
    internal static class RoutedRpcManager
    {
        [MethodImpl(MethodImplOptions.NoInlining)] internal static void ProcessRoutedRPC(ZRoutedRpc rpc,ZRpc peer,ZPackage packet)
        {
            var data=packet.Rpc;
            if(data.m_targetPeerID==99)VanillaAccess.HandleRoutedRpc(rpc,data);
            else if(!StationRouter.TryRoute(rpc,data))VanillaAccess.RouteRpc(rpc,data);
        }
    }
}
