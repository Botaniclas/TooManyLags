// Managed fixtures for the production camera publisher and controller.
// Handoffs/Unity/transport are doubles; their real safeguards have separate suites.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public struct Vector3 { public float x, y, z; public float sqrMagnitude { get { return x*x+y*y+z*z; } } public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3 {x=a.x-b.x,y=a.y-b.y,z=a.z-b.z}; } }
    public struct Quaternion { public Vector3 eulerAngles; }
    public sealed class Transform { public Vector3 forward; }
    public class Component : Object
    {
        public ZNetView View;
        public T GetComponent<T>() where T : class { return View as T; }
    }
    public static class Time { public static float unscaledTime; }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public long Value; public static readonly ZDOID None = new ZDOID();
    public ZDOID(long value) { Value = value; }
    public bool IsNone() { return Value == 0; }
    public bool Equals(ZDOID other) { return Value == other.Value; }
    public override bool Equals(object other) { return other is ZDOID && Equals((ZDOID)other); }
    public override int GetHashCode() { return Value.GetHashCode(); }
}
public struct Vector2s { public short x, y; public Vector2s(short x, short y) { this.x = x; this.y = y; } }
public static class HashFixture { public static int GetStableHashCode(this string value) { return value.GetHashCode(); } }
public static class ZDOVars { public const int s_health = 1, s_tamed = 2, s_user = 3, s_attachJointHash = 4, s_follow = 5; }
public sealed class ZDO
{
    public ZDOID m_uid; public long Owner; public ushort OwnerRevision; public bool Valid = true;
    public UnityEngine.Vector3 Position; public UnityEngine.Quaternion Rotation; public int Prefab = 100, Coverage = -1;
    public bool IsValid() { return Valid; } public long GetOwner() { return Owner; }
    public UnityEngine.Vector3 GetPosition() { return Position; } public UnityEngine.Quaternion GetRotation() { return Rotation; }
    public int GetPrefab() { return Prefab; } public Vector2s GetSector() { return ZoneSystem.GetZone(Position); }
    public int GetInt(int key, int fallback = 0) { return key == TooManyLagsStandalone.CameraHeading.Key ? Coverage : fallback; }
    public void Set(int key, int value) { if (key == TooManyLagsStandalone.CameraHeading.Key) Coverage = value; }
    public float GetFloat(int key, float fallback = 0) { return fallback; } public long GetLong(int key, long fallback = 0) { return fallback; }
    public bool GetBool(int key) { return false; } public string GetString(int key) { return String.Empty; }
    public void SetOwner(long uid) { Owner = uid; OwnerRevision++; }
}
public sealed class ZDOMan
{
    public static ZDOMan instance = new ZDOMan(); public static long Session = 11;
    public readonly Dictionary<ZDOID, ZDO> Objects = new Dictionary<ZDOID, ZDO>();
    public int Forced;
    public static long GetSessionID() { return Session; }
    public ZDO GetZDO(ZDOID id) { ZDO data; return Objects.TryGetValue(id, out data) ? data : null; }
    public void ForceSendZDO(ZDOID id) { Forced++; }
    public void FindObjects(Vector2s sector, List<ZDO> output, HashSet<ZoneSystem.SectorIndex> visited)
    { foreach (ZDO data in Objects.Values) { Vector2s cell = data.GetSector(); if (cell.x == sector.x && cell.y == sector.y) output.Add(data); } }
}
public static class ZDOExtraData { public static int Added; public static void AddSessionHash(int key) { Added++; } }
public sealed class Player : UnityEngine.Component { public static Player m_localPlayer; }
public sealed class ZNetView : UnityEngine.Object
{
    public ZDO Data; public bool IsValid() { return Data != null && Data.IsValid(); }
    public bool IsOwner() { return Data.Owner == ZDOMan.Session; } public ZDO GetZDO() { return Data; }
}
public sealed class GameCamera : UnityEngine.Object
{
    public static GameCamera instance; public static bool Free;
    public readonly UnityEngine.Transform transform = new UnityEngine.Transform();
    public static bool InFreeFly() { return Free; }
}
public sealed class ZNet : UnityEngine.Object
{
    public static ZNet instance; public static bool IsSinglePlayer; public bool Server, Dedicated; public object ServerPeer;
    public bool IsServer() { return Server; } public bool IsDedicated() { return Dedicated; }
    public object GetServerPeer() { return ServerPeer; }
}
public sealed class ZNetScene : UnityEngine.Object
{
    public static ZNetScene instance = new ZNetScene();
    public static bool InActiveArea(UnityEngine.Vector3 point, UnityEngine.Vector3 reference)
    { return Math.Abs(point.x - reference.x) < 128 && Math.Abs(point.z - reference.z) < 128; }
}
public sealed class ZoneSystem : UnityEngine.Object
{
    public struct SectorIndex { }
    public static ZoneSystem instance = new ZoneSystem();
    public static Vector2s GetZone(UnityEngine.Vector3 point)
    { return new Vector2s((short)Math.Floor((point.x + 32) / 64), (short)Math.Floor((point.z + 32) / 64)); }
}
namespace TooManyLagsStandalone
{
    internal enum Kinds { None, Doors, Incinerators, TamedAnimals, Signs, Portals }
    internal sealed class Settings
    {
        internal float Interval = 1, Full = 2, Inner = 0, Outer = 3, Forward = .2f, Margin = 30, OverlapCooldown;
        internal bool Keep = true, Directional = true, Mobs; internal int CellSize = 4;
        internal GridCoveragePattern Pattern = GridCoveragePattern.Rounded7x7; internal Kinds Categories = Kinds.Doors;
    }
    internal static class Plugin
    {
        internal const string Id = "botanic.toomanylags.standalone";
        internal static AuthorityController Controller; internal static Network Network; internal static LocalAgent Local;
        internal static void SampleCamera() { } internal static void InvalidateCamera() { }
        internal static void Fault(Exception error) { throw new Exception("Production controller failed", error); }
    }
    internal struct PlayerPeer { internal long Uid; internal ZDO Character; internal UnityEngine.Vector3 Reference; }
    internal sealed class Network
    {
        internal bool Confirmed = true;
        internal readonly List<PlayerPeer> Current = new List<PlayerPeer>();
        internal readonly HashSet<long> Modded = new HashSet<long>(); internal int RankVersion;
        internal readonly HashSet<long> LegacyText = new HashSet<long>();
        internal bool Present(long uid) { foreach (PlayerPeer peer in Current) if (peer.Uid == uid) return true; return false; }
        internal bool Capable(long uid) { return Modded.Contains(uid); } internal bool MobCapable(long uid) { return false; }
        internal bool TextCapable(long uid) { return Capable(uid) && !LegacyText.Contains(uid); }
        internal float Latency(long uid) { return uid == 11 ? 0 : 40; }
        internal void Players(List<PlayerPeer> output) { output.Clear(); output.AddRange(Current); }
        internal void FlushAll() { }
    }
    internal sealed class LocalAgent { internal void SetMobWatch(bool value) { } }
    internal static class IncineratorProtection { internal static bool Supported { get { return true; } } }
    internal sealed class RecipientReadiness { internal RecipientReadiness(AuthorityController controller) { } internal void Forget(ZDOID id) { } internal void Reset() { } }
    internal sealed class ObjectCatalog
    {
        internal ObjectCatalog(Kinds categories) { } internal Kinds Prefab(int hash) { return hash == 1 ? Kinds.Doors : hash == 10 ? Kinds.Signs : hash == 11 ? Kinds.Portals : Kinds.None; }
        internal bool NativeTransferSafe(int hash) { return hash == 1; } internal static bool OwnerRequest(Kinds kind, int method) { return false; }
    }
    internal sealed class Handoffs
    {
        internal long Requested; internal int Requests; internal Handoffs(AuthorityController controller) { }
        internal bool Pending(ZDOID id) { return false; } internal void Tick(float now) { } internal void Observe(ZDO data) { }
        internal bool Committing(ZDOID id) { return false; }
        internal void Clear() { } internal void Forget(ZDOID id) { } internal void Cancel(ZDOID id) { }
        internal bool Request(ZDO data, long target, Kinds kind, ZDOID goal) { Requested = target; Requests++; data.SetOwner(target); return true; }
    }
    internal sealed class MobPolicy
    {
        internal int Ticks; internal MobPolicy(AuthorityController controller) { } internal void Tick(float now) { Ticks++; }
        internal void RanksChanged() { } internal void Changed(ZDO data) { } internal void Clear() { }
        internal void Forget(ZDOID id) { } internal void Wake(ZDOID id) { } internal long Desired(ZDO data, ZDOID target) { return 0; }
        internal bool Managed(ZDOID id, int method) { return false; } internal static bool FriendlyPrefab(int hash) { return false; }
        internal void RecordRecipient(ZDO data,long owner,ushort revision,uint state) { }
        internal bool TryRecipientState(ZDO data,long owner,out uint state) { state=0;return false; }
    }
    internal static class RequestRouter { internal static void Flush(float now) { } }
}
