#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TooManyLags;
using UnityEngine;

namespace NativeMobPreviewAudit
{
    // Shadow-only reads and writes for one audited native MonsterAI decision.
    internal sealed class SteeringContext
    {
        internal readonly MonsterAI Ai;
        internal readonly Humanoid Actor;
        internal readonly Character Target;
        internal readonly Transform Root, Eye, Visual, TargetRoot, TargetEye;
        internal readonly ZNetView View;
        internal readonly Animator TargetAnimation;
        internal readonly int CrouchTag;
        internal readonly NativeSteeringPlan Plan;
        internal readonly RaycastHit[] Hits = new RaycastHit[128];
        internal readonly Collider[] Colliders = new Collider[128];
        internal readonly Action<MonsterAI, Character> TargetField;
        internal readonly Action<MonsterAI, StaticTarget> StaticTargetField;
        internal readonly Action<BaseAI, bool> AlertField, HuntField;
        internal readonly Func<BaseAI, bool> HuntRead;
        internal readonly Func<MonsterAI, bool> EventRead, DayRead;
        internal readonly Action<BaseAI, string> ChargeField;
#pragma warning disable 0649
        private readonly Dictionary<string, Func<BaseAI, float>> baseRead = new Dictionary<string, Func<BaseAI, float>>();
        private readonly Dictionary<string, Action<BaseAI, float>> baseWrite = new Dictionary<string, Action<BaseAI, float>>();
        private readonly Dictionary<string, Func<MonsterAI, float>> aiRead = new Dictionary<string, Func<MonsterAI, float>>();
        private readonly Dictionary<string, Action<MonsterAI, float>> aiWrite = new Dictionary<string, Action<MonsterAI, float>>();
        private readonly Func<Humanoid, float> attackAgeRead;
        private readonly Action<Humanoid, float> attackAgeWrite;
        private readonly Func<BaseAI, int> solidRayMaskRead;
        internal Vector3 ShadowPosition, ShadowVelocity, TargetLead, TargetVelocity;
        internal Vector3 TargetCenterOffset;
        internal bool TargetVelocitySeeded, TargetTransformSeeded, ShadowFlying, ShadowSwimming;
        internal bool RandomReplayActive;
        internal bool AttackHeld;
        internal bool VisualAttacks;
        internal Attack VisualAttack;
        internal string AttackTrigger = "", PreviousAttackBase = "";
        internal int NextAttackChain;
        internal float PhysicalAttackAge;
        internal bool ShadowAttackPending, ShadowCanMove = true, ShadowStagger;
        internal bool ShadowInAttack;
        internal string AttackName = "";
        internal Quaternion TargetRotation;
        internal NativeRandomLedger RandomLedger;
        internal Quaternion ShadowRotation;
        internal float NativeTime;
        internal readonly MobBridgeWeaponState Weapons;
#pragma warning restore 0649

        internal SteeringContext(NativeSteeringPlan plan, MonsterAI ai, Humanoid actor, Character target, ZNetView view,
            MobBridgeWeaponState weapons = null)
        {
            Plan = plan; Ai = ai; Actor = actor; Target = target; View = view;
            Root = actor.transform; Eye = actor.m_eye; TargetRoot = target.transform; TargetEye = target.m_eye;
            TargetAnimation = FieldAccess.Reader<Character, Animator>(AccessTools.Field(typeof(Character), "m_animator"))(target);
            CrouchTag = ZSyncAnimation.GetHash("crouch");
            Vector3 targetCenter = TargetRoot.TransformPoint(target.GetCollider().center);
            TargetCenterOffset = Quaternion.Inverse(TargetRoot.rotation) * (targetCenter - TargetRoot.position);
            FieldInfo attackAge = AccessTools.Field(typeof(Humanoid), "m_timeSinceLastAttack");
            if (attackAge == null) throw new NotSupportedException("Missing attack age state.");
            attackAgeRead = FieldAccess.Reader<Humanoid, float>(attackAge);
            attackAgeWrite = FieldAccess.Writer<Humanoid, float>(attackAge);
            Attack previous = FieldAccess.Reader<Humanoid, Attack>(AccessTools.Field(typeof(Humanoid), "m_currentAttack"))(actor) ??
                FieldAccess.Reader<Humanoid, Attack>(AccessTools.Field(typeof(Humanoid), "m_previousAttack"))(actor);
            if (previous != null)
            {
                PreviousAttackBase = previous.m_attackAnimation;
                NextAttackChain = FieldAccess.Reader<Attack, int>(AccessTools.Field(typeof(Attack), "m_nextAttackChainLevel"))(previous);
            }
            FieldInfo solidRayMask = AccessTools.Field(typeof(BaseAI), "m_solidRayMask");
            if (solidRayMask == null) throw new NotSupportedException("Missing solid ray mask.");
            solidRayMaskRead = FieldAccess.Reader<BaseAI, int>(solidRayMask);
            Weapons = weapons ?? new MobBridgeWeaponState(actor);
            GameObject visual = actor.GetVisual(); Visual = visual ? visual.transform : null;
            TargetField = FieldAccess.Writer<MonsterAI, Character>(AccessTools.Field(typeof(MonsterAI), "m_targetCreature"));
            StaticTargetField = FieldAccess.Writer<MonsterAI, StaticTarget>(AccessTools.Field(typeof(MonsterAI), "m_targetStatic"));
            AlertField = FieldAccess.Writer<BaseAI, bool>(AccessTools.Field(typeof(BaseAI), "m_alerted"));
            HuntField = FieldAccess.Writer<BaseAI, bool>(AccessTools.Field(typeof(BaseAI), "m_huntPlayer"));
            HuntRead = FieldAccess.Reader<BaseAI, bool>(AccessTools.Field(typeof(BaseAI), "m_huntPlayer"));
            EventRead = FieldAccess.Reader<MonsterAI, bool>(AccessTools.Field(typeof(MonsterAI), "m_eventCreature"));
            DayRead = FieldAccess.Reader<MonsterAI, bool>(AccessTools.Field(typeof(MonsterAI), "m_despawnInDay"));
            ChargeField = FieldAccess.Writer<BaseAI, string>(AccessTools.Field(typeof(BaseAI), "m_charging"));
            foreach (FieldInfo field in plan.AiFields)
            {
                if (field.FieldType != typeof(float)) continue;
                if (field.DeclaringType == typeof(BaseAI))
                {
                    baseRead.Add(field.Name, FieldAccess.Reader<BaseAI, float>(field));
                    baseWrite.Add(field.Name, FieldAccess.Writer<BaseAI, float>(field));
                }
                else
                {
                    aiRead.Add(field.Name, FieldAccess.Reader<MonsterAI, float>(field));
                    aiWrite.Add(field.Name, FieldAccess.Writer<MonsterAI, float>(field));
                }
            }
        }

        internal void Own(object actor)
        {
            if (!ReferenceEquals(actor, Ai) && !ReferenceEquals(actor, Actor))
                throw new InvalidOperationException("Foreign actor mutation denied.");
        }

        internal float ReadBaseFloat(string name) { return baseRead[name](Ai); }
        internal void WriteBaseFloat(string name, float value) { baseWrite[name](Ai, value); }
        internal float ReadAiFloat(string name) { return aiRead[name](Ai); }
        internal void WriteAiFloat(string name, float value) { aiWrite[name](Ai, value); }
        internal void AdvanceAttackAge(float dt)
        {
            float value = ShadowInAttack ? 0f : attackAgeRead(Actor) + dt;
            attackAgeWrite(Actor, !Single.IsNaN(value) && !Single.IsInfinity(value) ? Math.Max(0f, value) : 0f);
        }
        internal void ResetAttackAge() { attackAgeWrite(Actor, 0f); }
        internal void SetAttackAge(float value) { attackAgeWrite(Actor, Math.Max(0f, value)); }
        internal float AttackAge { get { return attackAgeRead(Actor); } }
        internal float Altitude()
        {
            RaycastHit hit;
            return Physics.Raycast(ShadowPosition, Vector3.down, out hit, Single.MaxValue,
                solidRayMaskRead(Ai)) ? ShadowPosition.y - hit.point.y : 1000f;
        }

        internal Vector3 ReadPosition(Transform transform)
        {
            if (ReferenceEquals(transform, Root)) return ShadowPosition;
            if (ReferenceEquals(transform, Eye)) return FromRealRootPoint(Eye.position);
            Vector3 position = transform.position;
            if (ReferenceEquals(transform, TargetRoot) || ReferenceEquals(transform, TargetEye)) position += TargetLead;
            return position;
        }

        internal Vector3 FromRealRootPoint(Vector3 point)
        {
            return ShadowPosition + ShadowRotation * Quaternion.Inverse(Root.rotation) * (point - Root.position);
        }
    }
}
#endif
