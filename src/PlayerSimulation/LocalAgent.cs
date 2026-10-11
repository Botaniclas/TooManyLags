using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.PlayerSimulation
{
    internal sealed class LocalAgent
    {
        private sealed class Ticket
        {
            internal long Token, Source, Target;
            internal ZDOID Id, MobTarget;
            internal ushort Revision;
            internal Kinds Kind;
            internal float Until;
            internal bool Adopted;
        }
        private sealed class Lease { internal ushort Revision; internal float Until; }
        private sealed class Busy { internal Ticket Ticket; internal float Check; }
        private sealed class Forward { internal long Owner; internal ushort Revision; internal float Until; internal Kinds Kind; }
        private sealed class Replay { internal int Result; internal float Until; }
        private readonly Dictionary<ZDOID, Ticket> prepared = new Dictionary<ZDOID, Ticket>();
        private readonly Dictionary<ZDOID, Busy> blocked = new Dictionary<ZDOID, Busy>();
        private readonly Dictionary<ZDOID, Lease> refill = new Dictionary<ZDOID, Lease>();
        private readonly Dictionary<ZDOID, float> opening = new Dictionary<ZDOID, float>();
        private readonly Dictionary<ZDOID, Forward> forward = new Dictionary<ZDOID, Forward>();
        private readonly Dictionary<long, Replay> replay = new Dictionary<long, Replay>();
        private readonly List<ZDOID> ids = new List<ZDOID>();
        private readonly List<long> tokens = new List<long>();
        private readonly Dictionary<Vector2s, bool> areaReady = new Dictionary<Vector2s, bool>();
        private readonly ConditionalWeakTable<ZNetView, ViewState> views = new ConditionalWeakTable<ZNetView, ViewState>();
        private static readonly ConditionalWeakTable<ZNetView, Published> published = new ConditionalWeakTable<ZNetView, Published>();
        private sealed class Published { internal long Owner; internal ushort Revision; }
        private sealed class ViewState
        {
            internal Kinds Kind;
            internal Character Actor;
            internal MonsterAI Ai;
            internal Tameable Animal;
            internal Vagon Cart;
            internal Container Container;
            internal int Inventories;
        }
        private readonly OwnershipCadence cadence = new OwnershipCadence(.1f);
        private int readinessFrame = -1;
        private float nextSweep;
        private bool mobWatch;
        internal bool MobsEnabled = true;
        internal bool HasWork { get { return prepared.Count + blocked.Count + refill.Count + opening.Count + forward.Count + replay.Count != 0; } }
        private static readonly Action<MonsterAI, float> sensed = FloatSetter();
        private static readonly Func<BaseAI, ZNetView> aiView = FieldReader.Create<BaseAI, ZNetView>(AccessTools.Field(typeof(BaseAI), "m_nview"));
        private static readonly Func<MonsterAI, Character> aiTarget = FieldReader.Create<MonsterAI, Character>(AccessTools.Field(typeof(MonsterAI), "m_targetCreature"));
        private static readonly Action<BaseAI, bool> alerted = AccessTools.MethodDelegate<Action<BaseAI, bool>>(AccessTools.Method(typeof(BaseAI), "SetAlerted"));
        private static readonly Func<InventoryGui, Container> currentInventory = FieldReader.Create<InventoryGui, Container>(AccessTools.Field(typeof(InventoryGui), "m_currentContainer"));
        private static Action<MonsterAI, float> FloatSetter()
        {
            var field = AccessTools.Field(typeof(MonsterAI), "m_timeSinceSensedTargetCreature");
            if (field == null || field.FieldType != typeof(float)) throw new NotSupportedException("Native monster target timer unavailable.");
            DynamicMethod method = new DynamicMethod("TMLS_ResetSensedTarget", typeof(void), new[] { typeof(MonsterAI), typeof(float) }, typeof(LocalAgent).Module, true);
            ILGenerator il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
            return (Action<MonsterAI, float>)method.CreateDelegate(typeof(Action<MonsterAI, float>));
        }
        private ViewState State(ZNetView view)
        {
            return views.GetValue(view, delegate(ZNetView value)
            {
                Container[] inventories = value.GetComponentsInChildren<Container>(true);
                return new ViewState { Kind = ObjectCatalog.Classify(value.gameObject), Actor = value.GetComponent<Character>(), Ai = value.GetComponent<MonsterAI>(),
                    Animal = value.GetComponent<Tameable>(), Cart = value.GetComponent<Vagon>(), Container = inventories.Length == 1 ? inventories[0] : null, Inventories = inventories.Length };
            });
        }
        private static Ticket Read(ZPackage p)
        { return new Ticket { Token = p.ReadLong(), Id = p.ReadZDOID(), Source = p.ReadLong(), Target = p.ReadLong(), Revision = p.ReadUShort(), Kind = (Kinds)p.ReadInt(), MobTarget = p.ReadZDOID(), Until = Time.unscaledTime + 8f }; }
        internal void Receive(Message message, ZPackage packet)
        {
            if (ZDOMan.instance == null || !ZNetScene.instance) return;
            if (message == Message.Cancel)
            {
                long token = packet.ReadLong(); ZDOID id = packet.ReadZDOID(); Ticket current;
                // A committed owner packet may still be travelling. Its snapshot
                // expires naturally; cancellation must not discard adoption.
                if (prepared.TryGetValue(id, out current) && current.Token == token) current.Until = Math.Min(current.Until, Time.unscaledTime + 2f);
                Replay old; if (!replay.TryGetValue(token, out old) || old.Result != 1) replay[token] = new Replay { Result = 0, Until = Time.unscaledTime + 15f };
                Busy waiting; if (blocked.TryGetValue(id, out waiting) && waiting.Ticket.Token == token) blocked.Remove(id);
                return;
            }
            Ticket ticket = Read(packet); long me = ZDOMan.GetSessionID();
            if (ticket.Token <= 0 || ticket.Source == 0 || ticket.Target == 0 || ticket.Source == ticket.Target || ticket.Revision == UInt16.MaxValue) return;
            if (message == Message.ActivationCheck)
            {
                if (ticket.Target != me) return;
                ZDO data = ZDOMan.instance.GetZDO(ticket.Id); ZNetView view = data != null ? ZNetScene.instance.FindInstance(data) : null;
                if (data == null || !view || !view.IsValid() || !view.gameObject.activeInHierarchy || !AreaReady(data.GetPosition())) return;
                if (data.GetOwner() == me && data.OwnerRevision == ticket.Revision + 1)
                {
                    if (ticket.Kind == Kinds.None && data.GetLong(MobPolicy.WriterKey, 0) != me) Adopt(data, ticket.MobTarget);
                    PublishOwned(data); Activation(ticket, data.OwnerRevision); return;
                }
                if (data.GetOwner() == ticket.Source && data.OwnerRevision == ticket.Revision && prepared.Count < 4096) prepared[ticket.Id] = ticket;
                return;
            }
            if (message == Message.Prepare)
            {
                if (ticket.Target != me) return;
                ZDO data = ZDOMan.instance.GetZDO(ticket.Id); ZNetView view = data != null ? ZNetScene.instance.FindInstance(data) : null;
                bool ready = data != null && data.IsValid() && data.GetOwner() == ticket.Source && data.OwnerRevision == ticket.Revision && view && view.IsValid() && view.gameObject.activeInHierarchy && AreaReady(data.GetPosition());
                if (ready && ticket.Kind == Kinds.None) { Character target = TargetInstance(ticket.MobTarget); ready = MobsEnabled && State(view).Ai && MobPolicy.Eligible(data) && target is Player && !target.IsDead(); }
                if (ready && prepared.Count < 4096) prepared[ticket.Id] = ticket; else ready = false;
                Reply(Message.Prepared, ticket, delegate(ZPackage p) { p.Write(ready); }); return;
            }
            if (message != Message.Commit || ticket.Source != me) return;
            Replay prior;
            if (replay.TryGetValue(ticket.Token, out prior)) { ReplyResult(ticket, prior.Result); return; }
            ZDO source = ZDOMan.instance.GetZDO(ticket.Id); ZNetView sourceView = source != null ? ZNetScene.instance.FindInstance(source) : null;
            float until; int result = Check(ticket, source, sourceView, out until);
            if (result == 1)
            {
                // Source-side commit publishes the latest object state and stops
                // local simulation through the normal native owner revision.
                ZDOMan.instance.ForceSendZDO(ticket.Id); source.SetOwner(ticket.Target);
                forward[ticket.Id] = new Forward { Owner = ticket.Target, Revision = source.OwnerRevision, Until = Time.unscaledTime + 5f, Kind = ticket.Kind };
                blocked.Remove(ticket.Id);
                replay[ticket.Token] = new Replay { Result = 1, Until = Time.unscaledTime + 10f };
            }
            else if (result == 2 && blocked.Count < 4096) blocked[ticket.Id] = new Busy { Ticket = ticket, Check = until };
            ReplyResult(ticket, result);
        }
        private int Check(Ticket ticket, ZDO data, ZNetView view, out float until)
        {
            until = Single.PositiveInfinity;
            if (data == null || !data.IsValid() || data.GetOwner() != ticket.Source || data.OwnerRevision != ticket.Revision ||
                !view || !view.IsValid() || !ReferenceEquals(view.GetZDO(), data) || !view.gameObject.activeInHierarchy || data.OwnerRevision == UInt16.MaxValue) return 0;
            ViewState state = State(view);
            if (ticket.Kind == Kinds.None)
            {
                Character target = state.Ai ? TargetEvents.Read(state.Ai) : null;
                if (!MobsEnabled || !state.Ai || !state.Actor || !MobPolicy.Eligible(data) || state.Actor.IsDead() || state.Actor.IsBoss() || !(target is Player) || target.IsDead() || target.GetZDOID() != ticket.MobTarget) return 0;
            }
            else if (state.Kind != ticket.Kind || state.Kind == Kinds.Incinerators || state.Inventories > 1) return 3;
            if (state.Actor && (state.Actor.InAttack() || state.Actor.IsStaggering() || (ticket.Kind == Kinds.TamedAnimals && state.Ai && aiTarget(state.Ai))))
            { until = Time.unscaledTime + .1f; return 2; }
            if (data.GetLong(ZDOVars.s_user, 0) != 0 || (state.Cart && state.Cart.InUse())) return 2;
            if (state.Animal && ObjectCatalog.IsSummoned(state.Animal)) return 3;
            if (state.Animal && (state.Animal.HaveRider() || (state.Animal.m_commandable && !String.IsNullOrEmpty(data.GetString(ZDOVars.s_follow))))) { until = Time.unscaledTime + .1f; return 2; }
            if (state.Actor && (FriendlyCreatures.IsFriendly(state.Actor) || state.Actor.IsDead())) return 3;
            if (state.Actor && (state.Actor.IsAttached() || state.Actor.IsRiding())) { until = Time.unscaledTime + .1f; return 2; }
            float pending;
            if ((opening.TryGetValue(ticket.Id, out pending) && pending > Time.unscaledTime) || (state.Container && (state.Container.IsInUse() || data.GetInt(ZDOVars.s_inUse) != 0 || (InventoryGui.instance && currentInventory(InventoryGui.instance) == state.Container)))) return 2;
            Lease lease;
            if (refill.TryGetValue(ticket.Id, out lease) && lease.Revision == data.OwnerRevision && lease.Until > Time.unscaledTime) { until = lease.Until; return 2; }
            return 1;
        }
        private bool AreaReady(Vector3 point)
        {
            if (readinessFrame != Time.frameCount) { readinessFrame = Time.frameCount; areaReady.Clear(); }
            Vector2s zone = ZoneSystem.GetZone(point); bool ready;
            if (!areaReady.TryGetValue(zone, out ready)) { ready = ZNetScene.instance.IsAreaReady(point); areaReady.Add(zone, ready); }
            return ready;
        }
        private static Character TargetInstance(ZDOID id)
        { GameObject root = !id.IsNone() && ZNetScene.instance ? ZNetScene.instance.FindInstance(id) : null; return root ? root.GetComponent<Character>() : null; }
        internal void Observe(ZDO data, bool final = true)
        {
            if (data == null || !data.IsValid() || ZDOMan.instance == null) return;
            Ticket ticket;
            if (prepared.TryGetValue(data.m_uid, out ticket) && data.GetOwner() == ZDOMan.GetSessionID() &&
                (data.OwnerRevision == ticket.Revision || data.OwnerRevision == ticket.Revision + 1))
            {
                if (!ticket.Adopted && ticket.Kind == Kinds.None) { Adopt(data, ticket.MobTarget); ticket.Adopted = true; }
                if (final && data.OwnerRevision == ticket.Revision + 1) { prepared.Remove(data.m_uid); PublishOwned(data); Activation(ticket, data.OwnerRevision); }
            }
            if (blocked.ContainsKey(data.m_uid)) UseEnded(data.m_uid);
            if (final && data.GetOwner() == ZDOMan.GetSessionID() && MobsEnabled && mobWatch && MobPolicy.HostilePrefab(data.GetPrefab()))
            {
                ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
                MonsterAI ai = view ? view.GetComponent<MonsterAI>() : null;
                if (ai && Watched(ai))
                {
                    if (data.GetLong(MobPolicy.WriterKey, 0) != ZDOMan.GetSessionID()) Adopt(data, data.GetZDOID(MobPolicy.TargetKey));
                    PublishOwned(data);
                }
            }
        }
        private void Adopt(ZDO data, ZDOID targetId)
        {
            ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null; MonsterAI ai = view ? view.GetComponent<MonsterAI>() : null;
            Character target = TargetInstance(targetId), actor = view ? view.GetComponent<Character>() : null;
            // Adapted from FGN TargetSync's source-owner target adoption (MIT).
            if (!MobsEnabled || !ai || !actor || actor.IsTamed() || !target || target.IsDead() || !BaseAI.IsEnemy(actor, target) || (target.transform.position - ai.transform.position).sqrMagnitude > 3600f) return;
            TargetEvents.Adopt(ai, target); sensed(ai, 0); alerted(ai, true);
        }
        internal bool Watched(BaseAI ai)
        {
            if (!MobsEnabled || !mobWatch || !(ai is MonsterAI)) return false;
            ZNetView view = aiView(ai); if (!view || !view.IsValid() || !view.IsOwner()) return false;
            Character actor = ai.GetComponent<Character>();
            return actor && !actor.IsBoss() && !actor.IsDead() && !FriendlyCreatures.IsFriendly(actor) && !actor.IsTamed();
        }
        internal void TargetChanged(BaseAI ai)
        {
            if (!Watched(ai)) return;
            ZDO data = aiView(ai).GetZDO(); Character target = TargetEvents.Read((MonsterAI)ai);
            ZDOID id = target is Player && !target.IsDead() ? target.GetZDOID() : ZDOID.None;
            long owner = data.GetOwner(); ushort revision = data.OwnerRevision;
            if (data.GetZDOID(MobPolicy.TargetKey) != id || data.GetLong(MobPolicy.WriterKey, 0) != owner || data.GetLong(MobPolicy.RevisionKey, -1) != revision)
            { data.Set(MobPolicy.TargetKey, id); data.Set(MobPolicy.WriterKey, owner); data.Set(MobPolicy.RevisionKey, (long)revision); }
            Published marker = published.GetOrCreateValue(aiView(ai)); marker.Owner = owner; marker.Revision = revision;
            Plugin.Network.ToServer(Message.Target, delegate(ZPackage p) { p.Write(data.m_uid); p.Write(revision); p.Write(id); });
        }
        private void PublishOwned(ZDO data)
        {
            if (data.GetOwner() != ZDOMan.GetSessionID() || !ZNetScene.instance) return;
            ZNetView view = ZNetScene.instance.FindInstance(data); MonsterAI ai = view ? view.GetComponent<MonsterAI>() : null;
            if (!ai || !Watched(ai)) return;
            Published marker = published.GetOrCreateValue(view);
            if (marker.Owner != data.GetOwner() || marker.Revision != data.OwnerRevision) TargetChanged(ai);
        }
        internal void SetMobWatch(bool value)
        {
            value &= MobsEnabled; if (mobWatch == value) return; mobWatch = value;
            if (!value) return;
            foreach (BaseAI ai in BaseAI.GetAllInstances()) if (Watched(ai)) TargetChanged(ai);
        }
        internal void Instance(ZNetView view)
        {
            if (!view || !view.IsValid()) return;
            ZDO data = view.GetZDO(); Observe(data);
            if (MobsEnabled && MobPolicy.HostilePrefab(data.GetPrefab()))
            { if (data.IsOwner() && data.GetLong(MobPolicy.WriterKey, 0) != ZDOMan.GetSessionID()) Adopt(data, data.GetZDOID(MobPolicy.TargetKey)); PublishOwned(data); }
        }
        internal void Outgoing(ZDOID id, int method)
        {
            if (ZNet.IsSinglePlayer) return;
            if (method == "RPC_RequestOpen".GetStableHashCode() || method == "RPC_RequestStack".GetStableHashCode() || method == "RPC_RequestTakeAll".GetStableHashCode()) opening[id] = Time.unscaledTime + 5f;
        }
        internal void Response(Container container)
        {
            if (!container) return; ZNetView view = container.m_rootObjectOverride ? container.m_rootObjectOverride.GetComponent<ZNetView>() : container.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) return; opening.Remove(view.GetZDO().m_uid); UseEnded(view.GetZDO().m_uid);
        }
        internal void Refilling(ZDO data, int method)
        {
            if (ZNet.IsSinglePlayer || !ObjectCatalog.IsRefillMethod(method) || data == null || !data.IsOwner() || !ZNetScene.instance) return;
            ZNetView view = ZNetScene.instance.FindInstance(data); if (!view) return;
            if (ObjectCatalog.RefillRequest(State(view).Kind, method)) refill[data.m_uid] = new Lease { Revision = data.OwnerRevision, Until = Time.unscaledTime + 10f };
        }
        internal bool MayForward(ZDO data, int method)
        {
            Forward stamp;
            if (data == null || !forward.TryGetValue(data.m_uid, out stamp) || Time.unscaledTime > stamp.Until || data.GetOwner() != stamp.Owner || data.OwnerRevision != stamp.Revision) return false;
            return stamp.Kind == Kinds.None ? method == "RPC_Damage".GetStableHashCode() : ObjectCatalog.OwnerRequest(stamp.Kind, method);
        }
        internal bool Prepared(ZDOID id) { return prepared.ContainsKey(id); }
        internal bool NeedsFinalImport(ZDO data)
        {
            if (prepared.ContainsKey(data.m_uid)) return true;
            if (!MobsEnabled || !mobWatch || data.GetOwner() != ZDOMan.GetSessionID() || !MobPolicy.HostilePrefab(data.GetPrefab())) return false;
            ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            if (!view || !view.GetComponent<MonsterAI>()) return false;
            Published marker = published.GetOrCreateValue(view);
            return marker.Owner != data.GetOwner() || marker.Revision != data.OwnerRevision || data.GetLong(MobPolicy.WriterKey, 0) != data.GetOwner();
        }
        internal bool ForwardDestination(ZDOID id, ZDO data, int method, out long owner, out ushort revision)
        {
            owner = 0; revision = 0; Forward stamp;
            if (!forward.TryGetValue(id, out stamp) || Time.unscaledTime > stamp.Until ||
                (data != null && (data.GetOwner() != stamp.Owner || data.OwnerRevision != stamp.Revision))) return false;
            if (!(stamp.Kind == Kinds.None ? method == "RPC_Damage".GetStableHashCode() : ObjectCatalog.OwnerRequest(stamp.Kind, method))) return false;
            owner = stamp.Owner; revision = stamp.Revision; return true;
        }
        internal void UseEnded(ZDOID id)
        {
            Busy busy; if (!blocked.TryGetValue(id, out busy)) return;
            ZDO data = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null; ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            float until; int result = Check(busy.Ticket, data, view, out until);
            if (result == 2) { busy.Check = until; return; }
            blocked.Remove(id); Reply(Message.Wake, busy.Ticket, delegate(ZPackage p) { });
        }
        internal void Tick(float now)
        {
            if (!cadence.Due(now)) return;
            ids.Clear(); foreach (KeyValuePair<ZDOID, Busy> item in blocked) if (now >= item.Value.Check) ids.Add(item.Key);
            foreach (ZDOID id in ids) UseEnded(id); ids.Clear();
            if (now < nextSweep) return; nextSweep = now + 2f;
            Sweep(prepared, delegate(Ticket t) { return now >= t.Until; });
            Sweep(refill, delegate(Lease t) { return now >= t.Until; });
            Sweep(opening, delegate(float t) { return now >= t; });
            Sweep(forward, delegate(Forward t) { return now >= t.Until; });
            tokens.Clear(); foreach (KeyValuePair<long, Replay> item in replay) if (now >= item.Value.Until) tokens.Add(item.Key);
            foreach (long token in tokens) replay.Remove(token); tokens.Clear();
            // Source-side veto registrations cannot outlive a disconnected
            // coordinator or an object that was destroyed/unloaded.
            ids.Clear(); foreach (KeyValuePair<ZDOID, Busy> item in blocked)
                if (ZDOMan.instance == null || ZDOMan.instance.GetZDO(item.Key) == null || now - item.Value.Ticket.Until > 10f) ids.Add(item.Key);
            foreach (ZDOID id in ids) blocked.Remove(id); ids.Clear();
        }
        private void Sweep<T>(Dictionary<ZDOID, T> source, Predicate<T> expired)
        { ids.Clear(); foreach (KeyValuePair<ZDOID, T> item in source) if (expired(item.Value)) ids.Add(item.Key); foreach (ZDOID id in ids) source.Remove(id); ids.Clear(); }
        private static void Reply(Message message, Ticket ticket, Action<ZPackage> write)
        { Plugin.Network.ToServer(message, delegate(ZPackage p) { p.Write(ticket.Token); p.Write(ticket.Id); write(p); }); }
        private static void ReplyResult(Ticket ticket, int result) { Reply(Message.Result, ticket, delegate(ZPackage p) { p.Write(result); }); }
        private static void Activation(Ticket ticket, ushort revision) { Reply(Message.Activated, ticket, delegate(ZPackage p) { p.Write(revision); }); }
        internal void Reset() { prepared.Clear(); blocked.Clear(); refill.Clear(); opening.Clear(); forward.Clear(); replay.Clear(); areaReady.Clear(); mobWatch = false; }
    }
}
