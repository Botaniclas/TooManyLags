using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#pragma warning disable 0649, 0169, 0414

namespace UnityEngine
{
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public class Object
    {
        private static int serial;private readonly int nativeInstanceId=++serial;
        public string name; public bool Destroyed;
        public static implicit operator bool(Object value) { return !ReferenceEquals(value, null) && !value.Destroyed; }
        // StatusEffect.Clone is native MemberwiseClone: runtime clones share
        // their template's Unity identity despite having distinct CLR state.
        public override bool Equals(object value){Object other=value as Object;return !ReferenceEquals(other,null)&&nativeInstanceId==other.nativeInstanceId;}
        public override int GetHashCode(){return nativeInstanceId;}
        public static int DestroyCalls;public static void Destroy(Object value){if(value){DestroyCalls++;value.Destroyed=true;}}
    }
    public class ScriptableObject : Object { }
    public class GameObject : Object
    {
        internal readonly Dictionary<Type, Component> Components = new Dictionary<Type, Component>();
        public T Add<T>() where T : Component, new() { T value = new T { gameObject = this }; Components[typeof(T)] = value; return value; }
        public T GetComponent<T>() where T : Component
        { foreach (Component value in Components.Values) if (value is T) return (T)value; return null; }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z; public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public bool Equals(Vector3 value) { return x == value.x && y == value.y && z == value.z; }
        public override bool Equals(object value) { return value is Vector3 && Equals((Vector3)value); }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
    }
    public struct Quaternion { public float x,y,z,w; }
}
public struct ZDOID : IEquatable<ZDOID>
{
    public long UserID; public uint ID; public static readonly ZDOID None = new ZDOID();
    public ZDOID(long user, uint id) { UserID = user; ID = id; }
    public bool IsNone() { return UserID == 0 && ID == 0; }
    public bool Equals(ZDOID value) { return UserID == value.UserID && ID == value.ID; }
    public override bool Equals(object value) { return value is ZDOID && Equals((ZDOID)value); }
    public override int GetHashCode() { return UserID.GetHashCode() ^ (int)ID; }
    public static bool operator ==(ZDOID a, ZDOID b) { return a.Equals(b); }
    public static bool operator !=(ZDOID a, ZDOID b) { return !a.Equals(b); }
}
public class ZDO
{
    public ZDOID m_uid; public long Owner = 1; public uint OwnerRevision = 3; public int Prefab = 42; public bool Valid = true;
    public bool IsValid() { return Valid; } public bool IsOwner() { return Owner == ZDOMan.Local; } public int GetPrefab() { return Prefab; }
    public long GetOwner() { return Owner; } public void SetOwner(long owner) { Owner = owner; OwnerRevision++; }
}
public class ZDOMan
{
    public static ZDOMan instance; public static long Local = 1;
    public readonly Dictionary<ZDOID,ZDO> All = new Dictionary<ZDOID,ZDO>();
    public ZDO GetZDO(ZDOID id) { ZDO data; return All.TryGetValue(id, out data) ? data : null; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void HandleDestroyedZDO(ZDOID id)
    {
        ZDO data;if(All.TryGetValue(id,out data)){data.Valid=false;All.Remove(id);}
        if(ZNetScene.instance){UnityEngine.GameObject obj=ZNetScene.instance.FindInstance(id);if(obj)obj.Destroyed=true;ZNetScene.instance.All.Remove(id);}
    }
    public int DestroyRequests;
    public void DestroyZDO(ZDO data){if(data!=null&&data.IsValid()&&data.IsOwner()){DestroyRequests++;HandleDestroyedZDO(data.m_uid);}}
}
public class ZNetScene : UnityEngine.Object
{
    public static ZNetScene instance;
    public readonly Dictionary<ZDOID,UnityEngine.GameObject> All = new Dictionary<ZDOID,UnityEngine.GameObject>();
    public UnityEngine.GameObject FindInstance(ZDOID id) { UnityEngine.GameObject value; return All.TryGetValue(id, out value) ? value : null; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void AddInstance(ZDO data, ZNetView view) { All[data.m_uid] = view.gameObject; }
}
public class ZNetView : UnityEngine.Component
{
    public ZDO Data; public int Destroys, Claims;
    public bool IsValid() { return Data != null && Data.IsValid(); } public bool IsOwner() { return Data.IsOwner(); } public ZDO GetZDO() { return Data; }
    public void ClaimOwnership() { Claims++; if (!Data.IsOwner()) Data.SetOwner(ZDOMan.Local); }
    public void Destroy() { Destroys++; gameObject.Destroyed = true; Data.Valid = false; ZNetScene.instance.All.Remove(Data.m_uid); }
}
public class Character : UnityEngine.Component
{
    public SEMan Manager; public int Heals, DamageTicks; public float Health = 100;
    public SEMan GetSEMan() { return Manager; }
    public ZDOID GetZDOID() { return GetComponent<ZNetView>().Data.m_uid; }
}
public class Player : Character { }
public class LineConnect : UnityEngine.Component { }
public class Beacon : UnityEngine.Component { }
public class ObjectDB : UnityEngine.Object
{
    public static ObjectDB instance; public Dictionary<int,StatusEffect> Templates = new Dictionary<int,StatusEffect>();
    public StatusEffect GetStatusEffect(int name) { StatusEffect effect; return Templates.TryGetValue(name, out effect) ? effect : null; }
}
public class EffectList { }
public class StatusEffect : UnityEngine.ScriptableObject
{
    [Flags] public enum StatusAttribute { None = 0, ColdResistance = 1, SailingPower = 4 }
    public string m_name = "", m_category = "", m_tooltip = "";
    public float m_ttl; public short m_hitVariant; public bool m_isNew = true; public StatusAttribute m_attributes;
    public EffectList m_startEffects = new EffectList(), m_stopEffects = new EffectList();
    public Character m_character; protected float m_time; private float m_msgTimer; private int m_nameHash;
    protected UnityEngine.GameObject[] m_startEffectInstances;
    public static int Setups, Resets, Levels, Stops, OnDestroys;
    public StatusEffect Clone() { return (StatusEffect)MemberwiseClone(); }
    public virtual void Setup(Character actor) { Setups++; m_character = actor; }
    public virtual void ResetTime() { Resets++; m_time = 0; }
    public virtual void SetLevel(int level, float skill) { Levels++; GC.KeepAlive(level); GC.KeepAlive(skill); }
    public virtual void ModifySpeed(float baseSpeed, ref float speed, Character character, UnityEngine.Vector3 direction)
    { GC.KeepAlive(baseSpeed); GC.KeepAlive(character); GC.KeepAlive(direction); }
    public virtual void UpdateStatusEffect(float dt) { m_time += dt; m_msgTimer += dt; }
    public float GetDuration() { return m_time; }
    public virtual bool IsDone() { return m_ttl > 0 && m_time > m_ttl; }
    public int NameHash() { if (m_nameHash == 0) m_nameHash = Stable(name); return m_nameHash; }
    private static int Stable(string text) { unchecked { int hash = 17; foreach(char c in text) hash = hash * 31 + c; return hash; } }
    [MethodImpl(MethodImplOptions.NoInlining)] private void RemoveStartEffects()
    {
        if(m_startEffectInstances == null) return;
        foreach(UnityEngine.GameObject obj in m_startEffectInstances) if(obj) { ZNetView view = obj.GetComponent<ZNetView>(); if(view && view.IsValid()) { view.ClaimOwnership(); view.Destroy(); } }
        m_startEffectInstances = null;
    }
    public virtual void Stop() { Stops++; RemoveStartEffects(); }
    public virtual void OnDestroy() { OnDestroys++; RemoveStartEffects(); }
}
public class SEMan
{
    private readonly List<StatusEffect> m_statusEffects = new List<StatusEffect>(), m_removeStatusEffects = new List<StatusEffect>();
    private readonly HashSet<int> m_statusEffectsHashSet = new HashSet<int>();
    private int m_statusEffectAttributes, m_statusEffectAttributesOld = -1;
    public List<StatusEffect> GetStatusEffects() { return m_statusEffects; }
    public bool Has(int name) { return m_statusEffectsHashSet.Contains(name); }
    public int Attributes { get { return m_statusEffectAttributes; } } public int Previous { get { return m_statusEffectAttributesOld; } }
    public void Add(StatusEffect effect) { m_statusEffects.Add(effect); m_statusEffectsHashSet.Add(effect.NameHash()); }
    public void Remove(StatusEffect effect) { effect.Stop(); m_statusEffects.Remove(effect); m_statusEffectsHashSet.Remove(effect.NameHash()); }
    public void Tick(float dt) { foreach(StatusEffect effect in m_statusEffects) effect.UpdateStatusEffect(dt); }
    public void ApplyStatusEffectSpeedMods(ref float speed, UnityEngine.Vector3 direction)
    {
        float baseSpeed = speed;
        foreach(StatusEffect effect in m_statusEffects) effect.ModifySpeed(baseSpeed, ref speed, null, direction);
    }
}
public class SE_Poison : StatusEffect
{
    public float m_damageInterval = 1, m_baseTTL = 2; public EffectList m_tickEffect = new EffectList();
    private float m_timer, m_damageLeft, m_damagePerHit;
    public override void UpdateStatusEffect(float dt)
    { base.UpdateStatusEffect(dt); m_timer -= dt; if(m_timer > 0) return; m_timer = m_damageInterval; m_damageLeft -= m_damagePerHit; m_character.Health -= m_damagePerHit; m_character.DamageTicks++; }
}
public class SE_Burning : StatusEffect
{
    public float m_damageInterval = 1; private float m_timer, m_fireDamageLeft, m_fireDamagePerHit, m_spiritDamageLeft, m_spiritDamagePerHit;
    public EffectList m_tickEffect = new EffectList();
    public override void UpdateStatusEffect(float dt)
    { base.UpdateStatusEffect(dt); m_timer -= dt; if(m_timer > 0) return; m_timer = m_damageInterval; m_fireDamageLeft = Math.Max(0,m_fireDamageLeft-m_fireDamagePerHit); m_spiritDamageLeft = Math.Max(0,m_spiritDamageLeft-m_spiritDamagePerHit); m_character.Health -= m_fireDamagePerHit+m_spiritDamagePerHit; m_character.DamageTicks++; }
}
public class SE_Frost : StatusEffect { public float m_minSpeedFactor=.1f; public List<float> m_frostSlowMultipliers = new List<float>(); }
public class SE_Shield : StatusEffect { public float m_absorbDamage = 100; private float m_totalAbsorbDamage, m_damage; }
public class SE_Stats : StatusEffect
{
    public float m_healthUpFront=20, m_healthOverTimeDuration=10, m_staminaOverTimeDuration=10, m_eitrOverTimeDuration=10;
    private float m_tickTimer, m_healthOverTimeTimer, m_healthOverTimeTicks, m_healthOverTimeTickHP;
    public List<int> m_mods = new List<int>(); public UnityEngine.Vector3 m_jumpModifier;
    public override void Setup(Character actor) { base.Setup(actor); actor.Health += m_healthUpFront; actor.Heals++; }
    public override void ResetTime() { base.ResetTime(); m_character.Health += m_healthUpFront; m_character.Heals++; }
}
public class SE_Spawn : StatusEffect
{
    public UnityEngine.GameObject m_prefab; private bool m_spawned; public static int Spawns;
    public override void UpdateStatusEffect(float dt) { base.UpdateStatusEffect(dt); if(!m_spawned) { m_spawned=true; Spawns++; } }
}
public class SE_Harpooned : StatusEffect
{
    private Character m_attacker; private LineConnect m_line; private bool m_broken; private float m_baseDistance=999999, m_drainStaminaTimer;
    [MethodImpl(MethodImplOptions.NoInlining)] public override bool IsDone() { return base.IsDone() || m_broken || !m_attacker; }
}
public class SE_Demister : StatusEffect
{
    private UnityEngine.GameObject m_ballInstance; private UnityEngine.Vector3 m_ballVel; private int m_coverRayMask; public UnityEngine.GameObject m_ballPrefab;
    public static int Spawned;
    [MethodImpl(MethodImplOptions.NoInlining)] public override void UpdateStatusEffect(float dt) { base.UpdateStatusEffect(dt); if(!m_ballInstance) { Spawned++; m_ballInstance=new UnityEngine.GameObject(); } }
    [MethodImpl(MethodImplOptions.NoInlining)] private void RemoveEffects() { if(m_ballInstance) { ZNetView view=m_ballInstance.GetComponent<ZNetView>(); if(view && view.IsValid()) { view.ClaimOwnership(); view.Destroy(); } } }
    public override void Stop() { base.Stop(); RemoveEffects(); }
    public override void OnDestroy() { base.OnDestroy(); RemoveEffects(); }
}
public class SE_Finder : StatusEffect { private Beacon m_beacon; }
public class SimpleModEffect : StatusEffect
{
    private double durationFactor; private UnityEngine.Vector3 direction; private UnityEngine.Quaternion orientation;
    private long epoch; private string marker; public readonly List<int> SharedTemplateConfig = new List<int>();
}
public class SlowEffect : StatusEffect
{
    public float m_factor = .5f;
    public override void ModifySpeed(float baseSpeed, ref float speed, Character character, UnityEngine.Vector3 dir)
    {
        GC.KeepAlive(baseSpeed); GC.KeepAlive(character); GC.KeepAlive(dir);
        speed *= m_factor;
    }
}
public class ComplexModEffect : StatusEffect { public object RuntimeGraph; }
namespace TooManyLags
{
    internal static class Plugin { internal const string Id="status-state-fixture"; internal static int Warnings; internal static void Warn(string text) { Warnings++; Console.WriteLine("WARN: "+text); } }
}
