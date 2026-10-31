using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // One checkpoint per real handoff. Effect instances continue from captured
    // runtime state; their setup and already processed hits are never replayed.
    internal static class MobCombatState
    {
        private const string CapabilityRpc = "TooManyLags_CombatCapability", CheckpointRpc = "TooManyLags_CombatCheckpoint", EventRpc = "TooManyLags_CombatLateEvent";
        private const int Protocol = 4, Limit = 1024, MaxHops = 16, CheckpointLimit = 16384;
        private const double Lifetime = 5.0;
        private sealed class Capability { internal bool Ready, Registered; }
        private sealed class Slot<T, V> where T : class
        {
            internal readonly Func<T, V> Read;
            internal readonly Action<T, V> Write;
            internal Slot(string name)
            {
                FieldInfo field = AccessTools.Field(typeof(T), name);
                if (field == null || field.FieldType != typeof(V)) throw new NotSupportedException("Native combat field changed: " + name);
                Read = FieldReader.Create<T, V>(field);
                DynamicMethod setter = new DynamicMethod("TooManyLagsCombat_" + name, typeof(void), new[] { typeof(T), typeof(V) }, typeof(MobCombatState).Module, true);
                ILGenerator il = setter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
                Write = (Action<T, V>)setter.CreateDelegate(typeof(Action<T, V>));
            }
        }
        private sealed class State
        {
            internal ZDOID Id, Target;
            internal long Source, Destination;
            internal uint Revision;
            internal int Prefab;
            internal Vector3 Push, Velocity;
            internal float Stagger, Backstab, Turn, TurnChange, RotationFactor;
            internal float TargetDelay;
            internal HitData LastHit;
            internal byte[] Effects;
            internal double Until;
        }
        private struct Stamp : IEquatable<Stamp>
        {
            internal ZDOID Id; internal uint Revision;
            public bool Equals(Stamp value) { return Id == value.Id && Revision == value.Revision; }
            public override bool Equals(object value) { return value is Stamp && Equals((Stamp)value); }
            public override int GetHashCode() { return Id.GetHashCode() ^ (int)Revision; }
        }
        private struct Token : IEquatable<Token>
        {
            internal long Issuer, Sequence;
            public bool Equals(Token value) { return Issuer == value.Issuer && Sequence == value.Sequence; }
            public override bool Equals(object value) { return value is Token && Equals((Token)value); }
            public override int GetHashCode() { return Issuer.GetHashCode() ^ Sequence.GetHashCode(); }
        }
        private sealed class LateEvent
        {
            internal Token Token;
            internal ZDOID Id;
            internal long Sender, Destination;
            internal uint Anchor, Expected, SentRevision;
            internal int Prefab, Kind, Name, Level, Variant, Hops;
            internal float Skill;
            internal bool ResetTime, Delivered, Sent, Returned;
            internal HitData Hit;
            internal Vector3 Direction;
            internal double Until;
        }
        private struct DamageScope
        {
            internal Character Actor;
            internal HitData Hit;
            internal bool Captured;
        }
        private static Slot<Character, Vector3> push, velocity;
        private static Slot<Character, float> stagger, backstab, turn, turnChange, rotationFactor;
        private static Slot<Character, HitData> lastHit;
        private static Slot<MonsterAI, float> targetDelay;
        private static Slot<MonsterAI, Character> targetActor;
        private static Slot<MonsterAI, StaticTarget> staticTarget;
        private static Func<SEMan, Character> effectActor;
        private static Func<Character, ZNetView> actorView;
        private static MethodInfo damageMethod, effectMethod, staggerMethod, healMethod;
        private static readonly Dictionary<ZDOID, State> pending = new Dictionary<ZDOID, State>();
        private static readonly Dictionary<ZDOID, State> outgoing = new Dictionary<ZDOID, State>();
        private static readonly Dictionary<Stamp, State> history = new Dictionary<Stamp, State>();
        private static readonly Dictionary<Token, LateEvent> events = new Dictionary<Token, LateEvent>();
        private static readonly HashSet<LateEvent> serverWaiting = new HashSet<LateEvent>();
        private static readonly Dictionary<ZDOID, List<LateEvent>> awaiting = new Dictionary<ZDOID, List<LateEvent>>();
        private static readonly List<ZDOID> ids = new List<ZDOID>();
        private static readonly List<Stamp> stamps = new List<Stamp>();
        private static readonly List<Token> tokens = new List<Token>();
        private static ConditionalWeakTable<ZRpc, Capability> capabilities = new ConditionalWeakTable<ZRpc, Capability>();
        private static Harmony hooks;
        private static ZDOMan world;
        private static ZNet network;
        private static long sequence;
        private static double nextPrune = Double.PositiveInfinity;
        private static bool pumping;
        [ThreadStatic] private static Character suppressedActor;
        [ThreadStatic] private static HitData suppressedHit;
        [ThreadStatic] private static ZRoutedRpc.RoutedRPCData routed;
        private static readonly int staggerHash = "RPC_Stagger".GetStableHashCode();

        internal static void Start(Assembly fgn)
        {
            Stop();
            try
            {
                if (fgn == null) throw new ArgumentNullException("fgn");
                MobStatusState.Initialize();
                push = new Slot<Character, Vector3>("m_pushForce"); stagger = new Slot<Character, float>("m_staggerDamage");
                backstab = new Slot<Character, float>("m_backstabTime"); lastHit = new Slot<Character, HitData>("m_lastHit");
                velocity = new Slot<Character, Vector3>("m_currentVel"); turn = new Slot<Character, float>("m_currentTurnVel");
                turnChange = new Slot<Character, float>("m_currentTurnVelChange");
                rotationFactor = new Slot<Character, float>("currentRotSpeedFactor");
                targetDelay = new Slot<MonsterAI, float>("m_updateTargetTimer");
                targetActor = new Slot<MonsterAI, Character>("m_targetCreature");
                staticTarget = new Slot<MonsterAI, StaticTarget>("m_targetStatic");
                effectActor = FieldReader.Create<SEMan, Character>(AccessTools.Field(typeof(SEMan), "m_character"));
                actorView = FieldReader.Create<Character, ZNetView>(AccessTools.Field(typeof(Character), "m_nview"));
                damageMethod = AccessTools.Method(typeof(Character), "RPC_Damage", new[] { typeof(long), typeof(HitData) });
                effectMethod = AccessTools.Method(typeof(SEMan), "RPC_AddStatusEffect", new[] { typeof(long), typeof(int), typeof(bool), typeof(int), typeof(float), typeof(int) });
                staggerMethod = AccessTools.Method(typeof(Character), "RPC_Stagger", new[] { typeof(long), typeof(Vector3) });
                healMethod = AccessTools.Method(typeof(Character), "RPC_Heal", new[] { typeof(long), typeof(float), typeof(bool) });
                if (damageMethod == null || effectMethod == null || staggerMethod == null || healMethod == null) throw new NotSupportedException("Native combat RPC contract changed.");
                hooks = new Harmony(Plugin.Id + ".mobcombatstate");
                hooks.Patch(AccessTools.Method(typeof(ZNet), "OnNewConnection"), postfix: Hook("Connected", false));
                hooks.Patch(AccessTools.Method(typeof(ZNet), "RPC_PeerInfo"), postfix: Hook("PeerReady", false));
                hooks.Patch(AccessTools.Method(typeof(ZNet), "Shutdown"), prefix: Hook("Reset", false));
                hooks.Patch(AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData"), postfix: Hook("Replicated", false));
                hooks.Patch(AccessTools.Method(typeof(Character), "CustomFixedUpdate"), prefix: Hook("BeforeFixed", true));
                hooks.Patch(AccessTools.Method(typeof(MonsterAI), "UpdateAI", new[] { typeof(float) }), prefix: Hook("BeforeAi", true));
                hooks.Patch(damageMethod, prefix: Hook("BeforeDamage", true), postfix: Hook("AfterDamage", false), finalizer: Hook("AfterDamage", false));
                hooks.Patch(effectMethod, prefix: Hook("BeforeEffect", true));
                hooks.Patch(staggerMethod, prefix: Hook("BeforeStagger", true));
                hooks.Patch(healMethod, prefix: Hook("BeforeHeal", true));
                hooks.Patch(AccessTools.Method(typeof(ZNetView), "HandleRoutedRPC"), prefix: Hook("BeforeRouted", true), postfix: Hook("AfterRouted", false), finalizer: Hook("AfterRouted", false));
                MethodInfo fgnDamage = AccessTools.Method(fgn.GetType("FiresGhettoNetworkMod.CreatureOwnership", true), "OnDamage", new[] { typeof(Character), typeof(HitData) });
                if (fgnDamage == null || fgnDamage.ReturnType != typeof(bool)) throw new NotSupportedException("FGN stale damage prefix contract changed.");
                hooks.Patch(fgnDamage, prefix: Hook("BeforeFgnDamage", true));
                if (!ReferenceEquals(ZNet.instance, null)) foreach (ZNetPeer peer in ZNet.instance.GetPeers()) { Connected(peer); if (peer.IsReady()) Probe(peer); }
            }
            catch (Exception error) { Stop(); Plugin.Warn("Combat-state handoff unavailable: " + error.GetBaseException().Message); }
        }
        private static HarmonyMethod Hook(string name, bool first)
        {
            HarmonyMethod method = new HarmonyMethod(typeof(MobCombatState), name);
            if (first) { method.priority = Priority.First; method.before = new[] { "com.Fire.FiresGhettoNetworkMod", Plugin.Id + ".fgn.aiownership" }; }
            return method;
        }
        internal static void Stop() { if (hooks != null) hooks.UnpatchSelf(); hooks = null; Reset(); MobStatusState.Stop(); }
        private static void Reset()
        {
            MobStatusState.Reset();
            pending.Clear(); outgoing.Clear(); history.Clear(); events.Clear(); serverWaiting.Clear(); awaiting.Clear(); ids.Clear(); stamps.Clear(); tokens.Clear();
            capabilities = new ConditionalWeakTable<ZRpc, Capability>(); world = null; network = null; sequence = 0; pumping = false; nextPrune = Double.PositiveInfinity;
            suppressedActor = null; suppressedHit = null; routed = null;
        }
        private static void Context()
        {
            if (ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance)) return;
            MobStatusState.Reset();
            pending.Clear(); outgoing.Clear(); history.Clear(); events.Clear(); serverWaiting.Clear(); awaiting.Clear();
            nextPrune = Double.PositiveInfinity;
            world = ZDOMan.instance; network = ZNet.instance;
        }
        private static bool Trusted(ZNetPeer peer, ZRpc rpc)
        {
            if (!ZNet.instance || peer == null || !peer.IsReady() || !ReferenceEquals(peer.m_rpc, rpc)) return false;
            return ZNet.instance.IsServer() ? ReferenceEquals(ZNet.instance.GetPeer(peer.m_uid), peer) :
                peer.m_server && ReferenceEquals(ZNet.instance.GetServerPeer(), peer);
        }
        private static bool Capable(ZNetPeer peer)
        { Capability value; return peer != null && peer.IsReady() && peer.m_rpc != null && capabilities.TryGetValue(peer.m_rpc, out value) && value.Ready; }
        internal static bool CanCheckpoint(ZNetPeer peer) { return hooks != null && Capable(peer); }
        private static void Connected(ZNetPeer __0)
        {
            if (hooks == null || __0 == null || __0.m_rpc == null) return;
            ZNetPeer peer = __0; Capability state = capabilities.GetOrCreateValue(peer.m_rpc); if (state.Registered) return; state.Registered = true;
            peer.m_rpc.Register<int>(CapabilityRpc, delegate(ZRpc rpc, int value)
            {
                if (!Trusted(peer, rpc) || (value != Protocol && value != -Protocol)) return;
                capabilities.GetOrCreateValue(rpc).Ready = true;
                if (value == Protocol) rpc.Invoke(CapabilityRpc, -Protocol);
            });
            peer.m_rpc.Register<ZDOID, ZPackage>(CheckpointRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
            { try { ReceiveState(peer, rpc, id, packet); } catch (Exception error) { Plugin.Warn("Combat checkpoint rejected: " + error.GetBaseException().Message); } });
            peer.m_rpc.Register<ZDOID, ZPackage>(EventRpc, delegate(ZRpc rpc, ZDOID id, ZPackage packet)
            { try { ReceiveEvent(peer, rpc, id, packet); } catch (Exception error) { Plugin.Warn("Late combat event rejected: " + error.GetBaseException().Message); } });
        }
        private static void PeerReady(ZRpc rpc)
        { if (hooks != null && ZNet.instance) foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (ReferenceEquals(peer.m_rpc, rpc)) { Connected(peer); Probe(peer); break; } }
        private static void Probe(ZNetPeer peer) { if (peer != null && peer.IsReady() && peer.m_rpc != null) peer.m_rpc.Invoke(CapabilityRpc, Protocol); }
        private static bool Finite(float v) { return !Single.IsNaN(v) && !Single.IsInfinity(v); }
        private static bool Valid(State state)
        {
            return !state.Id.IsNone() && state.Source != 0 && state.Destination != 0 && state.Source != state.Destination && state.Revision < UInt16.MaxValue &&
                Finite(state.Push.x) && Finite(state.Push.y) && Finite(state.Push.z) && state.Push.sqrMagnitude <= 100000000f &&
                Finite(state.Velocity.x) && Finite(state.Velocity.y) && Finite(state.Velocity.z) && state.Velocity.sqrMagnitude <= 100000000f &&
                Finite(state.Turn) && Finite(state.TurnChange) && Finite(state.RotationFactor) && state.RotationFactor >= 0f && state.RotationFactor <= 100f && Finite(state.TargetDelay) && state.TargetDelay >= 0f && state.TargetDelay <= 6f &&
                Finite(state.Stagger) && state.Stagger >= 0f && state.Stagger <= 1000000000f && Finite(state.Backstab) && state.Backstab >= 0f && state.Backstab <= 300f;
        }
        private static Character Actor(ZDO data)
        {
            ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            Character actor = view && view.IsValid() && ReferenceEquals(view.GetZDO(), data) ? view.GetComponent<Character>() : null;
            return actor && !(actor is Player) && view.GetComponent<BaseAI>() ? actor : null;
        }
        internal static bool HandlesLateDamage(ZDO data)
        {
            if (hooks == null || outgoing.Count == 0 || data == null || !data.IsValid() || data.IsOwner() ||
                !ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance)) return false;
            State route;
            if (!outgoing.TryGetValue(data.m_uid, out route) || route.Source != ZDOMan.GetSessionID() ||
                route.Prefab != data.GetPrefab() || route.Until < Time.realtimeSinceStartupAsDouble ||
                data.OwnerRevision <= route.Revision || data.GetOwner() == 0 || !Actor(data)) return false;
            return network && (network.IsServer() || Capable(network.GetServerPeer()));
        }
        internal static void BeforeTransfer(ZDO data, long destination)
        {
            if (hooks == null || data == null || !data.IsValid() || !data.IsOwner() || destination == 0 || data.GetOwner() == destination) return;
            Character actor = Actor(data); if (!actor) return;
            if (data.OwnerRevision >= UInt16.MaxValue) return;
            Context();
            ZNetPeer peer = ZNet.instance ? (ZNet.instance.IsServer() ? ZNet.instance.GetPeer(destination) : ZNet.instance.GetServerPeer()) : null;
            if (!Capable(peer)) return; // Old endpoints keep their native protocol.
            Prune();
            State state = new State { Id = data.m_uid, Source = data.GetOwner(), Destination = destination, Revision = data.OwnerRevision, Prefab = data.GetPrefab(),
                Push = push.Read(actor), Velocity = velocity.Read(actor), Turn = turn.Read(actor), TurnChange = turnChange.Read(actor),
                RotationFactor = rotationFactor.Read(actor), Stagger = stagger.Read(actor), Backstab = Math.Max(0f, Math.Min(300f, 300f - (Time.time - backstab.Read(actor)))), LastHit = lastHit.Read(actor),
                Effects = MobStatusState.Capture(actor), Until = Time.realtimeSinceStartupAsDouble + Lifetime };
            MonsterAI ai = actor.GetComponent<MonsterAI>();
            if (ai)
            {
                Character target = targetActor.Read(ai);
                state.Target = target ? target.GetZDOID() : ZDOID.None;
                state.TargetDelay = Math.Max(0f, Math.Min(6f, targetDelay.Read(ai)));
            }
            if (!Valid(state)) throw new InvalidOperationException("Invalid native transient combat state.");
            ZPackage packet = EncodeState(state);
            if (packet.Size() > CheckpointLimit) throw new InvalidOperationException("Combat checkpoint exceeds the supported native payload size.");
            if (outgoing.Count >= Limit && !outgoing.ContainsKey(state.Id)) throw new InvalidOperationException("Combat handoff capacity reached.");
            Remember(state); outgoing[state.Id] = state;
            // Reliable checkpoint is on the same ordered source/server connection
            // as the subsequent native owner write. Exceptions abort that write.
            peer.m_rpc.Invoke(CheckpointRpc, state.Id, packet);
        }
        internal static void CompleteTransfer(ZDO data, long destination)
        {
            State state;
            if (hooks == null || data == null || !outgoing.TryGetValue(data.m_uid, out state) || !data.IsValid() || data.GetOwner() != destination ||
                state.Destination != destination || state.Source != ZDOMan.GetSessionID() || data.OwnerRevision != state.Revision + 1U) return;
            try { Character actor = Actor(data); if (actor) MobStatusState.Relinquish(actor, state.Effects, destination); }
            catch (Exception error) { Plugin.Warn("Committed mob effect cleanup failed: " + error.GetBaseException().Message); }
        }
        private static ZPackage EncodeState(State state)
        {
            ZPackage packet = new ZPackage(); packet.Write((byte)Protocol); packet.Write(state.Source); packet.Write(state.Destination); packet.Write(state.Revision);
            packet.Write(state.Prefab); packet.Write(state.Push); packet.Write(state.Stagger); packet.Write(state.Backstab);
            packet.Write(state.Velocity); packet.Write(state.Turn); packet.Write(state.TurnChange); packet.Write(state.LastHit != null);
            if (state.LastHit != null) state.LastHit.Serialize(ref packet);
            packet.Write(state.Effects != null);
            if (state.Effects != null) packet.Write(state.Effects);
            packet.Write(state.RotationFactor);
            packet.Write(state.Target); packet.Write(state.TargetDelay);
            return packet;
        }
        private static State DecodeState(ZDOID id, ZPackage packet)
        {
            if (packet == null || packet.Size() > CheckpointLimit || packet.ReadByte() != Protocol) return null;
            State state = new State { Id = id, Source = packet.ReadLong(), Destination = packet.ReadLong(), Revision = packet.ReadUInt(), Prefab = packet.ReadInt(),
                Push = packet.ReadVector3(), Stagger = packet.ReadSingle(), Backstab = packet.ReadSingle(),
                Velocity = packet.ReadVector3(), Turn = packet.ReadSingle(), TurnChange = packet.ReadSingle(), Until = Time.realtimeSinceStartupAsDouble + Lifetime };
            if (packet.ReadBool()) { state.LastHit = new HitData(); state.LastHit.Deserialize(ref packet); }
            if (packet.ReadBool()) state.Effects = packet.ReadByteArray();
            state.RotationFactor = packet.ReadSingle();
            state.Target = packet.ReadZDOID(); state.TargetDelay = packet.ReadSingle();
            return packet.GetPos() == packet.Size() && Valid(state) ? state : null;
        }
        private static void Remember(State state)
        {
            Stamp stamp = new Stamp { Id = state.Id, Revision = state.Revision };
            if (history.Count >= Limit && !history.ContainsKey(stamp)) throw new InvalidOperationException("Combat checkpoint history capacity reached.");
            history[stamp] = state;
            nextPrune = Math.Min(nextPrune, state.Until);
        }
        private static void ReceiveState(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage packet)
        {
            if (hooks == null || !Trusted(peer, rpc) || !Capable(peer) || id.IsNone()) return;
            Context(); if (world == null) return; Prune();
            State state = DecodeState(id, packet); if (state == null) return;
            ZDO data = world.GetZDO(id);
            if (data != null && (!data.IsValid() || data.GetPrefab() != state.Prefab || data.GetOwner() != state.Source || data.OwnerRevision != state.Revision)) return;
            if (network.IsServer())
            {
                if (state.Source != peer.m_uid || data == null) return;
                Remember(state);
                if (state.Destination != ZDOMan.GetSessionID())
                {
                    ZNetPeer next = network.GetPeer(state.Destination);
                    if (Capable(next)) next.m_rpc.Invoke(CheckpointRpc, id, EncodeState(state));
                    return;
                }
            }
            else if (state.Destination != ZDOMan.GetSessionID()) return;
            State prior;
            if (pending.TryGetValue(id, out prior)) { if (prior.Revision > state.Revision) return; }
            else if (pending.Count >= Limit) { Plugin.Warn("Combat checkpoint capacity exceeded; handoff checkpoint ignored."); return; }
            pending[id] = state;
            nextPrune = Math.Min(nextPrune, state.Until);
        }
        private static void Apply(ZDO data)
        {
            State state;
            if (data == null || !pending.TryGetValue(data.m_uid, out state)) return;
            if (Time.realtimeSinceStartupAsDouble > state.Until || !data.IsValid() || data.GetPrefab() != state.Prefab || data.OwnerRevision > state.Revision + 1U ||
                (data.OwnerRevision == state.Revision + 1U && data.GetOwner() != state.Destination)) { pending.Remove(data.m_uid); return; }
            if (!data.IsOwner() || data.GetOwner() != state.Destination || data.OwnerRevision != state.Revision + 1U) return;
            Character actor = Actor(data); if (!actor || actor.GetSEMan() == null) return;
            MobStatusState.Restore(actor, state.Effects);
            pending.Remove(data.m_uid); // At most once, before any destination gameplay.
            push.Write(actor, state.Push); stagger.Write(actor, state.Stagger);
            // Native walking keeps acceleration/turn accumulators separately
            // from body velocity and pushback. Restore, never add the impulse.
            velocity.Write(actor, state.Velocity); turn.Write(actor, state.Turn); turnChange.Write(actor, state.TurnChange);
            rotationFactor.Write(actor, state.RotationFactor);
            MonsterAI ai = actor.GetComponent<MonsterAI>();
            if (ai && !state.Target.IsNone())
            {
                ZDO victimData = world.GetZDO(state.Target);
                ZNetView victimView = victimData != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(victimData) : null;
                Character victim = victimView && victimView.IsValid() ? victimView.GetComponent<Character>() : null;
                if (victim && !victim.IsDead() && BaseAI.IsEnemy(actor, victim))
                { targetActor.Write(ai, victim); staticTarget.Write(ai, null); targetDelay.Write(ai, state.TargetDelay); }
            }
            backstab.Write(actor, state.Backstab > 0 ? Time.time - 300f + state.Backstab : Time.time - 301f);
            lastHit.Write(actor, state.LastHit); // Attribution only; NEVER RPC_Damage/health replay.
        }
        private static void BeforeFixed(Character __instance)
        {
            if (pending.Count == 0 && awaiting.Count == 0) return;
            try { Context(); ZDO data = Data(__instance); if (data != null) { Apply(data); Drain(data); } }
            catch (Exception error) { Plugin.Warn("Combat checkpoint restore failed: " + error.GetBaseException().Message); }
        }
        private static void BeforeAi(MonsterAI __instance)
        {
            if (pending.Count == 0 || !__instance) return;
            try { Context(); ZNetView view = __instance.GetComponent<ZNetView>(); if (view && view.IsValid()) Apply(view.GetZDO()); }
            catch (Exception error) { Plugin.Warn("Mob target checkpoint restore failed: " + error.GetBaseException().Message); }
        }
        private static ZDO Data(Character actor)
        { ZNetView view = actor && !(actor is Player) ? actorView(actor) : null; return view && view.IsValid() ? view.GetZDO() : null; }
        private static bool BeforeDamage(Character __instance, long sender, HitData hit, bool __runOriginal, out DamageScope __state)
        {
            __state = new DamageScope();
            if (!__runOriginal) return false;
            if (pending.Count == 0 && outgoing.Count == 0 && awaiting.Count == 0) return true;
            if (hit == null || hooks == null || !__instance || __instance is Player) return true;
            try
            {
                Context(); ZDO data = Data(__instance); if (data == null) return true;
                Apply(data); if (data.IsOwner()) return true;
                if (!pending.ContainsKey(data.m_uid) && !outgoing.ContainsKey(data.m_uid)) return true;
                LateEvent item = new LateEvent { Sender = sender, Kind = 1, Hit = hit };
                bool handled = HoldIncoming(data, item) || Forward(data, item);
                return !handled || Suppress(__instance, hit, out __state);
            }
            catch (Exception error)
            {
                // A failed reliable send may already have entered the transport.
                // Never let FGN retry the same mutable hit independently.
                Plugin.Warn("Late mob damage forwarding failed: " + error.GetBaseException().Message); return Suppress(__instance, hit, out __state);
            }
        }
        private static bool Suppress(Character actor, HitData hit, out DamageScope scope)
        {
            scope = new DamageScope { Actor = suppressedActor, Hit = suppressedHit, Captured = true };
            suppressedActor = actor; suppressedHit = hit; return false;
        }
        private static bool BeforeFgnDamage(Character __0, HitData __1, ref bool __result)
        {
            if (!ReferenceEquals(suppressedActor, __0) || !ReferenceEquals(suppressedHit, __1)) return true;
            __result = false; return false;
        }
        private static void AfterDamage(Character __instance, HitData hit, DamageScope __state)
        {
            if (__state.Captured && ReferenceEquals(suppressedActor, __instance) && ReferenceEquals(suppressedHit, hit))
            { suppressedActor = __state.Actor; suppressedHit = __state.Hit; }
        }
        private static bool BeforeEffect(SEMan __instance, long sender, int nameHash, bool resetTime, int itemLevel, float skillLevel, int variant, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (pending.Count == 0 && outgoing.Count == 0 && awaiting.Count == 0) return true;
            if (hooks == null || __instance == null) return true;
            try
            {
                Context(); Character actor = effectActor(__instance); ZDO data = Data(actor); if (data == null) return true;
                Apply(data); if (data.IsOwner()) return true;
                if (!pending.ContainsKey(data.m_uid) && !outgoing.ContainsKey(data.m_uid)) return true;
                LateEvent item = new LateEvent { Sender = sender, Kind = 2, Name = nameHash, ResetTime = resetTime, Level = itemLevel, Skill = skillLevel, Variant = variant };
                return !(HoldIncoming(data, item) || Forward(data, item));
            }
            catch (Exception error) { Plugin.Warn("Late mob status-effect forwarding failed: " + error.GetBaseException().Message); return false; }
        }
        private static bool BeforeStagger(Character __instance, long __0, Vector3 __1, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (hooks == null || (pending.Count == 0 && outgoing.Count == 0 && awaiting.Count == 0)) return true;
            try
            {
                Context(); ZDO data = Data(__instance); if (data == null) return true;
                Apply(data); if (data.IsOwner() || (!pending.ContainsKey(data.m_uid) && !outgoing.ContainsKey(data.m_uid))) return true;
                // Native RPC_Stagger also supports broadcasts and has no owner
                // check. Only reroute an explicitly owner-addressed late call;
                // observers and direct mod calls keep their native behaviour.
                if (routed == null || routed.m_targetPeerID != ZDOMan.GetSessionID() || routed.m_targetZDO != data.m_uid || routed.m_methodHash != staggerHash) return true;
                LateEvent item = new LateEvent { Sender = __0, Kind = 3, Direction = __1 };
                return !(HoldIncoming(data, item) || Forward(data, item));
            }
            catch (Exception error) { Plugin.Warn("Late mob stagger forwarding failed: " + error.GetBaseException().Message); return false; }
        }
        private static void BeforeRouted(ZRoutedRpc.RoutedRPCData __0, out ZRoutedRpc.RoutedRPCData __state)
        { __state = routed; if (pending.Count != 0 || outgoing.Count != 0) routed = __0; }
        private static void AfterRouted(ZRoutedRpc.RoutedRPCData __state) { routed = __state; }
        private static bool BeforeHeal(Character __instance, long __0, float __1, bool __2, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (hooks == null || (pending.Count == 0 && outgoing.Count == 0 && awaiting.Count == 0)) return true;
            try
            {
                Context(); ZDO data = Data(__instance); if (data == null) return true;
                Apply(data); if (data.IsOwner() || (!pending.ContainsKey(data.m_uid) && !outgoing.ContainsKey(data.m_uid))) return true;
                LateEvent item = new LateEvent { Sender = __0, Kind = 4, Skill = __1, ResetTime = __2 };
                return !(HoldIncoming(data, item) || Forward(data, item));
            }
            catch (Exception error) { Plugin.Warn("Late mob healing forwarding failed: " + error.GetBaseException().Message); return false; }
        }
        private static bool HoldIncoming(ZDO data, LateEvent item)
        {
            State state;
            if (!pending.TryGetValue(data.m_uid, out state) || state.Destination != ZDOMan.GetSessionID() || state.Prefab != data.GetPrefab() ||
                state.Until < Time.realtimeSinceStartupAsDouble || data.OwnerRevision != state.Revision || data.GetOwner() != state.Source) return false;
            item.Id = data.m_uid; item.Prefab = state.Prefab; item.Token = new Token { Issuer = ZDOMan.GetSessionID(), Sequence = ++sequence };
            item.Anchor = state.Revision; item.Expected = state.Revision + 1U; item.Until = state.Until;
            if (!Store(item)) return false;
            Queue(item); return true;
        }
        private static bool Forward(ZDO data, LateEvent item)
        {
            State route;
            if (!outgoing.TryGetValue(data.m_uid, out route) || route.Until < Time.realtimeSinceStartupAsDouble || data.GetPrefab() != route.Prefab ||
                data.OwnerRevision <= route.Revision || data.GetOwner() == 0 || route.Source != ZDOMan.GetSessionID()) return false;
            ZNetPeer peer = network && !network.IsServer() ? network.GetServerPeer() : null;
            if (!network || (!network.IsServer() && !Capable(peer))) return false;
            item.Id = data.m_uid; item.Prefab = data.GetPrefab(); item.Token = new Token { Issuer = ZDOMan.GetSessionID(), Sequence = ++sequence };
            item.Anchor = route.Revision; item.Expected = data.OwnerRevision; item.Until = route.Until;
            if (network.IsServer()) { if (!Store(item)) return false; Dispatch(item); }
            else peer.m_rpc.Invoke(EventRpc, item.Id, EncodeEvent(item));
            return true; // No native/FGN second forward or replay at this source.
        }
        private static ZPackage EncodeEvent(LateEvent item)
        {
            ZPackage p = new ZPackage(); p.Write((byte)Protocol); p.Write(item.Token.Issuer); p.Write(item.Token.Sequence); p.Write(item.Anchor); p.Write(item.Expected);
            p.Write(item.Prefab); p.Write(item.Sender); p.Write((byte)item.Kind); p.Write((byte)item.Hops); p.Write((float)Math.Max(0, item.Until - Time.realtimeSinceStartupAsDouble));
            if (item.Kind == 1) item.Hit.Serialize(ref p);
            else if (item.Kind == 2) { p.Write(item.Name); p.Write(item.ResetTime); p.Write(item.Level); p.Write(item.Skill); p.Write(item.Variant); }
            else if (item.Kind == 3) p.Write(item.Direction);
            else { p.Write(item.Skill); p.Write(item.ResetTime); }
            return p;
        }
        private static LateEvent DecodeEvent(ZDOID id, ZPackage p)
        {
            if (p == null || p.Size() > 1024 || p.ReadByte() != Protocol) return null;
            LateEvent item = new LateEvent { Id = id, Token = new Token { Issuer = p.ReadLong(), Sequence = p.ReadLong() }, Anchor = p.ReadUInt(), Expected = p.ReadUInt(),
                Prefab = p.ReadInt(), Sender = p.ReadLong(), Kind = p.ReadByte(), Hops = p.ReadByte() };
            float life = p.ReadSingle(); if (!Finite(life) || life <= 0 || life > Lifetime || item.Token.Issuer == 0 || item.Token.Sequence <= 0 || item.Anchor >= UInt16.MaxValue ||
                item.Expected <= item.Anchor || item.Expected > UInt16.MaxValue || item.Hops > MaxHops) return null;
            item.Until = Time.realtimeSinceStartupAsDouble + life;
            if (item.Kind == 1) { item.Hit = new HitData(); item.Hit.Deserialize(ref p); }
            else if (item.Kind == 2) { item.Name = p.ReadInt(); item.ResetTime = p.ReadBool(); item.Level = p.ReadInt(); item.Skill = p.ReadSingle(); item.Variant = p.ReadInt(); if (!Finite(item.Skill)) return null; }
            else if (item.Kind == 3) { item.Direction = p.ReadVector3(); if (!Finite(item.Direction.x) || !Finite(item.Direction.y) || !Finite(item.Direction.z) || item.Direction.sqrMagnitude > 100000000f) return null; }
            else if (item.Kind == 4) { item.Skill = p.ReadSingle(); item.ResetTime = p.ReadBool(); if (!Finite(item.Skill)) return null; }
            else return null;
            return p.GetPos() == p.Size() ? item : null;
        }
        private static bool Store(LateEvent item)
        {
            if (events.ContainsKey(item.Token)) return false;
            if (events.Count >= Limit) { Prune(); if (events.Count >= Limit) { Plugin.Warn("Late mob event capacity exceeded."); return false; } }
            events[item.Token] = item;
            if (network && network.IsServer()) serverWaiting.Add(item);
            nextPrune = Math.Min(nextPrune, item.Until); return true;
        }
        private static void ReceiveEvent(ZNetPeer peer, ZRpc rpc, ZDOID id, ZPackage packet)
        {
            if (hooks == null || !Trusted(peer, rpc) || !Capable(peer) || id.IsNone()) return;
            Context(); if (world == null) return; Prune();
            LateEvent item = DecodeEvent(id, packet); if (item == null) return;
            LateEvent previous;
            if (network.IsServer())
            {
                if (events.TryGetValue(item.Token, out previous))
                {
                    if (previous.Delivered || !previous.Sent || previous.Destination != peer.m_uid || item.Hops != previous.Hops || previous.Returned) return;
                    previous.Returned = true; previous.Sent = false; serverWaiting.Add(previous); Dispatch(previous); return;
                }
                State route;
                if (item.Token.Issuer != peer.m_uid || item.Hops != 0 || !history.TryGetValue(new Stamp { Id = id, Revision = item.Anchor }, out route) ||
                    (route.Source != peer.m_uid && route.Destination != peer.m_uid) || route.Prefab != item.Prefab || route.Until < Time.realtimeSinceStartupAsDouble) return;
                if (Store(item)) Dispatch(item);
            }
            else
            {
                if (events.TryGetValue(item.Token, out previous))
                {
                    if (previous.Delivered || !previous.Returned || item.Expected <= previous.Expected || item.Hops <= previous.Hops) return;
                    events.Remove(item.Token);
                }
                if (!Store(item)) return;
                Queue(item); Drain(world.GetZDO(item.Id));
            }
        }
        private static void Queue(LateEvent item)
        {
            List<LateEvent> list;
            if (!awaiting.TryGetValue(item.Id, out list)) awaiting[item.Id] = list = new List<LateEvent>();
            if (!list.Contains(item)) list.Add(item);
        }
        private static void Dispatch(LateEvent item)
        {
            if (item.Delivered || item.Sent || item.Until < Time.realtimeSinceStartupAsDouble || item.Hops >= MaxHops) return;
            ZDO data = world.GetZDO(item.Id);
            if (data == null || !data.IsValid() || data.GetPrefab() != item.Prefab || data.OwnerRevision <= item.Anchor || data.GetOwner() == 0) return;
            // A returned envelope cannot bounce against an unchanged server
            // ownership snapshot. Wait for native replication, without polling AI.
            if (item.Returned && data.OwnerRevision <= item.SentRevision) return;
            item.Expected = data.OwnerRevision;
            if (data.IsOwner()) { Queue(item); Drain(data); return; }
            ZNetPeer destination = network.GetPeer(data.GetOwner()); if (!Capable(destination)) return;
            item.Destination = destination.m_uid; item.SentRevision = data.OwnerRevision; item.Hops++; item.Returned = false; item.Sent = true;
            serverWaiting.Remove(item);
            destination.m_rpc.Invoke(EventRpc, item.Id, EncodeEvent(item));
        }
        private static void Drain(ZDO data)
        {
            List<LateEvent> list; if (data == null || !awaiting.TryGetValue(data.m_uid, out list)) return;
            // Detach before entering real native RPCs; a status-effect handler
            // may synchronously cause other calls on the same actor.
            awaiting.Remove(data.m_uid);
            foreach (LateEvent item in list) if (!item.Delivered && !TryDelivery(item) && item.Until >= Time.realtimeSinceStartupAsDouble) Queue(item);
        }
        private static bool TryDelivery(LateEvent item)
        {
            if (item.Delivered || item.Until < Time.realtimeSinceStartupAsDouble) return true;
            ZDO data = world.GetZDO(item.Id); if (data == null || !data.IsValid() || data.GetPrefab() != item.Prefab) return false;
            if (data.OwnerRevision < item.Expected) return false;
            if (!data.IsOwner())
            {
                if (network.IsServer()) { item.Sent = false; Dispatch(item); return item.Sent; }
                if (!item.Returned && Capable(network.GetServerPeer()))
                { item.Returned = true; network.GetServerPeer().m_rpc.Invoke(EventRpc, item.Id, EncodeEvent(item)); }
                return true;
            }
            Apply(data); Character actor = Actor(data); if (!actor || actor.GetSEMan() == null || pending.ContainsKey(item.Id)) return false;
            item.Delivered = true;
            if (network.IsServer()) serverWaiting.Remove(item);
            if (item.Kind == 1) damageMethod.Invoke(actor, new object[] { item.Sender, item.Hit });
            else if (item.Kind == 2) effectMethod.Invoke(actor.GetSEMan(), new object[] { item.Sender, item.Name, item.ResetTime, item.Level, item.Skill, item.Variant });
            else if (item.Kind == 3) staggerMethod.Invoke(actor, new object[] { item.Sender, item.Direction });
            else healMethod.Invoke(actor, new object[] { item.Sender, item.Skill, item.ResetTime });
            return true;
        }
        private static void Replicated()
        {
            MobStatusState.RefreshReferences();
            if (pumping || (pending.Count == 0 && events.Count == 0 && outgoing.Count == 0 && history.Count == 0)) return;
            if (pending.Count == 0 && awaiting.Count == 0 && serverWaiting.Count == 0 && Time.realtimeSinceStartupAsDouble <= nextPrune &&
                ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance)) return;
            try
            {
                pumping = true; Context(); if (world == null) return; Prune();
                ids.Clear(); foreach (ZDOID id in pending.Keys) ids.Add(id);
                foreach (ZDOID id in ids) Apply(world.GetZDO(id));
                // Actual event handlers may synchronously change ownership.
                // Snapshot only the rare queued-event path, never all NPCs.
                if (awaiting.Count != 0) foreach (ZDOID id in new List<ZDOID>(awaiting.Keys)) Drain(world.GetZDO(id));
                if (serverWaiting.Count != 0 && network && network.IsServer())
                {
                    // Delivered/sent entries remain only as deduplication
                    // tombstones; never scan them on each world packet.
                    foreach (LateEvent item in new List<LateEvent>(serverWaiting)) Dispatch(item);
                }
            }
            catch (Exception error) { Plugin.Warn("Combat state/event processing failed: " + error.GetBaseException().Message); }
            finally { ids.Clear(); tokens.Clear(); pumping = false; }
        }
        private static void Prune()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now <= nextPrune) return;
            ids.Clear(); foreach (KeyValuePair<ZDOID, State> pair in pending) if (pair.Value.Until < now) ids.Add(pair.Key); foreach (ZDOID id in ids) pending.Remove(id);
            ids.Clear(); foreach (KeyValuePair<ZDOID, State> pair in outgoing) if (pair.Value.Until < now) ids.Add(pair.Key); foreach (ZDOID id in ids) outgoing.Remove(id);
            stamps.Clear(); foreach (KeyValuePair<Stamp, State> pair in history) if (pair.Value.Until < now) stamps.Add(pair.Key); foreach (Stamp stamp in stamps) history.Remove(stamp);
            int expiredEvents = 0;
            tokens.Clear(); foreach (KeyValuePair<Token, LateEvent> pair in events) if (pair.Value.Until < now)
            { tokens.Add(pair.Key); if (!pair.Value.Delivered && !pair.Value.Sent && (!pair.Value.Returned || (network && network.IsServer()))) expiredEvents++; }
            foreach (Token token in tokens) { serverWaiting.Remove(events[token]); events.Remove(token); }
            ids.Clear(); foreach (KeyValuePair<ZDOID, List<LateEvent>> pair in awaiting)
            { pair.Value.RemoveAll(delegate(LateEvent item) { return item.Delivered || item.Until < now; }); if (pair.Value.Count == 0) ids.Add(pair.Key); }
            foreach (ZDOID id in ids) awaiting.Remove(id); ids.Clear(); stamps.Clear(); tokens.Clear();
            nextPrune = Double.PositiveInfinity;
            foreach (State state in pending.Values) nextPrune = Math.Min(nextPrune, state.Until);
            foreach (State state in outgoing.Values) nextPrune = Math.Min(nextPrune, state.Until);
            foreach (State state in history.Values) nextPrune = Math.Min(nextPrune, state.Until);
            foreach (LateEvent item in events.Values) nextPrune = Math.Min(nextPrune, item.Until);
            if (expiredEvents != 0) Plugin.Warn(expiredEvents + " queued mob combat event(s) expired before destination ownership/readiness.");
        }
    }
}
