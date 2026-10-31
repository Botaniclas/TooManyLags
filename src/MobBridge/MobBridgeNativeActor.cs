#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NativeMobPreviewAudit;
using UnityEngine;

namespace TooManyLags
{
    // Native methods execute against the captured actor fields and a private
    // Rigidbody. Source fields, root pose and callbacks restore before returning
    // to Unity; neither a second network actor nor duplicate AI registration exists.
    internal sealed class MobBridgeNativeActor : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly GameObject[] noEffects = new GameObject[0];
        private static Harmony hooks;
        private static int sessions;
        private static bool sessionHooks;
        private static FieldInfo[] stateFields, delegateFields;
        [ThreadStatic] private static MobBridgeNativeActor current;
        private static readonly Action<Character, float> motion = Bind("UpdateMotion");
        private static readonly Action<Character, float> water = Bind("UpdateWater");
        private static readonly Action<Character, float> tilt = Bind("UpdateGroundTilt");
        private static readonly Action<Character, float> ground = Bind("UpdateGroundContact");
        private static readonly Action<Character, Collision> collision =
            (Action<Character, Collision>)Delegate.CreateDelegate(typeof(Action<Character, Collision>), AccessTools.Method(typeof(Character), "OnCollisionStay"));
        private static readonly Action<Character> depth =
            (Action<Character>)Delegate.CreateDelegate(typeof(Action<Character>), AccessTools.Method(typeof(Character), "CalculateLiquidDepth"));
        private static readonly Action<Character> layer =
            (Action<Character>)Delegate.CreateDelegate(typeof(Action<Character>), AccessTools.Method(typeof(Character), "UpdateLayer"));
        private static readonly Action<Character, float> lookTransition = Bind("UpdateLookTransition");
        private static readonly Func<Character, bool> swimDepth =
            (Func<Character, bool>)Delegate.CreateDelegate(typeof(Func<Character, bool>), AccessTools.Method(typeof(Character), "InLiquidSwimDepth", Type.EmptyTypes));
        private static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>();
        // These delegates bind once, then access exactly the same native fields
        // without reflection, string lookup or boxing during motor ticks.
        private static readonly Action<Humanoid, bool> writeWalk = FieldAccess.Writer<Humanoid, bool>(Field("m_walk"));
        private static readonly Action<Humanoid, bool> writeFlying = FieldAccess.Writer<Humanoid, bool>(Field("m_flying"));
        private static readonly Action<Humanoid, Vector3> writeLook = FieldAccess.Writer<Humanoid, Vector3>(Field("m_lookDir"));
        private static readonly Action<Humanoid, Quaternion> writeYaw = FieldAccess.Writer<Humanoid, Quaternion>(Field("m_lookYaw"));
        private static readonly Action<Humanoid, int> writeAnimFrame = FieldAccess.Writer<Humanoid, int>(Field("m_cachedAnimHashFrame"));
        private static readonly Func<Humanoid, float> readHitTime = FieldAccess.Reader<Humanoid, float>(Field("m_hitWorldTime"));
        private static readonly Action<Humanoid, float> writeHitTime = FieldAccess.Writer<Humanoid, float>(Field("m_hitWorldTime"));
        private static readonly Func<Humanoid, float> readGroundTimer = FieldAccess.Reader<Humanoid, float>(Field("m_groundForceTimer"));
        private static readonly Action<Humanoid, float> writeGroundTimer = FieldAccess.Writer<Humanoid, float>(Field("m_groundForceTimer"));
        private static readonly Func<Humanoid, Vector3> readVelocity = FieldAccess.Reader<Humanoid, Vector3>(Field("m_currentVel"));
        private static readonly Action<Humanoid, int> writeCurrentHash = FieldAccess.Writer<Humanoid, int>(Field("m_cachedCurrentAnimHash"));
        private static readonly Action<Humanoid, int> writeNextHash = FieldAccess.Writer<Humanoid, int>(Field("m_cachedNextAnimHash"));
        private static readonly Action<Humanoid, int> writeAnyHash = FieldAccess.Writer<Humanoid, int>(Field("m_cachedNextOrCurrentAnimHash"));
        private readonly ShadowSnapshot<Humanoid> state;
        private readonly MobBridgeNativeWorld world;
        private readonly GameObject root;
        private readonly Transform eye;
        private readonly Rigidbody originalBody;
        private readonly PhysicsMaterial material;
        private readonly ICallback[] callbacks;
        private readonly MobStatusState.Shadow status;
        private bool disposed;
        private bool desiredFlight;
        internal readonly Humanoid Actor;
        internal readonly Rigidbody Body;
        internal readonly ZNetView View;
        private readonly ZSyncAnimation animation;
        internal readonly MobBridgeAnimation Animation;
        internal bool Swimming, CanMove, Flying, Staggering;
        internal Vector3 ControlVelocity;
        internal string ContactName = "none";

        private static Action<Character, float> Bind(string name)
        { return (Action<Character, float>)Delegate.CreateDelegate(typeof(Action<Character, float>), AccessTools.Method(typeof(Character), name)); }
        private static FieldInfo Field(string name)
        {
            FieldInfo field;
            if (!fields.TryGetValue(name, out field))
            { field = AccessTools.Field(typeof(Humanoid), name); if (field == null) throw new NotSupportedException("Missing native motion field: " + name); fields.Add(name, field); }
            return field;
        }
        private static FieldInfo[] StateFields()
        {
            if (stateFields != null) return stateFields;
            List<FieldInfo> result = new List<FieldInfo>();
            for (Type type = typeof(Humanoid); type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(Flags))
                {
                    Type value = field.FieldType;
                    if (!field.IsStatic && !field.IsInitOnly && (value.IsPrimitive || value.IsEnum || value == typeof(string) ||
                        value == typeof(Vector3) || value == typeof(Quaternion) || typeof(UnityEngine.Object).IsAssignableFrom(value))) result.Add(field);
                }
            return stateFields = result.ToArray();
        }
        internal static FieldInfo[] WarmFields() { return StateFields(); }
        internal static void WarmField(FieldInfo field)
        {
            FieldAccess.Prepare(typeof(Humanoid), field.FieldType, field);
            ShadowSnapshot<Humanoid>.PrepareType(field.FieldType);
        }
        private static FieldInfo[] DelegateFields()
        {
            if (delegateFields != null) return delegateFields;
            List<FieldInfo> result = new List<FieldInfo>();
            for (Type type = typeof(Humanoid); type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(Flags))
                    if (!field.IsStatic && typeof(Delegate).IsAssignableFrom(field.FieldType)) result.Add(field);
            return delegateFields = result.ToArray();
        }

        private interface ICallback
        {
            void Capture();
            void Clear();
            void Restore();
        }
        private sealed class Callback<V> : ICallback where V : class
        {
            private readonly Humanoid actor;
            private readonly Func<Humanoid, V> read;
            private readonly Action<Humanoid, V> write;
            private V saved;
            public Callback(Humanoid actor, FieldInfo field)
            {
                this.actor = actor;
                read = FieldAccess.Reader<Humanoid, V>(field);
                write = FieldAccess.Writer<Humanoid, V>(field);
            }
            public void Capture() { saved = read(actor); }
            public void Clear() { write(actor, null); }
            public void Restore() { write(actor, saved); }
        }
        private static ICallback[] CreateCallbacks(Humanoid actor)
        {
            FieldInfo[] fields = DelegateFields();
            ICallback[] result = new ICallback[fields.Length];
            for (int i = 0; i < fields.Length; ++i)
                result[i] = (ICallback)Activator.CreateInstance(typeof(Callback<>).MakeGenericType(fields[i].FieldType),
                    new object[] { actor, fields[i] });
            return result;
        }
        private void CaptureCallbacks() { foreach (ICallback callback in callbacks) callback.Capture(); }
        private void ClearCallbacks() { foreach (ICallback callback in callbacks) callback.Clear(); }
        private void RestoreCallbacks() { foreach (ICallback callback in callbacks) callback.Restore(); }

        internal MobBridgeNativeActor(Humanoid actor, Vector3 origin, Quaternion rotation, Vector3 velocity,
            float seconds, MobStatusState.Shadow status, MobBridgeCost cost)
        {
            EnsureHooks(); ++sessions;
            cost.Mark("nativeHooks");
            Actor = actor; View = actor.GetComponent<ZNetView>(); animation = actor.GetZAnim(); this.status = status;
            try
            {
                world = new MobBridgeNativeWorld(actor, origin, seconds, cost);
                root = new GameObject("TooManyLags_NativeRecording"); root.SetActive(false); world.AddRoot(root);
                root.layer = actor.gameObject.layer;
                root.transform.SetPositionAndRotation(origin, rotation); root.transform.localScale = actor.transform.lossyScale;
                Body = root.AddComponent<Rigidbody>(); CapsuleCollider shape = root.AddComponent<CapsuleCollider>();
                CapsuleCollider original = actor.GetCollider();
                shape.direction = original.direction; shape.center = original.center; shape.height = original.height; shape.radius = original.radius;
                shape.sharedMaterial = original.sharedMaterial; shape.contactOffset = original.contactOffset;
                material = shape.material;
                originalBody = actor.GetComponent<Rigidbody>();
                actor.CanMove(); // Populate native predicate caches before snapshotting.
                Body.mass = originalBody.mass; Body.linearDamping = originalBody.linearDamping; Body.angularDamping = originalBody.angularDamping;
                Body.constraints = originalBody.constraints; Body.collisionDetectionMode = originalBody.collisionDetectionMode;
                Body.maxAngularVelocity = originalBody.maxAngularVelocity; Body.maxDepenetrationVelocity = originalBody.maxDepenetrationVelocity;
                Body.solverIterations = originalBody.solverIterations; Body.solverVelocityIterations = originalBody.solverVelocityIterations;
                Body.useGravity = originalBody.useGravity; Body.isKinematic = false; Body.interpolation = RigidbodyInterpolation.None;
                if (actor.m_eye)
                {
                    GameObject item = new GameObject("RecordingEye"); eye = item.transform; eye.SetParent(root.transform, false);
                    eye.localPosition = actor.transform.InverseTransformPoint(actor.m_eye.position);
                    eye.localRotation = Quaternion.Inverse(actor.transform.rotation) * actor.m_eye.rotation;
                }
                MobBridgeNativeContact relay = root.AddComponent<MobBridgeNativeContact>(); relay.Recorder = this;
                root.SetActive(true); Body.position = origin; Body.rotation = rotation; Body.linearVelocity = velocity;
                cost.Mark("body");
                Animation = new MobBridgeAnimation(actor, root.transform);
                cost.Mark("rig"); cost.Count("bones", Animation.BoneCount);
                cost.Count("transitionLayers", Animation.TransitionLayers);
                cost.Count("initialRootGapDeg", (int)Math.Round(Animation.InitialRootGap));
                desiredFlight = Flying = actor.IsFlying();
                Staggering = actor.IsStaggering();
                state = ShadowSnapshot<Humanoid>.Create(actor, StateFields(), 0);
                cost.Mark("snapshotCreate");
                callbacks = CreateCallbacks(actor);
                cost.Mark("snapshotCallbacks");
                state.CaptureReal();
                try
                {
                    state.InstallShadow();
                    Field("m_body").SetValue(actor, Body); Field("m_collider").SetValue(actor, shape);
                    if (eye) Field("m_eye").SetValue(actor, eye);
                    Field("m_visual").SetValue(actor, Animation.VisualRoot.gameObject);
                    Field("m_animator").SetValue(actor, Animation.Animator);
                    Field("m_deepSnowWalkObj").SetValue(actor, null);
                    cost.Mark("snapshotInstall");
                    // No native motor reference may push a main-world platform.
                    foreach (string name in new[] { "m_lastGroundCollider", "m_lowestContactCollider" })
                        Field(name).SetValue(actor, world.Mirror((Collider)Field(name).GetValue(actor)));
                    Collider groundShape = (Collider)Field("m_lastGroundCollider").GetValue(actor);
                    Field("m_lastGroundBody").SetValue(actor, groundShape ? groundShape.attachedRigidbody : null);
                    Field("m_lastAttachBody").SetValue(actor, null);
                    cost.Mark("snapshotGround");
                    state.CaptureShadow();
                }
                finally { state.RestoreReal(); }
                cost.Mark("snapshot");
            }
            catch { Dispose(); throw; }
        }

        internal void Step(SteeringFrame command, float time, float dt, bool controller, Character target, Vector3 lead, Quaternion targetRotation)
        {
            if (disposed) throw new ObjectDisposedException("native recording");
            if (current != null) throw new InvalidOperationException("Nested native motion recording.");
            world.Refresh(target.transform.position + lead, targetRotation);
            Transform sourceRoot = Actor.transform;
            Vector3 sourcePosition = sourceRoot.position, bodyPosition = originalBody.position;
            Quaternion sourceRotation = sourceRoot.rotation, bodyRotation = originalBody.rotation;
            int sourceLayer = sourceRoot.gameObject.layer;
            Vector3 sourceVelocity = originalBody.linearVelocity, sourceAngular = originalBody.angularVelocity;
            UnityEngine.Random.State sourceRandom = UnityEngine.Random.state;
            bool entered = false;
            state.CaptureReal();
            CaptureCallbacks();
            current = this;
            try
            {
                state.InstallShadow();
                ClearCallbacks();
                sourceRoot.SetPositionAndRotation(Body.position, Body.rotation);
                Actor.SetMoveDir(command.MoveDirection); Actor.SetRun(command.Run);
                writeWalk(Actor, command.Walk);
                if (command.Flying != desiredFlight)
                {
                    writeFlying(Actor, command.Flying); desiredFlight = command.Flying;
                    Animation.Trigger(command.Flying ? "fly_takeoff" : "fly_land");
                }
                writeLook(Actor, command.LookDirection); writeYaw(Actor, command.LookYaw);
                writeAnimFrame(Actor, -1);
                Actor.SetLiquidLevel(Floating.GetLiquidLevel(Body.position, 1f, LiquidType.Water), LiquidType.Water, null);
                Actor.SetLiquidLevel(Floating.GetLiquidLevel(Body.position, 1f, LiquidType.Tar), LiquidType.Tar, null);
                if (status != null) { status.Enter(); entered = true; }
                ApplyAnimationEvents();
                Actor.AddRootMotion(Animation.RootMotion); Animation.RootMotion = Vector3.zero;
                if (controller)
                {
                    depth(Actor); layer(Actor); sourceRoot.gameObject.layer = Body.gameObject.layer;
                    water(Actor, dt); tilt(Actor, dt); lookTransition(Actor, dt);
                    Animation.SetFloat(Animator.StringToHash("tilt"), Vector3.Dot(Animation.VisualRoot.forward, Vector3.up));
                    writeHitTime(Actor, readHitTime(Actor) + dt);
                    ground(Actor, dt); motion(Actor, dt);
                    // Native UpdateRotation writes Character.transform, whereas
                    // native forces write m_body. Keep those same two views in
                    // sync before integrating the private Rigidbody.
                    Body.rotation = sourceRoot.rotation;
                    float groundTimer = readGroundTimer(Actor);
                    if (groundTimer > 0f) writeGroundTimer(Actor, groundTimer - dt);
                }
                world.Simulate(dt);
                Animation.Advance(time, true);
                ApplyAnimationEvents();
                Actor.AddRootMotion(Animation.RootMotion); Animation.RootMotion = Vector3.zero;
                writeAnimFrame(Actor, -1);
                Swimming = Actor.IsSwimming(); CanMove = Actor.CanMove();
                Flying = Actor.IsFlying();
                Staggering = Actor.IsStaggering();
                ControlVelocity = readVelocity(Actor);
                if (entered) status.RetainCurrent();
                state.CaptureShadow();
            }
            finally
            {
                try { if (entered) status.Exit(); }
                finally
                {
                    try { state.RestoreReal(); }
                    finally
                    {
                        try
                        {
                            RestoreCallbacks();
                        }
                        finally
                        {
                            try
                            {
                                if (originalBody)
                                {
                                    originalBody.position = bodyPosition; originalBody.rotation = bodyRotation;
                                    if (!originalBody.isKinematic)
                                    { originalBody.linearVelocity = sourceVelocity; originalBody.angularVelocity = sourceAngular; }
                                }
                            }
                            finally
                            {
                                try
                                {
                                    if (sourceRoot)
                                        try { sourceRoot.SetPositionAndRotation(sourcePosition, sourceRotation); }
                                        finally { sourceRoot.gameObject.layer = sourceLayer; }
                                }
                                finally { current = null; UnityEngine.Random.state = sourceRandom; }
                            }
                        }
                    }
                }
            }
            if (status != null) status.Advance(dt);
        }

        internal void Contact(Collision value)
        {
            if (!ReferenceEquals(current, this)) return;
            Actor.transform.SetPositionAndRotation(Body.position, Body.rotation);
            ContactName = value.collider ? value.collider.name : "none";
            collision(Actor, value);
        }
        private void ApplyAnimationEvents()
        {
            if (Animation.FlightEvent.HasValue)
            { writeFlying(Actor, Animation.FlightEvent.Value); Animation.FlightEvent = null; }
            if (Animation.JumpRequested)
            {
                Animation.JumpRequested = false;
                // Native Jump's unrelated fallback can pull the local player's
                // grapple. Admit only its native ground/water jump branch.
                if (Actor.IsOnGround() || swimDepth(Actor) && readHitTime(Actor) < .25f)
                    Actor.Jump(true);
            }
        }
        internal void Observe(Character target) { world.Observe(target); }
        private static bool Own(ZNetView view) { return current != null && ReferenceEquals(view, current.View); }
        private static bool BeforeOwner(ZNetView __instance, ref bool __result)
        { if (!Own(__instance)) return true; __result = true; return false; }
        private static bool BeforeRpc(ZNetView __instance) { return !Own(__instance); }
        private static bool BeforeDataWrite() { return current == null; }
        private static bool BeforeAnimation(ZSyncAnimation __instance)
        { return current == null || !ReferenceEquals(__instance, current.animation); }
        private static bool BeforeOwnAction(Character __instance)
        { return current == null || !ReferenceEquals(__instance, current.Actor); }
        private static bool BeforeAttack(Character __instance, ref bool __result)
        { if (current == null || !ReferenceEquals(__instance, current.Actor)) return true; __result = current.Animation != null && current.Animation.InAttack; return false; }
        private static bool BeforeMoveFactor(Humanoid __instance, ref float __result)
        {
            if (current == null || !ReferenceEquals(__instance, current.Actor)) return true;
            __result = current.Animation != null && (__instance.IsFlying() || __instance.IsOnGround()) ? current.Animation.MoveFactor : 1f;
            return false;
        }
        private static bool BeforeTurnFactor(Humanoid __instance, ref float __result)
        { if (current == null || !ReferenceEquals(__instance, current.Actor)) return true; __result = current.Animation == null ? 1f : current.Animation.TurnFactor; return false; }
        private static bool BeforeAnimHashes(Character __instance)
        {
            if (current == null || !ReferenceEquals(__instance, current.Actor) || current.Animation == null) return true;
            int next = current.Animation.NextTag, active = current.Animation.CurrentTag;
            writeCurrentHash((Humanoid)__instance, active);
            writeNextHash((Humanoid)__instance, next);
            writeAnyHash((Humanoid)__instance, next == 0 ? active : next);
            writeAnimFrame((Humanoid)__instance, MonoUpdaters.UpdateCount);
            return false;
        }
        private static bool BeforeFloat(ZSyncAnimation __instance, int hash, float value)
        { if (BeforeAnimation(__instance)) return true; if (current.Animation != null) current.Animation.SetFloat(hash, value); return false; }
        private static bool BeforeFloatName(ZSyncAnimation __instance, string name, float value)
        { return BeforeAnimation(__instance) || BeforeFloat(__instance, Animator.StringToHash(name), value); }
        private static bool BeforeBool(ZSyncAnimation __instance, int hash, bool value)
        { if (BeforeAnimation(__instance)) return true; if (current.Animation != null) current.Animation.SetBool(hash, value); return false; }
        private static bool BeforeBoolName(ZSyncAnimation __instance, string name, bool value)
        { return BeforeAnimation(__instance) || BeforeBool(__instance, Animator.StringToHash(name), value); }
        private static bool BeforeTrigger(ZSyncAnimation __instance, string name)
        { if (BeforeAnimation(__instance)) return true; if (current.Animation != null) current.Animation.Trigger(name); return false; }
        private static bool BeforeBehaviourEnter(AnimSetTrigger __instance, Animator __0)
        { if (!MobBridgeAnimation.Own(__0)) return true; MobBridgeAnimation.Current.BehaviourTrigger(__instance.TriggerOnEnter, __instance.TriggerOnEnterEnable); return false; }
        private static bool BeforeBehaviourExit(AnimSetTrigger __instance, Animator __0)
        { if (!MobBridgeAnimation.Own(__0)) return true; MobBridgeAnimation.Current.BehaviourTrigger(__instance.TriggerOnExit, __instance.TriggerOnExitEnable); return false; }
        private static bool BeforeIdleEnter(RandomIdle __instance, Animator __0, AnimatorStateInfo __1)
        { if (!MobBridgeAnimation.Own(__0)) return true; MobBridgeAnimation.Current.Idle(__instance, __1, true); return false; }
        private static bool BeforeIdleUpdate(RandomIdle __instance, Animator __0, AnimatorStateInfo __1)
        { if (!MobBridgeAnimation.Own(__0)) return true; MobBridgeAnimation.Current.Idle(__instance, __1, false); return false; }
        private static bool BeforeStateController(Animator __0) { return !MobBridgeAnimation.Own(__0); }
        private static bool BeforeEffects(ref GameObject[] __result)
        { if (current == null) return true; __result = noEffects; return false; }

        private static void EnsureHooks()
        {
            if (hooks != null) return;
            Harmony candidate = new Harmony(Plugin.Id + ".native-recording");
            try
            {
                candidate.Patch(AccessTools.Method(typeof(ZNetView), "IsOwner"), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeOwner") { priority = Priority.First });
                foreach (MethodInfo method in typeof(ZDO).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (!method.ContainsGenericParameters && (method.Name == "Set" || method.Name == "SetOwner" || method.Name == "SetPosition" || method.Name == "SetRotation"))
                        candidate.Patch(method, prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeDataWrite") { priority = Priority.First });
                foreach (MethodInfo method in typeof(ZNetView).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (method.Name == "InvokeRPC") candidate.Patch(method, prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeRpc") { priority = Priority.First });
                foreach (MethodInfo method in typeof(ZSyncAnimation).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (method.Name == "SetFloat" || method.Name == "SetBool" || method.Name == "SetTrigger")
                    {
                        string hook = method.Name == "SetTrigger" ? "BeforeTrigger" :
                            (method.Name == "SetFloat" ? "BeforeFloat" : "BeforeBool") + (method.GetParameters()[0].ParameterType == typeof(string) ? "Name" : "");
                        candidate.Patch(method, prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), hook) { priority = Priority.First });
                    }
                candidate.Patch(AccessTools.Method(typeof(Character), "Damage", new[] { typeof(HitData) }), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeOwnAction") { priority = Priority.First });
                candidate.Patch(AccessTools.Method(typeof(Character), "AddPushbackForce"), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeOwnAction"));
                candidate.Patch(AccessTools.Method(typeof(Humanoid), "InAttack"), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeAttack") { priority = Priority.First });
                candidate.Patch(AccessTools.Method(typeof(Humanoid), "GetAttackSpeedFactorMovement"), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeMoveFactor"));
                candidate.Patch(AccessTools.Method(typeof(Humanoid), "GetAttackSpeedFactorRotation"), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeTurnFactor"));
                candidate.Patch(AccessTools.Method(typeof(Character), "UpdateCachedAnimHashes"), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeAnimHashes"));
                Type[] stateArguments = { typeof(Animator), typeof(AnimatorStateInfo), typeof(int) };
                candidate.Patch(AccessTools.DeclaredMethod(typeof(AnimSetTrigger), "OnStateEnter", stateArguments), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeBehaviourEnter"));
                candidate.Patch(AccessTools.DeclaredMethod(typeof(AnimSetTrigger), "OnStateExit", stateArguments), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeBehaviourExit"));
                candidate.Patch(AccessTools.DeclaredMethod(typeof(RandomIdle), "OnStateEnter", stateArguments), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeIdleEnter"));
                candidate.Patch(AccessTools.DeclaredMethod(typeof(RandomIdle), "OnStateUpdate", stateArguments), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeIdleUpdate"));
                candidate.Patch(AccessTools.DeclaredMethod(typeof(StateController), "OnStateEnter", stateArguments), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeStateController"));
                candidate.Patch(AccessTools.Method(typeof(EffectList), "Create"), prefix: new HarmonyMethod(typeof(MobBridgeNativeActor), "BeforeEffects"));
                hooks = candidate;
            }
            catch { candidate.UnpatchSelf(); throw; }
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (Animation != null) Animation.Dispose();
            if (root) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            if (material) UnityEngine.Object.Destroy(material);
            if (world != null) world.Dispose();
            --sessions;
            ReleaseHooks();
        }
        internal static void PrepareSession() { EnsureHooks(); sessionHooks = true; }
        internal static void EndSession() { sessionHooks = false; ReleaseHooks(); }
        private static void ReleaseHooks()
        { if (!sessionHooks && sessions == 0 && hooks != null) { hooks.UnpatchSelf(); hooks = null; } }
    }

    internal sealed class MobBridgeNativeContact : MonoBehaviour
    {
        internal MobBridgeNativeActor Recorder;
        private void OnCollisionEnter(Collision value) { if (Recorder != null) Recorder.Contact(value); }
        private void OnCollisionStay(Collision value) { if (Recorder != null) Recorder.Contact(value); }
    }
}
#endif
