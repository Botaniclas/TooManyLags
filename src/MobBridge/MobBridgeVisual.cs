#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using HarmonyLib;
using NativeMobPreviewAudit;
using UnityEngine;

namespace TooManyLags
{
    // Shared old-owner/observer presentation. The actor root stays replicated by
    // game; the visual subtree follows the isolated bridge pose until takeover.
    internal static class MobBridgeVisual
    {
        private const int Limit = 16;
        private sealed class OwnershipBlend
        {
            internal ZSyncTransform Sync;
            internal bool Waiting;
            internal Vector3 ModelPosition;
            internal Quaternion ModelRotation;
        }
        private sealed class AttackEvent
        {
            internal float Time, Length;
            internal Attack Template;
            internal string Trigger, Name;
            internal int WeaponState;
            internal bool Started, Cancelled, Checked;
            internal double StartedAt, LastVisualAt;
        }
        private sealed class OwnerGain
        {
            internal ZDOID Id; internal Character Actor; internal Transform Visual;
            internal double Started; internal float Duration;
            internal MobBridgeTakeover Motion;
            internal Vector3 PresentedLocalPosition; internal Quaternion PresentedLocalRotation;
            internal Vector3 SavedLocalPosition; internal Quaternion SavedLocalRotation;
            internal int PresentationFrame = -1; internal bool Applied;
        }
        private sealed class Track
        {
            internal ZDO Data; internal ZNetView View; internal Character Actor;
            internal Transform Visual; internal long Destination; internal uint Revision;
            internal MobBridgeTimeline Timeline; internal MobBridgeSimulation Simulation;
            internal MobBridgeBody Body; internal MobBridgeTakeover Takeover;
            internal List<AttackEvent> Attacks;
            internal AttackEvent VisualAttack;
            internal uint CorrectionRevision;
            internal OwnershipBlend Ownership;
            internal MobBridgeAnimation Animation;
            internal bool Observer;
            internal double InputAt;
            internal int StreamCursor;
            internal MobBridgeTargetTimeline TargetTimeline;
            internal MobBridgeHit Hit; internal MobStatusState.Shadow Status;
            internal MobBridgeSkin Skin;
            internal MobBridgeEffects Effects;
            internal MobBridgePlacement Placement;
            internal int PhysicsLayer;
            internal MobBridgeRateTracker Rate, AiRate;
            internal SteeringFrame Command; internal double Started, Last, LastSimulation;
            internal MobBridgeCaptureClock CaptureClock;
#if TML_BRIDGE_DIAGNOSTICS
            internal long CaptureTicks, MaxCaptureTicks, AiTicks, MotorTicks;
            internal int Decisions, MotorSteps;
            internal bool RotationSample;
            internal Quaternion LastRootRotation, LastBoneRotation;
            internal float MaxRootStep, MaxBoneStep;
            internal float MaxDrawModelGap, MaxDrawBoneGap;
            internal float MaxApplyBoneGap;
            internal int DrawSamples;
            internal float MaxDivergence;
            internal int MotionSamples;
            internal int StarvedFrames, EndpointFrames, BufferSlowFrames;
            internal float MinPlaybackRate;
            internal float MaxPlaybackRateStep, MaxFrameDelta;
            internal Vector3 EntryVelocity, FirstMotorPosition, SecondMotorPosition;
            internal float FirstMotorTime, SecondMotorTime;
            internal bool EntryLogged;
#endif
            internal float Simulated, Latency, DestinationInterval, PhysicsInterval;
            internal float RecordingWindow, FirstArrival, MatchingHorizon, OwnerStep;
            internal MobBridgeTiming Timing;
            internal float PlannedDelay;
            internal bool CaptureAttack;
            internal Vector3 Origin;
            internal uint Token;
            internal bool ClockSeen; internal int PresentationFrame = -1;
            internal long TakeoverStartTick = -1L;
            internal float DestinationSimulation;
            internal readonly MobBridgeFrame DestinationFrame = new MobBridgeFrame();
            internal float? Frozen;
#if TML_BRIDGE_DIAGNOSTICS
            internal string ExpectedAttack = "";
#endif
            internal Vector3 PresentedPosition; internal Quaternion PresentedRotation;
            internal Vector3 InheritedOffset; internal Quaternion InheritedRotation;
            internal Vector3 InheritedModelOffset, JoinModelPosition;
            internal Quaternion InheritedModelRotation, JoinModelRotation;
            internal float AnimationJoinTime;
            internal double DestinationSampleWall;
            internal MobBridgeRecording History;
            internal Vector3 LocalPosition; internal Quaternion LocalRotation;
            internal Vector3 SavedPosition; internal Quaternion SavedRotation; internal bool Applied;
        }
        private static readonly List<Track> tracks = new List<Track>(Limit);
        private static readonly List<OwnerGain> ownerGains = new List<OwnerGain>(Limit);
        private static readonly List<Camera> rendering = new List<Camera>(4);
        private static Harmony presentationHooks;
        private static bool sessionHooks;
        private static Dictionary<ZSyncTransform, Track> ownershipSync;
        private static Func<ZSyncTransform, bool> syncWasOwner;
        [ThreadStatic] private static int hudAnchorDepth;
        private static int forwardSpeedHash, sidewaySpeedHash, onGroundHash, flyingHash, inWaterHash;
        private static bool animationHashesReady;

        internal static bool Contains(ZDOID id, uint revision, uint token)
        {
            return tracks.Exists(delegate(Track item) { return !item.Observer && item.Data != null && item.Data.m_uid == id &&
                item.Revision == revision && item.Token == token; });
        }
        private static bool AtCapacity(bool observer)
        {
            int count = 0;
            foreach (Track item in tracks) if (item.Observer == observer && ++count >= Limit) return true;
            return false;
        }
        internal static int DebugPhase(ZDO data)
        {
            foreach (Track item in tracks)
                if (ReferenceEquals(item.Data, data)) return item.Takeover == null ? 2 : 3;
            return -1;
        }
        internal static void Cancel(ZDOID id)
        {
            Restore();
            for (int i = tracks.Count - 1; i >= 0; --i)
                if (tracks[i].Data.m_uid == id) RemoveAt(i);
            CancelOwnerGain(id);
        }

        internal static void PrimeOwnerAnimation(Humanoid actor)
        {
            if (!actor || !actor.transform) return;
            ZSyncAnimation animation = actor.GetZAnim();
            if (animation)
            {
                if (!animationHashesReady)
                {
                    forwardSpeedHash = Animator.StringToHash("forward_speed");
                    sidewaySpeedHash = Animator.StringToHash("sideway_speed");
                    onGroundHash = Animator.StringToHash("onGround");
                    flyingHash = Animator.StringToHash("flying");
                    inWaterHash = Animator.StringToHash("inWater");
                    animationHashesReady = true;
                }
                Vector3 velocity = actor.GetVelocity();
                Vector3 forward = actor.transform.forward, right = actor.transform.right;
                float forwardSpeed = Vector3.Dot(velocity, forward);
                float sidewaySpeed = Vector3.Dot(velocity, right);
                if (animation.HasParameter("forward_speed", AnimatorControllerParameterType.Float))
                    animation.SetFloat(forwardSpeedHash, forwardSpeed);
                if (animation.HasParameter("sideway_speed", AnimatorControllerParameterType.Float))
                    animation.SetFloat(sidewaySpeedHash, sidewaySpeed);
                if (animation.HasParameter("onGround", AnimatorControllerParameterType.Bool))
                    animation.SetBool(onGroundHash, actor.IsOnGround());
                if (animation.HasParameter("flying", AnimatorControllerParameterType.Bool))
                    animation.SetBool(flyingHash, actor.IsFlying());
                if (animation.HasParameter("inWater", AnimatorControllerParameterType.Bool))
                    animation.SetBool(inWaterHash, actor.IsSwimming());
            }
            // The replicated animation tag cache belongs to the old owner.
            // Force the first local AI/decision read to inspect the restored
            // native animator instead of serving the idle replica hash.
            AccessTools.Field(typeof(Character), "m_cachedAnimHashFrame").SetValue(actor, -1);
        }

        internal static void BeginOwnerGain(ZDOID id, Character actor, Vector3 priorPosition,
            Quaternion priorRotation, Vector3 priorVelocity, Vector3 velocity, float minimumSeconds)
        {
            if (id.IsNone() || !actor || ZNet.instance == null || ZNet.instance.IsServer() || Application.isBatchMode) return;
            for (int i = 0; i < tracks.Count; ++i)
                if (tracks[i].Data != null && tracks[i].Data.m_uid == id) return;
            GameObject model = actor.GetVisual();
            Transform visual = model ? model.transform : null;
            if (!visual || visual == actor.transform || !visual.IsChildOf(actor.transform)) return;
            Restore();
            CancelOwnerGain(id);
            Vector3 nativePosition = visual.position;
            Quaternion nativeRotation = visual.rotation;
            Vector3 offset = priorPosition - nativePosition;
            float angle = Quaternion.Angle(priorRotation, nativeRotation);
            if (offset.sqrMagnitude <= .000001f && angle <= .1f) return;
            float speed = Math.Max(.1f, Math.Max(actor.m_speed,
                Math.Max(priorVelocity.magnitude, velocity.magnitude)));
            float duration = Math.Max(Math.Max(minimumSeconds, RenderFrameSeconds()),
                Math.Max(1.5f * offset.magnitude / speed, 1.5f * angle / Math.Max(30f, actor.m_turnSpeed)));
            duration = Math.Min(.5f, duration);
            double now = Time.realtimeSinceStartupAsDouble;
            ownerGains.Add(new OwnerGain
            {
                Id = id, Actor = actor, Visual = visual, Started = now, Duration = duration,
                Motion = new MobBridgeTakeover(now, duration, priorPosition, priorRotation,
                    nativePosition, nativeRotation, velocity, priorVelocity)
            });
            OwnerGain gain = ownerGains[ownerGains.Count - 1];
            gain.SavedLocalPosition = visual.localPosition;
            gain.SavedLocalRotation = visual.localRotation;
            ComputeOwnerGainPose(gain, now);
            visual.localPosition = gain.PresentedLocalPosition;
            visual.localRotation = gain.PresentedLocalRotation;
            gain.Applied = true;
            if (tracks.Count == 0 && ownerGains.Count == 1)
            { Camera.onPreCull += BeforeRender; Camera.onPreRender += BeforeDraw; Camera.onPostRender += AfterRender; }
            Plugin.TestLog("bridge owner gain; mob=" + id + "; offset=" + Number(offset.magnitude) +
                "; offsetY=" + Number(offset.y) + "; yaw=" + Number(angle) +
                "; priorSpeed=" + Number(priorVelocity.magnitude) + "; targetSpeed=" + Number(velocity.magnitude) +
                "; duration=" + Number(duration));
        }

        internal static void CancelOwnerGain(ZDOID id)
        {
            for (int i = ownerGains.Count - 1; i >= 0; --i)
            {
                OwnerGain gain = ownerGains[i];
                if (gain.Id != id) continue;
                RestoreOwnerGain(gain);
                ownerGains.RemoveAt(i);
            }
            if (tracks.Count == 0 && ownerGains.Count == 0) Unsubscribe();
        }

        internal static void CancelOwnerGains()
        {
            Restore();
            for (int i = 0; i < ownerGains.Count; ++i) RestoreOwnerGain(ownerGains[i]);
            ownerGains.Clear();
            if (tracks.Count == 0) Unsubscribe();
        }

        private static void Finish(Track item, bool final)
        {
            if (item.Observer) MobBridgeObserver.Finish(item.Data.m_uid, item.Revision, item.Token, final);
            else MobBridgeProtocol.Finish(item.Data.m_uid, item.Destination, item.Revision, item.Token, final);
        }
        private static void StreamRecording(Track item, bool closed)
        {
            if (item.Observer || !MobBridgeObserver.Publishing(item.Data.m_uid, item.Revision, item.Token)) return;
            try { MobBridgeObserver.Publish(item.Data, item.History, item.Animation, ref item.StreamCursor, closed); }
            catch (Exception error) { Plugin.Warn("Observer recording send retained local playback: " + error.GetBaseException().Message); }
        }

        internal static bool StartObserver(ZDO data, MobBridgeObserverPacket descriptor,
            IList<MobBridgeObserverPacket.Sample> samples, bool closed)
        {
            if (samples.Count < 2 || data == null || !data.IsValid() || data.IsOwner() ||
                data.GetOwner() != descriptor.Destination || data.OwnerRevision != descriptor.Revision + 1U ||
                ZNet.instance == null || ZNet.instance.IsServer() || Application.isBatchMode) return false;
#if TML_DEBUG_SYSTEM
            if (FgnSoloCombatTest.Vanilla(data)) return false;
#endif
            ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            Character actor = view ? view.GetComponent<Character>() : null;
            GameObject model = actor ? actor.GetVisual() : null;
            Transform visual = model ? model.transform : null;
            if (!actor || !view.IsValid() || !visual || visual == actor.transform || !visual.IsChildOf(actor.transform)) return false;
            CancelOwnerGain(data.m_uid);
            double now = Time.realtimeSinceStartupAsDouble;
            Vector3 inherited = actor.transform.position; Quaternion inheritedRotation = actor.transform.rotation;
            Vector3 modelPosition = visual.localPosition; Quaternion modelRotation = visual.localRotation;
            for (int i = tracks.Count - 1; i >= 0; --i)
                if (ReferenceEquals(tracks[i].Data, data))
                {
                    if (tracks[i].Token == descriptor.Token && tracks[i].Revision == descriptor.Revision) return true;
                    inherited = tracks[i].PresentedPosition; inheritedRotation = tracks[i].PresentedRotation;
                    modelPosition = tracks[i].LocalPosition; modelRotation = tracks[i].LocalRotation;
                    Restore(); RemoveAt(i);
                }
            if (AtCapacity(true) || !EnsurePresentationHooks()) return false;
            var item = new Track { Data = data, View = view, Actor = actor, Visual = visual, Observer = true,
                Destination = descriptor.Destination, Revision = descriptor.Revision, Token = descriptor.Token,
                Started = now, Last = now, InputAt = now, Latency = descriptor.Window,
                RecordingWindow = descriptor.RecordingSeconds, FirstArrival = descriptor.ExpectedArrival,
                MatchingHorizon = descriptor.RecordingSeconds * descriptor.Rate, OwnerStep = descriptor.NativeStep, PhysicsLayer = descriptor.PhysicsLayer,
                Timeline = new MobBridgeTimeline(now, descriptor.Window, descriptor.Rate, true),
                History = new MobBridgeRecording(now, descriptor.RecordingSeconds, descriptor.Rate),
                Rate = new MobBridgeRateTracker(descriptor.Rate), AiRate = new MobBridgeRateTracker(descriptor.AiRate),
                DestinationInterval = MobBridgeClockCadence.InitialInterval(descriptor.AiRate),
#if TML_BRIDGE_DIAGNOSTICS
                MinPlaybackRate = descriptor.Rate * MobBridgeTimeline.FakeRate,
#endif
                Origin = descriptor.Origin, PresentedPosition = inherited, PresentedRotation = inheritedRotation,
                InheritedOffset = inherited - descriptor.Origin,
                InheritedRotation = Quaternion.Inverse(descriptor.Rotation) * inheritedRotation,
                LocalPosition = visual.localPosition, LocalRotation = visual.localRotation,
                InheritedModelOffset = modelPosition - samples[0].ModelPosition,
                InheritedModelRotation = Quaternion.Inverse(samples[0].ModelRotation) * modelRotation };
            try
            {
                item.Animation = new MobBridgeAnimation(actor, descriptor.Paths, samples[0]);
                item.Timeline.ExpectFirst(item.FirstArrival);
                item.Timeline.ExpectMatch(descriptor.MatchArrival, descriptor.Startup);
                item.Timeline.ExpectFrames(MobBridgeProtocol.FrameSeconds);
                item.Hit = MobBridgeHit.Create(actor, inherited, inheritedRotation);
                if (item.Hit == null) { item.Animation.Dispose(); return false; }
                item.Skin = new MobBridgeSkin(model);
                item.Effects = new MobBridgeEffects(actor, descriptor.Effects);
                item.History.Add(0f, descriptor.Origin, descriptor.Rotation);
                foreach (MobBridgeObserverPacket.Sample sample in samples) AddObserverSample(item, sample);
                if (closed) { item.History.Close(); item.Animation.History.Close(); }
                if (tracks.Count == 0 && ownerGains.Count == 0)
                { Camera.onPreCull += BeforeRender; Camera.onPreRender += BeforeDraw; Camera.onPostRender += AfterRender; }
                tracks.Add(item);
                MobBridgeProtocol.Report("observer bridge start; mob=" + data.m_uid + "; samples=" + samples.Count);
                return true;
            }
            catch
            {
                if (item.Hit != null) item.Hit.Dispose(); if (item.Animation != null) item.Animation.Dispose();
                if (item.Skin != null) item.Skin.Dispose(); if (item.Effects != null) item.Effects.Dispose();
                throw;
            }
        }
        private static void AddObserverSample(Track item, MobBridgeObserverPacket.Sample sample)
        {
            if (item.History.Closed || sample.Time <= item.History.End) return;
            item.History.Add(sample.Time, sample.Position, sample.Rotation);
            item.Animation.Receive(sample); item.Simulated = sample.Time;
        }
        internal static void AppendObserver(ZDOID id, uint revision, uint token,
            IList<MobBridgeObserverPacket.Sample> samples, bool closed)
        {
            foreach (Track item in tracks)
            {
                if (!item.Observer || item.Data.m_uid != id || item.Revision != revision || item.Token != token || item.Takeover != null) continue;
                foreach (MobBridgeObserverPacket.Sample sample in samples) AddObserverSample(item, sample);
                item.InputAt = Time.realtimeSinceStartupAsDouble;
                if (closed && !item.History.Closed) { item.History.Close(); item.Animation.History.Close(); }
                return;
            }
        }

        internal static void PrepareSession()
        {
            MobBridgeEffects.PrepareSession();
            MobBridgeNativeActor.PrepareSession();
            if (!EnsurePresentationHooks())
            { MobBridgeNativeActor.EndSession(); throw new NotSupportedException("Preview presentation hooks unavailable."); }
            sessionHooks = true;
        }

        internal static void EndSession()
        {
            Stop(); sessionHooks = false;
            MobBridgeEffects.EndSession();
            DisablePresentationHooks(); MobBridgeNativeActor.EndSession();
        }

        internal static void Start(ZDO data, long destination, MonsterAI ai, Humanoid actor, Character target,
            MobBridgeSeed seed, MobStatusState.Shadow status, Vector3 origin, Quaternion rotation, Vector3 velocity, float latency,
            float initialRate, float initialAiRate, uint sourceRevision, MobBridgeTiming timing = null)
        {
            if (!MobBridgeProtocol.Prepare() || data == null || !data.IsValid() || actor == null ||
                ai == null || target == null || seed == null || ZNet.instance == null ||
                ZNet.instance.IsServer() || Application.isBatchMode) return;
            ZNetView view = actor.GetComponent<ZNetView>();
            GameObject model = actor.GetVisual();
            Transform visual = model ? model.transform : null;
            if (!view || !view.IsValid() || !visual || visual == actor.transform || !visual.IsChildOf(actor.transform)) return;
            CancelOwnerGain(data.m_uid);
            double now = Time.realtimeSinceStartupAsDouble;
            // Keep the simulated body at the checkpoint origin, but start the
            // visual at the pose visible after the owner write.
            Vector3 inheritedPosition = actor.transform.position;
            Quaternion inheritedRotation = actor.transform.rotation;
            Vector3 inheritedModelPosition = visual.localPosition;
            Quaternion inheritedModelRotation = visual.localRotation;
            for (int i = tracks.Count - 1; i >= 0; --i)
            {
                if (tracks[i].Data == null || tracks[i].Data.m_uid != data.m_uid) continue;
                inheritedPosition = tracks[i].PresentedPosition;
                inheritedRotation = tracks[i].PresentedRotation;
                inheritedModelPosition = tracks[i].LocalPosition;
                inheritedModelRotation = tracks[i].LocalRotation;
                Restore(); RemoveAt(i);
            }
            // Do not evict a visible unsettled correction to admit another mob.
            if (AtCapacity(false)) return;
            float recordingWindow = timing == null ? latency : timing.RecordingWindow;
            float firstArrival = timing == null ? latency : timing.FirstArrival;
            float ownerStep = timing == null ? Time.fixedDeltaTime : timing.PhysicsStep;
            NativeSteeringPlan plan = MobBridgeProtocol.Plan;
            if (plan == null) return;
            MobBridgeCost cost = null;
#if TML_BRIDGE_DIAGNOSTICS
            cost = new MobBridgeCost(data.m_uid, "setup");
#endif
            MobBridgeHit hit;
            try { hit = MobBridgeHit.Create(actor, origin, rotation); }
            catch (Exception error)
            {
                cost.Mark("hitboxesFailed"); cost.Finish();
                Plugin.Warn("bridge hit proxy unavailable; native transfer retained: " + error.GetBaseException().Message);
                return;
            }
            cost.Mark("hitboxes");
            if (hit == null) { cost.Finish(); return; }
            MobBridgeBody body;
            try { body = new MobBridgeBody(actor, target, origin, rotation, velocity, recordingWindow, status, cost); }
            catch (Exception error)
            {
                hit.Dispose();
                cost.Mark("bodyFailed"); cost.Finish();
                Plugin.Warn("native recording unavailable; native handoff retained: " + error.GetBaseException().Message);
                return;
            }
            Track item = new Track
            {
                Data = data, View = view, Actor = actor, Visual = visual, Destination = destination,
                Revision = sourceRevision, Timeline = new MobBridgeTimeline(now, latency, initialRate, true),
                Hit = hit, Status = status, PhysicsLayer = body.PhysicsLayer,
                Body = body, Animation = body.Animation, Started = now, Last = now,
                History = new MobBridgeRecording(now, recordingWindow, initialRate),
                CaptureClock = new MobBridgeCaptureClock(Time.fixedDeltaTime, recordingWindow * initialRate),
                RecordingWindow = recordingWindow, FirstArrival = firstArrival,
                MatchingHorizon = recordingWindow * initialRate, OwnerStep = ownerStep,
                Timing = timing,
                LastSimulation = now, Latency = latency, Rate = new MobBridgeRateTracker(initialRate), AiRate = new MobBridgeRateTracker(initialAiRate), Token = seed.Seed,
                DestinationInterval = MobBridgeClockCadence.InitialInterval(initialAiRate),
#if TML_BRIDGE_DIAGNOSTICS
                MinPlaybackRate = MobBridgeTimeline.FakeRate * initialRate,
                EntryVelocity = velocity,
#endif
                TargetTimeline = new MobBridgeTargetTimeline(target, now, latency, initialRate, MobBridgeProtocol.GameRate),
                LocalPosition = visual.localPosition, LocalRotation = visual.localRotation,
                PresentedPosition = inheritedPosition, PresentedRotation = inheritedRotation,
                Origin = origin,
                InheritedOffset = inheritedPosition - origin, InheritedRotation = Quaternion.Inverse(rotation) * inheritedRotation,
                InheritedModelOffset = inheritedModelPosition - visual.localPosition,
                InheritedModelRotation = Quaternion.Inverse(visual.localRotation) * inheritedModelRotation
            };
            item.Timeline.ExpectFirst(firstArrival);
            if (timing != null) item.Timeline.ExpectMatch(timing.ExpectedMatch, timing.Startup);
            item.Timeline.ExpectFrames(timing == null ? MobBridgeProtocol.FrameSeconds : timing.ReceiverFrame);
            try { item.Simulation = new MobBridgeSimulation(plan, ai, target, seed, status, 0d, recordingWindow * initialRate); }
            catch (Exception error)
            {
                hit.Dispose(); item.Body.Dispose();
                cost.Mark("aiContextFailed"); cost.Finish();
                Plugin.Warn("bridge simulation unavailable; native transfer retained: " + error.GetBaseException().Message);
                return;
            }
            cost.Mark("aiContext");
            if (!EnsurePresentationHooks()) { hit.Dispose(); item.Body.Dispose(); cost.Mark("presentationFailed"); cost.Finish(); return; }
            cost.Mark("presentationHooks");
            try { item.Skin = new MobBridgeSkin(model); }
            catch (Exception error)
            {
                hit.Dispose(); item.Body.Dispose(); cost.Mark("skinFailed"); cost.Finish();
                Plugin.Warn("bridge skin refresh unavailable; native handoff retained: " + error.GetBaseException().Message);
                return;
            }
            cost.Mark("skin"); cost.Count("skinRenderers", item.Skin.Count); cost.Count("skinChanged", item.Skin.Changed);
            try { item.Effects = new MobBridgeEffects(actor, status == null ? null : status.VisualFxIds); }
            catch (Exception error) { Plugin.Warn("Mob attached FX retain native presentation: " + error.GetBaseException().Message); }
            cost.Count("attachedFx", item.Effects == null ? 0 : item.Effects.Attached);
            cost.Mark("effects");
            item.History.Add(0f, origin, rotation);
            if (tracks.Count == 0 && ownerGains.Count == 0)
            { Camera.onPreCull += BeforeRender; Camera.onPreRender += BeforeDraw; Camera.onPostRender += AfterRender; }
            tracks.Add(item);
            MobBridgeObserver.Begin(data, destination, sourceRevision, seed.Seed, latency, initialRate, initialAiRate,
                origin, rotation, item.Animation, status == null ? null : status.VisualFxIds, recordingWindow, firstArrival, ownerStep,
                timing == null ? 0f : timing.ExpectedMatch, timing == null ? 0f : timing.Startup, body.PhysicsLayer);
            cost.Mark("publish"); cost.Finish();
            Plugin.TestLog("bridge visual ready; mob=" + data.m_uid + "; statuses=" +
                (status == null ? 0 : status.Count) + "; fakeHitbox=hitbox; shapes=" + hit.Count);
            MobBridgeProtocol.Report("bridge start; mob=" + data.m_uid + "; latency=" + latency.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "s; fake=" + MobBridgeTimeline.FakeRate.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                "x; recording=" + Number(recordingWindow) + "; firstExpected=" + Number(firstArrival));
        }

        internal static void Tick()
        {
            if (tracks.Count == 0 && ownerGains.Count == 0) return;
            int frame = Time.frameCount;
            bool due = false;
            for (int j = 0; j < tracks.Count; ++j) if (tracks[j].PresentationFrame != frame) { due = true; break; }
            for (int j = 0; j < ownerGains.Count; ++j) if (ownerGains[j].PresentationFrame != frame) { due = true; break; }
            if (!due) return;
            Restore();
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = tracks.Count - 1; i >= 0; --i)
            {
                Track item = tracks[i];
                if (item.PresentationFrame == frame) continue;
                item.PresentationFrame = frame;
                if (!Valid(item, now)) { RemoveAt(i); continue; }
                if (item.View.IsOwner()) item.Hit.RestorePhysics();
                try
                {
                    Advance(item, now);
                    UpdatePresentation(item, now);
                    // Compare with this frame's advanced presentation, not the
                    // preceding frame's pose while the native root has moved.
                    if (item.Takeover != null && (item.Ownership == null || !item.Ownership.Waiting) &&
                        TakeoverSettled(item, now))
                    { RemoveAt(i); continue; }
#if TML_BRIDGE_DIAGNOSTICS
                    if (!item.Observer && item.Takeover == null && item.MotionSamples < 2 &&
                        now - item.Started >= (item.MotionSamples == 0 ? .15d : .4d))
                    {
                        ++item.MotionSamples;
                        MobBridgeProtocol.Report("bridge motion; mob=" + item.Data.m_uid +
                            "; wall=" + Number((float)(now - item.Started)) + "; fake=" + Number(item.Timeline.FakeTime(now)) +
                            "; body=" + Number(item.Simulated) + "; moved=" + Number((item.PresentedPosition - item.Origin).magnitude) +
                            "; command=" + Number(item.Command.MoveDirection.magnitude) + "; desired=" + Number(item.Body.DesiredSpeed) +
                            "; velocity=" + Number(item.Body.Velocity.magnitude) + "; canMove=" + item.Body.CanMove +
                            "; recordingClosed=" + item.History.Closed + "; recordingEnd=" + Number(item.History.End) +
                            "; contact=" + item.Body.ContactName +
                            "; visualAttack=" + item.Animation.InAttack);
                    }
#endif
                }
                catch (Exception error)
                {
                    Plugin.Warn("Mob bridge frame failed; mob=" + item.Data.m_uid + "; error=" + error);
                    RemoveAt(i);
                }
            }
            for (int i = ownerGains.Count - 1; i >= 0; --i)
            {
                OwnerGain gain = ownerGains[i];
                if (gain.PresentationFrame == frame) continue;
                gain.PresentationFrame = frame;
                if (!gain.Actor || !gain.Visual || !gain.Visual.gameObject.activeInHierarchy)
                { RestoreOwnerGain(gain); ownerGains.RemoveAt(i); continue; }
                if (ComputeOwnerGainPose(gain, now))
                { RestoreOwnerGain(gain); ownerGains.RemoveAt(i); continue; }
            }
            if (tracks.Count == 0 && ownerGains.Count == 0) Unsubscribe();
        }

        internal static void Stop()
        {
            Restore();
            MobBridgeEffects.OwnershipSynced = null; ownershipSync = null;
            foreach (Track item in tracks)
            {
                if (!item.Observer && !item.History.Closed) CloseRecording(item, "presentation stopped");
                Finish(item, true);
                if (item.Hit != null) item.Hit.Dispose();
                if (item.Effects != null) item.Effects.Dispose();
                if (item.Body != null) item.Body.Dispose();
                else if (item.Animation != null) item.Animation.Dispose();
                if (item.Skin != null) item.Skin.Dispose();
            }
            tracks.Clear();
            foreach (OwnerGain gain in ownerGains) RestoreOwnerGain(gain);
            ownerGains.Clear();
            Unsubscribe();
        }
        private static string Number(float value) { return value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture); }

        internal static void ObserveClock(ZDOID id, uint revision, uint token, long tick,
            float simulationTime, float sampleTime, Vector3 position, Quaternion rotation,
            Vector3 velocity, float attackTime, string attack, float aiSimulation, double now, bool attackActive = false)
        {
            for (int i = 0; i < tracks.Count; ++i)
            {
                Track item = tracks[i];
                if (item.Data == null || item.Data.m_uid != id || item.Token != token) continue;
                if (item.Revision != revision || item.Data.GetOwner() != item.Destination ||
                    item.Data.OwnerRevision != revision + 1U) continue;
                float sampleInterval = sampleTime - item.DestinationFrame.SampleTime;
                bool physicalProgress = simulationTime > item.DestinationFrame.Simulation;
                if (!item.DestinationFrame.Observe(tick, simulationTime, sampleTime, position, rotation, velocity,
                    attackTime, attack, aiSimulation, attackActive)) return;
                bool first = !item.ClockSeen;
                if (item.Takeover != null)
                {
                    // Smoothing must continue to consume the live owner stream.
                    // Every newer frame moves its correction target and velocity.
                    item.ClockSeen = true;
                    item.DestinationSimulation = Math.Max(item.DestinationSimulation, simulationTime);
                    item.DestinationSampleWall = now;
                    if (!first)
                        item.DestinationInterval = MobBridgeClockCadence.ObserveInterval(item.DestinationInterval, sampleInterval);
                    if (tick > 0) item.AiRate.Observe(aiSimulation, simulationTime);
                    if (tick > 0 && item.Rate.Observe(simulationTime, sampleTime))
                    {
                        item.Timeline.ObserveRealRate(item.Rate.Estimate, item.DestinationInterval);
                        if (!item.Observer) item.TargetTimeline.ObserveRates(item.Rate.Estimate, MobBridgeProtocol.GameRate);
                    }
                    return;
                }
                if (physicalProgress && simulationTime < item.MatchingHorizon &&
                    item.MatchingHorizon - simulationTime < MobBridgeProtocol.ClockInterval)
                    item.PhysicsInterval = Math.Max(item.PhysicsInterval, sampleInterval);
                if (!first) item.DestinationInterval = MobBridgeClockCadence.ObserveInterval(item.DestinationInterval, sampleInterval);
                item.ClockSeen = true;
                item.DestinationSimulation = Math.Max(item.DestinationSimulation, simulationTime);
                item.DestinationSampleWall = now;
                float extra = item.Timeline.ObserveDelivery((float)Math.Max(0d, now - item.Started), sampleTime);
                if (item.Timing != null && extra != item.PlannedDelay)
                {
                    item.Timeline.ExpectMatch(item.Timing.MatchForRoute(item.Latency + extra), extra);
                    item.PlannedDelay = extra;
                }
                if (!item.Observer) item.TargetTimeline.ObserveRoute((float)Math.Max(.001d, now - item.Started - sampleTime));
                item.Timeline.ObserveReal(now, false);
                if (tick > 0) item.AiRate.Observe(aiSimulation, simulationTime);
                if (tick > 0 && item.Rate.Observe(simulationTime, sampleTime))
                    item.Timeline.ObserveRealRate(item.Rate.Estimate, item.DestinationInterval);
                if (!item.Observer) item.TargetTimeline.ObserveRates(item.Rate.Estimate, MobBridgeProtocol.GameRate);
                if (first) MobBridgeProtocol.Report("bridge clock; mob=" + id + "; tick=" + tick +
                    "; sim=" + simulationTime.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; fake=" + item.Timeline.FakeTime(now).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
        }

        private static bool Valid(Track item, double now)
        {
            if (item.Actor == null || !item.Visual || !item.View || !item.View.IsValid() ||
                item.Hit == null || !item.Hit.Alive ||
                !item.Data.IsValid() || item.Actor.IsDead() || now < item.Started) return false;
            if (item.Takeover != null)
            {
                // Only active corrections pay this primitive epoch comparison.
                // Ordinary mobs and unchanged handoffs acquire no watcher/work.
                if (item.Data.OwnerRevision != item.CorrectionRevision) OwnershipChanged(item, now);
                return true;
            }
            if (item.Data.GetOwner() != item.Destination || item.Data.OwnerRevision != item.Revision + 1U)
            {
                // A newer native owner epoch invalidates fake decisions, not the
                // displayed pose. Preserve it through a cosmetic correction.
                BeginTakeover(item, item.Last, true);
                OwnershipChanged(item, now);
            }
            return true;
        }

        private static void RemoveOwnershipWatch(Track item)
        {
            if (item.Ownership == null || !item.Ownership.Waiting) return;
            if (ownershipSync != null && !ReferenceEquals(item.Ownership.Sync, null)) ownershipSync.Remove(item.Ownership.Sync);
            item.Ownership.Waiting = false;
            if (ownershipSync == null || ownershipSync.Count == 0)
            { ownershipSync = null; MobBridgeEffects.OwnershipSynced = null; }
        }

        private static void OwnershipChanged(Track item, double now)
        {
            item.Placement = null;
            RemoveOwnershipWatch(item);
            item.CorrectionRevision = item.Data.OwnerRevision;
            if (item.Ownership == null) item.Ownership = new OwnershipBlend();
            if (item.View.IsOwner())
            {
                ZSyncTransform sync = item.Actor.GetComponent<ZSyncTransform>();
                if (sync)
                {
                    if (syncWasOwner == null) syncWasOwner = FieldReader.Create<ZSyncTransform, bool>(
                        AccessTools.Field(typeof(ZSyncTransform), "m_wasOwner"));
                    if (!syncWasOwner(sync))
                    {
                        // Ownership metadata can arrive before native OwnerSync
                        // restores its root. Rebase after that actual reset, not
                        // against an assumed future root or a second AI replay.
                        item.Ownership.Sync = sync; item.Ownership.Waiting = true;
                        if (ownershipSync == null) ownershipSync = new Dictionary<ZSyncTransform, Track>();
                        ownershipSync[sync] = item;
                        MobBridgeEffects.OwnershipSynced = OwnershipSynced;
                        return;
                    }
                }
            }
            RebaseOwnership(item, now);
        }

        private static void OwnershipSynced(ZSyncTransform sync)
        {
            Track item;
            if (ownershipSync == null || !ownershipSync.TryGetValue(sync, out item) || !syncWasOwner(sync)) return;
            RemoveOwnershipWatch(item);
            if (item.Data.IsValid() && item.Data.OwnerRevision == item.CorrectionRevision && item.View.IsOwner())
                RebaseOwnership(item, Time.realtimeSinceStartupAsDouble);
        }

        private static void RebaseOwnership(Track item, double now)
        {
            item.TakeoverStartTick = item.DestinationFrame.Tick;
            Vector3 root, velocity;
            Quaternion rotation;
            TakeoverTarget(item, now, out root, out rotation, out velocity);
            float distance = (item.PresentedPosition - root).magnitude;
            float yaw = Quaternion.Angle(item.PresentedRotation, rotation);
            float simulation = Math.Max(.001f, item.History.End);
            float playback = 2f * simulation;
            float speed = Math.Max(item.Actor.m_speed, velocity.magnitude);
            float frame = RenderFrameSeconds();
            float ownerReady = OwnerReadySeconds(item, frame);
            float duration = MobBridgeTakeover.SettleForPlaybackCapped(playback, distance, yaw, speed,
                item.Actor.m_turnSpeed, ownerReady, frame);
            duration = Math.Max(duration, MobBridgeTakeover.SettleForPlaybackCapped(playback,
                (item.LocalPosition - item.Visual.localPosition).magnitude,
                Quaternion.Angle(item.LocalRotation, item.Visual.localRotation), speed,
                item.Actor.m_groundTiltSpeed, ownerReady, frame));
            item.Ownership.ModelPosition = item.LocalPosition; item.Ownership.ModelRotation = item.LocalRotation;
            item.Takeover.RebaseOwnership(now, duration, item.PresentedRotation, root, rotation, velocity);
            Plugin.TestLog("bridge ownership rebase; mob=" + item.Data.m_uid + "; revision=" + item.CorrectionRevision +
                "; offset=" + Number(distance) + "; ready=" + Number(ownerReady) + "; settle=" + Number(duration));
        }

        private static void Advance(Track item, double now)
        {
            double previous = item.Last;
#if TML_BRIDGE_DIAGNOSTICS
            item.MaxFrameDelta = Math.Max(item.MaxFrameDelta, (float)Math.Max(0d, now - item.Last));
#endif
            item.Last = now;
            // A received state may already cover the previously displayed
            // horizon. Start the continuous transition there, then advance it
            // through this whole frame. Advancing fake first can miss that
            // match; starting at 'now' would instead repeat the previous pose.
            if (item.Takeover == null && !item.Frozen.HasValue && item.ClockSeen && item.DestinationSimulation > 0f &&
                item.DestinationSimulation + .000001f >= item.Timeline.FakeTime(previous) &&
                item.History.End + .000001f >= item.Timeline.FakeTime(previous))
                BeginTakeover(item, previous);
            if (item.Takeover == null)
            {
#if TML_BRIDGE_DIAGNOSTICS
                float previousRate = item.Timeline.PlaybackRate;
#endif
                if (item.Observer) item.Timeline.ManageInputBuffer(item.History.End, (float)Math.Max(0d, now - item.InputAt),
                    (float)MobBridgeProtocol.ClockInterval, item.History.Closed);
                float bufferInterval = item.DestinationInterval;
                float physicalStep = item.OwnerStep;
                // The publisher sends completed native steps near its matching
                // window, not only ordinary 50ms states. Do not reserve a full
                // periodic gap after each of those useful tail notifications.
                if (item.ClockSeen && item.DestinationSimulation < item.MatchingHorizon &&
                    item.MatchingHorizon - item.DestinationSimulation < MobBridgeProtocol.ClockInterval)
                {
                    bufferInterval = Math.Min(bufferInterval, Math.Max(item.PhysicsInterval, physicalStep / item.Rate.Estimate));
                }
                item.Timeline.ManageBuffer(item.Observer && item.History.Closed ? item.History.End : item.History.Horizon(item.History.Deadline),
                    item.DestinationSimulation, item.ClockSeen ? (float)Math.Max(0d, now - item.DestinationSampleWall) : 0f,
                    item.Rate.Estimate, bufferInterval, item.ClockSeen, physicalStep, Time.deltaTime);
                item.Timeline.AdvanceFrame(now, Time.deltaTime);
#if TML_BRIDGE_DIAGNOSTICS
                if (item.Timeline.BufferLimited) ++item.BufferSlowFrames;
                item.MinPlaybackRate = Math.Min(item.MinPlaybackRate, item.Timeline.PlaybackRate);
                item.MaxPlaybackRateStep = Math.Max(item.MaxPlaybackRateStep,
                    Math.Abs(item.Timeline.PlaybackRate - previousRate));
#endif
                if (!item.Observer && item.History.AcceptsInput(now)) { item.TargetTimeline.Observe(now); item.Body.ObserveInputs(); }
                if (!item.Timeline.RealStarted && now >= item.Started + item.FirstArrival + .25d)
                    item.Timeline.ObserveReal(item.Started + item.FirstArrival + .25d, true);
#if TML_BRIDGE_DIAGNOSTICS
                if (item.ClockSeen)
                {
                    float common = Math.Min(item.History.End, item.DestinationSimulation);
                    Vector3 projected, observed; Quaternion unused;
                    item.History.Sample(common, out projected, out unused);
                    item.DestinationFrame.History.Sample(common, out observed, out unused);
                    item.MaxDivergence = Math.Max(item.MaxDivergence, (projected - observed).magnitude);
                }
#endif
                float destination = ObservedDestination(item);
                item.Timeline.MatchHorizon(now, destination,
                    (float)(now - item.DestinationSampleWall));
                AdvanceSimulation(item, false);
                float playback = Math.Min(item.History.End, item.Timeline.FakeTime(now));
                bool atEnd = item.History.Closed && playback + .000001f >= item.History.End;
                bool complete = item.ClockSeen && destination > 0f && destination + .000001f >= playback;
#if TML_BRIDGE_DIAGNOSTICS
                if (!item.History.Closed && item.Timeline.FakeTime(now) > item.History.End + .000001f)
                    ++item.StarvedFrames;
                if (atEnd && !complete) ++item.EndpointFrames;
#endif
                if (item.Frozen.HasValue)
                {
                    if (!atEnd) return;
                    if (item.ClockSeen && destination + .00001f >= item.Frozen.Value && item.DestinationFrame.Tick >= item.Simulation.DecisionTick)
                    {
                        MobBridgeProtocol.Report("bridge unsupported decision release; mob=" + item.Data.m_uid +
                            "; sim=" + Number(item.Frozen.Value) +
                            "; destination=" + destination.ToString("F3",
                                System.Globalization.CultureInfo.InvariantCulture));
#if TML_BRIDGE_DIAGNOSTICS
                        if (item.ExpectedAttack.Length != 0 && item.ExpectedAttack != item.DestinationFrame.Attack)
                            MobBridgeProtocol.Report("bridge attack differs; mob=" + item.Data.m_uid +
                                "; fake=" + item.ExpectedAttack + "; destination=" + item.DestinationFrame.Attack);
#endif
                        BeginTakeover(item, now);
                    }
                    return;
                }
                if (complete && !item.Frozen.HasValue) BeginTakeover(item, now);
            }
            if (item.Takeover != null)
            {
                Vector3 target, velocity;
                Quaternion rotation;
                TakeoverTarget(item, now, out target, out rotation, out velocity);
                item.Takeover.AdvancePosition(target, now, velocity);
            }
        }

        private static float ObservedDestination(Track item)
        {
            return item.ClockSeen ? item.DestinationSimulation : 0f;
        }

        private static void AdvanceSimulation(Track item, bool unused)
        {
            if (item.Observer) return;
            if (item.History.Closed) return;
#if TML_BRIDGE_DIAGNOSTICS
            long captureStarted = MobBridgeCost.Now;
#endif
            try
            {
                float horizon = item.History.Horizon(item.Last);
                bool final = item.Last >= item.History.Deadline;
                float time, dt; bool decision, controller;
                while (item.CaptureClock.Take(horizon, final, .05f / Math.Max(.1f, item.AiRate.Estimate),
                    out time, out dt, out decision, out controller))
                {
                    if (decision)
                    {
#if TML_BRIDGE_DIAGNOSTICS
                        long aiStarted = MobBridgeCost.Now; ++item.Decisions;
#endif
                        Vector3 targetLead, targetVelocity; Quaternion targetRotation;
                        item.TargetTimeline.Prepare(time, out targetLead, out targetVelocity, out targetRotation);
                        item.Simulation.SetTargetInput(targetLead, targetVelocity, targetRotation);
                        item.Simulation.SetAttackState(item.CaptureAttack || item.Animation.InAttack,
                            item.Animation.Pending, item.Body.CanMove, item.Body.Staggering, item.Body.Flying,
                            item.CaptureAttack ? 0f : item.Animation.AttackAge);
                        StepResult result = item.Simulation.TryStep(time,
                            item.Timeline.RealStartOffset(item.Last), item.Body.Position,
                            item.Body.Rotation, item.Body.Velocity, item.Body.Swimming);
                        if (!result.Produced)
                        {
                            item.Frozen = item.Simulated;
                            CloseRecording(item, null);
                            return;
                        }
                        item.Animation.Alerted = item.Simulation.Alerted;
                        if (item.Simulation.AttackRequested)
                        {
                            if (item.Attacks == null) item.Attacks = new List<AttackEvent>(2);
                            item.Attacks.Add(new AttackEvent
                            {
                                Time = time,
                                Template = item.Simulation.VisualAttack,
                                Trigger = item.Simulation.AttackTrigger,
                                Name = item.Simulation.AttackName,
                                WeaponState = item.Simulation.WeaponState
                            });
                            // Do not mutate the private animator graph while
                            // recording. The graph is triggered only when the
                            // visual attack reaches its buffered timestamp.
                            item.CaptureAttack = true;
#if TML_BRIDGE_DIAGNOSTICS
                            item.ExpectedAttack = item.Simulation.AttackName;
#endif
                            MobBridgeProtocol.Report("bridge visual attack; mob=" + item.Data.m_uid + "; sim=" + Number(time) +
                                "; trigger=" + item.Simulation.AttackTrigger);
                        }
#if TML_BRIDGE_DIAGNOSTICS
                        item.AiTicks += MobBridgeCost.Now - aiStarted;
#endif
                        item.Command = result.Frame;
                        continue;
                    }
#if TML_BRIDGE_DIAGNOSTICS
                    long motorStarted = MobBridgeCost.Now; ++item.MotorSteps;
#endif
                    Vector3 lead, velocity; Quaternion rotation;
                    item.TargetTimeline.Prepare(time, out lead, out velocity, out rotation);
                    item.Body.TargetInput(lead, rotation);
                    if (!item.Body.Move(item.Command, time, dt, controller))
                    { item.Frozen = item.Simulated; Plugin.Warn("Mob bridge native movement returned an invalid pose; native handoff retained."); CloseRecording(item, "invalid native pose"); return; }
#if TML_BRIDGE_DIAGNOSTICS
                    item.MotorTicks += MobBridgeCost.Now - motorStarted;
#endif
                    item.Simulated = time;
                    item.History.Add(item.Simulated, item.Body.Position, item.Body.Rotation);
#if TML_BRIDGE_DIAGNOSTICS
                    if (item.MotorSteps == 1) { item.FirstMotorTime = time; item.FirstMotorPosition = item.Body.Position; }
                    if (item.MotorSteps == 2) { item.SecondMotorTime = time; item.SecondMotorPosition = item.Body.Position; }
#endif
                }
                if (item.Last >= item.History.Deadline) CloseRecording(item, "complete");
            }
            finally
            {
                StreamRecording(item, item.History.Closed);
#if TML_BRIDGE_DIAGNOSTICS
                long elapsed = MobBridgeCost.Now - captureStarted;
                item.CaptureTicks += elapsed; item.MaxCaptureTicks = Math.Max(item.MaxCaptureTicks, elapsed);
                if (item.History.Closed)
                    MobBridgeCost.Record("bridge cost; mob=" + item.Data.m_uid + "; operation=capture; totalMs=" +
                        Number((float)MobBridgeCost.Milliseconds(item.CaptureTicks)) + "; maxFrameMs=" +
                        Number((float)MobBridgeCost.Milliseconds(item.MaxCaptureTicks)) + "; aiMs=" +
                        Number((float)MobBridgeCost.Milliseconds(item.AiTicks)) + "; motorMs=" +
                        Number((float)MobBridgeCost.Milliseconds(item.MotorTicks)) + "; decisions=" + item.Decisions +
                        "; motorSteps=" + item.MotorSteps);
#endif
            }
        }

        private static void CloseRecording(Track item, string reason)
        {
            MobBridgeCost cost = null;
#if TML_BRIDGE_DIAGNOSTICS
            cost = new MobBridgeCost(item.Data.m_uid, "close");
#endif
            item.History.Close();
            ReportEntry(item);
            // Keep the existing private animator graph alive only when a
            // buffered attack still needs its normal-speed overlay.
            if (item.Attacks != null && item.Attacks.Count != 0)
                item.Animation.History.Close();
            else if (item.Body != null) item.Body.Dispose();
            else item.Animation.History.Close();
            if (!item.Observer) StreamRecording(item, true);
            cost.Mark("dispose"); cost.Finish();
            MobBridgeProtocol.Report("bridge recording closed; mob=" + item.Data.m_uid + "; end=" + Number(item.History.End) + "; reason=" + reason);
        }

        [System.Diagnostics.Conditional("TML_BRIDGE_DIAGNOSTICS")]
        private static void ReportEntry(Track item)
        {
#if TML_BRIDGE_DIAGNOSTICS
            if (item.Observer || item.EntryLogged) return;
            item.EntryLogged = true;
            MobBridgeCost.Record("bridge entry; mob=" + item.Data.m_uid + "; motorSteps=" + item.MotorSteps +
                "; entrySpeed=" + Number(item.EntryVelocity.magnitude) +
                "; firstSim=" + Number(item.FirstMotorTime) + "; firstMove=" + Number((item.FirstMotorPosition - item.Origin).magnitude) +
                "; firstY=" + Number(item.FirstMotorPosition.y) +
                "; secondSim=" + Number(item.SecondMotorTime) + "; secondMove=" + Number((item.SecondMotorPosition - item.FirstMotorPosition).magnitude) +
                "; secondY=" + Number(item.SecondMotorPosition.y) +
                "; inheritedOffset=" + Number(item.InheritedOffset.magnitude) + "; inheritedY=" + Number(item.InheritedOffset.y));
#endif
        }

        private static void TakeoverTarget(Track item, double now, out Vector3 position,
            out Quaternion rotation, out Vector3 velocity)
        {
            Transform root = item.Actor.transform;
            position = root.position;
            rotation = root.rotation;
            velocity = item.Actor.GetVelocity();
            if (!item.ClockSeen || item.DestinationFrame.Tick < 0) return;
            velocity = item.DestinationFrame.Velocity;
        }

        private static bool TakeoverSettled(Track item, double now)
        {
            Vector3 target, velocity;
            Quaternion rotation;
            // Do not release the display on the same owner frame that started
            // the correction. At low latency the next 50ms owner clock can
            // arrive after a short correction; retiring first exposes the
            // stale replica pose and the following packet looks like a reset.
            if (item.TakeoverStartTick >= 0L && item.DestinationFrame.Tick <= item.TakeoverStartTick)
                return false;
            TakeoverTarget(item, now, out target, out rotation, out velocity);
            return item.Takeover.Settled(now, target);
        }

        private static void UpdateVisualAttack(Track item, double now, float sample)
        {
            AttackEvent active = item.VisualAttack;
            if (active != null)
            {
                float visualDelta = (float)Math.Max(0d, now - active.LastVisualAt);
                active.LastVisualAt = now;
                bool localComplete = now - active.StartedAt >= active.Length;
                if (!localComplete)
                    item.Animation.AdvanceVisual(visualDelta);
                if (active.Checked && item.DestinationFrame.AttackActiveObserved &&
                    !item.DestinationFrame.AttackActive)
                {
                    item.Animation.EndVisualAttack();
                    item.VisualAttack = null;
                    return;
                }
                if (!active.Checked && item.ClockSeen &&
                    item.DestinationSimulation + .000001f >= active.Time + item.DestinationInterval)
                {
                    active.Checked = true;
                    if (item.DestinationFrame.AttackTime + .000001f < active.Time ||
                        item.DestinationFrame.Attack != active.Name ||
                        (item.DestinationFrame.AttackActiveObserved && !item.DestinationFrame.AttackActive))
                    {
                        item.Animation.EndVisualAttack();
                        active.Cancelled = true;
                        item.VisualAttack = null;
                    }
                }
                if (localComplete && !item.DestinationFrame.AttackActiveObserved)
                {
                    item.Animation.EndVisualAttack();
                    item.VisualAttack = null;
                    return;
                }
                // With a real lifecycle signal, keep the last attack pose
                // frozen until the owner reports the attack ended. This avoids
                // the visual clip ending early and switching to idle.
                return;
            }
            if (item.VisualAttack != null || item.Attacks == null) return;
            for (int i = 0; i < item.Attacks.Count; ++i)
            {
                AttackEvent entry = item.Attacks[i];
                if (entry.Started || entry.Cancelled || sample + .000001f < entry.Time) continue;
                entry.Started = true; entry.StartedAt = entry.LastVisualAt = now;
                entry.Length = item.Animation.BeginVisualAttack(entry.Template, entry.Trigger, entry.WeaponState, sample);
                item.VisualAttack = entry;
                return;
            }
        }

        private static void BeginTakeover(Track item, double now, bool interrupted = false)
        {
            if (!item.History.Closed) CloseRecording(item, "observed takeover");
            Vector3 fake;
            Quaternion fakeRotation;
            float horizon = Math.Min(item.History.End, item.Timeline.FakeTime(now));
            item.AnimationJoinTime = horizon;
            item.TakeoverStartTick = item.DestinationFrame.Tick;
            ComputePose(item, now, out fake, out fakeRotation);
            ComputeModelPose(item, horizon, now, out item.JoinModelPosition, out item.JoinModelRotation);
#if TML_BRIDGE_DIAGNOSTICS
            Vector3 matched; Quaternion matchedRotation;
            item.DestinationFrame.History.Sample(horizon, out matched, out matchedRotation);
            float matchedDistance = (fake - matched).magnitude;
#endif
            Vector3 root, rootVelocity;
            Quaternion rootRotation;
            TakeoverTarget(item, now, out root, out rootRotation, out rootVelocity);
            float yaw = -MobVisualCurve.Yaw(fakeRotation, rootRotation);
            float distance = (fake - root).magnitude;
            float movementSpeed = Math.Max(item.Actor.m_speed, rootVelocity.magnitude);
            float simulation = Math.Max(.001f, horizon);
            float playback = 2f * simulation;
            float frame = RenderFrameSeconds();
            float ownerReady = OwnerReadySeconds(item, frame);
            float settle = MobBridgeTakeover.SettleForPlaybackCapped(playback, distance, yaw,
                movementSpeed, item.Actor.m_turnSpeed, ownerReady, frame);
            settle = Math.Max(settle, MobBridgeTakeover.SettleForPlaybackCapped(playback,
                (item.JoinModelPosition - item.Visual.localPosition).magnitude,
                Quaternion.Angle(item.JoinModelRotation, item.Visual.localRotation), movementSpeed,
                item.Actor.m_groundTiltSpeed, ownerReady, frame));
            item.Takeover = new MobBridgeTakeover(now, settle, fake, fakeRotation,
                root, rootRotation, rootVelocity,
                (item.History.End > 0f ? item.History.Velocity(horizon) :
                    item.Body != null ? item.Body.Velocity : item.Actor.GetVelocity()) * item.Timeline.PlaybackRate);
            item.CorrectionRevision = item.Data.OwnerRevision;
            Finish(item, false);
#if TML_BRIDGE_DIAGNOSTICS
            MobBridgeProtocol.Report("bridge takeover; mob=" + item.Data.m_uid + "; fake=" +
                horizon.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; matchedOffset=" + matchedDistance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; destination=" + item.DestinationSimulation.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; offset=" + distance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; yaw=" + yaw.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                "; ready=" + ownerReady.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; settle=" + settle.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; maxDivergence=" + item.MaxDivergence.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; clock=" + item.ClockSeen + "; reason=" + (interrupted ? "owner-change" : "matched"));
#endif
        }

        private static float OwnerReadySeconds(Track item, float frame)
        {
            float rate = item.Rate == null ? 1f : item.Rate.Estimate;
            float step = item.OwnerStep > 0f ? item.OwnerStep : Time.fixedDeltaTime;
            float simulationReady = MobBridgeTimeline.Finite(step) && MobBridgeTimeline.Finite(rate) && rate > 0f ?
                step / rate : frame;
            float sendInterval = MobBridgeTimeline.Finite(item.DestinationInterval) && item.DestinationInterval > 0f ?
                item.DestinationInterval : (float)MobBridgeProtocol.ClockInterval;
            return Math.Max(frame, Math.Max(simulationReady,
                Math.Max(sendInterval, MobBridgeProtocol.FrameSeconds)));
        }

        private static float RenderFrameSeconds()
        {
            float frame = Time.deltaTime;
            if (!MobBridgeTimeline.Finite(frame) || frame <= 0f) frame = Time.smoothDeltaTime;
            return MobBridgeTimeline.Finite(frame) && frame > 0f ? frame : .016f;
        }

        private static void BeforeRender(Camera camera)
        {
            try { Tick(); ApplyRender(camera); }
            catch (Exception error) { Stop(); Plugin.Report("Mob bridge render stopped; gameplay retained: " + error.GetBaseException().Message); }
        }

        private static void ApplyRender(Camera camera)
        {
            if (rendering.Count != 0) { rendering.Add(camera); return; }
            Restore(); rendering.Add(camera);
            foreach (Track item in tracks)
            {
                if (!item.Visual || !item.Visual.gameObject.activeInHierarchy) continue;
                ApplyPose(item);
                if (item.Effects != null) item.Effects.Update(item.PresentedPosition, item.PresentedRotation, item.Animation);
            }
            foreach (OwnerGain gain in ownerGains)
            {
                if (!gain.Visual || !gain.Visual.gameObject.activeInHierarchy) continue;
                if (!gain.Applied)
                {
                    gain.SavedLocalPosition = gain.Visual.localPosition;
                    gain.SavedLocalRotation = gain.Visual.localRotation;
                }
                gain.Visual.localPosition = gain.PresentedLocalPosition;
                gain.Visual.localRotation = gain.PresentedLocalRotation;
                gain.Applied = true;
            }
        }

        private static void BeforeDraw(Camera camera)
        {
            if (rendering.Count == 0) return;
            try
            {
                // Culling may synchronize native animation after onPreCull.
                // Save/replace bones only at the last camera boundary, so the
                // cached presentation wins without an extra rig application.
                foreach (Track item in tracks)
                {
                    if (!item.Applied || !item.Visual) continue;
                    item.Animation.Apply();
                    if (item.Effects != null) item.Effects.Update(item.PresentedPosition, item.PresentedRotation, item.Animation);
                    // Native IK/animation may have completed since the HUD
                    // sample. During the short blend, refresh query shapes
                    // from the actual final bones that this camera will draw.
                    if (item.Animation.Blending)
                        item.Hit.Follow(item.PresentedPosition, item.PresentedRotation, item.Animation);
#if TML_BRIDGE_DIAGNOSTICS
                    item.LastBoneRotation = item.Animation.RootRotation;
                    if (item.RotationSample)
                        item.MaxApplyBoneGap = Math.Max(item.MaxApplyBoneGap,
                            Quaternion.Angle(item.LastBoneRotation, item.Animation.VisibleRootRotation));
#endif
                }
                MeasureDraw(camera);
            }
            catch (Exception error) { Stop(); Plugin.Report("Mob bridge draw stopped; gameplay retained: " + error.GetBaseException().Message); }
        }

        [System.Diagnostics.Conditional("TML_BRIDGE_DIAGNOSTICS")]
        private static void MeasureDraw(Camera camera)
        {
#if TML_BRIDGE_DIAGNOSTICS
            // One read of the model/root bone per active preview/camera. Detect
            // late pose overwrites without changing rendering or simulation.
            if (rendering.Count == 0) return;
            foreach (Track item in tracks)
            {
                if (!item.Applied || !item.Visual || !item.RotationSample) continue;
                item.MaxDrawModelGap = Math.Max(item.MaxDrawModelGap,
                    Quaternion.Angle(item.PresentedRotation * item.LocalRotation, item.Visual.rotation));
                item.MaxDrawBoneGap = Math.Max(item.MaxDrawBoneGap,
                    Quaternion.Angle(item.LastBoneRotation, item.Animation.VisibleRootRotation));
                ++item.DrawSamples;
            }
#endif
        }

        private static void ApplyPose(Track item)
        {
            item.SavedPosition = item.Visual.localPosition;
            item.SavedRotation = item.Visual.localRotation;
            item.Applied = true;
            item.Visual.SetPositionAndRotation(ModelPosition(item),
                item.PresentedRotation * item.LocalRotation);
        }

        private static void RestorePose(Track item)
        {
            if (item.Animation != null) item.Animation.Restore();
            if (!item.Applied) return;
            if (item.Visual) { item.Visual.localPosition = item.SavedPosition; item.Visual.localRotation = item.SavedRotation; }
            item.Applied = false;
            if (item.Effects != null && item.Actor && item.Visual)
                item.Effects.Update(item.PresentedPosition, item.PresentedRotation, item.Animation);
        }

        private static void AfterRender(Camera camera)
        {
            if (rendering.Count == 0) return;
            MeasureDraw(camera);
            if (!ReferenceEquals(rendering[rendering.Count - 1], camera)) { Restore(); return; }
            rendering.RemoveAt(rendering.Count - 1);
            if (rendering.Count == 0) Restore();
        }

        private static void Restore()
        {
            foreach (Track item in tracks)
            {
                RestorePose(item);
            }
            foreach (OwnerGain gain in ownerGains) RestoreOwnerGain(gain);
            rendering.Clear();
        }

        private static void RestoreOwnerGain(OwnerGain gain)
        {
            if (gain == null || !gain.Applied) return;
            if (gain.Visual)
            { gain.Visual.localPosition = gain.SavedLocalPosition; gain.Visual.localRotation = gain.SavedLocalRotation; }
            gain.Applied = false;
        }

        private static bool ComputeOwnerGainPose(OwnerGain gain, double now)
        {
            Vector3 nativeWorld = gain.Actor.transform.TransformPoint(gain.Visual.localPosition);
            Quaternion nativeWorldRotation = gain.Actor.transform.rotation * gain.Visual.localRotation;
            gain.Motion.AdvancePosition(nativeWorld, now, gain.Actor.GetVelocity());
            Vector3 wantedWorld = gain.Motion.Position;
            gain.PresentedLocalPosition = gain.Actor.transform.InverseTransformPoint(wantedWorld);
            Quaternion wantedWorldRotation = gain.Motion.Rotation(nativeWorldRotation, now);
            gain.PresentedLocalRotation = Quaternion.Inverse(gain.Actor.transform.rotation) * wantedWorldRotation;
            return gain.Motion.Settled(now, nativeWorld);
        }

        private static void ComputePose(Track item, double now, out Vector3 position, out Quaternion rotation)
        {
            if (item.Takeover != null)
            {
                Vector3 target, velocity;
                Quaternion targetRotation;
                TakeoverTarget(item, now, out target, out targetRotation, out velocity);
                position = item.Takeover.Position;
                rotation = item.Takeover.Rotation(targetRotation, now);
                return;
            }
            float sample = Math.Min(item.History.End, item.Timeline.FakeTime(now));
            item.History.Sample(sample, out position, out rotation);
            // Preserve an overlapping bridge's display frame. Decaying it
            // here creates a second competing correction that can run backwards.
            // The observed-horizon takeover settles the whole residual once.
            position += item.InheritedOffset;
            rotation = rotation * item.InheritedRotation;
        }

        private static void ComputeModelPose(Track item, float sample, double now, out Vector3 position, out Quaternion rotation)
        {
            if (item.Takeover == null)
            {
                item.Animation.History.SampleRoot(sample, out position, out rotation);
                position += item.InheritedModelOffset;
                rotation = rotation * item.InheritedModelRotation;
                return;
            }
            float t = item.Takeover.AnimationWeight(now);
            position = item.JoinModelPosition * (1f - t) + item.Visual.localPosition * t;
            rotation = Quaternion.Slerp(item.JoinModelRotation, item.Visual.localRotation, t);
            if (item.Ownership != null && !item.Ownership.Waiting)
            {
                float weight = item.Takeover.OwnershipWeight(now);
                position = item.Ownership.ModelPosition * (1f - weight) + position * weight;
                rotation = Quaternion.Slerp(item.Ownership.ModelRotation, rotation, weight);
            }
        }

        private static Vector3 ModelPosition(Track item)
        {
            return MobBridgePresentationPoint.ModelPosition(item.PresentedPosition, item.PresentedRotation,
                item.LocalPosition, item.Actor.transform.lossyScale);
        }

        private static void UpdatePresentation(Track item, double now)
        {
            Vector3 previous = item.PresentedPosition;
            ComputePose(item, now, out item.PresentedPosition, out item.PresentedRotation);
            if (!item.InheritedOffset.Equals(Vector3.zero))
            {
                if (item.Placement == null) item.Placement = new MobBridgePlacement(item.Actor, item.PhysicsLayer);
                Vector3 constrained = item.Placement.Resolve(previous, item.PresentedPosition, item.PresentedRotation);
                item.PresentedPosition = constrained;
            }
            float sample = item.Takeover == null ? Math.Min(item.History.End, item.Timeline.FakeTime(now)) :
                item.Takeover.AnimationTime(item.AnimationJoinTime, now);
            UpdateVisualAttack(item, now, sample);
            ComputeModelPose(item, sample, now, out item.LocalPosition, out item.LocalRotation);
            float nativeWeight = MobBridgeAnimationBlend.NativeWeight((float)Math.Max(0d, now - item.Started), sample,
                item.History.Horizon(item.History.Deadline), item.Takeover == null ? 0f : item.Takeover.AnimationWeight(now),
                item.Animation.History.AttackWeight(sample));
            // An empty producer has no moving sample yet. Keep native bones
            // until the first interval exists instead of showing frozen t0.
            if (item.Animation.History.End <= 0f) nativeWeight = 1f;
            if (item.VisualAttack != null)
                item.Animation.PrepareLive(ModelPosition(item), item.PresentedRotation * item.LocalRotation);
            else
                item.Animation.Prepare(sample, nativeWeight,
                    ModelPosition(item), item.PresentedRotation * item.LocalRotation);
            if (item.Effects != null) item.Effects.Update(item.PresentedPosition, item.PresentedRotation, item.Animation);
            FollowHit(item, now);
        }

        internal static void PrepareEffectAnchors()
        {
            // Unity samples emitter transforms before ordinary LateUpdate.
            // Advance the common presentation once at that native boundary.
            Tick();
        }

        private static void CurrentPose(Track item, double now, out Vector3 position, out Quaternion rotation)
        {
            position = item.PresentedPosition; rotation = item.PresentedRotation;
        }

        private static void FollowHit(Track item, double now)
        {
            if (item.Hit == null || !item.Hit.Alive) return;
            Vector3 position;
            Quaternion rotation;
            CurrentPose(item, now, out position, out rotation);
            // Query only requested chains; never mutate the live visible rig.
            item.Hit.Follow(position, rotation, item.Animation);
#if TML_BRIDGE_DIAGNOSTICS
            Quaternion bone = item.Animation.RootRotation;
            if (item.RotationSample)
            {
                item.MaxRootStep = Math.Max(item.MaxRootStep, Quaternion.Angle(item.LastRootRotation, rotation));
                item.MaxBoneStep = Math.Max(item.MaxBoneStep, Quaternion.Angle(item.LastBoneRotation, bone));
            }
            item.LastRootRotation = rotation; item.LastBoneRotation = bone; item.RotationSample = true;
#endif
        }

        private static bool EnsurePresentationHooks()
        {
            if (presentationHooks != null) return true;
            Harmony candidate = new Harmony(Plugin.Id + ".mobbridge.presentation");
            try
            {
                candidate.Patch(AccessTools.Method(typeof(Character), "Damage", new[] { typeof(HitData) }),
                    prefix: new HarmonyMethod(typeof(MobBridgeVisual), "BeforeHit"));
                candidate.Patch(AccessTools.Method(typeof(EnemyHud), "UpdateHuds"),
                    prefix: new HarmonyMethod(typeof(MobBridgeVisual), "BeforeHud"),
                    finalizer: new HarmonyMethod(typeof(MobBridgeVisual), "AfterHud"));
                candidate.Patch(AccessTools.Method(typeof(Projectile), "FindHitObject", new[] { typeof(Collider) }),
                    prefix: new HarmonyMethod(typeof(MobBridgeVisual), "BeforeHitObject"));
                candidate.Patch(AccessTools.Method(typeof(Character), "FindWeakSpotIndex", new[] { typeof(Collider) }),
                    prefix: new HarmonyMethod(typeof(MobBridgeVisual), "BeforeWeakSpot"));
                candidate.Patch(AccessTools.Method(typeof(Character), "GetTopPoint"),
                    prefix: new HarmonyMethod(typeof(MobBridgeVisual), "BeforeTopPoint"));
                presentationHooks = candidate;
                return true;
            }
            catch (Exception error)
            {
                candidate.UnpatchSelf();
                Plugin.Warn("bridge presentation hooks unavailable; native transfer retained: " +
                    error.GetBaseException().Message);
                return false;
            }
        }

        private static void DisablePresentationHooks()
        {
            hudAnchorDepth = 0;
            if (presentationHooks == null) return;
            presentationHooks.UnpatchSelf(); presentationHooks = null;
        }

        private static bool BeforeHit(Character __instance, HitData hit)
        {
            if (!__instance || hit == null) return true;
            for (int i = 0; i < tracks.Count; ++i)
            {
                Track item = tracks[i];
                if (ReferenceEquals(item.Actor, __instance) && item.Hit != null && item.Hit.Alive)
                {
                    bool accepted = item.Hit.Accepts(hit);
                    if (!accepted)
                        MobBridgeProtocol.Report("bridge hidden hit suppressed; mob=" + item.Data.m_uid);
                    else if (item.Hit.HitsFakePose(hit))
                        MobBridgeProtocol.Report("bridge fake hit accepted; mob=" + item.Data.m_uid +
                            "; status=" + hit.m_statusEffectHash);
                    return accepted;
                }
            }
            return true;
        }

        private static bool BeforeHitObject(Collider __0, ref GameObject __result)
        {
            Character owner;
            if (!MobBridgeHit.TryOwner(__0, out owner)) return true;
            __result = owner ? owner.gameObject : null;
            return false;
        }

        private static void BeforeWeakSpot(Character __instance, ref Collider __0)
        {
            // Let vanilla calculate its native weak-spot index for this shape.
            // HitData keeps the displayed collider for hit routing/feedback.
            __0 = MobBridgeHit.NativeCollider(__instance, __0);
        }

        private static void BeforeHud()
        {
            // EnemyHud runs before LateUpdate. Preparing the frame here avoids
            // a one-render-frame nameplate lag; later consumers only read it.
            if (tracks.Count == 0) return;
            Tick();
            ++hudAnchorDepth;
        }

        private static Exception AfterHud(Exception __exception)
        {
            if (hudAnchorDepth > 0) --hudAnchorDepth;
            return __exception;
        }

        private static bool BeforeTopPoint(Character __instance, ref Vector3 __result)
        {
            if (hudAnchorDepth <= 0 || !__instance) return true;
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < tracks.Count; ++i)
            {
                Track item = tracks[i];
                if (!ReferenceEquals(item.Actor, __instance) || item.Hit == null || !item.Hit.Alive) continue;
                Vector3 position;
                Quaternion rotation;
                CurrentPose(item, now, out position, out rotation);
                __result = item.Hit.TopPoint(position, rotation, item.LocalRotation);
                return false;
            }
            return true;
        }

        private static void RemoveAt(int index)
        {
            Track item = tracks[index];
#if TML_BRIDGE_DIAGNOSTICS
            MobBridgeCost.Record("bridge cost; mob=" + item.Data.m_uid + "; operation=presentation; maxRootStepDeg=" +
                Number(item.MaxRootStep) + "; maxBoneStepDeg=" + Number(item.MaxBoneStep) +
                "; maxDrawModelGapDeg=" + Number(item.MaxDrawModelGap) + "; maxDrawBoneGapDeg=" +
                Number(item.MaxDrawBoneGap) + "; maxApplyBoneGapDeg=" + Number(item.MaxApplyBoneGap) +
                "; drawSamples=" + item.DrawSamples + "; bufferStarvedFrames=" + item.StarvedFrames +
                "; endpointHeldFrames=" + item.EndpointFrames + "; bufferSlowFrames=" + item.BufferSlowFrames +
                "; minPlaybackRate=" + Number(item.MinPlaybackRate) + "; maxPlaybackRateStep=" + Number(item.MaxPlaybackRateStep) +
                "; maxFrameMs=" + Number(item.MaxFrameDelta * 1000f) +
                "; attachedFx=" + (item.Effects == null ? 0 : item.Effects.Attached));
#endif
            if (!item.Observer && !item.History.Closed) CloseRecording(item, "presentation retired");
            RemoveOwnershipWatch(item);
            Finish(item, true);
            if (item.Hit != null) item.Hit.Dispose();
            if (item.Effects != null) item.Effects.Dispose();
            if (item.Body != null) { item.Animation.Restore(); item.Body.Dispose(); }
            else if (item.Animation != null) { item.Animation.Restore(); item.Animation.Dispose(); }
            if (item.Skin != null) item.Skin.Dispose();
            tracks.RemoveAt(index);
            if (tracks.Count == 0 && ownerGains.Count == 0) Unsubscribe();
        }
        private static void Unsubscribe()
        {
            Camera.onPreCull -= BeforeRender; Camera.onPreRender -= BeforeDraw; Camera.onPostRender -= AfterRender;
            if (!sessionHooks) DisablePresentationHooks();
            rendering.Clear();
        }

    }

    internal static class MobBridgeActorExtensions
    {
        // Keeps the takeover condition tied to the captured owner epoch without
        // mutating the actor or asking a replica for owner-only state.
        internal static bool OwnerRevisionRevised(this Character actor, uint revision)
        {
            ZNetView view = actor ? actor.GetComponent<ZNetView>() : null;
            ZDO data = view && view.IsValid() ? view.GetZDO() : null;
            return data == null || data.OwnerRevision > revision + 1U;
        }
    }
}
#endif
