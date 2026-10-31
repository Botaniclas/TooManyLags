#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Threading;
using HarmonyLib;
using NativeMobPreviewAudit;
using UnityEngine;

namespace TooManyLags
{
    // Native MonsterAI decisions in an isolated transaction. This class owns
    // only decisions and control output; real gameplay remains at the current
    // owner and every live field is restored after each step.
    internal sealed class MobBridgeSimulation
    {
        private readonly NativeSteeringPlan plan;
        private readonly SteeringContext context;
        private readonly ShadowTransaction<MonsterAI, Humanoid, UnityEngine.Random.State> transaction;
        private readonly Func<Character, Vector3> move, look;
        private readonly Func<Character, Quaternion> yaw;
        private readonly Func<Character, bool> run;
        private readonly float gameTime;
        private readonly int thread;
        private readonly MobStatusState.Shadow status;
        private bool nativeInvoked;
        private Vector3 targetLead, targetVelocity;
        private Quaternion targetRotation;
        private bool targetSeeded;
        private double previousDecisionTime;
        private float physicalElapsed;

        internal bool AttackRequested { get { return context.AttackHeld; } }
        internal long DecisionTick { get { return transaction.Calls; } }
        internal string AttackName { get { return context.AttackName; } }
        internal Attack VisualAttack { get { return context.VisualAttack; } }
        internal string AttackTrigger { get { return context.AttackTrigger; } }
        internal int WeaponState { get { return context.Weapons.Current == null ? 0 : (int)context.Weapons.Current.m_shared.m_animationState; } }
        internal bool Alerted { get; private set; }

        internal MobBridgeSimulation(NativeSteeringPlan plan, MonsterAI ai, Character target,
            MobBridgeSeed seed, MobStatusState.Shadow status, double now, float simulationSeconds)
        {
            if (plan == null || seed == null || !ai || !target)
                throw new NotSupportedException("MonsterAI with a live Character target required.");
            Humanoid actor = ai.GetComponent<Humanoid>();
            ZNetView view = ai.GetComponent<ZNetView>();
            if (!actor || !view || !view.IsValid() || actor.GetType() != typeof(Humanoid) ||
                actor.IsDead() || actor.IsTamed() || actor.IsBoss() || actor.IsRiding() || actor.IsAttached() || seed.InAttack)
                throw new NotSupportedException("Actor state not suitable for bridge simulation.");
            this.plan = plan;
            this.status = status;
            gameTime = seed.GameTime;
            thread = Thread.CurrentThread.ManagedThreadId;
            context = new SteeringContext(plan, ai, actor, target, view, seed.Weapons);
            context.ShadowInAttack = seed.InAttack;
            context.VisualAttacks = true;
            context.ShadowFlying = actor.IsFlying();
            move = FieldAccess.Reader<Character, Vector3>(AccessTools.Field(typeof(Character), "m_moveDir"));
            look = FieldAccess.Reader<Character, Vector3>(AccessTools.Field(typeof(Character), "m_lookDir"));
            yaw = FieldAccess.Reader<Character, Quaternion>(AccessTools.Field(typeof(Character), "m_lookYaw"));
            run = FieldAccess.Reader<Character, bool>(AccessTools.Field(typeof(Character), "m_run"));
            SandboxScope scope = new SandboxScope(ai, actor, view, actor.GetZAnim());
            NativeCadence cadence = new NativeCadence(now, Math.Max(.06f, simulationSeconds), true);
            transaction = new ShadowTransaction<MonsterAI, Humanoid, UnityEngine.Random.State>(
                seed.Ai, seed.Controls, scope, cadence,
                delegate { return UnityEngine.Random.state; }, delegate(UnityEngine.Random.State value) { UnityEngine.Random.state = value; },
                Ownership, Step, Output, seed.RandomState);
        }

        internal StepResult TryStep(double now, float realStartOffset, Vector3 position, Quaternion rotation,
            Vector3 velocity, bool swimming)
        {
            if (thread != Thread.CurrentThread.ManagedThreadId) return new StepResult { Status = StepStatus.WrongThread };
            if (NativeSteeringAdapters.Current != null) return new StepResult { Status = StepStatus.NestedScope };
            context.AttackHeld = false;
            // Destination rebases captured timestamp ages at simulation zero.
            // Adding network delay here aged only the fake cooldowns a second time.
            context.NativeTime = gameTime + (float)now;
            physicalElapsed = (float)Math.Max(0d, now - previousDecisionTime);
            previousDecisionTime = now;
            context.ShadowPosition = position;
            context.ShadowRotation = rotation;
            context.ShadowVelocity = velocity;
            context.ShadowSwimming = swimming;
            nativeInvoked = false;
            StepResult result = transaction.TryStep(now);
            result.NativeRan = nativeInvoked;
            return result;
        }

        internal void SetTargetInput(Vector3 lead, Vector3 velocity, Quaternion rotation)
        {
            targetLead = Finite(lead) ? lead : new Vector3();
            targetVelocity = Finite(velocity) ? velocity : new Vector3();
            targetRotation = IsFinite(rotation) ? rotation : Quaternion.identity;
            targetSeeded = true;
        }
        internal void SetAttackState(bool inAttack, bool pending, bool canMove, bool stagger, bool flying, float attackAge)
        {
            context.ShadowInAttack = inAttack; context.ShadowAttackPending = pending;
            context.ShadowCanMove = canMove; context.ShadowStagger = stagger;
            context.ShadowFlying = flying; context.PhysicalAttackAge = attackAge;
        }

        private OwnerStamp Ownership()
        {
            ZDO data = context.View && context.View.IsValid() ? context.View.GetZDO() : null;
            if (data == null || !data.IsValid()) return new OwnerStamp(0, UInt32.MaxValue);
            return new OwnerStamp(data.GetOwner(), data.OwnerRevision);
        }

        private bool Step(float dt)
        {
            NativeSteeringAdapters.Current = context;
            bool statusEntered = status != null;
            try
            {
                if (statusEntered) status.Enter();
                // Humanoid attack age advances with body time, not AI pass dt.
                // Under CPU load one 50ms AI pass can span more physical time.
                if (context.VisualAttacks) context.SetAttackAge(context.PhysicalAttackAge);
                else context.AdvanceAttackAge(physicalElapsed);
                Character target = context.Target;
                if (!target || target.IsDead() || context.Actor.IsDead() ||
                    context.Actor.IsRiding() || context.Actor.IsAttached()) return false;
                context.TargetVelocity = targetSeeded ? targetVelocity : target.GetVelocity();
                if (!Finite(context.TargetVelocity)) context.TargetVelocity = new Vector3();
                context.TargetLead = targetSeeded ? targetLead : new Vector3();
                context.TargetRotation = targetSeeded ? targetRotation : target.transform.rotation;
                context.TargetTransformSeeded = targetSeeded;
                context.TargetVelocitySeeded = targetSeeded;
                nativeInvoked = true;
                return plan.Step(context.Ai, dt);
            }
            catch (PreviewAction)
            {
                SandboxScope.BlockOwnAction(context.Ai, PreviewSignal.UnsupportedActionRequested);
                return false;
            }
            finally
            {
                if (statusEntered) status.Exit();
                NativeSteeringAdapters.Current = null;
            }
        }

        private SteeringFrame Output()
        {
            Alerted = context.Ai.IsAlerted();
            SteeringFrame frame = new SteeringFrame
            {
                MoveDirection = move(context.Actor),
                Run = run(context.Actor),
                Walk = context.Actor.GetWalk(),
                Flying = context.ShadowFlying,
                LookDirection = look(context.Actor),
                LookYaw = yaw(context.Actor)
            };
            if (context.AttackHeld && !context.VisualAttacks) NativeSteeringPolicy.HoldAttack(ref frame);
            return frame;
        }

        private static bool Finite(Vector3 value)
        {
            return MobBridgeTimeline.Finite(value.x) && MobBridgeTimeline.Finite(value.y) &&
                MobBridgeTimeline.Finite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return MobBridgeTimeline.Finite(value.x) && MobBridgeTimeline.Finite(value.y) &&
                MobBridgeTimeline.Finite(value.z) && MobBridgeTimeline.Finite(value.w);
        }
    }
}
#endif
