using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TooManyLagsStandalone
{
    // Uses the normal guarded handoff and native TextInput. Only the edited
    // object is pinned; no inventory probe, ownership writer or grid-wide lock.
    internal static class TextEditingProtection
    {
        private enum Action { Begin, Grant, End, Reject, Native }
        private sealed class Entry
        {
            internal ZDO Data, Character;
            internal long Editor, Token;
            internal ZNetPeer Peer;
            internal Vector3 Anchor;
            internal float Deadline;
            internal bool Granted;
            internal ushort Revision;
            internal TextInput Input;
            internal Player Player;
            internal TextReceiver Receiver;
            internal string Topic;
            internal int Limit;
        }
        private static readonly Dictionary<ZDOID, Entry> server = new Dictionary<ZDOID, Entry>();
        private static readonly Dictionary<long, List<Entry>> editors = new Dictionary<long, List<Entry>>();
        private static readonly Dictionary<ZDOID, long> characters = new Dictionary<ZDOID, long>();
        private static readonly Dictionary<ZNetPeer, long> tokens = new Dictionary<ZNetPeer, long>();
        private static long hostToken;
        private static readonly Dictionary<ZDOID, Entry> local = new Dictionary<ZDOID, Entry>();
        private static readonly HashSet<Entry> preparing = new HashSet<Entry>();
        private static readonly List<Entry> scratch = new List<Entry>();
        private static readonly FieldInfo queued = AccessTools.Field(typeof(TextInput), "m_queuedSign");
        private static Entry opening;
        private static bool bypass;
        private static long sequence;
        private static float next;
        private static Vector3 lastPoint;
        private static Player sampledPlayer;
        private static bool sampled;
        internal static bool Supported { get { return queued != null && queued.FieldType == typeof(TextReceiver); } }
        internal static bool HasWork { get { return preparing.Count != 0 || local.Count != 0; } }
        internal static bool Tracks(ZDO data)
        { return data != null && ((server.Count != 0 && (server.ContainsKey(data.m_uid) || characters.ContainsKey(data.m_uid))) || (local.Count != 0 && local.ContainsKey(data.m_uid))); }
        private static bool Present(Entry entry)
        {
            if (Plugin.Network == null || !Plugin.Network.TextCapable(entry.Editor)) return false;
            return entry.Peer == null ? entry.Editor == ZDOMan.GetSessionID() :
                ReferenceEquals(ZNet.instance.GetPeer(entry.Editor), entry.Peer);
        }
        private static void Send(long uid, Action action, ZDOID id, long token, ushort revision = 0)
        {
            if (Plugin.Network == null) return;
            Plugin.Network.Send(uid, Message.TextEdit, delegate(ZPackage p) { p.Write((int)action); p.Write(id); p.Write(token); p.Write(revision); });
        }
        internal static bool Request(TextInput input, TextReceiver receiver, string topic, int limit, bool runOriginal)
        {
            if (!runOriginal) return false;
            if (bypass || !Supported || !Plugin.HooksActive || !ZNet.instance || !Player.m_localPlayer || ZDOMan.instance == null) return true;
            Component component = receiver as Component;
            if (!component || (component.GetType() != typeof(Sign) && component.GetType() != typeof(TeleportWorld))) return true;
            long host = ZNet.instance.IsServer() ? ZDOMan.GetSessionID() : ZNet.instance.GetServerPeer() != null ? ZNet.instance.GetServerPeer().m_uid : 0;
            if (Plugin.Network == null || !Plugin.Network.TextCapable(host)) return true;
            ZNetView view = component.GetComponent<ZNetView>();
            if (!view || !view.IsValid() || !view.gameObject.activeInHierarchy) return false;
            ZDO data = view.GetZDO(); Entry previous;
            if (local.TryGetValue(data.m_uid, out previous))
                return previous.Granted && ReferenceEquals(previous.Data, data) && data.IsOwner() && data.OwnerRevision == previous.Revision;
            if (opening != null || local.Count >= 64) { Notice("Another text edit is still being prepared."); return false; }
            Entry entry = new Entry { Data = data, Editor = ZDOMan.GetSessionID(), Token = ++sequence,
                Peer = ZNet.instance.IsServer() ? null : ZNet.instance.GetServerPeer(), Anchor = Player.m_localPlayer.transform.position,
                Deadline = Time.unscaledTime + 8f, Input = input, Player = Player.m_localPlayer, Receiver = receiver, Topic = topic, Limit = limit };
            local.Add(data.m_uid, entry); opening = entry; sampled = false; Bind();
            try { Send(host, Action.Begin, data.m_uid, entry.Token); Plugin.Network.FlushAll(); }
            catch { RemoveLocal(entry); Notice("Text editing could not be prepared. Please try again."); }
            return false;
        }
        internal static void Receive(ZNetPeer peer, long sender, ZPackage packet)
        {
            if (!Plugin.HooksActive || !ZNet.instance || ZDOMan.instance == null || packet == null || packet.Size() > 64 ||
                Plugin.Network == null || !Plugin.Network.TextCapable(sender)) return;
            bool host = ZNet.instance.IsServer(), own = host && peer == null && sender == ZDOMan.GetSessionID();
            if (host && !own && (peer == null || !ReferenceEquals(ZNet.instance.GetPeer(sender), peer))) return;
            if (!host && (peer == null || !peer.m_server || !ReferenceEquals(ZNet.instance.GetServerPeer(), peer))) return;
            try
            {
                Action action = (Action)packet.ReadInt(); ZDOID id = packet.ReadZDOID(); long token = packet.ReadLong(); ushort revision = packet.ReadUShort();
                if (id.IsNone() || token <= 0 || packet.GetPos() != packet.Size()) return;
                if (host && (action == Action.Begin || action == Action.End))
                {
                    long last = hostToken; if (peer != null) tokens.TryGetValue(peer, out last);
                    if (action == Action.End)
                    {
                        if (peer == null) hostToken = Math.Max(last, token); else tokens[peer] = Math.Max(last, token); Entry ended;
                        if (server.TryGetValue(id, out ended) && ended.Editor == sender && ReferenceEquals(ended.Peer, peer) && ended.Token == token) RemoveServer(ended, false);
                    }
                    else if (token > last) { if (peer == null) hostToken = token; else tokens[peer] = token; Begin(peer, sender, id, token); }
                    return;
                }
                if (host && !own) return;
                Entry entry;
                if (!local.TryGetValue(id, out entry) || !ReferenceEquals(entry.Peer, peer) || entry.Token != token) return;
                if (action == Action.Grant) { entry.Granted = true; entry.Revision = revision; }
                else if (action == Action.Reject) { RemoveLocal(entry); Notice("Text editing was cancelled or the object is busy. Please try again."); }
                else if (action == Action.Native && ReferenceEquals(opening, entry)) { RemoveLocal(entry); Open(entry); }
            }
            catch (Exception error) { Plugin.Warn("Ignored invalid text-edit packet: " + error.GetBaseException().Message); }
        }
        private static void Begin(ZNetPeer peer, long editor, ZDOID id, long token)
        {
            ZDO data = ZDOMan.instance.GetZDO(id), character = null;
            if (peer != null && !peer.m_characterID.IsNone()) character = ZDOMan.instance.GetZDO(peer.m_characterID);
            else if (peer == null && Player.m_localPlayer)
            { ZNetView player = Player.m_localPlayer.GetComponent<ZNetView>(); if (player && player.IsValid()) character = player.GetZDO(); }
            List<Entry> entries; int count = editors.TryGetValue(editor, out entries) ? entries.Count : 0;
            if (data == null || !data.IsValid() || character == null || !character.IsValid() || character.GetOwner() != editor || character.GetFloat(ZDOVars.s_health, 1f) <= 0 ||
                !AuthorityController.Finite(character.GetPosition()) || !AuthorityController.Finite(data.GetPosition()) ||
                (character.GetPosition() - data.GetPosition()).sqrMagnitude > 100f)
            { Send(editor, Action.Reject, id, token); return; }
            // One actor/connection generation per editor. A new incarnation
            // retires the old locks before sharing this player's small index.
            if (entries != null && entries.Count != 0 && (!ReferenceEquals(entries[0].Peer, peer) || !ReferenceEquals(entries[0].Character, character)))
            {
                for (int i = entries.Count - 1; i >= 0; i--) RemoveServer(entries[i], ReferenceEquals(entries[i].Peer, peer));
                entries = null; count = 0;
            }
            if (server.ContainsKey(id) || server.Count >= 768 || count >= 64) { Send(editor, Action.Reject, id, token); return; }
            if (Plugin.Controller == null || !Plugin.Controller.Active || !Plugin.Controller.ManagesText(data) || !Plugin.Network.TextCapable(data.GetOwner()))
            { Send(editor, Action.Native, id, token); return; }
            Entry entry = new Entry { Data = data, Character = character, Editor = editor, Peer = peer, Token = token,
                Anchor = character.GetPosition(), Deadline = Time.unscaledTime + 8f };
            server.Add(id, entry); preparing.Add(entry);
            if (entries == null) { entries = new List<Entry>(); editors.Add(editor, entries); characters[character.m_uid] = editor; }
            entries.Add(entry); Bind();
            if (!Plugin.Controller.PrepareTextEdit(data)) RemoveServer(entry, true);
        }
        internal static void Poll()
        {
            if (!HasWork || !ZNet.instance) return;
            float now = Time.unscaledTime; if (now < next) return; next = now + .05f;
            if (ZNet.instance.IsServer() && preparing.Count != 0)
            {
                scratch.Clear(); foreach (Entry entry in preparing) scratch.Add(entry);
                foreach (Entry entry in scratch)
                {
                    if (!preparing.Contains(entry)) continue;
                    if (now >= entry.Deadline || !entry.Data.IsValid() || !Present(entry) || Plugin.Controller == null) { RemoveServer(entry, true); continue; }
                    if (entry.Data.GetOwner() != entry.Editor || Plugin.Controller.Handoffs.Pending(entry.Data.m_uid) || Plugin.Controller.Handoffs.Committing(entry.Data.m_uid)) continue;
                    entry.Granted = true; entry.Revision = entry.Data.OwnerRevision; preparing.Remove(entry); Bind();
                    Plugin.Controller.EndTextEdit(entry.Data);
                    if (entry.Peer != null) ZDOMan.instance.ForceSendZDO(entry.Editor, entry.Data.m_uid);
                    Send(entry.Editor, Action.Grant, entry.Data.m_uid, entry.Token, entry.Revision);
                }
                scratch.Clear();
            }
            if (local.Count == 0) return;
            if (!Player.m_localPlayer) { ClearLocal(true); return; }
            Vector3 point = Player.m_localPlayer.transform.position;
            bool dead = Player.m_localPlayer.IsDead();
            if (opening == null && sampled && ReferenceEquals(sampledPlayer, Player.m_localPlayer) && !dead && (point - lastPoint).sqrMagnitude == 0f) return;
            lastPoint = point; sampledPlayer = Player.m_localPlayer; sampled = true;
            scratch.Clear(); foreach (Entry entry in local.Values) scratch.Add(entry);
            foreach (Entry entry in scratch)
            {
                if (!local.ContainsKey(entry.Data.m_uid)) continue;
                if (!entry.Data.IsValid() || !ReferenceEquals(entry.Player, Player.m_localPlayer) || dead || !AuthorityController.Finite(point) || (point - entry.Anchor).sqrMagnitude > .01f) { Release(entry); continue; }
                if (!ReferenceEquals(entry, opening)) continue;
                if (now >= entry.Deadline) { Release(entry); Notice("Text editing timed out. Please try again."); continue; }
                if (!entry.Granted || !entry.Data.IsOwner() || entry.Data.OwnerRevision != entry.Revision) continue;
                opening = null; if (!Open(entry)) Release(entry);
            }
            scratch.Clear();
        }
        private static bool Open(Entry entry)
        {
            Component component = entry.Receiver as Component; ZNetView view = component ? component.GetComponent<ZNetView>() : null;
            if (!component || !entry.Input || !view || !view.IsValid() || !ReferenceEquals(view.GetZDO(), entry.Data) ||
                !view.gameObject.activeInHierarchy || !PrivateArea.CheckAccess(component.transform.position)) return false;
            try { bypass = true; entry.Input.RequestText(entry.Receiver, entry.Topic, entry.Limit); return true; }
            finally { bypass = false; }
        }
        private static long Target(ZDO data)
        { Entry entry; return data != null && server.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data) && !entry.Granted ? entry.Editor : 0; }
        private static bool Locked(ZDO data)
        { Entry entry; return data != null && server.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data) && entry.Granted; }
        private static bool Busy(ZDO data)
        { Entry entry; return data != null && local.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data); }
        private static void Bind()
        {
            AuthorityController.TextEditTarget = preparing.Count == 0 ? (Func<ZDO, long>)null : Target;
            AuthorityController.TextEditLocked = server.Count == preparing.Count ? (Func<ZDO, bool>)null : Locked;
            AuthorityController.TextEditMovement = server.Count == 0 ? (System.Action<long, ZDO, Vector3>)null : Moved;
            LocalAgent.TextEditBusy = local.Count == 0 ? (Func<ZDO, bool>)null : Busy;
            NativeEvents.TextChanged = server.Count + local.Count == 0 ? (System.Action<ZDO>)null : Observed;
            NativeEvents.TextImport = server.Count + local.Count == 0 ? (Func<ZDO, bool>)null : Tracks;
        }
        private static void Moved(long uid, ZDO character, Vector3 point)
        {
            List<Entry> entries; if (!editors.TryGetValue(uid, out entries)) return;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (!ReferenceEquals(entry.Character, character) || character.GetOwner() != uid || !AuthorityController.Finite(point) ||
                    (point - entry.Anchor).sqrMagnitude > .01f || character.GetFloat(ZDOVars.s_health, 1f) <= 0) RemoveServer(entry, true);
            }
        }
        internal static void Observed(ZDO data)
        {
            if (data == null) return; Entry entry;
            long editor; if (characters.Count != 0 && characters.TryGetValue(data.m_uid, out editor)) Moved(editor, data, data.GetPosition());
            if (server.Count != 0 && server.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data) && entry.Granted &&
                (data.GetOwner() != entry.Editor || data.OwnerRevision != entry.Revision)) RemoveServer(entry, true);
            if (local.Count != 0 && local.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data) && entry.Granted &&
                !ReferenceEquals(entry, opening) && (!data.IsOwner() || data.OwnerRevision != entry.Revision)) Release(entry);
        }
        internal static void Removed(ZDO data)
        {
            if (data == null) return; Entry entry;
            long editor; List<Entry> entries;
            if (characters.Count != 0 && characters.TryGetValue(data.m_uid, out editor) && editors.TryGetValue(editor, out entries))
                for (int i = entries.Count - 1; i >= 0; i--) if (ReferenceEquals(entries[i].Character, data)) RemoveServer(entries[i], true);
            if (server.Count != 0 && server.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data)) RemoveServer(entry, true);
            if (local.Count != 0 && local.TryGetValue(data.m_uid, out entry) && ReferenceEquals(entry.Data, data)) Release(entry);
        }
        internal static void Disconnected(ZNetPeer peer)
        {
            if (peer == null) return;
            tokens.Remove(peer); List<Entry> entries;
            if (editors.TryGetValue(peer.m_uid, out entries))
                for (int i = entries.Count - 1; i >= 0; i--) if (ReferenceEquals(entries[i].Peer, peer)) RemoveServer(entries[i], false);
            if (peer.m_server)
            { var removed = new List<Entry>(); foreach (Entry entry in local.Values) if (ReferenceEquals(entry.Peer, peer)) removed.Add(entry); foreach (Entry entry in removed) RemoveLocal(entry); }
        }
        private static void RemoveServer(Entry entry, bool notify)
        {
            if (!server.Remove(entry.Data.m_uid)) return;
            preparing.Remove(entry); List<Entry> entries;
            if (editors.TryGetValue(entry.Editor, out entries)) { entries.Remove(entry); if (entries.Count == 0) { editors.Remove(entry.Editor); characters.Remove(entry.Character.m_uid); } }
            Bind(); if (Plugin.Controller != null) Plugin.Controller.EndTextEdit(entry.Data);
            if (notify && Present(entry)) Send(entry.Editor, Action.Reject, entry.Data.m_uid, entry.Token);
        }
        private static void Release(Entry entry)
        {
            long host = ZNet.instance && ZNet.instance.IsServer() ? ZDOMan.GetSessionID() : entry.Peer != null ? entry.Peer.m_uid : 0;
            Send(host, Action.End, entry.Data.m_uid, entry.Token); RemoveLocal(entry);
            if (Plugin.Network != null) Plugin.Network.FlushAll();
        }
        private static void RemoveLocal(Entry entry)
        {
            local.Remove(entry.Data.m_uid); if (ReferenceEquals(opening, entry)) opening = null;
            if (entry.Input && ReferenceEquals(queued.GetValue(entry.Input), entry.Receiver)) { queued.SetValue(entry.Input, null); entry.Input.Hide(); }
            Bind();
        }
        private static void ClearLocal(bool notify)
        { var entries = new List<Entry>(local.Values); foreach (Entry entry in entries) { if (notify) Release(entry); else RemoveLocal(entry); } }
        internal static void Reset()
        {
            ClearLocal(false); server.Clear(); editors.Clear(); characters.Clear(); tokens.Clear(); preparing.Clear(); scratch.Clear(); opening = null;
            next = 0; hostToken = 0; sampledPlayer = null; sampled = bypass = false; Bind();
        }
        private static void Notice(string text) { if (Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.Center, "TooManyLags: " + text); }
    }
    [HarmonyPatch(typeof(TextInput), "RequestText", new[] { typeof(TextReceiver), typeof(string), typeof(int) })]
    internal static class TextRequestPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.Last)]
        private static bool Prefix(TextInput __instance, TextReceiver __0, string __1, int __2, bool __runOriginal)
        { return TextEditingProtection.Request(__instance, __0, __1, __2, __runOriginal); }
    }
}
