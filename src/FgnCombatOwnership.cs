using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    internal static class FgnCombatOwnership
    {
        private sealed class Report { internal ZDO Data; internal long Owner, BoundaryToken; internal uint Revision; internal ZDOID Target; }
        private sealed class Lease { internal ZDO Data; internal long ReturnPeer, SourceOwner; internal uint SourceRevision; internal double Requested; internal uint Pass; internal bool Initial; }
        private sealed class Forward { internal ZDO Data; internal long Owner; internal double Until; }
        private sealed class Watch { internal ZDO Data; internal long Owner; internal uint Revision, Pass; internal bool Acknowledged, AttackRejected, AttackReady; }
        private sealed class FactionActor { internal Character Actor; }
        private sealed class AiPass { internal ZDO Data; internal long Owner; internal uint Revision; }
        private static ConditionalWeakTable<BaseAI, AiPass> completedAi = new ConditionalWeakTable<BaseAI, AiPass>();
        internal static Func<Vector3, bool> ServerCollisionReady = null;
        internal static Action<ZNetPeer> FlightConnect = null, FlightProbe = null;
        internal static Func<ZNetPeer, int, bool> FlightMode = null;
        internal static Action<ZDO, long> FlightTransfer = null;
        internal static bool Enabled;
        internal static FgnCombatBridge Fgn;
        private static ConfigEntry<bool> configured;
        private static ConfigEntry<bool> defaultMobs;
        private static ConfigEntry<bool> bossAlwaysServerOwned;
        private static ConfigEntry<float> bossPlayerRadius;
        private static bool serverDefaultsConfigured;
        // Optional defaults track loaded actors only, using scene lifecycle
        // events and the existing two-second FGN pass, never physics polling.
        private static readonly Dictionary<ZDOID, ZDO> defaultLoaded = new Dictionary<ZDOID, ZDO>();
        private static System.Version detectedVersion;
        internal static string InstallationStatus = "not initialized";
        internal static string StartupError;
        private static ConfigEntry<int> maximumPing;
        private static ConfigEntry<float> nearbyRadius;
        private static Harmony harmony;
        private static Harmony sessionHarmony;
        private static bool serverConfirmed;
        private static ZDO localCapability;
        private static ConditionalWeakTable<BaseAI, Report> reports = new ConditionalWeakTable<BaseAI, Report>();
        private static readonly Dictionary<ZDOID, Lease> managed = new Dictionary<ZDOID, Lease>();
        private static readonly Dictionary<ZDOID, Forward> forwarded = new Dictionary<ZDOID, Forward>();
        private static readonly Dictionary<ZDOID, Watch> watching = new Dictionary<ZDOID, Watch>();
        private static readonly Dictionary<ZDOID, Report> receivedTargets = new Dictionary<ZDOID, Report>();
        private static readonly List<ZDOID> scratch = new List<ZDOID>();
        private const int PrefabFactionLimit = 1024;
        private static readonly Dictionary<int, Character> prefabFactionActors = new Dictionary<int, Character>(PrefabFactionLimit);
        private static readonly int[] prefabFactionOrder = new int[PrefabFactionLimit];
        private static int prefabFactionNext;
        private static ZNetScene factionScene;
        private static ConditionalWeakTable<ZNetView, FactionActor> liveFactionActors = new ConditionalWeakTable<ZNetView, FactionActor>();
        private static readonly KeyValuePair<int, int> targetHash = ZDO.GetHashZDOID("toomanylags.fgn.ai.target");
        private static readonly int sourceHash = "toomanylags.fgn.ai.source".GetStableHashCode();
        private static readonly int revisionHash = "toomanylags.fgn.ai.revision".GetStableHashCode();
        private static readonly int capabilityHash = "toomanylags.fgn.ai.protocol".GetStableHashCode();
        private static readonly Func<BaseAI, ZNetView> aiView = FieldReader.Create<BaseAI, ZNetView>(AccessTools.Field(typeof(BaseAI), "m_nview"));
        private static ZDOMan world;
        private static ZNet network;
        private static int passMoves;
        private static uint pass;
        private static double forwardExpiry;
        private static bool collecting;
        private const long Protocol = 5L;

        internal static void Install(ConfigFile config, Assembly assembly, System.Version version, bool allowInstall = true)
        {
            configured = config.Bind("03 - AI Mob Ownership", "EnableAIOwnership", true,
                "Server only; restart required. Enable target-based Hostile Mob ownership. Clients follow the server; their local value is ignored. Attacks block transfers until attack end.");
            bossAlwaysServerOwned = config.Bind("03 - AI Mob Ownership", "BossAlwaysServerOwned", true,
                "Server only; restart required. On a boss player-target change, use server ownership if at least two living players are within BossPlayerRadius. Otherwise bosses follow normal Hostile Mob target rules. Requires EnableAIOwnership and Server-Side Simulation for server ownership. No continuous radius checks; unchanged targets keep their last decision.");
            bossPlayerRadius = config.Bind("03 - AI Mob Ownership", "BossPlayerRadius", 30f,
                new ConfigDescription("Server only; restart required. Player distance from the boss in metres for BossAlwaysServerOwned. Evaluated on player-target acquisition, change or loss, not movement.", new AcceptableValueRange<float>(1f, 100f)));
            defaultMobs = config.Bind("02 - Default Ownership and Overrides", "ServerOwnedMobsByDefault", false,
                "Server only; restart required. Give idle tamed animals a server default outside player fields, even when the interactive default is off. Fixed mob spawners also default to the server and stay outside the grid; ambient spawning stays native. Hostile Mobs without a player target use guarded server handoffs (requires EnableAIOwnership); player targets keep normal target ownership. Friendly magic summons, bosses and active riding/following stay excluded. Requires Server-Side Simulation; can increase server CPU usage.");
            serverDefaultsConfigured = defaultMobs.Value;
            maximumPing = config.Bind("03 - AI Mob Ownership", "ServerFallbackPingMs", 200,
                new ConfigDescription("Server only; live console supported. Target ping limit in milliseconds for server fallback with Server-Side Simulation, or a lower-latency nearby player with NoServerSideSimulation=true. Requires another eligible FiresGhettoNetworking player within ServerFallbackRadius. 0: target ping must exceed the best other candidate by 250 ms.", new AcceptableValueRange<int>(0, 2000)));
            nearbyRadius = config.Bind("03 - AI Mob Ownership", "ServerFallbackRadius", 6f,
                new ConfigDescription("Server only; live console supported. Distance in metres to another eligible FiresGhettoNetworking player for high-ping fallback. With Server-Side Simulation the server takes over; otherwise a lower-latency nearby player. The candidate must have the mob in its active area.", new AcceptableValueRange<float>(1f, 100f)));
            StartupError = null;
            detectedVersion = version;
            InstallationStatus = assembly == null ? "FGN absent" : "disabled in config";
            // A passive per-connection endpoint answers OFF as well. It never patches FGN,
            // ownership or AI when the feature is disabled and has no runtime polling.
            if (assembly != null)
            {
                try
                {
                    sessionHarmony = new Harmony(Plugin.Id + ".fgn.aimode");
                    sessionHarmony.Patch(AccessTools.Method(typeof(ZNet), "OnNewConnection"), prefix: Hook("SessionConnection"));
                }
                catch (Exception error)
                {
                    if (sessionHarmony != null) { try { sessionHarmony.UnpatchSelf(); } catch { } }
                    InstallationStatus = "mode endpoint failed: " + error.GetBaseException().Message;
                    StartupError = InstallationStatus;
                    Plugin.Warn("Optional FGN AI mode unavailable: " + error.Message); return;
                }
            }
            if (!configured.Value || assembly == null) return;
            if (!allowInstall)
            {
                InstallationStatus = "inactive: another AI ownership policy is installed";
                return;
            }
            try
            {
                // Bind and validate the actual contracts below. Version metadata
                // alone must never disable an otherwise compatible FGN update.
                Fgn = new FgnCombatBridge(assembly);
                FgnBossPolicy.Configure(bossAlwaysServerOwned.Value, bossPlayerRadius.Value);
                RewriteTarget(PatchProcessor.GetOriginalInstructions(Fgn.TryHandOff));
                RewriteVeto(PatchProcessor.GetOriginalInstructions(Fgn.Veto));
                FgnMobHandoff.RewriteResult(PatchProcessor.GetOriginalInstructions(Fgn.OnHandOff));
                MethodInfo release = AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS");
                RewriteNativeOwner(PatchProcessor.GetOriginalInstructions(release));
                harmony = new Harmony(Plugin.Id + ".fgn.aiownership");
                harmony.Patch(Fgn.TryHandOff, prefix: Hook("TryPrefix"), postfix: Hook("TryPostfix"), transpiler: Hook("RewriteTarget"));
                harmony.Patch(Fgn.Veto, postfix: Hook("VetoPostfix"), transpiler: Hook("RewriteVeto"));
                FgnBossPolicy.Install(harmony, assembly);
                harmony.Patch(Fgn.OnResult, prefix: Hook("ResultPrefix"), postfix: Hook("ResultPostfix"));
                harmony.Patch(Fgn.OnHandOff,
                    prefix: Hook("BeforeOnHandOff"),
                    transpiler: new HarmonyMethod(AccessTools.Method(typeof(FgnMobHandoff), "RewriteResult")));
                harmony.Patch(AccessTools.Method(typeof(ZDOMan), "RPC_ZDOData"), postfix: new HarmonyMethod(AccessTools.Method(typeof(FgnMobHandoff), "AfterReplication")));
                harmony.Patch(Fgn.Balance, prefix: Hook("BalancePrefix"), postfix: Hook("BalancePostfix"));
                harmony.Patch(release, transpiler: Hook("RewriteNativeOwner"));
                FgnTargetEvents.Install(harmony, assembly, Watching, Observe, Fail);
                FgnAttackRetry.Install(harmony, OnAttackEnded, Fail);
                FgnTargetBoundary.Install(harmony);
                harmony.Patch(AccessTools.Method(typeof(BaseAI), "Awake"), postfix: Hook("AiAwakePostfix"));
                if (serverDefaultsConfigured)
                {
                    harmony.Patch(AccessTools.Method(typeof(ZNetScene), "AddInstance", new[] { typeof(ZDO), typeof(ZNetView) }), postfix: Hook("DefaultLoadedPostfix"));
                    harmony.Patch(AccessTools.Method(typeof(ZNetView), "ResetZDO"), prefix: Hook("DefaultUnloadedPrefix"));
                    harmony.Patch(AccessTools.Method(typeof(ZNetView), "OnDestroy"), prefix: Hook("DefaultUnloadedPrefix"));
                    harmony.Patch(AccessTools.Method(typeof(ZNetScene), "Shutdown"), prefix: Hook("ClearDefaultLoaded"));
                    harmony.Patch(AccessTools.Method(typeof(ZNetScene), "OnDestroy"), prefix: Hook("ClearDefaultLoaded"));
                }
                harmony.Patch(AccessTools.Method(typeof(Player), "OnDestroy"), prefix: Hook("PlayerDestroyPrefix"));
                harmony.Patch(AccessTools.Method(typeof(ZNet), "SetCharacterID"), postfix: Hook("CapabilityPostfix"));
                harmony.Patch(AccessTools.Method(typeof(Character), "RPC_Damage"), prefix: Hook("DamagePrefix"));
                harmony.Patch(AccessTools.Method(typeof(ZNet), "Shutdown"), prefix: Hook("Reset"));
                harmony.Patch(AccessTools.Method(typeof(ZNetScene), "Shutdown"), prefix: Hook("ResetFactions"));
                harmony.Patch(AccessTools.Method(typeof(ZNetScene), "OnDestroy"), prefix: Hook("ResetFactions"));
                Enabled = true;
                InstallationStatus = "installed";
            }
            catch (Exception error)
            {
                Enabled = false;
                if (harmony != null) { try { harmony.UnpatchSelf(); } catch { } }
                Fgn = null;
                InstallationStatus = "installation failed: " + error.GetBaseException().Message;
                StartupError = InstallationStatus;
                Plugin.Warn("Optional FGN AI ownership not installed: " + error.Message);
            }
        }

        private static HarmonyMethod Hook(string name) { return new HarmonyMethod(AccessTools.Method(typeof(FgnCombatOwnership), name)); }
        private static bool Active { get { return Enabled && Fgn != null && Fgn.Active(); } }
        // On-demand diagnostics only. Never initialize a world, publish a report,
        // acknowledge a watch or request ownership from a status query.
        internal static IEnumerable<string> DescribeStatus()
        {
            yield return "TooManyLags " + Plugin.Version + "; FGN " + (detectedVersion == null ? "absent" : detectedVersion.ToString());
            yield return "AI ownership: config=" + (configured == null ? "unknown" : configured.Value.ToString()) +
                "; hooks=" + Enabled + "; " + InstallationStatus;
            bool server = ZNet.instance && ZNet.instance.IsDedicated();
            yield return "Mode: " + (!ZNet.instance ? "no session" : server ? "dedicated server" : "client/local game") +
                "; server balance=" + (server ? Active.ToString() : "n/a") + "; server confirmed=" + serverConfirmed;
            bool current = ZDOMan.instance != null && ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance);
            yield return "Current session=" + current + "; watched=" + watching.Count + "; reports=" + receivedTargets.Count + "; server leases=" + managed.Count;
            if (!current || !ZNet.instance) yield break;
            if (!server)
            {
                ZDO player = world.GetZDO(ZNet.instance.LocalPlayerCharacterID);
                yield return "Local peer=" + ZDOMan.GetSessionID() + "; player=" + ZNet.instance.LocalPlayerCharacterID +
                    "; protocol=" + (player == null ? "missing player ZDO" : player.GetLong(capabilityHash, 0L).ToString()) + " (required: " + Protocol + ")";
            }
            else if (Fgn != null)
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                {
                    if (peer == null) continue;
                    ZDO player = world.GetZDO(peer.m_characterID);
                    yield return "Peer " + peer.m_uid + "; player=" + peer.m_characterID + ": ready=" + peer.IsReady() + "; FGN matched=" + Fgn.Matched(peer) +
                        "; protocol=" + (player == null ? "missing player ZDO" : player.GetLong(capabilityHash, 0L).ToString());
                }
        }
        internal static string DescribeMob(ZDO data, BaseAI ai)
        {
            if (data == null || !data.IsValid()) return "mob ZDO unavailable";
            ZDOID target;
            string report = ReadTarget(data, out target) ? target.IsNone() ? "none/non-player" : target.ToString() : "missing/stale";
            string local = "unavailable";
            ZNetView view = ai ? aiView(ai) : null;
            if (ai && view && view.IsValid() && ReferenceEquals(view.GetZDO(), data))
            {
                Character actual = ai.GetTargetCreature();
                local = (actual && actual is Player && !actual.IsDead() ? actual.GetZDOID().ToString() : "none/non-player") +
                    (view.IsOwner() ? " (authoritative)" : " (replica; not authoritative)");
            }
            return "mob=" + data.m_uid + "; owner=" + data.GetOwner() + "; revision=" + data.OwnerRevision +
                "; rule=" + DiagnosticRule(data) + "; AI=" + local + "; report=" + report + DiagnosticLastHit(data.m_uid) + FgnTargetBoundary.Describe(ai);
        }
        private static string DiagnosticRule(ZDO data)
        {
            if (!Enabled || Fgn == null) return "stock FGN: " + InstallationStatus;
            if (!ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance) || !ZNet.instance)
                return "stock FGN: session not initialized";
            bool server = ZNet.instance.IsDedicated();
            if (server && !Active) return "stock FGN: server balance inactive";
            if (!server && !serverConfirmed) return "stock FGN: server mode unconfirmed";
            if (IsFriendlyCreature(data)) return "outside AI policy: friendly player creature (ownership unchanged)";
            if (data.GetBool(ZDOVars.s_tamed)) return "outside AI policy: tamed (ownership unchanged)";
            if (data.GetLong(ZDOVars.s_user, 0L) != 0L) return "outside AI policy: controlled/ridden";
            if (Fgn.Held(data) && !ShouldOverride(data.m_uid)) return "stock FGN: external combat hold";
            if (Fgn.Possessed(data)) return "outside AI policy: Valcast possessed";
            if (server && data.GetOwner() == ZDOMan.GetSessionID() && !managed.ContainsKey(data.m_uid)) return "outside AI policy: server-owned";
            if (server && data.GetOwner() == ZDOMan.GetSessionID() && ShouldOverride(data.m_uid)) return ServerDefaults ? "AI target / server default" : "AI target (temporary server lease)";
            if (server && data.GetOwner() != ZDOMan.GetSessionID() && !Participating(ZNet.instance.GetPeer(data.GetOwner())))
                return "stock FGN: owner protocol/FGN participation missing";
            if (!server && data.GetOwner() != ZDOMan.GetSessionID())
                return "observer: only the current owner receives the server watch";
            if (!Watched(data)) return ServerDefaults ? "waiting for loaded-mob ownership watch" : "stock FGN: not marked by two-player scan; check SERVER row";
            Watch watch = watching[data.m_uid];
            if (server && !watch.Acknowledged) return "waiting for owner's initial target report";
            return ShouldOverride(data.m_uid) ? "AI target" : "stock FGN: no valid target report";
        }
        private static string DiagnosticLastHit(ZDOID id)
        {
            // Box/reflection only on explicit command, never in combat work.
            if (Fgn == null) return "";
            FieldInfo field = AccessTools.Field(Fgn.TryHandOff.DeclaringType, "s_lastHitBy");
            System.Collections.IDictionary hits = field == null ? null : field.GetValue(null) as System.Collections.IDictionary;
            if (hits == null || !hits.Contains(id)) return "; last attacker=none";
            object stamp = hits[id]; Type type = stamp.GetType();
            long peer = (long)type.GetField("Peer").GetValue(stamp);
            double at = (double)type.GetField("At").GetValue(stamp);
            return "; last attacker=" + peer + "; age=" + (Time.realtimeSinceStartupAsDouble - at).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }
        internal static string DescribeServerMob(ZDOID id)
        {
            // Explicit diagnostic request only. Read FGN's existing scan state;
            // never run a scan, initialize a session or change a watch/owner here.
            if (!ZNet.instance || !ZNet.instance.IsDedicated()) return "SERVER: dedicated session unavailable";
            if (Fgn == null || !Enabled) return "SERVER: " + InstallationStatus;
            ZDO data = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(id);
            if (data == null || !data.IsValid()) return "SERVER mob=" + id + ": ZDO unavailable";
            string line = "SERVER " + DescribeMob(data, null) + "; scan pass=" + pass;
            Type type = Fgn.Balance.DeclaringType;
            FieldInfo scanned = AccessTools.Field(type, "s_prefabsScanned"), prefabsField = AccessTools.Field(type, "s_creaturePrefabs"),
                maskField = AccessTools.Field(type, "s_creatureTypeMask"), visitedField = AccessTools.Field(type, "s_visited");
            HashSet<int> prefabs = prefabsField == null ? null : prefabsField.GetValue(null) as HashSet<int>;
            HashSet<ZDOID> visited = visitedField == null ? null : visitedField.GetValue(null) as HashSet<ZDOID>;
            line += "; next scan=" + (Fgn.NextPass() - Time.realtimeSinceStartupAsDouble).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s" +
                "; prefabs scanned=" + (scanned == null ? "unknown" : scanned.GetValue(null).ToString()) +
                "; prefab=" + (prefabs == null ? "unknown" : prefabs.Contains(data.GetPrefab()).ToString()) +
                "; type=" + (maskField == null ? "unknown" : (((int)maskField.GetValue(null) & (1 << (int)data.Type)) != 0).ToString()) +
                "; visited=" + (visited == null ? "unknown" : visited.Contains(id).ToString());
            int candidates = 0;
            foreach (ZNetPeer candidate in Fgn.Candidates) if (Candidate(candidate, data.GetPosition())) candidates++;
            line += "; FGN candidates=" + Fgn.Candidates.Count + "; eligible near mob=" + candidates;
            ZNetPeer owner = ZNet.instance.GetPeer(data.GetOwner());
            ZDO player = ZDOMan.instance == null || owner == null ? null : ZDOMan.instance.GetZDO(owner.m_characterID);
            line += "; owner ready=" + (owner != null && owner.IsReady()) + "; owner matched=" + (owner != null && Fgn.Matched(owner)) +
                "; owner in area=" + Candidate(owner, data.GetPosition()) +
                "; owner protocol=" + (player == null ? "missing player ZDO" : player.GetLong(capabilityHash, 0L).ToString()) +
                "; player ZDO owner=" + (player == null ? "missing" : player.GetOwner().ToString());
            Watch watch;
            if (watching.TryGetValue(id, out watch))
                line += "; watch owner=" + watch.Owner + "; watch revision=" + watch.Revision + "; acknowledged=" + watch.Acknowledged;
            else line += "; server watch=missing";
            long targetPeer; double cooldown;
            line += "; reported target peer=" + (TryTargetPeer(id, out targetPeer) ? targetPeer == 0 ? "none/non-player" : targetPeer.ToString() : "missing/stale") +
                "; transfer pending=" + Fgn.Pending(id, Time.realtimeSinceStartupAsDouble) +
                "; cooldown=" + (Fgn.Cooldowns.TryGetValue(id, out cooldown) ? Math.Max(0, cooldown - Time.realtimeSinceStartupAsDouble) : 0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "s";
            return line;
        }
        private static void EnsureWorld()
        {
            if (ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance)) return;
            Reset(); world = ZDOMan.instance; network = ZNet.instance;
            if (world == null) return;
            ZDOExtraData.AddSessionHash(targetHash.Key); ZDOExtraData.AddSessionHash(targetHash.Value);
            ZDOExtraData.AddSessionHash(sourceHash); ZDOExtraData.AddSessionHash(revisionHash);
            ZDOExtraData.AddSessionHash(capabilityHash);
        }
        private static void Reset()
        {
#if TML_DEBUG_SYSTEM
            FgnSoloCombatTest.Reset();
#endif
            defaultLoaded.Clear();
            ResetFactions();
            managed.Clear(); forwarded.Clear(); watching.Clear(); receivedTargets.Clear(); scratch.Clear(); reports = new ConditionalWeakTable<BaseAI, Report>();
            completedAi = new ConditionalWeakTable<BaseAI, AiPass>();
            FgnTargetEvents.Reset(); FgnAttackRetry.Reset(); collecting = false;
            FgnMobHandoff.Reset(); FgnTargetBoundary.Reset();
            MobSourceHandoff.Reset();
            FgnBossPolicy.Reset();
            FgnCombatStatus.Reset();
            serverConfirmed = false; localCapability = null;
            world = null; network = null; passMoves = 0; forwardExpiry = 0;
        }
        internal static void Stop()
        {
            MobSourceHandoff.Stop();
            bool wasEnabled = Enabled; Enabled = false;
            if (wasEnabled) InstallationStatus = "stopped";
            if (wasEnabled) NotifyServerOff();
            try { RevokeCapability(); ReleaseManaged(); }
            catch (Exception error) { Plugin.Warn("Optional FGN AI cleanup: " + error.Message); }
            finally { Enabled = false; Reset(); }
            if (harmony != null) { try { harmony.UnpatchSelf(); } catch (Exception error) { Plugin.Warn(error.Message); } }
            harmony = null; Fgn = null;
            if (sessionHarmony != null) { try { sessionHarmony.UnpatchSelf(); } catch (Exception error) { Plugin.Warn(error.Message); } }
            sessionHarmony = null;
        }
        private static void Fail(Exception error)
        {
            // A combat-policy fault leaves the other plugin modules active.
            bool wasEnabled = Enabled; Enabled = false;
            InstallationStatus = "runtime failure: " + error.GetBaseException().Message;
            if (wasEnabled) NotifyServerOff();
            try { RevokeCapability(); Enabled = false; ReleaseManaged(); }
            catch (Exception cleanup) { Plugin.Warn("Optional FGN AI cleanup: " + cleanup.Message); }
            finally
            {
                Enabled = false; defaultLoaded.Clear(); managed.Clear(); forwarded.Clear(); watching.Clear(); receivedTargets.Clear();
                FgnTargetEvents.Reset(); FgnAttackRetry.Reset(); FgnMobHandoff.Reset(); FgnTargetBoundary.Reset();
#if TML_DEBUG_SYSTEM
                FgnSoloCombatTest.Reset();
#endif
                ResetFactions(); forwardExpiry = 0;
            }
            Plugin.Warn("Optional FGN AI ownership disabled: " + error.Message);
        }
        private static void NotifyServerOff()
        {
            // A live server stop/fault must restore client vetoes as well. Cleanup
            // remains independent if a disconnected peer cannot receive this event.
            try
            {
                if (!ZNet.instance || !ZNet.instance.IsDedicated()) return;
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                    if (peer != null && peer.IsReady() && peer.m_rpc != null)
                        try { peer.m_rpc.Invoke("TooManyLags_AI_Mode", 3); }
                        catch (Exception sendError) { Plugin.Warn(sendError.Message); }
            }
            catch (Exception error) { Plugin.Warn(error.Message); }
        }
        private static void ReleaseManaged()
        {
            if (ZNet.instance && ZNet.instance.IsDedicated() && ReferenceEquals(world, ZDOMan.instance))
                foreach (Lease lease in managed.Values)
                    if (lease.Data.IsValid() && lease.Data.GetOwner() == ZDOMan.GetSessionID() &&
                        (Fgn == null || !Fgn.Possessed(lease.Data)) && !IsFriendlyCreature(lease.Data))
                    { lease.Data.SetOwner(0L); if (Fgn != null) Fgn.ClearOwned(lease.Data.m_uid); }
            managed.Clear();
        }

        internal static void ConnectCurrentServer()
        {
            ZNetPeer peer = ZNet.instance ? ZNet.instance.GetServerPeer() : null;
            if (peer == null || !peer.IsReady() || peer.m_rpc == null) return;
            SessionConnection(peer);
            if (FlightProbe != null) FlightProbe(peer);
            if (Enabled)
            {
                MobSourceHandoff.Probe(peer);
                peer.m_rpc.Invoke("TooManyLags_AI_Mode", 1); FgnTargetBoundary.Probe(peer);
#if TML_DEBUG_SYSTEM
                FgnSoloCombatTest.Probe(peer);
#endif
            }
        }
        private static void SessionConnection(ZNetPeer __0)
        {
            if (__0 == null || __0.m_rpc == null) return;
            ZNetPeer peer = __0;
            FgnCombatStatus.Connect(peer);
            FgnMobHandoff.Connect(peer);
            FgnTargetBoundary.Connect(peer);
            MobSourceHandoff.Connect(peer);
            if (FlightConnect != null) FlightConnect(peer);
#if TML_DEBUG_SYSTEM
            FgnSoloCombatTest.Connect(peer);
#endif
            peer.m_rpc.Register<int>("TooManyLags_AI_Mode", delegate(ZRpc rpc, int mode)
            {
                try
                {
                    if (!ZNet.instance) return;
                    if (FlightMode != null && FlightMode(peer, mode)) return;
                    if (FgnTargetBoundary.Mode(peer, mode, Active)) return;
#if TML_DEBUG_SYSTEM
                    if (FgnSoloCombatTest.Mode(peer, mode)) return;
#endif
                    if (ZNet.instance.IsDedicated())
                    {
                        if (mode == 1) peer.m_rpc.Invoke("TooManyLags_AI_Mode", Active ? 7 : 3);
                        return;
                    }
                    if (!peer.m_server || !ReferenceEquals(ZNet.instance.GetServerPeer(), peer) || (mode < 2 || mode > 7)) return;
                    // Identified requests require the matching protocol on both ends.
                    // Older clients/servers retain stock FGN without unknown RPCs.
                    EnsureWorld(); serverConfirmed = mode == 7;
                    if (!serverConfirmed) { RevokeCapability(); return; }
                    PublishCapability(ZNet.instance.LocalPlayerCharacterID);
                    MobSourceHandoff.Probe(peer);
                }
                catch (Exception error) { Fail(error); }
            });
            peer.m_rpc.Register<ZDOID, long, long>("TooManyLags_AI_Watch", delegate(ZRpc rpc, ZDOID id, long owner, long revision)
            {
                try { ReceiveWatch(peer, id, owner, revision); } catch (Exception error) { Fail(error); }
            });
            peer.m_rpc.Register<ZDOID, ZDOID, long>("TooManyLags_AI_Target", delegate(ZRpc rpc, ZDOID id, ZDOID target, long revision)
            {
                try { ReceiveTarget(peer, id, target, revision); } catch (Exception error) { Fail(error); }
            });
            peer.m_rpc.Register<ZDOID, long>("TooManyLags_AI_AttackReady", delegate(ZRpc rpc, ZDOID id, long revision)
            {
                try { ReceiveAttackReady(peer, id, revision); } catch (Exception error) { Fail(error); }
            });
        }
        private static void CapabilityPostfix(ZDOID __0)
        {
            if (!Enabled || ZDOMan.instance == null || __0.IsNone()) return;
            try
            {
                EnsureWorld();
                if (ZNet.instance.IsDedicated()) return;
                ZNetPeer server = ZNet.instance.GetServerPeer();
                if (server != null && server.IsReady())
                {
                    server.m_rpc.Invoke("TooManyLags_AI_Mode", 1); FgnTargetBoundary.Probe(server);
                    if (FlightProbe != null) FlightProbe(server);
#if TML_DEBUG_SYSTEM
                    FgnSoloCombatTest.Probe(server);
#endif
                }
                if (serverConfirmed) PublishCapability(__0);
            }
            catch (Exception error) { Fail(error); }
        }
        private static void PublishCapability(ZDOID id)
        {
            if (!Enabled || !serverConfirmed || world == null) return;
            ZDO player = world.GetZDO(id);
            if (player != null && player.IsValid() && player.GetOwner() == ZDOMan.GetSessionID())
            { player.Set(capabilityHash, Protocol); localCapability = player; }
        }
        private static void RevokeCapability()
        {
            if (localCapability != null && localCapability.IsValid() && ReferenceEquals(world, ZDOMan.instance) &&
                localCapability.GetOwner() == ZDOMan.GetSessionID()) localCapability.Set(capabilityHash, 0L);
            localCapability = null; serverConfirmed = false;
            if (ZNet.instance && !ZNet.instance.IsDedicated())
            {
                watching.Clear(); receivedTargets.Clear(); FgnTargetEvents.Reset(); FgnAttackRetry.Reset(); FgnTargetBoundary.Reset();
#if TML_DEBUG_SYSTEM
                FgnSoloCombatTest.Reset();
#endif
            }
        }
        internal static bool ShouldOverride(ZDOID mob)
        {
            if (!Enabled || Fgn == null || ZDOMan.instance == null || !ZNet.instance) return false;
            ZDO data = ZDOMan.instance.GetZDO(mob);
            if (data == null || !data.IsValid() || Fgn.Possessed(data) || IsFriendlyCreature(data)) return false;
            Lease lease;
            if (ZNet.instance.IsDedicated() && data.GetOwner() == ZDOMan.GetSessionID() &&
                managed.TryGetValue(mob, out lease) && ReferenceEquals(lease.Data, data)) return true;
            ZNetPeer peer = ZNet.instance.GetPeer(data.GetOwner());
            // Clients do not have other client peers; their own valid report is sufficient for the local veto.
            ZDOID target;
            if (!ZNet.instance.IsDedicated()) return serverConfirmed && Watched(data) && ReadTarget(data, out target);
            Watch watch;
            return Participating(peer) && Watched(data) && watching.TryGetValue(mob, out watch) && watch.Acknowledged;
        }
        private static bool Participating(ZNetPeer peer)
        {
            if (peer == null || !peer.IsReady() || !Fgn.Matched(peer) || peer.m_characterID.IsNone()) return false;
            ZDO player = world.GetZDO(peer.m_characterID);
            return player != null && player.IsValid() && player.GetOwner() == peer.m_uid && player.GetLong(capabilityHash, 0L) == Protocol;
        }
        internal static bool HandoffSource(ZDO data, long source, ZRpc rpc)
        {
            if (!Active || !ZNet.instance.IsDedicated() || !ReferenceEquals(world, ZDOMan.instance) ||
                !ReferenceEquals(network, ZNet.instance) || data == null || !ReferenceEquals(world.GetZDO(data.m_uid), data) || !Eligible(data)) return false;
            ZNetPeer peer = network.GetPeer(source);
            return Participating(peer) && ReferenceEquals(peer.m_rpc, rpc) && Candidate(peer, data.GetPosition());
        }
        internal static bool HandoffServer(ZNetPeer peer)
        {
            return Enabled && serverConfirmed && ZNet.instance && !ZNet.instance.IsDedicated() &&
                ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance) &&
                peer != null && peer.IsReady() && peer.m_server && ReferenceEquals(peer, network.GetServerPeer());
        }
        internal static bool HandoffObserve(ZDO data)
        {
            ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            BaseAI ai = view ? view.GetComponent<BaseAI>() : null;
            if (!Watching(ai)) return false;
            Observe(ai); return Enabled;
        }
        private static bool BeforeOnHandOff(ZRpc rpc, ZDOID id, long newOwner)
        {
            if (!Enabled || Fgn == null || rpc == null || id.IsNone() || ZDOMan.instance == null) return true;
            ZDO data = ZDOMan.instance.GetZDO(id);
#if TML_DEBUG_SYSTEM
            if (FgnSoloCombatTest.Active) return true;
#endif
            if (data == null || !data.IsValid() || TargetAllowsOwnership(data)) return true;
            FgnMobHandoff.SendResult(rpc, "FGN_HandOffResult", new object[] { id, false, "non-player target" });
            return false;
        }
        internal static bool TargetAllowsOwnership(ZDO data)
        {
            Character actor = LoadedCharacter(data);
            BaseAI ai = actor ? actor.GetComponent<BaseAI>() : null;
            Character target = ai ? ai.GetTargetCreature() : null;
#if TML_DEBUG_SYSTEM
            if (FgnSoloCombatTest.Selected(data)) return !target || !target.IsDead();
#endif
            return !target || target is Player && !target.IsDead();
        }
        internal static void HandoffRetry(ZDO data)
        {
            ZDOID target;
            if (Active && Eligible(data) && ReadTarget(data, out target)) ProcessTargetEvent(data, target);
        }
        internal static void HandoffChanged(ZDO data)
        {
            if (Active && Eligible(data)) MarkWatch(data);
        }
        internal static void HandoffFailure(Exception error) { Fail(error); }
#if TML_DEBUG_SYSTEM
        internal static bool SoloEligible(ZDO data, ZNetPeer peer)
        {
            if (!Active || !Participating(peer) || !Eligible(data) || !Candidate(peer, data.GetPosition()) ||
                (data.GetOwner() != peer.m_uid && data.GetOwner() != ZDOMan.GetSessionID())) return false;
            Character mob = LoadedCharacter(data);
            return mob && !mob.IsDead() && !mob.IsBoss() && mob.GetComponent<MonsterAI>();
        }
        internal static bool SoloEligiblePending(ZDO data, ZNetPeer peer)
        {
            if (!Active || !Participating(peer) || !Eligible(data) ||
                (data.GetOwner() != peer.m_uid && data.GetOwner() != ZDOMan.GetSessionID())) return false;
            Character mob = LoadedCharacter(data);
            return mob && !mob.IsDead() && !mob.IsBoss() && mob.GetComponent<MonsterAI>();
        }
        internal static void SoloPrepare(ZDO data, long player)
        {
            if (!FgnSoloCombatTest.Selected(data)) return;
            if (data.GetOwner() == ZDOMan.GetSessionID() && !managed.ContainsKey(data.m_uid))
                managed[data.m_uid] = new Lease { Data = data, ReturnPeer = player, SourceOwner = data.GetOwner(), SourceRevision = data.OwnerRevision, Pass = pass };
            MarkWatch(data);
        }
#endif
        private static void AiAwakePostfix(BaseAI __instance)
        {
            // An initial native assignment has no source client to report its
            // target. Bootstrap through the same deferred owner observation.
            if (ServerDefaults && managed.Count != 0)
            {
                ZNetView view = aiView(__instance); Lease lease;
                ZDO data = view && view.IsValid() ? view.GetZDO() : null;
                if (data != null && managed.TryGetValue(data.m_uid, out lease) && lease.Initial &&
                    ReferenceEquals(lease.Data, data) && data.GetOwner() == ZDOMan.GetSessionID() && data.OwnerRevision == lease.SourceRevision)
                {
                    lease.Initial = false;
                    watching[data.m_uid] = new Watch { Data = data, Owner = data.GetOwner(), Revision = data.OwnerRevision, Pass = pass };
                }
            }
            if (Watching(__instance)) FgnTargetEvents.Schedule(__instance);
        }
        private static void PlayerDestroyPrefix(Player __instance)
        {
            if (!Enabled || watching.Count == 0 || ReferenceEquals(__instance, null) || !ZNetScene.instance ||
                !ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance)) return;
            try
            {
                // Unity may destroy a target without another m_targetCreature store.
                // Only this rare despawn event inspects already watched local AIs.
                // Match the object reference: its ZDO/view may already be detached.
                long owner = ZDOMan.GetSessionID();
                foreach (Watch watch in watching.Values)
                {
                    if (watch.Owner != owner) continue;
                    ZNetView view = watch.Data == null ? null : ZNetScene.instance.FindInstance(watch.Data);
                    BaseAI ai = view ? view.GetComponent<BaseAI>() : null;
                    if (Watching(ai) && ReferenceEquals(ai.GetTargetCreature(), __instance)) FgnTargetEvents.Schedule(ai);
                }
            }
            catch (Exception error) { Fail(error); }
        }
        private static bool Watched(ZDO data)
        {
            Watch watch;
            return data != null && watching.TryGetValue(data.m_uid, out watch) &&
                (watch.Data == null || ReferenceEquals(watch.Data, data)) && watch.Owner == data.GetOwner() && watch.Revision == data.OwnerRevision;
        }
        internal static bool Watching(BaseAI ai)
        {
            if (!Enabled || !ai || !ai.isActiveAndEnabled || !ZNet.instance || ZDOMan.instance == null ||
                (!ZNet.instance.IsDedicated() && !serverConfirmed)) return false;
            ZNetView view = aiView(ai);
            return view && view.IsValid() && view.IsOwner() && ReferenceEquals(world, ZDOMan.instance) &&
                ReferenceEquals(network, ZNet.instance) && Watched(view.GetZDO()) && !IsFriendlyCreature(view.GetZDO());
        }
        internal static void Observe(BaseAI ai)
        {
            if (!Watching(ai)) return;
            try
            {
                if (!ZNet.instance || (!ZNet.instance.IsDedicated() && !serverConfirmed)) return;
                ZNetView view = aiView(ai);
                if (!view || !view.IsValid() || !view.IsOwner()) return;
                ZDO data = view.GetZDO();
                if (data.GetBool(ZDOVars.s_tamed) || data.GetLong(ZDOVars.s_user, 0L) != 0L) return;
                EnsureWorld();
                // A watch can arrive before its ZDO. Bind it after the first
                // valid owner/revision observation, including late AI Awake.
                Watch watch = watching[data.m_uid];
                if (watch.Data == null) watch.Data = data;
                Character target = ai.GetTargetCreature();
                ZDOID id = target && target is Player && !target.IsDead() ? target.GetZDOID() : ZDOID.None;
                AiPass first; Report previous;
                if (id.IsNone() && ai is MonsterAI && (!completedAi.TryGetValue(ai, out first) ||
                    !ReferenceEquals(first.Data, data) || first.Owner != data.GetOwner() || first.Revision != data.OwnerRevision) &&
                    (!reports.TryGetValue(ai, out previous) || !ReferenceEquals(previous.Data, data) ||
                     previous.Owner != data.GetOwner() || previous.Revision != data.OwnerRevision)) return;
                Report report = reports.GetOrCreateValue(ai); long owner = data.GetOwner();
                if (ReferenceEquals(report.Data, data) && report.Owner == owner && report.Revision == data.OwnerRevision && report.Target == id) return;
                data.Set(targetHash, id); data.Set(sourceHash, owner); data.Set(revisionHash, (long)data.OwnerRevision);
                report.Data = data; report.Owner = owner; report.Revision = data.OwnerRevision; report.Target = id;
                if (ZNet.instance.IsDedicated())
                {
                    watch.Acknowledged = true;
                    receivedTargets[data.m_uid] = report; ProcessTargetEvent(data, id);
                    FgnTargetBoundary.ServerObserved(ai);
                }
                else
                {
                    ZNetPeer server = ZNet.instance.GetServerPeer();
                    if (server != null && server.IsReady() && MobSourceHandoff.TryCommit(ai, data, id, server)) return;
                    if (server != null && server.IsReady() && !FgnTargetBoundary.Send(ai, data, id, server))
                        server.m_rpc.Invoke("TooManyLags_AI_Target", data.m_uid, id, (long)data.OwnerRevision);
                }
            }
            catch (Exception error) { Fail(error); }
        }
        internal static void CompletedAi(BaseAI ai)
        {
            if (!Watching(ai)) return;
            ZNetView view = aiView(ai); ZDO data = view.GetZDO();
            AiPass first = completedAi.GetOrCreateValue(ai);
            if (ReferenceEquals(first.Data, data) && first.Owner == data.GetOwner() && first.Revision == data.OwnerRevision) return;
            first.Data = data; first.Owner = data.GetOwner(); first.Revision = data.OwnerRevision;
            // Bootstrap None only after a real successful AI pass. Adoption and
            // target search must precede a server-default decision on new owners.
            FgnTargetEvents.Schedule(ai);
        }
        internal static bool ReadTarget(ZDO data, out ZDOID target)
        {
            target = ZDOID.None;
            Report received;
            if (data != null && data.IsValid() && ZNet.instance && ZNet.instance.IsDedicated() && ReferenceEquals(world, ZDOMan.instance) &&
                receivedTargets.TryGetValue(data.m_uid, out received) && ReferenceEquals(received.Data, data) &&
                received.Owner == data.GetOwner() && received.Revision == data.OwnerRevision)
            { target = received.Target; return true; }
            if (data == null || !data.IsValid() || data.GetOwner() == 0 ||
                data.GetLong(sourceHash, 0L) != data.GetOwner() || data.GetLong(revisionHash, -1L) != data.OwnerRevision) return false;
            target = data.GetZDOID(targetHash); return true;
        }
        internal static bool TryTargetPeer(ZDOID mob, out long peer)
        {
            peer = 0L; ZDOID id;
            if (ZDOMan.instance == null || !ReadTarget(ZDOMan.instance.GetZDO(mob), out id)) return false;
            if (id.IsNone()) return true;
            foreach (ZNetPeer candidate in ZNet.instance.GetPeers())
                if (candidate != null && candidate.IsReady() && candidate.m_characterID == id) { peer = candidate.m_uid; return true; }
            // An unavailable target is not proof that the mob is out of combat.
            return false;
        }

        private static void ReceiveWatch(ZNetPeer peer, ZDOID id, long owner, long revision)
        {
            if (!Enabled || !ZNet.instance || ZNet.instance.IsDedicated() || !serverConfirmed ||
                !peer.m_server || !ReferenceEquals(ZNet.instance.GetServerPeer(), peer) || id.IsNone() || revision < 0 || revision > UInt16.MaxValue) return;
            EnsureWorld();
            if (owner == 0) { watching.Remove(id); receivedTargets.Remove(id); return; }
            if (owner != ZDOMan.GetSessionID()) return;
            ZDO data = world.GetZDO(id);
            if (IsFriendlyCreature(data)) { watching.Remove(id); receivedTargets.Remove(id); return; }
            // Keep a pending watch even if ownership data or its scene instance arrives later.
            watching[id] = new Watch { Data = data, Owner = owner, Revision = (uint)revision };
            QueueBootstrap(data);
        }
        private static void ReceiveTarget(ZNetPeer peer, ZDOID id, ZDOID target, long revision)
        { ReceiveTargetCore(peer, id, target, revision, 0L); }
        internal static bool BoundaryTarget(ZNetPeer peer, ZDOID id, ZDOID target, long revision, long token)
        { return token > 0 && ReceiveTargetCore(peer, id, target, revision, token); }
        private static bool ReceiveTargetCore(ZNetPeer peer, ZDOID id, ZDOID target, long revision, long boundaryToken)
        {
            if (!Active || !ZNet.instance.IsDedicated() || world == null || !ReferenceEquals(world, ZDOMan.instance) ||
                !ReferenceEquals(network, ZNet.instance) || revision < 0 || revision > UInt16.MaxValue || !Participating(peer) ||
                !ReferenceEquals(network.GetPeer(peer.m_uid), peer)) return false;
            ZDO data = world.GetZDO(id); Watch watch;
            if (!Eligible(data) || data.GetOwner() != peer.m_uid || data.OwnerRevision != revision || !Watched(data) ||
                !watching.TryGetValue(id, out watch)) return false;
            Report previous; bool current = receivedTargets.TryGetValue(id, out previous) &&
                ReferenceEquals(previous.Data, data) && previous.Owner == peer.m_uid && previous.Revision == revision;
            watch.Acknowledged = true;
            if (boundaryToken > 0 && current && previous.BoundaryToken >= boundaryToken) return false;
            if (current && previous.Target == target)
            { if (boundaryToken > 0) previous.BoundaryToken = boundaryToken; return true; }
            receivedTargets[id] = new Report { Data = data, Owner = peer.m_uid, Revision = (uint)revision, Target = target, BoundaryToken = boundaryToken };
            ProcessTargetEvent(data, target);
            return true;
        }
        private static void ProcessTargetEvent(ZDO data, ZDOID target)
        {
            FgnBossPolicy.Record(data, target);
            // Optional server defaults also react to losing a player target.
            if ((!target.IsNone() || ServerDefaults || FgnBossPolicy.IsBoss(data)) && Active && Watched(data)) Fgn.TryTransfer(data, Time.realtimeSinceStartupAsDouble);
        }
        private static void RememberAttack(ZDO data, Character mob)
        {
            ZDOID target;
            if (!Enabled || Fgn == null || !ZNet.instance || ZDOMan.instance == null ||
                (!ZNet.instance.IsDedicated() && !serverConfirmed) ||
                !ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance) ||
                !Eligible(data) || data.GetOwner() != ZDOMan.GetSessionID() ||
                !ShouldOverride(data.m_uid) || !ReadTarget(data, out target) || target.IsNone()) return;
            if (!mob) mob = LoadedCharacter(data);
            if (mob) FgnAttackRetry.Arm(mob, data);
        }
        private static void OnAttackEnded(Humanoid mob, ZDO data)
        {
            if (!Enabled || Fgn == null || !ZNet.instance || !Eligible(data) ||
                !ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance) ||
                !ReferenceEquals(LoadedCharacter(data), mob) || data.GetOwner() != ZDOMan.GetSessionID() ||
                !ShouldOverride(data.m_uid)) return;
            // Stop also occurs during cancellation/attack chaining. Never force
            // a transfer through an animation which still counts as attacking.
            if (FgnTargetBoundary.Busy(mob)) { FgnAttackRetry.Arm(mob, data); return; }
            BaseAI ai = mob.GetComponent<BaseAI>();
            Player target = ai ? ai.GetTargetCreature() as Player : null;
            if (!target || target.IsDead()) return;
            if (ZNet.instance.IsDedicated())
            {
                if (!Active) return;
                CaptureLeaseTarget(data); Fgn.TryTransfer(data, Time.realtimeSinceStartupAsDouble); return;
            }
            if (!serverConfirmed || !Watching(ai)) return;
            ZDOID reported, actual = target.GetZDOID();
            if (!ReadTarget(data, out reported) || reported != actual)
                Observe(ai);
            // The target event may arrive while the rejected commit is still
            // in flight. Send readiness as well so that its reply can retry now.
            if (!Enabled || !Watching(ai)) return;
            ZNetPeer server = ZNet.instance.GetServerPeer();
            if (server != null && server.IsReady())
            {
                if (MobSourceHandoff.TryCommit(ai, data, actual, server)) return;
                server.m_rpc.Invoke("TooManyLags_AI_AttackReady", data.m_uid, (long)data.OwnerRevision);
            }
        }
        private static void ReceiveAttackReady(ZNetPeer peer, ZDOID id, long revision)
        {
            if (!Active || !ZNet.instance.IsDedicated() || world == null ||
                !ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance) ||
                revision < 0 || revision > UInt16.MaxValue || !Participating(peer) ||
                !ReferenceEquals(network.GetPeer(peer.m_uid), peer)) return;
            ZDO data = world.GetZDO(id); Watch watch; ZDOID target;
            if (!Eligible(data) || data.GetOwner() != peer.m_uid || data.OwnerRevision != revision ||
                !Watched(data) || !watching.TryGetValue(id, out watch) || !watch.Acknowledged ||
                !ReadTarget(data, out target) || target.IsNone()) return;
            if (FgnMobHandoff.Waiting(data) || Fgn.Pending(id, Time.realtimeSinceStartupAsDouble))
            { watch.AttackReady = true; return; } // Result may still be in flight.
            if (!watch.AttackRejected) return;
            watch.AttackReady = watch.AttackRejected = false;
            ProcessTargetEvent(data, target);
        }
        private static void QueueBootstrap(ZDO data)
        {
            ZNetView view = data != null && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            BaseAI ai = view ? view.GetComponent<BaseAI>() : null;
            if (!ai) return;
            reports.Remove(ai); FgnTargetEvents.Schedule(ai);
        }
        private static bool TwoCandidates(ZDO data)
        {
            int count = 0; Vector3 position = data.GetPosition();
            foreach (ZNetPeer candidate in Fgn.Candidates)
                if (Candidate(candidate, position) && ++count >= 2) return true;
            return false;
        }
        private static bool ServerDefaults
        { get { return serverDefaultsConfigured && Enabled && Fgn != null && ZNet.instance && ZNet.instance.IsDedicated() && Fgn.Simulation(); } }
        private static bool AnyLoadingPeer(Vector3 position)
        {
            // Vanilla first assignment needs a ready native loading area, not
            // a completed FGN greeting. Later player handoffs keep Candidate.
            long server = ZDOMan.GetSessionID();
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (peer != null && peer.m_uid != 0 && peer.m_uid != server && peer.IsReady() &&
                    Finite(peer.GetRefPos()) && ZNetScene.InActiveArea(position, peer.GetRefPos())) return true;
            return false;
        }
        private static void DefaultLoadedPostfix(ZDO zdo, ZNetView nview)
        {
            if (!ServerDefaults || zdo == null || !nview || !nview.IsValid() || !ReferenceEquals(nview.GetZDO(), zdo)) return;
            try
            {
                Character actor = nview.GetComponent<Character>();
                if (!actor || actor is Player || actor.IsBoss() || FriendlyCreatures.IsFriendly(actor)) return;
                EnsureWorld(); defaultLoaded[zdo.m_uid] = zdo;
                // ZNetView creates a new server ZDO already owned by this session.
                // Keep that first owner before vanilla can replace it with a peer.
                // Origin + zero owner revision exclude loaded/previously handed-off data.
                if (Active && zdo.m_uid.UserID == ZDOMan.GetSessionID() && zdo.OwnerRevision == 0 &&
                    zdo.GetOwner() == ZDOMan.GetSessionID() && Eligible(zdo) &&
                    zdo.Persistent && nview.m_persistent && !nview.m_distant && zdo.GetFloat(ZDOVars.s_health, 1f) > 0f &&
                    nview.GetComponent<BaseAI>() && nview.GetComponent<ZSyncTransform>() &&
                    ReferenceEquals(world.GetZDO(zdo.m_uid), zdo) && !managed.ContainsKey(zdo.m_uid) &&
                    AnyLoadingPeer(zdo.GetPosition()) && (ServerCollisionReady == null || ServerCollisionReady(zdo.GetPosition()))) RememberInitial(zdo);
                // Early loads may still lack an owner or client capability;
                // the existing balance pass retries only these loaded actors.
            }
            catch (Exception error) { Fail(error); }
        }
        private static void DefaultUnloadedPrefix(ZNetView __instance)
        {
            if ((defaultLoaded.Count == 0 && managed.Count == 0) || ReferenceEquals(__instance, null)) return;
            try
            {
                ZDO data = __instance.GetZDO(); ZDO known;
                Lease lease;
                if (data != null && managed.TryGetValue(data.m_uid, out lease) && lease.Initial && ReferenceEquals(lease.Data, data)) managed.Remove(data.m_uid);
                if (data == null || !defaultLoaded.TryGetValue(data.m_uid, out known) || !ReferenceEquals(known, data)) return;
                defaultLoaded.Remove(data.m_uid); managed.Remove(data.m_uid); RemoveWatch(data.m_uid); receivedTargets.Remove(data.m_uid);
            }
            catch (Exception error) { Fail(error); }
        }
        private static void ClearDefaultLoaded()
        {
            defaultLoaded.Clear(); scratch.Clear();
            foreach (var entry in managed) if (entry.Value.Initial) scratch.Add(entry.Key);
            foreach (ZDOID id in scratch) managed.Remove(id);
            scratch.Clear();
        }
        private static void ProcessDefaults()
        {
            scratch.Clear(); foreach (ZDOID id in defaultLoaded.Keys) scratch.Add(id);
            foreach (ZDOID id in scratch)
            {
                ZDO data;
                if (!defaultLoaded.TryGetValue(id, out data)) continue;
                ZNetView view = ReferenceEquals(world.GetZDO(id), data) && data.IsValid() && ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
                if (!view || !view.IsValid() || !ReferenceEquals(view.GetZDO(), data))
                { defaultLoaded.Remove(id); managed.Remove(id); RemoveWatch(id); receivedTargets.Remove(id); continue; }
                if (!MarkWatch(data) || passMoves >= 16) continue;
                Watch watch;
                if (watching.TryGetValue(id, out watch) && watch.Acknowledged)
                    Fgn.TryTransfer(data, Time.realtimeSinceStartupAsDouble);
            }
            scratch.Clear();
        }
        private static bool MarkWatch(ZDO data)
        {
            long owner = data.GetOwner();
            bool localLease = owner == ZDOMan.GetSessionID() && managed.ContainsKey(data.m_uid);
            ZNetPeer source = ZNet.instance.GetPeer(owner);
            if (!Eligible(data) || (!localLease && (!Participating(source) || !Candidate(source, data.GetPosition()))) ||
                ((ServerDefaults
#if TML_DEBUG_SYSTEM
                    || FgnSoloCombatTest.Selected(data)
#endif
                ) ? !LoadedCharacter(data) : !TwoCandidates(data)))
            { RemoveWatch(data.m_uid); return false; }
            Watch watch;
            if (!watching.TryGetValue(data.m_uid, out watch) || !ReferenceEquals(watch.Data, data) || watch.Owner != owner || watch.Revision != data.OwnerRevision)
            {
                RemoveWatch(data.m_uid);
                watch = new Watch { Data = data, Owner = owner, Revision = data.OwnerRevision };
                watching[data.m_uid] = watch;
            }
            watch.Pass = pass;
            if (!watch.Acknowledged)
            {
                if (owner == ZDOMan.GetSessionID()) QueueBootstrap(data);
                else
                {
                    ZNetPeer peer = ZNet.instance.GetPeer(owner);
                    peer.m_rpc.Invoke("TooManyLags_AI_Watch", data.m_uid, owner, (long)data.OwnerRevision);
                }
            }
            return true;
        }
        internal static long SourceDestination(ZDO data, ZDOID target)
        {
            ZNetPeer server = ZNet.instance ? ZNet.instance.GetServerPeer() : null;
            if (server == null) return 0L;
#if TML_DEBUG_SYSTEM
            long test;
            if (FgnSoloCombatTest.LocalDestination(data, out test)) return test;
#endif
            if (target.IsNone()) return serverDefaultsConfigured ? server.m_uid : 0L;
            ZDO player = world == null ? null : world.GetZDO(target);
            return player != null && player.IsValid() ? player.GetOwner() : 0L;
        }
        internal static void SourceCommit(ZRpc rpc, ZDOID id, long destination)
        {
            ZDO data = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(id);
            if (data == null || !data.IsOwner()) return;
            object visual = MobHandoffVisual.CaptureSource(data, destination);
            try { ZDOMan.instance.ForceSendZDO(id); MobFlightHandoff.Transfer(data, destination); }
            finally { MobHandoffVisual.CommitObserver(visual); }
            GC.KeepAlive(rpc);
        }
        internal static bool SourceLocalReady(ZDO data, ZDOID target, long destination)
        {
            if (!Enabled || !serverConfirmed || !ZNet.instance || ZNet.instance.IsDedicated() || !Eligible(data) || !data.IsOwner()) return false;
            ZNetPeer server = ZNet.instance.GetServerPeer();
            if (!HandoffServer(server) || !MobCombatState.CanCheckpoint(server)) return false;
            Character actor = LoadedCharacter(data); BaseAI ai = actor ? actor.GetComponent<BaseAI>() : null;
            if (!actor || !Watching(ai) || FgnTargetBoundary.Busy(actor)) return false;
            Player actual = ai.GetTargetCreature() as Player;
            if (target.IsNone()) return !actual && destination == server.m_uid;
            if (!actual || actual.IsDead() || actual.GetZDOID() != target) return false;
            ZDO player = world.GetZDO(target);
            return player != null && player.IsValid() && (destination == player.GetOwner() || destination == server.m_uid);
        }
        internal static void SourceAccepted(ZDO data, long source, ZDOID target, long destination)
        {
            if (!Active || data == null || !data.IsValid() || data.GetOwner() != destination) return;
            Fgn.ClearPending(data.m_uid); Fgn.ClearOwned(data.m_uid); Fgn.ClearStreak(data.m_uid);
            receivedTargets.Remove(data.m_uid);
            if (destination == ZDOMan.GetSessionID())
                managed[data.m_uid] = new Lease { Data = data, ReturnPeer = source, SourceOwner = source, SourceRevision = data.OwnerRevision - 1U,
                    Requested = Time.realtimeSinceStartupAsDouble, Pass = pass };
            else managed.Remove(data.m_uid);
            MarkWatch(data);
            ZNetPeer recipient = destination == ZDOMan.GetSessionID() ? null : ZNet.instance.GetPeer(destination);
            if (recipient != null && recipient.IsReady()) MobNativePublication.Send(data, recipient);
            GC.KeepAlive(target);
        }
        private static void RemoveWatch(ZDOID id)
        {
            MobSourceHandoff.Forget(id);
            Watch watch;
            if (!watching.TryGetValue(id, out watch)) return;
            watching.Remove(id);
            // A server exception still needs a report to return safely after unwatching.
            if (!managed.ContainsKey(id) || watch.Data == null || watch.Data.GetOwner() != ZDOMan.GetSessionID()) receivedTargets.Remove(id);
            if (!ZNet.instance || !ZNet.instance.IsDedicated() || watch.Owner == ZDOMan.GetSessionID()) return;
            ZNetPeer peer = ZNet.instance.GetPeer(watch.Owner);
            if (peer != null && peer.IsReady() && peer.m_rpc != null)
                peer.m_rpc.Invoke("TooManyLags_AI_Watch", id, 0L, (long)watch.Revision);
        }

        private static void BalancePrefix(out bool __state)
        {
            __state = false;
            // FGN's ZDOMan.Update caller runs on clients too. Active() is false
            // there by design; server cleanup must never clear a client's watch.
            if (!Enabled || !ZNet.instance || !ZNet.instance.IsDedicated()) return;
            collecting = false;
            try
            {
                EnsureWorld();
                if (!Active) { ReleaseManaged(); ClearWatches(); PruneForwarded(); return; }
                __state = Time.realtimeSinceStartupAsDouble >= Fgn.NextPass();
                if (__state) { passMoves = 0; pass++; Fgn.PerOwner.Clear(); collecting = true; }
                if (__state) FgnMobHandoff.AfterReplication();
            }
            catch (Exception error) { Fail(error); }
        }
        private static void BalancePostfix(bool __state)
        {
            collecting = false;
            if (!__state || !Active) return;
            try
            {
                if (ServerDefaults) ProcessDefaults();
                // FGN may stop its scan at its 16-transfer cap. Do not unwatch unseen but eligible mobs.
                scratch.Clear(); foreach (ZDOID id in watching.Keys) scratch.Add(id);
                foreach (ZDOID id in scratch)
                {
                    Watch watch;
                    if (!watching.TryGetValue(id, out watch) || watch.Pass == pass) continue;
                    ZDO data = world.GetZDO(id);
                    if (ReferenceEquals(data, watch.Data) && data.IsValid()) MarkWatch(data);
                    else RemoveWatch(id);
                }
                scratch.Clear();
                PruneForwarded();
                // Only exception leases: handles a solo player or a mob outside FGN's 3x3 scan.
                scratch.Clear(); foreach (ZDOID id in managed.Keys) scratch.Add(id);
                foreach (ZDOID id in scratch)
                {
                    Lease lease;
                    if (!managed.TryGetValue(id, out lease)) continue;
                    ZDO data = world.GetZDO(id);
                    if (lease.Initial && ReferenceEquals(data, lease.Data) && data.IsValid() && ServerDefaults && Eligible(data) &&
                        data.GetOwner() == ZDOMan.GetSessionID() && data.OwnerRevision == lease.SourceRevision)
                    {
                        if (!AnyLoadingPeer(data.GetPosition()))
                        { managed.Remove(id); data.SetOwner(0L); Fgn.ClearOwned(id); continue; }
                        // Reuse this existing, bounded-to-active-areas lease
                        // pass until the normal scene creates the actor.
                        if (!ZNetScene.instance.FindInstance(data)) continue;
                        lease.Initial = false;
                        if (MarkWatch(data)) QueueBootstrap(data);
                    }
                    if (!ReferenceEquals(data, lease.Data) || !Eligible(data) || !LoadedCharacter(data))
                    { managed.Remove(id); if (!watching.ContainsKey(id)) receivedTargets.Remove(id); continue; }
                    if (data.GetOwner() != ZDOMan.GetSessionID())
                    {
                        if (Time.realtimeSinceStartupAsDouble - lease.Requested >= 5.0)
                        { managed.Remove(id); if (!watching.ContainsKey(id)) receivedTargets.Remove(id); }
                        continue;
                    }
                    if (passMoves >= 16 || lease.Pass == pass) continue;
                    bool result = false;
                    Handle(data, Time.realtimeSinceStartupAsDouble, ref result);
                    if (result) passMoves++;
                }
                scratch.Clear();
            }
            catch (Exception error) { Fail(error); }
        }
        private static void ClearWatches()
        {
            if (watching.Count == 0 && receivedTargets.Count == 0) return;
            scratch.Clear(); foreach (ZDOID id in watching.Keys) scratch.Add(id);
            foreach (ZDOID id in scratch) RemoveWatch(id);
            scratch.Clear(); receivedTargets.Clear(); FgnTargetEvents.Reset(); FgnAttackRetry.Reset();
        }
        private static void CaptureLeaseTarget(ZDO data)
        {
            // Only an outstanding own server lease, once in the existing balance pass.
            Character mob = LoadedCharacter(data);
            BaseAI ai = mob ? mob.GetComponent<BaseAI>() : null;
            ZNetView view = ai ? aiView(ai) : null;
            if (!view || !view.IsOwner() || !ReferenceEquals(view.GetZDO(), data)) return;
            Character target = ai.GetTargetCreature();
            ZDOID id = target && target is Player && !target.IsDead() ? target.GetZDOID() : ZDOID.None;
            Report report;
            if (!receivedTargets.TryGetValue(data.m_uid, out report))
                receivedTargets[data.m_uid] = report = new Report();
            report.Data = data; report.Owner = data.GetOwner(); report.Revision = data.OwnerRevision; report.Target = id;
            FgnBossPolicy.Record(data, id);
        }
        private static void PruneForwarded()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (forwarded.Count == 0 || now < forwardExpiry) return;
            scratch.Clear(); foreach (ZDOID id in forwarded.Keys) scratch.Add(id);
            forwardExpiry = Double.MaxValue;
            foreach (ZDOID id in scratch)
            {
                Forward entry = forwarded[id];
                if (now >= entry.Until || !ReferenceEquals(world.GetZDO(id), entry.Data) || entry.Data.GetOwner() != entry.Owner) forwarded.Remove(id);
                else forwardExpiry = Math.Min(forwardExpiry, entry.Until);
            }
            scratch.Clear();
        }
        private static bool TryPrefix(ZDO __0, double __1, ref bool __result)
        {
            if (!Active) return true;
            try
            {
                if (__0 == null) return true;
                if (collecting && __0 != null && __0.IsValid()) MarkWatch(__0);
                if (FgnMobHandoff.Waiting(__0) || MobSourceHandoff.Waiting(__0)) { __result = false; return false; }
                Watch watch;
                if (watching.TryGetValue(__0.m_uid, out watch) && Watched(__0) && !watch.Acknowledged)
                { __result = false; return false; }
                // Stock FGN caps its outer scan only. Immediate calls must share that same budget.
                if (passMoves >= 16) { __result = false; return false; }
                return Handle(__0, __1, ref __result);
            }
            catch (Exception error) { Fail(error); return true; }
        }
        private static void TryPostfix(bool __result) { if (Enabled && __result) passMoves++; }
        private static bool Handle(ZDO data, double now, ref bool result)
        {
            result = false;
            if (!Eligible(data))
            { if (data != null) { managed.Remove(data.m_uid); if (!watching.ContainsKey(data.m_uid)) receivedTargets.Remove(data.m_uid); } return false; }
            long owner = data.GetOwner(), server = ZDOMan.GetSessionID();
            Lease lease; bool ours = managed.TryGetValue(data.m_uid, out lease) && ReferenceEquals(lease.Data, data);
            if (ours) lease.Pass = pass;
            if (owner == server && !ours) return true; // Never take over another mod's server-owned mob policy.
            if (owner == server && ours && !Watched(data)) CaptureLeaseTarget(data);
            if (owner != server && !Candidate(ZNet.instance.GetPeer(owner), data.GetPosition())) return true;
            if (!ShouldOverride(data.m_uid)) return true; // Optional/missing client feature retains unmodified FGN.
#if TML_DEBUG_SYSTEM
            if (FgnSoloCombatTest.Selected(data))
            {
                long destination;
                if (!FgnSoloCombatTest.Destination(data, out destination)) return false;
                if (destination == server && !ServerReady(data)) return false;
                result = Request(data, destination, now);
                if (result && destination == server)
                    managed[data.m_uid] = new Lease { Data = data, ReturnPeer = owner, SourceOwner = owner,
                        SourceRevision = data.OwnerRevision, Requested = now, Pass = pass };
                return false;
            }
#endif
            long targetOwner;
            if (!TryTargetPeer(data.m_uid, out targetOwner)) return false; // Wait for the new owner's actual AI report.
            ZNetPeer target = targetOwner == 0 ? null : ZNet.instance.GetPeer(targetOwner);
            if (target != null && !Candidate(target, data.GetPosition()))
            {
                if (owner == server)
                {
                    // Temporary exceptions still need an eligible recipient.
                    // Configured server defaults remain while a native player
                    // loads the area, including an unfinished FGN greeting.
                    ZNetPeer replacement = BestCandidate(data.GetPosition());
                    if (replacement != null) result = Request(data, replacement.m_uid, now);
                    else if (!(ServerDefaults && !FgnBossPolicy.IsBoss(data) && AnyLoadingPeer(data.GetPosition())))
                    { managed.Remove(data.m_uid); receivedTargets.Remove(data.m_uid); data.SetOwner(0L); Fgn.ClearOwned(data.m_uid); }
                }
                return false;
            }
            if (target != null && FgnBossPolicy.WantsServer(data))
            {
                if (owner == server || !ServerReady(data)) return false;
                result = Request(data, server, now);
                if (result) managed[data.m_uid] = new Lease { Data = data, ReturnPeer = target.m_uid,
                    SourceOwner = owner, SourceRevision = data.OwnerRevision, Requested = now, Pass = pass };
                return false;
            }
            if (target == null)
            {
                if (ServerDefaults && !FgnBossPolicy.IsBoss(data))
                {
                    if (owner == server)
                    {
                        // Already settled: no repeated collider-readiness work.
                        if (!AnyLoadingPeer(data.GetPosition()))
                        { managed.Remove(data.m_uid); RemoveWatch(data.m_uid); receivedTargets.Remove(data.m_uid); data.SetOwner(0L); Fgn.ClearOwned(data.m_uid); }
                        return false;
                    }
                    int count;
                    if (Fgn.Pending(data.m_uid, now) ||
                        (Fgn.PerOwner.TryGetValue(owner, out count) && count >= 6) || !ServerReady(data)) return false;
                    result = Request(data, server, now);
                    if (result) managed[data.m_uid] = new Lease { Data = data, ReturnPeer = owner,
                        SourceOwner = owner, SourceRevision = data.OwnerRevision, Requested = now, Pass = pass };
                    return false;
                }
                if (owner != server) return true; // FGN's unchanged noncombat lower-ping path.
                ZNetPeer returning = ZNet.instance.GetPeer(lease.ReturnPeer);
                if (!Candidate(returning, data.GetPosition())) returning = BestCandidate(data.GetPosition());
                if (returning != null) result = Request(data, returning.m_uid, now);
                else { managed.Remove(data.m_uid); receivedTargets.Remove(data.m_uid); data.SetOwner(0L); Fgn.ClearOwned(data.m_uid); }
                return false;
            }
            bool high = HighPing(target, data.GetPosition());
            bool nearby = high && OtherPlayerNearby(target, data);
            if (high && nearby && Fgn.Simulation())
            {
                if (owner == server) return false;
                if (!ServerReady(data)) return false;
                result = Request(data, server, now);
                if (result) managed[data.m_uid] = new Lease { Data = data, ReturnPeer = target.m_uid,
                    SourceOwner = owner, SourceRevision = data.OwnerRevision, Requested = now, Pass = pass };
                return false;
            }
            // A reported live player target uses our guarded transaction even
            // at low ping. Modern FGN intentionally refuses combat in its native
            // fallback, so falling through here can suppress target handoffs.
            if (owner != target.m_uid) result = Request(data, target.m_uid, now);
            return false;
        }
        private static bool HighPing(ZNetPeer target, Vector3 position)
        {
            float measured;
            if (!Fgn.TryMeasuredRtt(target, out measured)) return false;
            if (maximumPing.Value > 0) return measured > maximumPing.Value;
            float best = Single.MaxValue;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                float ping;
                if (peer != target && Candidate(peer, position) && Fgn.TryMeasuredRtt(peer, out ping)) best = Math.Min(best, ping);
            }
            return best < Single.MaxValue && measured > best + 250f;
        }
        private static bool OtherPlayerNearby(ZNetPeer target, ZDO data)
        {
            Vector3 position = data.GetPosition(); Vector2s sector = data.GetSector();
            foreach (ZNetPeer peer in Fgn.Candidates)
            {
                if (peer == target || !Candidate(peer, position) || peer.m_characterID.IsNone()) continue;
                Vector2s center = ZoneSystem.GetZone(peer.GetRefPos());
                if (Math.Abs((int)sector.x - center.x) > 1 || Math.Abs((int)sector.y - center.y) > 1) continue;
                ZDO player = world.GetZDO(peer.m_characterID);
                if (player == null || !player.IsValid() || player.GetOwner() != peer.m_uid ||
                    player.GetFloat(ZDOVars.s_health, 1f) <= 0f) continue;
                Vector3 point = player.GetPosition();
                if (Finite(point) && (point - position).sqrMagnitude <= nearbyRadius.Value * nearbyRadius.Value) return true;
            }
            return false;
        }
        internal static bool Candidate(ZNetPeer peer, Vector3 position)
        {
            return peer != null && peer.IsReady() && Finite(peer.GetRefPos()) && peer.GetRefPos() != Vector3.zero &&
                Fgn.Matched(peer) && ZNetScene.InActiveArea(position, peer.GetRefPos());
        }
        private static ZNetPeer BestCandidate(Vector3 position)
        {
            ZNetPeer best = null; float ping = Single.MaxValue;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (Candidate(peer, position) && Fgn.Rtt(peer) < ping) { best = peer; ping = Fgn.Rtt(peer); }
            return best;
        }
        private static bool Eligible(ZDO data)
        {
            return data != null && data.IsValid() && !data.m_uid.IsNone() && data.GetOwner() != 0L &&
                Finite(data.GetPosition()) && !data.GetBool(ZDOVars.s_tamed) &&
                data.GetLong(ZDOVars.s_user, 0L) == 0L && !Fgn.Possessed(data) && !IsFriendlyCreature(data);
        }
        private static void ResetFactions()
        {
            prefabFactionActors.Clear(); prefabFactionNext = 0; factionScene = null;
            liveFactionActors = new ConditionalWeakTable<ZNetView, FactionActor>();
        }
        private static bool IsFriendlyCreature(ZDO data)
        { return (Fgn != null && data != null && Fgn.ExcludedPrefab(data.GetPrefab())) || FriendlyCreatures.IsFriendly(CreatureActor(data)); }
        internal static Character CreatureActor(ZDO data)
        {
            if (data == null) return null;
            ZNetScene scene = ZNetScene.instance;
            if (!scene) { if (!ReferenceEquals(factionScene, null)) ResetFactions(); return null; }
            if (!ReferenceEquals(factionScene, scene)) { ResetFactions(); factionScene = scene; }
            ZNetView view = scene.FindInstance(data);
            if (view && view.IsValid() && ReferenceEquals(view.GetZDO(), data))
            {
                // A loaded actor's current faction wins over its prefab. Weak
                // view keys retain no unloaded actors; native components are stable.
                FactionActor loaded = liveFactionActors.GetOrCreateValue(view);
                if (!loaded.Actor) loaded.Actor = view.GetComponent<Character>();
                if (loaded.Actor) return loaded.Actor;
            }
            int hash = data.GetPrefab(); Character actor;
            bool known = prefabFactionActors.TryGetValue(hash, out actor);
            if (known && (ReferenceEquals(actor, null) || actor)) return actor;
            GameObject prefab = scene.GetPrefab(hash);
            if (!prefab) return null; // Late prefab registration must be retried.
            actor = prefab.GetComponent<Character>();
            if (!known)
            {
                // Fixed FIFO bound; successful hot lookups perform no GetComponent.
                if (prefabFactionActors.Count == PrefabFactionLimit) prefabFactionActors.Remove(prefabFactionOrder[prefabFactionNext]);
                prefabFactionOrder[prefabFactionNext] = hash;
                prefabFactionNext = (prefabFactionNext + 1) % PrefabFactionLimit;
            }
            prefabFactionActors[hash] = actor;
            // Keep the actor reference, not a stale bool: prefab faction changes
            // are read through the same native getter on every cached lookup.
            return actor;
        }
        private static Character LoadedCharacter(ZDO data)
        {
            ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            if (!view || !view.IsValid() || view.m_distant || !view.gameObject.activeInHierarchy ||
                view.transform.parent != null || !ReferenceEquals(view.GetZDO(), data)) return null;
            Character mob = view.GetComponent<Character>();
            if (!mob || mob is Player || FriendlyCreatures.IsFriendly(mob) || !mob.isActiveAndEnabled || mob.IsDead() || mob.IsTamed() ||
                mob.IsAttached() || mob.IsRiding()) return null;
            BaseAI ai = view.GetComponent<BaseAI>();
            ZSyncTransform sync = view.GetComponent<ZSyncTransform>();
            return ai && ai.isActiveAndEnabled && !ai.HaveRider() && sync && sync.isActiveAndEnabled && sync.m_syncPosition ? mob : null;
        }
        private static bool ServerReady(ZDO data)
        {
            // Native/PUP simulation guards own collision readiness after gain.
            // A real loaded actor is still needed for the local attack/use veto.
            Character mob = LoadedCharacter(data);
            Vector3 destination = data.GetPosition();
            return mob && Finite(destination) && Finite(mob.transform.position) &&
                data.IsValid() && data.GetPosition() == destination && Eligible(data);
        }
        private static bool Finite(Vector3 point)
        {
            return !Single.IsNaN(point.x) && !Single.IsNaN(point.y) && !Single.IsNaN(point.z) &&
                !Single.IsInfinity(point.x) && !Single.IsInfinity(point.y) && !Single.IsInfinity(point.z);
        }
        private static bool Request(ZDO data, long target, double now)
        {
            int count; long owner = data.GetOwner(), server = ZDOMan.GetSessionID();
            if (target == 0L || target == owner || passMoves >= 16 || Fgn.Pending(data.m_uid, now) || MobSourceHandoff.Waiting(data)) return false;
            Fgn.PerOwner.TryGetValue(owner, out count); if (count >= 6) return false;
            if (target == server && ServerCollisionReady != null && !ServerCollisionReady(data.GetPosition())) return false;
            if (owner == server)
            {
                Character mob = LoadedCharacter(data);
                if (!mob) return false;
                if (FgnTargetBoundary.Busy(mob)) { RememberAttack(data, mob); return false; }
                if (mob.IsStaggering() || !ServerReady(data) || !Candidate(ZNet.instance.GetPeer(target), data.GetPosition())) return false;
                world.ForceSendZDO(data.m_uid);
                if (FlightTransfer != null) FlightTransfer(data, target); else data.SetOwner(target);
                managed.Remove(data.m_uid);
                receivedTargets.Remove(data.m_uid);
                // A server round-trip may return to the same player UID. FGN must
                // establish its normal ownership hold from this actual return.
                Fgn.ClearOwned(data.m_uid);
                forwarded[data.m_uid] = new Forward { Data = data, Owner = target, Until = now + 5.0 };
                if (forwarded.Count == 1 || forwardExpiry > now + 5.0) forwardExpiry = now + 5.0;
            }
            else
            {
                ZNetPeer old = ZNet.instance.GetPeer(owner);
                if (!Candidate(old, data.GetPosition())) return false;
                FgnMobHandoff.Send(old.m_rpc, "FGN_HandOff", new object[] { data.m_uid, target });
            }
            Fgn.PerOwner[owner] = count + 1; Fgn.MarkPending(data.m_uid, target, now); Fgn.ClearStreak(data.m_uid); return true;
        }
        private static void NativeSetOwner(ZDO data, long owner)
        {
            if (MobSourceHandoff.Waiting(data)) return;
            Lease lease;
            if (Enabled && managed.Count != 0 && ReferenceEquals(world, ZDOMan.instance) && ReferenceEquals(network, ZNet.instance) &&
                managed.TryGetValue(data.m_uid, out lease) && ReferenceEquals(lease.Data, data) && Active && Eligible(data))
            {
                if (lease.Initial && (data.GetOwner() != ZDOMan.GetSessionID() || data.OwnerRevision != lease.SourceRevision ||
                    !ServerDefaults || !AnyLoadingPeer(data.GetPosition()))) managed.Remove(data.m_uid);
                else if (data.GetOwner() == ZDOMan.GetSessionID()) return;
                // Do not race the old owner's asynchronous FGN handoff with native ownership writes.
                if (data.GetOwner() == lease.SourceOwner && data.OwnerRevision == lease.SourceRevision &&
                    (FgnMobHandoff.Waiting(data) || (Time.realtimeSinceStartupAsDouble - lease.Requested < 5.0 && Fgn.Pending(data.m_uid, Time.realtimeSinceStartupAsDouble))) &&
                    Candidate(ZNet.instance.GetPeer(lease.SourceOwner), data.GetPosition()) && ShouldOverride(data.m_uid)) return;
            }
            bool initial = InitialDefault(data, owner);
            if (initial) owner = ZDOMan.GetSessionID();
            data.SetOwner(owner);
            if (initial && data.GetOwner() == owner) RememberInitial(data);
        }
        private static void RememberInitial(ZDO data)
        {
            managed[data.m_uid] = new Lease { Data = data, SourceOwner = data.GetOwner(), SourceRevision = data.OwnerRevision,
                Requested = Time.realtimeSinceStartupAsDouble, Pass = pass, Initial = true };
        }
        internal static void InitialMobLoad(ZDO data)
        {
            if (!ServerDefaults || !Active) return;
            try
            {
                if (!InitialDefault(data, ZDOMan.GetSessionID())) return;
                // Called before the ordinary CreateObject/Instantiate sequence.
                // Native terrain/type gates and all Awake/physics guards still run.
                data.SetOwner(ZDOMan.GetSessionID());
                if (data.GetOwner() == ZDOMan.GetSessionID()) RememberInitial(data);
            }
            catch (Exception error) { Fail(error); }
        }
        private static bool InitialDefault(ZDO data, long nativeOwner)
        {
            // An unowned ZDO may already have a passive server instance. Created
            // describes that local instance, not previous ownership or active use.
            // Existing owners and ordinary guarded handoffs stay untouched.
            if (!ServerDefaults || !Active || nativeOwner == 0L || data == null || !data.IsValid() || data.GetOwner() != 0L ||
                !data.Persistent || data.m_uid.IsNone() || data.OwnerRevision == UInt16.MaxValue ||
                data.GetBool(ZDOVars.s_tamed) || data.GetLong(ZDOVars.s_user, 0L) != 0L ||
                data.GetFloat(ZDOVars.s_health, 1f) <= 0f || !Finite(data.GetPosition()) || !ZNetScene.instance ||
                Fgn.Possessed(data) || Fgn.ExcludedPrefab(data.GetPrefab())) return false;
            EnsureWorld();
            if (!ReferenceEquals(world.GetZDO(data.m_uid), data)) return false;
            Character actor = CreatureActor(data);
            if (!actor || actor is Player || actor.IsBoss() || FriendlyCreatures.IsFriendly(actor) ||
                !actor.GetComponent<BaseAI>() || !actor.GetComponent<ZSyncTransform>()) return false;
            ZNetView template = actor.GetComponent<ZNetView>();
            // Only actual creature prefabs need a peer-area check. Ordinary
            // scene objects leave through the existing cached actor lookup.
            return template && template.m_persistent && !template.m_distant && AnyLoadingPeer(data.GetPosition()) &&
                (ServerCollisionReady == null || ServerCollisionReady(data.GetPosition()));
        }
        private static bool LocalHit(Dictionary<ZDOID, double> hits, ZDOID id, out double value)
        {
            if (ShouldOverride(id)) { value = 0; return false; }
            return hits.TryGetValue(id, out value);
        }
        private static bool NativeHeld(ZDO data)
        { return !ShouldOverride(data.m_uid) && Fgn.Held(data); }
        private static bool NativeAttack(Character actor)
        {
            ZNetView view = actor ? actor.GetComponent<ZNetView>() : null;
            return view && view.IsValid() && ShouldOverride(view.GetZDO().m_uid) ? FgnTargetBoundary.Busy(actor) : actor.InAttack();
        }
        private static bool NativeStagger(Character actor)
        { return actor.IsStaggering(); } // Our active-stagger guard; no remembered combat timer.
        private static void ResultPrefix(ZRpc __0, ZDOID __1, bool __2, string __3, out double __state)
        {
            __state = 0.0; // Also safe when another prefix skips this hook/original.
            if (FgnMobHandoff.Completing) return;
            if (__2 || __3 != "attacking" || __0 == null || !Enabled || Fgn == null || ZDOMan.instance == null || !ZNet.instance || !ZNet.instance.IsDedicated() ||
                !ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance)) return;
            try
            {
                if (!Active) return;
                ZDO data = world.GetZDO(__1); ZDOID target;
                if (!Eligible(data) || !ShouldOverride(__1) || !ReadTarget(data, out target) || target.IsNone()) return;
                ZNetPeer owner = network.GetPeer(data.GetOwner());
                if (owner == null || !ReferenceEquals(owner.m_rpc, __0)) return;
                double now = Time.realtimeSinceStartupAsDouble;
                // Capture before FGN removes its pending request. Late/foreign results
                // and unrelated mobs keep stock FGN's cooldown.
                if (Fgn.Pending(__1, now)) __state = now + 6.0;
            }
            catch (Exception error) { Fail(error); }
        }
        private static void ResultPostfix(ZDOID __1, double __state)
        {
            if (__state <= 0.0 || !Enabled || Fgn == null ||
                !ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance)) return;
            double until, now = Time.realtimeSinceStartupAsDouble;
            // Keep FGN's cleanup/statistics and any different cooldown from other
            // patches. Only remove this native attack rejection's new cooldown.
            // Attack-end events and existing FGN passes retain the full attack veto.
            // Realtime can advance between prefix and native write, even in one frame.
            if (Fgn.Cooldowns.TryGetValue(__1, out until) && until >= __state && until <= now + 6.0)
                Fgn.Cooldowns.Remove(__1);
            HandoffAttackRejected(__1);
        }
        internal static void HandoffAttackRejected(ZDOID id)
        {
            try
            {
                Watch watch; ZDO data = world.GetZDO(id); ZDOID target;
                if (!Watched(data) || !watching.TryGetValue(id, out watch) ||
                    !ShouldOverride(id) || !ReadTarget(data, out target) || target.IsNone()) return;
                watch.AttackRejected = true;
                if (watch.AttackReady)
                {
                    watch.AttackReady = watch.AttackRejected = false;
                    ProcessTargetEvent(data, target);
                }
            }
            catch (Exception error) { Fail(error); }
        }
        private static void VetoPostfix(ZDO __0, long __1, ref string __result)
        {
            // A queued stock FGN handoff may arrive after a live faction change.
            if (__result == null && Enabled && IsFriendlyCreature(__0)) { __result = "friendly creature"; return; }
            if (__result == "attacking")
            {
                try { RememberAttack(__0, null); } catch (Exception error) { Fail(error); }
                return;
            }
            // A queued FGN request can outlive the AI target that selected it.
            // Validate only when the original veto allows a watched client mob.
            // No polling, target write, forced handoff or native veto bypass.
            if (__result != null || !Enabled || !ZNet.instance || ZNet.instance.IsDedicated() || !serverConfirmed ||
                !ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance) ||
                __0 == null || !Watched(__0) || __0.GetOwner() != ZDOMan.GetSessionID()) return;
            try
            {
                if (!Eligible(__0)) return;
                Character actor = LoadedCharacter(__0);
                if (FgnTargetBoundary.Busy(actor))
                { __result = "attacking"; RememberAttack(__0, actor); return; }
                ZDOID latest;
                if (ReadTarget(__0, out latest) && FgnMobHandoff.StaleTarget(__0, latest))
                { __result = "AI target changed"; return; }
                ZNetPeer server = ZNet.instance.GetServerPeer();
                // Preserve the configured high-ping dedicated-server exception.
                if (server != null && server.m_server && server.IsReady() && __1 == server.m_uid) return;
                ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(__0) : null;
                BaseAI ai = view ? view.GetComponent<BaseAI>() : null;
                if (!Watching(ai)) return;
                Player target = ai.GetTargetCreature() as Player;
                if (!target || target.IsDead()) return; // Keep FGN's noncombat policy.
                ZNetView targetView = target.GetComponent<ZNetView>();
                ZDO player = targetView && targetView.IsValid() ? targetView.GetZDO() : null;
                if (player == null || !player.IsValid() || !ReferenceEquals(world.GetZDO(player.m_uid), player)) return;
                long expectedOwner = player.GetOwner();
                if (expectedOwner != 0 && __1 != expectedOwner) __result = "AI target changed";
            }
            catch (Exception error) { Fail(error); }
        }
        private static bool DamagePrefix(Character __instance, HitData hit, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (!Enabled || forwarded.Count == 0 || !ZNet.instance || !ZNet.instance.IsDedicated() || !__instance || hit == null) return true;
            try
            {
                if (FriendlyCreatures.IsFriendly(__instance)) return true;
                ZNetView view = __instance.GetComponent<ZNetView>();
                if (!view || !view.IsValid() || view.IsOwner()) return true;
                ZDO data = view.GetZDO(); Forward entry;
                if (!forwarded.TryGetValue(data.m_uid, out entry)) return true;
                if (!ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(network, ZNet.instance) ||
                    !ReferenceEquals(entry.Data, data) || data.GetOwner() != entry.Owner || Time.realtimeSinceStartupAsDouble > entry.Until)
                { forwarded.Remove(data.m_uid); return true; }
                ZNetPeer next = ZNet.instance.GetPeer(entry.Owner);
                if (next == null || !next.IsReady() || !Fgn.Matched(next)) return true;
                view.InvokeRPC(entry.Owner, "RPC_Damage", hit); return false;
            }
            catch (Exception error) { Fail(error); return true; }
        }

        internal static List<CodeInstruction> RewriteTarget(IEnumerable<CodeInstruction> source)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(); FieldInfo context = null; int replacements = 0, combatChecks = 0;
            foreach (CodeInstruction original in source)
            {
                CodeInstruction item = new CodeInstruction(original);
                if (item.opcode == OpCodes.Ldsfld) context = item.operand as FieldInfo;
                MethodInfo call = item.operand as MethodInfo;
                if (call != null && call.IsStatic && call.DeclaringType == Fgn.TryHandOff.DeclaringType &&
                    call.Name == "CombatReason" && call.ReturnType == typeof(string))
                {
                    ParameterInfo[] parameters = call.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(ZDO) && parameters[1].ParameterType == typeof(double))
                        combatChecks++;
                }
                if (context != null && context.Name == "s_lastHitBy" && call != null && call.Name == "TryGetValue" && call.DeclaringType == context.FieldType)
                { item.opcode = OpCodes.Call; item.operand = Fgn.TargetReader; replacements++; }
                result.Add(item);
            }
            // Current FGN no longer selects fighters in its native fallback.
            // Our prefix already handles reported targets; leave its new combat
            // and 30-second hold guards intact for peers without target reports.
            if (replacements != 1 && !(replacements == 0 && combatChecks == 1))
                throw new NotSupportedException("FGN combat target read changed.");
            return FgnMobHandoff.RewriteSend(result);
        }
        internal static List<CodeInstruction> RewriteVeto(IEnumerable<CodeInstruction> source)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(); int replacements = 0, held = 0, attackChecks = 0, staggerChecks = 0;
            MethodInfo originalRead = AccessTools.Method(typeof(Dictionary<ZDOID, double>), "TryGetValue");
            MethodInfo originalAttack = AccessTools.Method(typeof(Character), "InAttack"), originalStagger = AccessTools.Method(typeof(Character), "IsStaggering");
            foreach (CodeInstruction original in source)
            {
                CodeInstruction item = new CodeInstruction(original);
                if (originalRead.Equals(item.operand)) { item.opcode = OpCodes.Call; item.operand = AccessTools.Method(typeof(FgnCombatOwnership), "LocalHit"); replacements++; }
                else if (Fgn.Held.Method.Equals(item.operand)) { item.opcode = OpCodes.Call; item.operand = AccessTools.Method(typeof(FgnCombatOwnership), "NativeHeld"); held++; }
                else if (originalAttack.Equals(item.operand)) { item.opcode = OpCodes.Call; item.operand = AccessTools.Method(typeof(FgnCombatOwnership), "NativeAttack"); attackChecks++; }
                else if (originalStagger.Equals(item.operand)) { item.opcode = OpCodes.Call; item.operand = AccessTools.Method(typeof(FgnCombatOwnership), "NativeStagger"); staggerChecks++; }
                result.Add(item);
            }
            if (replacements != 1 || held != 1 || attackChecks != 1 || staggerChecks != 1) throw new NotSupportedException("FGN combat-veto contract changed.");
            return FgnBossPolicy.RewriteVeto(result);
        }
        internal static List<CodeInstruction> RewriteNativeOwner(IEnumerable<CodeInstruction> source)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(); int replacements = 0;
            MethodInfo setter = AccessTools.Method(typeof(ZDO), "SetOwner", new[] { typeof(long) });
            foreach (CodeInstruction original in source)
            {
                CodeInstruction item = new CodeInstruction(original);
                if (setter.Equals(item.operand)) { item.opcode = OpCodes.Call; item.operand = AccessTools.Method(typeof(FgnCombatOwnership), "NativeSetOwner"); replacements++; }
                result.Add(item);
            }
            if (replacements != 2) throw new NotSupportedException("Native ownership writes changed.");
            return result;
        }
    }
}
