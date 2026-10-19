using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object item) { return item != null; } }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform OwnTransform;
        public Transform transform { get { return OwnTransform ?? gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>(bool include) where T : class { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool include) where T : class { return gameObject.GetComponentsInChildren<T>(include); }
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public bool activeInHierarchy = true;
        public readonly Transform transform;
        public GameObject() { transform = new Transform { gameObject = this }; }
        private readonly List<Component> components = new List<Component>();
        public T Add<T>(T value) where T : Component { value.gameObject = this; components.Add(value); return value; }
        public T GetComponent<T>() where T : class { return components.OfType<T>().FirstOrDefault(); }
        public T GetComponentInChildren<T>(bool include) where T : class { return GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool include) where T : class { return components.OfType<T>().ToArray(); }
    }
    public class Rigidbody : Component { public bool isKinematic = true, useGravity; }
    public class Collider : Component { public float Extent = .5f; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        public static readonly List<Collider> Hits = new List<Collider>();
        public static bool Saturated;
        public static int Queries;
        public static int OverlapSphereNonAlloc(Vector3 center, float radius, Collider[] buffer, int mask, QueryTriggerInteraction triggers)
        {
            Queries++; int count = 0;
            foreach (Collider hit in Hits)
            {
                ZNetView view = hit.GetComponent<ZNetView>(); Vector3 at = view && view.IsValid() ? view.Data.Position : hit.transform.position;
                float x = at.x - center.x, y = at.y - center.y, z = at.z - center.z, range = radius + hit.Extent;
                if (x*x+y*y+z*z > range*range) continue;
                buffer[count++] = hit; if (count == buffer.Length) break;
            }
            return Saturated ? buffer.Length : count;
        }
    }
    public class Transform : Component
    {
        public Vector3 position; public Quaternion rotation;
        public Vector3 InverseTransformPoint(Vector3 point)
        { return new Quaternion { Yaw = -rotation.Yaw } * new Vector3(point.x - position.x, point.y - position.y, point.z - position.z); }
    }
    public struct Vector3
    {
        public static Vector3 zero { get { return new Vector3(); } }
        public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
    }
    public struct Quaternion
    {
        public float Yaw; public Vector3 eulerAngles { get { return new Vector3(0, Yaw, 0); } }
        public static Vector3 operator *(Quaternion q, Vector3 p)
        { double radians = q.Yaw * Math.PI / 180; return new Vector3((float)(p.x*Math.Cos(radians)+p.z*Math.Sin(radians)),p.y,(float)(p.z*Math.Cos(radians)-p.x*Math.Sin(radians))); }
    }
    public static class Time { public static float unscaledTime; public static float? SimulatedTime; public static float time { get { return SimulatedTime ?? unscaledTime; } } }
}
namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T> { public T Value; }
    public sealed class ConfigDescription { public ConfigDescription(string text, object range) { } }
    public sealed class AcceptableValueRange<T> { public AcceptableValueRange(T min, T max) { } }
    public sealed class AcceptableValueList<T> { public AcceptableValueList(params T[] values) { } }
    public sealed class ConfigFile
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public ConfigEntry<T> Bind<T>(string section, string key, T value, object description)
        { object saved; return new ConfigEntry<T> { Value = Values.TryGetValue(key, out saved) ? (T)saved : value }; }
    }
}
namespace BepInEx
{ public class BaseUnityPlugin : UnityEngine.MonoBehaviour { public bool isActiveAndEnabled = true; } }
public interface Interactable { }
public class Door : UnityEngine.MonoBehaviour, Interactable { }
public class Chair : UnityEngine.MonoBehaviour, Interactable { }
public class Barber : UnityEngine.MonoBehaviour, Interactable { }
public class Bed : UnityEngine.MonoBehaviour, Interactable { }
public class CraftingStation : UnityEngine.MonoBehaviour, Interactable { }
public class PrivateArea : UnityEngine.MonoBehaviour, Interactable { }
public class Sign : UnityEngine.MonoBehaviour, Interactable { }
public class TeleportWorld : UnityEngine.MonoBehaviour, Interactable { }
public class Pickable : UnityEngine.MonoBehaviour, Interactable
{ public bool Picked; public float m_respawnTimeMinutes = 10; public UnityEngine.GameObject m_hideWhenPicked; public bool GetPicked() { return Picked; } }
public class PickableItem : UnityEngine.MonoBehaviour, Interactable
{ private bool m_picked; public bool Finished { get { return m_picked; } } public void Complete() { m_picked = true; } }
public class Beehive : UnityEngine.MonoBehaviour, Interactable { }
public class SapCollector : UnityEngine.MonoBehaviour, Interactable { }
public class Feast : UnityEngine.MonoBehaviour, Interactable { }
public class Smelter : UnityEngine.MonoBehaviour
{
    public bool Accept = true;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void RPC_AddOre(long sender, string item, bool cheated) { ZDO data = GetComponent<ZNetView>().GetZDO(); if (Accept && data.GetOwner() == 99) data.Set(ZDOVars.s_queued, data.GetInt(ZDOVars.s_queued) + 1); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void RPC_AddFuel(long sender) { ZDO data = GetComponent<ZNetView>().GetZDO(); if (Accept && data.GetOwner() == 99) data.Set(ZDOVars.s_fuel, data.GetFloat(ZDOVars.s_fuel, 0) + 1); }
}
public class MapTable : UnityEngine.MonoBehaviour { }
public class CookingStation : UnityEngine.MonoBehaviour, Interactable
{
    public UnityEngine.Transform[] m_slots = new UnityEngine.Transform[2];
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void RPC_AddItem(long sender, string item, bool cheated) { GetComponent<ZNetView>().GetZDO().Set("slot0".GetStableHashCode(), item); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void RPC_AddFuel(long sender) { ZDO data = GetComponent<ZNetView>().GetZDO(); data.Set(ZDOVars.s_fuel, data.GetFloat(ZDOVars.s_fuel, 0) + 1); }
}
public class ShieldGenerator : UnityEngine.MonoBehaviour, Interactable
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void RPC_AddFuel(long sender) { RPC_SetFuel(sender, GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_fuel, 0) + 1); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void RPC_SetFuel(long sender, float fuel) { GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_fuel, fuel); }
}
public class Fireplace : UnityEngine.MonoBehaviour, Interactable
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void RPC_AddFuel(long sender) { ZDO data = GetComponent<ZNetView>().GetZDO(); data.Set(ZDOVars.s_fuel, data.GetFloat(ZDOVars.s_fuel, 0) + 1); }
}
public class Turret : UnityEngine.MonoBehaviour, Interactable
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void RPC_AddAmmo(long sender, string ammo) { ZDO data = GetComponent<ZNetView>().GetZDO(); data.Set(ZDOVars.s_ammo, data.GetInt(ZDOVars.s_ammo) + 1); }
}
public class ArcheryTarget : UnityEngine.MonoBehaviour, Interactable { }
public class Switch : UnityEngine.MonoBehaviour, Interactable { }
public class Teleport : UnityEngine.MonoBehaviour, Interactable { }
public class Vegvisir : UnityEngine.MonoBehaviour, Interactable { }
public class RuneStone : UnityEngine.MonoBehaviour, Interactable { }
public class WayStone : UnityEngine.MonoBehaviour, Interactable { }
public class Ladder : UnityEngine.MonoBehaviour, Interactable { }
public class ModdedControl : UnityEngine.MonoBehaviour, Interactable { }
public class Container : UnityEngine.MonoBehaviour, Interactable
{
    public enum PrivacySetting { Private, Group, Public }
    public PrivacySetting m_privacy = PrivacySetting.Public;
    public ZNetView m_rootObjectOverride;
    public Vagon m_wagon;
    public bool InUse;
    public bool IsInUse() { return InUse; }
}
public class Incinerator : UnityEngine.MonoBehaviour { }
public class TombStone : UnityEngine.MonoBehaviour { }
public class Ship : UnityEngine.MonoBehaviour
{
    public ShipControlls m_shipControlls;
    public float m_lastWaterImpactTime = -1;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void UpdateOwner() { GetComponent<ZNetView>().Data.SetOwner(77); }
    public void RunOwner() { UpdateOwner(); }
}
public class ShipControlls : UnityEngine.MonoBehaviour, Interactable { public Ship m_ship; }
public class Vagon : UnityEngine.MonoBehaviour, Interactable
{
    public Container m_container;
    public bool LocalJoint;
    public bool IsAttached() { return LocalJoint || GetComponent<ZNetView>().Data.GetBool(ZDOVars.s_attachJointHash); }
    public bool InUse() { return (m_container && m_container.IsInUse()) || IsAttached(); }
}
public class Character : UnityEngine.MonoBehaviour
{
    public static readonly List<Character> All = new List<Character>();
    public static List<Character> GetAllCharacters() { return All; }
    public enum Faction { Animals, Players, PlayerSpawned }
    public Faction NativeFaction;
    public bool Attacking, Staggering, NativeBoss;
    public int AttackReads;
    public Faction GetFaction() { return NativeFaction; }
    public bool IsBoss() { return NativeBoss; }
    public virtual bool InAttack() { AttackReads++; return Attacking; }
    public bool IsStaggering() { return Staggering; }
    public bool Dead;
    public float Radius = .5f;
    public bool RadiusUnavailable;
    public bool IsDead() { return Dead; }
    public float GetRadius() { if (RadiusUnavailable) throw new NullReferenceException("Character capsule not initialized"); return Radius; }
}
public class Humanoid : Character
{
    public class ItemSet { public UnityEngine.GameObject[] m_items; }
    public Attack m_currentAttack;
    public ItemDrop m_unarmedWeapon;
    public UnityEngine.GameObject[] m_defaultItems, m_randomWeapon;
    public ItemSet[] m_randomSets;
}
public class Attack
{
    public string m_attackAnimation;
    public int m_attackChainLevels, m_attackRandomAnimations;
    public bool m_loopingAttack, Done = true;
    public bool IsDone() { return Done; }
}
public class Player : Character { }
public class BaseAI : UnityEngine.MonoBehaviour { public static bool IsEnemy(Character a, Character b) { return a.NativeFaction != b.NativeFaction; } }
public class MonsterAI : BaseAI { public bool Charging; public bool m_attackPlayerObjects = true; public bool IsCharging() { return Charging; } }
public class StaticTarget : UnityEngine.MonoBehaviour
{
    public bool Priority, RandomTarget = true;
    public UnityEngine.Vector3 Surface, Center;
    public bool IsPriorityTarget() { return Priority; }
    public bool IsRandomTarget() { return RandomTarget; }
    public UnityEngine.Vector3 FindClosestPoint(UnityEngine.Vector3 point) { return Surface; }
    public UnityEngine.Vector3 GetCenter() { return Center; }
}
public class ZSyncAnimation : UnityEngine.MonoBehaviour
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void RPC_SetTrigger(long sender, string name) { }
    public void Trigger(long sender, string name) { RPC_SetTrigger(sender, name); }
}
public class Tameable : UnityEngine.MonoBehaviour, Interactable { }
public class Sadle : UnityEngine.MonoBehaviour, Interactable { }
public class Piece : UnityEngine.MonoBehaviour { }
public class WearNTear : UnityEngine.MonoBehaviour { public bool m_staticPosition = true; }
public class ItemDrop : UnityEngine.MonoBehaviour, Interactable
{
    public class ItemData
    {
        public enum AiTarget { Enemy, Friend, FriendHurt }
        public class SharedData
        {
            public Attack m_attack, m_secondaryAttack;
            public float m_aiAttackInterval, m_aiAttackRange, m_aiAttackRangeMin, m_aiAttackMaxAngle;
            public bool m_aiInvertAngleCheck;
            public AiTarget m_aiTargetType;
        }
        public SharedData m_shared = new SharedData();
        public bool Weapon = true;
        public bool IsWeapon() { return Weapon; }
    }
    public ItemData m_itemData = new ItemData();
    public bool Converted; public bool IsPiece() { return Converted; }
}
public class FishingFloat : UnityEngine.MonoBehaviour { }
public class Fish : UnityEngine.MonoBehaviour, Interactable { public bool Hooked; public bool IsHooked() { return Hooked; } public void OnHooked(FishingFloat ff) { Hooked = ff != null; } }
public class BossStone : UnityEngine.MonoBehaviour { }
public class ZSyncTransform : UnityEngine.Component { }
public class ZNetView : UnityEngine.MonoBehaviour
{
    public bool m_persistent = true, m_distant;
    public ZDO Data;
    public bool IsValid() { return Data != null && Data.Valid; }
    public ZDO GetZDO() { return Data; }
    public Action<long, string, object[]> Invoke;
    public int Invocations;
    public void InvokeRPC(long target, string name, params object[] args)
    { Invocations++; if (Invoke != null) Invoke(target, name, args); }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public static readonly ZDOID None = new ZDOID(0);
    public long Id;
    public ZDOID(long id) { Id = id; }
    public bool IsNone() { return Id == 0; }
    public bool Equals(ZDOID other) { return Id == other.Id; }
    public override bool Equals(object other) { return other is ZDOID && Equals((ZDOID)other); }
    public override int GetHashCode() { return Id.GetHashCode(); }
}
public class ZDO
{
    private static long next;
    public ZDOID m_uid = new ZDOID(++next);
    public ushort OwnerRevision;
    public uint DataRevision;
    public bool Persistent = true, Valid = true, InUse, Attached, Tamed, Alert, HasTarget, MadePiece;
    public string Follow = "";
    public ZDOID Parent;
    public int Hooked;
    public float Health = 100;
    public int Prefab, Writes, OwnerReads;
    public long Owner, User;
    public UnityEngine.Vector3 Position, Velocity;
    public int VelocityReads;
    public UnityEngine.Quaternion Rotation;
    public readonly Dictionary<int, float> Floats = new Dictionary<int, float>();
    public readonly Dictionary<int, int> Ints = new Dictionary<int, int>();
    public readonly Dictionary<int, string> Strings = new Dictionary<int, string>();
    public int RefillReads;
    public long GetOwner() { OwnerReads++; return Owner; }
    public void SetOwner(long uid) { if (uid != Owner) { SetOwnerInternal(uid); Writes++; OwnerRevision++; } }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void SetOwnerInternal(long uid) { Owner = uid; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void InternalSetPosition(UnityEngine.Vector3 position) { Position = position; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void SetRotation(UnityEngine.Quaternion rotation) { Rotation = rotation; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Set(int key, bool value) { if (key == ZDOVars.s_tamed) Tamed = value; else if (key == ZDOVars.s_alert) Alert = value; else if (key == ZDOVars.s_haveTargetHash) HasTarget = value; else if (key == ZDOVars.s_piece) MadePiece = value; else if (key == ZDOVars.s_attachJointHash) Attached = value; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Set(int key, long value) { User = value; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Set(int key, string value) { if (key == ZDOVars.s_follow) Follow = value; else if (key == ZDOVars.s_attachJointHash) Attached = value.Length != 0; else Strings[key] = value; }
    public void Set(int key, int value) { Ints[key] = value; }
    public void Set(int key, float value) { Floats[key] = value; }
    public void Serialize(ZPackage package) { package.Write(Prefab); }
    public bool ThrowDeserialize;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Deserialize(ZPackage package) { if (ThrowDeserialize) throw new Exception("receive fixture"); DataRevision++; }
    public bool IsValid() { return Valid; }
    public int GetPrefab() { return Prefab; }
    public UnityEngine.Vector3 GetPosition() { return Position; }
    public UnityEngine.Quaternion GetRotation() { return Rotation; }
    public UnityEngine.Vector3 GetVec3(int key, UnityEngine.Vector3 fallback) { VelocityReads++; return Velocity; }
    public long GetLong(int key, long fallback) { return User; }
    public int GetInt(int key) { int value; if (key == ZDOVars.s_hooked) return Hooked; if (key == ZDOVars.s_inUse) return InUse ? 1 : 0; RefillReads++; return Ints.TryGetValue(key, out value) ? value : 0; }
    public bool GetBool(int key) { return key == ZDOVars.s_tamed ? Tamed : key == ZDOVars.s_alert ? Alert : key == ZDOVars.s_haveTargetHash ? HasTarget : key == ZDOVars.s_piece ? MadePiece : Attached; }
    public string GetString(int key, string fallback) { string value; if (key == ZDOVars.s_follow) return Follow; if (key == ZDOVars.s_attachJointHash) return Attached ? "joint" : ""; RefillReads++; return Strings.TryGetValue(key, out value) ? value : fallback; }
    public float GetFloat(int key, float fallback) { float value; if (key == ZDOVars.s_health) return Health; RefillReads++; return Floats.TryGetValue(key, out value) ? value : fallback; }
    public ZDOID GetConnectionZDOID(ZDOExtraData.ConnectionType type) { return Parent; }
}
public static class ZDOVars { public const int s_user = 1, s_inUse = 2, s_attachJointHash = 3, s_velHash = 4, s_tamed = 5, s_follow = 6, s_alert = 7, s_haveTargetHash = 8, s_health = 9, s_piece = 10, s_hooked = 11, s_fuel = 12, s_queued = 13, s_ammo = 14, s_aggravated = 15; }
public static class ZDOExtraData { public enum ConnectionType { SyncTransform } }
public class ZNetPeer
{
    public ZRpc m_rpc = new ZRpc();
    public ISocket m_socket = new FixtureSocket();
    public long m_uid;
    public ZDOID m_characterID;
    public bool Ready = true;
    public UnityEngine.Vector3 Position;
    public bool IsReady() { return Ready; }
    public UnityEngine.Vector3 GetRefPos() { return Position; }
}
public class ZNet : UnityEngine.Object
{
    public static ZNet instance;
    public readonly List<ZNetPeer> Peers = new List<ZNetPeer>();
    public List<ZNetPeer> GetPeers() { return Peers; }
    public ZNetPeer GetPeer(long uid) { return Peers.Find(delegate(ZNetPeer peer) { return peer.m_uid == uid; }); }
}
public class ZDOMan
{
    public static ZDOMan instance;
    public readonly Dictionary<ZDOID, ZDO> Data = new Dictionary<ZDOID, ZDO>();
    public int ForcedSends;
    public long LastForcedPeer;
    public void ForceSendZDO(long peer, ZDOID id) { ForcedSends++; LastForcedPeer = peer; }
    public static long GetSessionID() { return 99; }
    public ZDO GetZDO(ZDOID id) { ZDO value; return Data.TryGetValue(id, out value) ? value : null; }
    public readonly List<ZDO> Assignments = new List<ZDO>();
    private void ReleaseNearbyZDOS(UnityEngine.Vector3 refPosition, long uid) { foreach (ZDO value in Assignments) value.SetOwner(uid); }
    public void RunAssignment(long uid) { ReleaseNearbyZDOS(new UnityEngine.Vector3(), uid); }
    public ZDO Receiving;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void RPC_ZDOData(ZRpc rpc, ZPackage pkg) { Receiving.Deserialize(pkg); }
    public void Receive(ZRpc rpc, ZPackage pkg) { RPC_ZDOData(rpc, pkg); }
}
public class ZNetScene : UnityEngine.Object
{
    public static ZNetScene instance;
    public readonly Dictionary<int, UnityEngine.GameObject> Prefabs = new Dictionary<int, UnityEngine.GameObject>();
    public readonly Dictionary<ZDO, ZNetView> Instances = new Dictionary<ZDO, ZNetView>();
    public ZNetView FindInstance(ZDO data) { ZNetView view; return Instances.TryGetValue(data, out view) ? view : null; }
    public bool AreaReady = true;
    public int AreaChecks;
    public bool IsAreaReady(UnityEngine.Vector3 position) { AreaChecks++; return AreaReady; }
    public UnityEngine.GameObject GetPrefab(int hash) { UnityEngine.GameObject root; return Prefabs.TryGetValue(hash, out root) ? root : null; }
    public int NrOfInstances() { return InstanceCount; }
    public int InstanceCount;
    public static bool InActiveArea(UnityEngine.Vector3 position, UnityEngine.Vector3 center)
    { return Math.Abs(position.x - center.x) <= 128 && Math.Abs(position.z - center.z) <= 128; }
}
public static class ZoneSystem { public static UnityEngine.Vector3 GetZone(UnityEngine.Vector3 value) { return value; } }
public static class StringHash { public static int GetStableHashCode(this string value) { return value.GetHashCode(); } }
public class ZPackage
{
    private readonly MemoryStream stream;
    private readonly BinaryWriter writer;
    private readonly BinaryReader reader;
    public ZPackage() { stream = new MemoryStream(); writer = new BinaryWriter(stream); reader = new BinaryReader(stream); }
    public ZPackage(byte[] value) : this() { writer.Write(value); stream.Position = 0; }
    public byte[] GetArray() { return stream.ToArray(); }
    public void Write(int value) { writer.Write(value); }
    public void Write(bool value) { writer.Write(value); }
    public void Write(ushort value) { writer.Write(value); }
    public void Write(uint value) { writer.Write(value); }
    public void Write(long value) { writer.Write(value); }
    public void Write(float value) { writer.Write(value); }
    public void Write(ZDOID value) { writer.Write(value.Id); }
    public void Write(UnityEngine.Vector3 value) { writer.Write(value.x); writer.Write(value.y); writer.Write(value.z); }
    public void Write(ZPackage value) { byte[] data = value.GetArray(); writer.Write(data.Length); writer.Write(data); }
    public int ReadInt() { return reader.ReadInt32(); }
    public bool ReadBool() { return reader.ReadBoolean(); }
    public ushort ReadUShort() { return reader.ReadUInt16(); }
    public uint ReadUInt() { return reader.ReadUInt32(); }
    public long ReadLong() { return reader.ReadInt64(); }
}
public interface ISocket { void GetConnectionQuality(out float local, out float remote, out int ping, out float outgoing, out float incoming); }
public class FixtureSocket : ISocket
{
    public int Ping = 50, Reads;
    public bool ThrowQuality;
    public void GetConnectionQuality(out float local, out float remote, out int ping, out float outgoing, out float incoming)
    { Reads++; if (ThrowQuality) throw new InvalidOperationException("transport telemetry unavailable"); local = remote = outgoing = incoming = 0; ping = Ping; }
}
public static class FixtureWire { public static readonly List<string> Order = new List<string>(); }
public class ZRpc
{
    public bool Connected = true;
    public readonly List<ZPackage> Sent = new List<ZPackage>();
    public bool IsConnected() { return Connected; }
    public void Invoke(string method, params object[] args) { Sent.Add((ZPackage)args[0]); FixtureWire.Order.Add(method); }
}
public class ZRoutedRpc
{
    public sealed class RoutedRPCData
    {
        public long m_msgID, m_senderPeerID, m_targetPeerID;
        public int m_methodHash;
        public ZDOID m_targetZDO;
        public ZPackage m_parameters;
    }
    public readonly List<RoutedRPCData> Local = new List<RoutedRPCData>(), Remote = new List<RoutedRPCData>();
    public bool Throw;
    private void RouteRPC(RoutedRPCData packet)
    { if (Throw) throw new Exception("transport fixture"); if (!TooManyLagsDedicatedAddon.RequestRouter.Handle(this, packet)) throw new Exception("recursive forwarding"); FixtureWire.Order.Add("RoutedRPC"); Remote.Add(packet); }
    private void HandleRoutedRPC(RoutedRPCData packet)
    { if (!TooManyLagsDedicatedAddon.RequestRouter.Handle(this, packet)) throw new Exception("recursive forwarding"); Local.Add(packet); }
}
namespace TooManyLagsDedicatedAddon
{
    internal static class Plugin
    {
        internal const string Dependency = "MVP.Valheim_Serverside_Simulations";
        internal static Controller Current;
        internal static bool EnsureWorld() { return Current != null; }
        internal static int StatusSchedules;
        internal static void ScheduleStatus() { StatusSchedules++; }
        internal static void Fault(Exception error) { throw error; }
        internal static void Reset() { Current = null; }
    }
}
