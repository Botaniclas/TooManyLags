using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsDedicatedAddon
{
    internal sealed partial class Controller
    {
        private sealed class MobState
        {
            internal Humanoid Actor;
            internal MonsterAI Ai;
            internal MobAttackProfile Profile;
            internal long Source;
            internal ushort Revision;
            internal bool Pending, Armed, SawAttack, Looping, Combat, IdleHandoffObserved;
            internal float TriggerAt, IdleAt = Single.PositiveInfinity;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal bool TargetValid;
            internal int SelectionEpoch;
            internal long Target;
            internal float[] Cooldowns;
            internal float CooldownClock = Single.NegativeInfinity;
            internal bool ForecastDirty = true;
        }
        private struct MobPlayer
        {
            internal long Uid; internal ZDO Data; internal Vector3 Position;
            internal MobPlayer(long uid, ZDO data, Vector3 position) { Uid = uid; Data = data; Position = position; }
        }
        private readonly Dictionary<ZDO, Node> mobs = new Dictionary<ZDO, Node>();
        private readonly Dictionary<int, MobAttackProfile> mobProfiles = new Dictionary<int, MobAttackProfile>();
        private readonly HashSet<Node> sharedMobs = new HashSet<Node>(), mobDirty = new HashSet<Node>(), pendingMobs = new HashSet<Node>();
        private readonly DueQueue<Node> mobDue = new DueQueue<Node>();
        private List<MobPlayer> mobPlayers = new List<MobPlayer>(), previousMobPlayers = new List<MobPlayer>();
        private bool mobPlayersChanged;
        private int mobPlayerEpoch;
        internal int PendingMobTransfers { get { return pendingMobs.Count; } }
        internal int SharedMobCount { get { return sharedMobs.Count; } }
        internal bool HasMobNodes { get { return mobs.Count != 0; } }
        internal int MobGuardChecks, MobConeChecks;
        private static Groups CreatureGroup(ZDO data, Groups groups)
        {
            if ((groups & (Groups.TamedAnimals | Groups.HostileMobs)) == 0) return groups;
            return groups & (data.GetBool(ZDOVars.s_tamed) ? Groups.TamedAnimals : Groups.HostileMobs);
        }
        private void AddMob(Node node)
        {
            Humanoid actor = node.View.GetComponent<Humanoid>(); MonsterAI ai = node.View.GetComponent<MonsterAI>();
            if (!actor || !ai) return;
            MobAttackProfile profile;
            if (!mobProfiles.TryGetValue(node.Data.GetPrefab(), out profile))
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(node.Data.GetPrefab());
                profile = new MobAttackProfile(prefab ? prefab.GetComponent<Humanoid>() : actor, options.MobAttackForecast);
                mobProfiles.Add(node.Data.GetPrefab(), profile);
            }
            node.Mob = new MobState { Actor = actor, Ai = ai, Profile = profile, Source = node.Data.GetOwner(), Revision = node.Data.OwnerRevision,
                Position = node.Data.GetPosition(), Rotation = node.Data.GetRotation(), Combat = node.Data.GetBool(ZDOVars.s_alert) || node.Data.GetBool(ZDOVars.s_haveTargetHash) };
            mobs.Add(node.Data, node);
        }
        private void ForgetMob(Node node)
        { CancelMob(node); mobs.Remove(node.Data); sharedMobs.Remove(node); mobDirty.Remove(node); }
        private void CancelMob(Node node)
        { mobDue.Remove(node); pendingMobs.Remove(node); forecastWaiting.Remove(node); if (node.Mob != null) node.Mob.Pending = false; }
        private void MobOwnerChanged(Node node)
        {
            if (node.Mob == null) return;
            CancelMob(node);
            MobState mob = node.Mob;
            mob.Source = node.Data.GetOwner(); mob.Revision = node.Data.OwnerRevision;
            mob.Armed = mob.SawAttack = mob.Looping = false; mob.IdleAt = Single.PositiveInfinity;
            mob.IdleHandoffObserved = false;
            mob.TargetValid = false;
            ResetMobCooldowns(mob);
            mob.ForecastDirty = true;
        }
        private void BeginMobPlayers()
        {
            var old = previousMobPlayers; previousMobPlayers = mobPlayers; mobPlayers = old; mobPlayers.Clear(); mobPlayersChanged = false;
        }
        private void AddMobPlayer(long uid, ZDO data, Vector3 position)
        {
            if ((options.Groups & Groups.HostileMobs) == 0) return;
            int index = mobPlayers.Count;
            if (index >= previousMobPlayers.Count || previousMobPlayers[index].Uid != uid || !Same(position, previousMobPlayers[index].Position)) mobPlayersChanged = true;
            mobPlayers.Add(new MobPlayer(uid, data, position));
        }
        private static bool Same(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        private void MobPoseChanged(Node node)
        {
            if (node.Mob == null || !sharedMobs.Contains(node)) return;
            Vector3 position = node.Data.GetPosition(); Quaternion rotation = node.Data.GetRotation();
            if (Same(position, node.Mob.Position) && rotation.Equals(node.Mob.Rotation)) return;
            node.Mob.Position = position; node.Mob.Rotation = rotation; node.Mob.TargetValid = false; mobDirty.Add(node);
        }
        private void RefreshMobCones()
        {
            if (mobs.Count == 0) return;
            if (mobPlayersChanged || mobPlayers.Count != previousMobPlayers.Count)
            {
                mobPlayerEpoch++;
                if (forecastWaiting.Count != 0)
                {
                    if (mobPlayers.Count != previousMobPlayers.Count) forecastDirty = true;
                    else for (int i = 0; i < mobPlayers.Count; i++)
                    {
                        if (mobPlayers[i].Uid != previousMobPlayers[i].Uid) { forecastDirty = true; break; }
                        if (!Same(mobPlayers[i].Position, previousMobPlayers[i].Position)) ForecastTargetChanged(mobPlayers[i].Data, previousMobPlayers[i].Position);
                    }
                }
                foreach (Node node in sharedMobs) if (node.InGrid && !node.Removed) world.Received(node, Time.unscaledTime);
            }
            foreach (Node node in mobDirty) if (node.InGrid && !node.Removed) world.Received(node, Time.unscaledTime);
            mobDirty.Clear();
        }
        private long MobTarget(Node node, long field, bool force = false)
        {
            if (ReferenceEquals(node, mobTestNode)) return mobTestTarget;
            if (field != server || world.Grid.Participants(node.Cell) < 2) { sharedMobs.Remove(node); node.Mob.TargetValid = false; return field; }
            sharedMobs.Add(node);
            if (!force && node.Mob.TargetValid && node.Mob.SelectionEpoch == mobPlayerEpoch) return node.Mob.Target;
            MobConeChecks++;
            Vector3 position = node.Data.GetPosition(), forward = node.Data.GetRotation() * new Vector3(0, 0, 1);
            if (!Finite(position) || !Finite(forward)) return server;
            float radius = node.Mob.Actor.IsBoss() ? options.BossMobConeRange : options.MobConeRange;
            float range = radius * radius, best = range;
            int eligible = 0;
            long selected = server, owner = node.Data.GetOwner();
            foreach (MobPlayer player in mobPlayers)
            {
                if (!player.Data.IsValid() || player.Data.GetOwner() != player.Uid || player.Data.GetFloat(ZDOVars.s_health, 1f) <= 0f) continue;
                Vector3 at = player.Data.GetPosition(); if (!Finite(at)) continue;
                float x = at.x - position.x, y = at.y - position.y, z = at.z - position.z;
                float distance = x * x + y * y + z * z;
                if (distance > range || (!options.MobConeServerWhenMultiple && distance > best)) continue;
                float dot = x * forward.x + y * forward.y + z * forward.z;
                if (dot < 0f || dot * dot + .000001f < distance * .5f || !Available(player.Uid, position)) continue;
                if (++eligible > 1 && options.MobConeServerWhenMultiple) { selected = server; break; }
                if (distance > best) continue;
                if (distance == best && selected != server && selected == owner) continue;
                if (distance == best && selected != server && player.Uid != owner && selected < player.Uid) continue;
                selected = player.Uid; best = distance;
            }
            node.Mob.Target = selected; node.Mob.TargetValid = true; node.Mob.SelectionEpoch = mobPlayerEpoch;
            return selected;
        }
        private static bool Finite(Vector3 value)
        { return !Single.IsNaN(value.x) && !Single.IsNaN(value.y) && !Single.IsNaN(value.z) && !Single.IsInfinity(value.x) && !Single.IsInfinity(value.y) && !Single.IsInfinity(value.z); }
        private void WakeMob(Node node)
        { if (node.Mob != null && node.Mob.Pending && !mobDue.Contains(node)) mobDue.Add(node, Time.unscaledTime); }
        internal void MobTrigger(ZDO data, long source, string trigger)
        {
            Node node;
            if (data == null || trigger == null || !mobs.TryGetValue(data, out node) || node.Group != Groups.HostileMobs || source != data.GetOwner()) return;
            MobState mob = node.Mob;
            if (mob.Source != source || mob.Revision != data.OwnerRevision) MobOwnerChanged(node);
            if (trigger == "attack_abort")
            { if (mob.Armed) { mob.Looping = false; mob.SawAttack = true; WakeMob(node); } return; }
            bool looping;
            if (!mob.Profile.Triggers.TryGetValue(trigger, out looping)) return;
            ObserveMobAbility(node, trigger, source);
            mob.IdleHandoffObserved = false;
            mob.Armed = true; mob.SawAttack = false; mob.Looping = looping;
            mob.TriggerAt = Time.unscaledTime; mob.IdleAt = Single.PositiveInfinity; WakeMob(node);
        }
        // Native observation is not a source-client grant. Never expire a
        // known running/looping attack into a forced ownership assignment.
        private bool MobIdle(Node node, long owner, float now, out bool retry)
        {
            MobGuardChecks++; retry = true; MobState mob = node.Mob;
            if (mob.Source != owner || mob.Revision != node.Data.OwnerRevision) MobOwnerChanged(node);
            bool attack = mob.Actor.InAttack();
            if (attack) { mob.IdleHandoffObserved = false; if (owner == server || mob.Armed) mob.SawAttack = true; mob.IdleAt = Single.PositiveInfinity; return false; }
            if (mob.Actor.IsStaggering() || mob.Ai.IsCharging()) { mob.IdleAt = Single.PositiveInfinity; return false; }
            if (owner == server)
            {
                Attack current = MobAttackAccess.Current(mob.Actor);
                return current == null || current.IsDone();
            }
            bool combat = node.Data.GetBool(ZDOVars.s_alert) || node.Data.GetBool(ZDOVars.s_haveTargetHash);
            // Dispatch precedes Animator evaluation. Observe that startup gap;
            // expiry parks the intent and never forces an unobserved attack out.
            if (mob.Armed && !mob.SawAttack && now - mob.TriggerAt < .5f) return false;
            if ((mob.Armed && (mob.Looping || !mob.SawAttack)) || (!mob.Armed && combat && !mob.IdleHandoffObserved))
            {
                // Late-loaded/unknown combat history waits for an actual
                // trigger/animation observation or a replicated combat exit.
                if (combat || mob.Looping || now - mob.TriggerAt < .1f) { retry = false; return false; }
                mob.Armed = false;
            }
            if (Single.IsPositiveInfinity(mob.IdleAt)) { mob.IdleAt = now; return false; }
            return now - mob.IdleAt >= (mob.SawAttack ? .02f : .1f);
        }
        private ApplyResult ApplyMob(Node node, long field, long owner)
        {
            MobState mob = node.Mob;
            if (mob == null || !mob.Actor || !mob.Ai || mob.Actor.GetFaction() == Character.Faction.Players ||
                node.Data.GetFloat(ZDOVars.s_health, 1f) <= 0f || State(node) != 1)
            { CancelMob(node); Wait(node); return ApplyResult.Dropped; }
            long target = MobTarget(node, field);
            if (target == owner) { CancelMob(node); node.Managed = true; node.ManagedOwner = target; return ApplyResult.Done; }
            if (node.Data.OwnerRevision == UInt16.MaxValue || !Available(target, node.Data.GetPosition()) ||
                !node.View.gameObject.activeInHierarchy ||
                (owner != 0 && owner != server && !Available(owner, node.Data.GetPosition())))
            { CancelMob(node); Wait(node); return ApplyResult.Dropped; }
            bool retry;
            if (!MobIdle(node, owner, Time.unscaledTime, out retry))
            {
                forecastWaiting.Remove(node);
                mob.Pending = true; pendingMobs.Add(node);
                if (retry && !mobDue.Contains(node)) mobDue.Add(node, Time.unscaledTime + .02f);
                Wait(node); return ApplyResult.Dropped;
            }
            // A forecast may veto, never replace, the existing source guard.
            // The server can commit its own idle source before its next AI step.
            if (owner != server && ForecastMobAttack(node))
            {
                mob.Pending = true; pendingMobs.Add(node); forecastWaiting.Add(node);
                mobDue.Remove(node); Wait(node); return ApplyResult.Dropped;
            }
            forecastWaiting.Remove(node);
            // No callbacks or asynchronous source permission are invented.
            // Recheck the real native owner before the single server-thread write.
            if (node.Data.GetOwner() != owner || mob.Source != owner || mob.Revision != node.Data.OwnerRevision)
            { MobOwnerChanged(node); Wait(node); return ApplyResult.Dropped; }
            // Motion during the wait must never commit an obsolete cone target.
            target = MobTarget(node, field, true);
            if (target == owner) { CancelMob(node); node.Managed = true; node.ManagedOwner = owner; return ApplyResult.Done; }
            if (!Available(target, node.Data.GetPosition()) || (target == server && !MobileAreaReady(node)))
            { CancelMob(node); Wait(node); return ApplyResult.Dropped; }
            CancelMob(node); Assign(node, target); MobOwnerChanged(node);
            if (owner != 0 && owner != server) NativeSnapshot.SendOwnerChange(node.Data, owner);
            if (target != server) NativeSnapshot.SendOwnerChange(node.Data, target);
            node.Managed = node.Data.GetOwner() == target; node.ManagedOwner = target;
            // Our own guarded idle handoff preserves observation continuity.
            // Alert/target presence during pursuit is not a new attack. Native
            // owner revisions, triggers and observed attacks invalidate this bit.
            if (node.Managed && target != server) mob.IdleHandoffObserved = true;
            if (node.Managed && mobTestNode != null) MobTestCommitted(node, owner, target);
            return node.Managed ? ApplyResult.Done : ApplyResult.Dropped;
        }
        private void ServiceMobs(float now)
        {
            if (forecastDirty)
            {
                forecastDirty = false;
                foreach (Node waitingMob in forecastWaiting) { waitingMob.Mob.ForecastDirty = true; WakeMob(waitingMob); }
            }
            Node node;
            while (mobDue.Take(now, out node))
            {
                if (node.Removed || !node.InGrid || node.Released || node.Group != Groups.HostileMobs || peers.Count == 0)
                { CancelMob(node); continue; }
                long field = world.Grid.Owner(node.Cell);
                if (field == 0 && options.Keep) { CancelMob(node); continue; }
                ApplyResult result = Apply(node, field);
                if (result == ApplyResult.Done) world.Received(node, now);
            }
        }
        private void ClearMobs()
        {
            if (mobTestNode != null) StopMobTest("last player left", false);
            mobDue.Clear(); foreach (Node node in pendingMobs) if (node.Mob != null) node.Mob.Pending = false;
            pendingMobs.Clear(); sharedMobs.Clear(); mobDirty.Clear(); mobPlayers.Clear(); previousMobPlayers.Clear();
            forecastWaiting.Clear(); forecastDirty = false;
        }
    }

    internal sealed class MobAttackProfile
    {
        internal readonly Dictionary<string, bool> Triggers = new Dictionary<string, bool>(StringComparer.Ordinal);
        internal readonly MobForecastProfile Forecast;
        internal MobAttackProfile(Humanoid actor, bool forecast = true)
        {
            if (!actor) return;
            Items(actor.m_defaultItems); Items(actor.m_randomWeapon);
            if (actor.m_randomSets != null) foreach (Humanoid.ItemSet set in actor.m_randomSets) if (set != null) Items(set.m_items);
            if (actor.m_unarmedWeapon && actor.m_unarmedWeapon.m_itemData != null && actor.m_unarmedWeapon.m_itemData.m_shared != null)
            { Add(actor.m_unarmedWeapon.m_itemData.m_shared.m_attack); Add(actor.m_unarmedWeapon.m_itemData.m_shared.m_secondaryAttack); }
            if (forecast) Forecast = new MobForecastProfile(actor);
        }
        private void Items(GameObject[] items)
        {
            if (items == null) return;
            foreach (GameObject item in items)
            {
                ItemDrop drop = item ? item.GetComponent<ItemDrop>() : null;
                if (!drop || drop.m_itemData == null || drop.m_itemData.m_shared == null) continue;
                Add(drop.m_itemData.m_shared.m_attack); Add(drop.m_itemData.m_shared.m_secondaryAttack);
            }
        }
        private void Add(Attack attack)
        {
            if (attack == null || String.IsNullOrEmpty(attack.m_attackAnimation)) return;
            int count = attack.m_attackChainLevels > 1 ? attack.m_attackChainLevels : attack.m_attackRandomAnimations >= 2 ? attack.m_attackRandomAnimations : 0;
            if (count > 64) return; // Unsupported custom animation set; no unbounded cold allocation.
            if (count == 0) AddTrigger(attack.m_attackAnimation, attack.m_loopingAttack);
            else for (int i = 0; i < count; i++) AddTrigger(attack.m_attackAnimation + i, attack.m_loopingAttack);
        }
        private void AddTrigger(string name, bool looping)
        { bool previous; Triggers[name] = looping || (Triggers.TryGetValue(name, out previous) && previous); }
    }
    internal static class MobAttackAccess
    {
        private static readonly Func<Humanoid, Attack> current = Bind();
        private static Func<Humanoid, Attack> Bind()
        {
            FieldInfo field = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
            if (field == null || field.FieldType != typeof(Attack) || field.IsStatic) throw new NotSupportedException("Native mob Attack state changed.");
            var method = new DynamicMethod("TML_CurrentMobAttack", typeof(Attack), new[] { typeof(Humanoid) }, typeof(MobAttackAccess), true);
            ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
            return (Func<Humanoid, Attack>)method.CreateDelegate(typeof(Func<Humanoid, Attack>));
        }
        internal static void Validate() { if (current == null) throw new NotSupportedException("Native mob Attack reader unavailable."); }
        internal static Attack Current(Humanoid actor) { return current(actor); }
    }
    [HarmonyPatch(typeof(ZSyncAnimation), "RPC_SetTrigger")]
    internal static class MobAnimationTrigger
    {
        [HarmonyPrefix] private static void Prefix(ZSyncAnimation __instance, long __0, string __1)
        {
            try
            {
                Controller controller = Plugin.Current; if (controller == null || !controller.HasMobNodes) return;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (view && view.IsValid()) controller.MobTrigger(view.GetZDO(), __0, __1);
            }
            catch (Exception error) { Plugin.Fault(error); }
        }
    }
}
