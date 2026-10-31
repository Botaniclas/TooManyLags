using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // One transient checkpoint, sent before the native ownership write. No ZDO
    // keys, extra round trip, Update callback or permanent motion packet fields.
    internal static class MobFlightHandoff
    {
        internal static Action<ZDO, long> CombatTransfer = null;
        internal static Action<ZDO, long> CombatTransferred = null;
        internal static Action<ZDO, long> SimulationTransfer = null;
        internal static Action<ZDO, long> SimulationTransferred = null;
        internal static Func<ZDO, bool> NativeTransfer = null;
        private const string Rpc = "TooManyLags_MobFlight";
        private const int ProbeMode = 23, ReadyMode = 24;
        private const int Flying = 1, Run = 2, RandomFlight = 4, Circle = 8, Wander = 16;
        private const int Limit = 256;
        private sealed class Capability { internal bool Ready; }
        private sealed class State
        {
            internal ZDOID Id, Target;
            internal long Source, Destination;
            internal uint Revision;
            internal int Prefab, Flags;
            internal float Phase, Turn, TurnChange, CircleRemaining, WanderRemaining;
            internal Vector3 Velocity, Current, Direction, CirclePoint, WanderPoint;
            internal double Received;
        }
        private sealed class Slot<T, V> where T : class
        {
            internal readonly Func<T, V> Read;
            internal readonly Action<T, V> Write;
            internal Slot(string name)
            {
                FieldInfo field = AccessTools.Field(typeof(T), name);
                if (field == null || field.FieldType != typeof(V)) throw new NotSupportedException("Native flight field changed: " + name);
                Read = FieldReader.Create<T, V>(field);
                DynamicMethod setter = new DynamicMethod("TooManyLagsFlight_" + name, typeof(void), new[] { typeof(T), typeof(V) }, typeof(MobFlightHandoff).Module, true);
                ILGenerator il = setter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
                Write = (Action<T, V>)setter.CreateDelegate(typeof(Action<T, V>));
            }
        }
        private static Slot<Character, Vector3> current, direction;
        private static Slot<Character, bool> run;
        private static Slot<Character, float> turn, turnChange;
        private static Slot<BaseAI, float> phase, circleAt, wanderFor;
        private static Slot<BaseAI, Vector3> circlePoint, wanderPoint;
        private static Slot<BaseAI, bool> wanderReached;
        private static readonly Dictionary<ZDOID, State> pending = new Dictionary<ZDOID, State>();
        private static readonly List<ZDOID> scratch = new List<ZDOID>();
        private static ConditionalWeakTable<ZRpc, Capability> capabilities = new ConditionalWeakTable<ZRpc, Capability>();
        private static ConditionalWeakTable<BaseAI, State> intents = new ConditionalWeakTable<BaseAI, State>();
        private static Harmony hooks;
        private static ZDOMan world;
        private static ZNet network;
        private static bool supported;

        internal static void Start(Assembly fgn)
        {
            Stop();
            try
            {
                current = new Slot<Character, Vector3>("m_currentVel"); direction = new Slot<Character, Vector3>("m_moveDir");
                run = new Slot<Character, bool>("m_run"); turn = new Slot<Character, float>("m_currentTurnVel");
                turnChange = new Slot<Character, float>("m_currentTurnVelChange"); phase = new Slot<BaseAI, float>("m_randomFlyTimer");
                circleAt = new Slot<BaseAI, float>("aroundPointUpdateTime"); circlePoint = new Slot<BaseAI, Vector3>("arroundPointTarget");
                wanderFor = new Slot<BaseAI, float>("m_randomMoveUpdateTimer"); wanderPoint = new Slot<BaseAI, Vector3>("m_randomMoveTarget");
                wanderReached = new Slot<BaseAI, bool>("m_reachedRandomMoveTarget");
                MethodInfo commit = AccessTools.Method(fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true), "OnHandOff", new[] { typeof(ZRpc), typeof(ZDOID), typeof(long) });
                Rewrite(PatchProcessor.GetOriginalInstructions(commit));
                hooks = new Harmony(Plugin.Id + ".mobflight");
                hooks.Patch(commit, transpiler: new HarmonyMethod(typeof(MobFlightHandoff), "Rewrite"));
                hooks.Patch(AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData"), postfix: new HarmonyMethod(typeof(MobFlightHandoff), "Replicated"));
                hooks.Patch(AccessTools.Method(typeof(BaseAI), "Awake"), postfix: new HarmonyMethod(typeof(MobFlightHandoff), "Awoke"));
                hooks.Patch(AccessTools.Method(typeof(ZNet), "Shutdown"), prefix: new HarmonyMethod(typeof(MobFlightHandoff), "Reset"));
                FgnCombatOwnership.FlightConnect = Connect; FgnCombatOwnership.FlightProbe = Probe;
                FgnCombatOwnership.FlightMode = Mode; FgnCombatOwnership.FlightTransfer = Transfer;
                TamedMobHandoff.FlightTransfer = Transfer; PlayerSimulation.LocalAgent.FlightTransfer = Transfer;
                FgnTargetEvents.FlightAdopt = Adopted; PlayerSimulation.TargetEvents.FlightAdopt = Adopted;
            }
            catch (Exception error) { Stop(); Plugin.Report("Flight-state handoff unavailable; native ownership retained: " + error.GetBaseException().Message); }
        }
        internal static void Stop()
        {
            if (hooks != null) hooks.UnpatchSelf(); hooks = null;
            FgnCombatOwnership.FlightConnect = FgnCombatOwnership.FlightProbe = null;
            FgnCombatOwnership.FlightMode = null; FgnCombatOwnership.FlightTransfer = null;
            TamedMobHandoff.FlightTransfer = null; PlayerSimulation.LocalAgent.FlightTransfer = null;
            FgnTargetEvents.FlightAdopt = null; PlayerSimulation.TargetEvents.FlightAdopt = null;
            SimulationTransfer = null; SimulationTransferred = null;
            Reset();
        }
        private static void Reset()
        {
            pending.Clear(); scratch.Clear(); capabilities = new ConditionalWeakTable<ZRpc, Capability>();
            intents = new ConditionalWeakTable<BaseAI, State>(); world = null; network = null; supported = false;
        }
        private static void Context()
        {
            if (ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance)) return;
            pending.Clear(); scratch.Clear(); intents = new ConditionalWeakTable<BaseAI, State>();
            world = ZDOMan.instance; network = ZNet.instance;
        }
        private static void Probe(ZNetPeer peer)
        { if (hooks != null && peer != null && peer.IsReady()) peer.m_rpc.Invoke("TooManyLags_AI_Mode", ProbeMode); }
        private static bool Mode(ZNetPeer peer, int mode)
        {
            if (hooks == null || !ZNet.instance || peer == null || !peer.IsReady()) return false;
            if (mode == ProbeMode && ZNet.instance.IsServer())
            { capabilities.GetOrCreateValue(peer.m_rpc).Ready = true; peer.m_rpc.Invoke("TooManyLags_AI_Mode", ReadyMode); return true; }
            if (mode != ReadyMode || ZNet.instance.IsServer()) return false;
            if (peer.m_server && ReferenceEquals(peer, ZNet.instance.GetServerPeer())) supported = true;
            return true;
        }
        private static bool Capable(ZNetPeer peer)
        { Capability item; return peer != null && peer.IsReady() && peer.m_rpc != null && capabilities.TryGetValue(peer.m_rpc, out item) && item.Ready; }
        private static void Connect(ZNetPeer peer)
        {
            if (hooks == null || peer == null || peer.m_rpc == null) return;
            peer.m_rpc.Register<ZDOID, ZPackage>(Rpc, delegate(ZRpc rpc, ZDOID id, ZPackage payload)
            {
                try { Receive(peer, rpc, id, payload); }
                catch (Exception error) { Plugin.Warn("Flight checkpoint ignored: " + error.GetBaseException().Message); }
            });
        }
        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        private static bool Motion(Vector3 value) { return Finite(value) && value.sqrMagnitude <= 40000f; }
        private static bool Valid(State item)
        {
            return item.Source != 0 && item.Destination != 0 && item.Source != item.Destination && item.Revision < UInt16.MaxValue &&
                (item.Flags & ~31) == 0 && Finite(item.Phase) && item.Phase >= 0 && Finite(item.Turn) && Finite(item.TurnChange) &&
                Motion(item.Velocity) && Motion(item.Current) && Motion(item.Direction) && item.Direction.sqrMagnitude <= 4f &&
                ((item.Flags & Circle) == 0 || (Finite(item.CirclePoint) && Finite(item.CircleRemaining) && item.CircleRemaining > 0 && item.CircleRemaining <= 3600f)) &&
                ((item.Flags & Wander) == 0 || (Finite(item.WanderPoint) && Finite(item.WanderRemaining) && item.WanderRemaining > 0 && item.WanderRemaining <= 3600f));
        }
        private static State Capture(ZDO data, ZNetView view, Character actor, BaseAI ai, long target)
        {
            Rigidbody body = view.GetComponent<Rigidbody>();
            if (!body) return null;
            State item = new State { Id = data.m_uid, Source = data.GetOwner(), Destination = target, Revision = data.OwnerRevision,
                Prefab = data.GetPrefab(), Flags = (actor.IsFlying() ? Flying : 0) | (run.Read(actor) ? Run : 0) | (ai.m_randomFly ? RandomFlight : 0),
                Phase = ai.m_randomFly ? phase.Read(ai) : 0f, Velocity = body.linearVelocity, Current = current.Read(actor),
                Direction = direction.Read(actor), Turn = turn.Read(actor), TurnChange = turnChange.Read(actor) };
            Character victim = ai.GetTargetCreature(); item.Target = victim ? victim.GetZDOID() : ZDOID.None;
            // A new target may precede its first movement decision. Never carry
            // the previous target's circle/wander destination across that choice.
            if (!FgnTargetBoundary.FreshTarget(ai))
            {
                item.CircleRemaining = ai.m_randomCircleInterval - Math.Max(0f, Time.time - circleAt.Read(ai));
                item.CirclePoint = circlePoint.Read(ai);
                if (item.CircleRemaining > 0 && Finite(item.CirclePoint) && (item.CirclePoint - data.GetPosition()).sqrMagnitude <= 65536f) item.Flags |= Circle;
                item.WanderRemaining = wanderFor.Read(ai); item.WanderPoint = wanderPoint.Read(ai);
                if (!wanderReached.Read(ai) && item.WanderRemaining > 0 && Finite(item.WanderPoint) &&
                    (item.WanderPoint - data.GetPosition()).sqrMagnitude <= 65536f) item.Flags |= Wander;
            }
            return Valid(item) ? item : null;
        }
        internal static void Transfer(ZDO data, long target)
        {
            bool native = NativeTransfer != null && NativeTransfer(data);
            if (!native)
            {
            try
            {
            if (hooks != null && data != null && data.IsValid() && data.IsOwner() && data.GetOwner() != target && target != 0 && data.OwnerRevision < UInt16.MaxValue && ZNet.instance && ZNetScene.instance)
            {
                Context();
                ZNetView view = ZNetScene.instance.FindInstance(data);
                Character actor = view ? view.GetComponent<Character>() : null; BaseAI ai = view ? view.GetComponent<BaseAI>() : null;
                if (view && view.IsValid() && ReferenceEquals(view.GetZDO(), data) && actor && !(actor is Player) && ai)
                {
                    // Ground actors need the same final native pose/velocity
                    // cutoff as flyers before the immediate source publication.
                    ZSyncTransform sync = view.GetComponent<ZSyncTransform>();
                    if (sync) sync.SyncNow();
                    ZNetPeer peer = ZNet.instance.IsServer() ? ZNet.instance.GetPeer(target) : ZNet.instance.GetServerPeer();
                    if ((actor.IsFlying() || ai.m_randomFly) && (ZNet.instance.IsServer() ? Capable(peer) : supported && peer != null && peer.IsReady()))
                    {
                        State item = Capture(data, view, actor, ai, target);
                        if (item != null) peer.m_rpc.Invoke(Rpc, data.m_uid, Encode(item));
                    }
                }
            }
            }
            catch (Exception error) { Plugin.Warn("Flight checkpoint unavailable; native handoff retained: " + error.GetBaseException().Message); }
            // Outside the optional flight fallback: a failed combat checkpoint
            // must not silently discard already-processed combat state.
            if (SimulationTransfer != null) SimulationTransfer(data, target);
            if (CombatTransfer != null) CombatTransfer(data, target);
            }
            data.SetOwner(target); // Original final owner write; all native vetoes remain before it.
            if (!native)
            {
            if (CombatTransferred != null) CombatTransferred(data, target);
            if (SimulationTransferred != null) SimulationTransferred(data, target);
            }
        }
        private static ZPackage Encode(State item)
        {
            ZPackage p = new ZPackage(); p.Write((byte)1); p.Write(item.Source); p.Write(item.Destination); p.Write(item.Revision); p.Write(item.Prefab);
            p.Write((byte)item.Flags); p.Write(item.Phase); p.Write(item.Velocity); p.Write(item.Current); p.Write(item.Direction);
            p.Write(item.Turn); p.Write(item.TurnChange); p.Write(item.Target);
            if ((item.Flags & Circle) != 0) { p.Write(item.CirclePoint); p.Write(item.CircleRemaining); }
            if ((item.Flags & Wander) != 0) { p.Write(item.WanderPoint); p.Write(item.WanderRemaining); }
            return p;
        }
        private static State Decode(ZDOID id, ZPackage p)
        {
            if (p == null || p.Size() > 160 || p.ReadByte() != 1) return null;
            State item = new State { Id = id, Source = p.ReadLong(), Destination = p.ReadLong(), Revision = p.ReadUInt(), Prefab = p.ReadInt(),
                Flags = p.ReadByte(), Phase = p.ReadSingle(), Velocity = p.ReadVector3(), Current = p.ReadVector3(), Direction = p.ReadVector3(),
                Turn = p.ReadSingle(), TurnChange = p.ReadSingle(), Target = p.ReadZDOID(), Received = Time.realtimeSinceStartupAsDouble };
            if ((item.Flags & Circle) != 0) { item.CirclePoint = p.ReadVector3(); item.CircleRemaining = p.ReadSingle(); }
            if ((item.Flags & Wander) != 0) { item.WanderPoint = p.ReadVector3(); item.WanderRemaining = p.ReadSingle(); }
            return p.GetPos() == p.Size() && Valid(item) ? item : null;
        }
        private static void Receive(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage payload)
        {
            if (hooks == null || !ZNet.instance || !peer.IsReady() || !ReferenceEquals(peer.m_rpc, rpc) || id.IsNone()) return;
            bool server = ZNet.instance.IsServer();
            if (server ? !Capable(peer) || !ReferenceEquals(ZNet.instance.GetPeer(peer.m_uid), peer) :
                !supported || !peer.m_server || !ReferenceEquals(ZNet.instance.GetServerPeer(), peer)) return;
            Context(); if (world == null) return;
            State item = Decode(id, payload); if (item == null) return;
            ZDO data = world.GetZDO(id);
            // The checkpoint precedes the ownership record on ordered transport.
            // Late checkpoints never rewind a destination which already owns it.
            if (data != null && (!data.IsValid() || data.GetPrefab() != item.Prefab || data.GetOwner() != item.Source || data.OwnerRevision != item.Revision)) return;
            if (server)
            {
                if (item.Source != peer.m_uid || data == null) return;
                if (item.Destination != ZDOMan.GetSessionID())
                {
                    ZNetPeer next = ZNet.instance.GetPeer(item.Destination);
                    if (Capable(next)) next.m_rpc.Invoke(Rpc, id, Encode(item));
                    return;
                }
            }
            else if (item.Destination != ZDOMan.GetSessionID()) return;
            State prior;
            if (pending.TryGetValue(id, out prior))
            { if (prior.Revision >= item.Revision) return; }
            else if (pending.Count >= Limit) { Replicated(); if (pending.Count >= Limit) return; }
            pending[id] = item;
        }
        private static void Replicated()
        {
            if (pending.Count == 0) return;
            try { RestorePending(); }
            catch (Exception error) { pending.Clear(); scratch.Clear(); Plugin.Warn("Flight checkpoint restore stopped: " + error.GetBaseException().Message); }
        }
        private static void RestorePending()
        {
            Context(); if (world == null) return;
            scratch.Clear(); foreach (ZDOID id in pending.Keys) scratch.Add(id);
            foreach (ZDOID id in scratch)
            {
                State item; if (!pending.TryGetValue(id, out item)) continue;
                if (Time.realtimeSinceStartupAsDouble - item.Received > 5.0) { pending.Remove(id); continue; }
                ZDO data = world.GetZDO(id); if (data == null) continue;
                if (!data.IsValid() || data.GetPrefab() != item.Prefab || data.OwnerRevision > item.Revision + 1U ||
                    (data.OwnerRevision == item.Revision + 1U && data.GetOwner() != item.Destination)) { pending.Remove(id); continue; }
                if (data.GetOwner() == item.Destination && data.OwnerRevision == item.Revision + 1U && data.IsOwner() && ZNetScene.instance)
                {
                    ZNetView view = ZNetScene.instance.FindInstance(data);
                    BaseAI ai = view ? view.GetComponent<BaseAI>() : null; Character actor = view ? view.GetComponent<Character>() : null;
                    if (!view || !view.IsValid() || !ReferenceEquals(view.GetZDO(), data) || !ai || !actor || actor is Player) continue;
                    pending.Remove(id); Apply(data, view, actor, ai, item);
                }
            }
            scratch.Clear();
        }
        private static void Awoke(BaseAI __instance)
        { if (pending.Count != 0) Replicated(); }
        private static void Apply(ZDO data, ZNetView view, Character actor, BaseAI ai, State item)
        {
            actor.m_flying = (item.Flags & Flying) != 0;
            if (ai.m_randomFly && (item.Flags & RandomFlight) != 0) phase.Write(ai, item.Phase);
            current.Write(actor, item.Current); direction.Write(actor, item.Direction); run.Write(actor, (item.Flags & Run) != 0);
            turn.Write(actor, item.Turn); turnChange.Write(actor, item.TurnChange);
            // Native/PUP guards retain control of freezing, gravity and body
            // activation. The native movement accumulator is restored either way.
            Rigidbody body = view.GetComponent<Rigidbody>(); if (body && !body.isKinematic) body.linearVelocity = item.Velocity;
            // Expire this replica's old movement goals even when there is no
            // valid source goal. Never resume an old owner's stale local path.
            circleAt.Write(ai, Time.time - ai.m_randomCircleInterval - 1f); wanderFor.Write(ai, 0f); wanderReached.Write(ai, true);
            Character victim = ai.GetTargetCreature(); ZDOID target = victim ? victim.GetZDOID() : ZDOID.None;
            if (target == item.Target) ApplyIntent(ai, item);
            else if ((item.Flags & (Circle | Wander)) != 0) { intents.Remove(ai); intents.Add(ai, item); }
        }
        private static void ApplyIntent(BaseAI ai, State item)
        {
            float waited = (float)Math.Max(0, Time.realtimeSinceStartupAsDouble - item.Received);
            float remaining = item.CircleRemaining - waited;
            if ((item.Flags & Circle) != 0 && remaining > 0) { circlePoint.Write(ai, item.CirclePoint); circleAt.Write(ai, Time.time - ai.m_randomCircleInterval + Math.Min(ai.m_randomCircleInterval, remaining)); }
            remaining = item.WanderRemaining - waited;
            if ((item.Flags & Wander) != 0 && remaining > 0) { wanderPoint.Write(ai, item.WanderPoint); wanderFor.Write(ai, remaining); wanderReached.Write(ai, false); }
        }
        private static void Adopted(BaseAI ai, Character victim)
        {
            try { RestoreIntent(ai, victim); }
            catch (Exception error) { Plugin.Warn("Flight movement goal ignored: " + error.GetBaseException().Message); }
        }
        private static void RestoreIntent(BaseAI ai, Character victim)
        {
            State item; if (!ai || !intents.TryGetValue(ai, out item)) return;
            intents.Remove(ai);
            if (!ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance)) return;
            ZNetView view = ai.GetComponent<ZNetView>(); ZDO data = view && view.IsValid() ? view.GetZDO() : null;
            if (data == null || !data.IsOwner() || data.m_uid != item.Id || data.GetOwner() != item.Destination || data.OwnerRevision != item.Revision + 1U ||
                Time.realtimeSinceStartupAsDouble - item.Received > 5.0 || (victim ? victim.GetZDOID() : ZDOID.None) != item.Target) return;
            ApplyIntent(ai, item);
        }
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source)
        {
            MethodInfo setter = AccessTools.Method(typeof(ZDO), "SetOwner", new[] { typeof(long) });
            List<CodeInstruction> result = new List<CodeInstruction>(); int count = 0;
            foreach (CodeInstruction original in source)
            {
                CodeInstruction item = new CodeInstruction(original);
                if (setter.Equals(item.operand)) { item.opcode = OpCodes.Call; item.operand = AccessTools.Method(typeof(MobFlightHandoff), "Transfer"); count++; }
                result.Add(item);
            }
            if (count != 1) throw new NotSupportedException("FGN final mob owner write changed.");
            return result;
        }
    }
}
