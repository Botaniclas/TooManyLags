using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using BotanicOwnerDebug;
class DebugTests
{
    static int checks, foreignCalls; static uint next;
    static readonly int colour = Shader.PropertyToID("_Color");
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Equal(Color a, Color b) { return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a; }
    static void Reset()
    {
        if (Plugin.Instance && Plugin.Instance.View != null) { Plugin.Instance.View.Dispose(); Plugin.Instance.View = null; }
        Plugin.Instance = new GameObject().Add<Plugin>(); ZNetScene.instance = new GameObject().Add<ZNetScene>();
        ZNet.instance = new GameObject().Add<ZNet>(); ZDOMan.instance = new ZDOMan();
        Camera.main = new GameObject().Add<Camera>(); Camera.main.transform.position = new Vector3(0, 0, -10);
        Time.unscaledTime = 0; Graphics.Draws.Clear(); Shader.MissingColour = false; next = 0;
    }
    static Material Solid() { return new Material(new Shader { name = "Custom/Piece" }); }
    static ZNetView Spawn(long owner, Material source, bool mob)
    {
        var g = new GameObject(); if (mob) g.Add<Character>(); else g.Add<Piece>(); var v = g.Add<ZNetView>();
        v.Data = new ZDO { m_uid = new ZDOID { UserID = 987654, ID = ++next }, Owner = owner };
        if (mob) g.Add<SkinnedMeshRenderer>().sharedMesh = new Mesh();
        else { g.Add<MeshRenderer>(); g.Add<MeshFilter>().sharedMesh = new Mesh(); }
        var r = g.Get<Renderer>(); r.SourceMaterials.Clear(); r.SourceMaterials.Add(source);
        ZDOMan.instance.Objects[v.Data.m_uid] = v.Data; ZNetScene.instance.AddInstance(v.Data, v); return v;
    }
    static DebugView Start() { var v = new DebugView(); Plugin.Instance.View = v; return v; }
    static void Tick(DebugView view, float time, bool interactiveOnly = false, bool mobsOnly = false)
    { Time.unscaledTime = time; Graphics.Draws.Clear(); view.Draw(1337, 1f, 200, interactiveOnly, mobsOnly); if (Camera.main) Camera.main.RenderDebug(); }
    static void Colour(long owner)
    {
        Rgb rgb = OwnerColour.Get(OwnerKey.Resolve(true, owner, 1), 1337);
        Check(Graphics.Draws.Any(d => Equal(d.Colour, new Color(rgb.R, rgb.G, rgb.B, 1))), "Live owner colour " + owner);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static void ForeignPostfix() { foreignCalls++; }
    static void Main()
    {
        try
        {
            DisplayMode mode = DisplayMode.Off;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                mode = DisplayModes.Next(mode); Check(mode == DisplayMode.MobsOnly, "F10 first press selects only mobs");
                mode = DisplayModes.Next(mode); Check(mode == DisplayMode.InteractiveAndMobs, "F10 second press selects interactions plus mobs");
                mode = DisplayModes.Next(mode); Check(mode == DisplayMode.All, "F10 third press selects all");
                mode = DisplayModes.Next(mode); Check(mode == DisplayMode.Off, "F10 fourth press disables overlay");
            }
            Check(DisplayModes.Current(false, false) == DisplayMode.Off && DisplayModes.Current(false, true) == DisplayMode.Off, "Disabled config always means off");
            Check(DisplayModes.Current(true, false) == DisplayMode.All && DisplayModes.Current(true, true) == DisplayMode.InteractiveAndMobs, "Existing enabled config and new filter map to correct mode");
            Check(DisplayModes.Current(true, false, true) == DisplayMode.MobsOnly && DisplayModes.Current(true, true, true) == DisplayMode.MobsOnly, "Mobs-only config takes priority over interactive filter");
            Check(DisplayModes.Current(false, true, true) == DisplayMode.Off && DisplayModes.Next(DisplayModes.Current(false, true, true)) == DisplayMode.MobsOnly, "Disabled saved filters still restart cycle at Mobs only");
            Check(OwnerKey.Resolve(false, 1, 1).Kind == OwnerKind.Unknown, "Unknown precedence");
            Check(OwnerKey.Resolve(true, 0, 1).Kind == OwnerKind.Unowned, "Unowned classification");
            Check(OwnerKey.Resolve(true, 1, 1).Kind == OwnerKind.Server, "Server classification");
            var grey = OwnerColour.Get(OwnerKey.Resolve(true, 0, 1), 42);
            var white = OwnerColour.Get(OwnerKey.Resolve(true, 1, 1), 99);
            Check(grey.R == .18f && grey.G == .18f && grey.B == .18f, "Unowned always dark grey");
            Check(white.R == 1 && white.G == 1 && white.B == 1, "Server always white");
            OwnerColour.Reset();
            var fixedColours = new System.Collections.Generic.List<Rgb>();
            for (long id = 1000; id < 1012; id++)
            {
                Rgb c = OwnerColour.Get(OwnerKey.Resolve(true, id, 1), 1337);
                Check(!fixedColours.Any(x => x.R == c.R && x.G == c.G && x.B == c.B), "Twelve unique palette colours");
                fixedColours.Add(c);
            }
            for (int n = 0; n < 12; n++)
            {
                Rgb c = OwnerColour.Get(OwnerKey.Resolve(true, 1000+n, 1), 1337);
                Check(c.R == fixedColours[n].R && c.G == fixedColours[n].G && c.B == fixedColours[n].B, "Existing colour stays fixed");
            }
            OwnerColour.Reset();
            for (long id = -500; id < 500; id++)
            {
                Rgb c = OwnerColour.Get(OwnerKey.Resolve(true, id, 1), 1337);
                Check(c.R >= 0 && c.R <= 1 && c.G >= 0 && c.G <= 1 && c.B >= 0 && c.B <= 1, "Colour bounds");
                if (id != 0 && id != 1) Check(Math.Max(c.R, Math.Max(c.G, c.B)) == 1 && Math.Min(c.R, Math.Min(c.G, c.B)) == 0, "Saturated player colours");
            }
            Reset(); var source = Solid(); var native = Spawn(77, source, false); var r = native.GetComponent<Renderer>();
            r.localToWorldMatrix = new Matrix4x4 { Marker = 987 }; var mesh = native.GetComponent<MeshFilter>().sharedMesh;
            int assignmentCount = Metrics.Assignments, copyCount = Metrics.Copies, bakeCount = Metrics.Bakes;
            var debug = Start(); Tick(debug, 0); Colour(77);
            Check(Graphics.Draws.Single().Renderer == r && Graphics.Draws.Single().Mesh == mesh && Graphics.Draws.Single().MatrixMarker == 987, "Native renderer is submitted, no copied matrix/mesh");
            Check(r.SourceMaterials.Single() == source && Metrics.Assignments == assignmentCount && Metrics.Copies == copyCount && Metrics.Bakes == bakeCount, "Original source/bindings untouched; no copies or baking");
            var paint = Graphics.Draws.Single().Material;
            Check(paint.shader.name == "Hidden/Internal-Colored" && paint.Ints["_ZTest"] == 8 && paint.Ints["_ZWrite"] == 0 && paint.Ints["_Cull"] == 2, "Flat through-wall pass, no depth writing");
            Check(debug.PaintedCount == 1 && debug.TrackedCount == 1, "Visible/registered counters");
            int scans = Metrics.Scans, reads = Metrics.OwnerReads, writes = Metrics.ColourWrites, matReads = Metrics.MaterialReads;
            Tick(debug, .1f);
            Check(Metrics.Scans == scans && Metrics.OwnerReads == reads && Metrics.ColourWrites == writes && Metrics.MaterialReads == matReads, "Stable frame has no model/material/owner/colour work");
            Tick(debug, 1.1f); Check(Metrics.Scans == scans && Metrics.MaterialReads == matReads, "Building has no repeating hierarchy/material scans");
            native.Data.SetOwnerInternal(88); Tick(debug, 1.11f); Colour(88);
            Check(Metrics.Scans == scans && Metrics.MaterialReads == matReads, "Owner transition does not rescan model/materials");
            native.Data.Owner = 99; native.Data.OwnerRevision++; Tick(debug, 1.12f); Colour(99);
            native.Data.SetOwnerInternal(0); Tick(debug, 1.13f); Colour(0);
            native.Data.SetOwnerInternal(1); Tick(debug, 1.14f); Colour(1);
            Check(Graphics.Draws.Single().Renderer == r && source.Alive && r.SourceMaterials.Single() == source, "All owner transitions keep original rendering resources");
            // Commands retain the native reference; pose/scale is live when rendered.
            Graphics.Draws.Clear(); debug.Draw(1337, 1, 200);
            var secondMesh = new Mesh(); native.GetComponent<MeshFilter>().sharedMesh = secondMesh; r.localToWorldMatrix = new Matrix4x4 { Marker = 123 };
            Camera.main.RenderDebug();
            Check(Graphics.Draws.Single().Mesh == secondMesh && Graphics.Draws.Single().MatrixMarker == 123 && mesh.Alive, "Late mesh/transform changes use native renderer at execution time");
            r.enabled = false; Tick(debug, 1.15f); Check(Graphics.Draws.Count == 0 && !r.enabled, "PUP disabled original skipped");
            r.enabled = true; r.forceRenderingOff = true; Tick(debug, 1.16f); Check(Graphics.Draws.Count == 0, "Force-off respected");
            r.forceRenderingOff = false; r.isVisible = false; Tick(debug, 1.17f); Check(Graphics.Draws.Count == 0, "Native visibility respected");
            r.isVisible = true; native.gameObject.activeInHierarchy = false; Tick(debug, 1.18f); Check(Graphics.Draws.Count == 0, "Inactive model respected");
            native.gameObject.activeInHierarchy = true; r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; Tick(debug, 1.19f); Check(Graphics.Draws.Count == 0, "Shadow-only skipped");
            r.shadowCastingMode = ShadowCastingMode.On; r.bounds = new Bounds { center = new Vector3(300, 0, 0), Visible = true }; Tick(debug, 1.2f); Check(Graphics.Draws.Count == 0, "Distance checked every frame");
            r.bounds = new Bounds { Visible = false }; Tick(debug, 1.21f); Check(Graphics.Draws.Count == 0, "Frustum respected");
            r.bounds = new Bounds { Visible = true }; Camera.main.cullingMask = 0; Tick(debug, 1.22f); Check(Graphics.Draws.Count == 0, "Camera layer respected");
            Camera.main.cullingMask = -1;
            var foreignSource = Solid(); r.SourceMaterials[0] = foreignSource; debug.Dispose(); Plugin.Instance.View = null;
            Check(r.SourceMaterials[0] == foreignSource && foreignSource.Alive && source.Alive && !paint.Alive, "Off destroys only own paint, preserves foreign replacement");

            Reset(); source = Solid(); var mob = Spawn(77, source, true); debug = Start(); Tick(debug, 0); Colour(77);
            var skin = mob.GetComponent<SkinnedMeshRenderer>(); var originalSkin = skin.sharedMesh;
            Check(Graphics.Draws.Single().Renderer == skin && Graphics.Draws.Single().Mesh == originalSkin && Metrics.Bakes == 0, "Animated mob uses real skinned renderer, no pose bake");
            Graphics.Draws.Clear(); debug.Draw(1337, 1, 200); skin.sharedMesh = secondMesh; skin.localToWorldMatrix = new Matrix4x4 { Marker = 456 }; Camera.main.RenderDebug();
            Check(Graphics.Draws.Single().Mesh == secondMesh && Graphics.Draws.Single().MatrixMarker == 456 && originalSkin.Alive, "Mob live skin/scale state retained");
            mob.Data.SetOwnerInternal(88); Tick(debug, .1f); Colour(88);
            var g = new GameObject(); mob.gameObject.Child(g); var newPart = g.Add<MeshRenderer>(); g.Add<MeshFilter>().sharedMesh = new Mesh();
            var equip = mob.gameObject.Add<VisEquipment>(); equip.AttachItem(); Tick(debug, .11f);
            Check(Graphics.Draws.Count == 2, "Model attachment event finds new renderer immediately");
            mob.gameObject.Children.Remove(g); Tick(debug, 1.2f); Check(Graphics.Draws.Count == 1, "Mob fallback removes detached model part");
            Reset(); source = Solid(); native = Spawn(77, source, false); native.gameObject.Add<Player>(); debug = Start(); Tick(debug, 0); Check(Graphics.Draws.Count == 0, "Player root excluded");
            Reset(); source = Solid(); native = Spawn(77, source, false); var child = Spawn(88, source, false); native.gameObject.Child(child.gameObject); debug = Start(); Tick(debug, 0);
            Check(Graphics.Draws.Count == 2, "Nested independent network renderer counted once"); Colour(77); Colour(88);
            var charTree = new GameObject(); native.gameObject.Child(charTree); charTree.Add<Player>(); var charR = charTree.Add<MeshRenderer>(); charTree.Add<MeshFilter>().sharedMesh = new Mesh();
            var staticEquip = native.gameObject.Add<VisEquipment>(); staticEquip.AttachArmor(); Tick(debug, .1f);
            Check(Graphics.Draws.Count == 2 && Graphics.Draws.All(d => d.Renderer != charR), "Player subtree in building excluded");

            Reset(); source = Solid(); native = Spawn(77, source, false); r = native.GetComponent<Renderer>();
            var alpha = Solid(); alpha.renderQueue = 3000; alpha.Tag = "Transparent"; r.SourceMaterials.Add(alpha);
            var fx = new GameObject(); native.gameObject.Child(fx); var fxR = fx.Add<MeshRenderer>(); fx.Add<MeshFilter>().sharedMesh = new Mesh();
            fxR.SourceMaterials.Clear(); fxR.SourceMaterials.Add(new Material(new Shader { name = "Custom/Smoke" }));
            var ps = new GameObject(); native.gameObject.Child(ps); ps.Add<ParticleSystemRenderer>();
            debug = Start(); Tick(debug, 0);
            Check(Graphics.Draws.Count == 1 && Graphics.Draws.Single().Submesh == 0 && Graphics.Draws.Single().Renderer == r, "Particle/mesh effect and transparent slot excluded");
            Check(r.SourceMaterials.SequenceEqual(new[] { source, alpha }) && fxR.SourceMaterials[0].shader.name == "Custom/Smoke", "Effect/original slots untouched");
            source = Solid(); source.Colours.Clear(); Check(BuildingVisuals.Tintable(source), "Flat overlay supports opaque native material without colour property");
            source.SetInt("_ZWrite", 0); Check(!BuildingVisuals.Tintable(source), "No-depth source excluded");
            source = Solid(); source.SetColor(colour, new Color(1, 1, 1, 0)); Check(!BuildingVisuals.Tintable(source), "Invisible original colour excluded");
            foreach (string name in new[] { "Custom/VFX", "Custom/Glow", "Custom/Billboard", "Custom/Distortion", "UI/Default", "Hidden/Test", "Custom/Particles", "Custom/Smoke", "Custom/Fog", "Custom/Invisible", "Custom/DepthOnly" })
                Check(!BuildingVisuals.Tintable(new Material(new Shader { name = name })), "Effect/invisible shader excluded " + name);
            Reset(); source = Solid(); native = Spawn(77, source, false); native.gameObject.Components.Remove(native.GetComponent<Piece>());
            debug = Start(); Tick(debug, 0); Check(Graphics.Draws.Count == 0, "Nonbuilding root excluded");
            Reset(); source = Solid(); native = Spawn(77, source, false); native.gameObject.Components.Remove(native.GetComponent<Piece>()); native.gameObject.Add<WearNTear>();
            debug = Start(); Tick(debug, 0); Check(Graphics.Draws.Count == 1, "WearNTear structural root accepted");
            Reset(); source = Solid(); native = Spawn(77, source, false); native.gameObject.Components.Remove(native.GetComponent<Piece>()); native.gameObject.Add<ItemDrop>();
            debug = Start(); Tick(debug, 0, true); Check(Graphics.Draws.Count == 1 && BuildingVisuals.Interactive(native), "Loose items are interactive without Piece/WearNTear");
            native.Data.SetOwnerInternal(78); Tick(debug, .01f, true); Colour(78);
            Tick(debug, .02f, false, true); Check(Graphics.Draws.Count == 0, "Mobs-only filter hides loose items");
            Tick(debug, .03f); Check(Graphics.Draws.Count == 1, "All mode includes loose items");
            native.ResetZDO(); Tick(debug, .04f, true); Check(Graphics.Draws.Count == 0 && debug.TrackedCount == 0, "Picked-up/unloaded item leaves overlay registry");
            Reset(); native = Spawn(79, Solid(), false); var held = new GameObject(); native.gameObject.Child(held); held.Add<ItemDrop>();
            held.Add<MeshRenderer>().SourceMaterials.Add(Solid()); held.Add<MeshFilter>().sharedMesh = new Mesh();
            debug = Start(); Tick(debug, 0); Check(Graphics.Draws.Count == 1, "Embedded item visuals do not become a second overlay");
            Reset(); native = Spawn(80, Solid(), false); native.gameObject.Add<ItemDrop>(); var effect = new GameObject(); native.gameObject.Child(effect);
            var itemEffect = effect.Add<MeshRenderer>(); itemEffect.SourceMaterials.Clear(); itemEffect.SourceMaterials.Add(new Material(new Shader { name="Custom/Glow" })); effect.Add<MeshFilter>().sharedMesh = new Mesh();
            debug = Start(); Tick(debug, 0, true); Check(Graphics.Draws.Count == 1, "Loose item effects remain excluded");
            Reset(); native = Spawn(81, Solid(), false); native.gameObject.Add<Player>(); native.gameObject.Add<ItemDrop>();
            debug = Start(); Tick(debug, 0, true); Check(Graphics.Draws.Count == 0, "Player root stays excluded even with an item component");
            Reset(); source = Solid(); native = Spawn(77, source, false); native.m_persistent = false; debug = Start(); Tick(debug, 0); Check(Graphics.Draws.Count == 0, "Transient root excluded");
            Reset(); source = Solid(); native = Spawn(77, source, false); native.m_distant = true; debug = Start(); Tick(debug, 0); Check(Graphics.Draws.Count == 0, "Distant proxy excluded");

            Reset(); source = Solid(); native = Spawn(77, source, false); debug = Start(); Tick(debug, 0);
            var firstCamera = Camera.main; var ownBuffer = firstCamera.Buffers.Single(); Tick(debug, .1f);
            Check(firstCamera.Added == 1 && ownBuffer.Queued.Count == 1, "No duplicate buffers/stale commands");
            var foreignBuffer = new CommandBuffer(); firstCamera.AddCommandBuffer(CameraEvent.BeforeImageEffects, foreignBuffer);
            Camera.main = new GameObject().Add<Camera>(); Tick(debug, .2f);
            Check(ownBuffer.Released && firstCamera.Buffers.SequenceEqual(new[] { foreignBuffer }), "Camera change removes only own buffer");
            var replacementCamera = Camera.main; var replacementBuffer = replacementCamera.Buffers.Single(); Camera.main = null; Tick(debug, .3f);
            Check(replacementBuffer.Released && replacementCamera.Buffers.Count == 0, "Missing camera detaches paint");
            foreignBuffer.Release(); Camera.main = replacementCamera; Tick(debug, .4f); native.ResetZDO(); Tick(debug, .5f); Check(Graphics.Draws.Count == 0, "ResetZDO removes renderer commands");
            Reset(); source = Solid(); native = Spawn(77, source, false); debug = Start(); Tick(debug, 0);
            var oldData = native.Data; ZDOMan.instance.Objects[oldData.m_uid] = new ZDO { m_uid = oldData.m_uid, Owner = 123 }; oldData.OwnerRevision++; Tick(debug, .1f);
            Check(Graphics.Draws.Count == 0, "ZDO identity reuse skipped without iterator corruption");
            native.Data = new ZDO { m_uid = new ZDOID { UserID = 444, ID = 800 }, Owner = 88 }; ZDOMan.instance.Objects[native.Data.m_uid] = native.Data;
            ZNetScene.instance.AddInstance(native.Data, native); Tick(debug, .2f); Colour(88); native.OnDestroy(); Tick(debug, .3f); Check(Graphics.Draws.Count == 0, "Pooled-view replacement/destroy cleaned");
            Reset(); source = Solid(); native = Spawn(77, source, false); debug = Start(); Tick(debug, 0); ZNet.instance.Shutdown();
            Check(debug.Legend.Count == 0 && Camera.main.Buffers.Count == 0 && native.Data.Owner == 77, "Shutdown detaches without ownership change");
            ZNetScene.instance = new GameObject().Add<ZNetScene>(); ZDOMan.instance = new ZDOMan(); Tick(debug, .1f); Check(Graphics.Draws.Count == 0, "World change clears old tracking");
            Spawn(66, source, false); Tick(debug, .2f); Colour(66);

            Reset(); source = Solid(); var wall = Spawn(501, source, false);
            var interactive = Spawn(502, Solid(), false); interactive.gameObject.Add<TestInteractable>();
            var chestView = Spawn(503, Solid(), false); chestView.gameObject.Add<Container>();
            var npcA = Spawn(504, Solid(), true); var npcB = Spawn(505, Solid(), true);
            var excludedPlayer = Spawn(506, Solid(), true); excludedPlayer.gameObject.Add<Player>();
            var rosterOnly = new ZNet.PlayerInfo { m_name = "Online", m_characterID = new ZDOID { UserID = 507, ID = 70 } }; ZNet.instance.Players.Add(rosterOnly);
            debug = Start(); Tick(debug, 0); Check(Graphics.Draws.Count == 5 && debug.TrackedCount == 5, "All mode preserves static, interactive and both NPC renderers; player excluded");
            var bufferBeforeMode = Camera.main.Buffers.Single(); Tick(debug, .1f, true);
            Check(Graphics.Draws.Count == 4 && debug.PaintedCount == 4 && debug.TrackedCount == 4, "Filtered mode draws only interactive structures and all NPC mobs");
            Check(Graphics.Draws.All(d => d.Renderer != wall.GetComponent<Renderer>()) && Graphics.Draws.Any(d => d.Renderer == npcA.GetComponent<Renderer>()) && Graphics.Draws.Any(d => d.Renderer == npcB.GetComponent<Renderer>()), "Noninteractive piece is hidden, mobs are not filtered by tame status/species");
            Check(debug.ModeLabel == "Mobs + interactive objects" && Camera.main.Buffers.Single() == bufferBeforeMode, "Mode shown without recreating hooks/camera buffer");
            Check(!debug.Legend.Any(p => p.Key.Uid == 501) && debug.Legend.Any(p => p.Key.Uid == 507), "Hidden static-only owner removed from filtered legend; all online players retained");
            Tick(debug, .15f, false, true);
            Check(Graphics.Draws.Count == 2 && debug.PaintedCount == 2 && debug.TrackedCount == 2 && Graphics.Draws.All(d => d.Renderer == npcA.GetComponent<Renderer>() || d.Renderer == npcB.GetComponent<Renderer>()), "Mobs-only hides every static and interactive structure while retaining both NPCs");
            Check(debug.ModeLabel == "Mobs only" && Camera.main.Buffers.Single() == bufferBeforeMode, "Mobs mode displayed without replacing camera buffer");
            Check(!debug.Legend.Any(p => p.Key.Uid == 501 || p.Key.Uid == 502 || p.Key.Uid == 503) && debug.Legend.Any(p => p.Key.Uid == 507), "Mobs-only legend omits building-only owners and retains online players");
            npcA.Data.SetOwnerInternal(518); Tick(debug, .16f, true, true); Colour(518);
            Check(Graphics.Draws.Count == 2, "Mob ownership stays live and MobsOnly takes precedence when both filters are set");
            npcB.ResetZDO(); Tick(debug, .17f, false, true);
            Check(Graphics.Draws.Count == 1 && debug.TrackedCount == 1, "Mob registry removes unloaded mob");
            npcB = Spawn(505, Solid(), true); Tick(debug, .18f, false, true);
            Check(Graphics.Draws.Count == 2 && debug.TrackedCount == 2, "Newly loaded mob appears in active MobsOnly without toggling");
            wall.Data.SetOwnerInternal(508); interactive.Data.SetOwnerInternal(509); Tick(debug, .2f, true); Colour(509);
            Check(Graphics.Draws.Count == 4, "Live ownership updates retain active filter");
            Tick(debug, .3f); Colour(508); Check(Graphics.Draws.Count == 5 && debug.ModeLabel == "All objects and mobs", "All mode restores static renderer with latest owner");
            Tick(debug, .4f, true); chestView.ResetZDO(); Tick(debug, .5f, true);
            Check(Graphics.Draws.Count == 3 && debug.TrackedCount == 3, "Filtered registry cleans unloaded interactive renderer");
            debug.Dispose(); Plugin.Instance.View = null; Check(Camera.main.Buffers.Count == 0 && wall.Data.Owner == 508, "Off clears all registries without touching ownership");

            Reset(); var plainInteractive = Spawn(510, Solid(), false); plainInteractive.gameObject.Components.Remove(plainInteractive.GetComponent<Piece>()); plainInteractive.gameObject.Add<TestInteractable>();
            debug = Start(); Tick(debug, 0, true); Check(Graphics.Draws.Count == 1, "Persistent pure Interactable root supported without Piece/WearNTear");
            Reset(); wall = Spawn(511, Solid(), false); var control = new GameObject(); wall.gameObject.Child(control); control.Add<TestInteractable>();
            debug = Start(); Tick(debug, 0, true); Check(Graphics.Draws.Count == 1, "Interaction on owned subobject makes its network root interactive");
            Reset(); wall = Spawn(512, Solid(), false); var independent = Spawn(513, Solid(), false); independent.gameObject.Add<TestInteractable>(); wall.gameObject.Child(independent.gameObject);
            debug = Start(); Tick(debug, 0, true); Check(Graphics.Draws.Count == 1 && Graphics.Draws.Single().Renderer == independent.GetComponent<Renderer>(), "Independent child interaction cannot make static parent interactive");
            Reset(); wall = Spawn(514, Solid(), false); independent = Spawn(515, Solid(), false); wall.gameObject.Child(independent.gameObject); independent.gameObject.Add<Container>().m_rootObjectOverride = wall;
            debug = Start(); Tick(debug, 0, true); Check(Graphics.Draws.Count == 1 && Graphics.Draws.Single().Renderer == wall.GetComponent<Renderer>(), "Container root ownership override follows Authority interaction criterion");

            Reset(); var staticBulk = Enumerable.Range(0, 1000).Select(i => Spawn(516, Solid(), false)).ToArray();
            interactive = Spawn(517, Solid(), false); interactive.gameObject.Add<TestInteractable>(); debug = Start(); Tick(debug, 0);
            scans = Metrics.Scans; matReads = Metrics.MaterialReads; reads = Metrics.OwnerReads; Tick(debug, .1f, true);
            Check(Graphics.Draws.Count == 1 && debug.TrackedCount == 1 && Metrics.Scans == scans && Metrics.MaterialReads == matReads && Metrics.OwnerReads == reads, "Filter switch uses cached 1-renderer registry for 1000 static parts; zero repeated detection/model/material/owner work");
            Tick(debug, .2f, true); Tick(debug, 1.2f, true);
            Check(Graphics.Draws.Count == 1 && Metrics.Scans == scans && Metrics.MaterialReads == matReads && Metrics.OwnerReads == reads, "Filtered stable frames do not inspect static hierarchies or owners");
            Tick(debug, 1.25f, false, true);
            Check(Graphics.Draws.Count == 0 && debug.TrackedCount == 0 && Metrics.Scans == scans && Metrics.MaterialReads == matReads && Metrics.OwnerReads == reads, "Mobs-only skips 1000 static parts and the interactive part through its cached empty registry");
            Tick(debug, 1.26f, false, true);
            Check(Graphics.Draws.Count == 0 && Metrics.Scans == scans && Metrics.MaterialReads == matReads && Metrics.OwnerReads == reads, "Stable empty MobsOnly does not inspect building hierarchies/materials/owners");
            Tick(debug, 1.3f); Check(Graphics.Draws.Count == 1001 && staticBulk.All(v => v.GetComponent<Renderer>().enabled), "Returning to all restores cached native renderers without enabling or mutating originals");
            Reset(); var many = Enumerable.Range(0, 1024).Select(i => Spawn(77, Solid(), false)).ToArray(); debug = Start();
            int beforeMats = Metrics.Materials; copyCount = Metrics.Copies; Tick(debug, 0);
            Check(Metrics.Materials == beforeMats + 1 && Metrics.Copies == copyCount && Graphics.Draws.Count == 1024, "1024 distinct sources share one paint; zero property copies");
            Check(Graphics.Draws.Select(d => d.Material).Distinct().Count() == 1, "Flat material count depends on owners/cull, not original sources");
            many[0].Data.SetOwnerInternal(88); Tick(debug, .1f); Colour(88); Colour(77);
            Check(Graphics.Draws.Select(d => d.Material).Distinct().Count() == 2, "Changing one owner keeps other shared colour");
            ZNet.instance.Players.Add(new ZNet.PlayerInfo { m_name = "Player A", m_characterID = new ZDOID { UserID = 77, ID = 10 } }); Tick(debug, 1.1f);
            Check(debug.Legend.Any(p => p.Key.Uid == 77 && p.Value.Contains("Player A")), "Legend maps real player UID");
            // An online player must appear before owning local buildings/mobs.
            scans = Metrics.Scans; reads = Metrics.OwnerReads; matReads = Metrics.MaterialReads; writes = Metrics.ColourWrites;
            ZNet.instance.Players.Add(new ZNet.PlayerInfo { m_name = "Neu dabei", m_characterID = new ZDOID { UserID = 123, ID = 20 } }); Tick(debug, 2.2f);
            Check(debug.Legend.Count(p => p.Key.Kind == OwnerKind.Player && p.Key.Uid == 123 && p.Value.Contains("Neu dabei")) == 1, "Joined player without any tracked objects is in legend");
            Check(!Graphics.Draws.Any(x => Equal(x.Colour, new Color(OwnerColour.Get(OwnerKey.Resolve(true, 123, 1), 1337).R, OwnerColour.Get(OwnerKey.Resolve(true, 123, 1), 1337).G, OwnerColour.Get(OwnerKey.Resolve(true, 123, 1), 1337).B, 1))), "Roster join does not recolour existing objects");
            Check(Metrics.Scans == scans && Metrics.OwnerReads == reads && Metrics.MaterialReads == matReads && Metrics.ColourWrites == writes, "Roster join adds no model/material/owner/paint work");
            ZNet.instance.Players.Add(new ZNet.PlayerInfo { m_name = "Joining" }); Tick(debug, 3.3f);
            Check(debug.Legend.Count(p => p.Key.Kind == OwnerKind.Unknown && p.Value == "Joining (owner ID loading)") == 1, "Missing character ID uses explicit pending row");
            Check(debug.Legend.Count(p => p.Key.Kind == OwnerKind.Unowned) == 1, "Pending player is never treated as unowned");
            ZNet.instance.Players[2] = new ZNet.PlayerInfo { m_name = "Joining", m_characterID = new ZDOID { UserID = -234, ID = 30 } }; Tick(debug, 4.4f);
            Check(!debug.Legend.Any(p => p.Value.Contains("owner ID loading")) && debug.Legend.Any(p => p.Key.Uid == -234 && p.Value.Contains("Joining")), "Replicated character ID replaces pending row with actual signed owner ID");
            ZNet.instance.Players[1] = new ZNet.PlayerInfo { m_name = "Player A", m_characterID = new ZDOID { UserID = 123, ID = 20 } }; Tick(debug, 5.5f);
            Check(debug.Legend.Count(p => p.Key.Kind == OwnerKind.Player && p.Value.StartsWith("Player A")) == 2, "Same player names keep distinct session IDs");
            ZNet.instance.Players.Clear(); Tick(debug, 6.6f);
            Check(!debug.Legend.Any(p => p.Key.Uid == 123 || p.Key.Uid == -234), "Departed players without tracked objects leave legend");
            Check(debug.Legend.Any(p => p.Key.Uid == 77 && p.Value.StartsWith("Owner")), "Departed owner of still tracked objects remains labelled without stale player name");
            many[0].Data.SetOwnerInternal(123); Tick(debug, 6.7f); Colour(123); Tick(debug, 7.8f);
            Check(debug.Legend.Count(p => p.Key.Uid == 123) == 1, "Owner arriving after roster update is not duplicated");
            ZNet.instance.Players.Add(new ZNet.PlayerInfo { m_name = "Zero ID", m_characterID = new ZDOID { ID = 50 } }); Tick(debug, 8.9f);
            Check(debug.Legend.Any(p => p.Key.Kind == OwnerKind.Unknown && p.Value == "Zero ID (owner ID loading)"), "Invalid zero user ID never gets unowned colour");
            var foreign = new Harmony("test.foreign.guard"); var method = typeof(ZDO).GetMethod("SetOwnerInternal");
            foreign.Patch(method, postfix: new HarmonyMethod(typeof(DebugTests).GetMethod("ForeignPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
            debug.Dispose(); Plugin.Instance.View = null;
            Check(!Harmony.GetPatchInfo(method).Owners.Contains(Plugin.Id) && Harmony.GetPatchInfo(method).Owners.Contains("test.foreign.guard"), "Unpatch only own hooks");
            int prior = foreignCalls; many[0].Data.SetOwnerInternal(22);
            Check(many[0].Data.Owner == 22 && foreignCalls == prior + 1, "Vanilla/foreign owner hooks preserved"); foreign.UnpatchSelf();
            Check(many.All(v => v.GetComponent<Renderer>().SourceMaterials.All(m => m.Alive)), "Every original source survives debug lifetime");
            Reset(); source = Solid(); Spawn(77, source, false); debug = Start(); Shader.MissingColour = true; bool unsupported = false;
            try { Tick(debug, 0); } catch (NotSupportedException) { unsupported = true; }
            Check(unsupported, "Missing shader refuses invalid fallback"); debug.Dispose(); Plugin.Instance.View = null;
            Reset(); source = Solid(); Spawn(77, source, false); debug = Start(); ZNet.instance.Dedicated = true; scans = Metrics.Scans; beforeMats = Metrics.Materials; Tick(debug, 0);
            Check(Graphics.Draws.Count == 0 && Metrics.Scans == scans && Metrics.Materials == beforeMats, "Dedicated server no scanning/drawing/materials");
            debug.Dispose(); Plugin.Instance.View = null;
            Check(UnityEngine.Object.Destroyed.All(o => o is Material && o.name == "BotanicOwnerDebugPaint"), "Only owned paint materials ever destroyed");
            Check(Metrics.Assignments == 0 && Metrics.Copies == 0 && Metrics.Bakes == 0, "Whole suite never writes renderer bindings, clones sources or bakes skins");
            System.Console.WriteLine("PASS: " + checks + " flat-renderer ownership/lifecycle assertions.");
        }
        catch (Exception e) { System.Console.Error.WriteLine(e); Environment.Exit(1); }
    }
}


