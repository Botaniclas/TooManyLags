using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BotanicOwnerDebug
{
    internal sealed class DebugView : IDisposable
    {
        private struct Surface { internal int Index, Cull; }
        private sealed class Part { internal Renderer Renderer; internal Entry Entry; internal Surface[] Surfaces; }
        private sealed class Entry
        {
            internal ZDO Data; internal ZNetView View; internal OwnerKey Owner; internal bool Mob, Interactive, Live;
            internal int CheckedFrame;
            internal readonly HashSet<Renderer> Parts = new HashSet<Renderer>();
        }
        private readonly Harmony hooks = new Harmony(Plugin.Id);
        private readonly Dictionary<ZDOID, Entry> entries = new Dictionary<ZDOID, Entry>();
        private readonly Dictionary<ZNetView, ZDOID> byView = new Dictionary<ZNetView, ZDOID>();
        private readonly Dictionary<Renderer, Part> parts = new Dictionary<Renderer, Part>();
        private readonly Dictionary<Renderer, Part> interactiveParts = new Dictionary<Renderer, Part>();
        private readonly Dictionary<Renderer, Part> mobParts = new Dictionary<Renderer, Part>();
        private bool interactiveOnly, mobsOnly;
        private readonly HashSet<ZDOID> changed = new HashSet<ZDOID>(), models = new HashSet<ZDOID>();
        private readonly List<ZDOID> retiredEntries = new List<ZDOID>();
        private readonly List<Renderer> retiredParts = new List<Renderer>();
        private readonly HashSet<Renderer> seen = new HashSet<Renderer>();
        private readonly List<Material> sourceMaterials = new List<Material>();
        private readonly List<Surface> surfaces = new List<Surface>();
        private readonly Dictionary<long, string> names = new Dictionary<long, string>();
        private readonly NativeColours colours = new NativeColours();
        internal readonly List<KeyValuePair<OwnerKey, string>> Legend = new List<KeyValuePair<OwnerKey, string>>();
        internal string ModeLabel { get { return mobsOnly ? "Mobs only" : interactiveOnly ? "Mobs + interactive objects" : "All objects and mobs"; } }
        internal readonly string Note = "Players, effects and clusters excluded";
        internal int PaintedCount { get; private set; }
        internal int TrackedCount { get { return mobsOnly ? mobParts.Count : interactiveOnly ? interactiveParts.Count : parts.Count; } }
        private readonly Func<ZNetScene, Dictionary<ZDO, ZNetView>> instances;
        private readonly Plane[] planes = new Plane[6];
        private ZDOMan world; private ZNetScene scene; private ZNet network;
        private Camera camera; private CommandBuffer commands;
        private float nextRefresh, maxDistance; private long server; private int frame;
        internal DebugView()
        {
            FieldInfo registry = typeof(ZNetScene).GetField("m_instances", BindingFlags.Instance | BindingFlags.NonPublic);
            if (registry == null || registry.FieldType != typeof(Dictionary<ZDO, ZNetView>)) throw new NotSupportedException("Native instance registry changed.");
            DynamicMethod read = new DynamicMethod("BotanicDebugInstances", registry.FieldType, new[] { typeof(ZNetScene) }, typeof(DebugView).Module, true);
            ILGenerator il = read.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, registry); il.Emit(OpCodes.Ret);
            instances = (Func<ZNetScene, Dictionary<ZDO, ZNetView>>)read.CreateDelegate(typeof(Func<ZNetScene, Dictionary<ZDO, ZNetView>>));
            try { hooks.CreateClassProcessor(typeof(DebugHooks)).Patch(); }
            catch { hooks.UnpatchSelf(); throw; }
        }
        private bool Context()
        {
            if (!ZNet.instance || ZNet.instance.IsDedicated() || !ZNetScene.instance || ZDOMan.instance == null) return false;
            if (!ReferenceEquals(world, ZDOMan.instance) || !ReferenceEquals(scene, ZNetScene.instance) || !ReferenceEquals(network, ZNet.instance))
            {
                Clear(); world = ZDOMan.instance; scene = ZNetScene.instance; network = ZNet.instance;
                foreach (var pair in instances(scene)) Add(pair.Key, pair.Value);
            }
            ZNetPeer peer = network.GetServerPeer();
            long nextServer = network.IsServer() ? ZDOMan.GetSessionID() : peer != null && peer.IsReady() ? peer.m_uid : 0;
            if (server != nextServer) { server = nextServer; foreach (var id in entries.Keys) changed.Add(id); nextRefresh = 0; }
            return true;
        }
        internal void Add(ZDO data, ZNetView view)
        {
            if (data == null || !BuildingVisuals.Eligible(view) || !view.IsValid() || !ReferenceEquals(view.GetZDO(), data) || data.m_uid.IsNone()) return;
            ZDOID previous;
            if (byView.TryGetValue(view, out previous) && !previous.Equals(data.m_uid)) Remove(view);
            Entry old;
            if (entries.TryGetValue(data.m_uid, out old))
            { if (ReferenceEquals(old.Data, data) && ReferenceEquals(old.View, view)) return; Remove(old.View); }
            entries.Add(data.m_uid, new Entry { Data = data, View = view, Mob = view.GetComponent<Character>() });
            byView[view] = data.m_uid; changed.Add(data.m_uid); models.Add(data.m_uid);
        }
        internal void Remove(ZNetView view)
        {
            ZDOID id; Entry entry;
            if (!byView.TryGetValue(view, out id)) return;
            byView.Remove(view); changed.Remove(id); models.Remove(id);
            if (entries.TryGetValue(id, out entry) && ReferenceEquals(entry.View, view))
            { foreach (Renderer part in entry.Parts) { parts.Remove(part); interactiveParts.Remove(part); mobParts.Remove(part); } entries.Remove(id); }
        }
        internal void Changed(ZDO data)
        { if (data != null && world != null && ReferenceEquals(world, ZDOMan.instance) && entries.ContainsKey(data.m_uid)) changed.Add(data.m_uid); }
        internal void ModelsChanged(Component source)
        {
            if (!source) return; ZNetView view = source.GetComponentInParent<ZNetView>(); ZDOID id;
            if (view && byView.TryGetValue(view, out id)) models.Add(id);
        }
        private bool Valid(ZDOID id, Entry entry)
        { return entry.View && entry.View.IsValid() && ReferenceEquals(entry.View.GetZDO(), entry.Data) && entry.Data.IsValid() && ReferenceEquals(world.GetZDO(id), entry.Data); }
        private void Scan(Entry entry)
        {
            entry.Interactive = !entry.Mob && BuildingVisuals.Interactive(entry.View);
            Character rootActor = entry.View.GetComponent<Character>(); seen.Clear();
            ItemDrop rootItem = entry.View.GetComponent<ItemDrop>();
            foreach (Renderer renderer in entry.View.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer || (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) || renderer.GetComponentInParent<ZNetView>() != entry.View) continue;
                ItemDrop item = renderer.GetComponentInParent<ItemDrop>();
                if (item && item != rootItem) continue;
                Character actor = renderer.GetComponentInParent<Character>();
                if (actor is Player || (actor && !ReferenceEquals(actor, rootActor))) continue;
                seen.Add(renderer); entry.Parts.Add(renderer);
                sourceMaterials.Clear(); renderer.GetSharedMaterials(sourceMaterials); surfaces.Clear();
                for (int i = 0; i < sourceMaterials.Count; i++)
                {
                    Material source = sourceMaterials[i]; if (!BuildingVisuals.Tintable(source)) continue;
                    int cull = source.HasProperty("_Cull") ? source.GetInt("_Cull") : 2;
                    if (cull < 0 || cull > 2) cull = 2;
                    surfaces.Add(new Surface { Index = i, Cull = cull });
                }
                sourceMaterials.Clear();
                Part part;
                if (!parts.TryGetValue(renderer, out part)) { part = new Part { Renderer = renderer, Entry = entry }; parts.Add(renderer, part); }
                bool same = part.Surfaces != null && part.Surfaces.Length == surfaces.Count;
                for (int i = 0; same && i < surfaces.Count; i++) same = part.Surfaces[i].Index == surfaces[i].Index && part.Surfaces[i].Cull == surfaces[i].Cull;
                if (!same) part.Surfaces = surfaces.ToArray();
                if (entry.Mob || entry.Interactive) interactiveParts[renderer] = part; else interactiveParts.Remove(renderer);
                if (entry.Mob) mobParts[renderer] = part; else mobParts.Remove(renderer);
            }
            retiredParts.Clear(); foreach (Renderer part in entry.Parts) if (!seen.Contains(part)) retiredParts.Add(part);
            foreach (Renderer part in retiredParts) { parts.Remove(part); interactiveParts.Remove(part); mobParts.Remove(part); entry.Parts.Remove(part); }
            seen.Clear(); retiredParts.Clear(); surfaces.Clear();
        }
        private OwnerKey Resolve(ZDO data)
        { return OwnerKey.Resolve(data != null && data.IsValid() && ReferenceEquals(world.GetZDO(data.m_uid), data), data == null ? 0 : data.GetOwner(), server); }
        private void Refresh()
        {
            names.Clear(); Legend.Clear(); var used = new HashSet<OwnerKey>();
            foreach (ZNet.PlayerInfo player in network.GetPlayerList())
            {
                string name = string.IsNullOrEmpty(player.m_name) ? "Player" : player.m_name;
                // Player characters are excluded from the overlay, but every
                // synchronised player still belongs in its colour legend.
                if (player.m_characterID.IsNone() || player.m_characterID.UserID == 0)
                {
                    Legend.Add(new KeyValuePair<OwnerKey, string>(OwnerKey.Resolve(false, 0, server), name + " (owner ID loading)"));
                    continue;
                }
                long uid = player.m_characterID.UserID; names[uid] = name;
                used.Add(OwnerKey.Resolve(true, uid, server));
            }
            used.Add(OwnerKey.Resolve(true, server, server)); used.Add(OwnerKey.Resolve(true, 0, server)); used.Add(OwnerKey.Resolve(false, 0, server));
            retiredEntries.Clear();
            foreach (var pair in entries)
            {
                Entry entry = pair.Value;
                if (!Valid(pair.Key, entry)) { retiredEntries.Add(pair.Key); continue; }
                if (mobsOnly ? entry.Mob : !interactiveOnly || entry.Mob || entry.Interactive) used.Add(entry.Owner);
                // Vanilla buildings keep their registered model variants.
                // Only a few moving/equipped mobs get a seconds fallback scan.
                if (entry.Mob) Scan(entry);
            }
            foreach (ZDOID id in retiredEntries) Remove(entries[id].View); retiredEntries.Clear();
            foreach (OwnerKey key in used)
            {
                string label;
                if (key.Kind == OwnerKind.Server) label = "Server";
                else if (key.Kind == OwnerKind.Unowned) label = "Unowned";
                else if (key.Kind == OwnerKind.Unknown) label = "Unknown owner / missing ZDO";
                else { string name; label = (names.TryGetValue(key.Uid, out name) ? name : "Owner") + " [" + key.Uid + "]"; }
                Legend.Add(new KeyValuePair<OwnerKey, string>(key, label));
            }
            Legend.Sort(delegate(KeyValuePair<OwnerKey, string> a, KeyValuePair<OwnerKey, string> b) { int kind = a.Key.Kind.CompareTo(b.Key.Kind); if (kind != 0) return kind; int uid = a.Key.Uid.CompareTo(b.Key.Uid); return uid != 0 ? uid : StringComparer.Ordinal.Compare(a.Value, b.Value); });
            nextRefresh = Time.unscaledTime + 1f;
        }
        private void UpdateEntries()
        {
            retiredEntries.Clear();
            foreach (ZDOID id in changed)
            {
                Entry entry; if (!entries.TryGetValue(id, out entry)) continue;
                if (!Valid(id, entry)) { retiredEntries.Add(id); continue; }
                entry.Owner = Resolve(entry.Data);
            }
            foreach (ZDOID id in retiredEntries) Remove(entries[id].View); retiredEntries.Clear(); changed.Clear();
            foreach (ZDOID id in models)
            {
                Entry entry; if (entries.TryGetValue(id, out entry) && Valid(id, entry)) Scan(entry);
            }
            models.Clear();
        }
        private void Attach(Camera next)
        {
            if (ReferenceEquals(camera, next) && commands != null) return;
            Detach(); camera = next; commands = new CommandBuffer { name = "BotanicOwnerDebugOnly" };
            // Keep the established render stage. The flat pass intentionally
            // ignores depth; the renderer carries transform/range/live skin.
            camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, commands);
        }
        private void Detach()
        {
            CommandBuffer old = commands; commands = null;
            try { if (camera && old != null) camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, old); }
            finally { camera = null; if (old != null) old.Release(); }
        }
        internal void Draw(int seed, float strength, float distance, bool onlyInteractive = false, bool onlyMobs = false)
        {
            PaintedCount = 0; if (!Context()) { Clear(); return; }
            Camera next = Camera.main; if (!next) { Detach(); return; }
            if (interactiveOnly != onlyInteractive || mobsOnly != onlyMobs) { interactiveOnly = onlyInteractive; mobsOnly = onlyMobs; nextRefresh = 0; }
            Attach(next); commands.Clear(); maxDistance = distance * distance; colours.Configure(seed, strength); frame++;
            UpdateEntries(); if (Time.unscaledTime >= nextRefresh) Refresh();
            GeometryUtility.CalculateFrustumPlanes(camera, planes); Vector3 position = camera.transform.position;
            foreach (Part part in (mobsOnly ? mobParts : interactiveOnly ? interactiveParts : parts).Values)
            {
                Renderer renderer = part.Renderer;
                // PUP-disabled originals stop here without material/model work.
                if (!renderer || !renderer.enabled || part.Surfaces.Length == 0 || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy || !renderer.isVisible || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                Bounds bounds = renderer.bounds; int layer = renderer.gameObject.layer;
                if ((camera.cullingMask & (1 << layer)) == 0 || (bounds.center - position).sqrMagnitude > maxDistance || !GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                Entry entry = part.Entry;
                if (entry.CheckedFrame != frame) { entry.Live = Valid(entry.Data.m_uid, entry); entry.CheckedFrame = frame; }
                if (!entry.Live) continue;
                foreach (Surface surface in part.Surfaces) commands.DrawRenderer(renderer, colours.Get(part.Entry.Owner, surface.Cull), surface.Index, 0);
                PaintedCount++;
            }
            colours.Sweep();
        }
        internal void Clear()
        {
            Detach(); colours.Clear(); entries.Clear(); byView.Clear(); parts.Clear(); interactiveParts.Clear(); mobParts.Clear(); changed.Clear(); models.Clear(); retiredEntries.Clear(); retiredParts.Clear(); seen.Clear(); sourceMaterials.Clear(); surfaces.Clear(); names.Clear(); Legend.Clear();
            world = null; scene = null; network = null; server = 0; nextRefresh = 0; PaintedCount = 0;
        }
        public void Dispose() { try { hooks.UnpatchSelf(); } finally { Clear(); } }
    }
    [HarmonyPatch]
    internal static class DebugHooks
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZNetScene), "AddInstance");
            yield return AccessTools.Method(typeof(ZNetView), "ResetZDO");
            yield return AccessTools.Method(typeof(ZNetView), "OnDestroy");
            yield return AccessTools.Method(typeof(ZDO), "SetOwnerInternal");
            yield return AccessTools.PropertySetter(typeof(ZDO), "OwnerRevision");
            yield return AccessTools.Method(typeof(ZNet), "Shutdown");
            foreach (string name in new[] { "AttachItem", "AttachArmor", "AttachBackItem", "SetRightItemVisual" })
            {
                MethodInfo method = AccessTools.Method(typeof(VisEquipment), name);
                if (method != null) yield return method;
            }
        }
        [HarmonyPrefix]
        private static void Before(object __instance, MethodBase __originalMethod)
        {
            DebugView view = Plugin.Instance ? Plugin.Instance.View : null; if (view == null) return;
            try
            {
                if (__instance is ZNetView) view.Remove((ZNetView)__instance);
                else if (__originalMethod.Name == "Shutdown") view.Clear();
            }
            catch { /* Debug cannot interrupt native cleanup. */ }
        }
        [HarmonyPostfix]
        private static void After(object __instance, object[] __args, MethodBase __originalMethod)
        {
            DebugView view = Plugin.Instance ? Plugin.Instance.View : null; if (view == null) return;
            try
            {
                if (__instance is ZDO) view.Changed((ZDO)__instance);
                else if (__originalMethod.Name == "AddInstance") view.Add((ZDO)__args[0], (ZNetView)__args[1]);
                else if (__instance is VisEquipment) view.ModelsChanged((VisEquipment)__instance);
            }
            catch { /* Debug errors never disable PUP gameplay guards. */ }
        }
    }
}
