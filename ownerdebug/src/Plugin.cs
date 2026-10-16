using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

[assembly: AssemblyVersion("1.0.10.0")]
namespace BotanicOwnerDebug
{
    [BepInPlugin(Id, "BotanicOwnerDebug", "1.0.10")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "botanic.owner.debug";
        internal static Plugin Instance;
        internal DebugView View;
        private ConfigEntry<bool> show, interactiveOnly, mobsOnly, legend, logs;
        private ConfigEntry<KeyboardShortcut> hotkey;
        private ConfigEntry<int> seed;
        private ConfigEntry<float> opacity, distance;
        private bool rejected;
        private ZNet colourNetwork;
        private void Awake()
        {
            Instance = this;
            show = Config.Bind("Debug", "ShowOwnerColours", false, "Local flat owner overlay for buildings, dropped items and mobs; players and effects excluded. F10 cycles Off -> Mobs only -> Mobs and interactive objects -> All -> Off. No server/client ownership changes.");
            interactiveOnly = Config.Bind("Debug", "InteractiveObjectsOnly", false, "When ShowOwnerColours is enabled, hide non-interactive buildings. Dropped items and all NPC mobs remain visible. F10 cycles this setting. Independent of server ownership mods and their categories/config; players and effects remain excluded.");
            mobsOnly = Config.Bind("Debug", "MobsOnly", false, "When ShowOwnerColours is enabled, show only NPC mobs (wild and tamed); players remain excluded. Takes precedence over InteractiveObjectsOnly. F10 cycles this setting.");
            hotkey = Config.Bind("Debug", "ToggleKey", new KeyboardShortcut(KeyCode.F10), "Cycle Off -> Mobs only -> Mobs and interactive objects -> All -> Off while playing. No toggle while typing or a game menu is open.");
            legend = Config.Bind("Debug", "ShowLegend", true, "Show owner colours and names. White=server, dark grey=unowned, saturated colours=players, blue-grey=unknown.");
            seed = Config.Bind("Debug", "ColourSeed", 1337, "Rotates the fixed six-colour palette; six extra variants support up to twelve owners. Colours stay assigned for this connection; assignment order may differ between clients.");
            opacity = Config.Bind("Debug", "ColourStrength", 1f, new ConfigDescription("Owner colour strength, default full colour. Original materials stay unchanged. Old Opacity setting is no longer used.", new AcceptableValueRange<float>(.15f, 1f)));
            distance = Config.Bind("Debug", "MaximumDistance", 200f, new ConfigDescription("Overlay range from camera; native renderer visibility checked every frame.", new AcceptableValueRange<float>(10f, 1000f)));
            logs = Config.Bind("Debug", "EnableLogs", false, "Log optional debug errors. Default false.");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) enabled = false;
        }
        private void Update()
        {
            if (!ReferenceEquals(colourNetwork, ZNet.instance)) { Stop(); OwnerColour.Reset(); colourNetwork = ZNet.instance; }
            if (ZNet.instance && ZNet.instance.IsDedicated()) { Stop(); return; }
            if (Player.m_localPlayer && hotkey.Value.IsDown() && !InputBlocked())
            {
                DisplayMode next = DisplayModes.Next(DisplayModes.Current(show.Value, interactiveOnly.Value, mobsOnly.Value));
                interactiveOnly.Value = next == DisplayMode.InteractiveAndMobs;
                mobsOnly.Value = next == DisplayMode.MobsOnly;
                show.Value = next != DisplayMode.Off; rejected = false;
            }
            if (!show.Value || !Player.m_localPlayer || !ZNetScene.instance || ZDOMan.instance == null) { Stop(); if (!show.Value) rejected = false; return; }
            if (rejected || View != null) return;
            try
            {
                View = new DebugView();
            }
            catch (Exception error) { Error(error); }
        }
        private static bool InputBlocked()
        { return (Chat.instance && Chat.instance.HasFocus()) || Console.IsVisible() || (TextInput.instance && TextInput.IsVisible()) || Menu.IsVisible() || InventoryGui.IsVisible(); }
        private void LateUpdate()
        {
            if (View == null) return;
            try { View.Draw(seed.Value, opacity.Value, distance.Value, interactiveOnly.Value, mobsOnly.Value); }
            catch (Exception error) { Error(error); }
        }
        private void OnGUI()
        {
            if (View == null || !legend.Value) return;
            Color previous = GUI.color;
            try
            {
                var rows = View.Legend;
                GUI.Box(new Rect(12, 50, 370, 111 + rows.Count * 22), "BotanicOwnerDebug 1.0.10 - " + hotkey.Value);
                GUI.Label(new Rect(22, 78, 350, 22), "Mode: " + View.ModeLabel);
                int y = 100;
                foreach (var row in rows)
                {
                    Rgb rgb = OwnerColour.Get(row.Key, seed.Value); GUI.color = new Color(rgb.R, rgb.G, rgb.B, 1);
                    GUI.Label(new Rect(23, y, 22, 22), "■"); GUI.color = previous;
                    GUI.Label(new Rect(49, y, 320, 22), row.Value); y += 22;
                }
                GUI.Label(new Rect(22, y + 4, 350, 24), View.Note);
                GUI.Label(new Rect(22, y + 25, 350, 24), "Drawn: " + View.PaintedCount + " / tracked: " + View.TrackedCount);
            }
            finally { GUI.color = previous; }
        }
        private void Error(Exception error)
        { rejected = true; Stop(); if (logs.Value) Logger.LogWarning("Owner debug disabled: " + error.Message); }
        private void Stop()
        {
            DebugView old = View; View = null;
            if (old != null) old.Dispose();
        }
        private void OnDestroy() { Stop(); if (Instance == this) Instance = null; }
        private void OnDisable() { Stop(); }
    }
}

