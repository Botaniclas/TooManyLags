#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TooManyLags
{
    // Transform-only private rig: no CharacterAnimEvent, equipment, network,
    // colliders, renderers or copied gameplay MonoBehaviours. A manual graph
    // advances during capture; render only samples immutable bone poses.
    internal sealed class MobBridgeAnimation : IDisposable
    {
        private readonly Transform[] visible, shadow;
#if TML_BRIDGE_DIAGNOSTICS
        private readonly Transform rootBone;
#endif
        private readonly bool receiving;
        private readonly int[] receivedBones;
        private readonly Dictionary<Transform, int> poseIndices = new Dictionary<Transform, int>();
        private Matrix4x4 modelMatrix;
        private Quaternion modelRotation;
        private float realWeight;
        internal bool Blending { get { return realWeight > 0f; } }
        private readonly Vector3[] positions, scales, savedPositions, savedScales;
        private readonly Quaternion[] rotations, savedRotations;
        private readonly Dictionary<int, AnimatorControllerParameterType> parameters = new Dictionary<int, AnimatorControllerParameterType>();
        private readonly Dictionary<RandomIdle, float> idlePhases = new Dictionary<RandomIdle, float>();
        [ThreadStatic] internal static MobBridgeAnimation Current;
        private UnityEngine.Random.State random;
        internal bool Alerted;
        internal bool Initialising = true;
        private PlayableGraph graph;
        private AnimatorControllerPlayable controller;
        private readonly Animator sourceAnimator;
        private readonly int attackTag;
        private bool disposed, applied, pending, entered;
        private float simulation, speed = 1f;
        private Attack attack;
        private bool stopped, flying;
        internal readonly Animator Animator;
        internal Transform VisualRoot { get { return shadow[0]; } }
        internal int BoneCount { get { return visible.Length; } }
        internal int TransitionLayers { get; private set; }
        internal float InitialRootGap { get; private set; }
#if TML_BRIDGE_DIAGNOSTICS
        internal Quaternion VisibleRootRotation { get { return rootBone ? rootBone.rotation : visible[0].rotation; } }
        internal Quaternion RootRotation
        {
            get
            {
                Vector3 position, scale; Quaternion rotation;
                return TryPose(rootBone ? rootBone : visible[0], out position, out rotation, out scale) ? rotation : modelRotation;
            }
        }
#endif
        internal readonly MobBridgeAnimationHistory History;
        internal bool InAttack { get; private set; }
        internal bool Pending { get { return pending; } }
        internal bool JumpRequested;
        internal float AttackAge { get; private set; }
        internal int CurrentTag { get { return controller.GetCurrentAnimatorStateInfo(0).tagHash; } }
        internal int NextTag { get { return controller.IsInTransition(0) ? controller.GetNextAnimatorStateInfo(0).tagHash : 0; } }
        internal bool? FlightEvent;
        internal Vector3 RootMotion;
        internal float MoveFactor { get { return attack == null || !InAttack ? 1f : stopped ? 0f : attack.m_speedFactor; } }
        internal float TurnFactor { get { return attack == null || !InAttack ? 1f : stopped ? 0f : attack.m_speedFactorRotation; } }
        private int visualTrigger;

        internal MobBridgeAnimation(Character actor, Transform parent)
        {
            Animator source = actor.GetComponentInChildren<Animator>();
            sourceAnimator = source;
            Transform visual = actor.GetVisual().transform;
            if (!source || !source.runtimeAnimatorController)
                throw new NotSupportedException("Visual attack preview requires an Animator controller.");
            foreach (StateMachineBehaviour behaviour in source.GetBehaviours<StateMachineBehaviour>())
                if (behaviour.GetType() != typeof(RandomIdle) && behaviour.GetType() != typeof(AnimSetTrigger) && behaviour.GetType() != typeof(StateController))
                    throw new NotSupportedException("Unreviewed visual animation behaviour: " + behaviour.GetType().Name);
            Dictionary<Transform, Transform> map = new Dictionary<Transform, Transform>();
            List<Transform> actual = new List<Transform>(), copies = new List<Transform>();
            CopyRig(visual, parent, map, actual, copies);
            visible = actual.ToArray(); shadow = copies.ToArray();
            for (int i = 0; i < visible.Length; ++i) poseIndices.Add(visible[i], i);
#if TML_BRIDGE_DIAGNOSTICS
            foreach (SkinnedMeshRenderer renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.rootBone && map.ContainsKey(renderer.rootBone)) { rootBone = renderer.rootBone; break; }
#endif
            positions = new Vector3[visible.Length]; rotations = new Quaternion[visible.Length]; scales = new Vector3[visible.Length];
            savedPositions = new Vector3[visible.Length]; savedRotations = new Quaternion[visible.Length]; savedScales = new Vector3[visible.Length];
            History = new MobBridgeAnimationHistory(visible.Length);
            attackTag = UnityEngine.Animator.StringToHash("attack");
            AttackAge = actor.GetTimeSinceLastAttack(); flying = actor.IsFlying();
            BaseAI ai = actor.GetComponent<BaseAI>(); Alerted = ai && ai.IsAlerted(); random = UnityEngine.Random.state;
            MobBridgeAnimation previous = Current;
            UnityEngine.Random.State sourceRandom = UnityEngine.Random.state;
            Current = this;
            try
            {
                Animator = map[source.transform].gameObject.AddComponent<Animator>();
                Animator.avatar = source.avatar; Animator.applyRootMotion = true;
                Animator.fireEvents = true; Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Animator.logWarnings = false;
                Animator.gameObject.AddComponent<MobBridgeAnimationRoot>().Recording = this;
                graph = PlayableGraph.Create("TooManyLags_VisualAttack"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                controller = AnimatorControllerPlayable.Create(graph, source.runtimeAnimatorController);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Recording", Animator);
                output.SetSourcePlayable(controller);
                foreach (AnimatorControllerParameter parameter in source.parameters)
                {
                    parameters.Add(parameter.nameHash, parameter.type);
                    if (parameter.type == AnimatorControllerParameterType.Float) controller.SetFloat(parameter.nameHash, source.GetFloat(parameter.nameHash));
                    else if (parameter.type == AnimatorControllerParameterType.Bool) controller.SetBool(parameter.nameHash, source.GetBool(parameter.nameHash));
                    else if (parameter.type == AnimatorControllerParameterType.Int) controller.SetInteger(parameter.nameHash, source.GetInteger(parameter.nameHash));
                }
                for (int i = 0; i < source.layerCount; ++i)
                {
#if TML_BRIDGE_DIAGNOSTICS
                    if (source.IsInTransition(i)) ++TransitionLayers;
#endif
                    AnimatorStateInfo state = source.GetCurrentAnimatorStateInfo(i);
                    controller.SetLayerWeight(i, source.GetLayerWeight(i));
                    if (state.fullPathHash != 0) controller.Play(state.fullPathHash, i, state.normalizedTime);
                }
                speed = source.speed; controller.SetSpeed(speed);
                graph.Play(); Evaluate(0f); Initialising = false;
#if TML_BRIDGE_DIAGNOSTICS
                if (rootBone) InitialRootGap = Quaternion.Angle(rootBone.localRotation, map[rootBone].localRotation);
#endif
                ReadPose(visible); History.Add(0f, positions, rotations, scales);
            }
            catch { Dispose(); throw; }
            finally { Current = previous; UnityEngine.Random.state = sourceRandom; }
        }

        // Read-only observer rig adapter: references existing transforms only.
        // No private world, Rigidbody, Animator, graph, AI or event receiver.
        internal MobBridgeAnimation(Character actor, string[] paths, MobBridgeObserverPacket.Sample initial)
        {
            receiving = true;
            sourceAnimator = null;
            Transform visual = actor.GetVisual().transform;
            Transform[] local = visual.GetComponentsInChildren<Transform>(true);
            int[] sourceToLocal = MapRig(paths, RigPaths(local));
            var captured = new List<Transform>();
            receivedBones = new int[paths.Length];
            for (int i = 0; i < paths.Length; ++i)
            {
                int index = sourceToLocal[i]; receivedBones[i] = index < 0 ? -1 : captured.Count;
                if (index >= 0) captured.Add(local[index]);
            }
            // Extra local attachments never enter skeletal playback/restore.
            // Their native transforms remain live even during a fake attack.
            visible = captured.ToArray(); shadow = visible;
            for (int i = 0; i < visible.Length; ++i) poseIndices.Add(visible[i], i);
#if TML_BRIDGE_DIAGNOSTICS
            foreach (SkinnedMeshRenderer renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.rootBone && poseIndices.ContainsKey(renderer.rootBone)) { rootBone = renderer.rootBone; break; }
#endif
            positions = new Vector3[visible.Length]; rotations = new Quaternion[visible.Length]; scales = new Vector3[visible.Length];
            savedPositions = new Vector3[visible.Length]; savedRotations = new Quaternion[visible.Length]; savedScales = new Vector3[visible.Length];
            History = new MobBridgeAnimationHistory(visible.Length);
            ReadReceivedPose(initial);
            History.Add(0f, positions, rotations, scales, initial.Attack);
        }
        private static int[] MapRig(string[] source, string[] local)
        {
            var found = new Dictionary<string, int>();
            for (int i = 0; i < local.Length; ++i) found.Add(local[i], i);
            var result = new int[source.Length];
            for (int i = 0; i < source.Length; ++i)
            { int index; result[i] = found.TryGetValue(source[i], out index) ? index : -1; }
            return result;
        }
        internal string[] RigPaths() { return RigPaths(visible); }
        private static string[] RigPaths(Transform[] bones)
        {
            var paths = new string[bones.Length];
            var known = new Dictionary<Transform, string>();
            var segments = new Dictionary<Transform, KeyValuePair<string, int>>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            paths[0] = ""; known.Add(bones[0], "");
            for (int i = 1; i < bones.Length; ++i)
            {
                Transform bone = bones[i]; KeyValuePair<string, int> segment;
                if (!segments.TryGetValue(bone, out segment))
                {
                    // One scan per current parent, rather than rescanning the
                    // preceding siblings for each bone. Nothing survives this
                    // call: equipment/FX changes are reflected next time.
                    Transform parent = bone.parent;
                    int children = parent.childCount;
                    if (children == 1) segment = new KeyValuePair<string, int>(bone.name, 0);
                    else
                    {
                        counts.Clear();
                        for (int child = 0; child < children; ++child)
                        {
                            Transform sibling = parent.GetChild(child); string name = sibling.name;
                            int ordinal; counts.TryGetValue(name, out ordinal);
                            segments.Add(sibling, new KeyValuePair<string, int>(name, ordinal));
                            counts[name] = ordinal + 1;
                        }
                        segment = segments[bone];
                    }
                }
                // Length prefixes distinguish duplicate names and literal path
                // separators; unrelated FX/equipment siblings do not shift IDs.
                paths[i] = known[bone.parent] + segment.Key.Length + ":" + segment.Key + ":" + segment.Value + "/";
                known.Add(bone, paths[i]);
            }
            return paths;
        }
        internal void Receive(MobBridgeObserverPacket.Sample sample)
        {
            if (!receiving || disposed || sample.Time <= History.End) return;
            if (sample.Positions != null && sample.Positions.Length == receivedBones.Length)
            {
                ReadReceivedPose(sample);
                History.Add(sample.Time, positions, rotations, scales, sample.Attack);
            }
            else History.AddRoot(sample.Time, sample.ModelPosition, sample.ModelRotation);
        }
        private void ReadReceivedPose(MobBridgeObserverPacket.Sample sample)
        {
            if (sample.Positions != null && sample.Positions.Length == receivedBones.Length)
                for (int i = 0; i < receivedBones.Length; ++i)
                {
                    int index = receivedBones[i]; if (index < 0) continue;
                    positions[index] = sample.Positions[i]; rotations[index] = sample.Rotations[i]; scales[index] = sample.Scales[i];
                }
            else ReadPose(visible);
            positions[0] = sample.ModelPosition; rotations[0] = sample.ModelRotation;
        }

        private static void CopyRig(Transform source, Transform parent, Dictionary<Transform, Transform> map,
            List<Transform> actual, List<Transform> copies)
        {
            GameObject item = new GameObject(source.name); Transform copy = item.transform;
            copy.SetParent(parent, false); copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation; copy.localScale = source.localScale;
            map.Add(source, copy); actual.Add(source); copies.Add(copy);
            for (int i = 0; i < source.childCount; ++i) CopyRig(source.GetChild(i), copy, map, actual, copies);
        }
        private bool Has(int hash, AnimatorControllerParameterType type)
        { AnimatorControllerParameterType found; return parameters.TryGetValue(hash, out found) && found == type; }
        internal void SetFloat(int hash, float value)
        { if (Has(hash, AnimatorControllerParameterType.Float)) controller.SetFloat(hash, value); }
        internal void SetBool(int hash, bool value)
        {
            if (hash == UnityEngine.Animator.StringToHash("flying")) flying = value;
            if (Has(hash, AnimatorControllerParameterType.Bool)) controller.SetBool(hash, value);
        }
        internal void Trigger(string name)
        { if (parameters.Count == 0) return; int hash = UnityEngine.Animator.StringToHash(name); if (Has(hash, AnimatorControllerParameterType.Trigger)) controller.SetTrigger(hash); }

        internal void Start(Attack template, string trigger, int weaponState, float time)
        {
            Advance(time, false);
            RootMotion = Vector3.zero;
            attack = template; stopped = false; entered = false;
            AttackAge = 0f;
            SetFloat(UnityEngine.Animator.StringToHash("statef"), weaponState);
            int stateHash = UnityEngine.Animator.StringToHash("statei");
            if (Has(stateHash, AnimatorControllerParameterType.Int)) controller.SetInteger(stateHash, weaponState);
            int hash = UnityEngine.Animator.StringToHash(trigger);
            pending = Has(hash, AnimatorControllerParameterType.Trigger);
            if (pending)
            {
                controller.SetTrigger(hash); Evaluate(0f);
                InAttack = CurrentTag == attackTag || NextTag == attackTag;
                entered = InAttack; pending = !InAttack;
            }
            else Plugin.Warn("Mob bridge visual attack trigger unavailable; trigger=" + trigger);
            Record(time);
        }

        // Visual-only attack playback. This runs on the existing private graph
        // at normal animation speed and does not write into the immutable
        // simulation recording.
        internal float BeginVisualAttack(Attack template, string trigger, int weaponState, float time)
        {
            Advance(time, false);
            RootMotion = Vector3.zero;
            attack = template; stopped = false; entered = false;
            AttackAge = 0f;
            SetFloat(UnityEngine.Animator.StringToHash("statef"), weaponState);
            int stateHash = UnityEngine.Animator.StringToHash("statei");
            if (Has(stateHash, AnimatorControllerParameterType.Int)) controller.SetInteger(stateHash, weaponState);
            visualTrigger = UnityEngine.Animator.StringToHash(trigger);
            pending = Has(visualTrigger, AnimatorControllerParameterType.Trigger);
            int attackHash = UnityEngine.Animator.StringToHash(template == null ? "" : template.m_attackAnimation);
            AnimatorStateInfo active;
            if (TryAttackState(sourceAnimator, attackHash, out active))
            {
                // The real attack started while fake playback was waiting.
                // Join it at the same state/time instead of firing trigger again.
                controller.Play(active.fullPathHash, 0, active.normalizedTime);
                speed = Math.Max(.01f, sourceAnimator.speed);
                controller.SetSpeed(speed);
                entered = InAttack = true;
                pending = false;
                return Remaining(active);
            }
            if (pending && !entered &&
                TryAttackState(controller, attackHash, out active))
            {
                // The graph was primed from an attack that began at the source.
                // Keep its phase; retriggering here would visibly restart it.
                entered = InAttack = true;
                pending = false;
                return Remaining(active);
            }
            if (pending)
            {
                controller.SetTrigger(visualTrigger); Evaluate(0f);
                InAttack = CurrentTag == attackTag || NextTag == attackTag;
                entered = InAttack;
                pending = !InAttack;
            }
            AnimatorStateInfo state = controller.GetCurrentAnimatorStateInfo(0);
            if (state.tagHash != attackTag && controller.IsInTransition(0))
                state = controller.GetNextAnimatorStateInfo(0);
            float length = state.length / Math.Max(.01f, speed);
            return length > .001f ? length : .001f;
        }

        private bool TryAttackState(Animator animator, int attackHash, out AnimatorStateInfo state)
        {
            state = animator ? animator.GetCurrentAnimatorStateInfo(0) : default(AnimatorStateInfo);
            if (animator && state.tagHash != attackTag && animator.IsInTransition(0))
                state = animator.GetNextAnimatorStateInfo(0);
            return animator && state.normalizedTime < 1f && state.tagHash == attackTag &&
                (attackHash == 0 || state.shortNameHash == attackHash);
        }

        private bool TryAttackState(AnimatorControllerPlayable playable, int attackHash, out AnimatorStateInfo state)
        {
            state = playable.GetCurrentAnimatorStateInfo(0);
            if (state.tagHash != attackTag && playable.IsInTransition(0))
                state = playable.GetNextAnimatorStateInfo(0);
            return state.normalizedTime < 1f && state.tagHash == attackTag &&
                (attackHash == 0 || state.shortNameHash == attackHash);
        }

        private float Remaining(AnimatorStateInfo state)
        {
            float phase = state.normalizedTime % 1f;
            if (phase < 0f) phase += 1f;
            float remaining = state.length * (1f - phase) / Math.Max(.01f, speed);
            return remaining > .001f ? remaining : .001f;
        }

        internal void AdvanceVisual(float dt)
        {
            if (disposed || dt <= 0f) return;
            Evaluate(dt);
            AnimatorStateInfo state = controller.GetCurrentAnimatorStateInfo(0);
            bool tagged = state.tagHash == attackTag || NextTag == attackTag;
            AttackAge = tagged ? 0f : AttackAge + dt;
            InAttack = tagged;
            if (!tagged && !pending && entered) entered = false;
            if (!InAttack && speed != 1f) { speed = 1f; controller.SetSpeed(1d); }
        }

        internal void EndVisualAttack()
        {
            if (visualTrigger != 0) controller.ResetTrigger(visualTrigger);
            attack = null; pending = entered = false; InAttack = false; stopped = false;
            visualTrigger = 0;
            if (speed != 1f) { speed = 1f; controller.SetSpeed(1d); }
        }

        internal void PrepareLive(Vector3 modelPosition, Quaternion rotation)
        {
            ReadPose(shadow);
            realWeight = 0f;
            modelRotation = rotation;
            Transform visual = visible[0], parent = visual.parent;
            modelMatrix = parent ? parent.localToWorldMatrix * Matrix4x4.TRS(parent.InverseTransformPoint(modelPosition),
                Quaternion.Inverse(parent.rotation) * rotation, visual.localScale) :
                Matrix4x4.TRS(modelPosition, rotation, visual.localScale);
        }

        internal void Advance(float time, bool record)
        {
            if (disposed) throw new ObjectDisposedException("visual attack recording");
            float dt = Math.Max(0f, time - simulation);
            if (dt > 0f)
            {
                AttackAge = InAttack ? 0f : AttackAge + dt;
                Evaluate(dt); simulation = time;
                AnimatorStateInfo state = controller.GetCurrentAnimatorStateInfo(0);
                bool tagged = state.tagHash == attackTag || NextTag == attackTag;
                if (tagged || pending) RootMotion += Animator.deltaPosition;
                if (tagged) { entered = true; pending = false; }
                if (entered && !tagged) { entered = pending = false; attack = null; stopped = false; }
                InAttack = tagged;
                if (!InAttack && speed != 1f) { speed = 1f; controller.SetSpeed(1d); }
            }
            if (record) Record(time);
        }

        private void Evaluate(float dt)
        {
            UnityEngine.Random.State saved = UnityEngine.Random.state;
            MobBridgeAnimation previous = Current;
            Current = this;
            try { UnityEngine.Random.state = random; graph.Evaluate(dt); }
            finally
            {
                try { random = UnityEngine.Random.state; }
                finally { Current = previous; UnityEngine.Random.state = saved; }
            }
        }
        internal static bool Own(Animator animator) { return Current != null && ReferenceEquals(Current.Animator, animator); }
        internal void BehaviourTrigger(string name, bool enable)
        {
            if (Initialising || String.IsNullOrEmpty(name)) return;
            int hash = UnityEngine.Animator.StringToHash(name);
            if (!Has(hash, AnimatorControllerParameterType.Trigger)) return;
            if (enable) controller.SetTrigger(hash); else controller.ResetTrigger(hash);
        }
        internal void Idle(RandomIdle behaviour, AnimatorStateInfo state, bool enter)
        {
            float phase = state.normalizedTime % 1f, last;
            if (!Initialising && (enter || !idlePhases.TryGetValue(behaviour, out last) || phase < last))
                SetFloat(UnityEngine.Animator.StringToHash(behaviour.m_valueName), Alerted && behaviour.m_alertedIdle >= 0 ?
                    behaviour.m_alertedIdle : UnityEngine.Random.Range(0, behaviour.m_animations));
            idlePhases[behaviour] = phase;
        }
        internal void VisualSpeed(float value) { speed = value; controller.SetSpeed(speed); }
        internal void VisualStop() { stopped = true; }
        internal void VisualFlight(bool value)
        { if (flying != value) Trigger(value ? "fly_takeoff" : "fly_land"); FlightEvent = flying = value; }
        internal void VisualHit() { if (attack != null && attack.m_toggleFlying) VisualFlight(!flying); }
        private void Record(float time)
        {
            if (time <= History.End) return;
            ReadPose(shadow); History.Add(time, positions, rotations, scales, InAttack || pending);
        }
        private void ReadPose(Transform[] rig)
        {
            for (int i = 0; i < rig.Length; ++i)
            { positions[i] = rig[i].localPosition; rotations[i] = rig[i].localRotation; scales[i] = rig[i].localScale; }
        }
        internal void Prepare(float time, float realWeight, Vector3 modelPosition, Quaternion rotation)
        {
            if (applied) throw new InvalidOperationException("Cannot prepare a pose over an applied rig.");
            // Recorded bones have zero weight during native locomotion. Keep
            // root placement and the late native camera sample, without
            // interpolating an entire skeleton that will be discarded.
            if (realWeight < 1f) History.Sample(time, positions, rotations, scales);
            this.realWeight = realWeight;
            modelRotation = rotation;
            Transform visual = visible[0], parent = visual.parent;
            modelMatrix = parent ? parent.localToWorldMatrix * Matrix4x4.TRS(parent.InverseTransformPoint(modelPosition),
                Quaternion.Inverse(parent.rotation) * rotation, visual.localScale) : Matrix4x4.TRS(modelPosition, rotation, visual.localScale);
            // Keep the recorded sample immutable. Native animation/IK may still
            // advance after this early HUD/physics preparation. Blend those
            // live bones only at the late camera boundary, using this same
            // already-captured weight/time rather than a second clock sample.
        }

        internal bool TryPose(Transform source, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = scale = Vector3.zero; rotation = Quaternion.identity;
            if (!source || !source.IsChildOf(visible[0])) return false;
            Matrix4x4 local = Matrix4x4.identity;
            // Only requested hitbox/root chains are composed. No full second
            // rig traversal, Transform writes or temporary GameObjects.
            for (Transform bone = source; bone != visible[0]; bone = bone.parent)
            {
                int index;
                bool known = poseIndices.TryGetValue(bone, out index);
                Vector3 p, s; Quaternion q;
                if (known && realWeight >= 1f)
                {
                    // Apply's snapshot must remain authoritative after native
                    // IK; early queries still use the live native transforms.
                    p = applied ? savedPositions[index] : bone.localPosition;
                    q = applied ? savedRotations[index] : bone.localRotation;
                    s = applied ? savedScales[index] : bone.localScale;
                }
                else
                {
                    p = known ? positions[index] : bone.localPosition;
                    q = known ? rotations[index] : bone.localRotation;
                    s = known ? scales[index] : bone.localScale;
                    if (known && applied && realWeight > 0f)
                    {
                        // Use the same late native sample as Apply, not another
                        // read of potentially overwritten live transforms.
                        p = p * (1f - realWeight) + savedPositions[index] * realWeight;
                        q = Quaternion.Slerp(q, savedRotations[index], realWeight);
                        s = s * (1f - realWeight) + savedScales[index] * realWeight;
                    }
                    else if (known && !applied && realWeight > 0f)
                    {
                        // Early gameplay queries use currently available native
                        // poses. Render refreshes blended hitboxes after animation
                        // finalization, without evaluating simulation again.
                        p = p * (1f - realWeight) + bone.localPosition * realWeight;
                        q = Quaternion.Slerp(q, bone.localRotation, realWeight);
                        s = s * (1f - realWeight) + bone.localScale * realWeight;
                    }
                }
                local = Matrix4x4.TRS(p, q, s) * local; rotation = q * rotation;
            }
            Matrix4x4 world = modelMatrix * local;
            position = world.MultiplyPoint3x4(Vector3.zero);
            scale = world.lossyScale; rotation = modelRotation * rotation;
            return true;
        }
        internal void Apply()
        {
            if (applied) return;
            applied = true;
            modelMatrix = visible[0].localToWorldMatrix; modelRotation = visible[0].rotation;
            for (int i = 1; i < visible.Length; ++i)
            {
                Transform bone = visible[i]; if (!bone) continue;
                savedPositions[i] = bone.localPosition; savedRotations[i] = bone.localRotation; savedScales[i] = bone.localScale;
                // Full native locomotion is a true passthrough. Keep the late
                // sample for hitbox queries, without rewriting native bones.
                if (realWeight >= 1f) continue;
                if (realWeight > 0f)
                {
                    bone.localPosition = positions[i] * (1f - realWeight) + savedPositions[i] * realWeight;
                    bone.localRotation = Quaternion.Slerp(rotations[i], savedRotations[i], realWeight);
                    bone.localScale = scales[i] * (1f - realWeight) + savedScales[i] * realWeight;
                }
                else
                { bone.localPosition = positions[i]; bone.localRotation = rotations[i]; bone.localScale = scales[i]; }
            }
        }
        internal void Restore()
        {
            if (!applied) return;
            for (int i = 1; realWeight < 1f && i < visible.Length; ++i)
                if (visible[i]) { visible[i].localPosition = savedPositions[i]; visible[i].localRotation = savedRotations[i]; visible[i].localScale = savedScales[i]; }
            applied = false;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            History.Close(); if (!receiving && graph.IsValid()) graph.Destroy();
        }
    }

    // Native clip events resolve only to this private receiver. No vanilla
    // CharacterAnimEvent exists here, so Hit cannot call real attack code.
    internal sealed class MobBridgeAnimationRoot : MonoBehaviour
    {
        internal MobBridgeAnimation Recording;
        private void OnAnimatorMove() { }
        private void Speed(float value) { if (Recording != null) Recording.VisualSpeed(value); }
        private void Stop(AnimationEvent value) { if (Recording != null) Recording.VisualStop(); }
        private void Jump() { if (Recording != null) Recording.JumpRequested = true; }
        private void TakeOff() { if (Recording != null) Recording.VisualFlight(true); }
        private void Land() { if (Recording != null) Recording.VisualFlight(false); }
        private void Hit() { if (Recording != null) Recording.VisualHit(); }
        private void OnAttackTrigger() { if (Recording != null) Recording.VisualHit(); }
        private void FootStep(AnimationEvent value) { }
        private void TrailOn() { }
        private void TrailOff() { }
        private void Chain() { }
        private void ResetChain() { }
        private void FreezeFrame(float value) { }
        private void DodgeMortal() { }
        private void GPower() { }
        private void Die() { }
    }
}
#endif
