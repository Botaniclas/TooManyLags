#if TML_DEBUG_SYSTEM
using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags
{
    // Explicit, temporary diagnostic policy for one mob and one online admin.
    // Debug build only. Inactive test: one null check in Poll, no test hooks.
    internal static class FgnSoloCombatTest
    {
        internal static bool LocalDestination(ZDO data, out long destination)
        { destination = localDestination; return ReferenceEquals(localData, data) && localTicket > 0 && Local() && destination != 0L; }
        internal static Func<ZDO, string> CollisionStatus = null;
        internal static Action<ZDOID> VanillaStarted = null;
        private sealed class Capability { internal bool Supported, Latency; }
        private sealed class Session
        {
            internal ZDO Data; internal ZNetPeer Player; internal ZDOMan World; internal ZNet Network;
            internal double End, Next, Poll, RequestedAt, Retry, Status, Interval;
            internal long Source, Destination, Ticket; internal uint Revision;
            internal bool Pending, Ready, Vanilla; internal int Completed;
            internal int PingTarget, BasePing, AddedPing; internal long LatencyEpoch;
            internal bool LatencyReady; internal double LatencyDeadline;
        }
        private const string ControlRpc = "TooManyLags_AI_SoloControl", MessageRpc = "TooManyLags_AI_SoloMessage",
            ArmRpc = "TooManyLags_AI_SoloArm", ReadyRpc = "TooManyLags_AI_SoloReady",
            LatencyRpc = "TooManyLags_AI_SoloLatency", LatencyReadyRpc = "TooManyLags_AI_SoloLatencyReady";
        // Revised readiness carries a separate success bit. Negotiate that
        // payload rather than starting a test with incompatible old endpoints.
        private const int ProbeMode = 20, ReadyMode = 21, UnavailableMode = 22;
        private const int LatencyProbeMode = 17, LatencyMode = 18, NoLatencyMode = 19;
        private static ConditionalWeakTable<ZRpc, Capability> capabilities = new ConditionalWeakTable<ZRpc, Capability>();
        private static Session session;
        private static Harmony hooks;
        private static ZDO localData;
        private static MonsterAI localAi;
        private static ZDOMan localWorld;
        private static ZNet localNetwork;
        private static long localTicket, localDestination, localSource, nextTicket;
#if TML_BRIDGE_DIAGNOSTICS
        private static double localStatus;
#endif
        private static uint localRevision;
        private static bool localArmed, localPrepared, supported, registered;
        private static bool latencySupported; private static long localLatencyEpoch;
        private static int localAiPasses, localTargetPasses;
        private static bool localAiRan;
        private static WeakReference terminal;
        private static ZNet terminalNetwork;
        private static ZDOID modeId;
        private static ZDOMan modeWorld;
        private static ZNet modeNetwork;
        private static bool vanillaMode;
        private static long modeEpoch;

        internal static bool Selected(ZDO data)
        { return session != null && ReferenceEquals(session.Data, data) && ReferenceEquals(session.World, ZDOMan.instance) && ReferenceEquals(session.Network, ZNet.instance); }
        internal static int DebugPhase(ZDO data)
        {
            if (data == null || !data.IsValid()) return -1;
            if (Selected(data)) return session.Pending ?
                (data.GetOwner() == session.Source && data.OwnerRevision == session.Revision ? 0 : 1) :
                session.Completed == 0 ? 0 : 1;
            if (data.m_uid != modeId || !ReferenceEquals(modeWorld, ZDOMan.instance) ||
                !ReferenceEquals(modeNetwork, ZNet.instance) || modeWorld == null ||
                !ReferenceEquals(modeWorld.GetZDO(modeId), data)) return -1;
            return ReferenceEquals(localData, data) && data.GetOwner() == localSource && data.OwnerRevision == localRevision ? 0 : 1;
        }
        internal static bool Destination(ZDO data, out long destination)
        { destination = Selected(data) && session.Pending && session.Ready ? session.Destination : 0L; return destination != 0L; }
        internal static bool Active { get { return session != null; } }
        internal static bool Vanilla(ZDO data)
        {
            if (Selected(data)) return session.Vanilla;
            return vanillaMode && data != null && data.m_uid == modeId &&
                ReferenceEquals(modeWorld, ZDOMan.instance) && ReferenceEquals(modeNetwork, ZNet.instance) &&
                modeWorld != null && ReferenceEquals(modeWorld.GetZDO(modeId), data);
        }

        internal static void Register()
        {
            if (registered || !FgnCombatOwnership.Enabled) return;
            foreach (string name in new[] { "ai_test", "ai_test_v", "ai_test_vanilla", "tml_ai_test" })
                new Terminal.ConsoleCommand(name, "Solo admin test: [start|stop|status] [RTT_ms] [interval_seconds]. Numeric first argument means start; no arguments toggles start/stop. Defaults: 0 ms, 0.5 seconds. _v/_vanilla bypass handoff bridges. Five-minute limit; normal guards remain active.", new Terminal.ConsoleEvent(Run));
            registered = true;
        }
        private static void Run(Terminal.ConsoleEventArgs args)
        {
            if (args == null || !args.Context) return;
            if (!FgnCombatOwnership.Enabled || !supported || !ZNet.instance || ZNet.instance.IsDedicated())
            { args.Context.AddString("Connect to a dedicated server with the same solo-test build and enabled Hostile Mob ownership first."); return; }
            string command; int ping;
            if (!ParseRequest(args.Args, out command, out ping))
            { args.Context.AddString("ai_test[_v|_vanilla] [start|stop|status] [RTT_ms: 0-1000] [interval_seconds: 0.1-60]. No arguments: toggle."); return; }
            bool start = command.StartsWith("start", StringComparison.Ordinal) || command.StartsWith("toggle", StringComparison.Ordinal);
            if (ping > 0 && !latencySupported) { args.Context.AddString("Artificial latency requires the updated solo-test build on server and client."); return; }
            if (command == "status") args.Context.AddString("MOB TEST: " + SoloNetworkLatency.Describe());
            ZNetPeer peer = ZNet.instance.GetServerPeer();
            if (!FgnCombatOwnership.HandoffServer(peer)) return;
            terminal = new WeakReference(args.Context); terminalNetwork = ZNet.instance;
            try { if (start) EnsureLocalHooks(); peer.m_rpc.Invoke(ControlRpc, command); }
            catch (Exception error) { args.Context.AddString("MOB TEST request failed: " + error.Message); }
        }
        internal static void Probe(ZNetPeer peer)
        { if (FgnCombatOwnership.Enabled && peer != null && peer.IsReady()) { peer.m_rpc.Invoke("TooManyLags_AI_Mode", ProbeMode); peer.m_rpc.Invoke("TooManyLags_AI_Mode", LatencyProbeMode); } }
        internal static bool Mode(ZNetPeer peer, int mode)
        {
            if (!ZNet.instance) return false;
            if (ZNet.instance.IsDedicated())
            {
                if (mode == LatencyProbeMode)
                {
                    if (peer != null && peer.IsReady()) { capabilities.GetOrCreateValue(peer.m_rpc).Latency = true; peer.m_rpc.Invoke("TooManyLags_AI_Mode", FgnCombatOwnership.Enabled ? LatencyMode : NoLatencyMode); }
                    return true;
                }
                if (mode != ProbeMode) return false;
                if (peer != null && peer.IsReady())
                { capabilities.GetOrCreateValue(peer.m_rpc).Supported = true; peer.m_rpc.Invoke("TooManyLags_AI_Mode", FgnCombatOwnership.Enabled ? ReadyMode : UnavailableMode); }
                return true;
            }
            if (mode == LatencyMode || mode == NoLatencyMode)
            {
                if (peer != null && peer.IsReady() && peer.m_server && ReferenceEquals(peer, ZNet.instance.GetServerPeer())) latencySupported = mode == LatencyMode;
                return true;
            }
            if (mode != ReadyMode && mode != UnavailableMode) return false;
            if (peer != null && peer.IsReady() && peer.m_server && ReferenceEquals(peer, ZNet.instance.GetServerPeer()))
            { supported = mode == ReadyMode; if (!supported) ClearLocal(true); }
            return true;
        }
        internal static void Connect(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;
            double lastControl = -10;
            peer.m_rpc.Register<string>(ControlRpc, delegate(ZRpc rpc, string command)
            {
                ZNet net = ZNet.instance;
                if (!net || !net.IsDedicated() || !peer.IsReady() || !ReferenceEquals(net.GetPeer(peer.m_uid), peer) || !ReferenceEquals(rpc, peer.m_rpc)) return;
                double now = Time.realtimeSinceStartupAsDouble;
                if (now - lastControl < 0.5 || command == null || command.Length > 64) return;
                lastControl = now;
                if (peer.m_socket == null || !net.IsAdmin(peer.m_socket.GetHostName()))
                { Message(peer, "Requires server admin rights (adminlist.txt)."); return; }
                try
                {
                    int ping; float interval;
                    bool vanilla = command == "toggle_vanilla" || command.StartsWith("start_vanilla", StringComparison.Ordinal);
                    if (command == "toggle" || command == "toggle_vanilla")
                    { if (session != null) Stop("stopped by admin"); else Start(peer, now, 0, .5f, vanilla); }
                    else if (ParseStart(command, out ping, out interval)) Start(peer, now, ping, interval, vanilla);
                    else if (command == "stop") { if (session != null) Stop("stopped by admin"); else Message(peer, "No test active."); }
                    else if (command == "status") Message(peer, Status());
                }
                catch (Exception error) { Plugin.Warn("AI test failed: " + error.Message); Stop("test error: " + error.Message); Message(peer, error.Message); }
            });
            peer.m_rpc.Register<string>(MessageRpc, delegate(ZRpc rpc, string line)
            {
                if (!FgnCombatOwnership.HandoffServer(peer) || line == null || line.Length > 1800) return;
                Plugin.TestLog(line);
                Terminal target = terminal != null && ReferenceEquals(terminalNetwork, ZNet.instance) ? terminal.Target as Terminal : null;
                if (target) target.AddString("MOB TEST: " + line);
            });
            peer.m_rpc.Register<ZDOID, ZPackage>(ArmRpc, delegate(ZRpc rpc, ZDOID id, ZPackage payload)
            {
                if (!supported || !FgnCombatOwnership.HandoffServer(peer)) return;
                long ticket, revision, destination; bool vanilla;
                try { ticket = payload.ReadLong(); revision = payload.ReadLong(); destination = payload.ReadLong(); vanilla = payload.ReadBool(); }
                catch (Exception) { return; }
                if (payload.GetPos() != payload.Size()) return;
                if (id.IsNone() && ticket == 0)
                {
                    ZDOID cancelled = localData == null ? ZDOID.None : localData.m_uid;
                    ClearLocal(true);
                    if (!cancelled.IsNone()) MobBridgeProtocol.Cancel(cancelled);
                    SoloNetworkLatency.End(); MobSourceHandoff.InvalidatePresentationTiming(); return;
                }
                if (ticket < 0 && ticket != Int64.MinValue && !id.IsNone() && destination == 0 && revision >= 0 && revision < UInt16.MaxValue)
                {
                    if (-ticket <= modeEpoch) return;
                    try { SetMode(id, vanilla); modeEpoch = -ticket; }
                    catch (Exception error) { Plugin.Warn("Cannot set AI test mode: " + error.Message); ClearLocal(true); }
                    return;
                }
                if (ticket <= localTicket || revision < 0 || revision >= UInt16.MaxValue || destination != peer.m_uid) return;
                ZDO data = ZDOMan.instance.GetZDO(id);
                if (data != null && data.OwnerRevision > revision) return;
                try
                {
                    if (data == null) throw new InvalidOperationException("Mob data is not loaded on the client.");
                    SetMode(id, vanilla);
                    Arm(data, ticket, destination, (uint)revision, ZDOMan.GetSessionID());
                }
                catch (Exception error)
                {
                    Plugin.Warn("Cannot arm AI test: " + error.Message); ClearLocal();
                    SendReady(id, ticket, (uint)revision, ZDOID.None, false);
                }
            });
            peer.m_rpc.Register<ZDOID, ZPackage>(ReadyRpc, delegate(ZRpc rpc, ZDOID id, ZPackage payload)
            {
                Session current = session;
                if (current == null || !ReferenceEquals(current.Player, peer) || !ReferenceEquals(peer.m_rpc, rpc) || !Selected(current.Data)) return;
                long ticket, revision; ZDOID target; bool prepared;
                try { ticket = payload.ReadLong(); revision = payload.ReadLong(); target = payload.ReadZDOID(); prepared = payload.ReadBool(); }
                catch (Exception) { return; }
                if (id != current.Data.m_uid || !current.Pending || current.Ready || ticket != current.Ticket || revision != current.Revision ||
                    current.Source != peer.m_uid || current.Data.GetOwner() != current.Source || current.Data.OwnerRevision != current.Revision ||
                    !FgnCombatOwnership.HandoffSource(current.Data, peer.m_uid, rpc)) return;
                if (!prepared) { Stop("mob not loaded at its owner; approach it and start again"); return; }
                ZDOID reported;
                if (!FgnCombatOwnership.ReadTarget(current.Data, out reported) || reported != target) return;
                current.Ready = true;
                TryTransfer(current, Time.realtimeSinceStartupAsDouble);
            });
            peer.m_rpc.Register<ZPackage>(LatencyRpc, delegate(ZRpc rpc, ZPackage payload)
            {
                if (!latencySupported || !FgnCombatOwnership.HandoffServer(peer) || !ReferenceEquals(rpc, peer.m_rpc)) return;
                long epoch; int ping;
                try { epoch = payload.ReadLong(); ping = payload.ReadInt(); } catch (Exception) { return; }
                if (epoch <= localLatencyEpoch || ping < 1 || ping > 1000) return;
                localLatencyEpoch = epoch; int baseline, added; string error;
                baseline = added = 0; error = "Disable fgn_netsim before using this latency test.";
                MobSourceHandoff.InvalidatePresentationTiming();
                bool ready = !OtherNetworkSimulation() && SoloNetworkLatency.Start(peer, ping, out baseline, out added, out error);
                Plugin.TestLog(ready ? SoloNetworkLatency.Describe() : "latency test unavailable: " + error);
                ZPackage reply = new ZPackage(); reply.Write(epoch); reply.Write(ready); reply.Write(baseline); reply.Write(added); reply.Write(error);
                peer.m_rpc.Invoke(LatencyReadyRpc, reply);
            });
            peer.m_rpc.Register<ZPackage>(LatencyReadyRpc, delegate(ZRpc rpc, ZPackage payload)
            {
                Session current = session;
                if (!ZNet.instance || !ZNet.instance.IsDedicated() || current == null || !ReferenceEquals(current.Player, peer) ||
                    !ReferenceEquals(peer.m_rpc, rpc) || !Selected(current.Data) || current.PingTarget == 0 || current.LatencyReady) return;
                long epoch; bool ready; int baseline, added; string error;
                try { epoch = payload.ReadLong(); ready = payload.ReadBool(); baseline = payload.ReadInt(); added = payload.ReadInt(); error = payload.ReadString(); } catch (Exception) { return; }
                if (epoch != current.LatencyEpoch || (ready && (baseline < 0 || baseline > 60000 || added != Math.Max(0, current.PingTarget - baseline)))) return;
                if (!ready) { Stop("latency setup failed: " + (error == null ? "" : error.Substring(0, Math.Min(200, error.Length)))); return; }
                current.LatencyReady = true; current.BasePing = baseline; current.AddedPing = added;
                Message(peer, "latency ready; target RTT=" + current.PingTarget + "ms; measured base=" + baseline + "ms; added=" + added + "ms round trip, split equally. Existing RTT cannot be reduced; frame scheduling and jitter still apply.");
                Poll();
            });
        }
        private static void Message(ZNetPeer peer, string line)
        {
            Plugin.TestLog(line);
            try { if (peer != null && peer.IsReady()) peer.m_rpc.Invoke(MessageRpc, line); } catch (Exception) { }
        }
        private static bool Alone(ZNetPeer player)
        {
            int count = 0;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (peer != null && peer.IsReady() && !peer.m_characterID.IsNone()) count++;
            return count == 1 && player != null && player.IsReady();
        }
        private static bool OtherNetworkSimulation()
        {
            if (FgnCombatOwnership.Fgn == null) return false;
            Type type = FgnCombatOwnership.Fgn.TryHandOff.DeclaringType.Assembly.GetType("FiresGhettoNetworkMod.NetSim", false);
            System.Reflection.PropertyInfo active = type == null ? null : type.GetProperty("Active", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            return active != null && (bool)active.GetValue(null, null);
        }
        private static bool ParseStart(string command, out int ping, out float interval)
        {
            ping = 0; interval = .5f;
            string[] parts = command == null ? new string[0] : command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 3 || (parts[0] != "start" && parts[0] != "start_vanilla")) return false;
            if (parts.Length >= 2 && (!Int32.TryParse(parts[1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out ping) || ping < 0 || ping > 1000)) return false;
            if (parts.Length == 3 && (!Single.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out interval) || Single.IsNaN(interval) || Single.IsInfinity(interval) || interval < .1f || interval > 60f)) return false;
            return true;
        }
        private static bool ParseRequest(string[] args, out string command, out int ping)
        {
            command = ""; ping = 0;
            if (args == null) return false;
            args = Array.FindAll(args, delegate(string value) { return !String.IsNullOrWhiteSpace(value); });
            if (args.Length == 0 || args.Length > 4) return false;
            bool vanilla = args[0].Equals("ai_test_v", StringComparison.OrdinalIgnoreCase) ||
                args[0].Equals("ai_test_vanilla", StringComparison.OrdinalIgnoreCase);
            if (args.Length == 1) { command = vanilla ? "toggle_vanilla" : "toggle"; return true; }
            string action = args[1].ToLowerInvariant();
            if ((action == "stop" || action == "status") && args.Length == 2) { command = action; return true; }
            int number;
            bool numeric = Int32.TryParse(args[1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out number);
            if (numeric && args.Length > 3 || !numeric && action != "start") return false;
            string text = numeric ? "start " + String.Join(" ", args, 1, args.Length - 1) :
                "start" + (args.Length > 2 ? " " + String.Join(" ", args, 2, args.Length - 2) : "");
            float interval;
            if (!ParseStart(text, out ping, out interval)) return false;
            command = StartCommand(ping, interval, vanilla); return true;
        }
        private static string StartCommand(int ping, float interval, bool vanilla)
        {
            return (vanilla ? "start_vanilla " : "start ") + ping.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " +
                interval.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
        private static void Start(ZNetPeer peer, double now, int ping, float interval, bool vanilla)
        {
            if (session != null) { Message(peer, Status() + "; stop the current test first."); return; }
            Capability capability;
            if (!FgnCombatOwnership.Enabled || FgnCombatOwnership.Fgn == null || !FgnCombatOwnership.Fgn.Simulation() ||
                !capabilities.TryGetValue(peer.m_rpc, out capability) || !capability.Supported)
            { Message(peer, "Requires this test build on server/client, active Hostile Mob ownership and Server-Side Simulation."); return; }
            if (ping > 0 && !capability.Latency) { Message(peer, "Artificial latency requires the updated solo-test client and server."); return; }
            if (ping > 0 && OtherNetworkSimulation()) { Message(peer, "Disable fgn_netsim on the server before using this latency test."); return; }
            if (!Alone(peer)) { Message(peer, "Solo test requires exactly one connected player."); return; }
            ZDO player = ZDOMan.instance.GetZDO(peer.m_characterID);
            if (player == null || !player.IsValid()) { Message(peer, "Player is not loaded."); return; }
            ZDO best = null; float nearest = 900f;
            foreach (Character mob in Character.GetAllCharacters())
            {
                if (!mob || mob is Player || mob.IsBoss() || mob.IsDead()) continue;
                float distance = (mob.transform.position - player.GetPosition()).sqrMagnitude;
                if (distance > nearest) continue;
                ZNetView view = mob.GetComponent<ZNetView>();
                ZDO data = view && view.IsValid() ? view.GetZDO() : null;
                if (!FgnCombatOwnership.SoloEligiblePending(data, peer)) continue;
                best = data; nearest = distance;
            }
            if (best == null) { Message(peer, "No eligible loaded Hostile Mob within 30 metres. Approach or spawn one, then start again."); return; }
            EnsureLocalHooks();
            MobSourceHandoff.InvalidatePresentationTiming();
            session = new Session { Data = best, Player = peer, World = ZDOMan.instance, Network = ZNet.instance, End = now + 300, Next = now,
                PingTarget = ping, LatencyReady = ping == 0, LatencyEpoch = ++nextTicket, LatencyDeadline = now + 5, Interval = interval, Vanilla = vanilla };
            SetMode(best.m_uid, vanilla);
            ZPackage mode = new ZPackage(); mode.Write(-session.LatencyEpoch); mode.Write((long)best.OwnerRevision); mode.Write(0L); mode.Write(vanilla);
            peer.m_rpc.Invoke(ArmRpc, best.m_uid, mode);
            if (ping > 0)
            {
                ZPackage payload = new ZPackage(); payload.Write(session.LatencyEpoch); payload.Write(ping);
                peer.m_rpc.Invoke(LatencyRpc, payload);
            }
            FgnCombatOwnership.SoloPrepare(best, peer.m_uid);
            Message(peer, "started; mode=" + (vanilla ? "vanilla" : "bridge") + "; mob=" + best.m_uid + "; prefab=" + best.GetPrefab() + "; player=" + peer.m_uid + "; target RTT=" + ping +
                "ms (0 = native); interval=" + interval.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                "s; normal guards active; stops after five minutes.");
            Poll();
        }
        internal static void Poll()
        {
            Session current = session;
            if (current == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < current.Poll) return;
            current.Poll = now + 0.05;
            try
            {
                ZDO player = ReferenceEquals(current.World, ZDOMan.instance) ? current.World.GetZDO(current.Player.m_characterID) : null;
                if (!FgnCombatOwnership.Enabled || !ReferenceEquals(current.Network, ZNet.instance) || !ZNet.instance || !ZNet.instance.IsDedicated() ||
                    !Alone(current.Player) || now >= current.End || player == null || !player.IsValid() ||
                    !FgnCombatOwnership.SoloEligiblePending(current.Data, current.Player) ||
                    (current.Data.GetPosition() - player.GetPosition()).sqrMagnitude > 2025f)
                { Stop("expired, player/mob unavailable, another player joined or more than 45m apart"); return; }
                if (!current.LatencyReady)
                { if (now >= current.LatencyDeadline) Stop("client did not confirm latency setup within five seconds"); return; }
                long owner = current.Data.GetOwner();
                if (current.Pending)
                {
                    if (owner == current.Destination && current.Data.OwnerRevision == current.Revision + 1U)
                    {
                        current.Completed++; current.Pending = current.Ready = false; current.Next = now + current.Interval;
                        FgnMobHandoff.AfterReplication(); FgnCombatOwnership.Fgn.ClearPending(current.Data.m_uid);
                        FgnCombatOwnership.SoloPrepare(current.Data, current.Player.m_uid);
                        Message(current.Player, "completed " + current.Completed + ": " + current.Source + " -> " + owner + "; next request in " +
                            current.Interval.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "s."); return;
                    }
                    if (owner != current.Source || current.Data.OwnerRevision != current.Revision) { Stop("ownership changed outside this test"); return; }
                    if (current.Ready && now >= current.Retry) TryTransfer(current, now);
                    if (now >= current.Status) { current.Status = now + 2; Message(current.Player, Status()); }
                    return;
                }
                if (now < current.Next) return;
                current.Source = owner; current.Revision = current.Data.OwnerRevision;
                current.Destination = owner == ZDOMan.GetSessionID() ? current.Player.m_uid : ZDOMan.GetSessionID();
                current.Ticket = ++nextTicket; current.Pending = true; current.Ready = false; current.RequestedAt = now; current.Status = now + 2;
                FgnCombatOwnership.SoloPrepare(current.Data, current.Player.m_uid);
                if (owner == ZDOMan.GetSessionID()) Arm(current.Data, current.Ticket, current.Destination, current.Revision, owner);
                else
                {
                    ZPackage payload = new ZPackage(); payload.Write(current.Ticket); payload.Write((long)current.Revision); payload.Write(current.Destination); payload.Write(current.Vanilla);
                    current.Player.m_rpc.Invoke(ArmRpc, current.Data.m_uid, payload);
                }
            }
            catch (Exception error) { Plugin.Warn("AI test failed: " + error.Message); Stop("test error: " + error.Message); }
        }
        private static void TryTransfer(Session current, double now)
        {
            current.Retry = now + 0.25;
            // Area readiness may become true after Start selected the mob.
            // Re-arm the existing watch before each bounded transfer retry.
            FgnCombatOwnership.SoloPrepare(current.Data, current.Player.m_uid);
            if (Selected(current.Data) && current.Ready && !FgnMobHandoff.Waiting(current.Data) && !FgnCombatOwnership.Fgn.Pending(current.Data.m_uid, now))
                FgnCombatOwnership.Fgn.TryTransfer(current.Data, now);
        }
        private static void Arm(ZDO data, long ticket, long destination, uint revision, long source)
        {
            ZNetView view = ZNetScene.instance ? ZNetScene.instance.FindInstance(data) : null;
            MonsterAI ai = view ? view.GetComponent<MonsterAI>() : null;
            if (!ai) throw new InvalidOperationException("Mob instance is not loaded at the current owner.");
            EnsureLocalHooks();
            localData = data; localAi = ai; localWorld = ZDOMan.instance; localNetwork = ZNet.instance;
            // An arm RPC may precede ownership replication. The existing AI hook
            // waits for this exact tuple; it never needs a polling retry loop.
            localTicket = ticket; localDestination = destination; localSource = source; localRevision = revision;
            localArmed = true; localPrepared = false;
#if TML_BRIDGE_DIAGNOSTICS
            localStatus = 0;
#endif
            localAiPasses = localTargetPasses = 0; localAiRan = false;
        }
        private static void EnsureLocalHooks()
        {
            if (hooks != null) return;
            Harmony candidate = new Harmony(Plugin.Id + ".solo-combat-test");
            try
            {
                candidate.Patch(AccessTools.Method(typeof(MonsterAI), "UpdateTarget"), postfix: new HarmonyMethod(AccessTools.Method(typeof(FgnSoloCombatTest), "Evaluated")));
                candidate.Patch(AccessTools.Method(typeof(MonsterAI), "UpdateAI", new[] { typeof(float) }), finalizer: new HarmonyMethod(AccessTools.Method(typeof(FgnSoloCombatTest), "Completed")));
                hooks = candidate;
                MobSourceHandoff.SkipLocalCommit = Vanilla;
                MobFlightHandoff.NativeTransfer = Vanilla;
                MobHandoffVisual.SkipPresentation = Vanilla;
            }
            catch { candidate.UnpatchSelf(); throw; }
        }
        private static bool Local()
        {
            return localAi && localData != null && localData.IsValid() && ReferenceEquals(localWorld, ZDOMan.instance) &&
                ReferenceEquals(localNetwork, ZNet.instance) && ReferenceEquals(localWorld.GetZDO(localData.m_uid), localData) &&
                localData.GetOwner() == ZDOMan.GetSessionID() && localData.GetOwner() == localSource && localData.OwnerRevision == localRevision;
        }
        private static void SetMode(ZDOID id, bool vanilla)
        {
            bool changed = modeId != id || vanillaMode != vanilla || !ReferenceEquals(modeWorld, ZDOMan.instance) || !ReferenceEquals(modeNetwork, ZNet.instance);
            EnsureLocalHooks(); modeId = id; vanillaMode = vanilla; modeWorld = ZDOMan.instance; modeNetwork = ZNet.instance;
            if (changed && vanilla)
            { MobHandoffVisual.Cancel(id); if (VanillaStarted != null) VanillaStarted(id); }
        }
        private static void Evaluated(MonsterAI __instance)
        {
            if (!localArmed || !ReferenceEquals(__instance, localAi) || !Local()) return;
            localTargetPasses++;
            try
            {
                if (!FgnTargetBoundary.HoldForSolo(__instance))
                {
#if TML_BRIDGE_DIAGNOSTICS
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (now >= localStatus)
                    {
                        localStatus = now + 2; Character actor = __instance.GetComponent<Character>();
                        Plugin.TestLog("source waits; ticket=" + localTicket + "; inAttack=" + (actor && actor.InAttack()) +
                            "; unfinishedAttack=" + (actor && FgnTargetBoundary.Busy(actor)) + "; watched=" + FgnCombatOwnership.Watching(__instance) +
                            "; target=" + DescribeTarget(__instance));
                    }
#endif
                    return;
                }
                localPrepared = true; localArmed = false;
            }
            catch (Exception error) { Plugin.Warn("AI test boundary failed: " + error.Message); ClearLocal(); }
        }
        private static Exception Completed(MonsterAI __instance, bool __result, Exception __exception)
        {
            if ((!localArmed && !localPrepared) || !ReferenceEquals(__instance, localAi)) return __exception;
            localAiPasses++; localAiRan = __result;
            // Sleeping AI returns successfully before UpdateTarget. Its completed
            // idle operation is safe too. A skipped/failed AI pass is not proof
            // of readiness, and a newly started attack must still finish first.
            if (localArmed && __exception == null && __result && Local() && FgnTargetBoundary.HoldForSolo(__instance))
            { localPrepared = true; localArmed = false; }
            if (!localPrepared) return __exception;
            localPrepared = false;
            if (__exception != null || !Local()) return __exception;
            try
            {
                // Readiness describes this completed AI operation, not whether
                // its real target happens to be the solo player.
                Player target = __instance.GetTargetCreature() as Player;
                ZDOID targetId = target && !target.IsDead() ? target.GetZDOID() : ZDOID.None;
                FgnCombatOwnership.Observe(__instance);
                if (!Local()) return __exception;
                if (ZNet.instance.IsDedicated())
                {
                    if (Selected(localData) && session.Ticket == localTicket && session.Destination == localDestination)
                    { session.Ready = true; TryTransfer(session, Time.realtimeSinceStartupAsDouble); }
                }
                else
                {
                    ZNetPeer server = ZNet.instance.GetServerPeer();
                    if (MobSourceHandoff.TryCommit(__instance, localData, targetId, server))
                    { FgnTargetBoundary.CommitFinished(localData, targetId); ClearLocal(); return __exception; }
                    SendReady(localData.m_uid, localTicket, localRevision, targetId, true);
                }
            }
            catch (Exception error) { Plugin.Warn("AI test readiness failed: " + error.Message); ClearLocal(); }
            return __exception;
        }
        private static void SendReady(ZDOID id, long ticket, uint revision, ZDOID target, bool prepared)
        {
            ZNetPeer peer = ZNet.instance.GetServerPeer();
            if (!FgnCombatOwnership.HandoffServer(peer)) return;
            ZPackage payload = new ZPackage(); payload.Write(ticket); payload.Write((long)revision); payload.Write(target); payload.Write(prepared);
            peer.m_rpc.Invoke(ReadyRpc, id, payload);
        }
        private static string Status()
        {
            if (session == null) return "inactive";
            Character mob = FgnCombatOwnership.CreatureActor(session.Data);
            return "active; mode=" + (session.Vanilla ? "vanilla" : "bridge") + "; mob=" + session.Data.m_uid + "; owner=" + session.Data.GetOwner() + "; desired=" + session.Destination +
                "; pending=" + session.Pending + "; boundaryReady=" + session.Ready + "; inAttack=" + (mob && mob.InAttack()) +
                "; unfinishedAttack=" + (mob && FgnTargetBoundary.Busy(mob)) +
                "; target=" + DescribeTarget(mob ? mob.GetComponent<BaseAI>() : null) +
                (session.Data.GetOwner() == ZDOMan.GetSessionID() && ReferenceEquals(localData, session.Data) ?
                    "; sleeping=" + (localAi && localAi.IsSleeping()) + "; AI passes=" + localAiPasses +
                    "; last AI ran=" + localAiRan + "; target passes=" + localTargetPasses : "") +
                (session.Data.GetOwner() == ZDOMan.GetSessionID() ? " (server authoritative)" : " (server replica)") +
                "; target RTT=" + session.PingTarget + "ms; latencyReady=" + session.LatencyReady + "; base RTT=" + session.BasePing + "ms; added RTT=" + session.AddedPing + "ms" +
                "; interval=" + session.Interval.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "s" +
                "; wait=" + (session.Pending ? (Time.realtimeSinceStartupAsDouble - session.RequestedAt).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "0") + "s; completed=" + session.Completed +
                (CollisionStatus == null ? "" : CollisionStatus(session.Data));
        }
        private static string DescribeTarget(BaseAI ai)
        {
            Character target = ai ? ai.GetTargetCreature() : null;
            return !target ? "none" : target.IsDead() ? "dead" : target is Player ? "player" : "non-player (" + target.name + ")";
        }
        private static void ClearLocal(bool endSession = false)
        {
            try
            {
                if (localAi && localData != null)
                {
                    Character target = localAi.GetTargetCreature();
                    FgnTargetBoundary.CommitFinished(localData, target && target is Player ? target.GetZDOID() : ZDOID.None);
                }
            }
            catch (Exception) { } // Destroyed views cannot prevent test-hook cleanup.
            localArmed = localPrepared = false; localAi = null; localData = null; localWorld = null; localNetwork = null;
            if (endSession)
            {
                modeId = ZDOID.None; vanillaMode = false; modeWorld = null; modeNetwork = null;
                MobSourceHandoff.SkipLocalCommit = null; MobFlightHandoff.NativeTransfer = null; MobHandoffVisual.SkipPresentation = null;
                if (hooks != null) { try { hooks.UnpatchSelf(); } catch (Exception) { } hooks = null; }
            }
        }
        private static void Stop(string reason, bool resume = true)
        {
            Session previous = session; session = null; ClearLocal(true);
            MobSourceHandoff.InvalidatePresentationTiming();
            if (previous == null) return;
            MobBridgeProtocol.Cancel(previous.Data.m_uid);
            ZPackage payload = new ZPackage(); payload.Write(0L); payload.Write(0L); payload.Write(0L); payload.Write(false);
            try { if (previous.Player.IsReady()) previous.Player.m_rpc.Invoke(ArmRpc, ZDOID.None, payload); } catch (Exception) { }
            Message(previous.Player, "stopped: " + reason + "; completed=" + previous.Completed + ". Normal ownership policy resumes; no forced owner reset.");
            if (resume && ReferenceEquals(previous.World, ZDOMan.instance) && ReferenceEquals(previous.Network, ZNet.instance) && FgnCombatOwnership.Enabled)
                try { FgnCombatOwnership.HandoffRetry(previous.Data); } catch (Exception) { }
        }
        internal static void Reset()
        {
            Stop("session reset", false); SoloNetworkLatency.End(); capabilities = new ConditionalWeakTable<ZRpc, Capability>(); supported = latencySupported = false; localTicket = localLatencyEpoch = 0;
            terminal = null; terminalNetwork = null; modeEpoch = 0;
        }
    }
}
#endif
