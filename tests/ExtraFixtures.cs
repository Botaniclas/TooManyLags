using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
public sealed class Chair : UnityEngine.MonoBehaviour, Interactable { }
public sealed class Barber : UnityEngine.MonoBehaviour, Interactable { }
public sealed class Bed : UnityEngine.MonoBehaviour, Interactable { }
public sealed class CraftingStation : UnityEngine.MonoBehaviour, Interactable { }
public sealed class StationExtension : UnityEngine.MonoBehaviour { }
public sealed class Smelter : UnityEngine.MonoBehaviour
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_AddOre(long sender,string item,bool cheated) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_AddFuel(long sender) { }
}
public sealed class CookingStation : UnityEngine.MonoBehaviour, Interactable
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_AddItem(long sender,string item,bool cheated) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_AddFuel(long sender) { }
}
public sealed class Fireplace : UnityEngine.MonoBehaviour, Interactable
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_AddFuel(long sender) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_AddFuelAmount(long sender,float amount) { }
}
public sealed class SapCollector : UnityEngine.MonoBehaviour, Interactable { }
public sealed class PrivateArea : UnityEngine.MonoBehaviour, Interactable { }
public sealed class MapTable : UnityEngine.MonoBehaviour { }
public sealed class WispSpawner : UnityEngine.MonoBehaviour { }
public sealed class Fermenter : UnityEngine.MonoBehaviour, Interactable
{
    [MethodImpl(MethodImplOptions.NoInlining)] public void RPC_AddItem(long sender,int item,bool cheated) { }
}
public sealed class Sign : UnityEngine.MonoBehaviour, Interactable { }
public sealed class ZPackage
{
    public ZRoutedRpc.RoutedRPCData Rpc;
    private readonly System.IO.MemoryStream bytes; public int Pos;
    public ZPackage(){bytes=new System.IO.MemoryStream();} public ZPackage(byte[] data){bytes=new System.IO.MemoryStream();bytes.Write(data,0,data.Length);bytes.Position=0;}
    public byte[] GetArray(){return bytes.ToArray();} public void SetPos(int pos){Pos=pos;bytes.Position=pos;}
    public void Write(ZDOID value){new System.IO.BinaryWriter(bytes).Write(value.ID);}
    public void Write(long value){new System.IO.BinaryWriter(bytes).Write(value);}
    public void Write(bool value){new System.IO.BinaryWriter(bytes).Write(value);}
    public ZDOID ReadZDOID(){return new ZDOID{ID=new System.IO.BinaryReader(bytes).ReadInt32()};}
    public long ReadLong(){return new System.IO.BinaryReader(bytes).ReadInt64();}
    public bool ReadBool(){return new System.IO.BinaryReader(bytes).ReadBoolean();}
}
public sealed class ZRoutedRpc
{
    public static ZRoutedRpc instance;
    public sealed class RoutedRPCData
    {
        public long m_msgID,m_senderPeerID,m_targetPeerID;
        public ZDOID m_targetZDO; public int m_methodHash; public ZPackage m_parameters=new ZPackage();
    }
    public readonly List<RoutedRPCData> Local=new List<RoutedRPCData>(),Remote=new List<RoutedRPCData>();
    public Action<RoutedRPCData> OnHandle;
    public Action<long,ZDOID,string,object[]> OnInvoke;
    public void InvokeRoutedRPC(long target,ZDOID id,string name,params object[] args){if(OnInvoke!=null)OnInvoke(target,id,name,args);}
    [MethodImpl(MethodImplOptions.NoInlining)] private void HandleRoutedRPC(RoutedRPCData data){Local.Add(data);if(OnHandle!=null)OnHandle(data);}
    [MethodImpl(MethodImplOptions.NoInlining)] private void RouteRPC(RoutedRPCData data){Remote.Add(data);}
    public void Incoming(RoutedRPCData data)
    { if(data.m_targetPeerID==99 || data.m_targetPeerID==0)HandleRoutedRPC(data); if(data.m_targetPeerID!=99)RouteRPC(data); }
}
namespace TooManyLags
{
    [HarmonyLib.HarmonyPatch(typeof(ZDOMan),"ReleaseNearbyZDOS")]
    internal static class OwnershipPatch
    {
        [HarmonyLib.HarmonyTranspiler]
        private static IEnumerable<HarmonyLib.CodeInstruction> Transpiler(IEnumerable<HarmonyLib.CodeInstruction> source)
        {return OwnershipTranspiler.Rewrite(source,HarmonyLib.AccessTools.Method(typeof(OwnershipPatch),"Persistent"));}
        internal static bool Persistent(ZDO data){return Plugin.Controller==null?data.Persistent:Plugin.Controller.Persistent(data);}
    }
}
