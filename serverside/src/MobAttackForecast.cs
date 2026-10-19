using System;
using System.Collections.Generic;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal struct ForecastReceiveState
    {
        internal bool Tracked, TrackRotation;
        internal Quaternion Rotation;
        internal bool Living, Tamed, Aggravated;
    }
    // Metadata is shared per prefab; no second GameObject, AI, pathfinder or clock.
    internal sealed class MobForecastProfile
    {
        internal sealed class Ability
        {
            internal float Cooldown, Range, Cosine;
            internal bool Enemy, Inverted;
        }
        internal readonly List<Ability> Abilities = new List<Ability>();
        internal readonly Dictionary<string, int> Triggers = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<ItemDrop.ItemData.SharedData> seen = new HashSet<ItemDrop.ItemData.SharedData>();
        internal bool Unknown;
        internal float MaximumRange;
        internal MobForecastProfile(Humanoid actor)
        {
            Items(actor.m_defaultItems); Items(actor.m_randomWeapon);
            if (actor.m_randomSets != null) foreach (Humanoid.ItemSet set in actor.m_randomSets) if (set != null) Items(set.m_items);
            if (actor.m_unarmedWeapon) Items(new[] { actor.m_unarmedWeapon.gameObject });
        }
        private void Items(GameObject[] items)
        {
            if (items == null) return;
            foreach (GameObject item in items)
            {
                ItemDrop drop = item ? item.GetComponent<ItemDrop>() : null;
                if (!drop || drop.m_itemData == null || drop.m_itemData.m_shared == null || !drop.m_itemData.IsWeapon()) continue;
                var shared = drop.m_itemData.m_shared;
                if (shared == null || !seen.Add(shared)) continue;
                if (Abilities.Count >= 128 || !Valid(shared.m_aiAttackInterval) || !Valid(shared.m_aiAttackRange) ||
                    !Valid(shared.m_aiAttackRangeMin) || !Valid(shared.m_aiAttackMaxAngle) || shared.m_aiAttackRange > 256f)
                { Unknown = true; continue; }
                int index = Abilities.Count;
                bool enemy = shared.m_aiTargetType == ItemDrop.ItemData.AiTarget.Enemy;
                float angle = enemy ? Math.Max(0f, Math.Min(180f, shared.m_aiAttackMaxAngle + (shared.m_aiInvertAngleCheck ? -5f : 5f))) : 180f;
                Abilities.Add(new Ability { Cooldown = Math.Max(0f, shared.m_aiAttackInterval), Range = Math.Max(0f, shared.m_aiAttackRange) + .5f,
                    Cosine = (float)Math.Cos(angle * Math.PI / 180d),
                    Enemy = enemy, Inverted = enemy && shared.m_aiInvertAngleCheck });
                MaximumRange = Math.Max(MaximumRange, Abilities[index].Range + .5f);
                Add(shared.m_attack, index); Add(shared.m_secondaryAttack, index);
            }
        }
        private static bool Valid(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        private void Add(Attack attack, int index)
        {
            if (attack == null || String.IsNullOrEmpty(attack.m_attackAnimation)) return;
            int count = attack.m_attackChainLevels > 1 ? attack.m_attackChainLevels : attack.m_attackRandomAnimations >= 2 ? attack.m_attackRandomAnimations : 0;
            if (count > 64) { Unknown = true; return; }
            if (count == 0) Add(attack.m_attackAnimation, index);
            else for (int i = 0; i < count; i++) Add(attack.m_attackAnimation + i, index);
        }
        private void Add(string trigger, int index)
        {
            int previous;
            Triggers[trigger] = Triggers.TryGetValue(trigger, out previous) && previous != index ? -1 : index;
        }
        internal static bool Contains(Ability ability, Vector3 origin, Vector3 forward, Vector3 target, float radius)
        {
            float x = target.x - origin.x, y = target.y - origin.y, z = target.z - origin.z;
            float distance = x * x + y * y + z * z;
            // Minimum range only gates native weapon selection, not every use
            // of an already equipped weapon. It cannot prove an attack impossible.
            float maximum = ability.Range + radius;
            if (distance > maximum * maximum) return false;
            float horizontal = x * x + z * z, facing = forward.x * forward.x + forward.z * forward.z;
            float cosine = horizontal <= .000001f || facing <= .000001f ? 1f : (x * forward.x + z * forward.z) / (float)Math.Sqrt(horizontal * facing);
            return ability.Inverted ? cosine <= ability.Cosine + .000001f : cosine + .000001f >= ability.Cosine;
        }
    }

    internal sealed partial class Controller
    {
        private readonly HashSet<Node> forecastWaiting = new HashSet<Node>();
        private readonly Dictionary<int, bool> forecastKinds = new Dictionary<int, bool>();
        private Collider[] forecastColliders;
        private readonly HashSet<Character> forecastCharacters = new HashSet<Character>();
        private readonly HashSet<StaticTarget> forecastBuildings = new HashSet<StaticTarget>();
        private bool forecastDirty;
        internal bool HasWaitingForecast { get { return forecastWaiting.Count != 0; } }
        internal int MobForecastChecks, MobForecastQueries, MobAbilityObservations, MobForecastPhysicsQueries, MobForecastCharacterSweeps;
        internal bool WatchesForecastTurn(ZDO data)
        { Node node; return forecastWaiting.Count != 0 && data != null && mobs.TryGetValue(data, out node) && forecastWaiting.Contains(node); }
        internal void ForecastMobTurned(ZDO data)
        {
            Node node;
            if (forecastWaiting.Count == 0 || data == null || !mobs.TryGetValue(data, out node) || !forecastWaiting.Contains(node)) return;
            node.Mob.ForecastDirty = true; WakeMob(node);
        }
        private void ObserveMobAbility(Node node, string trigger, long source)
        {
            MobForecastProfile profile = node.Mob.Profile.Forecast; int ability;
            if (profile == null || !profile.Triggers.TryGetValue(trigger, out ability) || ability < 0) return;
            MobAbilityObservations++;
            node.Mob.ForecastDirty = true;
            if (node.Mob.Cooldowns == null) { node.Mob.Cooldowns = new float[profile.Abilities.Count]; ResetMobCooldowns(node.Mob); }
            CheckForecastClock(node.Mob);
            // Receipt is not attack-start time. Shorten by 100 ms AND a full
            // cached RTT. No telemetry means no inferred remaining cooldown.
            int ping = source == server ? 0 : Ping(source, Time.unscaledTime);
            float remaining = source != server && ping <= 0 ? 0f : Math.Max(0f, profile.Abilities[ability].Cooldown - .1f - ping * .001f);
            node.Mob.Cooldowns[ability] = Time.time + remaining;
        }
        private static void CheckForecastClock(MobState mob)
        {
            if (Time.time < mob.CooldownClock) ResetMobCooldowns(mob);
            mob.CooldownClock = Time.time;
        }
        private static void ResetMobCooldowns(MobState mob)
        { if (mob.Cooldowns != null) for (int i = 0; i < mob.Cooldowns.Length; i++) mob.Cooldowns[i] = Single.NegativeInfinity; }
        internal void ForecastTargetChanged(ZDO data, Vector3? previous = null, bool worldEvent = false)
        {
            if (forecastWaiting.Count == 0 || data == null || !ForecastTarget(data)) return;
            ZNetView view = worldEvent ? null : ZNetScene.instance.FindInstance(data);
            Character actor = view ? view.GetComponent<Character>() : null;
            if (!actor) { forecastDirty = true; return; } // Rare creation/removal or unknown bounds.
            float radius;
            try { radius = actor.GetRadius(); }
            catch (NullReferenceException) { forecastDirty = true; return; } // Character.Awake may not have bound its capsule yet.
            Vector3 position = data.GetPosition();
            if (Single.IsNaN(radius) || Single.IsInfinity(radius) || radius < 0f || !Finite(position)) { forecastDirty = true; return; }
            foreach (Node waitingMob in forecastWaiting)
            {
                float reach = waitingMob.Mob.Profile.Forecast.MaximumRange + radius;
                Vector3 at = waitingMob.Data.GetPosition();
                if (ReferenceEquals(waitingMob.Data, data) || Near(at, position, reach) || (previous.HasValue && Near(at, previous.Value, reach)))
                { waitingMob.Mob.ForecastDirty = true; WakeMob(waitingMob); }
            }
        }
        private static bool Near(Vector3 a, Vector3 b, float range)
        { float x = a.x-b.x, y = a.y-b.y, z = a.z-b.z; return x*x+y*y+z*z <= range*range; }
        private bool ForecastTarget(ZDO data)
        {
            bool target; int prefab = data.GetPrefab();
            if (!forecastKinds.TryGetValue(prefab, out target))
            {
                GameObject root = ZNetScene.instance.GetPrefab(prefab);
                target = root && (root.GetComponent<Character>() || root.GetComponentInChildren<StaticTarget>(true));
                forecastKinds[prefab] = target;
            }
            return target;
        }
        internal ForecastReceiveState CaptureForecastReceive(ZDO data)
        {
            if (forecastWaiting.Count == 0 || data == null || !ForecastTarget(data)) return new ForecastReceiveState();
            bool turn = WatchesForecastTurn(data);
            return new ForecastReceiveState { Tracked = true, TrackRotation = turn, Rotation = turn ? data.GetRotation() : new Quaternion(), Living = data.GetFloat(ZDOVars.s_health, 1f) > 0f,
                Tamed = data.GetBool(ZDOVars.s_tamed), Aggravated = data.GetBool(ZDOVars.s_aggravated) };
        }
        internal void ForecastReceived(ZDO data, ForecastReceiveState previous)
        {
            // Deserialize writes rotation/target eligibility directly. Damage
            // that does not change alive/dead eligibility needs no new query.
            if (!previous.Tracked) return;
            // A target's rotation does not change another mob's attack geometry.
            // Only the waiting mob itself needs a new decision when it turns.
            if (previous.TrackRotation && !previous.Rotation.Equals(data.GetRotation())) ForecastMobTurned(data);
            if (previous.Living != (data.GetFloat(ZDOVars.s_health, 1f) > 0f) ||
                previous.Tamed != data.GetBool(ZDOVars.s_tamed) || previous.Aggravated != data.GetBool(ZDOVars.s_aggravated)) ForecastTargetChanged(data);
        }
        private bool ForecastMobAttack(Node node)
        {
            MobForecastProfile profile = node.Mob.Profile.Forecast;
            if (profile == null) return false;
            if (forecastWaiting.Contains(node) && !node.Mob.ForecastDirty) return true;
            node.Mob.ForecastDirty = false;
            MobForecastChecks++;
            if (profile.Unknown) return true;
            CheckForecastClock(node.Mob);
            float now = Time.time, maximum = 0f;
            for (int i = 0; i < profile.Abilities.Count; i++)
                if (node.Mob.Cooldowns == null || now >= node.Mob.Cooldowns[i]) maximum = Math.Max(maximum, profile.Abilities[i].Range);
            if (maximum == 0f) return false; // No ready ability: no geometry at all.
            Vector3 origin = node.Data.GetPosition(), forward = node.Data.GetRotation() * new Vector3(0, 0, 1);
            if (!Finite(origin) || !Finite(forward)) return true;
            MobForecastQueries++;
            int count = 0;
            try
            {
                // Usually a nearby player already proves a follow-up possible.
                // Prefer its current native ZDO pose; no collider query is needed.
                foreach (MobPlayer player in mobPlayers)
                {
                    ZNetView live = ZNetScene.instance.FindInstance(player.Data);
                    if (live && ForecastCharacter(node, profile, now, maximum, origin, forward, live.GetComponent<Character>())) return true;
                }
                // Native AI radius can exceed physical capsule bounds. A bounded
                // physics sphere must not exclude those larger creature targets.
                MobForecastCharacterSweeps++;
                foreach (Character target in Character.GetAllCharacters())
                    if (ForecastCharacter(node, profile, now, maximum, origin, forward, target)) return true;
                if (!node.Mob.Ai.m_attackPlayerObjects) return false;
                if (forecastColliders == null) forecastColliders = new Collider[128];
                MobForecastPhysicsQueries++;
                count = Physics.OverlapSphereNonAlloc(origin, maximum + .5f, forecastColliders, ~0, QueryTriggerInteraction.Ignore);
                if (count >= forecastColliders.Length) return true; // Saturation is unknown, never a clear scene.
                for (int n = 0; n < count; n++)
                {
                    Collider collider = forecastColliders[n]; if (!collider) continue;
                    StaticTarget building = collider.GetComponentInParent<StaticTarget>(true);
                    if (!building || !forecastBuildings.Add(building) || (!building.IsPriorityTarget() && !building.IsRandomTarget())) continue;
                    Vector3 closest = building.FindClosestPoint(origin), center = building.GetCenter();
                    if (!Finite(closest) || !Finite(center)) return true;
                    for (int i = 0; i < profile.Abilities.Count; i++)
                    {
                        var ability = profile.Abilities[i];
                        if (!ability.Enemy || (node.Mob.Cooldowns != null && now < node.Mob.Cooldowns[i])) continue;
                        float x = closest.x - origin.x, y = closest.y - origin.y, z = closest.z - origin.z;
                        // Static reach uses the closest surface; facing uses its centre.
                        if (x * x + y * y + z * z <= ability.Range * ability.Range && FacingBuilding(ability, origin, forward, center)) return true;
                    }
                }
                return false;
            }
            catch (NullReferenceException) { return true; } // Unready native target components are uncertain, not a global addon fault.
            finally { if (forecastColliders != null) Array.Clear(forecastColliders, 0, Math.Min(count, forecastColliders.Length)); forecastCharacters.Clear(); forecastBuildings.Clear(); }
        }
        private bool ForecastCharacter(Node node, MobForecastProfile profile, float now, float maximum, Vector3 origin, Vector3 forward, Character target)
        {
            if (!target || target == node.Mob.Actor || !forecastCharacters.Add(target) || target.IsDead()) return false;
            ZNetView view = target.GetComponent<ZNetView>();
            Vector3 position = view && view.IsValid() ? view.GetZDO().GetPosition() : target.transform.position;
            float radius = target.GetRadius();
            if (!Finite(position) || Single.IsNaN(radius) || Single.IsInfinity(radius) || radius < 0f) return true;
            if (!Near(origin, position, maximum + radius)) return false;
            bool enemy = BaseAI.IsEnemy(node.Mob.Actor, target);
            for (int i = 0; i < profile.Abilities.Count; i++)
                if ((node.Mob.Cooldowns == null || now >= node.Mob.Cooldowns[i]) && profile.Abilities[i].Enemy == enemy &&
                    MobForecastProfile.Contains(profile.Abilities[i], origin, forward, position, radius)) return true;
            return false;
        }
        private static bool FacingBuilding(MobForecastProfile.Ability ability, Vector3 origin, Vector3 forward, Vector3 center)
        {
            float x = center.x - origin.x, z = center.z - origin.z;
            float length = (x * x + z * z) * (forward.x * forward.x + forward.z * forward.z);
            float cosine = length <= .000001f ? 1f : (x * forward.x + z * forward.z) / (float)Math.Sqrt(length);
            return ability.Inverted ? cosine <= ability.Cosine + .000001f : cosine + .000001f >= ability.Cosine;
        }
    }
}
