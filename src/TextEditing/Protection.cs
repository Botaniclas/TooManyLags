using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TooManyLags.TextEditing
{
    // Native text UI is local. Prepare its actual editor through the existing
    // guarded handoff before opening it; never open a fake inventory or steal.
    internal static class Protection
    {
        private const string Endpoint = "TooManyLags_TextEditing_1";
        private enum Message { Begin, Grant, End, Reject, Native }
        private sealed class Entry
        {
            internal ZDO Data;
            internal ZDOID Id;
            internal ZNetPeer Peer;
            internal long Token;
            internal Vector3 Anchor;
            internal float Deadline;
            internal bool Granted;
            internal uint Revision, MinimumState;
            internal TextInput Input;
            internal TextReceiver Receiver;
            internal string Topic;
            internal int Limit;
        }
        private sealed class Link { internal ZNetPeer Peer; internal long LastToken; }
        private static readonly Dictionary<ZRpc, Link> links = new Dictionary<ZRpc, Link>();
        private static readonly Dictionary<ZDOID, Entry> server = new Dictionary<ZDOID, Entry>();
        private static readonly Dictionary<long, List<Entry>> editors = new Dictionary<long, List<Entry>>();
        private static readonly HashSet<Entry> preparing = new HashSet<Entry>();
        private static readonly Dictionary<ZDOID, Entry> local = new Dictionary<ZDOID, Entry>();
        private static readonly List<Entry> work = new List<Entry>();
        private static Harmony hooks;
        private static Entry opening, displayed;
        private static bool bypass;
        private static long sequence;
        private static float next;
        private static readonly FieldInfo queued = AccessTools.Field(typeof(TextInput), "m_queuedSign");

        internal static void Start()
        {
            if (hooks != null) return;
            Harmony installing = new Harmony(Plugin.Id + ".textediting");
            try
            {
                if (queued == null || queued.FieldType != typeof(TextReceiver)) throw new NotSupportedException("Native text receiver changed.");
                Patch(installing, typeof(TextInput), "RequestText", "Request", new[] { typeof(TextReceiver), typeof(string), typeof(int) });
                Patch(installing, typeof(TextInput), "setText", "Save", new[] { typeof(string) });
                Patch(installing, typeof(ZNet), "OnNewConnection", "Connected", new[] { typeof(ZNetPeer) }, true);
                Patch(installing, typeof(ZNet), "Disconnect", "Disconnected", new[] { typeof(ZNetPeer) });
                Patch(installing, typeof(ZNet), "Shutdown", "Shutdown", new[] { typeof(bool) });
                Patch(installing, typeof(ZNetView), "ResetZDO", "Unloaded", Type.EmptyTypes);
                Patch(installing, typeof(ZNetView), "OnDestroy", "Unloaded", Type.EmptyTypes);
                hooks = installing;
                GridHandoffs.Protocol.TextEditingAvailable = PlayerSimulation.Network.TextEditingAvailable = true;
                if (ZNet.instance) foreach (ZNetPeer peer in ZNet.instance.GetPeers()) Connected(peer);
            }
            catch { installing.UnpatchSelf(); Reset(); throw; }
        }
        private static void Patch(Harmony harmony, Type type, string name, string hook, Type[] arguments, bool postfix = false)
        {
            MethodInfo method = AccessTools.Method(type, name, arguments);
            if (method == null) throw new NotSupportedException("Native text protection target changed: " + type.Name + "." + name);
            HarmonyMethod patch = new HarmonyMethod(typeof(Protection), hook) { priority = Priority.Last };
            if (postfix) harmony.Patch(method, postfix: patch); else harmony.Patch(method, prefix: patch);
        }
        internal static void Stop()
        {
            ClearLocal(true);
            if (hooks != null) hooks.UnpatchSelf(); hooks = null;
            GridHandoffs.Protocol.TextEditingAvailable = PlayerSimulation.Network.TextEditingAvailable = false; Reset();
        }
        private static bool Current(Link link, ZRpc rpc)
        { return ZNet.instance && link.Peer.IsReady() && ReferenceEquals(link.Peer.m_rpc, rpc) && ReferenceEquals(ZNet.instance.GetPeer(link.Peer.m_uid), link.Peer); }
        private static bool Supported(ZNetPeer peer)
        { return GridHandoffs.Protocol.SupportsTextEditing(peer) || (PlayerSimulation.Plugin.Network != null && PlayerSimulation.Plugin.Network.SupportsTextEditing(peer)); }
        private static void Connected(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null || links.ContainsKey(peer.m_rpc)) return;
            Link link = new Link { Peer = peer }; links.Add(peer.m_rpc, link);
            peer.m_rpc.Register<ZPackage>(Endpoint, delegate(ZRpc rpc, ZPackage packet) { Receive(link, rpc, packet); });
        }
        private static bool Send(ZNetPeer peer, Message message, ZDOID id, long token, uint revision = 0, uint minimumState = 0)
        {
            try
            {
                ZPackage packet = new ZPackage(); packet.Write((int)message); packet.Write(id); packet.Write(token); packet.Write(revision); packet.Write(minimumState);
                peer.m_rpc.Invoke(Endpoint, packet); return true;
            }
            catch (Exception error) { Plugin.Warn("Text-edit message unavailable: " + error.GetBaseException().Message); return false; }
        }
        private static bool Request(TextInput __instance, TextReceiver __0, string __1, int __2, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (bypass || !ZNet.instance || ZNet.instance.IsServer() || !Player.m_localPlayer) return true;
            if (opening != null && !ReferenceEquals(opening.Receiver, __0)) Release(opening);
            Component receiver = __0 as Component;
            if (!receiver || (receiver.GetType() != typeof(Sign) && receiver.GetType() != typeof(TeleportWorld))) return true;
            ZNetPeer host = ZNet.instance.GetServerPeer();
            if (!Supported(host)) return true; // Older servers retain their native text UI.
            ZNetView view = receiver.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.gameObject.activeInHierarchy) return false;
            ZDO data = view.GetZDO(); Entry previous;
            if (local.TryGetValue(data.m_uid, out previous))
            {
                if (Here(previous))
                {
                    bool ready = previous.Granted && ReferenceEquals(previous.Data, data) && data.IsOwner() && data.OwnerRevision == previous.Revision;
                    if (ready) displayed = previous; return ready;
                }
                Release(previous);
            }
            if (opening != null || local.Count >= 64) { Notice("Another text edit is still being prepared."); return false; }
            Entry entry = new Entry { Data = data, Id = data.m_uid, Peer = host, Token = ++sequence, Anchor = Player.m_localPlayer.transform.position,
                Deadline = Time.unscaledTime + 8f, Input = __instance, Receiver = __0, Topic = __1, Limit = __2 };
            opening = entry; local.Add(data.m_uid, entry); Bind();
            if (!Send(host, Message.Begin, data.m_uid, entry.Token))
            { RemoveLocal(entry); Notice("Text editing could not be prepared. Please try again."); }
            return false;
        }
        private static bool Save(TextInput __instance, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            Entry entry = displayed;
            if (entry == null || !ReferenceEquals(entry.Input, __instance) || !ReferenceEquals(queued.GetValue(__instance), entry.Receiver)) return true;
            Component receiver = entry.Receiver as Component; ZNetView view = receiver ? receiver.GetComponent<ZNetView>() : null;
            if (!view || !ReferenceEquals(view.GetZDO(), entry.Data) || !Identity(entry) || !entry.Data.IsOwner() ||
                entry.Data.OwnerRevision != entry.Revision || !Here(entry))
            { Release(entry); Notice("Text editing was cancelled because the object or player changed."); return false; }
            return true;
        }
        private static void Receive(Link link, ZRpc rpc, ZPackage packet)
        {
            Link registered;
            if (hooks == null || !links.TryGetValue(rpc, out registered) || !ReferenceEquals(registered, link) ||
                !Current(link, rpc) || !Supported(link.Peer) || packet == null || packet.Size() > 64) return;
            try
            {
                Message message = (Message)packet.ReadInt(); ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(); uint revision = packet.ReadUInt(), minimum = packet.ReadUInt();
                if (token <= 0 || id.IsNone()) return;
                if (ZNet.instance.IsServer())
                {
                    if (message == Message.End)
                    { link.LastToken = Math.Max(link.LastToken, token); Entry ended; if (server.TryGetValue(id, out ended) && ReferenceEquals(ended.Peer, link.Peer) && ended.Token == token) RemoveServer(ended, false); return; }
                    if (message != Message.Begin || token <= link.LastToken) return;
                    link.LastToken = token; Begin(link.Peer, id, token); return;
                }
                if (!link.Peer.m_server || !ReferenceEquals(ZNet.instance.GetServerPeer(), link.Peer)) return;
                Entry entry;
                if (!local.TryGetValue(id, out entry) || !ReferenceEquals(entry.Peer, link.Peer) || entry.Token != token) return;
                if (message == Message.Reject) { RemoveLocal(entry); Notice("Text editing was cancelled or the object is busy. Please try again."); }
                else if (message == Message.Grant) { entry.Granted = true; entry.Revision = revision; entry.MinimumState = minimum; }
                else if (message == Message.Native && ReferenceEquals(entry, opening)) { RemoveLocal(entry); Open(entry); }
            }
            catch (Exception error) { Plugin.Warn("Text-edit packet rejected: " + error.GetBaseException().Message); }
        }
        private static void Begin(ZNetPeer peer, ZDOID id, long token)
        {
            ZDO data = ZDOMan.instance == null ? null : ZDOMan.instance.GetZDO(id);
            ZDO actor = ZDOMan.instance == null || peer.m_characterID.IsNone() ? null : ZDOMan.instance.GetZDO(peer.m_characterID);
            List<Entry> playerEntries; int count = editors.TryGetValue(peer.m_uid, out playerEntries) ? playerEntries.Count : 0;
            if (server.ContainsKey(id) || server.Count >= 768 || count >= 64 || data == null || !data.IsValid() ||
                actor == null || !actor.IsValid() || actor.GetFloat(ZDOVars.s_health, 1f) <= 0 ||
                !Finite(actor.GetPosition()) || !Finite(data.GetPosition()) || (actor.GetPosition() - data.GetPosition()).sqrMagnitude > 100f)
            { Send(peer, Message.Reject, id, token); return; }
            bool managed = Plugin.Controller != null ? Plugin.Controller.ManagesText(data) :
                PlayerSimulation.Plugin.Controller != null && PlayerSimulation.Plugin.Controller.ManagesText(data);
            if (!managed) { Send(peer, Message.Native, id, token); return; }
            Entry entry = new Entry { Data = data, Id = id, Peer = peer, Token = token, Deadline = Time.unscaledTime + 8f };
            server.Add(id, entry); preparing.Add(entry);
            if (playerEntries == null) { playerEntries = new List<Entry>(); editors.Add(peer.m_uid, playerEntries); }
            playerEntries.Add(entry); Bind();
            try
            {
                bool prepared = Plugin.Controller != null ? Plugin.Controller.PrepareTextEdit(data) :
                    PlayerSimulation.Plugin.Controller != null && PlayerSimulation.Plugin.Controller.PrepareTextEdit(data);
                if (!prepared) RemoveServer(entry, true);
            }
            catch { RemoveServer(entry, true); throw; }
        }
        // No idle object/physics scan: only incomplete text openings are polled.
        // Active server locks have no movement polling; their editor releases
        // them using actual local movement, not delayed replicated positions.
        internal static void Poll()
        {
            if (preparing.Count == 0 && local.Count == 0) return;
            float now = Time.unscaledTime; if (now < next) return; next = now + .05f;
            if (ZNet.instance && ZNet.instance.IsServer())
            {
                if (preparing.Count == 0) return;
                work.Clear(); foreach (Entry entry in preparing) work.Add(entry);
                foreach (Entry entry in work)
                {
                    if (!server.ContainsKey(entry.Id)) continue;
                    if (now >= entry.Deadline || !Identity(entry) || !entry.Peer.IsReady()) { RemoveServer(entry, true); continue; }
                    if (Plugin.Controller != null && (!Plugin.Controller.IsActive || Plugin.Controller.Cooperative == null)) { RemoveServer(entry, true); continue; }
                    bool pending = Plugin.Controller != null ? Plugin.Controller.TransferPending(entry.Data) || Plugin.Controller.Cooperative.Reserved(entry.Data) :
                        PlayerSimulation.Plugin.Controller == null || PlayerSimulation.Plugin.Controller.Handoffs.Pending(entry.Id);
                    if (pending || entry.Data.GetOwner() != entry.Peer.m_uid) continue;
                    entry.Granted = true; entry.Revision = entry.Data.OwnerRevision; preparing.Remove(entry); Bind();
                    uint minimum; entry.MinimumState = entry.Data.DataRevision;
                    if (Plugin.Controller != null && Plugin.Controller.TryRecipientState(entry.Data, entry.Peer.m_uid, out minimum))
                        entry.MinimumState = Math.Max(entry.MinimumState, minimum);
                    EndPolicy(entry.Data); ZDOMan.instance.ForceSendZDO(entry.Peer.m_uid, entry.Id);
                    if (!Send(entry.Peer, Message.Grant, entry.Id, entry.Token, entry.Revision, entry.MinimumState)) RemoveServer(entry, false);
                }
                work.Clear(); return;
            }
            if (!ZNet.instance || !Player.m_localPlayer) { ClearLocal(true); return; }
            work.Clear(); foreach (Entry entry in local.Values) work.Add(entry);
            foreach (Entry entry in work)
            {
                if (!Identity(entry) || !Here(entry))
                { Release(entry); continue; }
                if (entry.Granted && !ReferenceEquals(entry, opening) && (!entry.Data.IsOwner() || entry.Data.OwnerRevision != entry.Revision))
                { Release(entry); continue; }
                if (!ReferenceEquals(entry, opening)) continue;
                if (now >= entry.Deadline) { Release(entry); Notice("Text editing timed out. Please try again."); continue; }
                if (!entry.Granted || !entry.Data.IsOwner() || entry.Data.OwnerRevision != entry.Revision || entry.Data.DataRevision < entry.MinimumState) continue;
                opening = null;
                if (!Open(entry)) Release(entry);
            }
            work.Clear();
        }
        private static bool Open(Entry entry)
        {
            Component receiver = entry.Receiver as Component;
            ZNetView view = receiver ? receiver.GetComponent<ZNetView>() : null;
            if (!receiver || !entry.Input || !view || !view.IsValid() || !Identity(entry) || !ReferenceEquals(view.GetZDO(), entry.Data) ||
                !view.gameObject.activeInHierarchy || !Here(entry) || !PrivateArea.CheckAccess(receiver.transform.position)) return false;
            try { bypass = true; entry.Input.RequestText(entry.Receiver, entry.Topic, entry.Limit); displayed = local.ContainsKey(entry.Id) ? entry : null; return true; }
            catch (Exception error) { Plugin.Warn("Native text input failed: " + error.GetBaseException().Message); return false; }
            finally { bypass = false; }
        }
        private static long Target(ZDO data)
        { Entry entry; return data != null && server.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data) && !entry.Granted ? entry.Peer.m_uid : 0; }
        private static bool Locked(ZDO data)
        { Entry entry; return data != null && server.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data) && entry.Granted; }
        private static bool Busy(ZDO data)
        { Entry entry; return data != null && local.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data); }
        private static void Bind()
        {
            AuthorityController.TextEditTarget = preparing.Count == 0 ? (Func<ZDO, long>)null : Target;
            AuthorityController.TextEditLocked = server.Count == preparing.Count ? (Func<ZDO, bool>)null : Locked;
            AuthorityController.TextEditOwnerObserved = server.Count == 0 ? (Action<ZDO>)null : OwnerChanged;
            AuthorityController.TextEditDestroyed = server.Count == 0 ? (Action<ZDO>)null : Destroyed;
            AuthorityController.TextEditNodeRemoved = server.Count == 0 ? (Action<ZDOID>)null : DestroyedId;
            PlayerSimulation.AuthorityController.TextEditTarget = AuthorityController.TextEditTarget;
            PlayerSimulation.AuthorityController.TextEditLocked = AuthorityController.TextEditLocked;
            PlayerSimulation.AuthorityController.TextEditOwnerObserved = AuthorityController.TextEditOwnerObserved;
            PlayerSimulation.AuthorityController.TextEditDestroyed = server.Count == 0 ? (Action<ZDOID>)null : DestroyedId;
            GridHandoffs.Protocol.TextEditBusy = local.Count == 0 ? (Func<ZDO, bool>)null : Busy;
            PlayerSimulation.LocalAgent.TextEditBusy = GridHandoffs.Protocol.TextEditBusy;
        }
        private static void EndPolicy(ZDO data)
        {
            if (Plugin.Controller != null) Plugin.Controller.EndTextEdit(data);
            if (PlayerSimulation.Plugin.Controller != null) PlayerSimulation.Plugin.Controller.EndTextEdit(data);
        }
        private static void RemoveServer(Entry entry, bool notify)
        {
            server.Remove(entry.Id); preparing.Remove(entry);
            List<Entry> playerEntries;
            if (editors.TryGetValue(entry.Peer.m_uid, out playerEntries))
            { playerEntries.Remove(entry); if (playerEntries.Count == 0) editors.Remove(entry.Peer.m_uid); }
            if (Identity(entry)) EndPolicy(entry.Data); Bind();
            if (notify && entry.Peer.IsReady()) Send(entry.Peer, Message.Reject, entry.Id, entry.Token);
        }
        private static void Release(Entry entry)
        {
            if (entry.Peer.IsReady()) Send(entry.Peer, Message.End, entry.Id, entry.Token);
            RemoveLocal(entry);
        }
        private static void RemoveLocal(Entry entry)
        {
            local.Remove(entry.Id); if (ReferenceEquals(opening, entry)) opening = null;
            if (ReferenceEquals(displayed, entry)) displayed = null;
            if (entry.Input && ReferenceEquals(queued.GetValue(entry.Input), entry.Receiver))
            { queued.SetValue(entry.Input, null); entry.Input.Hide(); }
            Bind();
        }
        private static void ClearLocal(bool notify)
        { var items = new List<Entry>(local.Values); foreach (Entry entry in items) { if (notify) Release(entry); else RemoveLocal(entry); } }
        private static void Unloaded(ZNetView __instance) { Destroyed(__instance.GetZDO()); }
        private static void Destroyed(ZDO __instance)
        {
            if (__instance == null) return; Entry entry;
            if (server.Count != 0 && server.TryGetValue(__instance.m_uid, out entry) && ReferenceEquals(entry.Data, __instance)) RemoveServer(entry, true);
            if (local.Count != 0 && local.TryGetValue(__instance.m_uid, out entry) && ReferenceEquals(entry.Data, __instance)) Release(entry);
        }
        private static void OwnerChanged(ZDO __instance)
        {
            if (__instance == null) return; Entry entry;
            if (server.Count != 0 && server.TryGetValue(__instance.m_uid, out entry) && ReferenceEquals(entry.Data, __instance) &&
                entry.Granted && (__instance.GetOwner() != entry.Peer.m_uid || __instance.OwnerRevision != entry.Revision)) RemoveServer(entry, true);
        }
        private static void DestroyedId(ZDOID id)
        { Entry entry; if (server.TryGetValue(id, out entry)) RemoveServer(entry, true); }
        private static void Disconnected(ZNetPeer peer)
        {
            if (peer == null) return; if (peer.m_rpc != null) links.Remove(peer.m_rpc);
            var removed = new List<Entry>(); foreach (Entry entry in server.Values) if (ReferenceEquals(entry.Peer, peer)) removed.Add(entry);
            foreach (Entry entry in removed) RemoveServer(entry, false);
            if (peer.m_server) ClearLocal(false);
        }
        private static bool Finite(Vector3 value) { return PlayerSimulation.AuthorityController.Finite(value); }
        private static bool Identity(Entry entry) { return entry.Data.IsValid() && entry.Data.m_uid.Equals(entry.Id); }
        private static bool Here(Entry entry)
        { if (!Player.m_localPlayer || Player.m_localPlayer.IsDead()) return false; Vector3 point = Player.m_localPlayer.transform.position; return Finite(point) && (point - entry.Anchor).sqrMagnitude <= .01f; }
        private static void Notice(string text) { if (Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.Center, "TooManyLags: " + text); }
        private static void Shutdown() { Reset(); }
        private static void Reset()
        { ClearLocal(false); var items = new List<Entry>(server.Values); foreach (Entry entry in items) RemoveServer(entry, false); links.Clear(); editors.Clear(); preparing.Clear(); opening = displayed = null; work.Clear(); next = 0; bypass = false; Bind(); }
    }
}
