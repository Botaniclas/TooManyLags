public interface Interactable { }
public sealed class ItemStand : UnityEngine.MonoBehaviour, Interactable { }
public sealed class ArmorStand : UnityEngine.MonoBehaviour, Interactable { }
public sealed class TeleportWorld : UnityEngine.MonoBehaviour, Interactable { }
public sealed class Teleport : UnityEngine.MonoBehaviour, Interactable { }
public sealed class Incinerator : UnityEngine.MonoBehaviour
{ private bool isInUse; public void Burning(bool value){isInUse=value;} public bool Using(){return isInUse;} }
public sealed class Trap : UnityEngine.MonoBehaviour, Interactable { public bool Active; public bool IsActive(){return Active;} }
public sealed class Turret : UnityEngine.MonoBehaviour, Interactable
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void RPC_AddAmmo(long sender,string item) { }
}
public sealed class ShieldGenerator : UnityEngine.MonoBehaviour, Interactable
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void RPC_AddFuel(long sender) { }
}
public sealed class Pickable : UnityEngine.MonoBehaviour, Interactable
{
    public ZDOID LastActor; public int Picks, LastBonus;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void RPC_Pick(long sender, int bonus) { LastActor = Player.m_localPlayer.GetZDOID(); Picks++; LastBonus = bonus; }
    public void Pick(long sender, int bonus) { RPC_Pick(sender, bonus); }
}
public sealed class PickableItem : UnityEngine.MonoBehaviour, Interactable { }
public sealed class ItemDrop : UnityEngine.MonoBehaviour, Interactable
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void RPC_RequestOwn(long sender) { var view=GetComponent<ZNetView>(); if(view.GetZDO().GetOwner()==ZDOMan.GetSessionID())view.GetZDO().SetOwner(sender); }
    public void RequestOwn(long sender) { RPC_RequestOwn(sender); }
}
public sealed class Feast : UnityEngine.MonoBehaviour, Interactable { }
public sealed class OfferingBowl : UnityEngine.MonoBehaviour, Interactable { }
public sealed class ShipControlls : UnityEngine.MonoBehaviour, Interactable { }
public sealed class Switch : UnityEngine.MonoBehaviour, Interactable { }
public sealed class CustomInteractable : UnityEngine.MonoBehaviour, Interactable { }
