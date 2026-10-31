#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
// Experiment only: private copies of native steering, no patches to live methods.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace NativeMobPreviewAudit
{
    internal sealed class PreviewAction : Exception
    {
        internal PreviewAction() { }
    }

    internal sealed class NativeSteeringPlan
    {
        private readonly ForkPolicy policy = new ForkPolicy();
        private readonly Queue<MethodInfo> queue = new Queue<MethodInfo>();
        private readonly HashSet<FieldInfo> state = new HashSet<FieldInfo>();
        internal readonly NativeAiFork Fork;
        internal readonly Func<MonsterAI, float, bool> Step;
        internal readonly FieldInfo[] AiFields, ControlFields;
        internal readonly Func<BaseAI, Character, bool> See, Hear;
        internal readonly Func<BaseAI, ItemDrop.ItemData, bool> CanAttack;
        internal int Copies { get { return policy.Copies.Count; } }
        internal int FieldReads { get { return policy.FieldReads.Count; } }
        internal int ReadCalls { get { return policy.Reads.Count; } }
        internal IEnumerable<MethodInfo> CopiedMethods { get { return policy.Copies; } }
        internal IEnumerable<MethodBase> ReadDependencies { get { return policy.Reads; } }

        internal NativeSteeringPlan()
        {
            // The constructor below audits every native method replacement,
            // mutable field and required adapter signature. Platform patchers can
            // legitimately alter the assembly file hash without changing those
            // contracts, so the file hash must not be the hard availability gate.
            Replace(typeof(BaseAI), "UpdateRegeneration", "IgnoreBaseTick", typeof(float));
            Replace(typeof(BaseAI), "UpdateTakeoffLanding", "CheckFlight", typeof(float));
            Replace(typeof(MonsterAI), "UpdateSleep", "CheckAwake", typeof(float));
            Replace(typeof(BaseAI), "IsSleeping", "ReadSleeping");
            Replace(typeof(MonsterAI), "UpdateTarget", "KeepTarget", typeof(Humanoid), typeof(float), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType());
            Replace(typeof(Humanoid), "GetCurrentWeapon", "CurrentWeapon");
            Replace(typeof(Humanoid), "EquipItem", "EquipWeapon", typeof(ItemDrop.ItemData), typeof(bool));
            Replace(typeof(Humanoid), "UnequipItem", "UnequipWeapon", typeof(ItemDrop.ItemData), typeof(bool));
            Replace(typeof(Inventory), "GetAllItems", "InventoryWeapons");
            Replace(typeof(BaseAI), "HaveFriendsInRange", "Friends", typeof(float), typeof(Character).MakeByRefType(), typeof(Character).MakeByRefType());
            Replace(typeof(MonsterAI), "DoAttack", "IgnoreAttack", typeof(Character), typeof(bool));
            Replace(typeof(MonsterAI), "UpdateConsumeItem", "RejectConsume", typeof(Humanoid), typeof(float));
            Replace(typeof(Sadle), "UpdateRiding", "RejectRiding", typeof(float));
            Replace(typeof(BaseAI), "MoveAwayAndDespawn", "RejectDespawn", typeof(float), typeof(bool));
            Replace(typeof(Character), "Jump", "RejectJump", typeof(bool));
            Replace(typeof(BaseAI), "SetAlerted", "Alert", typeof(bool));
            Replace(typeof(MonsterAI), "SetAlerted", "MonsterAlert", typeof(bool));
            Replace(typeof(BaseAI), "SetTargetInfo", "IgnoreTargetInfo", typeof(ZDOID));
            Replace(typeof(BaseAI), "SetHuntPlayer", "Hunt", typeof(bool));
            Replace(typeof(BaseAI), "HuntPlayer", "ReadHunt");
            Replace(typeof(MonsterAI), "HuntPlayer", "ReadMonsterHunt");
            Replace(typeof(BaseAI), "HaveFriendInRange", "Friend", typeof(float));
            Replace(typeof(BaseAI), "HaveHurtFriendInRange", "HurtFriend", typeof(float));
            Replace(typeof(BaseAI), "ChargeStart", "Charge", typeof(string));
            Replace(typeof(BaseAI), "ChargeStop", "Uncharge");
            Replace(typeof(Character), "OnTargeted", "IgnoreTargeted", typeof(bool), typeof(bool));
            Replace(typeof(ZNetView), "IsOwner", "ViewOwner");
            Replace(typeof(Character), "IsTamed", "Tamed");
            Replace(typeof(Character), "IsTamed", "TamedAt", typeof(float));
            Replace(typeof(BaseAI), "IsAggravated", "Aggravated");
            Replace(typeof(Character), "GetVelocity", "Velocity");
            Replace(typeof(Character), "IsCrouching", "Crouching");
            Replace(typeof(Character), "IsFlying", "Flying");
            Replace(typeof(Character), "IsSwimming", "Swimming");
            Replace(typeof(Character), "InAttack", "InAttack");
            Replace(typeof(Humanoid), "InAttack", "HumanoidInAttack");
            Replace(typeof(Character), "GetCenterPoint", "CenterPoint");
            Replace(typeof(Transform), "get_position", "Position");
            Replace(typeof(Time), "get_time", "NativeTime");
            Replace(typeof(Transform), "get_forward", "Forward");
            Replace(typeof(Transform), "get_right", "Right");
            Replace(typeof(Transform), "get_up", "Up");
            Replace(typeof(Transform), "TransformPoint", "TransformPoint", typeof(Vector3));
            Replace(typeof(ZLog), "Log", "IgnoreLog", typeof(object));
            Replace(typeof(ZLog), "LogError", "IgnoreLog", typeof(object));
            ReplaceRead(typeof(ItemDrop.ItemData), "m_lastAttackTime", "WeaponAttackTime");
            foreach (string name in new[] { "s_tempRaycastHits", "s_tempSphereOverlap" })
                policy.StaticReads.Add(AccessTools.Field(typeof(BaseAI), name), AccessTools.Method(typeof(NativeSteeringAdapters), name == "s_tempRaycastHits" ? "RayBuffer" : "ColliderBuffer"));
            foreach (string name in new[] { "optimalWeapons", "outofRangeWeapons", "allWeapons" })
                policy.StaticReads.Add(AccessTools.Field(typeof(Humanoid), name), AccessTools.Method(typeof(NativeSteeringAdapters),
                    name == "optimalWeapons" ? "OptimalWeapons" : name == "outofRangeWeapons" ? "OutOfRangeWeapons" : "AllWeapons"));
            Add(AccessTools.DeclaredMethod(typeof(MonsterAI), "UpdateAI", new[] { typeof(float) }));
            Add(AccessTools.Method(typeof(BaseAI), "CanSeeTarget", new[] { typeof(Character) }));
            Add(AccessTools.Method(typeof(BaseAI), "CanHearTarget", new[] { typeof(Character) }));
            Add(AccessTools.Method(typeof(BaseAI), "CanUseAttack", new[] { typeof(ItemDrop.ItemData) }));
            while (queue.Count != 0) Audit(queue.Dequeue());
            Fork = new NativeAiFork(policy);
            Step = Fork.Bind<Func<MonsterAI, float, bool>>(AccessTools.DeclaredMethod(typeof(MonsterAI), "UpdateAI", new[] { typeof(float) }));
            See = Fork.Bind<Func<BaseAI, Character, bool>>(AccessTools.Method(typeof(BaseAI), "CanSeeTarget", new[] { typeof(Character) }));
            Hear = Fork.Bind<Func<BaseAI, Character, bool>>(AccessTools.Method(typeof(BaseAI), "CanHearTarget", new[] { typeof(Character) }));
            CanAttack = Fork.Bind<Func<BaseAI, ItemDrop.ItemData, bool>>(
                AccessTools.Method(typeof(BaseAI), "CanUseAttack", new[] { typeof(ItemDrop.ItemData) }));
            List<FieldInfo> ai = new List<FieldInfo>(), control = new List<FieldInfo>();
            foreach (FieldInfo field in state)
                if (field.DeclaringType == typeof(BaseAI) || field.DeclaringType == typeof(MonsterAI)) ai.Add(field);
                else if (field.DeclaringType == typeof(Character) || field.DeclaringType == typeof(Humanoid)) control.Add(field);
            // Adapters implement only these extra shadow-local state changes.
            foreach (string name in new[] { "m_alerted", "m_huntPlayer", "m_charging", "m_randomFlyTimer" }) Include(ai, typeof(BaseAI), name);
            Include(ai, typeof(MonsterAI), "m_timeSinceSensedTargetCreature");
            Include(ai, typeof(MonsterAI), "m_timeSinceAttacking");
            Include(ai, typeof(MonsterAI), "m_updateTargetTimer");
            Include(ai, typeof(MonsterAI), "m_updateWeaponTimer");
            Include(ai, typeof(MonsterAI), "m_unableToAttackTargetTimer");
            Include(ai, typeof(BaseAI), "m_path");
            Include(control, typeof(Character), "m_flying");
            Include(control, typeof(Character), "m_swimTimer");
            Include(control, typeof(Character), "m_lavaTimer");
            Include(control, typeof(Character), "m_aboveOrInLavaTimer");
            Include(control, typeof(Humanoid), "m_timeSinceLastAttack");
            AiFields = ai.ToArray(); ControlFields = control.ToArray();
        }

        private static void Include(List<FieldInfo> list, Type type, string name)
        { FieldInfo field = AccessTools.Field(type, name); if (field == null) throw new NotSupportedException("Missing state: " + name); if (!list.Contains(field)) list.Add(field); }
        private void Replace(Type type, string name, string adapter, params Type[] arguments)
        {
            MethodInfo method = AccessTools.DeclaredMethod(type, name, arguments), replacement = AccessTools.Method(typeof(NativeSteeringAdapters), adapter);
            if (method == null || replacement == null) throw new NotSupportedException("Missing adapter contract: " + type.Name + "." + name);
            policy.Replacements.Add(method, replacement);
        }
        private void ReplaceRead(Type type, string name, string adapter)
        {
            FieldInfo field = AccessTools.Field(type, name);
            MethodInfo replacement = AccessTools.Method(typeof(NativeSteeringAdapters), adapter);
            if (field == null || replacement == null)
                throw new NotSupportedException("Missing field-read contract: " + type.Name + "." + name);
            policy.FieldReads.Add(field, replacement);
        }
        private void Add(MethodInfo method)
        { if (method == null) throw new NotSupportedException("Missing native method."); if (policy.Copies.Add(method)) queue.Enqueue(method); }
        private void Audit(MethodInfo method)
        {
            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
            {
                FieldInfo field = instruction.operand as FieldInfo;
                if (instruction.opcode == OpCodes.Stfld)
                {
                    if (field.DeclaringType != typeof(BaseAI) && field.DeclaringType != typeof(MonsterAI) && field.DeclaringType != typeof(Character) && field.DeclaringType != typeof(Humanoid) && field.DeclaringType != typeof(Vector3))
                        throw new NotSupportedException("Foreign native state write: " + field);
                    policy.Writes.Add(field); if (field.DeclaringType != typeof(Vector3)) state.Add(field);
                }
                string op = instruction.opcode.Name;
                if (op.StartsWith("stind.", StringComparison.Ordinal) || instruction.opcode == OpCodes.Initobj || instruction.opcode == OpCodes.Stobj)
                {
                    // Current audited methods use local value types/out parameters.
                    if (method.DeclaringType != typeof(BaseAI) && method.DeclaringType != typeof(Character)) throw new NotSupportedException("Unaudited indirect write: " + method);
                    policy.IndirectWrites.Add(method);
                }
                MethodBase call = instruction.operand as MethodBase;
                if (call == null) continue;
                MethodInfo target = call as MethodInfo;
                if (target != null && policy.Replacements.ContainsKey(target)) continue;
                if (ReadOnly(call)) { policy.Reads.Add(call); continue; }
                if (target != null && (target.DeclaringType == typeof(BaseAI) || target.DeclaringType == typeof(MonsterAI) || target.DeclaringType == typeof(Character) || target.DeclaringType == typeof(Humanoid)))
                { Add(target); continue; }
                throw new NotSupportedException("Unreviewed native dependency: " + method.DeclaringType.Name + "." + method.Name + " -> " + call.DeclaringType.FullName + "." + call);
            }
        }

        private static bool ReadOnly(MethodBase method)
        {
            Type type = method.DeclaringType; string name = method.Name;
            if (type == typeof(Vector3) || type == typeof(Quaternion) || type == typeof(Mathf)) return true; // Value-only math; no shared objects.
            if (type == typeof(UnityEngine.Object)) return name == "op_Implicit" || name == "op_Equality" || name == "op_Inequality" || name == "get_name";
            if (type == typeof(Time)) return name == "get_time" || name == "get_fixedDeltaTime";
            if (type == typeof(UnityEngine.Random)) return name == "Range" || name == "get_value"; // State restored by transaction.
            if (type == typeof(Physics)) return name == "Raycast" || name == "SphereCast" || name == "RaycastNonAlloc" || name == "OverlapSphereNonAlloc";
            if (type == typeof(Component)) return name == "get_transform" || name == "GetComponentInParent";
            if (type == typeof(GameObject)) return name == "get_transform" || name == "GetComponent";
            if (type == typeof(Transform)) return name == "get_localScale";
            if (type == typeof(CapsuleCollider)) return name == "get_height" || name == "get_radius" || name == "get_center";
            if (type == typeof(Collider)) return name == "get_bounds";
            if (type == typeof(Bounds)) return name == "get_center";
            if (type == typeof(RaycastHit)) return name == "get_point" || name == "get_distance" || name == "get_collider";
            if (type == typeof(Rigidbody)) return name == "get_linearVelocity";
            if (type == typeof(ZNetView)) return name == "IsValid" || name == "GetZDO";
            if (type == typeof(ZDO)) return name == "GetBool" || name == "GetFloat" || name == "GetVec3" || name == "IsOwner";
            if (type == typeof(ZDOID)) return name == "IsNone";
            if (type == typeof(Pathfinding)) return name == "get_instance" || name == "GetPath" || name == "HavePath"; // Native read-query caches may update.
            if (type == typeof(ZoneSystem)) return name == "get_instance" || name == "GetGlobalKey" || name == "GetSolidHeight" || name == "IsLava";
            if (type == typeof(Game)) return name == "get_instance";
            if (type == typeof(EffectArea)) return name == "IsPointInsideNoMonsterArea" || name == "IsPointCloseToNoMonsterArea" || name == "IsPointInsideArea" || name == "GetRadius";
            if (type == typeof(EnvMan)) return name == "IsDay";
            if (type == typeof(RandEventSystem)) return name == "HaveActiveEvent" || name == "InEvent";
            if (type == typeof(ParticleMist)) return name == "IsMistBlocked" || name == "IsInMist";
            if (type == typeof(StaticTarget)) return name == "GetAllColliders" || name == "GetCenter" || name == "FindClosestPoint" || name == "IsPriorityTarget" || name == "IsRandomTarget";
            if (type == typeof(Player)) return name == "InCrownMode" || name == "InDebugFlyMode" || name == "InGhostMode";
            if (type == typeof(Attack)) return name == "IsDone";
            if (type == typeof(ItemDrop.ItemData)) return name == "IsWeapon";
            if (type == typeof(MonsterAI) && name == "PheromoneFleeCheck") return true; // Native read-only status iteration, preserves effect interpretation.
            if (type == typeof(Character))
                return name == "IsPlayer" || name == "IsDead" || name == "InGhostMode" || name == "GetStealthFactor" || name == "IsCrouching" || name == "InAttack" || name == "IsStaggering" || name == "IsSwimming" || name == "GetNoiseRange" || name == "GetTimeSinceLastAttack" || name == "InInterior" || name == "InLava" || name == "AboveOrInLava";
            if (type == typeof(string)) return name == "IsNullOrEmpty" || name == "op_Equality" || name == "get_Length" || name == "Concat" || name == "Format";
            if (type == typeof(Utils)) return name == "DistanceXZ" || name == "LengthXZ";
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return name == "ContainsKey";
            if (type == typeof(List<Vector3>)) return name == "get_Count" || name == "get_Item" || name == "RemoveAt";
            if (type == typeof(List<Collider>)) return name == "Contains";
            if (type == typeof(List<ItemDrop.ItemData>))
                return name == "Clear" || name == "Add" || name == "get_Count" || name == "get_Item" || name == "GetEnumerator";
            if (type == typeof(List<ItemDrop.ItemData>.Enumerator)) return name == "get_Current" || name == "MoveNext";
            if (type == typeof(IDisposable)) return name == "Dispose";
            return false;
        }
    }

    internal static class NativeSteeringAdapters
    {
        private static readonly Func<Humanoid, ItemDrop.ItemData, bool> haveAmmo =
            (Func<Humanoid, ItemDrop.ItemData, bool>)Delegate.CreateDelegate(typeof(Func<Humanoid, ItemDrop.ItemData, bool>), AccessTools.Method(typeof(Attack), "HaveAmmo"));
#pragma warning disable 0649
        [ThreadStatic] internal static SteeringContext Current;
#pragma warning restore 0649
        private static SteeringContext C { get { if (Current == null) throw new InvalidOperationException("No native preview scope."); return Current; } }
        internal static void IgnoreBaseTick(BaseAI actor, float dt) { C.Own(actor); }
        internal static void CheckFlight(BaseAI actor, float dt)
        {
            SteeringContext c = C; c.Own(actor);
            float timer = c.ReadBaseFloat("m_randomFlyTimer");
            bool flying = c.ShadowFlying;
            bool busy = c.ShadowInAttack || (c.VisualAttacks ? c.ShadowStagger : c.Actor.IsStaggering());
            float altitude = actor.m_randomFly ? c.Altitude() : 0f;
            bool choose = actor.m_randomFly && !busy && (flying
                ? timer + dt > actor.m_airDuration && altitude < actor.m_maxLandAltitude
                : timer + dt > actor.m_groundDuration);
            TooManyLags.NativeFlightTransition.Advance(actor.m_randomFly, dt, busy,
                flying, altitude, actor.m_maxLandAltitude, actor.m_airDuration, actor.m_groundDuration,
                actor.m_chanceToLand, actor.m_chanceToTakeoff, choose ? RandomValue() : 1f,
                ref timer, ref flying);
            c.WriteBaseFloat("m_randomFlyTimer", timer);
            c.ShadowFlying = flying;
        }
        internal static void CheckAwake(MonsterAI actor, float dt) { C.Own(actor); if (actor.IsSleeping()) throw new PreviewAction(); }
        internal static bool ReadSleeping(BaseAI actor) { C.Own(actor); return C.Ai.IsSleeping(); }
        internal static void KeepTarget(MonsterAI actor, Humanoid humanoid, float dt, out bool canHear, out bool canSee)
        {
            SteeringContext c = C; c.Own(actor);
            if (!c.Target || c.Target.IsDead()) throw new PreviewAction();
            c.TargetField(actor, c.Target);
            c.StaticTargetField(actor, null);
            c.WriteAiFloat("m_updateTargetTimer", Math.Max(0f, c.ReadAiFloat("m_updateTargetTimer") - dt));
            c.WriteAiFloat("m_unableToAttackTargetTimer", c.ReadAiFloat("m_unableToAttackTargetTimer") - dt);
            canHear = c.Plan.Hear(actor, c.Target); canSee = c.Plan.See(actor, c.Target);
            c.WriteAiFloat("m_timeSinceSensedTargetCreature", canHear || canSee ? dt : c.ReadAiFloat("m_timeSinceSensedTargetCreature") + dt);
            c.WriteAiFloat("m_timeSinceAttacking", c.ReadAiFloat("m_timeSinceAttacking") + dt);
        }
        internal static ItemDrop.ItemData CurrentWeapon(Humanoid actor)
        {
            C.Own(actor); return C.Weapons.Current;
        }
        internal static bool EquipWeapon(Humanoid actor, ItemDrop.ItemData item, bool effects)
        { C.Own(actor); if (!C.Weapons.Items.Contains(item)) throw new PreviewAction(); C.Weapons.Current = item; return true; }
        internal static void UnequipWeapon(Humanoid actor, ItemDrop.ItemData item, bool effects)
        { C.Own(actor); if (ReferenceEquals(item, C.Weapons.Current)) C.Weapons.Current = C.Weapons.Fallback; }
        internal static List<ItemDrop.ItemData> InventoryWeapons(Inventory inventory)
        { if (!ReferenceEquals(inventory, C.Actor.GetInventory())) throw new PreviewAction(); return C.Weapons.Items; }
        internal static List<ItemDrop.ItemData> OptimalWeapons() { return C.Weapons.Optimal; }
        internal static List<ItemDrop.ItemData> OutOfRangeWeapons() { return C.Weapons.OutOfRange; }
        internal static List<ItemDrop.ItemData> AllWeapons() { return C.Weapons.All; }
        internal static bool InAttack(Character actor)
        { return ReferenceEquals(actor, C.Actor) ? C.ShadowInAttack : actor.InAttack(); }
        internal static bool HumanoidInAttack(Humanoid actor) { return InAttack(actor); }
        // Preserve the native attack gate and cooldown without starting a live
        // attack, animation, projectile or damage event.
        internal static bool IgnoreAttack(MonsterAI actor, Character target, bool friend)
        {
            SteeringContext c = C;
            c.Own(actor);
            if (c.VisualAttacks && (c.ShadowInAttack || c.ShadowAttackPending || !c.ShadowCanMove || c.ShadowStagger)) return false;
            ItemDrop.ItemData weapon = c.Weapons.Current;
            if (weapon == null || !c.Plan.CanAttack(actor, weapon)) return false;
            Attack attack = weapon.m_shared.m_attack;
            if (attack == null || String.IsNullOrEmpty(attack.m_attackAnimation)) return false;
            if (c.VisualAttacks && !haveAmmo(c.Actor, weapon)) return false;
            string trigger = attack.m_attackAnimation;
            if (attack.m_attackChainLevels > 1)
            {
                int chain = c.PreviousAttackBase == attack.m_attackAnimation && c.AttackAge <= .2f ? c.NextAttackChain : 0;
                if (chain >= attack.m_attackChainLevels) chain = 0;
                trigger += chain; c.NextAttackChain = (chain + 1) % attack.m_attackChainLevels;
            }
            else if (attack.m_attackRandomAnimations >= 2)
                trigger += UnityEngine.Random.Range(0, attack.m_attackRandomAnimations);
            c.Weapons.Attacked(c.NativeTime);
            c.ResetAttackAge();
            c.WriteAiFloat("m_timeSinceAttacking", 0f);
            c.AttackName = attack.m_attackAnimation;
            c.VisualAttack = attack; c.AttackTrigger = trigger; c.PreviousAttackBase = attack.m_attackAnimation;
            c.AttackHeld = true;
            c.ShadowInAttack = true;
            return true;
        }
        internal static bool RejectConsume(MonsterAI actor, Humanoid humanoid, float dt) { C.Own(actor); return false; }
        internal static bool RejectRiding(Sadle saddle, float dt) { throw new PreviewAction(); }
        internal static void RejectDespawn(BaseAI actor, float dt, bool run) { C.Own(actor); throw new PreviewAction(); }
        internal static void RejectJump(Character actor, bool force) { C.Own(actor); throw new PreviewAction(); }
        internal static void Alert(BaseAI actor, bool alert) { C.Own(actor); if (alert) C.WriteAiFloat("m_timeSinceSensedTargetCreature", 0f); if (actor.m_canBeAlerted) C.AlertField(actor, alert); }
        internal static void MonsterAlert(MonsterAI actor, bool alert) { C.Own(actor); if (alert) C.WriteAiFloat("m_timeSinceSensedTargetCreature", 0f); Alert(actor, alert); }
        internal static void IgnoreTargetInfo(BaseAI actor, ZDOID target) { C.Own(actor); }
        internal static void Hunt(BaseAI actor, bool hunt) { C.Own(actor); C.HuntField(actor, hunt); }
        internal static bool ReadHunt(BaseAI actor)
        {
            C.Own(actor); if (!C.HuntRead(actor)) return false;
            ZDO data = C.View.GetZDO();
            if (data.GetBool(ZDOVars.s_eventCreature, C.EventRead(C.Ai)) && !RandEventSystem.InEvent()) return false;
            return !data.GetBool(ZDOVars.s_despawnInDay, C.DayRead(C.Ai)) || !EnvMan.IsDay();
        }
        internal static bool ReadMonsterHunt(MonsterAI actor) { return ReadHunt(actor); }
        internal static void Friends(BaseAI actor, float range, out Character hurtFriend, out Character friend)
        { C.Own(actor); friend = FindFriend(range, false); hurtFriend = FindFriend(range, true); }
        internal static Character Friend(BaseAI actor, float range) { C.Own(actor); return FindFriend(range, false); }
        internal static Character HurtFriend(BaseAI actor, float range) { C.Own(actor); return FindFriend(range, true); }
        private static Character FindFriend(float range, bool hurt)
        {
            SteeringContext c = C;
            foreach (Character other in Character.GetAllCharacters())
            {
                if ((!hurt && other == c.Actor) || BaseAI.IsEnemy(c.Actor, other) ||
                    Vector3.Distance(ReferenceEquals(other, c.Actor) ? c.ShadowPosition : other.transform.position, c.ShadowPosition) > range) continue;
                if (!hurt || other.GetHealth() < other.GetMaxHealth()) return other;
            }
            return null;
        }
        internal static void Charge(BaseAI actor, string charge) { C.Own(actor); C.ChargeField(actor, charge); }
        internal static void Uncharge(BaseAI actor) { C.Own(actor); C.ChargeField(actor, null); }
        internal static void IgnoreTargeted(Character actor, bool heard, bool alerted) { }
        internal static bool ViewOwner(ZNetView view) { return ReferenceEquals(C.View, view) || view.IsOwner(); }
        internal static bool Tamed(Character actor)
        {
            // Admission already proved this actor untamed; the fixed target is a
            // Player. Do not repeat GetComponent/ZDO reads on the ordinary path.
            if (ReferenceEquals(actor, C.Actor) || actor is Player) return false;
            throw new PreviewAction();
        }
        internal static bool TamedAt(Character actor, float time) { return Tamed(actor); }
        internal static bool Aggravated(BaseAI actor) { ZNetView view = actor.GetComponent<ZNetView>(); return actor.m_aggravatable && view && view.IsValid() && view.GetZDO().GetBool(ZDOVars.s_aggravated, false); }
        internal static Vector3 Velocity(Character actor)
        {
            if (ReferenceEquals(actor, C.Actor)) return C.ShadowVelocity;
            return ReferenceEquals(actor, C.Target) && C.TargetVelocitySeeded ? C.TargetVelocity : actor.GetVelocity();
        }
        internal static bool Crouching(Character actor)
        {
            if (ReferenceEquals(actor, C.Target))
                return C.TargetAnimation && C.TargetAnimation.GetCurrentAnimatorStateInfo(0).tagHash == C.CrouchTag;
            C.Own(actor); return false; // admitted ordinary hostile Humanoid
        }
        internal static bool Swimming(Character actor)
        {
            return ReferenceEquals(actor, C.Actor) ? C.ShadowSwimming : actor.IsSwimming();
        }
        internal static bool Flying(Character actor)
        {
            return ReferenceEquals(actor, C.Actor) ? C.ShadowFlying : actor.IsFlying();
        }
        internal static Vector3 CenterPoint(Character actor)
        {
            // Disabled hit capsules have empty world bounds. Geometry remains valid.
            Vector3 center = actor.transform.TransformPoint(actor.GetCollider().center);
            if (ReferenceEquals(actor, C.Actor)) return C.FromRealRootPoint(center);
            if (!ReferenceEquals(actor, C.Target)) return center;
            if (C.TargetTransformSeeded)
                return actor.transform.position + C.TargetLead + C.TargetRotation * C.TargetCenterOffset;
            return center + C.TargetLead;
        }
        internal static Vector3 Position(Transform transform) { return C.ReadPosition(transform); }
        internal static Vector3 Forward(Transform transform)
        {
            if (ReferenceEquals(transform, C.Root)) return C.ShadowRotation * Vector3.forward;
            return ReferenceEquals(transform, C.TargetRoot) && C.TargetTransformSeeded ? C.TargetRotation * Vector3.forward : transform.forward;
        }
        internal static Vector3 Right(Transform transform)
        {
            if (ReferenceEquals(transform, C.Root)) return C.ShadowRotation * Vector3.right;
            return ReferenceEquals(transform, C.TargetRoot) && C.TargetTransformSeeded ? C.TargetRotation * Vector3.right : transform.right;
        }
        internal static Vector3 Up(Transform transform)
        {
            if (ReferenceEquals(transform, C.Root)) return C.ShadowRotation * Vector3.up;
            if (ReferenceEquals(transform, C.Visual)) return C.ShadowRotation * Quaternion.Inverse(C.Root.rotation) * transform.up;
            return ReferenceEquals(transform, C.TargetRoot) && C.TargetTransformSeeded ? C.TargetRotation * Vector3.up : transform.up;
        }
        internal static Vector3 TransformPoint(Transform transform, Vector3 point)
        {
            Vector3 actual = transform.TransformPoint(point);
            if (ReferenceEquals(transform, C.Root)) return C.FromRealRootPoint(actual);
            if (ReferenceEquals(transform, C.TargetRoot) && C.TargetTransformSeeded)
                return C.TargetRoot.position + C.TargetLead + C.TargetRotation * point;
            return ReferenceEquals(transform, C.TargetRoot) ? actual + C.TargetLead : actual;
        }
        internal static RaycastHit[] RayBuffer() { return C.Hits; }
        internal static Collider[] ColliderBuffer() { return C.Colliders; }
        internal static void IgnoreLog(object message) { }
        internal static float NativeTime() { return C.NativeTime; }
        internal static float WeaponAttackTime(ItemDrop.ItemData weapon)
        {
            if (weapon == null) return 0f;
            return C.Weapons.LastAttack(weapon);
        }
        internal static float RandomValue()
        {
            float value;
            if (C.RandomReplayActive && C.RandomLedger != null && C.RandomLedger.TryTake(out value)) return value;
            return UnityEngine.Random.value;
        }
        internal static float RandomRangeFloat(float min, float max)
        {
            float value;
            if (C.RandomReplayActive && C.RandomLedger != null && C.RandomLedger.TryTake(out value)) return min + (max - min) * value;
            return UnityEngine.Random.Range(min, max);
        }
        internal static int RandomRangeInt(int min, int max)
        {
            if (max <= min) return min;
            float value;
            if (!C.RandomReplayActive || C.RandomLedger == null || !C.RandomLedger.TryTake(out value))
                return UnityEngine.Random.Range(min, max);
            return Math.Min(max - 1, min + (int)Math.Floor((max - min) * (double)value));
        }
    }
}

#endif
