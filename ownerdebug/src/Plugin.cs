using System;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyVersion("1.0.12.0")]
namespace BotanicOwnerDebug
{
    [BepInPlugin(Id, "BotanicOwnerDebug", "1.0.12")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "botanic.owner.debug";
        internal static Plugin Instance;
        internal DebugView View;
        private ConfigEntry<bool>[] categories;
        private ConfigEntry<bool> legend, logs, test;
        private ConfigEntry<KeyboardShortcut> hotkey;
        private ConfigEntry<int> seed;
        private ConfigEntry<float> opacity, distance;
        private bool rejected, menuOpen;
        internal static int ClosedOnFrame = -1;
        private ZNet colourNetwork;
        private Harmony menuHooks;
        private Rect window = new Rect(100, 100, 470, 440);
        private string radius;
        private static readonly string[] labels = { "Truly interactive", "Static: no destruction physics", "Static: destruction physics", "Tamed mobs", "Hostile / wild mobs", "Loose items" };
        private static readonly string[] keys = { "Interactive", "Static", "DestructionPhysics", "TamedMobs", "HostileMobs", "LooseItems" };
        internal static bool MenuOpen { get { return Instance && Instance.menuOpen; } }
        private int Mask
        {
            get { int mask = 0; for (int i = 0; i < categories.Length; i++) if (categories[i].Value) mask |= 1 << i; return mask; }
        }
        private void Awake()
        {
            Instance = this;
            categories = new ConfigEntry<bool>[keys.Length];
            for (int i = 0; i < keys.Length; i++) categories[i] = Config.Bind("Categories", keys[i], false, labels[i] + ": local owner overlay. Independent category switch.");
            test = Config.Bind("Debug", "TmlMobTestPhases", false, "Only ai_test mobs: old/new owner use normal assigned owner colours; red=stretched fake playback, orange=final smoothing. Requires compatible TooManyLags debug build.");
            hotkey = Config.Bind("Debug", "ToggleKey", new KeyboardShortcut(KeyCode.F10), "Open/close settings menu; does not enable colouring. Escape closes menu.");
            legend = Config.Bind("Debug", "ShowLegend", true, "Show owner or test-phase colour legend.");
            seed = Config.Bind("Debug", "ColourSeed", 1337, "Mixes the connection-local player shuffle. First four players randomly receive purple, green, blue and yellow without repetition; assignments stay stable for the connection. No effect on red/orange test phases.");
            opacity = Config.Bind("Debug", "ColourStrength", 1f, new ConfigDescription("Owner colour strength. Native materials unchanged.", new AcceptableValueRange<float>(.15f, 1f)));
            distance = Config.Bind("Debug", "MaximumDistance", 200f, new ConfigDescription("Radius from camera in metres; changes apply immediately.", new AcceptableValueRange<float>(10f, 1000f)));
            logs = Config.Bind("Debug", "EnableLogs", false, "Log optional debug errors.");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { enabled = false; return; }
            menuHooks = new Harmony(Id + ".menu");
            menuHooks.CreateClassProcessor(typeof(MenuInputHooks)).Patch();
            menuHooks.CreateClassProcessor(typeof(MenuControllerHooks)).Patch();
            menuHooks.CreateClassProcessor(typeof(MenuCursorHooks)).Patch();
            menuHooks.CreateClassProcessor(typeof(MenuScrollHooks)).Patch();
            menuHooks.CreateClassProcessor(typeof(MenuEscapeHooks)).Patch();
        }
        private void BindTml()
        {
            PluginInfo info;
            TmlDebug.Bind(Chainloader.PluginInfos.TryGetValue("botanic.toomanylags.fgnaddon", out info) && info.Instance ? info.Instance.GetType().Assembly : null);
        }
        private void Update()
        {
            if (!ReferenceEquals(colourNetwork, ZNet.instance)) { Stop(); OwnerColour.Reset(); colourNetwork = ZNet.instance; BindTml(); menuOpen = false; }
            if (!Player.m_localPlayer || (ZNet.instance && ZNet.instance.IsDedicated())) { menuOpen = false; Stop(); return; }
            if (hotkey.Value.IsDown() && (menuOpen || !InputBlocked()))
            { menuOpen = !menuOpen; if (menuOpen) { BindTml(); radius = distance.Value.ToString("0.#", CultureInfo.InvariantCulture); } }
            if (menuOpen && Input.GetKeyDown(KeyCode.Escape)) { menuOpen = false; ClosedOnFrame = Time.frameCount; }
            if (menuOpen && InputBlocked()) menuOpen = false;
            bool testActive = test.Value && TmlDebug.Supported;
            if ((Mask == 0 && !testActive) || !ZNetScene.instance || ZDOMan.instance == null) { Stop(); rejected = false; return; }
            if (rejected || View != null) return;
            try { View = new DebugView(); }
            catch (Exception error) { Error(error); }
        }
        private static bool InputBlocked()
        { return (Chat.instance && Chat.instance.HasFocus()) || Console.IsVisible() || (TextInput.instance && TextInput.IsVisible()) || Menu.IsVisible() || InventoryGui.IsVisible(); }
        internal void Render(Camera camera)
        {
            if (View == null) return;
            try { View.Draw(seed.Value, opacity.Value, distance.Value, false, false, camera, Mask, test.Value && TmlDebug.Supported); }
            catch (Exception error) { Error(error); }
        }
        private void OnGUI()
        {
            if (menuOpen)
            {
                window.width = Mathf.Min(470, Screen.width);
                window.height = Mathf.Min(TmlDebug.Installed ? 465 : 410, Screen.height);
                window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, Screen.width - window.width));
                window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, Screen.height - window.height));
                window = GUI.Window(731029, window, Settings, "OwnerDebug settings");
            }
            if (View == null || !legend.Value) return;
            Color previous = GUI.color;
            try
            {
                bool phases = test.Value && TmlDebug.Supported;
                var rows = View.Legend;
                GUI.Box(new Rect(12, 50, 370, (phases ? 150 : 85) + rows.Count * 22), "OwnerDebug - " + hotkey.Value + " settings");
                int y = 78;
                foreach (var row in rows) { LegendRow(row.Key, row.Value, y, previous); y += 22; }
                if (phases)
                {
                    GUI.Label(new Rect(22, y, 350, 22), "Old/new owner: assigned owner colour"); y += 22;
                    LegendRow(new OwnerKey { Kind = OwnerKind.Playback }, "Stretched fake playback", y, previous); y += 22;
                    LegendRow(new OwnerKey { Kind = OwnerKind.Correction }, "Final smoothing", y, previous); y += 22;
                }
                GUI.Label(new Rect(22, y + 4, 350, 24), "Drawn: " + View.PaintedCount + " | radius: " + distance.Value.ToString("0.#") + " m");
            }
            finally { GUI.color = previous; }
        }
        private void LegendRow(OwnerKey key, string label, int y, Color previous)
        {
            Rgb rgb = OwnerColour.Get(key, seed.Value); GUI.color = new Color(rgb.R, rgb.G, rgb.B, 1);
            GUI.Label(new Rect(23, y, 22, 22), "■"); GUI.color = previous; GUI.Label(new Rect(49, y, 320, 22), label);
        }
        private void Settings(int id)
        {
            int y = 32;
            for (int i = 0; i < categories.Length; i++, y += 31)
            {
                bool next = GUI.Toggle(new Rect(20, y, 420, 26), categories[i].Value, labels[i]);
                if (next != categories[i].Value) { categories[i].Value = next; rejected = false; }
            }
            GUI.Label(new Rect(20, y, 150, 24), "Radius (metres)");
            string edited = GUI.TextField(new Rect(180, y, 90, 24), radius ?? "200", 8);
            if (edited != radius)
            {
                radius = edited; float value;
                if (float.TryParse(radius.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value) && value >= 10 && value <= 1000) distance.Value = value;
            }
            y += 32;
            float slider = GUI.HorizontalSlider(new Rect(20, y, 420, 20), distance.Value, 10, 1000);
            if (Math.Abs(slider - distance.Value) > .01f) { distance.Value = slider; radius = slider.ToString("0.#", CultureInfo.InvariantCulture); }
            y += 27;
            if (TmlDebug.Installed)
            {
                bool previous = GUI.enabled; GUI.enabled = previous && TmlDebug.Supported;
                bool next = GUI.Toggle(new Rect(20, y, 420, 26), test.Value, "TML mob debug: ai_test phases only");
                if (next != test.Value) { test.Value = next; rejected = false; }
                GUI.enabled = previous; y += 28;
                if (!TmlDebug.Supported) GUI.Label(new Rect(20, y, 420, 24), "Update TooManyLags to enable phase diagnostics.");
                else GUI.Label(new Rect(20, y, 420, 24), "Overrides category filters while enabled.");
                y += 27;
            }
            if (GUI.Button(new Rect(20, y, 200, 28), "All categories off")) for (int i = 0; i < categories.Length; i++) categories[i].Value = false;
            if (GUI.Button(new Rect(240, y, 200, 28), "Close")) menuOpen = false;
            GUI.DragWindow(new Rect(0, 0, window.width, 28));
        }
        private void Error(Exception error)
        { rejected = true; Stop(); if (logs.Value) Logger.LogWarning("Owner debug disabled: " + error.Message); }
        private void Stop() { DebugView old = View; View = null; if (old != null) old.Dispose(); }
        private void OnDestroy()
        { Stop(); if (menuHooks != null) menuHooks.UnpatchSelf(); if (Instance == this) Instance = null; }
        private void OnDisable() { menuOpen = false; Stop(); }
    }
    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class MenuInputHooks
    {
        [HarmonyPostfix] private static void After(Player __instance, ref bool __result)
        { if (Plugin.MenuOpen && __instance == Player.m_localPlayer) __result = false; }
    }
    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    internal static class MenuCursorHooks
    {
        [HarmonyPostfix] private static void After()
        { if (Plugin.MenuOpen) { ZCursor.LockState = CursorLockMode.None; ZCursor.Show(); } }
    }
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class MenuControllerHooks
    {
        [HarmonyPostfix] private static void After(ref bool __result) { if (Plugin.MenuOpen) __result = false; }
    }
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class MenuEscapeHooks
    {
        [HarmonyPrefix] private static bool Before() { return !Plugin.MenuOpen && Plugin.ClosedOnFrame != Time.frameCount; }
    }
    [HarmonyPatch(typeof(ZInput), "GetMouseScrollWheel")]
    internal static class MenuScrollHooks
    {
        [HarmonyPostfix] private static void After(ref float __result) { if (Plugin.MenuOpen) __result = 0f; }
    }
}


