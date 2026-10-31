using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // A bounded, old-owner render bridge. Native roots and their network/AI
    // state are never moved. Only a separate collider-free Visual subtree is
    // offset between the camera's pre-cull and post-render callbacks.
    internal static class MobHandoffVisual
    {
        private sealed class Track
        {
            internal ZDO Data; internal ZNetView View; internal Character Actor;
            internal Transform Visual; internal long Destination;
            internal uint Revision, DataRevision; internal double Started, Last, Received;
            internal Vector3 Origin, Motion, Intent, Offset, LastRoot;
            internal Quaternion OriginRotation, Aim, LastRotation;
            internal float Window, Blend, TurnSpeed, Turn, Fade; internal bool HasState;
            internal bool Flight; internal float Height;
            internal BaseAI Ai; internal Character Target; internal ZDOID TargetId;
            internal bool TargetAim, TargetChanged, CheckTarget, Observer, Begun;
            internal float FlightFactor, FlightDesired, BaseTurnSpeed;
            internal Vector3 SavedPosition; internal Quaternion SavedRotation; internal bool Applied;
            internal Vector3 TargetPoint; internal float TargetHorizon, FutureWeight, WindowGoal, HorizonGoal, SimTime;
            internal bool RemoteFrame; internal Vector3 RemotePosition; internal float RemoteYaw; internal double RemoteAt;
        }
        private static readonly List<Track> tracks = new List<Track>(8);
        private static readonly Func<Humanoid, Attack> attack = FieldReader.Create<Humanoid, Attack>(AccessTools.Field(typeof(Humanoid), "m_currentAttack"));
        private static readonly Func<Character, float> flightFactor = FieldReader.Create<Character, float>(AccessTools.Field(typeof(Character), "currentRotSpeedFactor"));
        private static Harmony harmony;
        private static ZNet network;
        private static ZDOMan world;
        private static readonly List<Camera> rendering = new List<Camera>(4);
        private const int Limit = 32;
        internal static Action<ZDO, Vector3, float> SourcePreview = null;
        internal static Func<ZDO, bool> SkipPresentation = null;
        internal static void Cancel(ZDOID id)
        {
            Restore();
            for (int i = tracks.Count - 1; i >= 0; --i)
                if (tracks[i].Data.m_uid == id) tracks.RemoveAt(i);
            if (tracks.Count == 0) Unsubscribe();
        }

        internal static void Start(Assembly fgn, bool enabled)
        {
            Stop();
            if (!enabled || !ZNet.instance || ZNet.instance.IsServer() || Application.isBatchMode) return;
            try
            {
                MethodInfo commit = AccessTools.Method(fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true), "OnHandOff", new[] { typeof(ZRpc), typeof(ZDOID), typeof(long) });
                if (commit == null || commit.ReturnType != typeof(void)) throw new NotSupportedException("FGN mob visual handoff site unavailable.");
                harmony = new Harmony(Plugin.Id + ".mobvisual");
                harmony.Patch(commit, prefix: new HarmonyMethod(typeof(MobHandoffVisual), "BeforeCommit"), postfix: new HarmonyMethod(typeof(MobHandoffVisual), "AfterCommit"));
                network = ZNet.instance; world = ZDOMan.instance;
                PlayerSimulation.LocalAgent.VisualCapture = CapturePlayerTransfer;
                PlayerSimulation.LocalAgent.VisualCommitted = CompletePlayerTransfer;
            }
            catch (Exception error) { Stop(); Plugin.Report("Mob handoff visual bridge inactive: " + error.GetBaseException().Message); }
        }
        internal static void Stop()
        {
            Restore(); tracks.Clear(); Unsubscribe();
            PlayerSimulation.LocalAgent.VisualCapture = null;
            PlayerSimulation.LocalAgent.VisualCommitted = null;
            if (harmony != null) { harmony.UnpatchSelf(); harmony = null; }
            network = null; world = null;
        }
        private static bool Busy(Character actor)
        {
            if (actor.InAttack() || actor.IsStaggering() || actor.IsDead() || actor.IsAttached() || actor.IsRiding()) return true;
            Humanoid human = actor as Humanoid; Attack current = human ? attack(human) : null;
            return current != null && !current.IsDone();
        }
        private static float Rtt()
        {
            try
            {
                ZNetPeer server = network.GetServerPeer();
                if (server == null || !server.IsReady() || server.m_socket == null) return 0f;
                float local, remote, sent, received; int ping;
                server.m_socket.GetConnectionQuality(out local, out remote, out ping, out sent, out received);
                return ping > 0 ? ping / 1000f : 0f;
            }
            catch (Exception) { return 0f; } // Unsupported transports do not stop a handoff.
        }
        internal static Func<long, float, float> LatencyWindow = null;
        private static void BeforeCommit(ZDOID __1, long __2, out Track __state)
        {
            __state = null;
            try { __state = Capture(__1, __2); }
            catch (Exception error) { Stop(); Plugin.Report("Mob visual capture stopped; native handoffs retained: " + error.GetBaseException().Message); }
        }
        private static object CapturePlayerTransfer(ZDO data, long target)
        { Track item; BeforeCommit(data.m_uid, target, out item); return item; }
        internal static object CaptureSource(ZDO data, long target) { return CapturePlayerTransfer(data, target); }
        private static void CompletePlayerTransfer(object item) { AfterCommit(item as Track); }
        private static Track Capture(ZDOID __1, long __2)
        {
            if (!Current() || __2 == 0L || __2 == ZDOMan.GetSessionID()) return null;
            ZDO data = world.GetZDO(__1);
            if (data == null || !data.IsValid() || !data.IsOwner()) return null;
            ZNetView view = ZNetScene.instance.FindInstance(data);
            Character actor = view ? view.GetComponent<Character>() : null;
            if (!actor || actor is Player || Busy(actor)) return null;
            BaseAI ai = view.GetComponent<BaseAI>(); bool fresh = ai && FgnTargetBoundary.FreshTarget(ai);
            return CaptureModel(data, __2, data.OwnerRevision, actor.GetVelocity(), actor.GetMoveDir(),
                fresh ? ai.GetTargetCreature() : null, fresh, actor.IsFlying(), MobVisualCurve.Window(LatencyWindow == null ? Rtt() : LatencyWindow(__2, Rtt())));
        }
        private static bool Current()
        {
            if (world == null && ReferenceEquals(network, ZNet.instance)) world = ZDOMan.instance;
            return harmony != null && ReferenceEquals(network, ZNet.instance) && ReferenceEquals(world, ZDOMan.instance) && world != null && ZNetScene.instance;
        }
        internal static object CaptureObserver(ZDO data, long source, long destination, uint sourceRevision, ZDOID realTarget,
            Vector3 sourceVelocity, Vector3 sourceDirection, bool flight, float window)
        {
            if (!Current() || data == null || !data.IsValid() || !ReferenceEquals(world.GetZDO(data.m_uid), data) || source == 0 || destination == 0 ||
                source == destination || source == ZDOMan.GetSessionID() || destination == ZDOMan.GetSessionID() || sourceRevision >= UInt16.MaxValue ||
                !(data.OwnerRevision == sourceRevision && data.GetOwner() == source || data.OwnerRevision == sourceRevision + 1U && data.GetOwner() == destination)) return null;
            ZDO targetData = realTarget.IsNone() ? null : world.GetZDO(realTarget);
            ZNetView targetView = targetData != null && targetData.IsValid() ? ZNetScene.instance.FindInstance(targetData) : null;
            Character target = targetView ? targetView.GetComponent<Character>() : null;
            Track item = CaptureModel(data, destination, sourceRevision, sourceVelocity, sourceDirection, target, !realTarget.IsNone(), flight, MobVisualCurve.Window(window));
            if (item != null) item.Observer = true;
            return item;
        }
        internal static object CaptureObserver(ZDO data, long source, long destination, uint sourceRevision, ZDOID realTarget,
            Vector3 sourceVelocity, Vector3 sourceDirection, bool flight, float window, float sourceRotationFactor)
        {
            if (!MobVisualCurve.Finite(sourceRotationFactor) || sourceRotationFactor < 0f || sourceRotationFactor > 1f) return null;
            Track item = CaptureObserver(data, source, destination, sourceRevision, realTarget, sourceVelocity, sourceDirection, flight, window) as Track;
            if (item != null) item.FlightFactor = sourceRotationFactor;
            return item;
        }
        internal static void CommitObserver(object state) { AfterCommit(state as Track); }
        private static Track CaptureModel(ZDO data, long destination, uint sourceRevision, Vector3 velocity, Vector3 direction,
            Character target, bool fresh, bool flight, float window)
        {
            if (SkipPresentation != null && SkipPresentation(data)) return null;
            ZNetView view = ZNetScene.instance.FindInstance(data); Character actor = view ? view.GetComponent<Character>() : null;
            if (!actor || actor is Player || Busy(actor)) return null;
            GameObject model = actor.GetVisual(); Transform visual = model ? model.transform : null;
            // Modded models with gameplay bodies inside their visual hierarchy
            // retain native rendering. No clone/reparent/physics proxy is made.
            if (!visual || visual == actor.transform || !visual.IsChildOf(actor.transform) ||
                model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                model.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                model.GetComponentsInChildren<ZNetView>(true).Length != 0) return null;
            Vector3 position = actor.transform.position;
            Quaternion rotation = actor.transform.rotation;
            if (!MobVisualCurve.Finite(position) || !MobVisualCurve.Finite(velocity) || !MobVisualCurve.Finite(rotation)) return null;
            // Follow the actual native movement/look intent, including fleeing
            // and path turns; do not run or guess a second AI decision.
            direction.y = 0f;
            Quaternion aim = direction.sqrMagnitude > .0001f ? Quaternion.LookRotation(direction) : actor.GetLookYaw();
            if (!MobVisualCurve.Finite(direction) || !MobVisualCurve.Finite(aim)) return null;
            BaseAI ai = view.GetComponent<BaseAI>();
            ZDOID targetId = ZDOID.None;
            if (fresh) aim = rotation; // A lost/unloaded target has no new yaw goal.
            if (target && !target.IsDead())
            {
                ZNetView targetView = target.GetComponent<ZNetView>(); ZDO targetData = targetView && targetView.IsValid() ? targetView.GetZDO() : null;
                Vector3 targetPosition = target.transform.position;
                if (targetData != null && targetData.IsValid() && ReferenceEquals(world.GetZDO(targetData.m_uid), targetData))
                {
                    targetId = targetData.m_uid;
                }
                Vector3 towards = targetPosition - position; towards.y = 0f;
                if (MobVisualCurve.Finite(towards) && towards.sqrMagnitude > .0001f) aim = Quaternion.LookRotation(towards);
            }
            float rate = flight ? actor.m_flyTurnSpeed : actor.IsRunning() ? actor.m_runTurnSpeed : actor.m_turnSpeed;
            if (!MobVisualCurve.Finite(rate)) return null;
            float factor = flight ? flightFactor(actor) : 0f;
            if (!MobVisualCurve.Finite(factor)) return null;
            float baseRate = Math.Max(0f, Math.Min(720f, rate)), desired = (float)Math.Sqrt(Math.Min(1f, Math.Abs(MobVisualCurve.Yaw(rotation, aim)) / 90f));
            rate = baseRate * desired;
            // Presentation is read-only. SyncNow can move a just-acquired
            // native root; the existing handoff path publishes actual state.
            Vector3 motion = Vector3.ClampMagnitude(velocity, 20f);
            float speed = flight ? (actor.IsRunning() ? actor.m_flyFastSpeed : actor.m_flySlowSpeed) :
                actor.IsRunning() ? actor.m_runSpeed : actor.m_speed;
            if (!MobVisualCurve.Finite(speed)) return null;
            Vector3 horizontal = motion; horizontal.y = 0f;
            // A target assignment can precede the new movement decision. Do
            // not carry a previous target's dash/path into that new decision.
            Vector3 intent = fresh || direction.sqrMagnitude < .0001f ? new Vector3() :
                direction.normalized * Math.Min(horizontal.magnitude, Math.Max(0f, speed));
            return new Track { Data = data, View = view, Actor = actor, Visual = visual,
                Destination = destination, Revision = sourceRevision,
                Origin = position, LastRoot = position,
                Motion = motion, Intent = intent, OriginRotation = rotation,
                LastRotation = rotation, Aim = aim, Fade = 1f,
                Window = window, TurnSpeed = rate, Flight = flight, Ai = ai, Target = target, TargetId = targetId,
                TargetAim = fresh && target && !target.IsDead(), CheckTarget = fresh && target && ai && ReferenceEquals(ai.GetTargetCreature(), target),
                FlightFactor = factor, FlightDesired = desired, BaseTurnSpeed = baseRate };
        }
        private static void AfterCommit(Track __state)
        {
            try { Accepted(__state); }
            catch (Exception error) { Stop(); Plugin.Report("Mob visual bridge stopped; native handoff retained: " + error.GetBaseException().Message); }
        }
        private static void Accepted(Track item)
        {
            if (item == null || item.Begun || !Current() || !item.View || !item.Data.IsValid() ||
                !ReferenceEquals(world.GetZDO(item.Data.m_uid), item.Data) || item.Data.GetOwner() != item.Destination ||
                item.Revision >= UInt16.MaxValue || item.Data.OwnerRevision != item.Revision + 1U) return;
            item.Begun = true;
            if (item.Target) item.TargetPoint = item.Target.transform.position;
            item.WindowGoal = item.Window;
            item.HorizonGoal = item.TargetHorizon = .5f * item.Window;
            Restore();
            for (int i = tracks.Count - 1; i >= 0; i--) if (ReferenceEquals(tracks[i].Data, item.Data)) tracks.RemoveAt(i);
            if (tracks.Count >= Limit) tracks.RemoveAt(0);
            item.Started = item.Last = Time.realtimeSinceStartupAsDouble; item.DataRevision = item.Data.DataRevision;
            bool subscribe = tracks.Count == 0; tracks.Add(item);
            if (subscribe) { Camera.onPreCull += BeforeRender; Camera.onPostRender += AfterRender; }
        }
        internal static void Tick()
        {
            try { AdvanceAll(); }
            catch (Exception error) { Stop(); Plugin.Report("Mob visual playback stopped; native simulation retained: " + error.GetBaseException().Message); }
        }
        private static void AdvanceAll()
        {
            if (tracks.Count == 0) return;
            // Also restores after a camera abort, before any next gameplay tick.
            Restore();
            if (!ReferenceEquals(network, ZNet.instance) || !ReferenceEquals(world, ZDOMan.instance)) { Stop(); return; }
            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                Track item = tracks[i];
                if (!Advance(item, now)) tracks.RemoveAt(i);
            }
            if (tracks.Count == 0) Unsubscribe();
        }
        private static bool Advance(Track item, double now)
        {
            if (!item.Actor || !item.Visual || !item.View || !item.View.IsValid() || !item.Data.IsValid() ||
                !ReferenceEquals(item.View.GetZDO(), item.Data) || item.Data.GetOwner() != item.Destination ||
                item.Data.OwnerRevision != item.Revision + 1U || Busy(item.Actor)) return false;
            float age = (float)(now - item.Started), dt = (float)(now - item.Last);
            if (age < 0f || age > 2f || dt < 0f || dt > .25f) return false;
            // New measurements change presentation gradually. No extra work
            // once timing is settled; no deadline restart or gameplay delay.
            if (dt > 0f && (Math.Abs(item.TargetHorizon - item.HorizonGoal) > .00001f || !item.HasState && Math.Abs(item.Window - item.WindowGoal) > .00001f))
            {
                float factor = 1f - (float)Math.Exp(-dt / .1f);
                item.TargetHorizon += (item.HorizonGoal - item.TargetHorizon) * factor;
                if (!item.HasState) item.Window += (item.WindowGoal - item.Window) * factor;
            }
            Vector3 root = item.Actor.transform.position; Quaternion rotation = item.Actor.transform.rotation;
            if (!MobVisualCurve.Finite(root) || !MobVisualCurve.Finite(rotation) || (root - item.LastRoot).sqrMagnitude > 25f) return false;
            bool changed = !item.Observer && item.Flight != item.Actor.IsFlying() || item.TargetAim &&
                (!item.Target || item.Target.IsDead() || !item.TargetId.IsNone() && item.Target.GetZDOID() != item.TargetId ||
                item.CheckTarget && item.Ai && !ReferenceEquals(item.Ai.GetTargetCreature(), item.Target));
            if (changed && !item.TargetChanged)
            {
                item.TargetChanged = true; item.HasState = true; item.Received = now;
                item.Blend = Math.Max(MobVisualCurve.Recovery(Math.Min(age, item.Window), item.Offset.magnitude, item.Actor.GetVelocity().magnitude),
                    MobVisualCurve.Recovery(Math.Min(age, item.Window), Math.Abs(item.Turn), item.TurnSpeed));
                item.Flight = item.Actor.IsFlying();
            }
            if (item.TargetAim && !item.TargetChanged && item.Target && dt > 0f)
            {
                float duration = item.HasState ? (float)(item.Received - item.Started) + item.Blend : item.Window + MobVisualCurve.Recovery(item.Window);
                float weight = item.FutureWeight = Math.Max(item.FutureWeight, MobVisualCurve.FutureWeight(age, duration));
                Vector3 wanted = item.Target.transform.position;
                ZNetView targetView = item.Target.GetComponent<ZNetView>();
                if (targetView && targetView.IsValid() && !targetView.IsOwner() && !ReferenceEquals(item.Target, Player.m_localPlayer))
                    wanted += MobVisualCurve.ProgressiveTargetLead(item.Target.GetVelocity(), item.TargetHorizon, weight);
                float blend = 1f - (float)Math.Exp(-dt / Math.Max(.03f, Math.Min(.15f, item.Window * .3f)));
                item.TargetPoint += (wanted - item.TargetPoint) * blend;
                Vector3 towards = item.TargetPoint - root; towards.y = 0f;
                if (MobVisualCurve.Finite(towards) && towards.sqrMagnitude > .0001f)
                    item.Aim = Quaternion.RotateTowards(item.Aim, Quaternion.LookRotation(towards), Math.Max(30f, item.BaseTurnSpeed) * dt);
            }
            if (item.Flight)
            {
                float speed = item.Actor.GetVelocity().y;
                if (!MobVisualCurve.Finite(speed)) return false;
                item.Height = MobVisualCurve.HeightCorrection(item.Height * (float)Math.Exp(-3f * dt / MobVisualCurve.Recovery(item.Window)),
                    root.y - item.LastRoot.y, Math.Max(Math.Abs(item.Motion.y), Math.Abs(speed)), dt);
            }
            if (!item.HasState && (item.Data.DataRevision != item.DataRevision || age >= item.Window))
            {
                item.HasState = true;
                item.Received = item.Data.DataRevision != item.DataRevision ? now : item.Started + item.Window;
                // Only this handoff's delay matters. A short reply shortens
                // recovery; a lost packet never extends prediction indefinitely.
                item.Blend = MobVisualCurve.Recovery(Math.Min(age, item.Window), item.Offset.magnitude,
                    item.Actor.GetVelocity().magnitude);
                item.Blend = Math.Max(item.Blend, MobVisualCurve.Recovery(Math.Min(age, item.Window), Math.Abs(item.Turn), item.TurnSpeed));
            }
            if (!item.HasState && item.Observer && item.RemoteFrame)
            {
                if (now - item.RemoteAt > .2)
                { item.HasState = true; item.Received = now; item.Blend = MobVisualCurve.Recovery(item.Window); }
                else
                {
                    float factor = 1f - (float)Math.Exp(-dt / .06f);
                    item.Offset += (Vector3.ClampMagnitude(item.RemotePosition - root, .25f) - item.Offset) * factor;
                    Quaternion wanted = Quaternion.AngleAxis(item.RemoteYaw, new Vector3(0f, 1f, 0f));
                    float turn = Math.Max(-10f, Math.Min(10f, MobVisualCurve.Yaw(rotation, wanted)));
                    item.Turn += (turn - item.Turn) * factor;
                }
            }
            else if (!item.HasState)
            {
                Predict(item, root, rotation, age);
            }
            else
            {
                // Consume real native progress, never differentiate correction
                // steps into velocity. The root remains the only movement path.
                Vector3 nativeVelocity = item.Actor.GetVelocity();
                if (!MobVisualCurve.Finite(nativeVelocity)) return false;
                item.Offset = MobVisualCurve.Reconcile(item.Offset, root - item.LastRoot, nativeVelocity.magnitude, dt);
                item.Turn = MobVisualCurve.ReconcileTurn(item.Turn, MobVisualCurve.Yaw(item.LastRotation, rotation), item.TurnSpeed, dt);
                float fade = MobVisualCurve.Fade((float)(now - item.Received), item.Blend);
                float factor = item.Fade > 0f ? fade / item.Fade : 0f;
                item.Offset = item.Offset * factor; item.Turn *= factor; item.Fade = fade;
                if (fade <= 0f) return false;
            }
            item.Last = now; item.LastRoot = root; item.LastRotation = rotation;
            if (!item.Observer && !item.HasState && SourcePreview != null)
                SourcePreview(item.Data, root + item.Offset + new Vector3(0f, item.Height, 0f), MobVisualCurve.Yaw(Quaternion.identity, MobVisualCurve.TurnOffset(item.Turn) * rotation));
            return true;
        }
        internal static void ReceivePreview(ZDO data, long source, long destination, uint revision, Vector3 position, float yaw)
        {
            if (!MobVisualCurve.Finite(position) || !MobVisualCurve.Finite(yaw) || source == ZDOMan.GetSessionID() || destination == ZDOMan.GetSessionID()) return;
            foreach (Track item in tracks)
                if (item.Observer && !item.HasState && ReferenceEquals(item.Data, data) && item.Revision == revision && item.Destination == destination &&
                    data.OwnerRevision == revision + 1U && data.GetOwner() == destination && (position - item.Actor.transform.position).sqrMagnitude < 25f)
                { item.RemoteFrame = true; item.RemotePosition = position; item.RemoteYaw = yaw; item.RemoteAt = Time.realtimeSinceStartupAsDouble; }
        }
        internal static void TargetTiming(ZDO data, uint revision, float seconds)
        {
            if (!MobVisualCurve.Finite(seconds)) return;
            foreach (Track item in tracks) if (!item.Observer && ReferenceEquals(item.Data, data) && item.Revision == revision)
            {
                item.HorizonGoal = Math.Max(0f, Math.Min(.5f, seconds));
                if (!item.HasState) item.WindowGoal = MobVisualCurve.Window(2f * seconds);
            }
        }
        private static void Predict(Track item, Vector3 root, Quaternion rotation, float age)
        {
            // A lower RTT may shorten the bridge but cannot rewind its preview.
            float prediction = item.SimTime = Math.Max(item.SimTime, MobVisualCurve.PredictionTime(age, item.Window));
            // Add only the lead not already supplied by native extrapolation.
            // Never hold the model behind a root that is moving normally.
            Vector3 motion = item.Motion, progress = root - item.Origin;
            // Flying replicas already extrapolate native height. Preserve that
            // movement, including reversals, rather than add another Y path.
            motion.y = 0f; progress.y = 0f;
            item.Offset = MobVisualCurve.Lead(MobVisualCurve.Displacement(motion, item.Intent, prediction), progress);
            float turn = item.Flight ? MobVisualCurve.FlightTurn(item.BaseTurnSpeed, item.FlightFactor, item.FlightDesired, prediction) : item.TurnSpeed * prediction;
            item.Turn = MobVisualCurve.TurnLead(item.OriginRotation, rotation, item.Aim, turn);
        }
        private static void BeforeRender(Camera camera)
        {
            try { ApplyRender(camera); }
            catch (Exception error) { Stop(); Plugin.Report("Mob visual rendering stopped; native simulation retained: " + error.GetBaseException().Message); }
        }
        private static void ApplyRender(Camera camera)
        {
            // Water/reflection cameras may render inside another camera. Keep
            // the same visual offset until the outermost render has finished.
            if (rendering.Count != 0)
            {
                if (rendering.Count >= 64) { Restore(); return; }
                rendering.Add(camera); return;
            }
            Restore(); rendering.Add(camera);
            foreach (Track item in tracks)
            {
                if (!item.Actor || !item.Visual || !item.View || !item.View.IsValid() ||
                    item.Data.GetOwner() != item.Destination || item.Data.OwnerRevision != item.Revision + 1U ||
                    Busy(item.Actor) || !item.Visual.gameObject.activeInHierarchy || !item.Visual.IsChildOf(item.Actor.transform)) continue;
                Transform root = item.Actor.transform;
                item.SavedPosition = item.Visual.localPosition; item.SavedRotation = item.Visual.localRotation; item.Applied = true;
                // A native transform update after LateUpdate still wins. Do
                // not render an absolute pose saved earlier in the frame.
                float since = (float)Math.Max(0, Time.realtimeSinceStartupAsDouble - item.Last);
                Vector3 lead = item.HasState ? MobVisualCurve.Reconcile(item.Offset, root.position - item.LastRoot,
                    item.Actor.GetVelocity().magnitude, since) : MobVisualCurve.Consume(item.Offset, root.position - item.LastRoot);
                if (item.Flight)
                {
                    float speed = item.Actor.GetVelocity().y;
                    if (!MobVisualCurve.Finite(speed)) speed = item.Motion.y;
                    lead.y = MobVisualCurve.HeightCorrection(item.Height, root.position.y - item.LastRoot.y,
                        Math.Max(Math.Abs(item.Motion.y), Math.Abs(speed)), (float)Math.Max(0, Time.realtimeSinceStartupAsDouble - item.Last));
                }
                float step = MobVisualCurve.Yaw(item.LastRotation, root.rotation);
                float turn = item.HasState ? MobVisualCurve.ReconcileTurn(item.Turn, step, item.TurnSpeed, since) : MobVisualCurve.ConsumeTurn(item.Turn, step);
                if (!item.TargetChanged) turn = MobVisualCurve.BoundTurn(turn, root.rotation, item.Aim);
                Quaternion offset = MobVisualCurve.TurnOffset(turn);
                Vector3 position = root.position + lead + offset * (item.Visual.position - root.position);
                item.Visual.SetPositionAndRotation(position, offset * item.Visual.rotation);
            }
        }
        private static void AfterRender(Camera camera)
        {
            if (rendering.Count == 0) return;
            if (!ReferenceEquals(rendering[rendering.Count - 1], camera)) { Restore(); return; }
            rendering.RemoveAt(rendering.Count - 1);
            if (rendering.Count == 0) Restore();
        }
        private static void Restore()
        {
            foreach (Track item in tracks)
            {
                if (!item.Applied) continue;
                if (item.Visual) { item.Visual.localPosition = item.SavedPosition; item.Visual.localRotation = item.SavedRotation; }
                item.Applied = false;
            }
            rendering.Clear();
        }
        private static void Unsubscribe() { Camera.onPreCull -= BeforeRender; Camera.onPostRender -= AfterRender; rendering.Clear(); }
    }
}
